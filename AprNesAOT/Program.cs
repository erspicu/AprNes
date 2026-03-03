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
