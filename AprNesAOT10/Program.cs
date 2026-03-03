using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AprNes
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // ── CLI benchmark mode ────────────────────────────────────────────
            if (args.Length >= 2 && (args[0] == "--benchmark" || args[0] == "--benchmark-simd" || args[0] == "--benchmark-nosimd"))
            {
                string rom     = args[1];
                int    seconds = args.Length >= 3 && int.TryParse(args[2], out int s) ? s : 10;
                string outFile = args.Length >= 4 ? args[3] : null;

                if (args[0] == "--benchmark-simd")
                {
                    // SIMD 對比模式：同 ROM 分別跑 SIMD ON / OFF
                    RunSimdCompare(rom, seconds, outFile);
                }
                else if (args[0] == "--benchmark-nosimd")
                {
                    // 獨立 process 模式：強制 SIMD OFF
                    NesCore.SIMDEnabled = false;
                    BenchmarkRunner.Run(rom, seconds, outFile, ".NET 10 RyuJIT [SIMD OFF]");
                }
                else
                {
                    Func<byte[], int, int> aotRunner = NesCoreBenchmark.IsAvailable()
                        ? (rom2, sec) => NesCoreBenchmark.RunAotBenchmark(rom2, sec)
                        : (Func<byte[], int, int>)null;
                    BenchmarkRunner.Run(rom, seconds, outFile, ".NET 10 RyuJIT", aotRunner);
                }
                return;
            }

            // ── Normal WinForms mode ──────────────────────────────────────────
            Application.SetDefaultFont(new Font("Microsoft Sans Serif", 8.25F));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var ui = AprNesUI.GetInstance();
            ui.AddAotBenchmarkMenuItem();
            Application.Run(ui);
        }

        static void RunSimdCompare(string rom, int seconds, string outFile)
        {
            if (!System.IO.File.Exists(rom))
            {
                Console.WriteLine($"[ERROR] ROM not found: {rom}");
                System.Environment.Exit(1);
            }
            byte[] romBytes = System.IO.File.ReadAllBytes(rom);

            Console.WriteLine($"[SIMD Compare] .NET 10 RyuJIT  ROM: {System.IO.Path.GetFileName(rom)}  {seconds}s each");

            // SIMD ON
            NesCore.SIMDEnabled = true;
            Console.Write("[SIMD  ON ] running ...");
            int framesOn = BenchmarkRunner.RunJit(romBytes, seconds);
            Console.WriteLine($"  {framesOn} frames  ({framesOn / (float)seconds:F1} FPS)");

            // SIMD OFF
            NesCore.SIMDEnabled = false;
            Console.Write("[SIMD  OFF] running ...");
            int framesOff = BenchmarkRunner.RunJit(romBytes, seconds);
            Console.WriteLine($"  {framesOff} frames  ({framesOff / (float)seconds:F1} FPS)");

            NesCore.SIMDEnabled = true; // 還原

            float diff = (framesOn - framesOff) / (float)seconds;
            float pct  = (framesOn - framesOff) / (float)framesOff * 100f;
            Console.WriteLine($"[SIMD gain] +{diff:F1} FPS  ({pct:+0.0;-0.0}%)");

            if (!string.IsNullOrEmpty(outFile))
            {
                var sb = new StringBuilder();
                sb.AppendLine($"=== SIMD Compare (.NET 10 RyuJIT) ===");
                sb.AppendLine($"ROM  : {System.IO.Path.GetFileName(rom)}");
                sb.AppendLine($"Time : {seconds} sec each");
                sb.AppendLine($"Date : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();
                sb.AppendLine($"SIMD  ON  : {framesOn,7} frames   {framesOn / (float)seconds,8:F1} FPS");
                sb.AppendLine($"SIMD  OFF : {framesOff,7} frames   {framesOff / (float)seconds,8:F1} FPS");
                sb.AppendLine($"SIMD gain : {diff:+0.0;-0.0} FPS  ({pct:+0.0;-0.0}%)");
                sb.AppendLine();
                System.IO.File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8);
                Console.WriteLine($"[Saved] {outFile}");
            }
        }
    }
}
