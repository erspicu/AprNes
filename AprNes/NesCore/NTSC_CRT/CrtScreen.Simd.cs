// ════════════════════════════════════════════════════════════════════════
// CrtScreen.Simd.cs — .NET 10 SIMD-accelerated CRT pipeline
// ════════════════════════════════════════════════════════════════════════
// Fork origin: CrtScreen.cs @ commit 8a5f99d (pre-parallel-demod baseline)
//
// Role in the acceleration ladder:
//   • CrtScreen.cs           → net48 baseline (frozen, maintenance-only)
//   • CrtScreen.Simd.cs      → THIS FILE. .NET 10 SIMD path.
//                              Avx2.GatherVector256, Vector256<T>,
//                              [SkipLocalsInit], Fma explicit, etc.
//   • CrtScreen.Gpu.cs       → future. Avalonia/SkiaSharp SKSL shader
//                              pipeline replaces the CPU pipeline wholesale.
//
// Algorithm correctness lives in the shared Ntsc.cs — bug fixes there
// auto-propagate to all forks. CrtScreen's fork divergence is strictly
// about "how to render", not "what to render".
//
// Build configuration:
//   AprNes.csproj (net48)         → compiles CrtScreen.cs, ignores this file
//   AprNesAvalonia.csproj (net10) → compiles this file, excludes CrtScreen.cs
//   Future GPU build              → msbuild /p:CrtImpl=Gpu selects Gpu.cs
// ════════════════════════════════════════════════════════════════════════
using System;
using System.Numerics;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using static AprNes.NesCore;
using CrtMaskType = AprNes.NesCore.CrtMaskType;

namespace AprNes
{
    // ============================================================
    // CRT 電視光學模擬器（Stage 2） - SIMD / AVX2 版（.NET 10）
    // Phase 1: refactored to static class; dispatched via NesCore.Crt_Render
    // Shared state (config, dimensions, profiles) lives in CrtScreen.Shared.cs
    // ============================================================
    unsafe internal static class CrtScreenSimd
    {
        // ── SIMD 常數向量 ────────────────────────────
        static readonly Vector<float> vOne = new Vector<float>(1f);
        static readonly Vector<float> vZero = new Vector<float>(0f);
        static readonly Vector<float> v03 = new Vector<float>(0.3f);
        static readonly Vector<float> v059 = new Vector<float>(0.59f);
        static readonly Vector<float> v011 = new Vector<float>(0.11f);
        static readonly Vector<float> v255_0f = new Vector<float>(255.0f);
        static readonly Vector<int> vZeroi = new Vector<int>(0);
        static readonly Vector<int> v256i = new Vector<int>(256);
        static readonly Vector<int> v65536i = new Vector<int>(65536);
        static readonly Vector<int> vAlphai = new Vector<int>(unchecked((int)0xFF000000));
        static Vector<float> vBloom = new Vector<float>(0f);
        static Vector<float> vGF = new Vector<float>(0.229f);
        static Vector<float> v1_minus_GF = new Vector<float>(1f - 0.229f);

        // ── 快取緩衝區 ─────────────────────────────
        static float _cachedSigma = -1f;
        static int _cachedFrame = -1;
        static float* _weights;
        static int* _nearestY;
        static float* _boostRow;
        static uint* _curvTemp;
        static int* _curvMap;
        static int* _curvMapCol;
        static int _cachedCurvW, _cachedCurvH;
        static float _cachedCurvK = -1f;
        static uint* _prevFrame;
        static bool _prevFrameValid;

        // Per-thread row scratch for ApplyHorizontalBlur (replaces per-row stackalloc).
        // Allocated once per worker thread, lives until process exit.
        [ThreadStatic] static float* tls_crtBlurRow;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void EnsureCrtBlurScratch()
        {
            if (tls_crtBlurRow != null) return;
            tls_crtBlurRow = (float*)NesCore.AllocUnmanaged(Crt_SrcW * sizeof(float));
        }

        internal static void Init()
        {
            System.Console.WriteLine("[CRT] backend = SIMD (CrtScreen.Simd.cs, Vector256/Avx2 intrinsics, .NET 10)");
            if (_weights != null) NesCore.FreeUnmanaged((IntPtr)_weights);
            if (_nearestY != null) NesCore.FreeUnmanaged((IntPtr)_nearestY);
            if (_boostRow != null) NesCore.FreeUnmanaged((IntPtr)_boostRow);
            _weights = (float*)NesCore.AllocUnmanaged(Crt_DstH * sizeof(float));
            _nearestY = (int*)NesCore.AllocUnmanaged(Crt_DstH * sizeof(int));
            _boostRow = (float*)NesCore.AllocUnmanaged(Crt_DstH * sizeof(float));

            if (_curvTemp != null) NesCore.FreeUnmanaged((IntPtr)_curvTemp);
            if (_curvMap != null) NesCore.FreeUnmanaged((IntPtr)_curvMap);
            if (_curvMapCol != null) NesCore.FreeUnmanaged((IntPtr)_curvMapCol);
            _curvTemp = (uint*)NesCore.AllocUnmanaged(Crt_DstW * Crt_DstH * sizeof(uint));
            _curvMap = (int*)NesCore.AllocUnmanaged(Crt_DstW * Crt_DstH * sizeof(int));
            _curvMapCol = (int*)NesCore.AllocUnmanaged(Crt_DstW * Crt_DstH * sizeof(int));

            if (_prevFrame != null) NesCore.FreeUnmanaged((IntPtr)_prevFrame);
            _prevFrame = (uint*)NesCore.AllocUnmanaged(Crt_DstW * Crt_DstH * sizeof(uint));
            _prevFrameValid = false;

            _cachedSigma = -1f; _cachedFrame = -1; _cachedCurvK = -1f;
        }

        internal static void ApplyProfile()
        {
            if (crt_analogOutput == (int)AnalogOutputMode.RF)
            { BeamSigma = RF_BeamSigma; BloomStrength = RF_BloomStrength; BrightnessBoost = RF_BrightnessBoost; }
            else if (crt_analogOutput == (int)AnalogOutputMode.SVideo)
            { BeamSigma = SV_BeamSigma; BloomStrength = SV_BloomStrength; BrightnessBoost = SV_BrightnessBoost; }
            else
            { BeamSigma = AV_BeamSigma; BloomStrength = AV_BloomStrength; BrightnessBoost = AV_BrightnessBoost; }
            vBloom = new Vector<float>(BloomStrength);
            vGF = new Vector<float>(GammaCoeff);
            v1_minus_GF = new Vector<float>(1f - GammaCoeff);
        }

