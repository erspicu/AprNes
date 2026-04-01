using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AprNes;
using XBRz_speed;
using ScalexFilter;
using ScanLineBuilder;

namespace AprNesAvalonia.Platform;

/// <summary>
/// Platform-agnostic two-stage resize pipeline (no GDI).
/// Extracted from AprNes Render_resize — same filter logic, pure pointer output.
/// </summary>
public unsafe class RenderPipeline : IDisposable
{
    private uint* _input;
    private uint* _stage1Buf;
    private uint* _output;
    private int _s1Scale, _s2Scale;
    private int _stage1W, _stage1H;
    private ResizeFilter _s1Filter, _s2Filter;
    private bool _scanline;
    private bool _initialized;

    public int OutputW { get; private set; } = 256;
    public int OutputH { get; private set; } = 240;
    public uint* OutputPtr => _output != null ? _output : _input;
    public bool HasFilters => _s1Filter != ResizeFilter.None || _s2Filter != ResizeFilter.None || _scanline;
    public bool IsInitialized => _initialized;

    public void Configure(ResizeFilter s1Filter, int s1Scale,
                          ResizeFilter s2Filter, int s2Scale,
                          bool scanline)
    {
        // xBRZ can only process 256x240 input (stage 1)
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

    public void Init(uint* input)
    {
        FreeMem();
        _input = input;

        bool needStage1Buf = _s1Filter != ResizeFilter.None && _s2Filter != ResizeFilter.None;
        bool needOutputBuf = _s1Filter != ResizeFilter.None || _s2Filter != ResizeFilter.None;

        if (needStage1Buf)
            _stage1Buf = (uint*)Marshal.AllocHGlobal(sizeof(uint) * _stage1W * _stage1H);

        if (needOutputBuf)
            _output = (uint*)Marshal.AllocHGlobal(sizeof(uint) * OutputW * OutputH);
        else
            _output = _input;

        if (_s1Filter == ResizeFilter.XBRz)
            HS_XBRz.initTable(256, 240);

        if (_scanline)
            LibScanline.InitRates();

        _initialized = true;
    }

    public void Process()
    {
        if (!_initialized) return;

        uint* src = _input;
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

        // Scanline post-process
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
        if (_stage1Buf != null) { Marshal.FreeHGlobal((IntPtr)_stage1Buf); _stage1Buf = null; }
        if (_output != null && _output != _input) { Marshal.FreeHGlobal((IntPtr)_output); _output = null; }
        _initialized = false;
    }

    public void Dispose() => FreeMem();
}
