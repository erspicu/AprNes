using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace AprNesAvalonia.Views;

/// <summary>
/// Zero-copy emulator screen control.
/// Accepts an external uint* pointer and renders it directly via Skia's InstallPixels,
/// bypassing WriteableBitmap entirely — no CPU copy on the render path.
/// </summary>
public class EmuScreenControl : Control
{
    public IntPtr FrontBufferPtr { get; set; }
    public int FrameWidth { get; set; } = 256;
    public int FrameHeight { get; set; } = 240;

    public override void Render(DrawingContext context)
    {
        if (FrontBufferPtr != IntPtr.Zero && FrameWidth > 0 && FrameHeight > 0)
        {
            context.Custom(new EmuDrawOperation(
                new Rect(Bounds.Size), FrontBufferPtr, FrameWidth, FrameHeight));
        }
        base.Render(context);
    }

    /// <summary>
    /// Executes on Avalonia's dedicated Render Thread — UI Thread is not blocked.
    /// </summary>
    sealed class EmuDrawOperation : ICustomDrawOperation
    {
        private readonly IntPtr _ptr;
        private readonly int _w, _h;

        public Rect Bounds { get; }

        public EmuDrawOperation(Rect bounds, IntPtr ptr, int w, int h)
        {
            Bounds = bounds;
            _ptr = ptr;
            _w = w;
            _h = h;
        }

        public void Render(ImmediateDrawingContext context)
        {
            GuiBenchmark.NotifyRenderFrame();
            try
            {
                var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (leaseFeature == null) return;

                using var lease = leaseFeature.Lease();
                var canvas = lease.SkCanvas;
                var dstRect = new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height);

                // Phase 3A: render-thread GPU CRT. Runs SkSL on the GPU-backed
                // canvas (D3D11 on Windows). Skips the emulator-thread raster
                // CRT entirely. Active only when Gpu backend + this flag set.
                if (AprNes.NesCore.AnalogEnabled &&
                    AprNes.NesCore.CrtEnabled &&
                    AprNes.NesCore.CrtGpuRenderThreadActive &&
                    CrtGpuRenderThread.IsReady)
                {
                    CrtGpuRenderThread.Render(canvas, dstRect);
                    return;
                }

                if (_ptr == IntPtr.Zero) return;

                // Zero-copy: InstallPixels makes SKBitmap point directly at
                // the emulator's unmanaged buffer — O(1), no pixel copy.
                var info = new SKImageInfo(_w, _h, SKColorType.Bgra8888, SKAlphaType.Opaque);
                using var bmp = new SKBitmap();
                bmp.InstallPixels(info, _ptr, _w * 4);

                using var paint = new SKPaint { FilterQuality = SKFilterQuality.Low };
                canvas.DrawBitmap(bmp, dstRect, paint);
            }
            catch (AccessViolationException) { }
        }

        public void Dispose() { }
        public bool Equals(ICustomDrawOperation? other) => false;
        public bool HitTest(Point p) => false;
    }
}
