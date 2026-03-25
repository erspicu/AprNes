using System;
using System.Runtime.CompilerServices;

namespace AprNes
{
    // =========================================================================
    // ConsoleModelFilter — 主機型號差異濾波器
    // =========================================================================
    // 模擬不同 NES/Famicom 硬體版本的類比音訊特性。
    //
    // 預設主機型號（0-5）：
    //   0  Famicom (HVC-001)       ~14kHz LPF, 明亮清晰, 無 buzz
    //   1  Front-Loader (NES-001)  ~4.7kHz LPF, 溫暖厚實, 無 buzz
    //   2  Top-Loader (NES-101)    ~20kHz LPF, 60Hz AC buzz
    //   3  AV Famicom (HVC-101)    ~19kHz LPF, 無 buzz, AV 直出乾淨
    //   4  Sharp Twin Famicom      ~12kHz LPF, 無 buzz, 略暗於原版
    //   5  Sharp Famicom Titler    ~16kHz LPF, 無 buzz, S-Video 乾淨
    //   6  Custom                  使用者自訂所有參數
    //
    // 濾波器:
    //   一階 IIR LPF: y[n] = beta × x[n] + (1-beta) × y[n-1]
    //   beta = dt / (rc + dt), 其中 rc = 1/(2π×fCutoff), dt = 1/44100
    //
    // 可調參數:
    //   - LPF cutoff (Custom 模式)
    //   - Buzz on/off + 振幅 + 頻率 (50/60Hz)
    //   - RF crosstalk 音量
    // =========================================================================
    class ConsoleModelFilter
    {
        const double DT = 1.0 / 44100.0; // 取樣間隔

        // ── 預設主機截止頻率和 buzz 設定 ──────────────────────────
        //                        Famicom  Front   Top     AV-FC   Twin    Titler
        static readonly double[] presetCutoffs = { 14000, 4700, 20000, 19000, 12000, 16000 };
        static readonly bool[]   presetBuzz    = { false, false, true,  false, false, false };

        // ── 實例狀態 ────────────────────────────────────────────
        float lpfState = 0f;        // IIR LPF 內部狀態
        float currentBeta;          // 目前使用的 LPF beta 係數
        int currentModel = 0;       // 目前主機型號 (0-6)
        bool buzzEnabled = false;   // 目前是否啟用 buzz

        // Buzz 振盪器狀態
        double buzzPhase = 0.0;
        double buzzPhaseInc;        // 每 sample 相位增量 (freq/44100)
        float buzzAmplitude;        // buzz 振幅 (0.000~0.010)

        // RF crosstalk 振盪器狀態
        double rfPhase = 0.0;
        const double RF_PHASE_INC = 59.94 / 44100.0;
        float rfBaseVolume;         // RF 串擾基礎音量 (0.00~0.20)
        float videoLuminance = 0f;

        // ─────────────────────────────────────────────────────────
        // 建構子 — 設定預設值
        // ─────────────────────────────────────────────────────────
        public ConsoleModelFilter()
        {
            SetBuzzParams(60, 30);
            SetRfVolume(50);
            SetModel(0, 14000, false);
        }

        // ─────────────────────────────────────────────────────────
        // 輔助 — 從截止頻率計算 beta 係數
        // ─────────────────────────────────────────────────────────
        static float CalcBeta(double cutoffHz)
        {
            double rc = 1.0 / (2.0 * Math.PI * cutoffHz);
            return (float)(DT / (rc + DT));
        }

        // ─────────────────────────────────────────────────────────
        // SetModel — 設定主機型號
        //
        // 參數:
        //   model: 0-5=預設主機, 6=Custom
        //   customCutoff: Custom 模式的 LPF 截止頻率 (Hz)
        //   customBuzz: Custom 模式是否啟用 buzz
        //
        // 預設主機 (0-5): 自動套用對應的 cutoff 和 buzz 設定
        // Custom (6): 使用 customCutoff 和 customBuzz 參數
        // ─────────────────────────────────────────────────────────
        public void SetModel(int model, int customCutoff, bool customBuzz)
        {
            currentModel = Math.Max(0, Math.Min(6, model));

            if (currentModel < 6)
            {
                // 預設主機：使用 preset 表
                currentBeta = CalcBeta(presetCutoffs[currentModel]);
                buzzEnabled = presetBuzz[currentModel];
            }
            else
            {
                // Custom：使用使用者參數
                int cutoff = Math.Max(1000, Math.Min(22000, customCutoff));
                currentBeta = CalcBeta(cutoff);
                buzzEnabled = customBuzz;
            }
        }

        // ─────────────────────────────────────────────────────────
        // SetBuzzParams — 設定 Buzz 參數
        //
        // 參數:
        //   freq: 頻率 (50 或 60 Hz，對應歐規/美規市電)
        //   amplitude: 振幅 (0-100, 映射至 0.000~0.010)
        // ─────────────────────────────────────────────────────────
        public void SetBuzzParams(int freq, int amplitude)
        {
            int f = (freq == 50) ? 50 : 60;
            buzzPhaseInc = f / 44100.0;
            buzzAmplitude = Math.Max(0, Math.Min(100, amplitude)) * 0.0001f;
        }

        // ─────────────────────────────────────────────────────────
        // SetRfVolume — 設定 RF 串擾音量
        //
        // 參數:
        //   volume: 0-200 (映射至 0.00~0.20)
        // ─────────────────────────────────────────────────────────
        public void SetRfVolume(int volume)
        {
            rfBaseVolume = Math.Max(0, Math.Min(200, volume)) * 0.001f;
        }

        // ─────────────────────────────────────────────────────────
        // SetVideoLuminance — 設定 PPU 畫面平均亮度（供 RF crosstalk 使用）
        // ─────────────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetVideoLuminance(float luma)
        {
            videoLuminance = luma;
        }

        // ─────────────────────────────────────────────────────────
        // Process — 處理一個 44.1kHz 音訊樣本
        //
        // 流程:
        //   1. 一階 IIR LPF（beta 由主機型號或 Custom 設定決定）
        //   2. Buzz 啟用時：加入正弦波（頻率/振幅可調）
        //   3. RF 模式啟用時：加入鋸齒波 × 畫面亮度的串擾
        // ─────────────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float input, bool rfCrosstalk)
        {
            // 一階 IIR LPF
            lpfState += currentBeta * (input - lpfState);
            float output = lpfState;

            // Buzz（buzz 啟用且振幅 > 0 時才計算）
            if (buzzEnabled && buzzAmplitude > 0f)
            {
                buzzPhase += buzzPhaseInc;
                if (buzzPhase >= 1.0) buzzPhase -= 1.0;
                output += (float)(Math.Sin(2.0 * Math.PI * buzzPhase)) * buzzAmplitude;
            }

            // RF crosstalk
            if (rfCrosstalk && rfBaseVolume > 0f)
            {
                rfPhase += RF_PHASE_INC;
                if (rfPhase >= 1.0) rfPhase -= 1.0;
                float sawtooth = (float)(rfPhase - 0.5) * 2f;
                output += sawtooth * videoLuminance * rfBaseVolume;
            }

            return output;
        }

        // ─────────────────────────────────────────────────────────
        // Reset — 清除所有濾波器和振盪器狀態
        // ─────────────────────────────────────────────────────────
        public void Reset()
        {
            lpfState = 0f;
            buzzPhase = 0.0;
            rfPhase = 0.0;
        }
    }
}
