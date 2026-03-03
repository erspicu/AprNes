// BenchmarkRunner – headless JIT benchmark (CLI mode, shared by AprNes + AprNesAOT)
// AOT DLL benchmark 由 AprNesAOT 的 Program.cs 另外傳入 aotRunner delegate 執行。

using System;
using System.IO;
using System.Text;
using System.Threading;

namespace AprNes
{
    static class BenchmarkRunner
    {
        /// <summary>
        /// 執行 JIT benchmark；若提供 aotRunner 也執行 AOT benchmark。
        /// 結果輸出至 console 並寫入 outputFile（若不為 null）。
        /// </summary>
        /// <param name="romPath">ROM 路徑</param>
        /// <param name="seconds">每項測試秒數</param>
        /// <param name="outputFile">結果輸出檔案（null 則不寫檔）</param>
        /// <param name="runtimeLabel">JIT 框架描述，例如 ".NET Framework 4.6.1" 或 ".NET 8 RyuJIT"</param>
        /// <param name="aotRunner">可選的 AOT benchmark 函式，傳入 (romBytes, seconds) 回傳 frames；-1 表示失敗</param>
        public static void Run(string romPath, int seconds, string outputFile,
            string runtimeLabel, Func<byte[], int, int> aotRunner = null)
        {
            Console.WriteLine($"[JIT] {runtimeLabel}  running {seconds}s ...");

            if (!File.Exists(romPath))
            {
                Console.WriteLine($"[ERROR] ROM not found: {romPath}");
                Environment.Exit(1);
            }

            byte[] rom = File.ReadAllBytes(romPath);

            // ── JIT ────────────────────────────────────────────────────────
            int jitFrames = RunJit(rom, seconds);
            Console.WriteLine($"      {jitFrames} frames  ({jitFrames / (float)seconds:F1} FPS)");

            // ── AOT (optional) ─────────────────────────────────────────────
            int aotFrames = -2; // -2 = not run
            if (aotRunner != null)
            {
                Console.Write($"[AOT] NesCoreNative.dll  running {seconds}s ...");
                aotFrames = aotRunner(rom, seconds);
                if (aotFrames >= 0)
                    Console.WriteLine($"  {aotFrames} frames  ({aotFrames / (float)seconds:F1} FPS)");
                else
                    Console.WriteLine("  FAILED");
            }

            // ── 輸出 ───────────────────────────────────────────────────────
            var sb = new StringBuilder();
            sb.AppendLine($"JIT [{runtimeLabel}] : {jitFrames,7} frames   {jitFrames / (float)seconds,8:F1} FPS");
            if (aotFrames >= 0)
                sb.AppendLine($"AOT [NesCoreNative]     : {aotFrames,7} frames   {aotFrames / (float)seconds,8:F1} FPS");
            else if (aotFrames == -1)
                sb.AppendLine("AOT [NesCoreNative]     : FAILED");

            string result = sb.ToString();
            Console.WriteLine(result);

            if (!string.IsNullOrEmpty(outputFile))
            {
                // append（由 benchmark.ps1 先清檔，各次 run 疊加）
                File.AppendAllText(outputFile, result, Encoding.UTF8);
            }
        }

        public static int RunJit(byte[] rom, int seconds)
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
                using (var key = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    return key?.GetValue("ProcessorNameString") as string ?? "Unknown";
            }
            catch { return "Unknown"; }
        }

        public static string BuildHeader(string romPath, int seconds)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== AprNes Benchmark ===");
            sb.AppendLine($"ROM  : {Path.GetFileName(romPath)}");
            sb.AppendLine($"Time : {seconds} sec each");
            sb.AppendLine($"Date : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"OS   : {Environment.OSVersion}");
            sb.AppendLine($"CPU  : {GetCpuName()}");
            sb.AppendLine();
            return sb.ToString();
        }
    }
}

