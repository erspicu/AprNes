using System;
using System.Threading.Tasks;
using XBRz_speed;
using ScalexFilter;
using ScanLineBuilder;

namespace AprNes
{
    /// <summary>
    /// Platform-agnostic two-stage resize pipeline (no GDI).
    ///
    /// Single source of truth for the digital filter chain (xBRZ / Scalex / NN /
    /// Scanline). Both NetFx (via Render_resize wrapper) and Avalonia
    /// (EmulatorEngine.OutputOneFrame) compose this class.
    ///
    /// Reads NesCore.digitalFrameRgb at Process time (Phase C-3: emu thread
    /// pre-converts ntsc_rowPalettes → RGB before signaling render).
    /// </summary>
    public unsafe class RenderPipeline : IDisposable
    {
        private uint* _stage1Buf;
        private uint* _output;
        // Phase C-3: _output may alias NesCore.digitalFrameRgb in the 1× no-filter
        // case. _ownsOutput=true only when we allocated _output ourselves; FreeMem
        // only frees when owned.
        private bool _ownsOutput;
        private int _s1Scale, _s2Scale;
        private int _stage1W, _stage1H;
        private ResizeFilter _s1Filter, _s2Filter;
        private bool _scanline;
        private bool _initialized;

        public int OutputW { get; private set; } = 256;
        public int OutputH { get; private set; } = 240;
        public uint* OutputPtr => _output;
        public bool HasFilters => _s1Filter != ResizeFilter.None || _s2Filter != ResizeFilter.None || _scanline;
        public bool IsInitialized => _initialized;

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
            OutputW  = _stage1W * _s2Scale;
            OutputH  = _stage1H * _s2Scale;
        }

        public void Init()
        {
            FreeMem();

            bool needStage1Buf = _s1Filter != ResizeFilter.None && _s2Filter != ResizeFilter.None;
            bool needOutputBuf = _s1Filter != ResizeFilter.None || _s2Filter != ResizeFilter.None;

            if (needStage1Buf)
                _stage1Buf = (uint*)NesCore.AllocUnmanaged(sizeof(uint) * _stage1W * _stage1H);

            if (needOutputBuf)
            {
                _output = (uint*)NesCore.AllocUnmanaged(sizeof(uint) * OutputW * OutputH);
                _ownsOutput = true;
            }
            else
            {
                _output = NesCore.digitalFrameRgb; // 1×: read emu's pre-converted RGB
                _ownsOutput = false;
            }

            // Init xBRZ table (stage 1 only, fixed 256×240 input)
            if (_s1Filter == ResizeFilter.XBRz)
                HS_XBRz.initTable(256, 240);

            // Init scanline rates table
            if (_scanline)
                LibScanline.InitRates();

            _initialized = true;
        }

        public void Process()
        {
            if (!_initialized) return;

            // Phase C-3: emu pre-converts ntsc_rowPalettes → digitalFrameRgb at frame end.
            // Read the pre-converted RGB directly (race-free vs next frame's palette writes).
            uint* src = NesCore.digitalFrameRgb;
            uint* dst;

            // Determine stage1 destination
            if (_s1Filter != ResizeFilter.None && _s2Filter != ResizeFilter.None)
                dst = _stage1Buf;
            else if (_s1Filter != ResizeFilter.None)
                dst = _output;
            else
                dst = null;

            // Stage 1
            if (_s1Filter != ResizeFilter.None)
            {
                ApplyFilter(_s1Filter, _s1Scale, src, dst, 256, 240);
                src = dst;
            }

            // Stage 2
            if (_s2Filter != ResizeFilter.None)
                ApplyFilter(_s2Filter, _s2Scale, src, _output, _stage1W, _stage1H);

            // Scanline post-process (in-place on output)
            if (_scanline)
                LibScanline.ApplyInPlace(_output, OutputW, OutputH);
        }

        private static void ApplyFilter(ResizeFilter filter, int scale, uint* src, uint* dst, int srcW, int srcH)
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
        private static void NearestNeighborScale(uint* src, int srcW, int srcH, uint* dst, int scale)
        {
            int dstW = srcW * scale;
            Parallel.For(0, srcH, y =>
            {
                uint* srcRow = src + y * srcW;
                uint* dstBase = dst + y * scale * dstW;
                uint* dstRow0 = dstBase;
                for (int x = 0; x < srcW; x++)
                {
                    uint px = srcRow[x];
                    int dstX = x * scale;
                    for (int sx = 0; sx < scale; sx++)
                        dstRow0[dstX + sx] = px;
                }
                int rowBytes = dstW * sizeof(uint);
                for (int sy = 1; sy < scale; sy++)
                    Buffer.MemoryCopy(dstRow0, dstBase + sy * dstW, rowBytes, rowBytes);
            });
        }

        public void FreeMem()
        {
            if (_stage1Buf != null) { NesCore.FreeUnmanaged((IntPtr)_stage1Buf); _stage1Buf = null; }
            // Phase C-3: only free _output when we own it (not when aliasing
            // NesCore.digitalFrameRgb).
            if (_output != null && _ownsOutput) { NesCore.FreeUnmanaged((IntPtr)_output); }
            _output = null;
            _ownsOutput = false;
            _initialized = false;
        }

        public void Dispose() => FreeMem();
    }
}
