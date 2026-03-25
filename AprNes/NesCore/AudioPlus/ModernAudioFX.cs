using System;
using System.Runtime.CompilerServices;

namespace AprNes
{
    // =========================================================================
    // ModernAudioFX — 空間效果處理器
    // =========================================================================
    // 為 Modern 模式提供立體聲空間感增強效果：
    //
    // 1. Haas Effect（優先效應）：
    //    人耳對左右耳到達時間差 < 40ms 的聲音會感知為「同一聲源但更寬」。
    //    右聲道延遲 10~30ms，並將延遲信號以 40% 音量 crossfeed 回左聲道，
    //    營造出音場寬度感，而非明顯的回音。
    //
    // 2. Micro-Room Reverb（微型房間殘響）：
    //    4 條平行 Comb Filter（延遲長度各異，模擬不同反射路徑），
    //    每條 comb 的 feedback 路徑含一階 LPF（模擬牆壁對高頻的吸收），
    //    最終以 wet/dry mix 混合回原信號。
    //
    // 處理順序: Reverb（mono sum）→ Haas（R 延遲 + crossfeed）
    // 在 Haas 之前施加 Reverb，使殘響也受到空間化處理。
    // =========================================================================
    class ModernAudioFX
    {
        const int SAMPLE_RATE = 44100;
        const int MAX_DELAY = (int)(SAMPLE_RATE * 0.035); // 35ms 最大延遲緩衝（1543 samples）

        // ── Haas Effect 狀態 ──────────────────────────────────
        float[] haasDelayBuf = new float[MAX_DELAY]; // 右聲道延遲環形緩衝區
        int haasWritePos = 0;                        // 緩衝區寫入指標
        int haasDelaySamples;                        // 實際延遲 sample 數（依 ms 設定計算）
        float haasCrossfeed = 0.4f;                  // 延遲 R 混入 L 的比例（0.0~0.8）

        // ── Comb Filter Reverb 狀態 ──────────────────────────
        // 4 條平行梳狀濾波器，延遲長度選用互質數避免共振
        //   1116 samples ≈ 25.3ms, 1188 ≈ 26.9ms,
        //   1277 ≈ 28.9ms, 1356 ≈ 30.7ms
        // 不同長度模擬房間內不同距離牆壁的反射路徑
        static readonly int[] combLengths = { 1116, 1188, 1277, 1356 };
        const int COMB_COUNT = 4;
        float[][] combBuf;          // 每條 comb 的延遲線 buffer
        int[] combPos;              // 每條 comb 的讀寫指標
        float[] combLpfState;       // 每條 comb feedback 路徑的 LPF 狀態
        float combFeedback = 0.7f;  // 回饋增益（0.3~0.9，越高殘響越長）
        float combDamp = 0.3f;      // feedback LPF 阻尼係數（0.1~0.7，越高越暗）

        float reverbWet = 0f;  // 殘響濕信號混合量（0~0.30）

        // ─────────────────────────────────────────────────────────
        // 建構子 — 配置 4 條 Comb Filter 延遲線，設定預設參數
        // ─────────────────────────────────────────────────────────
        public ModernAudioFX()
        {
            combBuf = new float[COMB_COUNT][];
            combPos = new int[COMB_COUNT];
            combLpfState = new float[COMB_COUNT];
            for (int i = 0; i < COMB_COUNT; i++)
            {
                combBuf[i] = new float[combLengths[i]];
                combPos[i] = 0;
                combLpfState[i] = 0f;
            }
            SetHaasDelay(20);
            SetHaasCrossfeed(40);
            SetReverbWet(0);
            SetCombFeedback(70);
            SetCombDamp(30);
        }

        // ── 設定 ────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────
        // SetHaasDelay — 設定 Haas Effect 的右聲道延遲時間
        //
        // 參數:
        //   ms: 延遲毫秒數（10~30ms 範圍 clamp）
        // ─────────────────────────────────────────────────────────
        public void SetHaasDelay(int ms)
        {
            ms = Math.Max(10, Math.Min(30, ms));
            haasDelaySamples = SAMPLE_RATE * ms / 1000;
        }

        // ─────────────────────────────────────────────────────────
        // SetHaasCrossfeed — 設定 Haas crossfeed 比例
        //
        // 參數:
        //   percent: 0-80 (映射至 0.0~0.8)
        //            延遲 R 混入 L 的比例，影響空間寬度感
        // ─────────────────────────────────────────────────────────
        public void SetHaasCrossfeed(int percent)
        {
            haasCrossfeed = Math.Max(0, Math.Min(80, percent)) / 100f;
        }

        // ─────────────────────────────────────────────────────────
        // SetReverbWet — 設定殘響濕度（連續值）
        //
        // 參數:
        //   percent: 0-30 (映射至 0.0~0.30)
        //            0=關閉, 10=Light, 15=Medium, 30=Heavy
        // ─────────────────────────────────────────────────────────
        public void SetReverbWet(int percent)
        {
            reverbWet = Math.Max(0, Math.Min(30, percent)) / 100f;
        }

