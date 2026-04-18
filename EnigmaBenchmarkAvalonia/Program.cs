using Avalonia;
using System;
using System.Threading.Tasks;

namespace EnigmaBenchmarkAvalonia;

class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Catch absolutely everything — a silent async-void exception has
        // been killing the process after benchmark completion without any
        // log line; we need the stack trace to diagnose.
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Console.Error.WriteLine("[UNHANDLED AppDomain] " + e.ExceptionObject);
        };
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Console.Error.WriteLine("[UNOBSERVED Task] " + e.Exception);
            e.SetObserved();
        };

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
