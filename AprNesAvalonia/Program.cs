using Avalonia;
using AprNes;
using System;

namespace AprNesAvalonia;

class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Headless mode: --rom / --benchmark / --perf → TestRunner (no GUI)
        bool headless = false;
        foreach (string a in args)
        {
            if (a == "--rom" || a == "--benchmark" || a == "--perf")
            { headless = true; break; }
        }

        if (headless)
        {
            // Avalonia platform must be initialised for Bitmap (used in SaveScreenshot)
            BuildAvaloniaApp().SetupWithoutStarting();
            return TestRunner.Run(args);
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
