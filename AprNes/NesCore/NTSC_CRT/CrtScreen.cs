using System;
using System.Numerics;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AprNes
{
    // ============================================================
    // CRT 電視光學模擬器（Stage 2） - .NET 4.8.1 Fused & SWAR 版
    // ============================================================
    unsafe public static class CrtScreen
    {
        // ── 解耦參數 ────────────────────────
        static int _analogOutput;
        static int _analogSize = 4;
        static uint* _analogScreenBuf;
        static int _frameCount;

        public static void ApplyConfig(int analogOutput, int analogSize, uint* analogScreenBuf)
        {
            _analogOutput = analogOutput;
            _analogSize = analogSize;
            _analogScreenBuf = analogScreenBuf;
            ApplyProfile();
        }

        public static void SetFrameCount(int fc) => _frameCount = fc;

        public const int SrcW = 1024;
        public const int SrcH = 240;
        static int? _fullscreenW = null, _fullscreenH = null;
        public static int DstW => _fullscreenW ?? 256 * _analogSize;
        public static int DstH => _fullscreenH ?? 210 * _analogSize;

        public static void SetFullscreenSize(int w, int h) { _fullscreenW = w; _fullscreenH = h; }
        public static void ClearFullscreenSize() { _fullscreenW = null; _fullscreenH = null; }

        // ── 端子參數組 ──────────────────────────
        public static float RF_BeamSigma = 1.10f;
        public static float RF_BloomStrength = 0.50f;
        public static float RF_BrightnessBoost = 1.10f;
        public static float AV_BeamSigma = 0.85f;
        public static float AV_BloomStrength = 0.25f;
        public static float AV_BrightnessBoost = 1.25f;
        public static float SV_BeamSigma = 0.65f;
        public static float SV_BloomStrength = 0.10f;
        public static float SV_BrightnessBoost = 1.40f;

        static float BeamSigma;
        static float BloomStrength;
        static float BrightnessBoost;

        public static float VignetteStrength = 0.15f;
        public static bool InterlaceJitter = true;
        public enum MaskType { None, ApertureGrille, ShadowMask }
        public static MaskType ShadowMaskMode = MaskType.ApertureGrille;
        public static float ShadowMaskStrength = 0.3f;
        public static float CurvatureStrength = 0.12f;
        public static float PhosphorDecay = 0.6f;
        public static float HBeamSpread = 0.4f;
        public static float ConvergenceStrength = 2.0f;

        // ── SIMD 常數向量 ────────────────────────────
        static readonly Vector<float> vOne = new Vector<float>(1f);
        static readonly Vector<float> vZero = new Vector<float>(0f);
        static readonly Vector<float> v03 = new Vector<float>(0.3f);
        static readonly Vector<float> v059 = new Vector<float>(0.59f);
        static readonly Vector<float> v011 = new Vector<float>(0.11f);
        static readonly Vector<float> v255_5f = new Vector<float>(255.5f);
        static readonly Vector<int> v255i = new Vector<int>(255);
        static readonly Vector<int> vZeroi = new Vector<int>(0);
        static readonly Vector<int> v256i = new Vector<int>(256);
        static readonly Vector<int> v65536i = new Vector<int>(65536);
        static readonly Vector<int> vAlphai = new Vector<int>(unchecked((int)0xFF000000));
        static Vector<float> vBloom = new Vector<float>(0f);
        static Vector<float> vGF = new Vector<float>(0.229f);

        // ── 快取緩衝區 ─────────────────────────────
        static float _cachedSigma = -1f;
        static int _cachedFrame = -1;
        static float* _weights;
        static int* _nearestY;
        static float* _boostRow;
        static uint* _curvTemp;
        static int* _curvMap;
        static int _cachedCurvW, _cachedCurvH;
        static float _cachedCurvK = -1f;
        static uint* _prevFrame;
        static bool _prevFrameValid;

        public static void Init()
        {
            if (_weights != null) Marshal.FreeHGlobal((IntPtr)_weights);
            if (_nearestY != null) Marshal.FreeHGlobal((IntPtr)_nearestY);
            if (_boostRow != null) Marshal.FreeHGlobal((IntPtr)_boostRow);
            _weights = (float*)Marshal.AllocHGlobal(DstH * sizeof(float));
            _nearestY = (int*)Marshal.AllocHGlobal(DstH * sizeof(int));
            _boostRow = (float*)Marshal.AllocHGlobal(DstH * sizeof(float));

            if (_curvTemp != null) Marshal.FreeHGlobal((IntPtr)_curvTemp);
            if (_curvMap != null) Marshal.FreeHGlobal((IntPtr)_curvMap);
            _curvTemp = (uint*)Marshal.AllocHGlobal(DstW * DstH * sizeof(uint));
            _curvMap = (int*)Marshal.AllocHGlobal(DstW * DstH * sizeof(int));

            if (_prevFrame != null) Marshal.FreeHGlobal((IntPtr)_prevFrame);
            _prevFrame = (uint*)Marshal.AllocHGlobal(DstW * DstH * sizeof(uint));
            _prevFrameValid = false;

            _cachedSigma = -1f; _cachedFrame = -1; _cachedCurvK = -1f;
        }

        static void ApplyProfile()
        {
            if (_analogOutput == (int)AnalogOutputMode.RF)
            { BeamSigma = RF_BeamSigma; BloomStrength = RF_BloomStrength; BrightnessBoost = RF_BrightnessBoost; }
            else if (_analogOutput == (int)AnalogOutputMode.SVideo)
            { BeamSigma = SV_BeamSigma; BloomStrength = SV_BloomStrength; BrightnessBoost = SV_BrightnessBoost; }
            else
            { BeamSigma = AV_BeamSigma; BloomStrength = AV_BloomStrength; BrightnessBoost = AV_BrightnessBoost; }
            vBloom = new Vector<float>(BloomStrength);
            vGF = new Vector<float>(Ntsc.GammaCoeff);
        }

        static void PrecomputeScanlineWeights()
        {
            bool needUpdate = (_cachedSigma != BeamSigma);
            if (InterlaceJitter)
            {
                int fc = _frameCount;
                if (_cachedFrame != fc) { _cachedFrame = fc; needUpdate = true; }
            }
            if (!needUpdate) return;
            _cachedSigma = BeamSigma;

            float jitter = InterlaceJitter ? ((_frameCount & 1) == 0 ? 0.25f : -0.25f) : 0f;
            float inv = 1f / (2f * BeamSigma * BeamSigma);
            int dstH = DstH;
            float bb = BrightnessBoost;
            float vs = VignetteStrength;

            // ★ 技巧 1：迴圈不變量外提 (Loop-Invariant Code Motion) & 消滅除法
            // 將除法轉為倒數乘法，並提早算出所有的固定常數
            float invDstH = 1f / dstH;                  // 用乘法取代除法
            float scaleY = (float)SrcH * invDstH;       // Y軸縮放比例
            float jitterOffset = jitter * scaleY;       // 預先乘上縮放比例的 Jitter 偏移
            float vs4 = vs * 4f;                        // 預先算出 Vignette 常數
            int maxNy = SrcH - 1;                       // 預先算出 Y 軸最大限制值

            Parallel.For(0, dstH, ty =>
            {
                // ★ 技巧 2：完美契合 FMA (融合乘加) 
                // 原始寫法：((float)ty + jitter) / dstH * SrcH
                // 優化寫法：純粹的 A * B + C，只需 1 個 CPU 指令週期
                float sy = ty * scaleY + jitterOffset;

                // ★ 技巧 3：RyuJIT CMOV 無分支邊界限制
                // 絕對安全的 Clamping，消滅 if 判斷
                int ny = Math.Max(0, Math.Min((int)(sy + 0.5f), maxNy));
                _nearestY[ty] = ny;

                // 高斯分佈權重 (這行的 Math.Exp 是最重的運算，但在平行化下效能尚可)
                float dy = sy - ny;
                _weights[ty] = (float)Math.Exp(-(dy * dy) * inv);

                // ★ 技巧 4：代數簡化暗角計算
                // 原始寫法：vs * 4f * vy * vy
                // 優化寫法：直接套用預先算好的 vs4，省下一次浮點數乘法
                float vy = ty * invDstH - 0.5f;
                _boostRow[ty] = bb * (1f - vs4 * vy * vy);
            });
        }

        static void PrecomputeCurvature()
        {
            int dstW = DstW, dstH = DstH; float k = CurvatureStrength;
            if (_cachedCurvK == k && _cachedCurvW == dstW && _cachedCurvH == dstH) return;
            _cachedCurvK = k; _cachedCurvW = dstW; _cachedCurvH = dstH;

            int* cm = _curvMap;

            // ★ 技巧 1：代數展開與常數外提
            float maxW = dstW - 1;
            float maxH = dstH - 1;
            float invW = 1f / maxW;
            float invH = 1f / maxH;

            // 預先算好四捨五入的常數基底 (0.5f * max + 0.5f)
            float baseW = maxW * 0.5f + 0.5f;
            float baseH = maxH * 0.5f + 0.5f;

            Parallel.For(0, dstH, ty =>
            {
                // Y 軸的常數提早算好
                float cy = ty * invH - 0.5f;
                float cy2 = cy * cy; // 提早算出平方
                int rowOff = ty * dstW;

                for (int tx = 0; tx < dstW; tx++)
                {
                    float cx = tx * invW - 0.5f;

                    // ★ 技巧 2：完美契合 FMA 的乘加運算
                    float f = 1f + k * (cx * cx + cy2);
                    int sx = (int)(cx * (f * maxW) + baseW);
                    int sy = (int)(cy * (f * maxH) + baseH);

                    // ★ 技巧 3：純位元 2D 邊界判斷 (Branchless Bounds Check)
                    // 提煉出 4 個方向的越界符號 (-1 表示越界，0 表示安全)
                    int outX = (sx >> 31) | ((int)maxW - sx) >> 31;
                    int outY = (sy >> 31) | ((int)maxH - sy) >> 31;

                    // 合併越界遮罩 (outMask: 越界=-1, 安全=0)
                    int outMask = outX | outY;

                    int validVal = sy * dstW + sx;

                    // 如果安全：(validVal & ~0) | 0   => validVal
                    // 如果越界：(validVal &  0) | -1  => -1
                    cm[rowOff + tx] = (validVal & ~outMask) | outMask;
                }
            });
        }

        static void ApplyHorizontalBlur()
        {
            if (HBeamSpread <= 0f) return;
            float* lb = Ntsc.linearBuffer;
            if (lb == null) return;

            float alpha = HBeamSpread * 0.5f;
            float center = 1f - HBeamSpread;
            const int kPlane = Ntsc.kPlane;

            Parallel.For(0, 3 * SrcH, i =>
            {
                int plane = i / SrcH;
                int row = i % SrcH; // 簡化：直接使用取餘數
                float* p = lb + plane * kPlane + row * SrcW;

                float prev = p[0];
                int limit = SrcW - 1; // 把終點提早 1 格

                // ★ 技巧 1：主迴圈剝離 (Loop Peeling)
                // 在這個迴圈裡，x + 1 絕對不會越界，所以直接拿 p[x + 1]，連 if 和 ? : 都免了！
                for (int x = 0; x < limit; x++)
                {
                    float cur = p[x];
                    float next = p[x + 1]; // 100% 安全，無分支
                    p[x] = prev * alpha + cur * center + next * alpha;
                    prev = cur;
                }

                // ★ 技巧 2：處理尾巴 (Epilogue)
                // 專門處理剛剛被剝離出來的「最後一個像素」
                // 在原本邏輯中，最後一個像素的 next 等於 cur
                float lastCur = p[limit];

                // 代數簡化：lastCur * center + lastCur * alpha 可以合併為 lastCur * (center + alpha)
                p[limit] = prev * alpha + lastCur * (center + alpha);
            });
        }

        public static unsafe void Render()
        {
            if (_analogScreenBuf == null || Ntsc.linearBuffer == null) return;
            if (_weights == null || _nearestY == null || _boostRow == null) return;

            PrecomputeScanlineWeights();
            ApplyHorizontalBlur();

            float bloom = BloomStrength;
            float* brow = _boostRow;
            float gc = Ntsc.GammaCoeff;
            float* lb = Ntsc.linearBuffer;
            uint* dst = _analogScreenBuf;
            float* wts = _weights;
            int* nyArr = _nearestY;
            const int kPlane = Ntsc.kPlane;

            int dstW = DstW;
            int dstH = DstH;
            bool is1to1 = (dstW == SrcW);
            bool isDouble = (dstW == SrcW * 2);
            int VS = Vector<float>.Count;

            bool doMask = ShadowMaskMode != MaskType.None && ShadowMaskStrength > 0f;
            bool doPhosphor = PhosphorDecay > 0f && _prevFrame != null && _prevFrameValid;
            bool doConv = ConvergenceStrength > 0f;
            bool doCurv = CurvatureStrength > 0f && _curvMap != null;

            uint udim = doMask ? (uint)((1f - ShadowMaskStrength) * 256f) : 0u;
            uint udec = doPhosphor ? (uint)(PhosphorDecay * 256f) : 0u;
            bool isSM = ShadowMaskMode == MaskType.ShadowMask;
            uint* prev = _prevFrame;


            float maxOff = ConvergenceStrength;
            float halfW = dstW * 0.5f;
            float invHW = halfW > 0f ? 1f / halfW : 0f;

            uint* renderTarget = doCurv ? _curvTemp : dst;

            Parallel.For(0, dstH, ty =>
            {
                float weight = wts[ty]; float omw = 1f - weight; float boost = brow[ty];
                uint* rowPtr = renderTarget + ty * dstW;
                int ny = nyArr[ty];
                float* lb_r = lb + ny * SrcW;
                float* lb_g = lb + kPlane + ny * SrcW;
                float* lb_b = lb + 2 * kPlane + ny * SrcW;

                int x = 0;

                // ★ 技巧 1：常數外提 (Loop-Invariant Code Motion)
                // 提早計算光暈與亮度的基礎乘數，減少迴圈內運算
                float constA = weight * boost;
                float constB = bloom * omw * boost;
                var vConstA = new Vector<float>(constA);
                var vConstB = new Vector<float>(constB);

                // 1. 生成 Scanline
                if (is1to1)
                {
#pragma warning disable CS8500
                    for (; x <= SrcW - VS; x += VS)
                    {
                        var vr = *(Vector<float>*)(lb_r + x); var vg = *(Vector<float>*)(lb_g + x); var vb = *(Vector<float>*)(lb_b + x);
                        var vBright = vr * v03 + vg * v059 + vb * v011; var vFw = vConstA + vBright * vConstB;
                        vr = Vector.Min(Vector.Max(vr * vFw, vZero), vOne); vg = Vector.Min(Vector.Max(vg * vFw, vZero), vOne); vb = Vector.Min(Vector.Max(vb * vFw, vZero), vOne);
                        vr += vGF * vr * (vr - vOne); vg += vGF * vg * (vg - vOne); vb += vGF * vb * (vb - vOne);
                        var viR = Vector.Max(vZeroi, Vector.Min(Vector.ConvertToInt32(vr * v255_5f), v255i)); var viG = Vector.Max(vZeroi, Vector.Min(Vector.ConvertToInt32(vg * v255_5f), v255i)); var viB = Vector.Max(vZeroi, Vector.Min(Vector.ConvertToInt32(vb * v255_5f), v255i));
                        *(Vector<int>*)(rowPtr + x) = Vector.BitwiseOr(Vector.BitwiseOr(viB, viG * v256i), Vector.BitwiseOr(viR * v65536i, vAlphai));
                    }
#pragma warning restore CS8500
                }
                else if (isDouble)
                {
                    int srcX = 0;
#pragma warning disable CS8500
                    for (; srcX <= SrcW - VS; srcX += VS)
                    {
                        var vr = *(Vector<float>*)(lb_r + srcX); var vg = *(Vector<float>*)(lb_g + srcX); var vb = *(Vector<float>*)(lb_b + srcX);
                        var vBright = vr * v03 + vg * v059 + vb * v011; var vFw = vConstA + vBright * vConstB;
                        vr = Vector.Min(Vector.Max(vr * vFw, vZero), vOne); vg = Vector.Min(Vector.Max(vg * vFw, vZero), vOne); vb = Vector.Min(Vector.Max(vb * vFw, vZero), vOne);
                        vr += vGF * vr * (vr - vOne); vg += vGF * vg * (vg - vOne); vb += vGF * vb * (vb - vOne);
                        var viR = Vector.Max(vZeroi, Vector.Min(Vector.ConvertToInt32(vr * v255_5f), v255i)); var viG = Vector.Max(vZeroi, Vector.Min(Vector.ConvertToInt32(vg * v255_5f), v255i)); var viB = Vector.Max(vZeroi, Vector.Min(Vector.ConvertToInt32(vb * v255_5f), v255i));
                        var packed = Vector.BitwiseOr(Vector.BitwiseOr(viB, viG * v256i), Vector.BitwiseOr(viR * v65536i, vAlphai));
                        for (int k = 0; k < VS; k++)
                        {
                            uint px = ((uint*)&packed)[k]; int outX = (srcX + k) * 2;
                            rowPtr[outX] = px; rowPtr[outX + 1] = px;
                        }
                    }
#pragma warning restore CS8500
                    for (; srcX < SrcW; srcX++)
                    {
                        float r = lb_r[srcX], g = lb_g[srcX], b = lb_b[srcX]; float bright = r * 0.3f + g * 0.59f + b * 0.11f;
                        float fw = constA + bright * constB;

                        // ★ 技巧 2：無分支硬體浮點數限制 (Branchless Float Clamping)
                        r = Math.Max(0f, Math.Min(r * fw, 1f));
                        g = Math.Max(0f, Math.Min(g * fw, 1f));
                        b = Math.Max(0f, Math.Min(b * fw, 1f));

                        r += gc * r * (r - 1f); g += gc * g * (g - 1f); b += gc * b * (b - 1f);

                        // ★ 技巧 3：數學邏輯消滅多餘的 255 檢查
                        int ri = (int)(r * 255.5f); int gi = (int)(g * 255.5f); int bi = (int)(b * 255.5f);
                        uint px = (uint)(bi | (gi << 8) | (ri << 16) | 0xFF000000u);
                        rowPtr[srcX * 2] = px; rowPtr[srcX * 2 + 1] = px;
                    }
                    x = dstW;
                }
                else
                {
                    int fpScale = (SrcW << 16) / dstW;
                    int maxSrcX = SrcW - 1;
                    for (; x < dstW; x++)
                    {
                        int fp = x * fpScale; int srcX = fp >> 16; float t = (fp & 0xFFFF) * (1f / 65536f);

                        // ★ 技巧 4：無分支邊界限制
                        int srcX1 = Math.Min(srcX + 1, maxSrcX);

                        float r = lb_r[srcX] + t * (lb_r[srcX1] - lb_r[srcX]);
                        float g = lb_g[srcX] + t * (lb_g[srcX1] - lb_g[srcX]);
                        float b = lb_b[srcX] + t * (lb_b[srcX1] - lb_b[srcX]);
                        float bright = r * 0.3f + g * 0.59f + b * 0.11f;
                        float fw = constA + bright * constB;

                        r = Math.Max(0f, Math.Min(r * fw, 1f));
                        g = Math.Max(0f, Math.Min(g * fw, 1f));
                        b = Math.Max(0f, Math.Min(b * fw, 1f));

                        r += gc * r * (r - 1f); g += gc * g * (g - 1f); b += gc * b * (b - 1f);

                        int ri = (int)(r * 255.5f); int gi = (int)(g * 255.5f); int bi = (int)(b * 255.5f);
                        rowPtr[x] = (uint)(bi | (gi << 8) | (ri << 16) | 0xFF000000u);
                    }
                }

                for (; x < dstW; x++)
                {
                    float r = lb_r[x], g = lb_g[x], b = lb_b[x]; float bright = r * 0.3f + g * 0.59f + b * 0.11f;
                    float fw = constA + bright * constB;

                    r = Math.Max(0f, Math.Min(r * fw, 1f));
                    g = Math.Max(0f, Math.Min(g * fw, 1f));
                    b = Math.Max(0f, Math.Min(b * fw, 1f));

                    r += gc * r * (r - 1f); g += gc * g * (g - 1f); b += gc * b * (b - 1f);

                    int ri = (int)(r * 255.5f); int gi = (int)(g * 255.5f); int bi = (int)(b * 255.5f);
                    rowPtr[x] = (uint)(bi | (gi << 8) | (ri << 16) | 0xFF000000u);
                }

                // 2. 核心融合：SWAR LUT 加速
                if (doMask && doPhosphor) ProcessRowMaskPhosphor_SWAR(rowPtr, prev + ty * dstW, ty, dstW, udim, udec, isSM);
                else if (doMask) ProcessRowMask_SWAR(rowPtr, ty, dstW, udim, isSM);
                else if (doPhosphor) ProcessRowPhosphor_SWAR(rowPtr, prev + ty * dstW, dstW, udec);

                if (doConv && !doCurv)
                {
                    uint* tempRow = stackalloc uint[dstW];
                    Buffer.MemoryCopy(rowPtr, tempRow, dstW * sizeof(uint), dstW * sizeof(uint));
                    ProcessRowConvergence(rowPtr, tempRow, dstW, maxOff, halfW, invHW);
                }
            });

            if (PhosphorDecay > 0f && _prevFrame != null && !_prevFrameValid)
            {
                int bytes2 = DstW * DstH * sizeof(uint);
                Buffer.MemoryCopy(renderTarget, _prevFrame, bytes2, bytes2);
                _prevFrameValid = true;
            }

            if (doCurv) ApplyFullFrameCurvatureAndConvergence();
        }

        // ════════════════════════════════════════════════════════════════════
        // ★ Branchless SWAR Inline 函數 ★
        // ════════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ProcessRowMask_SWAR(uint* row, int ty, int dstW, uint udim, bool isSM)
        {
            // ★ 正確的 SWAR 寫法：均勻衰減全通道 → mask 還原保留通道
            // R+B 用 SWAR 一次乘法處理兩通道，G 單獨處理，再用位元遮罩還原要保留的通道
            int phase = (isSM && (ty & 1) != 0) ? 1 : 0;
            int tx = 0;

            // Prologue: align to phase 0
            if (phase == 1 && tx < dstW)
            {
                // Phase 1: keep G, dim R+B
                { uint px = row[tx]; uint dimRB = ((px & 0x00FF00FFu) * udim >> 8) & 0x00FF00FFu; uint dimG = (((px >> 8) & 0xFFu) * udim >> 8) << 8; row[tx] = 0xFF000000u | (px & 0x0000FF00u) | dimRB; }
                tx++;
                if (tx < dstW)
                {
                    // Phase 2: keep B, dim R+G
                    uint px = row[tx]; uint dimRB = ((px & 0x00FF00FFu) * udim >> 8) & 0x00FF00FFu; uint dimG = (((px >> 8) & 0xFFu) * udim >> 8) << 8; row[tx] = 0xFF000000u | (px & 0x000000FFu) | (dimRB & 0x00FF0000u) | dimG;
                    tx++;
                }
            }

            // Main 3x unrolled (phase 0 → 1 → 2)
            int limit = dstW - 2;
            for (; tx < limit; tx += 3)
            {
                // Phase 0: keep R, dim G+B — SWAR 均勻衰減 R+B，還原 R 從原始值
                { uint px = row[tx]; uint dimRB = ((px & 0x00FF00FFu) * udim >> 8) & 0x00FF00FFu; uint dimG = (((px >> 8) & 0xFFu) * udim >> 8) << 8; row[tx] = 0xFF000000u | (px & 0x00FF0000u) | dimG | (dimRB & 0xFFu); }
                // Phase 1: keep G, dim R+B — SWAR 均勻衰減 R+B，還原 G 從原始值
                { uint px = row[tx + 1]; uint dimRB = ((px & 0x00FF00FFu) * udim >> 8) & 0x00FF00FFu; row[tx + 1] = 0xFF000000u | (px & 0x0000FF00u) | dimRB; }
                // Phase 2: keep B, dim R+G — SWAR 均勻衰減 R+B，還原 B 從原始值
                { uint px = row[tx + 2]; uint dimRB = ((px & 0x00FF00FFu) * udim >> 8) & 0x00FF00FFu; uint dimG = (((px >> 8) & 0xFFu) * udim >> 8) << 8; row[tx + 2] = 0xFF000000u | (px & 0x000000FFu) | (dimRB & 0x00FF0000u) | dimG; }
            }

            // Epilogue
            if (tx < dstW)
            {
                { uint px = row[tx]; uint dimRB = ((px & 0x00FF00FFu) * udim >> 8) & 0x00FF00FFu; uint dimG = (((px >> 8) & 0xFFu) * udim >> 8) << 8; row[tx] = 0xFF000000u | (px & 0x00FF0000u) | dimG | (dimRB & 0xFFu); }
                tx++;
                if (tx < dstW)
                { uint px = row[tx]; uint dimRB = ((px & 0x00FF00FFu) * udim >> 8) & 0x00FF00FFu; row[tx] = 0xFF000000u | (px & 0x0000FF00u) | dimRB; }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ProcessRowMaskPhosphor_SWAR(uint* row, uint* prw, int ty, int dstW, uint udim, uint udec, bool isSM)
        {
            // ★ 正確的 SWAR 寫法：均勻衰減 + mask 還原 + SWAR 磷光衰減
            // 蔭罩：SWAR 均勻 udim 衰減 R+B → mask 還原保留通道
            // 磷光：SWAR 均勻 udec 衰減（已正確，保持不變）
            // ★ Positional Math.Max：通道留在原始位元位置直接比較，JIT 編譯為 CMOV（無分支）
            //   省掉 6 次 shift-extract + 6 次 local + 3 次 branch + 3 次 shift-reassemble
            int phase = (isSM && (ty & 1) != 0) ? 1 : 0;
            int tx = 0;

            // Prologue: align to phase 0
            if (phase == 1 && tx < dstW)
            {
                // Phase 1: keep G, dim R+B
                {
                    uint px = row[tx], prv = prw[tx];
                    uint dimRB = ((px & 0x00FF00FFu) * udim >> 8) & 0x00FF00FFu;
                    uint masked = (px & 0x0000FF00u) | dimRB;
                    uint dec_rb = ((prv & 0x00FF00FFu) * udec >> 8) & 0x00FF00FFu; uint dec_g = (((prv >> 8) & 0xFFu) * udec >> 8) << 8;
                    uint res = 0xFF000000u | Math.Max(masked & 0x00FF0000u, dec_rb & 0x00FF0000u) | Math.Max(masked & 0x0000FF00u, dec_g) | Math.Max(masked & 0x000000FFu, dec_rb & 0x000000FFu);
                    row[tx] = res; prw[tx] = res;
                }
                tx++;
                if (tx < dstW)
                {
                    // Phase 2: keep B, dim R+G
                    uint px = row[tx], prv = prw[tx];
                    uint dimRB = ((px & 0x00FF00FFu) * udim >> 8) & 0x00FF00FFu; uint dimG = (((px >> 8) & 0xFFu) * udim >> 8) << 8;
                    uint masked = (px & 0x000000FFu) | (dimRB & 0x00FF0000u) | dimG;
                    uint dec_rb = ((prv & 0x00FF00FFu) * udec >> 8) & 0x00FF00FFu; uint dec_g2 = (((prv >> 8) & 0xFFu) * udec >> 8) << 8;
                    uint res = 0xFF000000u | Math.Max(masked & 0x00FF0000u, dec_rb & 0x00FF0000u) | Math.Max(masked & 0x0000FF00u, dec_g2) | Math.Max(masked & 0x000000FFu, dec_rb & 0x000000FFu);
                    row[tx] = res; prw[tx] = res;
                    tx++;
                }
            }

            // Main 3x unrolled (phase 0 → 1 → 2)
            int limit = dstW - 2;
            for (; tx < limit; tx += 3)
            {
                // Phase 0: keep R, dim G+B
                {
                    uint px = row[tx], prv = prw[tx];
                    uint dimRB = ((px & 0x00FF00FFu) * udim >> 8) & 0x00FF00FFu; uint dimG = (((px >> 8) & 0xFFu) * udim >> 8) << 8;
                    uint masked = (px & 0x00FF0000u) | dimG | (dimRB & 0xFFu);
                    uint dec_rb = ((prv & 0x00FF00FFu) * udec >> 8) & 0x00FF00FFu; uint dec_g = (((prv >> 8) & 0xFFu) * udec >> 8) << 8;
                    uint res = 0xFF000000u | Math.Max(masked & 0x00FF0000u, dec_rb & 0x00FF0000u) | Math.Max(dimG, dec_g) | Math.Max(masked & 0x000000FFu, dec_rb & 0x000000FFu);
                    row[tx] = res; prw[tx] = res;
                }
                // Phase 1: keep G, dim R+B
                {
                    uint px = row[tx + 1], prv = prw[tx + 1];
                    uint dimRB = ((px & 0x00FF00FFu) * udim >> 8) & 0x00FF00FFu;
                    uint masked = (px & 0x0000FF00u) | dimRB;
                    uint dec_rb = ((prv & 0x00FF00FFu) * udec >> 8) & 0x00FF00FFu; uint dec_g = (((prv >> 8) & 0xFFu) * udec >> 8) << 8;
                    uint res = 0xFF000000u | Math.Max(masked & 0x00FF0000u, dec_rb & 0x00FF0000u) | Math.Max(masked & 0x0000FF00u, dec_g) | Math.Max(masked & 0x000000FFu, dec_rb & 0x000000FFu);
                    row[tx + 1] = res; prw[tx + 1] = res;
                }
                // Phase 2: keep B, dim R+G
                {
                    uint px = row[tx + 2], prv = prw[tx + 2];
                    uint dimRB = ((px & 0x00FF00FFu) * udim >> 8) & 0x00FF00FFu; uint dimG = (((px >> 8) & 0xFFu) * udim >> 8) << 8;
                    uint masked = (px & 0x000000FFu) | (dimRB & 0x00FF0000u) | dimG;
                    uint dec_rb = ((prv & 0x00FF00FFu) * udec >> 8) & 0x00FF00FFu; uint dec_g = (((prv >> 8) & 0xFFu) * udec >> 8) << 8;
                    uint res = 0xFF000000u | Math.Max(masked & 0x00FF0000u, dec_rb & 0x00FF0000u) | Math.Max(dimG, dec_g) | Math.Max(masked & 0x000000FFu, dec_rb & 0x000000FFu);
                    row[tx + 2] = res; prw[tx + 2] = res;
                }
            }

            // Epilogue
            if (tx < dstW)
            {
                // Phase 0: keep R, dim G+B
                uint px = row[tx], prv = prw[tx];
                uint dimRB = ((px & 0x00FF00FFu) * udim >> 8) & 0x00FF00FFu; uint dimG = (((px >> 8) & 0xFFu) * udim >> 8) << 8;
                uint masked = (px & 0x00FF0000u) | dimG | (dimRB & 0xFFu);
                uint dec_rb = ((prv & 0x00FF00FFu) * udec >> 8) & 0x00FF00FFu; uint dec_g = (((prv >> 8) & 0xFFu) * udec >> 8) << 8;
                uint res = 0xFF000000u | Math.Max(masked & 0x00FF0000u, dec_rb & 0x00FF0000u) | Math.Max(dimG, dec_g) | Math.Max(masked & 0x000000FFu, dec_rb & 0x000000FFu);
                row[tx] = res; prw[tx] = res;
                tx++;
                if (tx < dstW)
                {
                    // Phase 1: keep G, dim R+B
                    px = row[tx]; prv = prw[tx];
                    dimRB = ((px & 0x00FF00FFu) * udim >> 8) & 0x00FF00FFu;
                    masked = (px & 0x0000FF00u) | dimRB;
                    dec_rb = ((prv & 0x00FF00FFu) * udec >> 8) & 0x00FF00FFu; dec_g = (((prv >> 8) & 0xFFu) * udec >> 8) << 8;
                    res = 0xFF000000u | Math.Max(masked & 0x00FF0000u, dec_rb & 0x00FF0000u) | Math.Max(masked & 0x0000FF00u, dec_g) | Math.Max(masked & 0x000000FFu, dec_rb & 0x000000FFu);
                    row[tx] = res; prw[tx] = res;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ProcessRowPhosphor_SWAR(uint* row, uint* prw, int dstW, uint udec)
        {
            for (int tx = 0; tx < dstW; tx++)
            {
                uint px = row[tx];
                uint prv = prw[tx];

                // 1. SWAR 衰減乘法 (保持不變，這已經是最佳解)
                uint dec_RB = (((prv & 0x00FF00FFu) * udec) >> 8) & 0x00FF00FFu;
                uint dec_G = (((prv & 0x0000FF00u) * udec) >> 8) & 0x0000FF00u;

                // 2. 原地分離頻道 (In-Place Channel Isolation)
                // 直接用遮罩濾出顏色，絕對不使用位移！
                uint px_R = px & 0x00FF0000u;
                uint px_G = px & 0x0000FF00u;
                uint px_B = px & 0x000000FFu;

                uint dec_R = dec_RB & 0x00FF0000u;
                uint dec_B = dec_RB & 0x000000FFu;
                // dec_G 已經是 0x0000GG00 的乾淨狀態，不需要再 Mask

                // 3. 原地無分支取最大值 (Branchless CMOV Max)
                uint res_R = Math.Max(px_R, dec_R);
                uint res_G = Math.Max(px_G, dec_G);
                uint res_B = Math.Max(px_B, dec_B);

                // 4. 一行流組裝 (直接 OR，沒有任何左移操作)
                uint finalColor = 0xFF000000u | res_R | res_G | res_B;

                row[tx] = finalColor;
                prw[tx] = finalColor;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ProcessRowConvergence(uint* dst, uint* src, int dstW, float maxOff, float halfW, float invHW)
        {
            // 1. 迴圈外預計算常數 (Loop-Invariant Code Motion)
            float step = invHW * maxOff;
            float baseOffset = -halfW * step + 1024.5f; // 預先扣除 halfW 並加上四捨五入常數
            int maxIdx = dstW - 1;

            for (int tx = 0; tx < dstW; tx++)
            {
                // 2. 完美契合 FMA (融合乘加) 的單行計算
                int ioff = (int)(tx * step + baseOffset) - 1024;

                // 3. RyuJIT CMOV 無分支邊界限制 (Branchless Clamping)
                int rxR = Math.Max(0, Math.Min(tx + ioff, maxIdx));
                int rxB = Math.Max(0, Math.Min(tx - ioff, maxIdx));

                // 4. SWAR 零位移、零轉型的極速像素組裝 (Zero-Shift Assembly)
                dst[tx] = (src[rxB] & 0x000000FFu) | (src[tx] & 0x0000FF00u) | (src[rxR] & 0x00FF0000u) | 0xFF000000u;
            }
        }

        static void ApplyFullFrameCurvatureAndConvergence()
        {
            PrecomputeCurvature();
            int dstW = DstW, dstH = DstH;
            uint* dst = _analogScreenBuf;
            uint* tmp = _curvTemp;
            int* map = _curvMap;

            bool doConv = ConvergenceStrength > 0f;

            // ★ 技巧 1：分支外提 (Branch Hoisting)
            // 判斷一次就好，不要在幾十萬個像素迴圈裡重複問 CPU 同一個問題
            if (doConv)
            {
                // ★ 技巧 2：迴圈不變量外提 (Loop-Invariant Code Motion)
                float maxOff = ConvergenceStrength;
                float halfW = dstW * 0.5f;
                float invHW = halfW > 0f ? 1f / halfW : 0f;
                float step = invHW * maxOff;
                float baseOffset = -halfW * step + 1024.5f;
                int maxIdx = dstW - 1;

                Parallel.For(0, dstH, ty =>
                {
                    int rowOff = ty * dstW;
                    for (int tx = 0; tx < dstW; tx++)
                    {
                        int dstIdx = rowOff + tx;
                        int srcIdx = map[dstIdx];

                        // 這個分支必須留著，用來保護記憶體越界 (Out-of-bounds Guard)
                        if (srcIdx < 0) { dst[dstIdx] = 0xFF000000u; continue; }

                        // 利用整數除法的特性，快速算出列首位址與行座標
                        int srcTx = srcIdx % dstW;
                        int srcRowOff = srcIdx - srcTx;

                        // ★ 技巧 3：融合乘加 (FMA) 與無分支四捨五入
                        int ioff = (int)(srcTx * step + baseOffset) - 1024;

                        // ★ 技巧 4：RyuJIT CMOV 無分支邊界限制
                        int rxR = Math.Max(0, Math.Min(srcTx + ioff, maxIdx));
                        int rxB = Math.Max(0, Math.Min(srcTx - ioff, maxIdx));

                        // ★ 技巧 5：SWAR 零位移、零轉型的極速像素組裝
                        dst[dstIdx] = (tmp[srcRowOff + rxB] & 0x000000FFu) |
                                      (tmp[srcRowOff + srcTx] & 0x0000FF00u) |
                                      (tmp[srcRowOff + rxR] & 0x00FF0000u) |
                                      0xFF000000u;
                    }
                });
            }
            else
            {
                // 若玩家沒開 Convergence，跑這條最乾淨的極速迴圈
                Parallel.For(0, dstH, ty =>
                {
                    int rowOff = ty * dstW;
                    for (int tx = 0; tx < dstW; tx++)
                    {
                        int dstIdx = rowOff + tx;
                        int srcIdx = map[dstIdx];

                        if (srcIdx < 0) dst[dstIdx] = 0xFF000000u;
                        else dst[dstIdx] = tmp[srcIdx];
                    }
                });
            }
        }
    }
}