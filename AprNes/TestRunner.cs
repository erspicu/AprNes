using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using NesTestFramework;

namespace AprNes
{
    unsafe static class TestRunner
    {
        /// <summary>
        /// Check if args represent a simple blargg test (--rom + --wait-result only).
        /// If so, use the NesTestFramework BlarggTestRunner for a clean interface demo.
        /// </summary>
        static int? TryFrameworkPath(string[] args)
        {
            // Only activate for simple: --rom X --wait-result [--max-wait N] [--region R]
            // Skip if any advanced features are requested
            string[] advancedFlags = { "--screenshot", "--timed-screenshots", "--benchmark",
                "--analog", "--ultra-analog", "--crt", "--dump-ac-results", "--dump-debug",
                "--log", "--time", "--input", "--expected-crc", "--pass-on-stable",
                "--accuracy", "--soft-reset", "--audio-dsp", "--audio-mode" };

            if (!args.Contains("--wait-result")) return null;
            if (args.Any(a => advancedFlags.Contains(a))) return null;

            // Parse minimal args
            string romPath = null;
            int maxWait = 15;
            NesRegion region = NesRegion.NTSC;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--rom": if (i + 1 < args.Length) romPath = args[++i]; break;
                    case "--max-wait": if (i + 1 < args.Length) int.TryParse(args[++i], out maxWait); break;
                    case "--region":
                        if (i + 1 < args.Length)
                        {
                            string r = args[++i].ToLowerInvariant();
                            if (r == "pal") region = NesRegion.PAL;
                            else if (r == "dendy") region = NesRegion.Dendy;
                        }
                        break;
                }
            }

            if (romPath == null || !File.Exists(romPath)) return null;

            // Build a TestDefinition from the args
            string suite = Path.GetDirectoryName(romPath);
            string rom = Path.GetFileName(romPath);
            var test = new TestDefinition(suite ?? ".", rom, maxWait, region);

            // Use framework
            using (var emu = new AprNesAdapter.AprNesEmulatorCore())
            {
                var runner = new BlarggTestRunner(emu);
                // RunTest expects romDir + suite/rom, but we have the full path.
                // Override: pass parent dir as romDir, suite=".", rom=filename won't work.
                // Instead, pass the dir containing the ROM as romDir with suite="."
                string romDir = Path.GetDirectoryName(Path.GetFullPath(romPath));
                test.Suite = ".";
                test.Rom = rom;

                var result = runner.RunTest(test, romDir);

                string name = Path.GetFileNameWithoutExtension(rom);
                if (result.Passed)
                {
                    Console.WriteLine($"PASS | {rom} | {name}");
                    Console.WriteLine($"\n{result.ResultText}");
                    return 0;
                }
                else
                {
                    Console.WriteLine($"FAIL({result.ResultCode}) | {rom} | ({result.DetectionMethod}: {result.ResultText})");
                    return result.ResultCode == 0 ? 1 : result.ResultCode;
                }
            }
        }

        public static int Run(string[] args)
        {
            // Use NesTestFramework path when explicitly requested via --use-framework
            if (args.Contains("--use-framework"))
            {
                var fwResult = TryFrameworkPath(args);
                if (fwResult.HasValue) return fwResult.Value;
            }

            // Fall back to full-featured TestRunnerCore for advanced scenarios
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