        static void PrecomputeScanlineWeights()
        {
            bool needUpdate = (_cachedSigma != BeamSigma);
            if (InterlaceJitter)
            {
                int fc = crt_frameCount;
                if (_cachedFrame != fc) { _cachedFrame = fc; needUpdate = true; }
            }
            if (!needUpdate) return;
            _cachedSigma = BeamSigma;

            float jitter = InterlaceJitter ? ((crt_frameCount & 1) == 0 ? 0.25f : -0.25f) : 0f;
            float inv = 1f / (2f * BeamSigma * BeamSigma);
            int dstH = Crt_DstH;
            float bb = BrightnessBoost;
            float vs = VignetteStrength;

            // ★ 技巧 1：迴圈不變量外提 (Loop-Invariant Code Motion) & 消滅除法
            // 將除法轉為倒數乘法，並提早算出所有的固定常數
            float invCrt_DstH = 1f / dstH;                  // 用乘法取代除法
            float scaleY = (float)Crt_SrcH * invCrt_DstH;       // Y軸縮放比例
            float jitterOffset = jitter * scaleY;       // 預先乘上縮放比例的 Jitter 偏移
            float vs4 = vs * 4f;                        // 預先算出 Vignette 常數
            int maxNy = Crt_SrcH - 1;                       // 預先算出 Y 軸最大限制值

            Parallel.For(0, dstH, ty =>
            {
                // 源行映射（不含 jitter，確保 _nearestY 幀間穩定，不會整行跳動）
                float sy = ty * scaleY;
                int ny = Math.Max(0, Math.Min((int)(sy + 0.5f), maxNy));
                _nearestY[ty] = ny;

                // Jitter 僅影響光束權重（sub-pixel 亮度偏移，模擬隔行掃描）
                float dy = sy + jitterOffset - ny;
                _weights[ty] = (float)Math.Exp(-(dy * dy) * inv);

                // ★ 技巧 4：代數簡化暗角計算
                // 原始寫法：vs * 4f * vy * vy
                // 優化寫法：直接套用預先算好的 vs4，省下一次浮點數乘法
                float vy = ty * invCrt_DstH - 0.5f;
                _boostRow[ty] = bb * (1f - vs4 * vy * vy);
            });
        }

