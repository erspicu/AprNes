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
        bool headless = false;
        foreach (string a in args)
        {
            if (a == "--rom" || a == "--perf")
            { headless = true; break; }
        }

        if (headless)
        {
            // Avalonia platform must be initialised for Bitmap (used in SaveScreenshot)
            BuildAvaloniaApp().SetupWithoutStarting();
            return TestRunner.Run(args);
        }

        // GUI mode: honor --crt-strategy CLI flag (same syntax as TestRunner)
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--crt-strategy")
            {
                string s = args[i + 1].ToLowerInvariant();
                AprNes.NesCore.CrtBackend wanted = s switch
                {
                    "scalar" => AprNes.NesCore.CrtBackend.Scalar,
                    "simd"   => AprNes.NesCore.CrtBackend.Simd,
                    "gpu"    => AprNes.NesCore.CrtBackend.Gpu,
                    _        => AprNes.NesCore.Crt_GetBackend(),
                };
                AprNes.NesCore.Crt_SetBackend(wanted);

                // Phase 3A: activate render-thread GPU path
                if (wanted == AprNes.NesCore.CrtBackend.Gpu)
                {
                    AprNes.NesCore.CrtGpuRenderThreadActive = true;
                    CrtGpuRenderThread.Init();
                }
                break;
            }
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
