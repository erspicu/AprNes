// ════════════════════════════════════════════════════════════════════════
// CrtGpuRenderThread.cs — Phase 3A: CRT shader on Avalonia's GPU canvas
// ════════════════════════════════════════════════════════════════════════
// Called from EmuScreenControl.EmuDrawOperation.Render on Avalonia's render
// thread after ISkiaSharpApiLeaseFeature.Lease(). The provided SKCanvas is
// GPU-backed (D3D11 on Windows) — shader executes on real GPU hardware, no
// readback to CPU needed.
//
// Reads NesCore.linearBuffer (float RGB planes, 1024×240) directly. Emu thread
// writes linearBuffer at end of NTSC stage; render thread samples it here.
// Minor tearing possible without sync; acceptable for Phase 3A MVP.
//
// Emu thread's CrtScreen.Gpu.Render() no-ops when CrtGpuRenderThreadActive
// is true, avoiding duplicate CRT work + readback cost.
// ════════════════════════════════════════════════════════════════════════
using System;
using SkiaSharp;
using AprNes;
using static AprNes.NesCore;

namespace AprNesAvalonia;

internal static unsafe class CrtGpuRenderThread
{
    static SKRuntimeEffect? _effect;

    // Input: 1024×240 Bgra8888 quantization of linearBuffer.
    // Cubic sampling for boundary smoothing happens entirely in SkSL
    // (sampleHCatmullRom in crt_core_v1.sksl) — texture sampling here is left
    // as nearest so the shader's 4-tap helper sees raw pixels, not pre-blurred.
    static SKBitmap? _inputBitmap;
    const int SrcW = 1024;
    const int SrcH = 240;

    // Intermediate main surface: shader renders here (GPU-resident), then we
    // blit to Avalonia canvas + blit to prev surface. Avoids re-running the
    // full shader for phosphor history (Phase 3B optimization).
    static SKSurface? _mainSurface;
    // Phosphor ping-pong: _prevSurface holds previous rendered output. GPU-backed
    // when possible, raster fallback otherwise.
    static SKSurface? _prevSurface;
    static int _surfaceW, _surfaceH;
    static bool _surfaceIsGpu;

    public static void Init()
    {
        if (_effect != null) return;   // idempotent

        try
        {
            // Prefer newest versioned file (crt_core_YYYYMMDDHHMMSS_author.sksl);
            // falls back to the baseline crt_core_v1.sksl if none exists.
            _effect = ShaderLoader.LoadLatest("crt_core_", fallbackFile: "crt_core_v1.sksl");
            Console.WriteLine("[CRT-RT] shader loaded (render-thread GPU path)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CRT-RT] shader load failed: {ex.Message}");
            _effect = null;
            return;
        }

        _inputBitmap?.Dispose();
        _inputBitmap = new SKBitmap(
            new SKImageInfo(SrcW, SrcH, SKColorType.Bgra8888, SKAlphaType.Opaque));
    }

    public static bool IsReady => _effect != null && _inputBitmap != null;

    static void EnsureSurfaces(GRContext? grContext, int w, int h)
    {
        bool wantGpu = grContext != null;
        if (_mainSurface != null && _prevSurface != null &&
            _surfaceW == w && _surfaceH == h && _surfaceIsGpu == wantGpu)
            return;

        _mainSurface?.Dispose();
        _prevSurface?.Dispose();

        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
        if (grContext != null)
        {
            _mainSurface = SKSurface.Create(grContext, false, info);   // GPU-backed
            _prevSurface = SKSurface.Create(grContext, false, info);   // GPU-backed
            _surfaceIsGpu = true;
        }
        else
        {
            _mainSurface = SKSurface.Create(info);                      // raster fallback
            _prevSurface = SKSurface.Create(info);
            _surfaceIsGpu = false;
        }
        _prevSurface!.Canvas.Clear(SKColors.Black);                     // first-frame prev = black
        _prevSurface.Canvas.Flush();
        _surfaceW = w; _surfaceH = h;
    }

