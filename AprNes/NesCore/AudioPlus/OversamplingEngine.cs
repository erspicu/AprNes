using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace AprNes
{
    // =========================================================================
    // OversamplingEngine — 原生取樣率超採樣引擎
    // =========================================================================
    // 將 APU 原生 1.789773 MHz 的樣本流，透過 polyphase FIR 降頻至 44.1 kHz。
    //
    // 原理：NES APU 以 CPU clock 速率產生樣本（比 CD 高 40.58 倍）。
    //       直接每 ~40.58 cycle 取一個 sample 會產生混疊失真 (aliasing)。
    //       正確做法是在原生速率緩衝所有樣本，再用 anti-aliasing FIR 降頻。
    //
    // 規格：
    //   - FIR: 256-Tap, Blackman-windowed Sinc, cutoff 20kHz
    //   - Polyphase: 128 phase（處理非整數降頻比的小數部分）
    //   - Ring buffer: 65536 floats（容納 ~2 frame）
    //   - SIMD: Vector<float> 加速卷積（AVX2 = 8 floats/op）
    //
    // 使用方式：
    //   Authentic 模式: 1 個實例（混音後的單一電壓流）
    //   Modern 模式:    5 個實例（每聲道各一個）
    // =========================================================================
    class OversamplingEngine
    {
        // ── 常數 ────────────────────────────────────────────────
        const int TAPS = 256;               // FIR 總 tap 數（決定濾波品質）
        const int PHASES = 128;             // polyphase 分相數（插值精度）
        const int TAPS_PER_PHASE = TAPS / PHASES;
        const int HALF_TAPS = TAPS / 2;     // sinc 中心偏移（對稱核心）
        const int BUF_SIZE = 65536;         // ring buffer 大小（2^16，用 mask 取代 %）
        const int BUF_MASK = BUF_SIZE - 1;

        const double CPU_FREQ = 1789773.0;  // NTSC CPU clock (Hz)
        const int SAMPLE_RATE = 44100;       // 輸出取樣率 (Hz)
        const double CLOCKS_PER_SAMPLE = CPU_FREQ / SAMPLE_RATE; // ~40.584

        // Anti-aliasing cutoff: 20kHz / CPU_FREQ ≈ 0.01117（歸一化頻率）
        const double CUTOFF_NORM = 20000.0 / CPU_FREQ;

        // ── 靜態 FIR 核心（所有實例共用，static constructor 預計算）───
        // kernel[phase][tap]: 128 個分相各有 256 個 tap 係數
        // 每個分相對應不同的小數延遲，用於非整數降頻比的精確插值
        static readonly float[][] kernel;

        // ─────────────────────────────────────────────────────────
        // 靜態建構子 — 預計算 Blackman-windowed Sinc FIR 核心
        //
        // 對每個 phase p (0..127):
        //   fraction = p / 128 （小數延遲）
        //   對每個 tap i (0..255):
        //     x = (i - 128) - fraction （距離中心的偏移）
        //     sinc(x) = sin(2π × cutoff × x) / (π × x)  （理想低通）
        //     blackman(x) = 0.42 - 0.5×cos(...) + 0.08×cos(...)  （窗函數）
        //     tap = sinc × blackman
        //   全部 tap 正規化使增益 = 1.0
        // ─────────────────────────────────────────────────────────
        static OversamplingEngine()
        {
            kernel = new float[PHASES][];
            for (int p = 0; p < PHASES; p++)
            {
                kernel[p] = new float[TAPS];
                double fraction = (double)p / PHASES;
                double sum = 0.0;

                for (int i = 0; i < TAPS; i++)
                {
                    double x = (i - HALF_TAPS) - fraction;

                    // Sinc function（理想低通濾波器的脈衝響應）
                    double sinc;
                    if (Math.Abs(x) < 1e-9)
                        sinc = 1.0;
                    else
                        sinc = Math.Sin(2.0 * Math.PI * CUTOFF_NORM * x) / (Math.PI * x);

                    // Blackman window（減少截斷漣漪，旁瓣衰減 > 58dB）
                    // 係數: a0=0.42, a1=0.5, a2=0.08
                    double wPhase = (x + HALF_TAPS) / TAPS;
                    double window = 0.42
                                  - 0.50 * Math.Cos(2.0 * Math.PI * wPhase)
                                  + 0.08 * Math.Cos(4.0 * Math.PI * wPhase);

                    double val = sinc * window;
                    kernel[p][i] = (float)val;
                    sum += val;
                }

                // 正規化：確保所有 tap 加總 = 1.0（unity gain）
                if (Math.Abs(sum) > 1e-12)
                {
                    float invSum = (float)(1.0 / sum);
                    for (int i = 0; i < TAPS; i++)
                        kernel[p][i] *= invSum;
                }
            }
        }

        // ── 實例狀態 ────────────────────────────────────────────
        float[] ringBuf = new float[BUF_SIZE]; // 輸入樣本環形緩衝區
        int writePos = 0;                       // 下一個寫入位置
        double inputPhase = 0.0;                // 累計未消耗的輸入 clock 數

        // ─────────────────────────────────────────────────────────
        // PushSample — 每 APU cycle 呼叫，將一個電壓樣本寫入 ring buffer
        // ─────────────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushSample(float voltage)
        {
            ringBuf[writePos] = voltage;
            writePos = (writePos + 1) & BUF_MASK;
            inputPhase += 1.0;
        }

        // ─────────────────────────────────────────────────────────
        // TryGetSample — 嘗試產出一個 44.1kHz 輸出樣本
        //
        // 在 PushSample 後呼叫。當累積夠 ~40.58 個輸入 clock 時，
        // 選擇對應的 polyphase 核心做 FIR 卷積，產出一個降頻樣本。
        // 回傳 true 表示有輸出，false 表示尚未累積足夠。
        // 適用於 per-sample callback 模式（AudioDispatcher 使用）。
        // ─────────────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetSample(out float result)
        {
            result = 0f;
            if (inputPhase < CLOCKS_PER_SAMPLE) return false;

            inputPhase -= CLOCKS_PER_SAMPLE;

            // 小數部分決定使用哪個 polyphase 核心
            int intPhase = (int)inputPhase;
            double frac = inputPhase - intPhase;

            int phaseIdx = (int)(frac * PHASES);
            if (phaseIdx >= PHASES) phaseIdx = PHASES - 1;

            // 卷積中心 = 最新寫入位置回退未消耗的 clock 數
            int center = (writePos - 1 - intPhase) & BUF_MASK;
            int startIdx = (center - HALF_TAPS) & BUF_MASK;

            result = Convolve(kernel[phaseIdx], startIdx);
            return true;
        }

        // ─────────────────────────────────────────────────────────
        // Decimate — 批次降頻模式（每 frame 呼叫）
        //
        // 將所有累積的輸入一次降頻至 44.1kHz，寫入 output 陣列。
        // 回傳實際產出的 sample 數（通常 ~735 = 一幀）。
        // 適用於 ModernAudioMixer.ProcessFrame() 批次處理。
        // ─────────────────────────────────────────────────────────
        public int Decimate(float[] output, int maxCount)
        {
            int produced = 0;

            while (inputPhase >= CLOCKS_PER_SAMPLE && produced < maxCount)
            {
                inputPhase -= CLOCKS_PER_SAMPLE;

                int intPhase = (int)inputPhase;
                double frac = inputPhase - intPhase;

                int phaseIdx = (int)(frac * PHASES);
                if (phaseIdx >= PHASES) phaseIdx = PHASES - 1;

                int center = (writePos - 1 - intPhase) & BUF_MASK;
                int startIdx = (center - HALF_TAPS) & BUF_MASK;

                float[] k = kernel[phaseIdx];
                float acc = Convolve(k, startIdx);

                output[produced++] = acc;
            }

            return produced;
        }

        // ─────────────────────────────────────────────────────────
        // Reset — 清除 ring buffer 和累計狀態
        // ─────────────────────────────────────────────────────────
        public void Reset()
        {
            Array.Clear(ringBuf, 0, BUF_SIZE);
            writePos = 0;
            inputPhase = 0.0;
        }

        // ─────────────────────────────────────────────────────────
        // Convolve — FIR 卷積核心（SIMD 加速）
        //
        // 從 ring buffer 的 startIdx 位置開始，與 FIR 核心 k[] 做內積。
        // 使用 System.Numerics.Vector<float> 進行 SIMD 並行計算，
        // AVX2 下一次處理 8 個 float，256 taps 只需 32 次迭代。
        // 因 ring buffer 可能跨越邊界，用 scalar gather 收集樣本。
        // ─────────────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        float Convolve(float[] k, int startIdx)
        {
            float acc = 0f;
            int vecLen = Vector<float>.Count;

            // SIMD 主迴圈
            int i = 0;
            for (; i + vecLen <= TAPS; i += vecLen)
            {
                // Scalar gather：從 ring buffer 收集樣本（處理邊界 wrap）
                float[] tmp = new float[vecLen];
                for (int j = 0; j < vecLen; j++)
                    tmp[j] = ringBuf[(startIdx + i + j) & BUF_MASK];

                Vector<float> vSample = new Vector<float>(tmp);
                Vector<float> vKernel = new Vector<float>(k, i);
                acc += Vector.Dot(vSample, vKernel);
            }

            // Scalar 尾端處理（TAPS 非 vecLen 整數倍時）
            for (; i < TAPS; i++)
                acc += ringBuf[(startIdx + i) & BUF_MASK] * k[i];

            return acc;
        }
    }
}
