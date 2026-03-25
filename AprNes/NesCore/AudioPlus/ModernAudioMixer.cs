using System;
using System.Runtime.CompilerServices;

namespace AprNes
{
    // =========================================================================
    // ModernAudioMixer — 5 軌獨立超採樣立體聲混音器
    // =========================================================================
    // Modern 模式的核心混音器。與 Authentic 模式的差異：
    //   Authentic: 先混音再降頻（模擬真實 DAC 的非線性交互）
    //   Modern:    先降頻再混音（保留各聲道獨立性，可立體聲配置）
    //
    // 每聲道各有獨立的 OversamplingEngine (256-Tap FIR)，
    // 在 1.789MHz → 44.1kHz 降頻後，依立體聲 pan 配置混合為 L/R 輸出。
    //
    // 立體聲基礎配置（StereoWidth=100% 時）：
    //   Pulse1:   L=0.7, R=0.3  (偏左)  — 左右方波分離，營造空間感
    //   Pulse2:   L=0.3, R=0.7  (偏右)
    //   Triangle: L=0.5, R=0.5  (中央)  — 低頻穩定在中央
    //   Noise:    L=0.5, R=0.5  (中央)  — 打擊樂穩定在中央
    //   DMC:      L=0.5, R=0.5  (中央)  — 採樣音效穩定在中央
    //
    // StereoWidth 控制分離程度：
    //   0%:   全部 (0.5, 0.5)，等效 mono
    //   50%:  Pulse1 (0.6, 0.4)，適度分離
    //   100%: 使用上述基礎配置，最大分離
    //
    // 額外效果：Triangle 專用 Low-Shelf Biquad Bass Boost (150Hz)
    // =========================================================================
    class ModernAudioMixer
    {
        // ── 5 個獨立超採樣引擎 ──────────────────────────────────
        // 每聲道各一個 FIR 降頻器，分別從 1.789MHz 降至 44.1kHz
        OversamplingEngine[] engines = new OversamplingEngine[5];

        // Per-channel 降頻暫存緩衝（批次模式 ProcessFrame 使用）
        // 每 frame 約 735 samples (29780 cycles / 40.58)，MAX_SAMPLES 留餘量
        float[][] chBuf = new float[5][];
        const int MAX_SAMPLES = 800;

        // ── 立體聲 pan 表 (L, R) — 基礎配置（StereoWidth=100% 時）──
        // index: 0=Pulse1, 1=Pulse2, 2=Triangle, 3=Noise, 4=DMC
        static readonly float[,] basePan = {
            { 0.7f, 0.3f },  // Pulse1: 偏左
            { 0.3f, 0.7f },  // Pulse2: 偏右
            { 0.5f, 0.5f },  // Triangle: 中央
            { 0.5f, 0.5f },  // Noise: 中央
            { 0.5f, 0.5f },  // DMC: 中央
        };

        // 實際 pan 值（依 StereoWidth 從 basePan 線性插值計算）
        float[,] pan = new float[5, 2];

        // ── Triangle Bass Boost（Low-Shelf Biquad）──────────────
        // NES Triangle 聲道本身缺少低頻豐滿感（4-bit 步階三角波），
        // 使用 Low-Shelf Biquad 提升指定頻率以下的頻段。
        // BassBoostDb: 0-12 dB（0=passthrough）
        // BassBoostFreq: 80-300 Hz（中心頻率）
        double bq_b0, bq_b1, bq_b2, bq_a1, bq_a2; // Biquad 係數
        double bq_x1, bq_x2, bq_y1, bq_y2;         // Biquad 狀態（x=input, y=output history）
        int cachedBoostDb = -1;                       // 快取，避免重複計算
        int cachedBoostFreq = -1;

        // ── 歸一化係數 ─────────────────────────────────────────
        // NES APU 各聲道原始整數範圍：Pulse=0~15, Tri=0~15, Noise=0~15, DMC=0~127
        // 除以最大值歸一化至 [0, 1] 範圍，供 OversamplingEngine 處理
        static readonly float[] normScale = {
            1f / 15f,   // Pulse1
            1f / 15f,   // Pulse2
            1f / 15f,   // Triangle
            1f / 15f,   // Noise
            1f / 127f,  // DMC
        };

        // ─────────────────────────────────────────────────────────
        // 建構子 — 初始化 5 個 OversamplingEngine + 降頻暫存 + 預設參數
        // ─────────────────────────────────────────────────────────
        public ModernAudioMixer()
        {
            for (int i = 0; i < 5; i++)
            {
                engines[i] = new OversamplingEngine();
                chBuf[i] = new float[MAX_SAMPLES];
            }
            SetStereoWidth(50);
            SetBassBoost(0, 150);
        }

