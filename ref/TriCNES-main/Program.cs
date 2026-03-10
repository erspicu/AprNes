using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TriCNES
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            // Headless test mode: --rom triggers TestRunner
            if (args != null && args.Any(a => a == "--rom"))
            {
                return TestRunner.Run(args);
            }

            // Normal GUI mode
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TriCNESGUI());
            return 0;
        }
    }
}
