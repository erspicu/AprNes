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
    //  輸出：AnalogScreenBuf [1024 × 840 BGRA]（透過 ApplyConfig 注入）
    //
    //  垂直映射：240 → 840（× 3.5），連續域高斯掃描線
    //  演算法：
    //    1. 高斯掃描線權重：W = exp(−dy² / (2σ²))
    //    2. Bloom（高光溢出）：W_final = W + brightness × BloomStrength × (1−W)
    //    3. BrightnessBoost：補償掃描線黑溝造成的平均亮度損失
    //    4. Fast gamma（≈ pow(v,1/1.13)，與原 YiqToRgb 一致，保留 NES 色調）
    //
    //  三種端子參數組（由 AnalogOutputMode 決定）：
    //    RF     : BeamSigma=1.10, BloomStrength=0.50, BrightnessBoost=1.10
    //    AV     : BeamSigma=0.85, BloomStrength=0.25, BrightnessBoost=1.25
    //    SVideo : BeamSigma=0.65, BloomStrength=0.10, BrightnessBoost=1.40
    // ============================================================

    unsafe public static class CrtScreen
    {
        // ── 解耦參數（由外部透過 ApplyConfig 注入）────────────────────────
        static int    _analogOutput;        // AnalogOutputMode as int
        static int    _analogSize = 4;
        static uint*  _analogScreenBuf;
        static int    _frameCount;

        /// <summary>
        /// 將外部運行時參數注入 CrtScreen 模組（每次設定變更或 Init 時呼叫）
        /// </summary>
        public static void ApplyConfig(int analogOutput, int analogSize,
                                        uint* analogScreenBuf)
        {
            _analogOutput    = analogOutput;
            _analogSize      = analogSize;
            _analogScreenBuf = analogScreenBuf;
            ApplyProfile();  // 端子參數只在設定變更時需要套用
        }

        /// <summary>每幀更新幀計數器（供 interlace jitter 使用）</summary>
        public static void SetFrameCount(int fc) => _frameCount = fc;

        public const int SrcW = 1024;  // linearBuffer 列寬（固定）
        public const int SrcH = 240;   // linearBuffer 列數（固定）
        // DstW/DstH 依 AnalogSize 動態決定（256×N × 210×N，維持 8:7 AR）
        // 全螢幕時可由外部覆寫為任意解析度
        static int? _fullscreenW = null, _fullscreenH = null;
        public static int DstW => _fullscreenW ?? 256 * _analogSize;
        public static int DstH => _fullscreenH ?? 210 * _analogSize;

        public static void SetFullscreenSize(int w, int h) { _fullscreenW = w; _fullscreenH = h; }
        public static void ClearFullscreenSize() { _fullscreenW = null; _fullscreenH = null; }

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

        // #15 Vertical vignette strength (0=off, 0.15=subtle, 0.3=visible)
        public static float VignetteStrength = 0.15f;

        // #18 Interlace jitter (simulate CRT field alternation, ±0.25 pixel)
        public static bool InterlaceJitter = true;

        // #11 Shadow mask / Aperture grille
        public enum MaskType { None, ApertureGrille, ShadowMask }
        public static MaskType ShadowMaskMode = MaskType.ApertureGrille;
        public static float ShadowMaskStrength = 0.3f;

        // #14 Screen curvature (barrel distortion)
        public static float CurvatureStrength = 0.12f;

        // #10 Phosphor persistence (decay per frame, 0=off, 0.6=default)
        public static float PhosphorDecay = 0.6f;

        // #12 Horizontal beam spread (blur strength, 0=off, 0.4=default)
        public static float HBeamSpread = 0.4f;

        // #13 Beam convergence (max sub-pixel offset at screen edge, 0=off)
        public static float ConvergenceStrength = 2.0f;

        // ── SIMD 常數向量（static，避免每幀 new）────────────────────────────
        static readonly Vector<float> vOne   = new Vector<float>(1f);
        static readonly Vector<float> vZero  = new Vector<float>(0f);
        static readonly Vector<float> v03    = new Vector<float>(0.3f);
        static readonly Vector<float> v059   = new Vector<float>(0.59f);
        static readonly Vector<float> v011   = new Vector<float>(0.11f);
        static readonly Vector<float> v255_5f = new Vector<float>(255.5f);
        static readonly Vector<int>   v255i   = new Vector<int>(255);
        static readonly Vector<int>   vZeroi  = new Vector<int>(0);
        static readonly Vector<int>   v256i   = new Vector<int>(256);
        static readonly Vector<int>   v65536i = new Vector<int>(65536);
        static readonly Vector<int>   vAlphai = new Vector<int>(unchecked((int)0xFF000000));
        // 依設定變動的向量（ApplyProfile 時更新）
        static Vector<float> vBloom = new Vector<float>(0f);
        static Vector<float> vGF    = new Vector<float>(0.229f);

        // ── 掃描線預計算快取（unmanaged memory）─────────────────────────────
        static float  _cachedSigma = -1f;
        static int    _cachedFrame = -1;
        static float* _weights;    // DstH floats
        static int*   _nearestY;   // DstH ints
        static float* _boostRow;   // DstH floats: per-row boost (BrightnessBoost × vignette)
        // #14 Curvature remap
        static uint*  _curvTemp;   // temp buffer for pre-distortion frame
        static int*   _curvMap;    // remap table [DstW × DstH]
        static int    _cachedCurvW, _cachedCurvH;
        static float  _cachedCurvK = -1f;
        // #10 Phosphor persistence
        static uint*  _prevFrame;
        static bool   _prevFrameValid;

        // ════════════════════════════════════════════════════════════════════
        // Init
        // ════════════════════════════════════════════════════════════════════
        public static void Init()
        {
            // 每次 Init 都重新分配（AnalogSize 可能已改變）
            if (_weights  != null) Marshal.FreeHGlobal((IntPtr)_weights);
            if (_nearestY != null) Marshal.FreeHGlobal((IntPtr)_nearestY);
            if (_boostRow != null) Marshal.FreeHGlobal((IntPtr)_boostRow);
            _weights  = (float*)Marshal.AllocHGlobal(DstH * sizeof(float));
            _nearestY = (int*)  Marshal.AllocHGlobal(DstH * sizeof(int));
            _boostRow = (float*)Marshal.AllocHGlobal(DstH * sizeof(float));
            // #14 Curvature buffers
            if (_curvTemp != null) Marshal.FreeHGlobal((IntPtr)_curvTemp);
            if (_curvMap  != null) Marshal.FreeHGlobal((IntPtr)_curvMap);
            _curvTemp = (uint*)Marshal.AllocHGlobal(DstW * DstH * sizeof(uint));
            _curvMap  = (int*) Marshal.AllocHGlobal(DstW * DstH * sizeof(int));
            // #10 Phosphor persistence buffer
            if (_prevFrame != null) Marshal.FreeHGlobal((IntPtr)_prevFrame);
            _prevFrame = (uint*)Marshal.AllocHGlobal(DstW * DstH * sizeof(uint));
            _prevFrameValid = false;
            _cachedSigma = -1f; // 強制重新計算掃描線權重
            _cachedFrame = -1;
            _cachedCurvK = -1f;
        }

        // ── 端子參數套用 ─────────────────────────────────────────────────────
        static void ApplyProfile()
        {
            if (_analogOutput == (int)AnalogOutputMode.RF)
            { BeamSigma = RF_BeamSigma; BloomStrength = RF_BloomStrength; BrightnessBoost = RF_BrightnessBoost; }
            else if (_analogOutput == (int)AnalogOutputMode.SVideo)
            { BeamSigma = SV_BeamSigma; BloomStrength = SV_BloomStrength; BrightnessBoost = SV_BrightnessBoost; }
            else
            { BeamSigma = AV_BeamSigma; BloomStrength = AV_BloomStrength; BrightnessBoost = AV_BrightnessBoost; }
            vBloom = new Vector<float>(BloomStrength);
            vGF    = new Vector<float>(Ntsc.GammaCoeff);
        }

        // ── 掃描線高斯權重預計算（BeamSigma 改變 / 每幀隔行時重算）──────────
        static void PrecomputeScanlineWeights()
        {
            bool needUpdate = (_cachedSigma != BeamSigma);
            // #18 Interlace jitter: weights change every frame
            if (InterlaceJitter)
            {
                int fc = _frameCount;
                if (_cachedFrame != fc) { _cachedFrame = fc; needUpdate = true; }
            }
            if (!needUpdate) return;
            _cachedSigma = BeamSigma;

            // #18 Interlace jitter: ±0.25 pixel vertical offset per frame
            float jitter = InterlaceJitter ? ((_frameCount & 1) == 0 ? 0.25f : -0.25f) : 0f;

            float inv = 1f / (2f * BeamSigma * BeamSigma);
            int dstH = DstH;
            float bb = BrightnessBoost;
            float vs = VignetteStrength;
            Parallel.For(0, dstH, ty =>
            {
                float sy = ((float)ty + jitter) / dstH * SrcH;
                int   ny = (int)(sy + 0.5f);
                if (ny >= SrcH) ny = SrcH - 1;
                _nearestY[ty] = ny;
                float dy = sy - ny;
                _weights[ty] = (float)Math.Exp(-(dy * dy) * inv);

                // #15 Vignette: parabolic vertical falloff
                float vy = (float)ty / dstH - 0.5f;
                float vigFactor = 1f - vs * 4f * vy * vy;
                _boostRow[ty] = bb * vigFactor;
            });
        }

        // ── #14 螢幕曲率預計算 ──────────────────────────────────────────────
        static void PrecomputeCurvature()
        {
            int dstW = DstW, dstH = DstH;
            float k = CurvatureStrength;
            if (_cachedCurvK == k && _cachedCurvW == dstW && _cachedCurvH == dstH) return;
            _cachedCurvK = k; _cachedCurvW = dstW; _cachedCurvH = dstH;

            float invW = 1f / (dstW - 1);
            float invH = 1f / (dstH - 1);
            int* cm = _curvMap;
            Parallel.For(0, dstH, ty =>
            {
                float cy = ty * invH - 0.5f;
                int rowOff = ty * dstW;
                for (int tx = 0; tx < dstW; tx++)
                {
                    float cx = tx * invW - 0.5f;
                    float r2 = cx * cx + cy * cy;
                    float f = 1f + k * r2;
                    int sx = (int)((0.5f + cx * f) * (dstW - 1) + 0.5f);
                    int sy = (int)((0.5f + cy * f) * (dstH - 1) + 0.5f);
                    cm[rowOff + tx] = (sx >= 0 && sx < dstW && sy >= 0 && sy < dstH)
                        ? sy * dstW + sx : -1;
                }
            });
        }

        // ── #11 蔭罩後處理 ──────────────────────────────────────────────────
        static void ApplyShadowMask()
        {
            if (ShadowMaskMode == MaskType.None || ShadowMaskStrength <= 0f) return;

            int dstW = DstW, dstH = DstH;
            uint* dst = _analogScreenBuf;
            int dimI = (int)((1f - ShadowMaskStrength) * 256f);
            bool isSM = ShadowMaskMode == MaskType.ShadowMask;

            Parallel.For(0, dstH, ty =>
            {
                uint* row = dst + (long)ty * dstW;
                int phase = (isSM && (ty & 1) != 0) ? 1 : 0;
                for (int tx = 0; tx < dstW; tx++)
                {
                    uint px = row[tx];
                    int b = (int)(px & 0xFF);
                    int g = (int)((px >> 8) & 0xFF);
                    int r = (int)((px >> 16) & 0xFF);

                    if (phase == 0)      { g = g * dimI >> 8; b = b * dimI >> 8; }
                    else if (phase == 1) { r = r * dimI >> 8; b = b * dimI >> 8; }
                    else                 { r = r * dimI >> 8; g = g * dimI >> 8; }

                    row[tx] = (uint)(b | (g << 8) | (r << 16)) | 0xFF000000u;
                    if (++phase == 3) phase = 0;
                }
            });
        }

        // ── B8: ShadowMask + Phosphor 合併 pass ─────────────────────────────
        static void ApplyShadowMaskAndPhosphor()
        {
            bool doMask = ShadowMaskMode != MaskType.None && ShadowMaskStrength > 0f;
            bool doPhosphor = PhosphorDecay > 0f && _prevFrame != null && _prevFrameValid;
            if (!doMask && !doPhosphor) return;

            int dstW = DstW, dstH = DstH;
            uint* dst = _analogScreenBuf;
            uint* prev = _prevFrame;
            int dimI = doMask ? (int)((1f - ShadowMaskStrength) * 256f) : 0;
            bool isSM = ShadowMaskMode == MaskType.ShadowMask;
            float decay = PhosphorDecay;

            Parallel.For(0, dstH, ty =>
            {
                int rowOff = ty * dstW;
                int phase = (doMask && isSM && (ty & 1) != 0) ? 1 : 0;
                for (int tx = 0; tx < dstW; tx++)
                {
                    int idx = rowOff + tx;
                    uint px = dst[idx];
                    int b = (int)(px & 0xFF);
                    int g = (int)((px >> 8) & 0xFF);
                    int r = (int)((px >> 16) & 0xFF);

                    if (doMask)
                    {
                        if (phase == 0)      { g = g * dimI >> 8; b = b * dimI >> 8; }
                        else if (phase == 1) { r = r * dimI >> 8; b = b * dimI >> 8; }
                        else                 { r = r * dimI >> 8; g = g * dimI >> 8; }
                        if (++phase == 3) phase = 0;
                    }

                    if (doPhosphor)
                    {
                        uint prv = prev[idx];
                        int pb = (int)((prv & 0xFF) * decay);
                        int pg = (int)(((prv >> 8) & 0xFF) * decay);
                        int pr = (int)(((prv >> 16) & 0xFF) * decay);
                        if (pb > b) b = pb;
                        if (pg > g) g = pg;
                        if (pr > r) r = pr;
                    }

                    uint result = (uint)(b | (g << 8) | (r << 16)) | 0xFF000000u;
                    dst[idx] = result;
                    if (doPhosphor) prev[idx] = result;
                }
            });
        }

        // ── B6: Convergence + Curvature 合併 pass ───────────────────────────
        static void ApplyConvergenceAndCurvature()
        {
            bool doConv = ConvergenceStrength > 0f;
            bool doCurv = CurvatureStrength > 0f && _curvMap != null;
            if (!doConv && !doCurv) return;
            if (doCurv) PrecomputeCurvature();

            int dstW = DstW, dstH = DstH;
            uint* dst = _analogScreenBuf;
            uint* tmp = _curvTemp;
            int bytes = dstW * dstH * sizeof(uint);
            int* map = doCurv ? _curvMap : null;
            float maxOff = ConvergenceStrength;
            float halfW  = dstW * 0.5f;
            float invHW  = halfW > 0f ? 1f / halfW : 0f;

            Buffer.MemoryCopy(dst, tmp, bytes, bytes);

            Parallel.For(0, dstH, ty =>
            {
                int rowOff = ty * dstW;
                for (int tx = 0; tx < dstW; tx++)
                {
                    int dstIdx = rowOff + tx;
                    int srcRowOff, srcTx;
                    if (doCurv)
                    {
                        int srcIdx = map[dstIdx];
                        if (srcIdx < 0) { dst[dstIdx] = 0xFF000000u; continue; }
                        srcRowOff = srcIdx - srcIdx % dstW;
                        srcTx = srcIdx % dstW;
                    }
                    else
                    {
                        srcRowOff = rowOff;
                        srcTx = tx;
                    }

                    if (doConv)
                    {
                        float cx = (srcTx - halfW) * invHW;
                        int ioff = (int)(cx * maxOff + (cx >= 0 ? 0.5f : -0.5f));
                        int rxR = srcTx + ioff;
                        if (rxR < 0) rxR = 0; else if (rxR >= dstW) rxR = dstW - 1;
                        int rxB = srcTx - ioff;
                        if (rxB < 0) rxB = 0; else if (rxB >= dstW) rxB = dstW - 1;

                        uint srcR = tmp[srcRowOff + rxR];
                        uint srcG = tmp[srcRowOff + srcTx];
                        uint srcB = tmp[srcRowOff + rxB];

                        dst[dstIdx] = (uint)((int)(srcB & 0xFF) | ((int)((srcG >> 8) & 0xFF) << 8) | ((int)((srcR >> 16) & 0xFF) << 16)) | 0xFF000000u;
                    }
                    else
                    {
                        dst[dstIdx] = tmp[srcRowOff + srcTx];
                    }
                }
            });
        }

        // ── #14 曲率後處理 ──────────────────────────────────────────────────
        static void ApplyCurvature()
        {
            if (CurvatureStrength <= 0f || _curvTemp == null || _curvMap == null) return;
            PrecomputeCurvature();

            int dstW = DstW, dstH = DstH;
            uint* dst = _analogScreenBuf;
            uint* tmp = _curvTemp;
            int bytes = dstW * dstH * sizeof(uint);
            int* map = _curvMap;

            Buffer.MemoryCopy(dst, tmp, bytes, bytes);

            Parallel.For(0, dstH, ty =>
            {
                int rowOff = ty * dstW;
                for (int tx = 0; tx < dstW; tx++)
                {
                    int srcIdx = map[rowOff + tx];
                    dst[rowOff + tx] = srcIdx >= 0 ? tmp[srcIdx] : 0xFF000000u;
                }
            });
        }

        // ── #12 水平 beam 擴散（在 linearBuffer 上做 3-tap 模糊）──────────
        static void ApplyHorizontalBlur()
        {
            if (HBeamSpread <= 0f) return;
            float* lb = Ntsc.linearBuffer;
            if (lb == null) return;
            float alpha  = HBeamSpread * 0.5f;
            float center = 1f - HBeamSpread;
            const int kPlane = Ntsc.kPlane;

            Parallel.For(0, 3 * SrcH, i =>
            {
                int plane = i / SrcH;
                int row   = i - plane * SrcH;
                float* p = lb + plane * kPlane + row * SrcW;
                float prev = p[0];
                for (int x = 0; x < SrcW; x++)
                {
                    float cur  = p[x];
                    float next = (x + 1 < SrcW) ? p[x + 1] : cur;
                    p[x] = prev * alpha + cur * center + next * alpha;
                    prev = cur;
                }
            });
        }

        // ── #10 磷光體餘輝（per-channel max of current vs decayed previous）──
        static void ApplyPhosphorPersistence()
        {
            if (PhosphorDecay <= 0f || _prevFrame == null)
            {
                _prevFrameValid = false;
                return;
            }

            uint* dst  = _analogScreenBuf;
            uint* prev = _prevFrame;
            int dstW = DstW, dstH = DstH;

            if (!_prevFrameValid)
            {
                int bytes = dstW * dstH * sizeof(uint);
                Buffer.MemoryCopy(dst, prev, bytes, bytes);
                _prevFrameValid = true;
                return;
            }

            float decay = PhosphorDecay;
            Parallel.For(0, dstH, ty =>
            {
                int rowOff = ty * dstW;
                for (int x = 0; x < dstW; x++)
                {
                    int idx = rowOff + x;
                    uint cur = dst[idx];
                    uint prv = prev[idx];

                    int cb = (int)(cur & 0xFF);
                    int cg = (int)((cur >> 8) & 0xFF);
                    int cr = (int)((cur >> 16) & 0xFF);

                    int pb = (int)((prv & 0xFF) * decay);
                    int pg = (int)(((prv >> 8) & 0xFF) * decay);
                    int pr = (int)(((prv >> 16) & 0xFF) * decay);

                    int rb = cb > pb ? cb : pb;
                    int rg = cg > pg ? cg : pg;
                    int rr = cr > pr ? cr : pr;

                    uint result = (uint)(rb | (rg << 8) | (rr << 16)) | 0xFF000000u;
                    dst[idx]  = result;
                    prev[idx] = result;
                }
            });
        }

        // ── #13 Beam convergence（R/B 水平偏移，邊緣遞增）────────────────
        static void ApplyBeamConvergence()
        {
            if (ConvergenceStrength <= 0f || _curvTemp == null) return;

            int dstW = DstW, dstH = DstH;
            uint* dst = _analogScreenBuf;
            uint* tmp = _curvTemp;
            int bytes = dstW * dstH * sizeof(uint);
            float maxOff = ConvergenceStrength;
            float halfW  = dstW * 0.5f;
            float invHW  = 1f / halfW;

            Buffer.MemoryCopy(dst, tmp, bytes, bytes);

            Parallel.For(0, dstH, ty =>
            {
                int rowOff = ty * dstW;
                for (int tx = 0; tx < dstW; tx++)
                {
                    float cx = (tx - halfW) * invHW;
                    int ioff = (int)(cx * maxOff + (cx >= 0 ? 0.5f : -0.5f));

                    int rxR = tx + ioff;
                    if (rxR < 0) rxR = 0; else if (rxR >= dstW) rxR = dstW - 1;
                    int rxB = tx - ioff;
                    if (rxB < 0) rxB = 0; else if (rxB >= dstW) rxB = dstW - 1;

                    uint srcR = tmp[rowOff + rxR];
                    uint srcG = tmp[rowOff + tx];
                    uint srcB = tmp[rowOff + rxB];

                    int r = (int)((srcR >> 16) & 0xFF);
                    int g = (int)((srcG >> 8)  & 0xFF);
                    int b = (int)(srcB & 0xFF);

                    dst[rowOff + tx] = (uint)(b | (g << 8) | (r << 16)) | 0xFF000000u;
                }
            });
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
            if (_analogScreenBuf == null || Ntsc.linearBuffer == null) return;
            if (_weights == null || _nearestY == null || _boostRow == null) return;

            PrecomputeScanlineWeights();
            ApplyHorizontalBlur();   // #12 on linearBuffer before scanline render

            float  bloom     = BloomStrength;
            float* brow      = _boostRow;          // #15 per-row boost (includes vignette)
            float  gc        = Ntsc.GammaCoeff;    // #17 configurable gamma
            float* lb        = Ntsc.linearBuffer;
            uint*  dst       = _analogScreenBuf;
            float* wts       = _weights;
            int*   nyArr     = _nearestY;
            const int kPlane = Ntsc.kPlane; // R/G/B plane stride（245,760 floats）

            int dstW     = DstW;  // 快取，避免 lambda 內重複呼叫 property
            int dstH     = DstH;
            bool is1to1  = (dstW == SrcW);      // N=4：1:1 SIMD
            bool isDouble = (dstW == SrcW * 2); // N=8：每 source 像素 → 2 output，SIMD

            int VS = Vector<float>.Count;  // 4（SSE2）或 8（AVX2）

            Parallel.For(0, dstH, ty =>
            {
                float  weight  = wts[ty];
                float  omw     = 1f - weight;
                float  boost   = brow[ty];         // #15 per-row boost with vignette
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
                        r += gc * r * (r - 1f);
                        g += gc * g * (g - 1f);
                        b += gc * b * (b - 1f);
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

                        r += gc * r * (r - 1f);
                        g += gc * g * (g - 1f);
                        b += gc * b * (b - 1f);

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

                    r += gc * r * (r - 1f);
                    g += gc * g * (g - 1f);
                    b += gc * b * (b - 1f);

                    int ri = (int)(r * 255.5f); if (ri > 255) ri = 255;
                    int gi = (int)(g * 255.5f); if (gi > 255) gi = 255;
                    int bi = (int)(b * 255.5f); if (bi > 255) bi = 255;
                    rowPtr[x] = (uint)(bi | (gi << 8) | (ri << 16) | 0xFF000000u);
                }
            });

            // ── 後處理 ─────────────────────────────────────────────────────
            // B8: ShadowMask + Phosphor merged
            bool canMerge = (ShadowMaskMode != MaskType.None && ShadowMaskStrength > 0f)
                          || (PhosphorDecay > 0f && _prevFrame != null && _prevFrameValid);
            if (canMerge)
                ApplyShadowMaskAndPhosphor();
            else
            {
                ApplyShadowMask();
                ApplyPhosphorPersistence();
            }
            if (PhosphorDecay > 0f && _prevFrame != null && !_prevFrameValid)
            {
                int bytes2 = DstW * DstH * sizeof(uint);
                Buffer.MemoryCopy(_analogScreenBuf, _prevFrame, bytes2, bytes2);
                _prevFrameValid = true;
            }
            // B6: Convergence + Curvature merged
            if (ConvergenceStrength > 0f || (CurvatureStrength > 0f && _curvMap != null))
                ApplyConvergenceAndCurvature();
            else
            {
                ApplyBeamConvergence();
                ApplyCurvature();
            }
        }
    }
}
