# TriCNES Headless Benchmark Patch

The three `bench_profile_tricnes.bat` / `run_perfview_tricnes.bat` /
`run_perfview_pmu_tricnes.bat` scripts assume the upstream TriCNES
codebase has been patched to accept a `--benchmark` CLI flag.

This patch is kept *outside* the AprNes source tree (because TriCNES is
a reference-only checkout under `ref/`). To re-apply after a fresh
upstream sync, replace `ref/TriCNES-main-20260410/Program.cs` with the
content below.

---

## Usage after patch

```bash
TriCNES.exe --benchmark <rom-path> [seconds=30]
```

Output lands in `<rom-dir>/tricnes_bench_result.txt` (WinExe can't
reliably stream stdout; result is also written to a file).

## File: `ref/TriCNES-main-20260410/Program.cs` (full replacement)

```csharp
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
```

## Measurement notes (2026-04-15, AMD Ryzen 7 3700X, .NET Framework 4.8 Release)

- **1x FPS: 64.49** (ny2011, 30s)
- vs AprNes WinForms Debug 1x audio-0: ~118 FPS (**~1.83× faster**)
- JIT profile: work spread across ~14 methods at 2-10% each (vs AprNes
  ~4 methods dominating), reflecting TriCNES's per-component dispatch
  architecture (`_EmulatorCore` → `_6502` / `_EmulatePPU` / `_EmulateAPU`)
- L1 I-cache miss rate: 0.59% global (healthy)
