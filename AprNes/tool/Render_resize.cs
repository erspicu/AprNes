using System;
using System.Drawing;
using System.Drawing.Imaging;
using WINAPIGDI;

namespace AprNes
{
    /// <summary>
    /// NetFx GDI shell around the shared <see cref="RenderPipeline"/>.
    ///
    /// Owns the GDI device init + DrawImageHighSpeedtoDevice blit and the
    /// Bitmap snapshot (for screenshot capture). All filter logic lives in
    /// RenderPipeline; this class just adapts it to InterfaceGraphic.
    /// </summary>
    unsafe public class Render_resize : InterfaceGraphic
    {
        private readonly RenderPipeline _pipeline = new();

        public Render_resize() { }

        public void Configure(ResizeFilter s1Filter, int s1Scale,
                              ResizeFilter s2Filter, int s2Scale,
                              bool scanline)
            => _pipeline.Configure(s1Filter, s1Scale, s2Filter, s2Scale, scanline);

        public void freeMem() => _pipeline.FreeMem();

        public Bitmap GetOutput()
        {
            return new Bitmap(_pipeline.OutputW, _pipeline.OutputH,
                              _pipeline.OutputW * 4, PixelFormat.Format32bppRgb,
                              (IntPtr)_pipeline.OutputPtr);
        }

        public void init(uint* input, Graphics _device)
        {
            // input parameter retained for InterfaceGraphic contract; pipeline
            // sources from NesCore.digitalFrameRgb (Phase A4b/C-3) and ignores it.
            _pipeline.Init();
            NativeGDI.initHighSpeed(_device, _pipeline.OutputW, _pipeline.OutputH,
                                    _pipeline.OutputPtr, 0, 0);
            PublishRenderOutput();
        }

        // Headless init: allocate buffers without GDI device (for benchmark mode)
        public void initHeadless(uint* input)
        {
            _pipeline.Init();
            PublishRenderOutput();
        }

        private void PublishRenderOutput()
        {
            NesCore.RenderOutputPtr = _pipeline.OutputPtr;
            NesCore.RenderOutputW = _pipeline.OutputW;
            NesCore.RenderOutputH = _pipeline.OutputH;
        }

        public void Render()
        {
            RenderFilter();
            NativeGDI.DrawImageHighSpeedtoDevice();
        }

        // Run filter pipeline only (no GDI draw) — used by headless benchmark
        public void RenderFilter() => _pipeline.Process();
    }
}