        // ── 設定 ────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────
        // SetStereoWidth — 設定立體聲分離寬度
        //
        // 參數:
        //   width: 0~100（0=mono 全中央, 100=最大分離）
        // ─────────────────────────────────────────────────────────
        public void SetStereoWidth(int width)
        {
            float w = Math.Max(0, Math.Min(100, width)) / 100f;
            for (int ch = 0; ch < 5; ch++)
            {
                // Lerp: mono(0.5,0.5) → basePan at width=100
                pan[ch, 0] = 0.5f + (basePan[ch, 0] - 0.5f) * w;
                pan[ch, 1] = 0.5f + (basePan[ch, 1] - 0.5f) * w;
            }
        }

        // ─────────────────────────────────────────────────────────
        // SetBassBoost — 設定 Triangle 聲道低音增強
        //
        // 參數:
        //   dB: 增強量 (0-12 dB, 0=passthrough)
        //   freq: Low-shelf 中心頻率 (80-300 Hz)
        //
        // Biquad 係數依 Audio EQ Cookbook 公式計算。
        // 僅影響 Triangle 聲道。
        // ─────────────────────────────────────────────────────────
        public void SetBassBoost(int dB, int freq)
        {
            dB = Math.Max(0, Math.Min(12, dB));
            freq = Math.Max(80, Math.Min(300, freq));
            if (dB == cachedBoostDb && freq == cachedBoostFreq) return;
            cachedBoostDb = dB;
            cachedBoostFreq = freq;

            if (dB == 0)
            {
                bq_b0 = 1.0; bq_b1 = 0; bq_b2 = 0;
                bq_a1 = 0; bq_a2 = 0;
                return;
            }

            double gainDb = dB;
            double Q = 0.707;
            double A = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * freq / 44100.0;
            double sinW0 = Math.Sin(w0);
            double cosW0 = Math.Cos(w0);
            double alpha = sinW0 / (2.0 * Q);
            double sqrtA2alpha = 2.0 * Math.Sqrt(A) * alpha;

            double a0 = (A + 1) + (A - 1) * cosW0 + sqrtA2alpha;
            bq_b0 = (A * ((A + 1) - (A - 1) * cosW0 + sqrtA2alpha)) / a0;
            bq_b1 = (2.0 * A * ((A - 1) - (A + 1) * cosW0)) / a0;
            bq_b2 = (A * ((A + 1) - (A - 1) * cosW0 - sqrtA2alpha)) / a0;
            bq_a1 = (-2.0 * ((A - 1) + (A + 1) * cosW0)) / a0;
            bq_a2 = ((A + 1) + (A - 1) * cosW0 - sqrtA2alpha) / a0;

            bq_x1 = bq_x2 = bq_y1 = bq_y2 = 0;
        }

        // ── 每 APU cycle 呼叫 ───────────────────────────────────

        // ─────────────────────────────────────────────────────────
        // PushChannels — 推入 5 聲道的原始整數值
        //
        // 每 APU cycle（= CPU cycle）由 AudioDispatcher.PushApuCycle() 呼叫。
        // 各聲道值乘以歸一化係數後推入各自的 OversamplingEngine ring buffer。
        //
        // 參數:
        //   sq1, sq2: Pulse 1/2 輸出 (0-15)
        //   tri:      Triangle 輸出 (0-15)
        //   noise:    Noise 輸出 (0-15)
        //   dmc:      DMC 輸出 (0-127)
        // ─────────────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushChannels(int sq1, int sq2, int tri, int noise, int dmc)
        {
            engines[0].PushSample(sq1 * normScale[0]);
            engines[1].PushSample(sq2 * normScale[1]);
            engines[2].PushSample(tri * normScale[2]);
            engines[3].PushSample(noise * normScale[3]);
            engines[4].PushSample(dmc * normScale[4]);
        }

        // ── Per-sample 模式（AudioDispatcher 即時回呼用）────────

