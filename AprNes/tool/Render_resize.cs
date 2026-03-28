using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WINAPIGDI;
using XBRz_speed;
using ScalexFilter;
using ScanLineBuilder;

namespace AprNes
{
    // Filter type enum for two-stage resize pipeline
    public enum ResizeFilter
    {
        None,       // 1x pass-through
        NN,         // Nearest-Neighbor integer scaling
        XBRz,       // xBRZ pixel-art scaling (2x-6x)
        ScaleX      // Scale2x / Scale3x
    }

    // Unified two-stage resize renderer
    // Replaces Render_xbrz_1x~9x and Render_scanline_2x/4x/6x
    unsafe public class Render_resize : InterfaceGraphic
    {
        uint* _input;
        uint* _stage1Buf;   // intermediate buffer after stage1 (null if not needed)
        uint* _output;      // final output buffer
        int _s1Scale, _s2Scale;
        int _finalW, _finalH;
        int _stage1W, _stage1H;
        ResizeFilter _s1Filter, _s2Filter;
        bool _scanline;

        public Render_resize() { }

        public void Configure(ResizeFilter s1Filter, int s1Scale,
                              ResizeFilter s2Filter, int s2Scale,
                              bool scanline)
        {
            // xBRZ can only process 256×240 input (stage 1) due to static internal buffers
            if (s2Filter == ResizeFilter.XBRz)
                s2Filter = ResizeFilter.None;

            _s1Filter = s1Filter;
            _s1Scale  = s1Filter == ResizeFilter.None ? 1 : s1Scale;
            _s2Filter = s2Filter;
            _s2Scale  = s2Filter == ResizeFilter.None ? 1 : s2Scale;
            _scanline = scanline;

            _stage1W = 256 * _s1Scale;
            _stage1H = 240 * _s1Scale;
            _finalW  = _stage1W * _s2Scale;
            _finalH  = _stage1H * _s2Scale;
        }

        public void freeMem()
        {
            if (_stage1Buf != null) { Marshal.FreeHGlobal((IntPtr)_stage1Buf); _stage1Buf = null; }
            if (_output != null && _output != _input) { Marshal.FreeHGlobal((IntPtr)_output); _output = null; }
        }

        public Bitmap GetOutput()
        {
            uint* buf = _output != null ? _output : _input;
            return new Bitmap(_finalW, _finalH, _finalW * 4, PixelFormat.Format32bppRgb, (IntPtr)buf);
        }

        public void init(uint* input, Graphics _device)
        {
            _input = input;

            bool needStage1Buf = (_s1Filter != ResizeFilter.None) && (_s2Filter != ResizeFilter.None);
            bool needOutputBuf = (_s1Filter != ResizeFilter.None) || (_s2Filter != ResizeFilter.None);

            if (needStage1Buf)
                _stage1Buf = (uint*)Marshal.AllocHGlobal(sizeof(uint) * _stage1W * _stage1H);

            if (needOutputBuf)
                _output = (uint*)Marshal.AllocHGlobal(sizeof(uint) * _finalW * _finalH);
            else
                _output = _input; // 1x: direct output from NES

            NativeGDI.initHighSpeed(_device, _finalW, _finalH, _output, 0, 0);

            // Init xBRZ table (stage 1 only, fixed 256×240 input)
            if (_s1Filter == ResizeFilter.XBRz)
                HS_XBRz.initTable(256, 240);

            // Init scanline rates table
            if (_scanline)
                LibScanline.InitRates();

            NesCore.RenderOutputPtr = _output;
            NesCore.RenderOutputW = _finalW;
            NesCore.RenderOutputH = _finalH;
        }

        // Headless init: allocate buffers without GDI device (for benchmark mode)
        public void initHeadless(uint* input)
        {
            _input = input;

            bool needStage1Buf = (_s1Filter != ResizeFilter.None) && (_s2Filter != ResizeFilter.None);
            bool needOutputBuf = (_s1Filter != ResizeFilter.None) || (_s2Filter != ResizeFilter.None);

            if (needStage1Buf)
                _stage1Buf = (uint*)Marshal.AllocHGlobal(sizeof(uint) * _stage1W * _stage1H);

            if (needOutputBuf)
                _output = (uint*)Marshal.AllocHGlobal(sizeof(uint) * _finalW * _finalH);
            else
                _output = _input;

            if (_s1Filter == ResizeFilter.XBRz)
                HS_XBRz.initTable(256, 240);

            if (_scanline)
                LibScanline.InitRates();

            NesCore.RenderOutputPtr = _output;
            NesCore.RenderOutputW = _finalW;
            NesCore.RenderOutputH = _finalH;
        }

        public void Render()
        {
            RenderFilter();
            NativeGDI.DrawImageHighSpeedtoDevice();
        }

        // Run filter pipeline only (no GDI draw) — used by headless benchmark
        public void RenderFilter()
        {
            uint* src = _input;
            uint* dst;

            // Determine stage1 destination
            if (_s1Filter != ResizeFilter.None && _s2Filter != ResizeFilter.None)
                dst = _stage1Buf;
            else if (_s1Filter != ResizeFilter.None)
                dst = _output;
            else
                dst = null; // no stage1

            // Stage 1
            if (_s1Filter != ResizeFilter.None)
            {
                ApplyFilter(_s1Filter, _s1Scale, src, dst, 256, 240);
                src = dst;
            }

            // Stage 2
            if (_s2Filter != ResizeFilter.None)
            {
                ApplyFilter(_s2Filter, _s2Scale, src, _output, _stage1W, _stage1H);
            }

            // Scanline post-process (in-place on output)
            if (_scanline)
                LibScanline.ApplyInPlace(_output, _finalW, _finalH);
        }

        static void ApplyFilter(ResizeFilter filter, int scale, uint* src, uint* dst, int srcW, int srcH)
        {
            switch (filter)
            {
                case ResizeFilter.XBRz:
                    HS_XBRz.ScaleImage(src, dst, scale);
                    break;

                case ResizeFilter.ScaleX:
                    if (scale == 2)
                        ScalexTool.toScale2x_dx(src, srcW, srcH, dst);
                    else
                        ScalexTool.toScale3x_dx(src, srcW, srcH, dst);
                    break;

                case ResizeFilter.NN:
                    NearestNeighborScale(src, srcW, srcH, dst, scale);
                    break;
            }
        }

        // Nearest-Neighbor integer scaling — pixel copy, near-zero CPU cost
        static void NearestNeighborScale(uint* src, int srcW, int srcH, uint* dst, int scale)
        {
            int dstW = srcW * scale;

            Parallel.For(0, srcH, y =>
            {
                uint* srcRow = src + y * srcW;
                uint* dstBase = dst + y * scale * dstW;

                // Fill first output row
                uint* dstRow0 = dstBase;
                for (int x = 0; x < srcW; x++)
                {
                    uint px = srcRow[x];
                    int dstX = x * scale;
                    for (int sx = 0; sx < scale; sx++)
                        dstRow0[dstX + sx] = px;
                }

                // Duplicate first row to remaining rows in this block
                int rowBytes = dstW * sizeof(uint);
                for (int sy = 1; sy < scale; sy++)
                    Buffer.MemoryCopy(dstRow0, dstBase + sy * dstW, rowBytes, rowBytes);
            });
        }
    }
}
