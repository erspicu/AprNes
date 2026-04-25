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

            // Pick source buffer + dimensions:
            //   Analog mode → AnalogScreenBuf at Crt_DstW × Crt_DstH (varies with AnalogSize)
            //   Normal mode → ntsc_rowPalettes (palette indices) converted on the fly via NesColors
            int w, h;
            uint* src;
            uint* tempRgb = null;
            if (NesCore.AnalogEnabled && NesCore.AnalogScreenBuf != null)
            {
                w = NesCore.Crt_DstW;
                h = NesCore.Crt_DstH;
                src = NesCore.AnalogScreenBuf;
            }
            else
            {
                w = 256;
                h = 240;
                if (NesCore.ntsc_rowPalettes == null || NesCore.NesColors == null)
                {
                    System.Console.Error.WriteLine(
                        "[Screenshot] palette buffer / NesColors not initialised; skipped");
                    return;
                }
                // Phase A5: convert palette indices → RGB into a one-shot scratch buffer.
                tempRgb = (uint*)System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(uint) * w * h);
                NesCore.Convert_PalIdxFrameToRGB(tempRgb);
                src = tempRgb;
            }

            if (src == null || w <= 0 || h <= 0)
            {
                System.Console.Error.WriteLine(
                    "[Screenshot] source buffer null or invalid dimensions; skipped");
                if (tempRgb != null) System.Runtime.InteropServices.Marshal.FreeHGlobal((nint)tempRgb);
                return;
            }

            // Bgra8888: NesCore writes 0xFFRRGGBB in uint; memory layout (little-endian)
            // is B G R A, matching Avalonia's Bgra8888 pixel format.
            var bmp = new Avalonia.Media.Imaging.Bitmap(
                Avalonia.Platform.PixelFormats.Bgra8888,
                Avalonia.Platform.AlphaFormat.Unpremul,
                (nint)src,
                new Avalonia.PixelSize(w, h),
                new Avalonia.Vector(96, 96),
                w * 4);
            try
            {
                using var fs = File.Create(path);
                bmp.Save(fs);
            }
            finally
            {
                bmp.Dispose();
                if (tempRgb != null) System.Runtime.InteropServices.Marshal.FreeHGlobal((nint)tempRgb);
            }
        }
    }
}
