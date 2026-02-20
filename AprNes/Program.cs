using System;
using System.Windows.Forms;

namespace AprNes
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                return TestRunner.Run(args);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(AprNesUI.GetInstance());
            return 0;
        }
    }
}