        static void PrecomputeCurvature()
        {
            int dstW = Crt_DstW, dstH = Crt_DstH; float k = CurvatureStrength;
            if (_cachedCurvK == k && _cachedCurvW == dstW && _cachedCurvH == dstH) return;
            _cachedCurvK = k; _cachedCurvW = dstW; _cachedCurvH = dstH;

            int* cm = _curvMap;
            int* cmCol = _curvMapCol;

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
                    cmCol[rowOff + tx] = (sx & ~outMask) | outMask;
                }
            });
        }

        static void ApplyHorizontalBlur()
        {
            if (HBeamSpread <= 0f) return;
            float* lb = linearBuffer;
            if (lb == null) return;

            float alpha = HBeamSpread * 0.5f;
            float center = 1f - HBeamSpread;

            var vAlpha = new Vector<float>(alpha);
            var vCenter = new Vector<float>(center);
            int VS = Vector<float>.Count;

            Parallel.For(0, 3 * Crt_SrcH, i =>
            {
                int plane = i / Crt_SrcH;
                int row = i % Crt_SrcH;
                float* p = lb + plane * kPlane + row * Crt_SrcW;

                // Snapshot row to a per-thread scratch buffer — breaks the in-place
                // Read-After-Write hazard that blocked SIMD in the scalar version.
                // ThreadStatic unmanaged allocation, lives for process lifetime.
                EnsureCrtBlurScratch();
                float* src = tls_crtBlurRow;
                Buffer.MemoryCopy(p, src, Crt_SrcW * sizeof(float), Crt_SrcW * sizeof(float));

                // Left edge: original used prev=p[0] on first iter → src[0]*(α+c) + src[1]*α
                p[0] = src[0] * (alpha + center) + src[1] * alpha;

                int x = 1;
                if (Vector.IsHardwareAccelerated)
                {
                    // SIMD 3-tap: all three taps are unaligned reads from `src`,
                    // written to disjoint positions in `p`. On AVX2 this is
                    // 8 pixels per iteration (vmovups x3 + fma pattern).
                    int vecEnd = Crt_SrcW - 1 - VS;
                    for (; x <= vecEnd; x += VS)
                    {
                        var vPrev = *(Vector<float>*)(src + x - 1);
                        var vCur  = *(Vector<float>*)(src + x);
                        var vNext = *(Vector<float>*)(src + x + 1);
                        *(Vector<float>*)(p + x) = vPrev * vAlpha + vCur * vCenter + vNext * vAlpha;
                    }
                }

                // Scalar tail (up to VS-1 pixels)
                int limitMinus1 = Crt_SrcW - 1;
                for (; x < limitMinus1; x++)
                    p[x] = src[x - 1] * alpha + src[x] * center + src[x + 1] * alpha;

                // Right edge: original epilogue — lastCur * (center + alpha) collapses the
                // duplicated tail term since next would equal cur at the boundary.
                p[limitMinus1] = src[limitMinus1 - 1] * alpha + src[limitMinus1] * (center + alpha);
            });
        }

        internal static unsafe void Render()
        {
            if (crt_analogScreenBuf == null || linearBuffer == null) return;
            if (_weights == null || _nearestY == null || _boostRow == null) return;

            PrecomputeScanlineWeights();
            ApplyHorizontalBlur();

            float bloom = BloomStrength;
            float* brow = _boostRow;
            float gc = GammaCoeff;
            float* lb = linearBuffer;
            uint* dst = crt_analogScreenBuf;
            float* wts = _weights;
            int* nyArr = _nearestY;
            // kPlane defined in Ntsc partial (kOutW * kSrcH)

            int dstW = Crt_DstW;
            int dstH = Crt_DstH;
            bool is1to1 = (dstW == Crt_SrcW);
            bool isDouble = (dstW == Crt_SrcW * 2);
            int VS = Vector<float>.Count;

            bool doMask = ShadowMaskMode != CrtMaskType.None && ShadowMaskStrength > 0f;
            bool doPhosphor = PhosphorDecay > 0f && _prevFrame != null && _prevFrameValid;
            bool doConv = ConvergenceStrength > 0f;
            bool doCurv = CurvatureStrength > 0f && _curvMap != null;

            uint udim = doMask ? (uint)((1f - ShadowMaskStrength) * 256f) : 0u;
            uint udec = doPhosphor ? (uint)(PhosphorDecay * 256f) : 0u;
            bool isSM = ShadowMaskMode == CrtMaskType.ShadowMask;
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
                float* lb_r = lb + ny * Crt_SrcW;
                float* lb_g = lb + kPlane + ny * Crt_SrcW;
                float* lb_b = lb + 2 * kPlane + ny * Crt_SrcW;

                float constA = weight * boost;
                float constB = bloom * omw * boost;
                var vConstA = new Vector<float>(constA);
                var vConstB = new Vector<float>(constB);
                int x = 0;

                // 1. 生成 Scanline (極度簡化的路徑)
                if (is1to1)
                {
#pragma warning disable CS8500
                    for (; x <= Crt_SrcW - VS; x += VS)
                    {
                        *(Vector<int>*)(rowPtr + x) = ProcessPixelVector(
                            *(Vector<float>*)(lb_r + x), *(Vector<float>*)(lb_g + x), *(Vector<float>*)(lb_b + x),
                            vConstA, vConstB);
                    }
#pragma warning restore CS8500
                    for (; x < Crt_SrcW; x++)
                    {
                        rowPtr[x] = ProcessPixelScalar(lb_r[x], lb_g[x], lb_b[x], constA, constB);
                    }
                }
                else if (isDouble)
                {
                    int srcX = 0;
#pragma warning disable CS8500
                    for (; srcX <= Crt_SrcW - VS; srcX += VS)
                    {
                        var packed = ProcessPixelVector(
                            *(Vector<float>*)(lb_r + srcX), *(Vector<float>*)(lb_g + srcX), *(Vector<float>*)(lb_b + srcX),
                            vConstA, vConstB);

                        for (int k = 0; k < VS; k++)
                        {
                            uint px = ((uint*)&packed)[k];
                            int outX = (srcX + k) * 2;
                            rowPtr[outX] = px; rowPtr[outX + 1] = px;
                        }
                    }
#pragma warning restore CS8500
                    for (; srcX < Crt_SrcW; srcX++)
                    {
                        uint px = ProcessPixelScalar(lb_r[srcX], lb_g[srcX], lb_b[srcX], constA, constB);
                        rowPtr[srcX * 2] = px; rowPtr[srcX * 2 + 1] = px;
                    }
                    x = dstW; // 標記已處理完畢
                }
                else
                {
                    // 任意比例縮放 (Bilinear) + batch SIMD
                    int fpScale = (Crt_SrcW << 16) / dstW;
                    int maxSrcX = Crt_SrcW - 1;
#pragma warning disable CS8500
                    float* rBatch = stackalloc float[VS];
                    float* gBatch = stackalloc float[VS];
                    float* bBatch = stackalloc float[VS];
                    for (; x <= dstW - VS; x += VS)
                    {
                        for (int k = 0; k < VS; k++)
                        {
                            int fp = (x + k) * fpScale; int srcX = fp >> 16;
                            float t = (fp & 0xFFFF) * (1f / 65536f);
                            int srcX1 = Math.Min(srcX + 1, maxSrcX);
                            rBatch[k] = lb_r[srcX] + t * (lb_r[srcX1] - lb_r[srcX]);
                            gBatch[k] = lb_g[srcX] + t * (lb_g[srcX1] - lb_g[srcX]);
                            bBatch[k] = lb_b[srcX] + t * (lb_b[srcX1] - lb_b[srcX]);
                        }
                        *(Vector<int>*)(rowPtr + x) = ProcessPixelVector(
                            *(Vector<float>*)rBatch, *(Vector<float>*)gBatch, *(Vector<float>*)bBatch,
                            vConstA, vConstB);
                    }
#pragma warning restore CS8500
                    for (; x < dstW; x++)
                    {
                        int fp = x * fpScale; int srcX = fp >> 16; float t = (fp & 0xFFFF) * (1f / 65536f);
                        int srcX1 = Math.Min(srcX + 1, maxSrcX);
                        float r = lb_r[srcX] + t * (lb_r[srcX1] - lb_r[srcX]);
                        float g = lb_g[srcX] + t * (lb_g[srcX1] - lb_g[srcX]);
                        float b = lb_b[srcX] + t * (lb_b[srcX1] - lb_b[srcX]);
                        rowPtr[x] = ProcessPixelScalar(r, g, b, constA, constB);
                    }
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
                int bytes2 = Crt_DstW * Crt_DstH * sizeof(uint);
                Buffer.MemoryCopy(renderTarget, _prevFrame, bytes2, bytes2);
                _prevFrameValid = true;
            }

            if (doCurv) ApplyFullFrameCurvatureAndConvergence();
        }



        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint ProcessPixelScalar(float r, float g, float b, float constA, float constB)
        {
            float bright = r * 0.3f + g * 0.59f + b * 0.11f;
            float fw = constA + bright * constB;
            float gc = GammaCoeff;
            float gcInv = GammaCoeffInv;

            // 無分支邊界限制
            r = Math.Max(0f, Math.Min(r * fw, 1f));
            g = Math.Max(0f, Math.Min(g * fw, 1f));
            b = Math.Max(0f, Math.Min(b * fw, 1f));

            // Gamma 校正：V = V * ((1 - GC) + GC * V)
            r *= (gcInv + gc * r);
            g *= (gcInv + gc * g);
            b *= (gcInv + gc * b);

            // 組裝 32-bit ARGB
            return (uint)((int)(b * 255.5f) | ((int)(g * 255.5f) << 8) | ((int)(r * 255.5f) << 16) | 0xFF000000u);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector<int> ProcessPixelVector(Vector<float> vr, Vector<float> vg, Vector<float> vb, Vector<float> vConstA, Vector<float> vConstB)
        {
            var vBright = vr * v03 + vg * v059 + vb * v011;
            var vFw = vConstA + vBright * vConstB;

            vr = Vector.Min(Vector.Max(vr * vFw, vZero), vOne);
            vg = Vector.Min(Vector.Max(vg * vFw, vZero), vOne);
            vb = Vector.Min(Vector.Max(vb * vFw, vZero), vOne);

            vr *= (v1_minus_GF + vGF * vr);
            vg *= (v1_minus_GF + vGF * vg);
            vb *= (v1_minus_GF + vGF * vb);

            var viR = Vector.ConvertToInt32(vr * v255_0f);
            var viG = Vector.ConvertToInt32(vg * v255_0f);
            var viB = Vector.ConvertToInt32(vb * v255_0f);

            return Vector.BitwiseOr(Vector.BitwiseOr(viB, viG * v256i), Vector.BitwiseOr(viR * v65536i, vAlphai));
        }

        // ════════════════════════════════════════════════════════════════════
        // ★ Branchless SWAR Inline 函數 ★
        // ════════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ProcessRowMask_SWAR(uint* row, int ty, int dstW, uint udim, bool isSM)
        {
            int phase = (isSM && (ty & 1) != 0) ? 1 : 0;

            if (Avx2.IsSupported)
            {
                int vecW = 8;
                Vector256<uint> vUdim    = Vector256.Create(udim);
                Vector256<uint> vMaskRB  = Vector256.Create(0x00FF00FFu);
                Vector256<uint> vMaskG   = Vector256.Create(0x0000FF00u);
                Vector256<uint> vAlpha   = Vector256.Create(0xFF000000u);
                Vector256<uint> vAllOnes = Vector256.Create(0xFFFFFFFFu);

                // keepMask 8-lane patterns — RGB cycles per pixel.
                // After 8 pixels, phase advances by +8 mod 3 = +2.
                const uint R_ = 0x00FF0000u, G_ = 0x0000FF00u, B_ = 0x000000FFu;
                Vector256<uint> vKeepP0 = Vector256.Create(R_, G_, B_, R_, G_, B_, R_, G_);
                Vector256<uint> vKeepP1 = Vector256.Create(G_, B_, R_, G_, B_, R_, G_, B_);
                Vector256<uint> vKeepP2 = Vector256.Create(B_, R_, G_, B_, R_, G_, B_, R_);

                int phaseIdx = phase;
                int tx = 0;
                int vecEnd = dstW - vecW + 1;
                for (; tx < vecEnd; tx += vecW)
                {
                    Vector256<uint> vPx = Avx.LoadVector256(row + tx);

                    Vector256<uint> vDimRB = Avx2.And(
                        Avx2.ShiftRightLogical(Avx2.MultiplyLow(Avx2.And(vPx, vMaskRB), vUdim), 8),
                        vMaskRB);
                    Vector256<uint> vDimG = Avx2.And(
                        Avx2.ShiftRightLogical(Avx2.MultiplyLow(Avx2.And(vPx, vMaskG), vUdim), 8),
                        vMaskG);

                    Vector256<uint> vKeep = phaseIdx == 0 ? vKeepP0 : (phaseIdx == 1 ? vKeepP1 : vKeepP2);
                    Vector256<uint> vNotKeep = Avx2.Xor(vKeep, vAllOnes);

                    Vector256<uint> vResult = Avx2.Or(
                        vAlpha,
                        Avx2.Or(Avx2.And(vPx, vKeep), Avx2.And(Avx2.Or(vDimRB, vDimG), vNotKeep)));

                    Avx.Store(row + tx, vResult);

                    phaseIdx = (phaseIdx + 2) % 3;
                }

                // Handle remainder (when dstW not multiple of 8).
                // Rebuild scalar keepMask from final cyclePos.
                uint keepMask;
                int cyclePos = ((phase + tx) % 3 + 3) % 3;
                if      (cyclePos == 0) keepMask = 0x00FF0000u;
                else if (cyclePos == 1) keepMask = 0x0000FF00u;
                else                    keepMask = 0x000000FFu;

                for (; tx < dstW; tx++)
                {
                    uint px = row[tx];
                    uint dim_RB = (((px & 0x00FF00FFu) * udim) >> 8) & 0x00FF00FFu;
                    uint dim_G  = (((px & 0x0000FF00u) * udim) >> 8) & 0x0000FF00u;
                    row[tx] = 0xFF000000u | (px & keepMask) | ((dim_RB | dim_G) & ~keepMask);
                    keepMask = (keepMask >> 8) | ((keepMask & 0xFFu) << 16);
                }
            }
            else
            {
                // Full scalar fallback (no Avx2).
                uint keepMask = phase == 1 ? 0x0000FF00u : 0x00FF0000u;
                for (int tx = 0; tx < dstW; tx++)
                {
                    uint px = row[tx];
                    uint dim_RB = (((px & 0x00FF00FFu) * udim) >> 8) & 0x00FF00FFu;
                    uint dim_G  = (((px & 0x0000FF00u) * udim) >> 8) & 0x0000FF00u;
                    row[tx] = 0xFF000000u | (px & keepMask) | ((dim_RB | dim_G) & ~keepMask);
                    keepMask = (keepMask >> 8) | ((keepMask & 0xFFu) << 16);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ProcessRowMaskPhosphor_SWAR(uint* row, uint* prw, int ty, int dstW, uint udim, uint udec, bool isSM)
        {
            int phase = (isSM && (ty & 1) != 0) ? 1 : 0;

            if (Avx2.IsSupported)
            {
                int tx = 0;
                int vecW = 8;
                Vector256<uint> vUdim    = Vector256.Create(udim);
                Vector256<uint> vUdec    = Vector256.Create(udec);
                Vector256<uint> vMaskRB  = Vector256.Create(0x00FF00FFu);
                Vector256<uint> vMaskG   = Vector256.Create(0x0000FF00u);
                Vector256<uint> vMaskR   = Vector256.Create(0x00FF0000u);
                Vector256<uint> vMaskB   = Vector256.Create(0x000000FFu);
                Vector256<uint> vAlpha   = Vector256.Create(0xFF000000u);
                Vector256<uint> vAllOnes = Vector256.Create(0xFFFFFFFFu);

                const uint R_ = 0x00FF0000u, G_ = 0x0000FF00u, B_ = 0x000000FFu;
                Vector256<uint> vKeepP0 = Vector256.Create(R_, G_, B_, R_, G_, B_, R_, G_);
                Vector256<uint> vKeepP1 = Vector256.Create(G_, B_, R_, G_, B_, R_, G_, B_);
                Vector256<uint> vKeepP2 = Vector256.Create(B_, R_, G_, B_, R_, G_, B_, R_);

                int phaseIdx = phase;
                int vecEnd = dstW - vecW + 1;
                for (; tx < vecEnd; tx += vecW)
                {
                    Vector256<uint> vPx  = Avx.LoadVector256(row + tx);
                    Vector256<uint> vPrv = Avx.LoadVector256(prw + tx);

                    // Phosphor decay (prv × udec)
                    Vector256<uint> vDecRB = Avx2.And(
                        Avx2.ShiftRightLogical(Avx2.MultiplyLow(Avx2.And(vPrv, vMaskRB), vUdec), 8),
                        vMaskRB);
                    Vector256<uint> vDecG = Avx2.And(
                        Avx2.ShiftRightLogical(Avx2.MultiplyLow(Avx2.And(vPrv, vMaskG), vUdec), 8),
                        vMaskG);

                    // Mask dim (px × udim)
                    Vector256<uint> vDimRB = Avx2.And(
                        Avx2.ShiftRightLogical(Avx2.MultiplyLow(Avx2.And(vPx, vMaskRB), vUdim), 8),
                        vMaskRB);
                    Vector256<uint> vDimG = Avx2.And(
                        Avx2.ShiftRightLogical(Avx2.MultiplyLow(Avx2.And(vPx, vMaskG), vUdim), 8),
                        vMaskG);

                    Vector256<uint> vKeep = phaseIdx == 0 ? vKeepP0 : (phaseIdx == 1 ? vKeepP1 : vKeepP2);
                    Vector256<uint> vNotKeep = Avx2.Xor(vKeep, vAllOnes);

                    // masked = (px & keep) | ((dim_RB | dim_G) & ~keep)
                    Vector256<uint> vMasked = Avx2.Or(
                        Avx2.And(vPx, vKeep),
                        Avx2.And(Avx2.Or(vDimRB, vDimG), vNotKeep));

                    // max(masked, dec) per channel
                    Vector256<uint> vMaxR = Avx2.Max(Avx2.And(vMasked, vMaskR), Avx2.And(vDecRB, vMaskR));
                    Vector256<uint> vMaxG = Avx2.Max(Avx2.And(vMasked, vMaskG), vDecG);
                    Vector256<uint> vMaxB = Avx2.Max(Avx2.And(vMasked, vMaskB), Avx2.And(vDecRB, vMaskB));

                    Vector256<uint> vResult = Avx2.Or(Avx2.Or(vMaxR, vMaxG), Avx2.Or(vMaxB, vAlpha));

                    Avx.Store(row + tx, vResult);
                    Avx.Store(prw + tx, vResult);

                    phaseIdx = (phaseIdx + 2) % 3;
                }

                // Remainder (< 8 pixels). Rebuild scalar keepMask from final cyclePos.
                uint keepMask;
                int cyclePos = ((phase + tx) % 3 + 3) % 3;
                if      (cyclePos == 0) keepMask = 0x00FF0000u;
                else if (cyclePos == 1) keepMask = 0x0000FF00u;
                else                    keepMask = 0x000000FFu;

                for (; tx < dstW; tx++)
                {
                    uint px = row[tx];
                    uint prv = prw[tx];
                    uint dec_RB = (((prv & 0x00FF00FFu) * udec) >> 8) & 0x00FF00FFu;
                    uint dec_G  = (((prv & 0x0000FF00u) * udec) >> 8) & 0x0000FF00u;
                    uint dim_RB = (((px  & 0x00FF00FFu) * udim) >> 8) & 0x00FF00FFu;
                    uint dim_G  = (((px  & 0x0000FF00u) * udim) >> 8) & 0x0000FF00u;
                    uint masked = (px & keepMask) | ((dim_RB | dim_G) & ~keepMask);
                    row[tx] = prw[tx] = 0xFF000000u
                                      | Math.Max(masked & 0x00FF0000u, dec_RB & 0x00FF0000u)
                                      | Math.Max(masked & 0x0000FF00u, dec_G)
                                      | Math.Max(masked & 0x000000FFu, dec_RB & 0x000000FFu);
                    keepMask = (keepMask >> 8) | ((keepMask & 0xFFu) << 16);
                }
            }
            else
            {
                // Full scalar fallback (no Avx2)
                uint keepMask = phase == 1 ? 0x0000FF00u : 0x00FF0000u;
                for (int tx = 0; tx < dstW; tx++)
                {
                    uint px = row[tx];
                    uint prv = prw[tx];
                    uint dec_RB = (((prv & 0x00FF00FFu) * udec) >> 8) & 0x00FF00FFu;
                    uint dec_G  = (((prv & 0x0000FF00u) * udec) >> 8) & 0x0000FF00u;
                    uint dim_RB = (((px  & 0x00FF00FFu) * udim) >> 8) & 0x00FF00FFu;
                    uint dim_G  = (((px  & 0x0000FF00u) * udim) >> 8) & 0x0000FF00u;
                    uint masked = (px & keepMask) | ((dim_RB | dim_G) & ~keepMask);
                    row[tx] = prw[tx] = 0xFF000000u
                                      | Math.Max(masked & 0x00FF0000u, dec_RB & 0x00FF0000u)
                                      | Math.Max(masked & 0x0000FF00u, dec_G)
                                      | Math.Max(masked & 0x000000FFu, dec_RB & 0x000000FFu);
                    keepMask = (keepMask >> 8) | ((keepMask & 0xFFu) << 16);
                }
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ProcessRowPhosphor_SWAR(uint* row, uint* prw, int dstW, uint udec)
        {
            if (Avx2.IsSupported)
            {
                int vecW = 8;
                Vector256<uint> vUdec   = Vector256.Create(udec);
                Vector256<uint> vMaskRB = Vector256.Create(0x00FF00FFu);
                Vector256<uint> vMaskG  = Vector256.Create(0x0000FF00u);
                Vector256<uint> vMaskR  = Vector256.Create(0x00FF0000u);
                Vector256<uint> vMaskB  = Vector256.Create(0x000000FFu);
                Vector256<uint> vAlpha  = Vector256.Create(0xFF000000u);

                int tx = 0;
                int vecEnd = dstW - vecW + 1;
                for (; tx < vecEnd; tx += vecW)
                {
                    Vector256<uint> vPx  = Avx.LoadVector256(row + tx);
                    Vector256<uint> vPrv = Avx.LoadVector256(prw + tx);

                    Vector256<uint> vDecRB = Avx2.And(
                        Avx2.ShiftRightLogical(Avx2.MultiplyLow(Avx2.And(vPrv, vMaskRB), vUdec), 8),
                        vMaskRB);
                    Vector256<uint> vDecG = Avx2.And(
                        Avx2.ShiftRightLogical(Avx2.MultiplyLow(Avx2.And(vPrv, vMaskG), vUdec), 8),
                        vMaskG);

                    Vector256<uint> vMaxR = Avx2.Max(Avx2.And(vPx, vMaskR), Avx2.And(vDecRB, vMaskR));
                    Vector256<uint> vMaxG = Avx2.Max(Avx2.And(vPx, vMaskG), vDecG);
                    Vector256<uint> vMaxB = Avx2.Max(Avx2.And(vPx, vMaskB), Avx2.And(vDecRB, vMaskB));

                    Vector256<uint> vResult = Avx2.Or(Avx2.Or(vMaxR, vMaxG), Avx2.Or(vMaxB, vAlpha));

                    Avx.Store(row + tx, vResult);
                    Avx.Store(prw + tx, vResult);
                }

                // Handle remainder (< 8 pixels)
                for (; tx < dstW; tx++)
                {
                    uint px = row[tx];
                    uint prv = prw[tx];
                    uint dec_RB = (((prv & 0x00FF00FFu) * udec) >> 8) & 0x00FF00FFu;
                    uint dec_G  = (((prv & 0x0000FF00u) * udec) >> 8) & 0x0000FF00u;
                    row[tx] = prw[tx] = 0xFF000000u
                                      | Math.Max(px & 0x00FF0000u, dec_RB & 0x00FF0000u)
                                      | Math.Max(px & 0x0000FF00u, dec_G)
                                      | Math.Max(px & 0x000000FFu, dec_RB & 0x000000FFu);
                }
            }
            else
            {
                // Full scalar fallback (no Avx2)
                for (int tx = 0; tx < dstW; tx++)
                {
                    uint px = row[tx];
                    uint prv = prw[tx];
                    uint dec_RB = (((prv & 0x00FF00FFu) * udec) >> 8) & 0x00FF00FFu;
                    uint dec_G  = (((prv & 0x0000FF00u) * udec) >> 8) & 0x0000FF00u;
                    row[tx] = prw[tx] = 0xFF000000u
                                      | Math.Max(px & 0x00FF0000u, dec_RB & 0x00FF0000u)
                                      | Math.Max(px & 0x0000FF00u, dec_G)
                                      | Math.Max(px & 0x000000FFu, dec_RB & 0x000000FFu);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [SkipLocalsInit]
        static void ProcessRowConvergence(uint* dst, uint* src, int dstW, float maxOff, float halfW, float invHW)
        {
            float step = invHW * maxOff;
            float baseOffset = -halfW * step + 1024.5f;
            int maxIdx = dstW - 1;

            if (Avx2.IsSupported)
            {
                int tx = 0;
                int vecW = 8;
                // Use fixed-point step (16.16) to SIMD compute ioff per lane
                int stepFx = (int)(step * 65536f);
                int baseFx = (int)((baseOffset - 1024f) * 65536f);

                Vector256<int> vStepOffsets = Vector256.Create(0, stepFx, 2*stepFx, 3*stepFx, 4*stepFx, 5*stepFx, 6*stepFx, 7*stepFx);
                Vector256<int> vStep8   = Vector256.Create(stepFx * 8);
                Vector256<int> vTxBase  = Vector256.Create(0, 1, 2, 3, 4, 5, 6, 7);
                Vector256<int> vTxStep  = Vector256.Create(8);
                Vector256<int> vMaxIdx  = Vector256.Create(maxIdx);
                Vector256<int> vZero    = Vector256<int>.Zero;
                Vector256<uint> vMaskB  = Vector256.Create(0x000000FFu);
                Vector256<uint> vMaskG  = Vector256.Create(0x0000FF00u);
                Vector256<uint> vMaskR  = Vector256.Create(0x00FF0000u);
                Vector256<uint> vAlpha  = Vector256.Create(0xFF000000u);

                Vector256<int> vIFx = Vector256.Create(baseFx) + vStepOffsets;
                Vector256<int> vTx  = vTxBase;

                int vecEnd = dstW - vecW + 1;
                for (; tx < vecEnd; tx += vecW)
                {
                    Vector256<int> vIoff = Avx2.ShiftRightArithmetic(vIFx, 16);
                    Vector256<int> vRxR  = Avx2.Min(Avx2.Max(Avx2.Add(vTx, vIoff), vZero), vMaxIdx);
                    Vector256<int> vRxB  = Avx2.Min(Avx2.Max(Avx2.Subtract(vTx, vIoff), vZero), vMaxIdx);

                    // Manual gather via GetElement (vpextrd, no memory round-trip)
                    Vector256<uint> vPixB = Vector256.Create(
                        src[vRxB.GetElement(0)], src[vRxB.GetElement(1)], src[vRxB.GetElement(2)], src[vRxB.GetElement(3)],
                        src[vRxB.GetElement(4)], src[vRxB.GetElement(5)], src[vRxB.GetElement(6)], src[vRxB.GetElement(7)]);
                    Vector256<uint> vPixR = Vector256.Create(
                        src[vRxR.GetElement(0)], src[vRxR.GetElement(1)], src[vRxR.GetElement(2)], src[vRxR.GetElement(3)],
                        src[vRxR.GetElement(4)], src[vRxR.GetElement(5)], src[vRxR.GetElement(6)], src[vRxR.GetElement(7)]);
                    // Middle pixel (G channel) is just src[tx..tx+7] — sequential load
                    Vector256<uint> vPixM = Avx.LoadVector256(src + tx);

                    Vector256<uint> vResult = Avx2.Or(
                        Avx2.Or(Avx2.And(vPixB, vMaskB), Avx2.And(vPixM, vMaskG)),
                        Avx2.Or(Avx2.And(vPixR, vMaskR), vAlpha));

                    Avx.Store(dst + tx, vResult);

                    vIFx = Avx2.Add(vIFx, vStep8);
                    vTx  = Avx2.Add(vTx, vTxStep);
                }

                // Remainder (< 8 pixels)
                for (; tx < dstW; tx++)
                {
                    int ioff = (int)(tx * step + baseOffset) - 1024;
                    int rxR = Math.Max(0, Math.Min(tx + ioff, maxIdx));
                    int rxB = Math.Max(0, Math.Min(tx - ioff, maxIdx));
                    dst[tx] = (src[rxB] & 0x000000FFu) | (src[tx] & 0x0000FF00u) | (src[rxR] & 0x00FF0000u) | 0xFF000000u;
                }
            }
            else
            {
                // Full scalar fallback (no Avx2)
                for (int tx = 0; tx < dstW; tx++)
                {
                    int ioff = (int)(tx * step + baseOffset) - 1024;
                    int rxR = Math.Max(0, Math.Min(tx + ioff, maxIdx));
                    int rxB = Math.Max(0, Math.Min(tx - ioff, maxIdx));
                    dst[tx] = (src[rxB] & 0x000000FFu) | (src[tx] & 0x0000FF00u) | (src[rxR] & 0x00FF0000u) | 0xFF000000u;
                }
            }
        }

        [SkipLocalsInit]
        static void ApplyFullFrameCurvatureAndConvergence()
        {
            PrecomputeCurvature();
            int dstW = Crt_DstW, dstH = Crt_DstH;
            uint* dst = crt_analogScreenBuf;
            uint* tmp = _curvTemp;
            int* map = _curvMap;
            int* col = _curvMapCol;

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
                // Fixed-point 16.16: eliminate per-pixel float→int conversion
                int stepFx = (int)(step * 65536f);
                int baseFx = (int)((-halfW * step + 0.5f) * 65536f); // baseOffset - 1024, pre-subtracted
                int maxIdx = dstW - 1;

                // doConv path: per-pixel 3-channel chromatic shift. .NET 10 SIMD:
                // triple Avx2.GatherVector256 per 8-pixel batch replaces 24 scalar loads.
                Parallel.For(0, dstH, ty =>
                {
                    int rowOff = ty * dstW;
                    int* mapRow = map + rowOff;
                    int* colRow = col + rowOff;
                    uint* dstRow = dst + rowOff;

                    if (Avx2.IsSupported)
                    {
                        int tx = 0;
                        int vecW = 8;
                        Vector256<int> vStepOffsets = Vector256.Create(0, stepFx, 2*stepFx, 3*stepFx, 4*stepFx, 5*stepFx, 6*stepFx, 7*stepFx);
                        Vector256<int> vStep8       = Vector256.Create(stepFx * 8);
                        Vector256<int> vMaxIdx      = Vector256.Create(maxIdx);
                        Vector256<int> vZero        = Vector256<int>.Zero;
                        Vector256<int> vBlack       = Vector256.Create(unchecked((int)0xFF000000u));
                        Vector256<int> vMaskB       = Vector256.Create(0x000000FF);
                        Vector256<int> vMaskG       = Vector256.Create(0x0000FF00);
                        Vector256<int> vMaskR       = Vector256.Create(0x00FF0000);

                        Vector256<int> vIFx = Vector256.Create(baseFx) + vStepOffsets;

                        int vecEnd = dstW - vecW + 1;
                        for (; tx < vecEnd; tx += vecW)
                        {
                            Vector256<int> vSrcIdx = Avx.LoadVector256(mapRow + tx);
                            Vector256<int> vSrcTx  = Avx.LoadVector256(colRow + tx);
                            Vector256<int> vSrcRowOff = Avx2.Subtract(vSrcIdx, vSrcTx);
                            Vector256<int> vIoff      = Avx2.ShiftRightArithmetic(vIFx, 16);

                            // rxR/rxB = clamp(srcTx ± ioff, 0, maxIdx)
                            Vector256<int> vRxR = Avx2.Min(Avx2.Max(Avx2.Add(vSrcTx, vIoff), vZero), vMaxIdx);
                            Vector256<int> vRxB = Avx2.Min(Avx2.Max(Avx2.Subtract(vSrcTx, vIoff), vZero), vMaxIdx);

                            // Invalid mask: srcIdx < 0 → -1 per lane (will be blended to black)
                            Vector256<int> vInvalid = Avx2.ShiftRightArithmetic(vSrcIdx, 31);

                            // Gather indices (mask invalid lanes to 0 for safe gather)
                            Vector256<int> vIdxB = Avx2.AndNot(vInvalid, Avx2.Add(vSrcRowOff, vRxB));
                            Vector256<int> vIdxM = Avx2.AndNot(vInvalid, Avx2.Add(vSrcRowOff, vSrcTx));
                            Vector256<int> vIdxR = Avx2.AndNot(vInvalid, Avx2.Add(vSrcRowOff, vRxR));

                            // Manual gather via GetElement (vpextrd, no memory round-trip)
                            Vector256<int> vPixB = Vector256.Create(
                                (int)tmp[vIdxB.GetElement(0)], (int)tmp[vIdxB.GetElement(1)], (int)tmp[vIdxB.GetElement(2)], (int)tmp[vIdxB.GetElement(3)],
                                (int)tmp[vIdxB.GetElement(4)], (int)tmp[vIdxB.GetElement(5)], (int)tmp[vIdxB.GetElement(6)], (int)tmp[vIdxB.GetElement(7)]);
                            Vector256<int> vPixM = Vector256.Create(
                                (int)tmp[vIdxM.GetElement(0)], (int)tmp[vIdxM.GetElement(1)], (int)tmp[vIdxM.GetElement(2)], (int)tmp[vIdxM.GetElement(3)],
                                (int)tmp[vIdxM.GetElement(4)], (int)tmp[vIdxM.GetElement(5)], (int)tmp[vIdxM.GetElement(6)], (int)tmp[vIdxM.GetElement(7)]);
                            Vector256<int> vPixR = Vector256.Create(
                                (int)tmp[vIdxR.GetElement(0)], (int)tmp[vIdxR.GetElement(1)], (int)tmp[vIdxR.GetElement(2)], (int)tmp[vIdxR.GetElement(3)],
                                (int)tmp[vIdxR.GetElement(4)], (int)tmp[vIdxR.GetElement(5)], (int)tmp[vIdxR.GetElement(6)], (int)tmp[vIdxR.GetElement(7)]);

                            // Byte blend: (B & 0xFF) | (M & 0xFF00) | (R & 0xFF0000) | 0xFF000000
                            Vector256<int> vBlend = Avx2.Or(
                                Avx2.Or(Avx2.And(vPixB, vMaskB), Avx2.And(vPixM, vMaskG)),
                                Avx2.Or(Avx2.And(vPixR, vMaskR), vBlack));

                            // Invalid lanes → pure black
                            Vector256<int> vResult = Avx2.Or(
                                Avx2.AndNot(vInvalid, vBlend),
                                Avx2.And(vInvalid, vBlack));

                            Avx.Store((int*)(dstRow + tx), vResult);

                            vIFx = Avx2.Add(vIFx, vStep8);
                        }

                        // Remainder (< 8 pixels)
                        int iFx = baseFx + tx * stepFx;
                        for (; tx < dstW; tx++)
                        {
                            int srcIdx = mapRow[tx];
                            if (srcIdx < 0) { dstRow[tx] = 0xFF000000u; iFx += stepFx; continue; }
                            int srcTx = colRow[tx];
                            int srcRowOff = srcIdx - srcTx;
                            int ioff = iFx >> 16;
                            int rxR = Math.Max(0, Math.Min(srcTx + ioff, maxIdx));
                            int rxB = Math.Max(0, Math.Min(srcTx - ioff, maxIdx));
                            dstRow[tx] = (tmp[srcRowOff + rxB] & 0x000000FFu) |
                                         (tmp[srcRowOff + srcTx] & 0x0000FF00u) |
                                         (tmp[srcRowOff + rxR] & 0x00FF0000u) |
                                         0xFF000000u;
                            iFx += stepFx;
                        }
                    }
                    else
                    {
                        // Full scalar fallback (no Avx2)
                        int iFx = baseFx;
                        for (int tx = 0; tx < dstW; tx++)
                        {
                            int srcIdx = mapRow[tx];
                            if (srcIdx < 0) { dstRow[tx] = 0xFF000000u; iFx += stepFx; continue; }
                            int srcTx = colRow[tx];
                            int srcRowOff = srcIdx - srcTx;
                            int ioff = iFx >> 16;
                            int rxR = Math.Max(0, Math.Min(srcTx + ioff, maxIdx));
                            int rxB = Math.Max(0, Math.Min(srcTx - ioff, maxIdx));
                            dstRow[tx] = (tmp[srcRowOff + rxB] & 0x000000FFu) |
                                         (tmp[srcRowOff + srcTx] & 0x0000FF00u) |
                                         (tmp[srcRowOff + rxR] & 0x00FF0000u) |
                                         0xFF000000u;
                            iFx += stepFx;
                        }
                    }
                });
            }
            else
            {
                // 若玩家沒開 Convergence，跑這條最乾淨的極速迴圈（.NET 10 SIMD: Avx2.GatherVector256）
                Parallel.For(0, dstH, ty =>
                {
                    int rowOff = ty * dstW;
                    int* mapRow = map + rowOff;
                    uint* dstRow = dst + rowOff;

                    if (Avx2.IsSupported)
                    {
                        int tx = 0;
                        Vector256<int> vBlack = Vector256.Create(unchecked((int)0xFF000000u));
                        int vecW = Vector256<int>.Count; // 8

                        int vecEnd = dstW - vecW + 1;
                        for (; tx < vecEnd; tx += vecW)
                        {
                            // Load 8 source indices at once
                            Vector256<int> vSrcIdx = Avx.LoadVector256(mapRow + tx);
                            // Sign-extend mask: -1 per lane where srcIdx < 0, else 0
                            Vector256<int> vMask = Avx2.ShiftRightArithmetic(vSrcIdx, 31);
                            // Safe index: srcIdx & ~mask (replace -1 sentinels with 0 for harmless gather)
                            Vector256<int> vSafe = Avx2.AndNot(vMask, vSrcIdx);
                            // Manual gather via GetElement (vpextrd, no memory round-trip)
                            Vector256<int> vPix = Vector256.Create(
                                (int)tmp[vSafe.GetElement(0)], (int)tmp[vSafe.GetElement(1)], (int)tmp[vSafe.GetElement(2)], (int)tmp[vSafe.GetElement(3)],
                                (int)tmp[vSafe.GetElement(4)], (int)tmp[vSafe.GetElement(5)], (int)tmp[vSafe.GetElement(6)], (int)tmp[vSafe.GetElement(7)]);
                            // Blend: (pix & ~mask) | (BLACK & mask)
                            Vector256<int> vResult = Avx2.Or(
                                Avx2.AndNot(vMask, vPix),
                                Avx2.And(vMask, vBlack));
                            Avx.Store((int*)(dstRow + tx), vResult);
                        }

                        // Remainder (< 8 pixels)
                        for (; tx < dstW; tx++)
                        {
                            int srcIdx = mapRow[tx];
                            int mask = srcIdx >> 31;
                            int safeIdx = srcIdx & ~mask;
                            dstRow[tx] = (tmp[safeIdx] & (uint)~mask) | (0xFF000000u & (uint)mask);
                        }
                    }
                    else
                    {
                        // Full scalar fallback (no Avx2)
                        for (int tx = 0; tx < dstW; tx++)
                        {
                            int srcIdx = mapRow[tx];
                            int mask = srcIdx >> 31;
                            int safeIdx = srcIdx & ~mask;
                            dstRow[tx] = (tmp[safeIdx] & (uint)~mask) | (0xFF000000u & (uint)mask);
                        }
                    }
                });
            }
        }
    }
}