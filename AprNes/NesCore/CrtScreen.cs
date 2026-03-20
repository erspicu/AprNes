using System;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

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
    //    4. Gamma 2.2 查表（LUT 4096 entries，輸入範圍 [0..2.0]）
    //
    //  三種端子參數組（由 NesCore.AnalogOutput 決定）：
    //    RF     : BeamSigma=1.10, BloomStrength=0.50, BrightnessBoost=1.10
    //    AV     : BeamSigma=0.85, BloomStrength=0.25, BrightnessBoost=1.25
    //    SVideo : BeamSigma=0.65, BloomStrength=0.10, BrightnessBoost=1.40
    // ============================================================

    unsafe public static class CrtScreen
    {
        public const int SrcW = 768;
        public const int SrcH = 240;
        public const int DstW = 768;
        public const int DstH = 630;

        // ── 端子參數組 ───────────────────────────────────────────────────────
        static float BeamSigma      = 0.85f;
        static float BloomStrength  = 0.25f;
        static float BrightnessBoost = 1.25f;

        // ── Gamma 2.2 LUT ────────────────────────────────────────────────────
        //   索引 i → 線性亮度 i / 2048.0f（範圍 0..2.0）
        //   輸出：sRGB byte（Gamma 校正後）
        static readonly byte[] _gammaLUT = new byte[4096];

        // ── 掃描線預計算快取 ─────────────────────────────────────────────────
        static float   _cachedSigma = -1f;
        static readonly float[] _weights  = new float[DstH];
        static readonly int[]   _nearestY = new int[DstH];

        // ════════════════════════════════════════════════════════════════════
        // Init
        // ════════════════════════════════════════════════════════════════════
        public static void Init()
        {
            // 建立 Gamma 2.2 LUT：線性 [0..2.0] → sRGB byte [0..255]
            for (int i = 0; i < 4096; i++)
            {
                double v = i / 2048.0;
                double g = Math.Pow(v, 1.0 / 2.2);
                _gammaLUT[i] = (byte)Math.Min(255, (int)(g * 255.0 + 0.5));
            }
            _cachedSigma = -1f; // 強制重新計算掃描線權重
        }

        // ── 端子參數套用 ─────────────────────────────────────────────────────
        static void ApplyProfile()
        {
            switch (NesCore.AnalogOutput)
            {
                case NesCore.AnalogOutputMode.RF:
                    BeamSigma = 1.10f; BloomStrength = 0.50f; BrightnessBoost = 1.10f; break;
                case NesCore.AnalogOutputMode.SVideo:
                    BeamSigma = 0.65f; BloomStrength = 0.10f; BrightnessBoost = 1.40f; break;
                default: // AV
                    BeamSigma = 0.85f; BloomStrength = 0.25f; BrightnessBoost = 1.25f; break;
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

            float   bloom  = BloomStrength;
            float   boost  = BrightnessBoost;
            float[] lb     = Ntsc.linearBuffer;
            uint*   dst    = NesCore.AnalogScreenBuf3x;
            float[] wts    = _weights;
            int[]   nyArr  = _nearestY;

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

        // ── Gamma 2.2 → BGRA uint ───────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint GammaBgra(float r, float g, float b)
        {
            int ri = (int)(r * 2048f);
            int gi = (int)(g * 2048f);
            int bi = (int)(b * 2048f);

            byte rb = ri <= 0 ? (byte)0 : ri >= 4096 ? (byte)255 : _gammaLUT[ri];
            byte gb = gi <= 0 ? (byte)0 : gi >= 4096 ? (byte)255 : _gammaLUT[gi];
            byte bb = bi <= 0 ? (byte)0 : bi >= 4096 ? (byte)255 : _gammaLUT[bi];

            return (uint)(bb | (gb << 8) | (rb << 16) | 0xFF000000u);
        }
    }
}
