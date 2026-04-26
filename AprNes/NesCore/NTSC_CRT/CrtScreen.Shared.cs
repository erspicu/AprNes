// ════════════════════════════════════════════════════════════════════════
// CrtScreen.Shared.cs — shared state + runtime dispatch between backends
// ════════════════════════════════════════════════════════════════════════
// Phase 1 of the CRT GPU design (MD/gpu/CRT_GPU_Design.md §14).
//
// Contains:
//   - Shared public config (VignetteStrength, PhosphorDecay, ...)
//   - Shared decoupling params (crt_analogSize, crt_analogScreenBuf, ...)
//   - Display dimensions (Crt_SrcW/H, Crt_DstW/H, fullscreen override)
//   - Terminal profile constants (RF_*, AV_*, SV_*) + runtime selections
//   - CrtBackend enum + Crt_GetBackend / Crt_SetBackend
//   - Public API dispatchers: Crt_Init / Crt_Render / Crt_ApplyConfig /
//     Crt_UpdateScreenBuf / Crt_SetFrameCount / Crt_SetFullscreenSize /
//     Crt_ClearFullscreenSize
//
// Per-backend state (cache buffers, SIMD constants) lives in:
//   - CrtScreenScalar  (CrtScreen.cs,       compiled always)
//   - CrtScreenSimd    (CrtScreen.Simd.cs,  compiled only when CRT_SIMD_AVAILABLE)
//
// Build configuration:
//   AprNes (.NET 4.8.1) — only scalar; CRT_SIMD_AVAILABLE undefined
//   AprNesAvalonia (.NET 10) — both; CRT_SIMD_AVAILABLE defined in csproj
// ════════════════════════════════════════════════════════════════════════
using System;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        // ── CRT backend selection ─────────────────────────────────────
        public enum CrtBackend { Scalar, Simd, Gpu }
#if CRT_SIMD_AVAILABLE
        static CrtBackend _crtBackend = CrtBackend.Simd;
#else
        static CrtBackend _crtBackend = CrtBackend.Scalar;
#endif

        public static CrtBackend Crt_GetBackend() => _crtBackend;

        /// <summary>
        /// Phase 3A: when true, emu-thread Crt_Render (for Gpu backend) skips entirely
        /// and the render thread runs the SkSL shader directly on Avalonia's
        /// GPU-backed SkCanvas via ISkiaSharpApiLeaseFeature. Only GUI mode sets this.
        /// Headless leaves it false so CrtScreenGpu raster path continues to populate
        /// crt_analogScreenBuf for --screenshot.
        /// </summary>
        public static bool CrtGpuRenderThreadActive = false;

        public static void Crt_SetBackend(CrtBackend b)
        {
#if !CRT_SIMD_AVAILABLE
            if (b != CrtBackend.Scalar)
            {
                Console.WriteLine($"[CRT] requested {b} but only Scalar available in this build; using Scalar.");
                b = CrtBackend.Scalar;
            }
#else
            // GPU backend not yet implemented — falls back to Simd (silent; logged on next Init)
#endif
            _crtBackend = b;
        }

        // ── Shared decoupling parameters ──────────────────────────────
        internal static int crt_analogOutput;
        internal static int crt_analogSize = 4;
        internal static uint* crt_analogScreenBuf;
        internal static int crt_frameCount;

        // ── Display dimensions ────────────────────────────────────────
        // Crt_SrcW tracks linearBuffer width (kOutW), so HD_NTSC double-rate
        // (2048 samples/scanline) propagates to all CRT backends automatically.
        public const int Crt_SrcW = kOutW;
        public const int Crt_SrcH = 240;
        static int? _fullscreenW = null, _fullscreenH = null;
        public static int Crt_DstW => _fullscreenW ?? 256 * crt_analogSize;
        public static int Crt_DstH => _fullscreenH ?? 210 * crt_analogSize;
        public static void Crt_SetFullscreenSize(int w, int h) { _fullscreenW = w; _fullscreenH = h; }
        public static void Crt_ClearFullscreenSize() { _fullscreenW = null; _fullscreenH = null; }

        // ── Terminal profiles (RF / AV / SVideo) ──────────────────────
        public static float RF_BeamSigma = 1.10f;
        public static float RF_BloomStrength = 0.50f;
        public static float RF_BrightnessBoost = 1.10f;
        public static float AV_BeamSigma = 0.85f;
        public static float AV_BloomStrength = 0.25f;
        public static float AV_BrightnessBoost = 1.25f;
        public static float SV_BeamSigma = 0.65f;
        public static float SV_BloomStrength = 0.10f;
        public static float SV_BrightnessBoost = 1.40f;

        // Runtime-selected profile values (set by active backend's ApplyProfile)
        internal static float BeamSigma;
        internal static float BloomStrength;
        internal static float BrightnessBoost;

        // ── User-tunable CRT parameters ───────────────────────────────
        public static float VignetteStrength = 0.15f;
        public static bool InterlaceJitter = true;
        public enum CrtMaskType { None, ApertureGrille, ShadowMask }
        public static CrtMaskType ShadowMaskMode = CrtMaskType.ApertureGrille;
        public static float ShadowMaskStrength = 0.3f;
        public static float CurvatureStrength = 0.12f;
        public static float PhosphorDecay = 0.15f;
        public static float HBeamSpread = 0.4f;
        public static float ConvergenceStrength = 2.0f;

        // ══════════════════════════════════════════════════════════════
        // Public API dispatchers
        // ══════════════════════════════════════════════════════════════

        public static void Crt_ApplyConfig(int analogOutput, int analogSize, uint* analogScreenBuf)
        {
            crt_analogOutput = analogOutput;
            crt_analogSize = analogSize;
            crt_analogScreenBuf = analogScreenBuf;
            Crt_DispatchApplyProfile();
        }

        static void Crt_DispatchApplyProfile()
        {
#if CRT_GPU_AVAILABLE
            if (_crtBackend == CrtBackend.Gpu) { CrtScreenGpu.ApplyProfile(); return; }
#endif
#if CRT_SIMD_AVAILABLE
            if (_crtBackend == CrtBackend.Simd) { CrtScreenSimd.ApplyProfile(); return; }
#endif
            CrtScreenScalar.ApplyProfile();
        }

        public static void Crt_UpdateScreenBuf(uint* buf) => crt_analogScreenBuf = buf;
        public static void Crt_SetFrameCount(int fc) => crt_frameCount = fc;

        public static void Crt_Init()
        {
            Console.WriteLine($"[CRT] dispatch backend = {_crtBackend}");
#if CRT_GPU_AVAILABLE
            if (_crtBackend == CrtBackend.Gpu) { CrtScreenGpu.Init(); return; }
#endif
#if CRT_SIMD_AVAILABLE
            if (_crtBackend == CrtBackend.Simd) { CrtScreenSimd.Init(); return; }
#endif
            CrtScreenScalar.Init();
        }

        public static void Crt_Render()
        {
#if CRT_GPU_AVAILABLE
            if (_crtBackend == CrtBackend.Gpu) { CrtScreenGpu.Render(); return; }
#endif
#if CRT_SIMD_AVAILABLE
            if (_crtBackend == CrtBackend.Simd) { CrtScreenSimd.Render(); return; }
#endif
            CrtScreenScalar.Render();
        }
    }
}
