using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace AprNes
{
    unsafe static class TestRunner
    {
        public static int Run(string[] args)
        {
            // Wire up platform-specific delegates
            TestRunnerCore.GetBaseDirectoryFn = () =>
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            TestRunnerCore.SaveScreenshotFn = SaveScreenshot;

            // Benchmark filter pipeline (uses Render_resize, WinForms-only)
            Render_resize filterRenderer = null;

            TestRunnerCore.BenchmarkFilterInitFn = (filterArgs, _) =>
            {
                string stage1 = string.IsNullOrEmpty(filterArgs[0]) ? null : filterArgs[0];
                string stage2 = string.IsNullOrEmpty(filterArgs[1]) ? null : filterArgs[1];
                bool scanline = filterArgs[2] == "1";

                filterRenderer = new Render_resize();
                var s1Filter = ParseResizeFilter(stage1);
                int s1Scale  = ParseResizeScale(stage1);
                var s2Filter = ParseResizeFilter(stage2);
                int s2Scale  = ParseResizeScale(stage2);
                filterRenderer.Configure(s1Filter, s1Scale, s2Filter, s2Scale, scanline);
                filterRenderer.initHeadless(NesCore.ScreenBuf1x);
            };

            TestRunnerCore.BenchmarkFilterStepFn = () =>
            {
                if (filterRenderer != null)
                    filterRenderer.RenderFilter();
            };

            TestRunnerCore.BenchmarkFilterCleanupFn = () =>
            {
                if (filterRenderer != null)
                {
                    filterRenderer.freeMem();
                    filterRenderer = null;
                }
            };

            TestRunnerCore.BenchmarkFilterDescFn = (stage1, stage2, scanline) =>
                FormatFilterDesc(stage1, stage2, scanline);

            return TestRunnerCore.Run(args);
        }

        static void SaveScreenshot(string path)
        {
            // Use analog buffer (1024×840) when AnalogEnabled, otherwise regular 256×240
            if (NesCore.AnalogEnabled && NesCore.AnalogScreenBuf != null)
            {
                const int W = 1024, H = 840;
                Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, W, H),
                    ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                uint* src = NesCore.AnalogScreenBuf;
                byte* dst = (byte*)data.Scan0;
                int stride = data.Stride;
                for (int y = 0; y < H; y++)
                {
                    uint* dstRow = (uint*)(dst + y * stride);
                    uint* srcRow = src + y * W;
                    for (int x = 0; x < W; x++) dstRow[x] = srcRow[x];
                }
                bmp.UnlockBits(data);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                bmp.Save(path, ImageFormat.Png);
                bmp.Dispose();
                return;
            }

            {
                Bitmap bmp = new Bitmap(256, 240, PixelFormat.Format32bppArgb);
                BitmapData data = bmp.LockBits(
                    new Rectangle(0, 0, 256, 240),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);

                uint* src = NesCore.ScreenBuf1x;
                byte* dst = (byte*)data.Scan0;
                int stride = data.Stride;

                for (int y = 0; y < 240; y++)
                {
                    uint* dstRow = (uint*)(dst + y * stride);
                    for (int x = 0; x < 256; x++)
                    {
                        dstRow[x] = src[y * 256 + x];
                    }
                }

                bmp.UnlockBits(data);

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                bmp.Save(path, ImageFormat.Png);
                bmp.Dispose();
            }
        }

        // Parse filter spec: "xbrz_4" → XBRz, "nn_3" → NN, "scalex_2" → ScaleX, null/"none" → None
        static ResizeFilter ParseResizeFilter(string spec)
        {
            if (string.IsNullOrEmpty(spec) || spec == "none") return ResizeFilter.None;
            string prefix = spec.Split('_')[0].ToLowerInvariant();
            switch (prefix)
            {
                case "xbrz":   return ResizeFilter.XBRz;
                case "scalex": return ResizeFilter.ScaleX;
                case "nn":     return ResizeFilter.NN;
                default:       return ResizeFilter.None;
            }
        }

        // Parse scale from spec: "xbrz_4" → 4, "none" → 1
        static int ParseResizeScale(string spec)
        {
            if (string.IsNullOrEmpty(spec) || spec == "none") return 1;
            string[] parts = spec.Split('_');
            int s;
            return (parts.Length == 2 && int.TryParse(parts[1], out s)) ? s : 1;
        }

        // Format filter description for log output
        static string FormatFilterDesc(string stage1, string stage2, bool scanline)
        {
            string s1 = string.IsNullOrEmpty(stage1) ? "none" : stage1;
            string s2 = string.IsNullOrEmpty(stage2) ? "none" : stage2;
            string desc = "S1=" + s1 + ", S2=" + s2;
            if (scanline) desc += ", Scanline=ON";
            return desc;
        }
    }
}
