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
        // TextBlock.Inlines is nullable in Avalonia 11 — initialize once here
        // so AppendColored never has to null-check.
        Log.Inlines = new InlineCollection();
        StartBtn.Click += OnStartClick;
        AboutBtn.Click += OnAboutClick;
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
    /// pressing Start. Builds dramatic tension — you're looking at something
    /// that was unbreakable in 1942, about to crack it in a blink.
    /// </summary>
    void PrepareReveal()
    {
        var plaintext = DefaultScenario.PlaintextBytes;
        var trueKey   = DefaultScenario.TrueKey();
        var ciphertext = trueKey.TransformFresh(
            plaintext, trueKey.PL, trueKey.PM, trueKey.PR);
        Reveal.SetCipher(ciphertext);
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
        var plaintext = DefaultScenario.PlaintextBytes;
        var trueKey   = DefaultScenario.TrueKey();
        int origPL = trueKey.PL, origPM = trueKey.PM, origPR = trueKey.PR;
        var ciphertext = trueKey.TransformFresh(plaintext, origPL, origPM, origPR);

        // Refresh ciphertext preview in case it changed (future: per-cipher scenario)
        Reveal.SetCipher(ciphertext);

        var fixedParts = new EnigmaM3
        {
            RL = trueKey.RL, RM = trueKey.RM, RR = trueKey.RR,
            Plugboard = trueKey.Plugboard,
            Reflector = trueKey.Reflector,
        };

        AppendColored($"──── RUN  scope={scope}  ({scope.TotalKeys():N0} keys) ────\n", BrushAmber);
        AppendColored("True key : ", BrushMuted);
        AppendColored($"{RotorData.RotorNames[trueKey.WL]} "
                    + $"{RotorData.RotorNames[trueKey.WM]} "
                    + $"{RotorData.RotorNames[trueKey.WR]}", BrushCyan);
        AppendColored("  /  ", BrushMuted);
        AppendColored($"{(char)('A'+origPL)} {(char)('A'+origPM)} {(char)('A'+origPR)}\n\n",
                      BrushCyan);

        var results = new List<(string name, CrackResult r)>();

        // ── GPU first (fastest visible win) ──
        SetStatus("Running GPU (warmup)…", BrushAmber);
        AppendColored("  [", BrushMuted);
        AppendColored("SkSL GPU", BrushCyan);
        AppendColored("] warmup (Quick)… ", BrushMuted);
        await Bench.RunGpuAsync(ciphertext, fixedParts, CrackScope.Quick);
        AppendColored("done\n", BrushGreen);

        SetStatus($"Running GPU ({scope})…", BrushAmber);
        AppendColored("  [", BrushMuted);
        AppendColored("SkSL GPU", BrushCyan);
        AppendColored($"] measured ({scope})… ", BrushMuted);
        var gpu = await Bench.RunGpuAsync(ciphertext, fixedParts, scope);
        AppendResult(gpu);
        results.Add(("SkSL GPU (Avalonia)", gpu));
        AppendLine();

        // ── CPU backends in descending speed: SIMD → Parallel → Scalar ──
        ICracker[] cpu =
        {
            new SimdCracker(),
            new ParallelScalarCracker(),
            new ScalarCracker(),
        };
        foreach (var c in cpu)
        {
            SetStatus($"Running {c.Name} (warmup)…", BrushAmber);
            AppendColored("  [", BrushMuted);
            AppendColored(c.Name, BrushCyan);
            AppendColored("] warmup (Quick)… ", BrushMuted);
            await Task.Run(() => c.Crack(ciphertext, fixedParts, CrackScope.Quick));
            AppendColored("done\n", BrushGreen);

            SetStatus($"Running {c.Name} ({scope})…", BrushAmber);
            AppendColored("  [", BrushMuted);
            AppendColored(c.Name, BrushCyan);
            AppendColored($"] measured ({scope})… ", BrushMuted);
            var r = await Task.Run(() => c.Crack(ciphertext, fixedParts, scope));
            AppendResult(r);
            results.Add((c.Name, r));
            AppendLine();
        }

        // ── Summary table ──
        SetStatus("Building summary…", BrushAmber);
        AppendLine();
        AppendColored("═══════════════ SUMMARY ═══════════════\n", BrushAmber);
        double baseline = results[^1].r.ElapsedSeconds;   // slowest = scalar
        AppendColored(
            $"{"Backend",-40}  {"Time",10}  {"K keys/s",10}  {"Speedup",8}\n",
            BrushDim);
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

        // ── Result verification ──
        var best = results[0].r;   // GPU was first
        AppendColored("GPU recovered key : ", BrushMuted);
        AppendColored($"{RotorData.RotorNames[best.L]} {RotorData.RotorNames[best.M]} "
                    + $"{RotorData.RotorNames[best.R]}", BrushCyan);
        AppendColored("  /  ", BrushMuted);
        AppendColored($"{(char)('A'+best.PL)} {(char)('A'+best.PM)} {(char)('A'+best.PR)}", BrushCyan);
        AppendColored($"  /  IC = {best.BestIc/100000.0:F5}\n", BrushAmber);

        bool matches = best.L == trueKey.WL && best.M == trueKey.WM && best.R == trueKey.WR
                    && best.PL == origPL && best.PM == origPM && best.PR == origPR;
        AppendColored("Matches truth     : ", BrushMuted);
        AppendColored(matches ? "YES ✔\n" : "NO ✘\n",
                      matches ? BrushGreen : BrushRed);
        AppendLine();

        // ── Populate + show historical card ──
        ShowHistoricalCard(gpu.ElapsedSeconds);

        // ── The money shot: reveal the plaintext ──
        SetStatus("Decrypting…", BrushAmber);
        await Reveal.RevealAsync(plaintext,
            "\u201CTo all U-boats, Group Nordwind, course 029 degrees …\u201D");
    }

    void ShowHistoricalCard(double gpuSeconds)
    {
        // Bletchley Bombe: ~15–20 min per Enigma key. Use 15 min as lower bound.
        const double bletchleyMinutes = 15.0;
        double bletchleySeconds = bletchleyMinutes * 60.0;
        double ratio = bletchleySeconds / Math.Max(gpuSeconds, 0.001);

        HistThenLabel.Text = "Bletchley Bombe (1942)";
        HistThenTime.Text  = "~15 min";
        HistNowLabel.Text  = "Your GPU (2025)";
        HistNowTime.Text   = gpuSeconds < 1.0
            ? $"{gpuSeconds * 1000:F0} ms"
            : $"{gpuSeconds:F2} s";
        HistRatio.Text     = ratio >= 1000
            ? $"{ratio / 1000:F1}k×"
            : $"{ratio:F0}×";

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
