using Avalonia;
using AprNes;
using System;

namespace AprNesAvalonia;

class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Headless mode: --rom / --perf → TestRunner (no GUI)
        // EXCEPT when --gui-benchmark is present (GUI mode with auto-loaded ROM)
        bool headless = false;
        bool guiBench = false;
        foreach (string a in args)
        {
            if (a == "--gui-benchmark") { guiBench = true; }
            else if (a == "--rom" || a == "--perf") { headless = true; }
        }
        if (guiBench) headless = false;

        if (headless)
        {
            // Avalonia platform must be initialised for Bitmap (used in SaveScreenshot)
            BuildAvaloniaApp().SetupWithoutStarting();
            return TestRunner.Run(args);
        }

        // GUI mode: parse CLI flags (subset of TestRunner's) for auto-config
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--crt-strategy" when i + 1 < args.Length:
                {
                    string s = args[++i].ToLowerInvariant();
                    AprNes.NesCore.CrtBackend wanted = s switch
                    {
                        "scalar" => AprNes.NesCore.CrtBackend.Scalar,
                        "simd"   => AprNes.NesCore.CrtBackend.Simd,
                        "gpu"    => AprNes.NesCore.CrtBackend.Gpu,
                        _        => AprNes.NesCore.Crt_GetBackend(),
                    };
                    AprNes.NesCore.Crt_SetBackend(wanted);
                    if (wanted == AprNes.NesCore.CrtBackend.Gpu)
                        AprNes.NesCore.CrtGpuRenderThreadActive = true;
                    break;
                }
                case "--gui-benchmark" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out int secs)) GuiBenchmark.DurationSec = secs;
                    break;
                case "--crt-shader" when i + 1 < args.Length:
                    // Explicit shader filename; bypasses LoadLatest discovery.
                    // e.g. --crt-shader crt_core_v1.sksl
                    ShaderLoader.CliOverride = args[++i];
                    break;
                case "--rom" when i + 1 < args.Length:
                    GuiBenchmark.RomPath = args[++i];
                    break;
                case "--analog":
                    GuiBenchmark.AnalogEnabled = true;
                    break;
                case "--ultra-analog":
                    GuiBenchmark.AnalogEnabled = true;
                    GuiBenchmark.UltraAnalog = true;
                    break;
                case "--crt":
                    GuiBenchmark.CrtEnabled = true;
                    break;
                case "--analog-size" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out int sz)) GuiBenchmark.AnalogSize = sz;
                    break;
                case "--analog-output" when i + 1 < args.Length:
                {
                    string m = args[++i].ToUpperInvariant();
                    GuiBenchmark.AnalogOutput = m switch
                    {
                        "RF"     => AprNes.AnalogOutputMode.RF,
                        "SVIDEO" => AprNes.AnalogOutputMode.SVideo,
                        _        => AprNes.AnalogOutputMode.AV,
                    };
                    break;
                }
                case "--audio-dsp":
                    GuiBenchmark.AudioDsp = true;
                    break;
                case "--audio-mode" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out int am)) GuiBenchmark.AudioMode = am;
                    break;
            }
        }

        // All CLI flags parsed; NOW init CrtGpuRenderThread so ShaderLoader.CliOverride
        // (set via --crt-shader above) is honored.
        if (AprNes.NesCore.CrtGpuRenderThreadActive)
            CrtGpuRenderThread.Init();

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
