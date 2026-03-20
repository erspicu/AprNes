using System;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AprNes
{
    // ============================================================
    // CRT 電視光學模擬器（Stage 2）
    // ============================================================
    //
    //  輸入：Ntsc.linearBuffer [768 × 240 × 3]  ← 線性 RGB，無 Gamma
    //  輸出：NesCore.AnalogScreenBuf3x [768 × 630 BGRA]
    //
    //  垂直映射：240 → 630（× 2.625），連續域高斯掃描線
    //  演算法：
    //    1. 高斯掃描線權重：W = exp(−dy² / (2σ²))
    //    2. Bloom（高光溢出）：W_final = W + brightness × BloomStrength × (1−W)
    //    3. BrightnessBoost：補償掃描線黑溝造成的平均亮度損失
    //    4. Fast gamma（≈ pow(v,1/1.13)，與原 YiqToRgb 一致，保留 NES 色調）
    //
    //  三種端子參數組（由 NesCore.AnalogOutput 決定）：
    //    RF     : BeamSigma=1.10, BloomStrength=0.50, BrightnessBoost=1.10
    //    AV     : BeamSigma=0.85, BloomStrength=0.25, BrightnessBoost=1.25
    //    SVideo : BeamSigma=0.65, BloomStrength=0.10, BrightnessBoost=1.40
    // ============================================================

    unsafe public static class CrtScreen
    {
        public const int SrcW = 1024;
        public const int SrcH = 240;
        public const int DstW = 1024;
        public const int DstH = 840;

        // ── 端子參數組（INI 讀入，開機時載入一次）──────────────────────────
        // RF 端子
        public static float RF_BeamSigma       = 1.10f;
        public static float RF_BloomStrength   = 0.50f;
        public static float RF_BrightnessBoost = 1.10f;
        // AV 端子
        public static float AV_BeamSigma       = 0.85f;
        public static float AV_BloomStrength   = 0.25f;
        public static float AV_BrightnessBoost = 1.25f;
        // S-Video 端子
        public static float SV_BeamSigma       = 0.65f;
        public static float SV_BloomStrength   = 0.10f;
        public static float SV_BrightnessBoost = 1.40f;

        // 當前使用中的參數（由 ApplyProfile 設定）
        static float BeamSigma;
        static float BloomStrength;
        static float BrightnessBoost;

        // ── 掃描線預計算快取 ─────────────────────────────────────────────────
        static float  _cachedSigma = -1f;
        static float* _weights;
        static int*   _nearestY;

        // ════════════════════════════════════════════════════════════════════
        // Init
        // ════════════════════════════════════════════════════════════════════
        public static void Init()
        {
            if (_weights == null)
            {
                _weights  = (float*)Marshal.AllocHGlobal(DstH * sizeof(float));
                _nearestY = (int*)  Marshal.AllocHGlobal(DstH * sizeof(int));
            }
            _cachedSigma = -1f; // 強制重新計算掃描線權重
        }

        // ── 端子參數套用 ─────────────────────────────────────────────────────
        static void ApplyProfile()
        {
            switch (NesCore.AnalogOutput)
            {
                case NesCore.AnalogOutputMode.RF:
                    BeamSigma = RF_BeamSigma; BloomStrength = RF_BloomStrength; BrightnessBoost = RF_BrightnessBoost; break;
                case NesCore.AnalogOutputMode.SVideo:
                    BeamSigma = SV_BeamSigma; BloomStrength = SV_BloomStrength; BrightnessBoost = SV_BrightnessBoost; break;
                default: // AV
                    BeamSigma = AV_BeamSigma; BloomStrength = AV_BloomStrength; BrightnessBoost = AV_BrightnessBoost; break;
            }
        }

        // ── 掃描線高斯權重預計算（BeamSigma 改變時才重算）───────────────────
        static void PrecomputeScanlineWeights()
        {
            if (_cachedSigma == BeamSigma) return;
            _cachedSigma = BeamSigma;

            float inv = 1f / (2f * BeamSigma * BeamSigma);
            for (int ty = 0; ty < DstH; ty++)
            {
                float sy = (float)ty / DstH * SrcH;
                int   ny = (int)(sy + 0.5f);
                if (ny >= SrcH) ny = SrcH - 1;
                _nearestY[ty] = ny;
                float dy = sy - ny;
                _weights[ty] = (float)Math.Exp(-(dy * dy) * inv);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // 主渲染（由 PPU.RenderScreen 在 VideoOutput 前呼叫）
        // ════════════════════════════════════════════════════════════════════
        public static unsafe void Render()
        {
            if (NesCore.AnalogScreenBuf3x == null) return;

            ApplyProfile();
            PrecomputeScanlineWeights();

            float  bloom = BloomStrength;
            float  boost = BrightnessBoost;
            float* lb    = Ntsc.linearBuffer;
            uint*  dst   = NesCore.AnalogScreenBuf3x;
            float* wts   = _weights;
            int*   nyArr = _nearestY;

            Parallel.For(0, DstH, ty =>
            {
                float weight = wts[ty];
                uint* rowPtr = dst + ty * DstW;
                int   srcOff = nyArr[ty] * DstW * 3;

                for (int x = 0; x < DstW; x++)
                {
                    int   px = srcOff + x * 3;
                    float r  = lb[px];
                    float g  = lb[px + 1];
                    float b  = lb[px + 2];

                    // Bloom：高亮度像素吃掉掃描線黑溝
                    float brightness = r * 0.3f + g * 0.59f + b * 0.11f;
                    float fw = weight + brightness * bloom * (1f - weight);
                    fw *= boost;

                    rowPtr[x] = GammaBgra(r * fw, g * fw, b * fw);
                }
            });
        }

        // ── Fast gamma → BGRA uint ──────────────────────────────────────────
        //   NES composite 訊號電壓已接近 broadcast gamma 編碼，
        //   使用與原 YiqToRgb 相同的 fast gamma（≈ pow(v, 1/1.13)）
        //   而非 sRGB gamma 2.2，避免中暗色過度提亮。
        //   公式：v' = v + 0.229·v·(v−1)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint GammaBgra(float r, float g, float b)
        {
            // clamp to [0,1] before gamma（Bloom 可能超過 1.0，超出範圍直接夾至 255）
            if (r < 0f) r = 0f; else if (r > 1f) r = 1f;
            if (g < 0f) g = 0f; else if (g > 1f) g = 1f;
            if (b < 0f) b = 0f; else if (b > 1f) b = 1f;

            const float gf = 0.229f;
            r += gf * r * (r - 1f);
            g += gf * g * (g - 1f);
            b += gf * b * (b - 1f);

            int ri = (int)(r * 255.5f);
            int gi = (int)(g * 255.5f);
            int bi = (int)(b * 255.5f);
            if (ri > 255) ri = 255;
            if (gi > 255) gi = 255;
            if (bi > 255) bi = 255;

            return (uint)(bi | (gi << 8) | (ri << 16) | 0xFF000000u);
        }
    }
}
