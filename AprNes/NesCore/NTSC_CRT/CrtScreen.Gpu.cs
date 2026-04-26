#if CRT_GPU_AVAILABLE
// ════════════════════════════════════════════════════════════════════════
// CrtScreen.Gpu.cs — SkiaSharp SKRuntimeEffect CRT pipeline
// ════════════════════════════════════════════════════════════════════════
// Phase 2 of the CRT GPU design (MD/gpu/CRT_GPU_Design.md §14 & §15).
// Only compiled when CRT_GPU_AVAILABLE is defined (AprNesAvalonia only).
//
// v1 MVP scope (Step 1):
//   ✓ Bilinear upscale linearBuffer (1024×240 RGB) → Crt_DstW × Crt_DstH
//   ✓ Brightness + gamma + scanline + mask + vignette (via crt_core_v1.sksl)
//   ✗ Phosphor decay, convergence, curvature, horizontal blur
//     (Phase 2 Step 2/3 will add these)
//
// Pipeline per frame:
//   1. CPU: quantize linearBuffer float RGB → Bgra8888 input SKBitmap
//   2. GPU: SKRuntimeEffect shader runs over output SKSurface
//   3. CPU: readback SKSurface → crt_analogScreenBuf (uint*)
//
// Uses CPU raster SKSurface for determinism; true GPU acceleration via
// Avalonia render-context integration is deferred to later iterations.
// ════════════════════════════════════════════════════════════════════════
using System;
using System.Runtime.InteropServices;
using SkiaSharp;
using AprNesAvalonia;
using static AprNes.NesCore;

namespace AprNes
{
    unsafe internal static class CrtScreenGpu
    {
        // Shader (compiled once, reused every frame)
        static SKRuntimeEffect? _effect;

        // Input: kOutW×240 Bgra8888 quantization of linearBuffer RGB planes
        // (1024 non-HD, 2048 HD — tracks linearBuffer stride automatically)
        static SKBitmap? _inputBitmap;
        static byte* _inputScratch;           // unmanaged staging for InstallPixels
        const int SrcW = kOutW;
        const int SrcH = 240;

        // Output + phosphor ping-pong (Step 3): _prevSurface holds frame N-1,
        // _outputSurface receives frame N. After render we swap references so
        // next frame's uPrev reads what we just wrote.
        static SKSurface? _outputSurface;
        static SKSurface? _prevSurface;
        static int _outputW, _outputH;

        internal static void ApplyProfile()
        {
            // Matches CrtScreenScalar.ApplyProfile / CrtScreenSimd.ApplyProfile
            if (crt_analogOutput == (int)AnalogOutputMode.RF)
            { BeamSigma = RF_BeamSigma; BloomStrength = RF_BloomStrength; BrightnessBoost = RF_BrightnessBoost; }
            else if (crt_analogOutput == (int)AnalogOutputMode.SVideo)
            { BeamSigma = SV_BeamSigma; BloomStrength = SV_BloomStrength; BrightnessBoost = SV_BrightnessBoost; }
            else
            { BeamSigma = AV_BeamSigma; BloomStrength = AV_BloomStrength; BrightnessBoost = AV_BrightnessBoost; }
        }

        internal static void Init()
        {
            Console.WriteLine("[CRT] backend = GPU (SkiaSharp SKRuntimeEffect, raster SKSurface)");

            // Compile shader (once; cached in ShaderLoader)
            // Picks newest versioned crt_core_*.sksl, falls back to baseline v1.
            try
            {
                _effect = ShaderLoader.LoadLatest("crt_core_", fallbackFile: "crt_core_v1.sksl");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CRT] shader load failed: {ex.Message}");
                Console.Error.WriteLine("[CRT] runtime will fall back to SIMD on next Render");
                _effect = null;
                return;
            }

            // Allocate input bitmap (1024×240 Bgra8888)
            _inputBitmap?.Dispose();
            _inputBitmap = new SKBitmap(
                new SKImageInfo(SrcW, SrcH, SKColorType.Bgra8888, SKAlphaType.Opaque));
            if (_inputScratch != null) { NesCore.FreeUnmanaged((IntPtr)_inputScratch); _inputScratch = null; }

            // Allocate output surface at current Crt_DstW × Crt_DstH
            EnsureOutputSurface();
        }

        static void EnsureOutputSurface()
        {
            int w = Crt_DstW, h = Crt_DstH;
            if (_outputSurface != null && w == _outputW && h == _outputH) return;

            _outputSurface?.Dispose();
            _prevSurface?.Dispose();
            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
            _outputSurface = SKSurface.Create(info);
            _prevSurface   = SKSurface.Create(info);
            _prevSurface!.Canvas.Clear(SKColors.Black);   // first-frame prev = all-black
            _prevSurface.Canvas.Flush();
            _outputW = w; _outputH = h;
        }

