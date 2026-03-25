using System;
using System.Runtime.CompilerServices;

namespace AprNes
{
    // =========================================================================
    // AuthenticAudioMixer — 物理級 DAC 非線性混音器
    // =========================================================================
    // 精確模擬 NES 硬體的 DAC 混音特性。真實 NES 使用電阻梯級 DAC，
    // 各聲道間存在非線性交互作用（不是簡單的線性加法）。
    //
    // NES DAC 公式（nesdev wiki）：
    //   pulse_out = 95.88 / (8128.0 / (pulse1 + pulse2) + 100)
    //   tnd_out   = 159.79 / (1.0 / (tri/8227 + noise/12241 + dmc/22638) + 100)
    //
    // 查找表：
    //   pulseTable[31]:    sq1+sq2 = 0~30，共 31 個值
    //   tndTable[32768]:   tri[0-15] × noise[0-15] × dmc[0-127] 完整 3D 查找
    //                      index = (tri<<11) | (noise<<7) | dmc
    //                      記憶體: 32768 × 4 bytes = 128KB
    //
    // 另含 90Hz HPF 在原生 1.79MHz 取樣率下運作，消除 DC 偏移。
    // =========================================================================
    class AuthenticAudioMixer
    {
        // ── DAC 查找表（靜態，所有實例共用）──────────────────────
        static readonly float[] pulseTable;      // [31]  Pulse 非線性混音
        static readonly float[] tndTable;        // [32768] TND 3D 非線性混音

        // ─────────────────────────────────────────────────────────
        // 靜態建構子 — 預計算 DAC 查找表
        // ─────────────────────────────────────────────────────────
        static AuthenticAudioMixer()
        {
            // Pulse table: pulse_out = 95.88 / (8128/n + 100), n = sq1+sq2
            pulseTable = new float[31];
            pulseTable[0] = 0f;
            for (int i = 1; i < 31; i++)
                pulseTable[i] = (float)(95.88 / (8128.0 / i + 100.0));

            // TND 3D table: tnd_out = 159.79 / (1/(t/8227 + n/12241 + d/22638) + 100)
            tndTable = new float[16 * 16 * 128];
            for (int t = 0; t < 16; t++)
            {
                for (int n = 0; n < 16; n++)
                {
                    for (int d = 0; d < 128; d++)
                    {
                        double sum = t / 8227.0 + n / 12241.0 + d / 22638.0;
                        float val = (sum < 1e-12) ? 0f : (float)(159.79 / (1.0 / sum + 100.0));
                        tndTable[(t << 11) | (n << 7) | d] = val;
                    }
                }
            }
        }

        // ── 90Hz HPF 狀態（在 1.79MHz 原生取樣率下運作）─────────
        // 一階 IIR 高通: y[n] = alpha × (y[n-1] + x[n] - x[n-1])
        // rc = 1/(2π×90), dt = 1/1789773
        // alpha = rc/(rc+dt) ≈ 0.99996844
        // 使用 double 精度避免長時間運算的累積誤差
        double hpfState = 0.0;
        double hpfPrev  = 0.0;
        const double HPF_ALPHA = 0.99996844;

        // ─────────────────────────────────────────────────────────
        // GetVoltage — 計算一個 APU cycle 的混音電壓值
        //
        // 流程: 5 聲道 → DAC 非線性查找 → 加入 expansion audio → 90Hz HPF
        //
        // 參數:
        //   sq1, sq2: Pulse 1/2 輸出 (0-15)
        //   tri:      Triangle 輸出 (0-15)
        //   noise:    Noise 輸出 (0-15)
        //   dmc:      DMC 輸出 (0-127)
        //   expansionAudio: Mapper 擴展音效（整數，需歸一化至 DAC 電壓級別）
        //
        // 回傳: 濾波後的 float 電壓值（約 ±0.5 範圍）
        // ─────────────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetVoltage(int sq1, int sq2, int tri, int noise, int dmc, int expansionAudio)
        {
            // DAC 非線性混音查找
            int pulseIdx = sq1 + sq2;
            if (pulseIdx > 30) pulseIdx = 30;

            float pulse = pulseTable[pulseIdx];
            float tnd = tndTable[(tri << 11) | (noise << 7) | dmc];
            double raw = pulse + tnd;

            // Expansion audio 歸一化至 DAC 電壓級別
            // 原始 SQUARELOOKUP+TNDLOOKUP 輸出 0..~98302，DAC pulse+tnd 輸出 0..~1.0
            // expansion 原本設計為與 98302 級別混合，故 /98302 歸一化
            raw += expansionAudio / 98302.0;

            // 90Hz 高通濾波器（消除 DC 偏移）
            double diff = raw - hpfPrev;
            hpfPrev = raw;
            hpfState = HPF_ALPHA * (hpfState + diff);

            return (float)hpfState;
        }

        // ─────────────────────────────────────────────────────────
        // Reset — 清除 HPF 濾波器狀態
        // ─────────────────────────────────────────────────────────
        public void Reset()
        {
            hpfState = 0.0;
            hpfPrev  = 0.0;
        }
    }
}
