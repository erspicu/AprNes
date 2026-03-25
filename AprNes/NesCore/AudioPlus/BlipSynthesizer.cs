using System;
using System.Runtime.CompilerServices;

namespace AprNes
{
    // =========================================================================
    // BlipSynthesizer — 帶限步階合成器 (Blip Buffer)
    // =========================================================================
    // 消除方波/雜音聲道邊緣跳變產生的混疊失真 (aliasing / Gibbs 現象)。
    //
    // 原理：
    //   NES Pulse 和 Noise 聲道的波形是「離散跳變」（0↔15），
    //   直接取樣會因高頻成分超過 Nyquist 而產生混疊。
    //   解決方法：不記錄絕對電壓值，改為記錄電壓「變化量」(delta)。
    //   每次波形跳變時，注入一個 windowed sinc 脈衝（帶限步階函數），
    //   再透過積分器還原為絕對電壓值。
    //
    // 優點：
    //   - 只在波形跳變時計算（Pulse 每半週期一次，非每 cycle）
    //   - 步階表小（32×16 = 512 floats），cache 友好
    //   - 自然帶限，無需額外 anti-aliasing FIR
    //
    // 步階表：
    //   32 phases × 16 taps = 512 floats
    //   每個 phase 對應不同的 subsample 偏移（精度 1/32 sample）
    //   每個 tap 是累積 windowed sinc 值（積分後成為帶限步階）
    //
    // 目前狀態：已建構但尚未整合至任何管線。
    // 計劃用途：Modern 模式中替代 Pulse/Noise 的 OversamplingEngine。
    // =========================================================================
    class BlipSynthesizer
    {
        // ── 常數 ────────────────────────────────────────────────
        const int PHASES = 32;          // subsample 分相精度（32 = 1/32 sample 解析度）
        const int TAPS = 16;            // 步階函數長度（每次 delta 影響 16 個 output samples）
        const int HALF_TAPS = TAPS / 2; // 步階中心偏移
        const int BUF_SIZE = 2048;      // output buffer 大小（2^11，足夠數幀）
        const int BUF_MASK = BUF_SIZE - 1;

        const double CPU_FREQ = 1789773.0;  // NTSC CPU clock (Hz)
        const int SAMPLE_RATE = 44100;       // 輸出取樣率 (Hz)
        const double CLOCKS_PER_SAMPLE = CPU_FREQ / SAMPLE_RATE; // ~40.584

        // ── 靜態步階表（所有實例共用，static constructor 預計算）──
        // stepTable[phase, tap]: 累積 windowed sinc，表示帶限步階函數
        // 「累積」是關鍵：普通 sinc 是脈衝響應，累積後成為步階響應
        static readonly float[,] stepTable;

        // ─────────────────────────────────────────────────────────
        // 靜態建構子 — 預計算 32×16 帶限步階表
        //
        // 對每個 phase p (0..31):
        //   fraction = p / 32 （subsample 偏移量）
        //   對每個 tap i (0..15):
        //     x = (i - 8) - fraction （距步階中心的偏移）
        //     sinc(x) = sin(πx) / (πx)  （理想帶限脈衝）
        //     blackman(x) = 0.42 - 0.5cos(...) + 0.08cos(...)  （窗函數）
        //     sum += sinc × blackman  （累積 → 步階）
        //     stepTable[p, i] = sum
        //   正規化使最終累積值 = 1.0（完整步階跳變 = 1.0）
        // ─────────────────────────────────────────────────────────
        static BlipSynthesizer()
        {
            stepTable = new float[PHASES, TAPS];

            for (int p = 0; p < PHASES; p++)
            {
                double phase = (double)p / PHASES;
                double sum = 0.0;

                for (int i = 0; i < TAPS; i++)
                {
                    double x = (i - HALF_TAPS) - phase;

                    // Sinc function（理想低通濾波器的脈衝響應）
                    double sinc;
                    if (Math.Abs(x) < 1e-9)
                        sinc = 1.0;
                    else
                        sinc = Math.Sin(Math.PI * x) / (Math.PI * x);

                    // Blackman window（減少截斷漣漪，旁瓣衰減 > 58dB）
                    double wPhase = (x + HALF_TAPS) / TAPS;
                    double window = 0.42
                                  - 0.50 * Math.Cos(2.0 * Math.PI * wPhase)
                                  + 0.08 * Math.Cos(4.0 * Math.PI * wPhase);

                    // 累積（不是直接存值）→ 脈衝響應變步階響應
                    sum += sinc * window;
                    stepTable[p, i] = (float)sum;
                }

                // 正規化：確保完整步階 = 1.0
                if (Math.Abs(sum) > 1e-12)
                {
                    float invSum = (float)(1.0 / sum);
                    for (int i = 0; i < TAPS; i++)
                        stepTable[p, i] *= invSum;
                }
            }
        }