        // ─────────────────────────────────────────────────────────
        // TryGetStereoSample — 嘗試產出一對立體聲樣本
        //
        // 在每次 PushChannels 後呼叫。當 5 個 OversamplingEngine 都累積
        // 夠 ~40.58 個輸入 clock 時（以 engine[0] 為基準），從各引擎取出
        // 降頻樣本，經 Triangle Bass Boost + 立體聲 pan + 空間效果混合。
        //
        // 參數:
        //   L, R: 輸出的左右聲道樣本值（float）
        //   fx: ModernAudioFX 實例（Haas + Reverb），可為 null（跳過空間效果）
        //
        // 回傳: true=有輸出（約每 40.58 次呼叫成功一次），false=尚未累積足夠
        // ─────────────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetStereoSample(out float L, out float R, ModernAudioFX fx)
        {
            L = R = 0f;

            // 以 engine[0] 為基準判斷是否 ready (所有 engine 同步 push)
            float s0;
            if (!engines[0].TryGetSample(out s0)) return false;

            float s1, s2, s3, s4;
            engines[1].TryGetSample(out s1);
            engines[2].TryGetSample(out s2);
            engines[3].TryGetSample(out s3);
            engines[4].TryGetSample(out s4);

            // Triangle bass boost (biquad)
            double triIn = s2;
            double triOut = bq_b0 * triIn + bq_b1 * bq_x1 + bq_b2 * bq_x2
                          - bq_a1 * bq_y1 - bq_a2 * bq_y2;
            bq_x2 = bq_x1; bq_x1 = triIn;
            bq_y2 = bq_y1; bq_y1 = triOut;

            // Stereo mix
            L += s0 * pan[0, 0]; R += s0 * pan[0, 1]; // Pulse1
            L += s1 * pan[1, 0]; R += s1 * pan[1, 1]; // Pulse2
            L += (float)triOut * pan[2, 0]; R += (float)triOut * pan[2, 1]; // Triangle
            L += s3 * pan[3, 0]; R += s3 * pan[3, 1]; // Noise
            L += s4 * pan[4, 0]; R += s4 * pan[4, 1]; // DMC

            // Apply spatial FX (Haas + Reverb) — single sample
            if (fx != null)
            {
                float[] tmp = { L, R };
                fx.Process(tmp, 1);
                L = tmp[0];
                R = tmp[1];
            }

            return true;
        }

        // ── 每 frame 呼叫（批次模式）────────────────────────────

        // ─────────────────────────────────────────────────────────
        // ProcessFrame — 批次降頻混音（每 frame 呼叫一次）
        //
        // 將所有累積的 1.789MHz 樣本一次降頻至 44.1kHz，
        // 然後混音為 stereo interleaved 格式 (L,R,L,R,...)。
        //
        // 流程:
        //   1. 5 個引擎各自 Decimate → chBuf[0..4]
        //   2. 逐 sample: Triangle Biquad → pan 混合 → 寫入 stereoOut
        //
        // 參數:
        //   stereoOut: L,R,L,R,... interleaved float 輸出陣列
        //   maxStereoSamples: 最大 stereo sample 對數
        //
        // 回傳: 實際產出的 stereo sample 對數（通常 ~735 = 一幀）
        //
        // 注意：此方法不含 ModernAudioFX 處理，呼叫方需另外處理空間效果。
        // ─────────────────────────────────────────────────────────
        public int ProcessFrame(float[] stereoOut, int maxStereoSamples)
        {
            // 降頻各聲道
            int count = engines[0].Decimate(chBuf[0], MAX_SAMPLES);
            for (int ch = 1; ch < 5; ch++)
                engines[ch].Decimate(chBuf[ch], MAX_SAMPLES);

            int outCount = Math.Min(count, maxStereoSamples);

            for (int i = 0; i < outCount; i++)
            {
                // Triangle bass boost (biquad)
                double triSample = chBuf[2][i];
                double triOut = bq_b0 * triSample + bq_b1 * bq_x1 + bq_b2 * bq_x2
                              - bq_a1 * bq_y1 - bq_a2 * bq_y2;
                bq_x2 = bq_x1; bq_x1 = triSample;
                bq_y2 = bq_y1; bq_y1 = triOut;

                // Stereo mix
                float L = 0f, R = 0f;
                L += chBuf[0][i] * pan[0, 0]; R += chBuf[0][i] * pan[0, 1]; // Pulse1
                L += chBuf[1][i] * pan[1, 0]; R += chBuf[1][i] * pan[1, 1]; // Pulse2
                L += (float)triOut * pan[2, 0]; R += (float)triOut * pan[2, 1]; // Triangle (boosted)
                L += chBuf[3][i] * pan[3, 0]; R += chBuf[3][i] * pan[3, 1]; // Noise
                L += chBuf[4][i] * pan[4, 0]; R += chBuf[4][i] * pan[4, 1]; // DMC

                stereoOut[i * 2]     = L;
                stereoOut[i * 2 + 1] = R;
            }

            return outCount;
        }

        // ─────────────────────────────────────────────────────────
        // Reset — 重置所有超採樣引擎和 Biquad 濾波器狀態
        //
        // 清除 5 個 OversamplingEngine 的 ring buffer + inputPhase，
        // 以及 Triangle Bass Boost Biquad 的歷史狀態。
        // 在 ROM 載入、Hard/Soft Reset 時呼叫。
        // ─────────────────────────────────────────────────────────
        public void Reset()
        {
            for (int i = 0; i < 5; i++)
                engines[i].Reset();
            bq_x1 = bq_x2 = bq_y1 = bq_y2 = 0;
        }
    }
}
