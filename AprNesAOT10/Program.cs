using System;
using System.Drawing;
using System.Windows.Forms;

namespace AprNes
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // ── CLI benchmark mode: AprNesAOT10.exe --benchmark <rom> [seconds] [output] ──
            if (args.Length >= 2 && args[0] == "--benchmark")
            {
                string rom     = args[1];
                int    seconds = args.Length >= 3 && int.TryParse(args[2], out int s) ? s : 10;
                string outFile = args.Length >= 4 ? args[3] : null;

                Func<byte[], int, int> aotRunner = NesCoreBenchmark.IsAvailable()
                    ? (rom2, sec) => NesCoreBenchmark.RunAotBenchmark(rom2, sec)
                    : (Func<byte[], int, int>)null;

                BenchmarkRunner.Run(rom, seconds, outFile, ".NET 10 RyuJIT", aotRunner);
                return;
            }

            // ── Normal WinForms mode ──────────────────────────────────────────────────
            Application.SetDefaultFont(new Font("Microsoft Sans Serif", 8.25F));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var ui = AprNesUI.GetInstance();
            ui.AddAotBenchmarkMenuItem();
            Application.Run(ui);
        }
    }
}