    /// <summary>
    /// Render CRT shader to the provided GPU canvas over the given destination
    /// rectangle. Called from Avalonia's render thread after leasing SkCanvas.
    /// </summary>
    public static void Render(SKCanvas canvas, GRContext? grContext, SKRect dstRect)
    {
        if (!IsReady) return;
        if (linearBuffer == null) return;

        int dstW = (int)dstRect.Width;
        int dstH = (int)dstRect.Height;
        if (dstW <= 0 || dstH <= 0) return;

        EnsureSurfaces(grContext, dstW, dstH);

        // ── Stage 1: quantize linearBuffer → Bgra8888 input ──
        float* lbR = linearBuffer;
        float* lbG = linearBuffer + kPlane;
        float* lbB = linearBuffer + 2 * kPlane;

        IntPtr bmpPixels = _inputBitmap!.GetPixels();
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

        // ── Stage 2: child shaders ──
        // Texture sampling left at default (nearest) — crt_core_v1.sksl's
        // sampleHCatmullRom helper does its own 4-tap cubic interpolation
        // over raw pixels. Stacking SkiaSharp Mitchell on top would over-blur.
        using var inputShader = SKShader.CreateBitmap(
            _inputBitmap,
            SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
        using var prevImage  = _prevSurface!.Snapshot();
        using var prevShader = prevImage.ToShader(
            SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);

        // ── Stage 3: uniforms (full CPU parity — see crt_core_v1.sksl header) ──
        var uniforms = new SKRuntimeEffectUniforms(_effect!);
        uniforms["uSrcSize"] = new[] { (float)SrcW, (float)SrcH };
        uniforms["uDstSize"] = new[] { (float)dstW, (float)dstH };
        // Gaussian beam: inv = 1 / (2*sigma^2); guarded against div-by-zero
        float sigma = Math.Max(BeamSigma, 0.001f);
        uniforms["uBeamInv"] = 1.0f / (2.0f * sigma * sigma);
        // InterlaceJitter: ±0.25 dst-rows, scaled to src-rows for shader convenience
        float scaleY    = (float)SrcH / dstH;
        float jitterDst = InterlaceJitter ? (((crt_frameCount & 1) == 0) ? 0.25f : -0.25f) : 0f;
        uniforms["uJitter"] = jitterDst * scaleY;
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
        uniforms["uHBlurAlpha"] = HBeamSpread * 0.5f;
        uniforms["uPhosphorDecay"] = PhosphorDecay;

        var children = new SKRuntimeEffectChildren(_effect!);
        children["uScreen"] = inputShader;
        children["uPrev"]   = prevShader;

        using var runtimeShader = _effect!.ToShader(uniforms, children);

        // ── Stage 4: render shader to intermediate main surface ──
        // (Origin 0,0 — shader's fragCoord starts at 0, matching uv calc.)
        using var paint = new SKPaint { Shader = runtimeShader, IsAntialias = false };
        var mainCanvas = _mainSurface!.Canvas;
        mainCanvas.Clear(SKColors.Black);
        mainCanvas.DrawRect(0, 0, dstW, dstH, paint);
        mainCanvas.Flush();

        // ── Stage 5: blit main → Avalonia canvas (what the user sees) ──
        // GPU→GPU texture copy; snapshot is a cheap handle on GPU image.
        using var mainImg = _mainSurface.Snapshot();
        canvas.DrawImage(mainImg, dstRect);

        // ── Stage 6: blit main → prev surface for next frame's phosphor ──
        // Phase 3B optimization: GPU texture copy instead of re-running the
        // full shader. ~50% saving on shader work per frame.
        if (PhosphorDecay > 0.001f)
        {
            var prevCanvas = _prevSurface!.Canvas;
            prevCanvas.DrawImage(mainImg, 0, 0);
            prevCanvas.Flush();
        }
    }

    public static void Dispose()
    {
        _inputBitmap?.Dispose(); _inputBitmap = null;
        _mainSurface?.Dispose(); _mainSurface = null;
        _prevSurface?.Dispose(); _prevSurface = null;
        // _effect is owned by ShaderLoader cache; don't dispose here
    }
}
