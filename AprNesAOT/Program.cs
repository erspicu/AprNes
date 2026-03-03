using System;
using System.Windows.Forms;

namespace AprNes
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var ui = AprNesUI.GetInstance();
            ui.AddAotBenchmarkMenuItem(); // AprNesAOT only – injects JIT vs AOT benchmark menu
            Application.Run(ui);
        }
    }
}