        // ─────────────────────────────────────────────────────────
        // SetCombFeedback — 設定 Comb Filter 回饋增益
        //
        // 參數:
        //   percent: 30-90 (映射至 0.30~0.90)
        //            越高殘響越長，90% 接近無限殘響
        // ─────────────────────────────────────────────────────────
        public void SetCombFeedback(int percent)
        {
            combFeedback = Math.Max(30, Math.Min(90, percent)) / 100f;
        }

        // ─────────────────────────────────────────────────────────
        // SetCombDamp — 設定 Comb Filter 高頻阻尼
        //
        // 參數:
        //   percent: 10-70 (映射至 0.10~0.70)
        //            越高殘響越暗（模擬吸音材質牆壁）
        // ─────────────────────────────────────────────────────────
        public void SetCombDamp(int percent)
        {
            combDamp = Math.Max(10, Math.Min(70, percent)) / 100f;
        }

        // ── 處理 ────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────
        // Process — 就地處理 stereo interleaved 音訊緩衝區
        //
        // 流程（對每個 sample pair）：
        //   1. Reverb: L+R 取 mono → 4 條 Comb Filter 平行處理 →
        //      平均後乘以 wet 加回 L/R
        //   2. Haas: R 寫入延遲線 → 讀取延遲值 → R=延遲值,
        //      L += 延遲值 × 40%（crossfeed 增加左聲道豐滿度）
        //
        // 參數:
        //   stereoBuffer: L,R,L,R,... interleaved float 陣列（就地修改）
        //   sampleCount: stereo sample 對數（不是 float 總數）
        //
        // Comb Filter 細節:
        //   每條 comb 讀取延遲值 → LPF 阻尼（模擬高頻吸收）→
        //   寫入 input + dampedFeedback × 0.7 → 累加延遲值至 reverbOut
        // ─────────────────────────────────────────────────────────
        public void Process(float[] stereoBuffer, int sampleCount)
        {
            for (int i = 0; i < sampleCount; i++)
            {
                float L = stereoBuffer[i * 2];
                float R = stereoBuffer[i * 2 + 1];

                // ── Reverb（在 Haas 之前處理，使殘響也受空間化）──
                if (reverbWet > 0f)
                {
                    float mono = (L + R) * 0.5f;
                    float reverbOut = 0f;

                    for (int c = 0; c < COMB_COUNT; c++)
                    {
                        // 讀取延遲線的當前位置（即 combLengths[c] samples 前的信號）
                        float delayed = combBuf[c][combPos[c]];

                        // feedback 路徑 LPF（一階 IIR，阻尼高頻）
                        // 模擬真實房間中高頻反射衰減較快的物理現象
                        combLpfState[c] = delayed + combDamp * (combLpfState[c] - delayed);

                        // 寫入: 新輸入 + 經 LPF 阻尼的 feedback
                        combBuf[c][combPos[c]] = mono + combLpfState[c] * combFeedback;

                        // 推進延遲線指標（環形）
                        combPos[c]++;
                        if (combPos[c] >= combLengths[c]) combPos[c] = 0;

                        reverbOut += delayed;
                    }

                    // 4 條 comb 取平均，避免音量疊加過大
                    reverbOut *= (1f / COMB_COUNT);

                    // Wet/Dry mix: 殘響信號加回原始 L/R
                    L += reverbOut * reverbWet;
                    R += reverbOut * reverbWet;
                }

                // ── Haas Effect（右聲道延遲 + 左聲道 crossfeed）──
                // 將 R 寫入延遲環形緩衝區
                haasDelayBuf[haasWritePos] = R;

                // 讀取 haasDelaySamples 之前的 R 值
                int readPos = haasWritePos - haasDelaySamples;
                if (readPos < 0) readPos += MAX_DELAY;
                float delayedR = haasDelayBuf[readPos];

                haasWritePos++;
                if (haasWritePos >= MAX_DELAY) haasWritePos = 0;

                // Crossfeed: 延遲的 R 混入 L（增加豐滿度）
                // R 替換為延遲版本（產生時間差 → 空間感）
                L += delayedR * haasCrossfeed;
                R = delayedR;

                stereoBuffer[i * 2]     = L;
                stereoBuffer[i * 2 + 1] = R;
            }
        }

        // ─────────────────────────────────────────────────────────
        // Reset — 清除所有延遲線和濾波器狀態
        //
        // 將 Haas 延遲緩衝區和 4 條 Comb Filter 延遲線歸零，
        // 避免 Reset 後播放殘留音訊（尤其是長殘響的尾音）。
        // ─────────────────────────────────────────────────────────
        public void Reset()
        {
            Array.Clear(haasDelayBuf, 0, MAX_DELAY);
            haasWritePos = 0;
            for (int i = 0; i < COMB_COUNT; i++)
            {
                Array.Clear(combBuf[i], 0, combLengths[i]);
                combPos[i] = 0;
                combLpfState[i] = 0f;
            }
        }
    }
}
