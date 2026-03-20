using System;
using System.Numerics;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AprNes
{
    // ============================================================
    // CRT 電視光學模擬器（Stage 2）
    // ============================================================
    //
    //  輸入：Ntsc.linearBuffer [1024 × 240 × 3]  ← 線性 RGB，無 Gamma
    //         Planar 佈局：R[0..kPlane-1] G[kPlane..2kPlane-1] B[2kPlane..3kPlane-1]
    //  輸出：NesCore.AnalogScreenBuf3x [1024 × 840 BGRA]
    //
    //  垂直映射：240 → 840（× 3.5），連續域高斯掃描線
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

        // ── 掃描線預計算快取（unmanaged memory）─────────────────────────────
        static float  _cachedSigma = -1f;
        static float* _weights;    // DstH floats
        static int*   _nearestY;   // DstH ints

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
        //
        //  Planar linearBuffer：lb_r / lb_g / lb_b 各自連續，SIMD 無跨步
        //  Vector<float> SIMD 主迴圈（SSE2=4 floats, AVX2=8 floats）
        //  Parallel.For 多核並行，每個 scanline 獨立
        // ════════════════════════════════════════════════════════════════════
        public static unsafe void Render()
        {
            if (NesCore.AnalogScreenBuf3x == null) return;

            ApplyProfile();
            PrecomputeScanlineWeights();

            float  bloom     = BloomStrength;
            float  boost     = BrightnessBoost;
            float* lb        = Ntsc.linearBuffer;
            uint*  dst       = NesCore.AnalogScreenBuf3x;
            float* wts       = _weights;
            int*   nyArr     = _nearestY;
            const int kPlane = Ntsc.kPlane; // R/G/B plane stride（245,760 floats）

            int VS = Vector<float>.Count;   // 4（SSE2）或 8（AVX2）

            // 常數向量（frame 層次，Parallel.For 外部建立一次）
            var vBloom = new Vector<float>(bloom);
            var vBoost = new Vector<float>(boost);
            var vOne   = new Vector<float>(1f);
            var vZero  = new Vector<float>(0f);
            var v03    = new Vector<float>(0.3f);
            var v059   = new Vector<float>(0.59f);
            var v011   = new Vector<float>(0.11f);
            var vGF    = new Vector<float>(0.229f);

            Parallel.For(0, DstH, ty =>
            {
                float  weight  = wts[ty];
                float  omw     = 1f - weight;            // (1 − weight)，用於 Bloom
                var    vWeight = new Vector<float>(weight);
                var    vOMW    = new Vector<float>(omw);
                uint*  rowPtr  = dst + ty * DstW;
                int    ny      = nyArr[ty];
                float* lb_r    = lb              + ny * DstW; // R plane，該 scanline 起始
                float* lb_g    = lb + kPlane     + ny * DstW; // G plane
                float* lb_b    = lb + 2 * kPlane + ny * DstW; // B plane

                // SIMD 主迴圈：每次處理 VS 個像素
                // 從 planar buffer 逐一載入連續 float（無跨步），利用指標轉型
#pragma warning disable CS8500
                int x = 0;
                for (; x <= DstW - VS; x += VS)
                {
                    var vr = *(Vector<float>*)(lb_r + x);
                    var vg = *(Vector<float>*)(lb_g + x);
                    var vb = *(Vector<float>*)(lb_b + x);

                    // Bloom：高亮度像素填補掃描線黑溝
                    var vBright = vr * v03 + vg * v059 + vb * v011;
                    var vFw     = (vWeight + vBright * vBloom * vOMW) * vBoost;

                    // 套用亮度係數 + clamp [0,1]
                    vr = Vector.Min(Vector.Max(vr * vFw, vZero), vOne);
                    vg = Vector.Min(Vector.Max(vg * vFw, vZero), vOne);
                    vb = Vector.Min(Vector.Max(vb * vFw, vZero), vOne);

                    // Fast gamma：v' = v + 0.229·v·(v−1)
                    vr += vGF * vr * (vr - vOne);
                    vg += vGF * vg * (vg - vOne);
                    vb += vGF * vb * (vb - vOne);

                    // 逐元素提取 → scale + pack BGRA
                    for (int k = 0; k < VS; k++)
                    {
                        int ri = Math.Min(255, (int)(vr[k] * 255.5f));
                        int gi = Math.Min(255, (int)(vg[k] * 255.5f));
                        int bi = Math.Min(255, (int)(vb[k] * 255.5f));
                        rowPtr[x + k] = (uint)(bi | (gi << 8) | (ri << 16) | 0xFF000000u);
                    }
                }
#pragma warning restore CS8500

                // 尾端 scalar（DstW=1024 可整除 4/8，實際不執行，保留完整性）
                for (; x < DstW; x++)
                {
                    float r = lb_r[x], g = lb_g[x], b = lb_b[x];
                    float bright = r * 0.3f + g * 0.59f + b * 0.11f;
                    float fw = (weight + bright * bloom * omw) * boost;

                    r *= fw; if (r < 0f) r = 0f; else if (r > 1f) r = 1f;
                    g *= fw; if (g < 0f) g = 0f; else if (g > 1f) g = 1f;
                    b *= fw; if (b < 0f) b = 0f; else if (b > 1f) b = 1f;

                    r += 0.229f * r * (r - 1f);
                    g += 0.229f * g * (g - 1f);
                    b += 0.229f * b * (b - 1f);

                    int ri = (int)(r * 255.5f); if (ri > 255) ri = 255;
                    int gi = (int)(g * 255.5f); if (gi > 255) gi = 255;
                    int bi = (int)(b * 255.5f); if (bi > 255) bi = 255;
                    rowPtr[x] = (uint)(bi | (gi << 8) | (ri << 16) | 0xFF000000u);
                }
            });
        }
    }
}