        // ── 實例狀態 ────────────────────────────────────────────
        float[] buffer = new float[BUF_SIZE]; // 累積 delta 的環形緩衝區
        int readPos = 0;                       // 下一個要讀取的 output sample 位置
        int writeBase = 0;                     // 目前 delta 寫入的基準位置
        double clockAccum = 0.0;               // 累計 APU clock 數（追蹤 output sample 時間軸）
        float lastAmplitude = 0f;              // 上一次振幅（用於計算 delta）
        double integrator = 0.0;               // leaky 積分器狀態（delta → 絕對電壓）
        const double INTEGRATOR_LEAK = 0.99997; // 積分器衰減係數（防止 DC 漂移累積）

        // ── API ─────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────
        // AddDelta — 在目前 clock 位置注入一個振幅變化
        //
        // 應在每次波形跳變時呼叫（不是每個 cycle），例如：
        //   - Pulse 輸出從 0 跳到 12 → AddDelta(12/15.0)
        //   - Noise LFSR 位元翻轉 → AddDelta(newLevel/15.0)
        //
        // 內部流程:
        //   1. 計算 delta = newAmplitude - lastAmplitude
        //   2. 依 clockAccum 算出在 output buffer 中的精確位置和 subsample phase
        //   3. 查找 stepTable[phase] 取得 16 個 tap 係數
        //   4. 將 delta × tap 累加至 buffer 中連續 16 個位置
        //
        // 參數:
        //   newAmplitude: 新的歸一化振幅值（0.0~1.0）
        // ─────────────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddDelta(float newAmplitude)
        {
            float delta = newAmplitude - lastAmplitude;
            if (Math.Abs(delta) < 1e-8f)
                return;
            lastAmplitude = newAmplitude;

            // 計算在 output buffer 中的精確位置
            double exactPos = clockAccum / CLOCKS_PER_SAMPLE;
            int sampleIdx = (int)exactPos;
            double frac = exactPos - sampleIdx;

            int phaseIdx = (int)(frac * PHASES);
            if (phaseIdx >= PHASES) phaseIdx = PHASES - 1;

            // 注入帶限步階到 buffer
            int baseIdx = (writeBase + sampleIdx) & BUF_MASK;
            for (int i = 0; i < TAPS; i++)
                buffer[(baseIdx + i) & BUF_MASK] += delta * stepTable[phaseIdx, i];
        }

        // ─────────────────────────────────────────────────────────
        // ClockAdvance — 每 APU cycle 推進 clock 計數
        //
        // 每 CPU cycle（= APU cycle）呼叫一次，累計 clock 數。
        // 用於追蹤 AddDelta 注入位置和 TryGetSample 的輸出時機。
        // ─────────────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClockAdvance()
        {
            clockAccum += 1.0;
        }

        // ─────────────────────────────────────────────────────────
        // TryGetSample — 嘗試取得一個 44.1kHz 輸出樣本
        //
        // 在每次 ClockAdvance 後呼叫。當累積夠 ~40.58 個 clock 時：
        //   1. 從 buffer 讀取該位置累積的 delta 總和
        //   2. 清除已讀取的 buffer 位置（避免重複計算）
        //   3. 透過 leaky integrator 將 delta 轉為絕對電壓值：
        //      integrator = integrator × 0.99997 + delta
        //      衰減係數防止長時間運行的 DC 漂移累積
        //   4. 推進 readPos 和 writeBase
        //
        // 回傳: true=有輸出，false=尚未累積足夠 clock
        //
        // 參數:
        //   result: 輸出的 float 電壓值
        // ─────────────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetSample(out float result)
        {
            result = 0f;
            if (clockAccum < CLOCKS_PER_SAMPLE) return false;

            clockAccum -= CLOCKS_PER_SAMPLE;

            // 讀取累積的 delta，透過 leaky integrator 轉為絕對值
            float delta = buffer[readPos];
            buffer[readPos] = 0f; // 清除已讀取的 delta

            integrator = integrator * INTEGRATOR_LEAK + delta;
            result = (float)integrator;

            readPos = (readPos + 1) & BUF_MASK;
            writeBase = readPos;

            return true;
        }

        // ─────────────────────────────────────────────────────────
        // Reset — 清除所有內部狀態
        //
        // 歸零 buffer、指標、clock 累計、振幅記錄、積分器狀態。
        // 在 ROM 載入、Hard/Soft Reset 時呼叫。
        // ─────────────────────────────────────────────────────────
        public void Reset()
        {
            Array.Clear(buffer, 0, BUF_SIZE);
            readPos = 0;
            writeBase = 0;
            clockAccum = 0.0;
            lastAmplitude = 0f;
            integrator = 0.0;
        }
    }
}