        internal static void Render()
        {
            // Phase 3A: if render thread will handle CRT on GPU canvas, skip emu-thread work
            if (CrtGpuRenderThreadActive) return;

            if (_effect == null || _inputBitmap == null)
            {
                // shader not available → fallback to Simd
                CrtScreenSimd.Render();
                return;
            }
            if (crt_analogScreenBuf == null || linearBuffer == null) return;

            EnsureOutputSurface();
            if (_outputSurface == null) return;

            // ── Stage 1: quantize linearBuffer (float RGB planes) to Bgra8888 ──
            // linearBuffer layout: [R plane 1024*240][G plane 1024*240][B plane 1024*240]
            // Output: _inputBitmap pixels = 0xFFRRGGBB (Bgra8888 little-endian = B G R A)
            float* lbR = linearBuffer;
            float* lbG = linearBuffer + kPlane;
            float* lbB = linearBuffer + 2 * kPlane;

            IntPtr bmpPixels = _inputBitmap.GetPixels();
            uint* dst = (uint*)bmpPixels;
            int n = SrcW * SrcH;
            for (int i = 0; i < n; i++)
            {
                uint r = (uint)Math.Clamp((int)(lbR[i] * 255f + 0.5f), 0, 255);
                uint g = (uint)Math.Clamp((int)(lbG[i] * 255f + 0.5f), 0, 255);
                uint b = (uint)Math.Clamp((int)(lbB[i] * 255f + 0.5f), 0, 255);
                dst[i] = 0xFF000000u | (r << 16) | (g << 8) | b;
            }
            _inputBitmap.NotifyPixelsChanged();

            // ── Stage 2: build SKShader from input bitmap + previous-frame surface ──
            // Texture sampling at default (nearest); cubic interpolation lives
            // in the SkSL helper sampleHCatmullRom (crt_core_v1.sksl).
            using var inputShader = SKShader.CreateBitmap(
                _inputBitmap,
                SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
            using var prevImage  = _prevSurface!.Snapshot();
            using var prevShader = prevImage.ToShader(
                SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);

            // ── Stage 3: set uniforms + children ──
            var uniforms = new SKRuntimeEffectUniforms(_effect);
            uniforms["uSrcSize"] = new[] { (float)SrcW, (float)SrcH };
            uniforms["uDstSize"] = new[] { (float)_outputW, (float)_outputH };
            uniforms["uScanlineStrength"] = Math.Clamp(1.0f - 1.0f / (1.0f + BeamSigma), 0f, 1f);
            uniforms["uBrightness"] = BrightnessBoost;
            uniforms["uBloomStrength"] = BloomStrength;
            uniforms["uGamma"] = GammaCoeff;
            uniforms["uMaskStrength"] = (ShadowMaskMode != CrtMaskType.None) ? ShadowMaskStrength : 0f;
            uniforms["uMaskType"] = (float)(ShadowMaskMode switch
            {
                CrtMaskType.ApertureGrille => 1,
                CrtMaskType.ShadowMask     => 2,
                _                          => 0,
            });
            uniforms["uVignetteStrength"] = VignetteStrength;
            uniforms["uCurvature"] = CurvatureStrength;
            uniforms["uConvergence"] = ConvergenceStrength;
            // HBlurAlpha is the side-tap weight of a 3-tap source-pixel-space blur.
            // At HD_NTSC the source is 2× denser, so the kernel covers half the
            // angular dot range — boost the side-tap weight by 1/kSampleRateScale
            // to compensate (clamped at 0.45f to keep center weight non-negative).
            uniforms["uHBlurAlpha"] = Math.Min(HBeamSpread * 0.5f / kSampleRateScale, 0.45f);
            uniforms["uPhosphorDecay"] = PhosphorDecay;

            var children = new SKRuntimeEffectChildren(_effect);
            children["uScreen"] = inputShader;
            children["uPrev"]   = prevShader;

            using var runtimeShader = _effect.ToShader(uniforms, children);

            // ── Stage 4: draw to output surface ──
            var canvas = _outputSurface.Canvas;
            canvas.Clear(SKColors.Black);
            using var paint = new SKPaint { Shader = runtimeShader, IsAntialias = false };
            canvas.DrawRect(0, 0, _outputW, _outputH, paint);
            canvas.Flush();

            // ── Stage 5: readback to crt_analogScreenBuf ──
            var readInfo = new SKImageInfo(_outputW, _outputH, SKColorType.Bgra8888, SKAlphaType.Opaque);
            bool ok = _outputSurface.ReadPixels(
                readInfo,
                (IntPtr)crt_analogScreenBuf,
                _outputW * sizeof(uint),
                0, 0);
            if (!ok)
            {
                Console.Error.WriteLine("[CRT GPU] SKSurface.ReadPixels failed; frame lost");
            }

            // ── Stage 6: ping-pong swap for next frame's phosphor read ──
            (_prevSurface, _outputSurface) = (_outputSurface, _prevSurface);
        }
    }
}
#endif
