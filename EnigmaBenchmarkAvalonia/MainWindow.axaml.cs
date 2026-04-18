using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using EnigmaBenchmark.Core;
using EnigmaBenchmark.Crackers;
using EnigmaBenchmark.Presets;

namespace EnigmaBenchmarkAvalonia;

public partial class MainWindow : Window
{
    readonly Stopwatch _totalSw = new();

    // ── Palette (mirrors CSS in docs/readme.html for consistency) ──
    static readonly IBrush BrushText    = Brush.Parse("#D8DEE4");
    static readonly IBrush BrushMuted   = Brush.Parse("#8A93A0");
    static readonly IBrush BrushAmber   = Brush.Parse("#FFD060");
    static readonly IBrush BrushGreen   = Brush.Parse("#80FF80");
    static readonly IBrush BrushCyan    = Brush.Parse("#80D0FF");
    static readonly IBrush BrushRed     = Brush.Parse("#FF8888");
    static readonly IBrush BrushDim     = Brush.Parse("#6A7280");

    public MainWindow()
    {
        InitializeComponent();
        Log.Inlines = new InlineCollection();
        StartBtn.Click += OnStartClick;
        AboutBtn.Click += OnAboutClick;
        CipherBox.SelectionChanged += (_, _) => PrepareReveal();
        PrintHeader();
        PrepareReveal();
        SetStatus("Ready", BrushGreen);
    }

    void PrintHeader()
    {
        AppendColored("— Select scope and press ",  BrushMuted);
        AppendColored("Start", BrushAmber);
        AppendColored(". GPU runs first, CPU backends follow.\n", BrushMuted);
        AppendColored("— Click ", BrushMuted);
        AppendColored("About ⓘ", BrushCyan);
        AppendColored(" for the full project + WWII crypto primer.\n\n", BrushMuted);
    }

    /// <summary>
    /// Pre-populate the reveal panel so the user sees the ciphertext before
    /// pressing Start. Uses whichever cipher is currently selected.
    /// </summary>
    void PrepareReveal()
    {
        switch (CipherBox.SelectedIndex)
        {
            case 2:   // Lorenz
            {
                var plaintext = DefaultScenario.LorenzPlaintextBytes;
                var pins = DefaultScenario.LorenzChiPins();
                var machine = LorenzSZ40.Create(pins, DefaultScenario.LorenzChiStart);
                var cipher = machine.TransformFresh(plaintext, DefaultScenario.LorenzChiStart);
                // Lorenz cipher bytes are Baudot 0-31 — decode to A-Z + '·'
                Reveal.SetCipherString(Baudot.Decode(cipher));
                break;
            }
            case 1:   // M4
            {
                var plaintext = DefaultScenario.M4PlaintextBytes;
                var trueKey   = DefaultScenario.M4TrueKey();
                var ct = trueKey.TransformFresh(plaintext, trueKey.PL, trueKey.PM, trueKey.PR);
                Reveal.SetCipher(ct);
                break;
            }
            default:  // M3
            {
                var plaintext = DefaultScenario.PlaintextBytes;
                var trueKey   = DefaultScenario.TrueKey();
                var ct = trueKey.TransformFresh(plaintext, trueKey.PL, trueKey.PM, trueKey.PR);
                Reveal.SetCipher(ct);
                break;
            }
        }
    }

    async void OnStartClick(object? sender, RoutedEventArgs e)
    {
        StartBtn.IsEnabled = false;
        StartBtn.Content = "Running…";
        HistCard.IsVisible = false;
        _totalSw.Restart();

        CrackScope scope = ScopeBox.SelectedIndex switch
        {
            1 => CrackScope.Normal,
            2 => CrackScope.Hard,
            3 => CrackScope.Extreme,
            _ => CrackScope.Quick,
        };

        try
        {
            await RunBenchmark(scope);
            _totalSw.Stop();
            SetStatus($"✔ Done in {_totalSw.Elapsed.TotalSeconds:F1}s", BrushGreen);
        }
        catch (Exception ex)
        {
            _totalSw.Stop();
            AppendLine();
            AppendColored($"[!] ERROR: {ex.Message}\n", BrushRed);
            AppendColored((ex.StackTrace ?? "") + "\n", BrushDim);
            SetStatus("✘ Error", BrushRed);
        }
        finally
        {
            StartBtn.IsEnabled = true;
            StartBtn.Content = "Start Benchmark";
        }
    }

