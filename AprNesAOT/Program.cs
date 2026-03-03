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
            // ── CLI benchmark mode: AprNesAOT.exe --benchmark <rom> [seconds] [output] ──
            if (args.Length >= 2 && args[0] == "--benchmark")
            {
                string rom     = args[1];
                int    seconds = args.Length >= 3 && int.TryParse(args[2], out int s) ? s : 10;
                string outFile = args.Length >= 4 ? args[3] : null;

                // header（benchmark.ps1 會把 .NET Fx 的結果 append 進來，這裡不寫 header）
                // 直接 append，header 由 ps1 呼叫 AprNes.exe 時寫入
                Func<byte[], int, int> aotRunner = NesCoreBenchmark.IsAvailable()
                    ? (rom2, sec) => NesCoreBenchmark.RunAotBenchmark(rom2, sec)
                    : (Func<byte[], int, int>)null;

                BenchmarkRunner.Run(rom, seconds, outFile, ".NET 8 RyuJIT", aotRunner);
                return;
            }

            // ── Normal WinForms mode ──────────────────────────────────────────────────
            // .NET 8 預設 Form 字體為 Segoe UI 9pt，.NET Framework 為 Microsoft Sans Serif 8.25pt
            // AutoScaleMode=Font 用 Form 字體量測縮放比，設回相同字體確保 scale factor = 1.0
            Application.SetDefaultFont(new Font("Microsoft Sans Serif", 8.25F));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var ui = AprNesUI.GetInstance();
            ui.AddAotBenchmarkMenuItem(); // AprNesAOT only – injects JIT vs AOT benchmark menu
            Application.Run(ui);
        }
    }
}
