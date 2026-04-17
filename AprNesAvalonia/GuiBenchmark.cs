// ════════════════════════════════════════════════════════════════════════
// GuiBenchmark.cs — GUI-mode automated benchmark harness
// ════════════════════════════════════════════════════════════════════════
// Lets us measure real GPU speedup from Phase 3A (render-thread SkSL) by
// running the full Avalonia GUI + render thread + emulator for a fixed
// duration, then printing stats to stdout before closing.
//
// Activation:
//   AprNesAvalonia.exe --gui-benchmark <secs> --rom <path> [other flags]
//
// Counters:
//   - RenderFrameCount: incremented in EmuDrawOperation.Render (what the
//     render thread actually paints)
//   - EmuFrameCount: incremented in MainWindow.OnFrameReady (emu-produced
//     frames ready for display; may differ from render count if vsync or
//     render-thread throttling drops frames)
//
// Output: a block of Console.WriteLine with duration / FPS / backend.
// ════════════════════════════════════════════════════════════════════════
using System;
using System.Diagnostics;
using System.Threading;

namespace AprNesAvalonia;

internal static class GuiBenchmark
{
    // Configured via --gui-benchmark CLI in Program.cs. 0 = disabled.
    public static int    DurationSec;
    public static string? RomPath;

    // Parsed CLI overrides captured in Program.cs, re-applied after INI
    public static bool? AnalogEnabled;
    public static bool? UltraAnalog;
    public static bool? CrtEnabled;
    public static int?  AnalogSize;
    public static AprNes.AnalogOutputMode? AnalogOutput;
    public static bool? AudioDsp;
    public static int?  AudioMode;

    public static void ApplyOverrides()
    {
        if (AnalogEnabled.HasValue) AprNes.NesCore.AnalogEnabled = AnalogEnabled.Value;
        if (UltraAnalog.HasValue)   AprNes.NesCore.UltraAnalog   = UltraAnalog.Value;
        if (CrtEnabled.HasValue)    AprNes.NesCore.CrtEnabled    = CrtEnabled.Value;
        if (AnalogSize.HasValue)    AprNes.NesCore.AnalogSize    = AnalogSize.Value;
        if (AnalogOutput.HasValue)  AprNes.NesCore.AnalogOutput  = AnalogOutput.Value;
        if (AudioDsp.HasValue)      AprNes.NesCore.AudioEnabled  = AudioDsp.Value;
        if (AudioMode.HasValue)     AprNes.NesCore.AudioMode     = AudioMode.Value;
    }

    public static bool IsActive { get; private set; }

    static long      _renderFrames;
    static long      _emuFrames;
    static Stopwatch? _sw;

    // Write to exe's own directory — avoids path manipulation issues when
    // launched from arbitrary working directories
    static readonly string _traceFile =
        System.IO.Path.Combine(AppContext.BaseDirectory, "gui_benchmark.trace.log");
    static readonly string _resultFile =
        System.IO.Path.Combine(AppContext.BaseDirectory, "gui_benchmark.log");

    public static void Trace(string msg)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_traceFile)!);
            System.IO.File.AppendAllText(_traceFile,
                $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
        }
        catch { }
    }

    public static void Start()
    {
        Trace("Start()");
        _renderFrames = 0;
        _emuFrames = 0;
        _sw = Stopwatch.StartNew();
        IsActive = true;
        Console.WriteLine($"=== GUI BENCHMARK START ({DurationSec}s) ===");
    }

    public static void NotifyRenderFrame()
    {
        if (IsActive) Interlocked.Increment(ref _renderFrames);
    }

    public static void NotifyEmuFrame()
    {
        if (IsActive) Interlocked.Increment(ref _emuFrames);
    }

    public static void Finish()
    {
        if (!IsActive || _sw == null) return;
        _sw.Stop();
        IsActive = false;

        double s = _sw.Elapsed.TotalSeconds;
        long rf = Interlocked.Read(ref _renderFrames);
        long ef = Interlocked.Read(ref _emuFrames);

        var lines = new[]
        {
            "",
            "=== GUI BENCHMARK RESULT ===",
            $"  Duration      : {s,10:F2} s",
            $"  Render frames : {rf,10}    ({rf / s:F2} FPS presented)",
            $"  Emu frames    : {ef,10}    ({ef / s:F2} FPS produced)",
            $"  CRT backend   : {AprNes.NesCore.Crt_GetBackend()}",
            $"  CrtGpu RT     : {AprNes.NesCore.CrtGpuRenderThreadActive}",
            $"  Analog        : {AprNes.NesCore.AnalogEnabled} (Ultra={AprNes.NesCore.UltraAnalog})",
            $"  CRT enabled   : {AprNes.NesCore.CrtEnabled}",
            $"  Analog size   : {AprNes.NesCore.AnalogSize}x",
            $"  Analog output : {AprNes.NesCore.AnalogOutput}",
            $"  Audio DSP     : {AprNes.NesCore.AudioEnabled} (mode={AprNes.NesCore.AudioMode})",
            $"  ROM           : {System.IO.Path.GetFileName(RomPath ?? "")}",
            "============================",
        };

        foreach (var ln in lines) Console.WriteLine(ln);

        // WinExe detaches stdout; mirror results to a file so the user always sees them.
        try { System.IO.File.WriteAllLines(_resultFile, lines); }
        catch { /* best-effort */ }
    }
}