    void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "docs", "readme.html");
            if (!File.Exists(path))
            {
                AppendColored($"[!] readme.html not found at {path}\n", BrushRed);
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendColored($"[!] Could not open readme: {ex.Message}\n", BrushRed);
        }
    }

    async Task RunBenchmark(CrackScope scope)
    {
        switch (CipherBox.SelectedIndex)
        {
            case 2:  await RunBenchmarkLorenz(scope); break;
            case 1:  await RunBenchmarkM4(scope);     break;
            default: await RunBenchmarkM3(scope);     break;
        }
    }

    async Task RunBenchmarkLorenz(CrackScope scope)
    {
        var plaintext = DefaultScenario.LorenzPlaintextBytes;
        var pins      = DefaultScenario.LorenzChiPins();
        var trueStart = DefaultScenario.LorenzChiStart;

        var encMachine = LorenzSZ40.Create(pins, trueStart);
        var ciphertext = encMachine.TransformFresh(plaintext, trueStart);

        Reveal.SetCipherString(Baudot.Decode(ciphertext));

        long totalKeys = 1L * LorenzSZ40.ChiPinCounts[0]
                           * LorenzSZ40.ChiPinCounts[1]
                           * LorenzSZ40.ChiPinCounts[2]
                           * LorenzSZ40.ChiPinCounts[3]
                           * LorenzSZ40.ChiPinCounts[4];

        AppendColored($"──── RUN  Lorenz SZ40 (Tunny, chi-only)  "
                    + $"({totalKeys:N0} keys) ────\n", BrushAmber);
        AppendColored("True χ start : ", BrushMuted);
        AppendColored($"[{trueStart[0]}, {trueStart[1]}, {trueStart[2]}, "
                    + $"{trueStart[3]}, {trueStart[4]}]  "
                    + $"(pin counts 41/31/29/26/23)\n\n", BrushCyan);

        var results = new List<(string name, CrackResultLorenz r)>();

        // GPU first
        SetStatus("Running GPU Lorenz (warmup)…", BrushAmber);
        AppendColored("  [", BrushMuted);
        AppendColored("SkSL GPU Lorenz", BrushCyan);
        AppendColored("] warmup… ", BrushMuted);
        await Bench.RunGpuLorenzAsync(ciphertext, pins, scope);
        AppendColored("done\n", BrushGreen);

        SetStatus("Running GPU Lorenz…", BrushAmber);
        AppendColored("  [", BrushMuted);
        AppendColored("SkSL GPU Lorenz", BrushCyan);
        AppendColored("] measured… ", BrushMuted);
        var gpu = await Bench.RunGpuLorenzAsync(ciphertext, pins, scope);
        AppendLorenzResult(gpu);
        results.Add(("SkSL GPU Lorenz (Avalonia)", gpu));
        AppendLine();

        // CPU backends — generous timeout on scalar since 22M × 700 chars is
        // a lot for one thread.
        ICrackerLorenz[] cpu =
        {
            new SimdCrackerLorenz(),
            new ParallelScalarCrackerLorenz(),
            new ScalarCrackerLorenz(),
        };
        double[] timeouts = { 0, 90, 90 };   // SIMD unlimited; others 90s
        for (int i = 0; i < cpu.Length; i++)
        {
            var c = cpu[i];
            double to = timeouts[i];

            // Warmup at a capped scope — actually Lorenz scope isn't variable,
            // so just do one fast run with a tight timeout to let JIT warm.
            SetStatus($"Running {c.Name} (warmup)…", BrushAmber);
            AppendColored("  [", BrushMuted);
            AppendColored(c.Name, BrushCyan);
            AppendColored("] warmup… ", BrushMuted);
            await Task.Run(() => c.Crack(ciphertext, pins, scope, 3));
            AppendColored("done\n", BrushGreen);

            SetStatus($"Running {c.Name}…", BrushAmber);
            AppendColored("  [", BrushMuted);
            AppendColored(c.Name, BrushCyan);
            AppendColored($"] measured (timeout {to}s)… ", BrushMuted);
            var r = await Task.Run(() => c.Crack(ciphertext, pins, scope, to));
            AppendLorenzResult(r);
            results.Add((c.Name, r));
            AppendLine();
        }

        // Summary
        SetStatus("Building summary…", BrushAmber);
        AppendLine();
        AppendColored("═══════════════ SUMMARY ═══════════════\n", BrushAmber);
        double baseline = results[^1].r.ElapsedSeconds;
        AppendColored($"{"Backend",-40}  {"Time",10}  {"K keys/s",10}  {"Speedup",8}\n", BrushDim);
        AppendColored(new string('─', 74) + "\n", BrushDim);
        foreach (var (name, r) in results)
        {
            double speedup = baseline / r.ElapsedSeconds;
            double kps = r.KeysTried / r.ElapsedSeconds / 1000;
            AppendColored($"{name,-40}  ", BrushText);
            AppendColored($"{r.ElapsedSeconds,9:F3}s", BrushAmber);
            AppendColored($"  {kps,10:F1}", BrushGreen);
            AppendColored($"  {speedup,7:F2}x", BrushCyan);
            AppendColored(r.TimedOut ? "  (TIMED OUT)\n" : "\n",
                          r.TimedOut ? BrushRed : BrushText);
        }
        AppendLine();

        // Per-backend verification
        AppendColored("Per-backend key recovery:\n", BrushMuted);
        foreach (var (name, r) in results)
        {
            bool m = r.ChiStart.Length == 5
                  && r.ChiStart[0] == trueStart[0]
                  && r.ChiStart[1] == trueStart[1]
                  && r.ChiStart[2] == trueStart[2]
                  && r.ChiStart[3] == trueStart[3]
                  && r.ChiStart[4] == trueStart[4];
            AppendColored($"  {name,-40} ", BrushText);
            AppendColored($"χ=[{r.ChiStart[0]},{r.ChiStart[1]},{r.ChiStart[2]},"
                        + $"{r.ChiStart[3]},{r.ChiStart[4]}]"
                        + $"  IC={r.BestIc/100000.0:F5}  ", BrushCyan);
            AppendColored(m ? "✔\n" : "✘\n", m ? BrushGreen : BrushRed);
        }
        AppendLine();

        // Historical card: Colossus ~1 hour per message in 1944
        ShowHistoricalCard(results[0].r.ElapsedSeconds,
                           "Colossus Mark II (1944)", "~1 hour", 3600.0);

        // Reveal the plaintext (decode Baudot → string)
        SetStatus("Decrypting…", BrushAmber);
        var revealed = Baudot.Decode(plaintext);
        await Reveal.RevealStringAsync(revealed,
            "\u201CTo OKW Keitel from Wolfsschanze: Operation Watch on the Rhine "
          + "begins 16 December 1944 — Ardennes offensive, Army Group B…\u201D");
    }

    void AppendLorenzResult(CrackResultLorenz r)
    {
        double kps = r.KeysTried / r.ElapsedSeconds / 1000;
        AppendColored($"{r.ElapsedSeconds,7:F3}s", BrushAmber);
        AppendColored($" ({kps,7:F0} K/s)", BrushGreen);
        AppendColored("  found=", BrushMuted);
        AppendColored($"{r.Found}", r.Found ? BrushGreen : BrushRed);
        AppendColored($"  bestIC={r.BestIc/100000.0:F5}", BrushCyan);
        AppendColored(r.TimedOut ? "  [TIMEOUT]\n" : "\n",
                      r.TimedOut ? BrushRed : BrushText);
    }

    async Task RunBenchmarkM3(CrackScope scope)
    {
        var plaintext = DefaultScenario.PlaintextBytes;
        var trueKey   = DefaultScenario.TrueKey();
        int origPL = trueKey.PL, origPM = trueKey.PM, origPR = trueKey.PR;
        var ciphertext = trueKey.TransformFresh(plaintext, origPL, origPM, origPR);

        Reveal.SetCipher(ciphertext);

        var fixedParts = new EnigmaM3
        {
            RL = trueKey.RL, RM = trueKey.RM, RR = trueKey.RR,
            Plugboard = trueKey.Plugboard,
            Reflector = trueKey.Reflector,
        };

        AppendColored($"──── RUN  Enigma M3  scope={scope}  ({scope.TotalKeys():N0} keys) ────\n",
                      BrushAmber);
        AppendColored("True key : ", BrushMuted);
        AppendColored($"{RotorData.RotorNames[trueKey.WL]} "
                    + $"{RotorData.RotorNames[trueKey.WM]} "
                    + $"{RotorData.RotorNames[trueKey.WR]}", BrushCyan);
        AppendColored("  /  ", BrushMuted);
        AppendColored($"{(char)('A'+origPL)} {(char)('A'+origPM)} {(char)('A'+origPR)}\n\n",
                      BrushCyan);

        var results = new List<(string name, CrackResult r)>();

        // GPU first
        await RunOneM3("SkSL GPU", scope,
            () => Bench.RunGpuAsync(ciphertext, fixedParts, CrackScope.Quick),
            () => Bench.RunGpuAsync(ciphertext, fixedParts, scope),
            "SkSL GPU (Avalonia)", results);

        ICracker[] cpu = { new SimdCracker(), new ParallelScalarCracker(), new ScalarCracker() };
        foreach (var c in cpu)
        {
            await RunOneM3(c.Name, scope,
                () => Task.Run(() => c.Crack(ciphertext, fixedParts, CrackScope.Quick)),
                () => Task.Run(() => c.Crack(ciphertext, fixedParts, scope)),
                c.Name, results);
        }

        WriteSummary(results);

        var best = results[0].r;
        AppendColored("GPU recovered key : ", BrushMuted);
        AppendColored($"{RotorData.RotorNames[best.L]} {RotorData.RotorNames[best.M]} "
                    + $"{RotorData.RotorNames[best.R]}", BrushCyan);
        AppendColored("  /  ", BrushMuted);
        AppendColored($"{(char)('A'+best.PL)} {(char)('A'+best.PM)} {(char)('A'+best.PR)}", BrushCyan);
        AppendColored($"  /  IC = {best.BestIc/100000.0:F5}\n", BrushAmber);

        bool matches = best.L == trueKey.WL && best.M == trueKey.WM && best.R == trueKey.WR
                    && best.PL == origPL && best.PM == origPM && best.PR == origPR;
        AppendColored("Matches truth     : ", BrushMuted);
        AppendColored(matches ? "YES ✔\n\n" : "NO ✘\n\n",
                      matches ? BrushGreen : BrushRed);

        ShowHistoricalCard(best.ElapsedSeconds, "Bletchley Bombe (1942)", "~15 min", 900.0);

        SetStatus("Decrypting…", BrushAmber);
        await Reveal.RevealAsync(plaintext,
            "\u201CTo all U-boats, Group Nordwind, course 029 degrees …\u201D");
    }

    async Task RunBenchmarkM4(CrackScope scope)
    {
        var plaintext = DefaultScenario.M4PlaintextBytes;
        var trueKey   = DefaultScenario.M4TrueKey();
        int origPL = trueKey.PL, origPM = trueKey.PM, origPR = trueKey.PR;
        int origPG = trueKey.PG;
        var ciphertext = trueKey.TransformFresh(plaintext, origPL, origPM, origPR);

        Reveal.SetCipher(ciphertext);

        var fixedParts = new EnigmaM4
        {
            RL = trueKey.RL, RM = trueKey.RM, RR = trueKey.RR, RG = trueKey.RG,
            PG = trueKey.PG,            // greek pos assumed known at Quick; searched at Normal+
            WG = trueKey.WG,            // greek wheel choice given
            Plugboard = trueKey.Plugboard,
            ThinReflector = trueKey.ThinReflector,
        };

        // M4 keyspace multiplier: Quick = M3 Quick (greek pos known),
        // Normal+ adds 26× for greek position search.
        long m4Keys = scope.TotalKeys();
        if (scope >= CrackScope.Normal) m4Keys *= 26;

        AppendColored($"──── RUN  Enigma M4 (U-Boot Shark)  scope={scope}  "
                    + $"({m4Keys:N0} keys) ────\n", BrushAmber);
        AppendColored("True key : ", BrushMuted);
        AppendColored($"{RotorData.RotorNames[trueKey.WL]} "
                    + $"{RotorData.RotorNames[trueKey.WM]} "
                    + $"{RotorData.RotorNames[trueKey.WR]}", BrushCyan);
        AppendColored("  /  ", BrushMuted);
        AppendColored($"{(char)('A'+origPL)} {(char)('A'+origPM)} {(char)('A'+origPR)}", BrushCyan);
        AppendColored("  /  greek ", BrushMuted);
        AppendColored($"{RotorData.GreekNames[trueKey.WG]} "
                    + $"pos={(char)('A'+origPG)}", BrushCyan);
        AppendColored("\n\n", BrushMuted);

        var results = new List<(string name, CrackResult r)>();

        await RunOneM4("SkSL GPU M4", scope,
            () => Bench.RunGpuM4Async(ciphertext, fixedParts, CrackScope.Quick),
            () => Bench.RunGpuM4Async(ciphertext, fixedParts, scope),
            "SkSL GPU M4 (Avalonia)", results);

        ICrackerM4[] cpu = { new SimdCrackerM4(), new ParallelScalarCrackerM4(), new ScalarCrackerM4() };
        foreach (var c in cpu)
        {
            await RunOneM4(c.Name, scope,
                () => Task.Run(() => c.Crack(ciphertext, fixedParts, CrackScope.Quick)),
                () => Task.Run(() => c.Crack(ciphertext, fixedParts, scope)),
                c.Name, results);
        }

        WriteSummary(results);

        // Per-backend recovered-key + matches-truth — critical for M4 because
        // at shorter CAP the GPU can hit statistical false positives from
        // IC noise while CPU (full ciphertext) lands on truth.
        AppendColored("Per-backend key recovery:\n", BrushMuted);
        foreach (var (name, r) in results)
        {
            bool m = r.L == trueKey.WL && r.M == trueKey.WM && r.R == trueKey.WR
                  && r.PL == origPL && r.PM == origPM && r.PR == origPR
                  && r.PG == origPG;
            AppendColored($"  {name,-40} ", BrushText);
            AppendColored($"{RotorData.RotorNames[r.L]} {RotorData.RotorNames[r.M]} {RotorData.RotorNames[r.R]}"
                        + $" / {(char)('A'+r.PL)} {(char)('A'+r.PM)} {(char)('A'+r.PR)}"
                        + $" / PG={(char)('A'+r.PG)}"
                        + $" / IC={r.BestIc/100000.0:F5}  ", BrushCyan);
            AppendColored(m ? "✔\n" : "✘\n", m ? BrushGreen : BrushRed);
        }
        AppendLine();

        // M4 historical comparison: Bletchley was BLIND on M4 for ~10 months
        // (Feb-Dec 1942). Once solved via Wetterkurzschlüssel, Bombe took
        // comparable time to M3 but the "cost" should reflect the capture-a-
        // codebook story. Use 10 months (~7.3 million sec) as the wall-clock
        // "break M4" wait.
        ShowHistoricalCard(results[0].r.ElapsedSeconds, "Bletchley blind (Feb-Dec 1942)",
                           "~10 months", 10 * 30.0 * 86400.0);

        SetStatus("Decrypting…", BrushAmber);
        await Reveal.RevealAsync(plaintext,
            "\u201CU-boat group Löwenherz. Convoy HX-320 at 58°N 22°W, speed 9 knots. "
          + "Attack at nightfall 20:20 …\u201D");
    }

    async Task RunOneM3(string displayName, CrackScope scope,
                        Func<Task<CrackResult>> warmup, Func<Task<CrackResult>> measured,
                        string resultName, List<(string, CrackResult)> results)
    {
        SetStatus($"Running {displayName} (warmup)…", BrushAmber);
        AppendColored("  [", BrushMuted);
        AppendColored(displayName, BrushCyan);
        AppendColored("] warmup (Quick)… ", BrushMuted);
        await warmup();
        AppendColored("done\n", BrushGreen);

        SetStatus($"Running {displayName} ({scope})…", BrushAmber);
        AppendColored("  [", BrushMuted);
        AppendColored(displayName, BrushCyan);
        AppendColored($"] measured ({scope})… ", BrushMuted);
        var r = await measured();
        AppendResult(r);
        results.Add((resultName, r));
        AppendLine();
    }

    Task RunOneM4(string displayName, CrackScope scope,
                  Func<Task<CrackResult>> warmup, Func<Task<CrackResult>> measured,
                  string resultName, List<(string, CrackResult)> results)
        => RunOneM3(displayName, scope, warmup, measured, resultName, results);

    void WriteSummary(List<(string name, CrackResult r)> results)
    {
        SetStatus("Building summary…", BrushAmber);
        AppendLine();
        AppendColored("═══════════════ SUMMARY ═══════════════\n", BrushAmber);
        double baseline = results[^1].r.ElapsedSeconds;   // slowest = scalar
        AppendColored($"{"Backend",-40}  {"Time",10}  {"K keys/s",10}  {"Speedup",8}\n", BrushDim);
        AppendColored(new string('─', 74) + "\n", BrushDim);
        foreach (var (name, r) in results)
        {
            double speedup = baseline / r.ElapsedSeconds;
            double kps = r.KeysTried / r.ElapsedSeconds / 1000;
            AppendColored($"{name,-40}  ", BrushText);
            AppendColored($"{r.ElapsedSeconds,9:F3}s", BrushAmber);
            AppendColored($"  {kps,10:F1}", BrushGreen);
            AppendColored($"  {speedup,7:F2}x\n", BrushCyan);
        }
        AppendLine();
    }

    void ShowHistoricalCard(double gpuSeconds, string thenLabel, string thenTime, double thenSeconds)
    {
        double ratio = thenSeconds / Math.Max(gpuSeconds, 0.001);

        HistThenLabel.Text = thenLabel;
        HistThenTime.Text  = thenTime;
        HistNowLabel.Text  = "Your GPU (2025)";
        HistNowTime.Text   = gpuSeconds < 1.0
            ? $"{gpuSeconds * 1000:F0} ms"
            : $"{gpuSeconds:F2} s";
        HistRatio.Text     = ratio switch
        {
            >= 1_000_000 => $"{ratio / 1_000_000:F1}M×",
            >= 1_000     => $"{ratio / 1_000:F1}k×",
            _            => $"{ratio:F0}×",
        };

        HistCard.IsVisible = true;
    }

    // ──────────────── helpers ────────────────
    void AppendResult(CrackResult r)
    {
        double kps = r.KeysTried / r.ElapsedSeconds / 1000;
        AppendColored($"{r.ElapsedSeconds,7:F3}s", BrushAmber);
        AppendColored($" ({kps,7:F0} K/s)", BrushGreen);
        AppendColored("  found=", BrushMuted);
        AppendColored($"{r.Found}", r.Found ? BrushGreen : BrushRed);
        AppendColored($"  bestIC={r.BestIc/100000.0:F5}\n", BrushCyan);
    }

    void SetStatus(string text, IBrush colour)
    {
        StatusLabel.Text = text;
        StatusLabel.Foreground = colour;
    }

    void AppendColored(string s, IBrush brush)
    {
        Log.Inlines!.Add(new Run(s) { Foreground = brush });
        Dispatcher.UIThread.Post(() => LogScroll.ScrollToEnd(),
                                 DispatcherPriority.Background);
    }

    void AppendLine() => AppendColored("\n", BrushText);
}
