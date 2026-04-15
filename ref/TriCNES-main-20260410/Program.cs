using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TriCNES
{
    internal static class Program
    {
        // WinExe has no stdout by default; attach to parent cmd.exe so prints are visible.
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int processId);
        const int ATTACH_PARENT_PROCESS = -1;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Headless benchmark mode — similar to AprNes --benchmark.
            //   TriCNES.exe --benchmark <rom-path> [seconds=30]
            if (args.Length >= 2 && args[0] == "--benchmark")
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
                string rom = args[1];
                int seconds = args.Length >= 3 && int.TryParse(args[2], out int s) ? s : 30;
                RunBenchmark(rom, seconds);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TriCNESGUI());
        }

        static void RunBenchmark(string romPath, int seconds)
        {
            if (!File.Exists(romPath))
            {
                Console.WriteLine($"ERROR: ROM not found: {romPath}");
                return;
            }

            Emulator emu = new Emulator();
            emu.PPU_DecodeSignal = false;    // raw RGB output, no NTSC signal decoding (matches AprNes 1x)
            emu.PPU_ShowRawNTSCSignal = false;
            emu.PPU_ShowScreenBorders = false;
            emu.PPUClock = 0;

            Cartridge cart = new Cartridge(romPath);
            emu.Cart = cart;
            cart.Emu = emu;

            Console.WriteLine($"TriCNES benchmark: {Path.GetFileName(romPath)} for {seconds}s");

            int frames = 0;
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                emu._CoreFrameAdvance();
                frames++;
            }
            sw.Stop();

            double fps = frames / sw.Elapsed.TotalSeconds;
            string line = $"BENCHMARK: {frames} frames in {sw.Elapsed.TotalSeconds:F2}s = {fps:F2} FPS";
            Console.WriteLine(line);
            // WinExe may not flush to attached console reliably; also write to a known file.
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(romPath), "tricnes_bench_result.txt"), line + Environment.NewLine); } catch {}
        }
    }
}
