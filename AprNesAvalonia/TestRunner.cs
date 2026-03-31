using System;
using System.IO;

namespace AprNes
{
    unsafe static class TestRunner
    {
        public static int Run(string[] args)
        {
            // Wire up platform-specific delegates
            TestRunnerCore.GetBaseDirectoryFn = () => AppContext.BaseDirectory;
            TestRunnerCore.SaveScreenshotFn = SaveScreenshot;

            // No benchmark filter pipeline on Avalonia (Render_resize is WinForms-only)
            TestRunnerCore.BenchmarkFilterInitFn = null;
            TestRunnerCore.BenchmarkFilterStepFn = null;
            TestRunnerCore.BenchmarkFilterCleanupFn = null;
            TestRunnerCore.BenchmarkFilterDescFn = null;

            return TestRunnerCore.Run(args);
        }

        static void SaveScreenshot(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Use Avalonia Bitmap — no System.Drawing dependency
            // NesCore.ScreenBuf1x: uint* 0xFFRRGGBB, little-endian = B G R A = Bgra8888
            var bmp = new Avalonia.Media.Imaging.Bitmap(
                Avalonia.Platform.PixelFormats.Bgra8888,
                Avalonia.Platform.AlphaFormat.Unpremul,
                (nint)NesCore.ScreenBuf1x,
                new Avalonia.PixelSize(256, 240),
                new Avalonia.Vector(96, 96),
                256 * 4);
            using var fs = File.Create(path);
            bmp.Save(fs);
            bmp.Dispose();
        }
    }
}
