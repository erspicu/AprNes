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

    // Input: 1024×240 Bgra8888 quantization of linearBuffer
    static SKBitmap? _inputBitmap;
    const int SrcW = 1024;
    const int SrcH = 240;

    // Phosphor ping-pong lives on render thread (separate from CrtScreenGpu).
    // _prevSurface holds previous rendered output. Allocated GPU-backed when
    // possible (via GRContext from the lease), raster fallback otherwise.
    static SKSurface? _prevSurface;
    static int _prevW, _prevH;
    static bool _prevIsGpu;

    public static void Init()
    {
        if (_effect != null) return;   // idempotent

        try
        {
            _effect = ShaderLoader.Load("crt_core_v1.sksl");
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

    static void EnsurePrevSurface(GRContext? grContext, int w, int h)
    {
        bool wantGpu = grContext != null;
        if (_prevSurface != null && _prevW == w && _prevH == h && _prevIsGpu == wantGpu) return;
        _prevSurface?.Dispose();
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
        if (grContext != null)
        {
            _prevSurface = SKSurface.Create(grContext, false, info);  // GPU-backed
            _prevIsGpu = true;
        }
        else
        {
            _prevSurface = SKSurface.Create(info);                     // raster fallback
            _prevIsGpu = false;
        }
        _prevSurface!.Canvas.Clear(SKColors.Black);
        _prevSurface.Canvas.Flush();
        _prevW = w; _prevH = h;
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

        EnsurePrevSurface(grContext, dstW, dstH);

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
        using var inputShader = SKShader.CreateBitmap(
            _inputBitmap,
            SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
        // Phosphor prev: when _prevSurface is GPU-backed, Snapshot is a GPU
        // image → shader sampling stays on GPU (no raster fallback).
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

        // ── Stage 4: draw to the GPU canvas (Avalonia D3D11 backing on Windows) ──
        // Shader's fragCoord space is local — translate so shader thinks origin is dst.Left,Top.
        using var paint = new SKPaint { Shader = runtimeShader, IsAntialias = false };
        canvas.Save();
        canvas.Translate(dstRect.Left, dstRect.Top);
        canvas.DrawRect(0, 0, dstW, dstH, paint);
        canvas.Restore();

        // ── Stage 5: update phosphor history ──
        // Re-render shader into _prevSurface (GPU-backed when grContext != null).
        // Cost: one additional shader pass, but on GPU — no CPU raster pipeline.
        if (PhosphorDecay > 0.001f)
        {
            var prevCanvas = _prevSurface.Canvas;
            prevCanvas.Clear(SKColors.Black);
            prevCanvas.DrawRect(0, 0, dstW, dstH, paint);
            prevCanvas.Flush();
        }
    }

    public static void Dispose()
    {
        _inputBitmap?.Dispose(); _inputBitmap = null;
        _prevSurface?.Dispose(); _prevSurface = null;
        // _effect is owned by ShaderLoader cache; don't dispose here
    }
}
