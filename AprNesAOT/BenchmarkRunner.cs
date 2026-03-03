// BenchmarkRunner – headless JIT vs AOT DLL benchmark (CLI mode)
// Called from Program.cs when --benchmark argument is passed.

using System;
using System.IO;
using System.Text;
using System.Threading;

namespace AprNes
{
    static class BenchmarkRunner
    {
        public static void Run(string romPath, int seconds, string outputFile)
        {
            Console.WriteLine("=== AprNes JIT vs AOT Benchmark ===");
            Console.WriteLine($"ROM    : {Path.GetFileName(romPath)}");
            Console.WriteLine($"Time   : {seconds} sec each");
            Console.WriteLine($"Date   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();

            if (!File.Exists(romPath))
            {
                Console.WriteLine($"[ERROR] ROM not found: {romPath}");
                Environment.Exit(1);
            }

            byte[] rom = File.ReadAllBytes(romPath);

            // ── JIT benchmark ───────────────────────────────────────────────
            Console.Write($"[1/2] JIT (.NET 8 managed)  running {seconds}s ...");
            int jitFrames = RunJit(rom, seconds);
            Console.WriteLine($"  {jitFrames} frames  ({jitFrames / (float)seconds:F1} FPS)");

            // ── AOT DLL benchmark ───────────────────────────────────────────
            int aotFrames = -1;
            bool aotAvail = NesCoreBenchmark.IsAvailable();
            if (aotAvail)
            {
                Console.Write($"[2/2] AOT (NesCoreNative.dll) running {seconds}s ...");
                aotFrames = NesCoreBenchmark.RunAotBenchmark(rom, seconds);
                if (aotFrames >= 0)
                    Console.WriteLine($"  {aotFrames} frames  ({aotFrames / (float)seconds:F1} FPS)");
                else
                    Console.WriteLine("  FAILED (init error)");
            }
            else
            {
                Console.WriteLine("[2/2] AOT  SKIPPED (NesCoreNative.dll not found)");
            }

            // ── Build result text ───────────────────────────────────────────
            var sb = new StringBuilder();
            sb.AppendLine("=== AprNes JIT vs AOT Benchmark ===");
            sb.AppendLine($"ROM    : {Path.GetFileName(romPath)}");
            sb.AppendLine($"Time   : {seconds} sec each");
            sb.AppendLine($"Date   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"OS     : {Environment.OSVersion}");
            sb.AppendLine($"CPU    : {GetCpuName()}");
            sb.AppendLine();
            sb.AppendLine($"JIT (.NET 8 managed) : {jitFrames,7} frames   {jitFrames / (float)seconds,8:F1} FPS");

            if (aotFrames >= 0)
            {
                sb.AppendLine($"AOT (NesCoreNative)  : {aotFrames,7} frames   {aotFrames / (float)seconds,8:F1} FPS");
                sb.AppendLine();
                float ratio = (float)aotFrames / jitFrames;
                sb.AppendLine($"AOT / JIT ratio : {ratio:F4}x  ({(ratio >= 1 ? "AOT faster" : "JIT faster")} by {Math.Abs(ratio - 1) * 100:F1}%)");
            }
            else if (aotAvail)
            {
                sb.AppendLine("AOT (NesCoreNative)  : FAILED");
            }
            else
            {
                sb.AppendLine("AOT (NesCoreNative)  : SKIPPED (DLL not found)");
            }

            string result = sb.ToString();
            Console.WriteLine();
            Console.WriteLine(result);

            // ── Write to file ───────────────────────────────────────────────
            if (!string.IsNullOrEmpty(outputFile))
            {
                File.WriteAllText(outputFile, result, Encoding.UTF8);
                Console.WriteLine($"Results saved to: {Path.GetFullPath(outputFile)}");
            }
        }

        static int RunJit(byte[] rom, int seconds)
        {
            int frames = 0;
            NesCore.exit = false;
            NesCore.init(rom);
            NesCore.LimitFPS = false;

            EventHandler counter = (s, e) => Interlocked.Increment(ref frames);
            NesCore.VideoOutput += counter;

            var t = new Thread(NesCore.run) { IsBackground = true };
            t.Start();
            Thread.Sleep(seconds * 1000);
            NesCore.exit = true;
            NesCore._event.Set();
            t.Join(2000);

            NesCore.VideoOutput -= counter;
            return frames;
        }

        static string GetCpuName()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return key?.GetValue("ProcessorNameString") as string ?? "Unknown";
            }
            catch { return "Unknown"; }
        }
    }
}
