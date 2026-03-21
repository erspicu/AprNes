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
    //  輸出：NesCore.AnalogScreenBuf [1024 × 840 BGRA]
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
        public const int SrcW = 1024;  // linearBuffer 列寬（固定）
        public const int SrcH = 240;   // linearBuffer 列數（固定）
        // DstW/DstH 依 AnalogSize 動態決定（256×N × 210×N，維持 8:7 AR）
        public static int DstW => 256 * NesCore.AnalogSize;
        public static int DstH => 210 * NesCore.AnalogSize;

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
            // 每次 Init 都重新分配（AnalogSize 可能已改變）
            if (_weights  != null) Marshal.FreeHGlobal((IntPtr)_weights);
            if (_nearestY != null) Marshal.FreeHGlobal((IntPtr)_nearestY);
            _weights  = (float*)Marshal.AllocHGlobal(DstH * sizeof(float));
            _nearestY = (int*)  Marshal.AllocHGlobal(DstH * sizeof(int));
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
            if (NesCore.AnalogScreenBuf == null) return;

            ApplyProfile();
            PrecomputeScanlineWeights();

            float  bloom     = BloomStrength;
            float  boost     = BrightnessBoost;
            float* lb        = Ntsc.linearBuffer;
            uint*  dst       = NesCore.AnalogScreenBuf;
            float* wts       = _weights;
            int*   nyArr     = _nearestY;
            const int kPlane = Ntsc.kPlane; // R/G/B plane stride（245,760 floats）

            int dstW     = DstW;  // 快取，避免 lambda 內重複呼叫 property
            int dstH     = DstH;
            bool is1to1  = (dstW == SrcW);      // N=4：1:1 SIMD
            bool isDouble = (dstW == SrcW * 2); // N=8：每 source 像素 → 2 output，SIMD

            int VS = Vector<float>.Count;  // 4（SSE2）或 8（AVX2）

            // 常數向量（frame 層次，Parallel.For 外部建立一次）
            var vBloom = new Vector<float>(bloom);
            var vBoost = new Vector<float>(boost);
            var vOne   = new Vector<float>(1f);
            var vZero  = new Vector<float>(0f);
            var v03    = new Vector<float>(0.3f);
            var v059   = new Vector<float>(0.59f);
            var v011   = new Vector<float>(0.11f);
            var vGF    = new Vector<float>(0.229f);

            // S02: SIMD 像素打包常數
            var v255_5f = new Vector<float>(255.5f);
            var v255i   = new Vector<int>(255);
            var vZeroi  = new Vector<int>(0);
            var v256i   = new Vector<int>(256);
            var v65536i = new Vector<int>(65536);
            var vAlphai = new Vector<int>(unchecked((int)0xFF000000));

            Parallel.For(0, dstH, ty =>
            {
                float  weight  = wts[ty];
                float  omw     = 1f - weight;
                uint*  rowPtr  = dst + ty * dstW;
                int    ny      = nyArr[ty];
                // linearBuffer 列寬永遠是 SrcW=1024，與 DstW 無關
                float* lb_r    = lb              + ny * SrcW;
                float* lb_g    = lb + kPlane     + ny * SrcW;
                float* lb_b    = lb + 2 * kPlane + ny * SrcW;

                int x = 0;

                if (is1to1)
                {
                    // N=4 最佳路徑：1:1 水平映射，SIMD 連續讀取
                    // S01: 常數提升 — vFw = constA + vBright * constB
                    var vConstA = new Vector<float>(weight * boost);
                    var vConstB = new Vector<float>(bloom * omw * boost);

#pragma warning disable CS8500
                    for (; x <= SrcW - VS; x += VS)
                    {
                        var vr = *(Vector<float>*)(lb_r + x);
                        var vg = *(Vector<float>*)(lb_g + x);
                        var vb = *(Vector<float>*)(lb_b + x);

                        var vBright = vr * v03 + vg * v059 + vb * v011;
                        var vFw     = vConstA + vBright * vConstB;

                        vr = Vector.Min(Vector.Max(vr * vFw, vZero), vOne);
                        vg = Vector.Min(Vector.Max(vg * vFw, vZero), vOne);
                        vb = Vector.Min(Vector.Max(vb * vFw, vZero), vOne);

                        vr += vGF * vr * (vr - vOne);
                        vg += vGF * vg * (vg - vOne);
                        vb += vGF * vb * (vb - vOne);

                        // S02: SIMD 像素打包（消除 scalar extraction loop）
                        var viR = Vector.Max(vZeroi, Vector.Min(Vector.ConvertToInt32(vr * v255_5f), v255i));
                        var viG = Vector.Max(vZeroi, Vector.Min(Vector.ConvertToInt32(vg * v255_5f), v255i));
                        var viB = Vector.Max(vZeroi, Vector.Min(Vector.ConvertToInt32(vb * v255_5f), v255i));
                        *(Vector<int>*)(rowPtr + x) = Vector.BitwiseOr(
                            Vector.BitwiseOr(viB, viG * v256i),
                            Vector.BitwiseOr(viR * v65536i, vAlphai));
                    }
#pragma warning restore CS8500
                }
                else if (isDouble)
                {
                    // N=8 SIMD 路徑：每 source 像素計算一次，結果寫入兩個相鄰 output
                    // S01: 常數提升 — vFw = constA + vBright * constB
                    var vConstA = new Vector<float>(weight * boost);
                    var vConstB = new Vector<float>(bloom * omw * boost);
                    int srcX = 0;

#pragma warning disable CS8500
                    for (; srcX <= SrcW - VS; srcX += VS)
                    {
                        var vr = *(Vector<float>*)(lb_r + srcX);
                        var vg = *(Vector<float>*)(lb_g + srcX);
                        var vb = *(Vector<float>*)(lb_b + srcX);

                        var vBright = vr * v03 + vg * v059 + vb * v011;
                        var vFw     = vConstA + vBright * vConstB;

                        vr = Vector.Min(Vector.Max(vr * vFw, vZero), vOne);
                        vg = Vector.Min(Vector.Max(vg * vFw, vZero), vOne);
                        vb = Vector.Min(Vector.Max(vb * vFw, vZero), vOne);

                        vr += vGF * vr * (vr - vOne);
                        vg += vGF * vg * (vg - vOne);
                        vb += vGF * vb * (vb - vOne);

                        // S02: SIMD 像素打包
                        var viR = Vector.Max(vZeroi, Vector.Min(Vector.ConvertToInt32(vr * v255_5f), v255i));
                        var viG = Vector.Max(vZeroi, Vector.Min(Vector.ConvertToInt32(vg * v255_5f), v255i));
                        var viB = Vector.Max(vZeroi, Vector.Min(Vector.ConvertToInt32(vb * v255_5f), v255i));
                        var packed = Vector.BitwiseOr(
                            Vector.BitwiseOr(viB, viG * v256i),
                            Vector.BitwiseOr(viR * v65536i, vAlphai));
                        // 每 source pixel → 2 output pixels（雙倍寫入）
                        for (int k = 0; k < VS; k++)
                        {
                            uint px = ((uint*)&packed)[k];
                            int outX = (srcX + k) * 2;
                            rowPtr[outX]     = px;
                            rowPtr[outX + 1] = px;
                        }
                    }
#pragma warning restore CS8500

                    // 尾端 scalar（SrcW=1024 整除 4/8，實際不執行）
                    for (; srcX < SrcW; srcX++)
                    {
                        float r = lb_r[srcX], g = lb_g[srcX], b = lb_b[srcX];
                        float bright = r * 0.3f + g * 0.59f + b * 0.11f;
                        float fw = weight * boost + bright * bloom * omw * boost;
                        r *= fw; if (r < 0f) r = 0f; else if (r > 1f) r = 1f;
                        g *= fw; if (g < 0f) g = 0f; else if (g > 1f) g = 1f;
                        b *= fw; if (b < 0f) b = 0f; else if (b > 1f) b = 1f;
                        r += 0.229f * r * (r - 1f);
                        g += 0.229f * g * (g - 1f);
                        b += 0.229f * b * (b - 1f);
                        int ri = (int)(r * 255.5f); if (ri > 255) ri = 255;
                        int gi = (int)(g * 255.5f); if (gi > 255) gi = 255;
                        int bi = (int)(b * 255.5f); if (bi > 255) bi = 255;
                        uint px = (uint)(bi | (gi << 8) | (ri << 16) | 0xFF000000u);
                        rowPtr[srcX * 2]     = px;
                        rowPtr[srcX * 2 + 1] = px;
                    }
                    return;
                }
                else
                {
                    // N=2/6 純量路徑：線性插值水平重採樣
                    // 固定小數點 16-bit fraction，避免浮點除法
                    int fpScale = (SrcW << 16) / dstW; // 每輸出像素對應的 source 步進（fixed-point）
                    for (; x < dstW; x++)
                    {
                        int fp    = x * fpScale;
                        int srcX  = fp >> 16;
                        float t   = (fp & 0xFFFF) * (1f / 65536f); // 小數部分
                        int srcX1 = srcX + 1 < SrcW ? srcX + 1 : srcX;
                        float r = lb_r[srcX] + t * (lb_r[srcX1] - lb_r[srcX]);
                        float g = lb_g[srcX] + t * (lb_g[srcX1] - lb_g[srcX]);
                        float b = lb_b[srcX] + t * (lb_b[srcX1] - lb_b[srcX]);
                        float bright = r * 0.3f + g * 0.59f + b * 0.11f;
                        float fw = weight * boost + bright * bloom * omw * boost;

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
                    return; // scalar path 已處理所有像素
                }

                // N=4 尾端 scalar（DstW=1024 整除 4/8，實際不執行）
                for (; x < dstW; x++)
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
