using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EnigmaBenchmark.Core;
using EnigmaBenchmark.Crackers;
using EnigmaBenchmark.Presets;

namespace EnigmaBenchmarkAvalonia;

public partial class MainWindow : Window
{
    readonly StringBuilder _log = new();
    readonly Stopwatch _totalSw = new();

    public MainWindow()
    {
        InitializeComponent();    // auto-generated from MainWindow.axaml
        StartBtn.Click += OnStartClick;
        PrintHeader();
        SetStatus("Ready", "#80FF80");
    }

    void PrintHeader()
    {
        AppendLine("================================================");
        AppendLine("  Enigma Benchmark — Avalonia (GPU-ready)");
        AppendLine("================================================");
        AppendLine();
        AppendLine("Select a scope and press Start. CPU crackers run on a");
        AppendLine("background thread (UI stays responsive). The GPU cracker");
        AppendLine("runs on the render thread and may freeze UI briefly.");
        AppendLine();
    }

    async void OnStartClick(object? sender, RoutedEventArgs e)
    {
        StartBtn.IsEnabled = false;
        StartBtn.Content = "Running…";
        _totalSw.Restart();

        CrackScope scope = (ScopeBox.SelectedIndex) switch
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
            SetStatus($"✔ Done in {_totalSw.Elapsed.TotalSeconds:F1}s", "#80FF80");
        }
        catch (Exception ex)
        {
            _totalSw.Stop();
            AppendLine();
            AppendLine($"[!] ERROR: {ex.Message}");
            AppendLine(ex.StackTrace ?? "");
            SetStatus("✘ Error", "#FF8080");
        }
        finally
        {
            StartBtn.IsEnabled = true;
            StartBtn.Content = "Start Benchmark";
        }
    }

    async Task RunBenchmark(CrackScope scope)
    {
        var plaintext = DefaultScenario.PlaintextBytes;
        var trueKey   = DefaultScenario.TrueKey();

        int origPL = trueKey.PL, origPM = trueKey.PM, origPR = trueKey.PR;
        var ciphertext = trueKey.TransformFresh(plaintext, origPL, origPM, origPR);

        var fixedParts = new EnigmaM3
        {
            RL = trueKey.RL, RM = trueKey.RM, RR = trueKey.RR,
            Plugboard = trueKey.Plugboard,
            Reflector = trueKey.Reflector,
        };

        AppendLine($"──── RUN  scope={scope}  ({scope.TotalKeys():N0} keys) ────");
        AppendLine($"True key : {RotorData.RotorNames[trueKey.WL]} "
                 + $"{RotorData.RotorNames[trueKey.WM]} {RotorData.RotorNames[trueKey.WR]}  /  "
                 + $"{(char)('A'+origPL)} {(char)('A'+origPM)} {(char)('A'+origPR)}");
        AppendLine();

        var results = new List<(string name, CrackResult r)>();

        // ── GPU first (fastest-feeling, lets user see it working) ──
        SetStatus("Running GPU (warmup)…", "#FFD060");
        Append("  [SkSL GPU] warmup (Quick)… ");
        await Bench.RunGpuAsync(ciphertext, fixedParts, CrackScope.Quick);
        AppendLine("done");

        SetStatus($"Running GPU ({scope})…", "#FFD060");
        Append($"  [SkSL GPU] measured ({scope})… ");
        var gpu = await Bench.RunGpuAsync(ciphertext, fixedParts, scope);
        AppendLine(FormatResult(gpu));
        results.Add(("SkSL GPU (Avalonia lease)", gpu));
        AppendLine();

        // ── CPU crackers on background thread (UI stays responsive) ──
        ICracker[] cpu = { new SimdCracker(), new ParallelScalarCracker(), new ScalarCracker() };
        foreach (var c in cpu)
        {
            SetStatus($"Running {c.Name} (warmup)…", "#FFD060");
            Append($"  [{c.Name}] warmup (Quick)… ");
            await Task.Run(() => c.Crack(ciphertext, fixedParts, CrackScope.Quick));
            AppendLine("done");

            SetStatus($"Running {c.Name} ({scope})…", "#FFD060");
            Append($"  [{c.Name}] measured ({scope})… ");
            var r = await Task.Run(() => c.Crack(ciphertext, fixedParts, scope));
            AppendLine(FormatResult(r));
            results.Add((c.Name, r));
            AppendLine();
        }

        // ── Summary ──
        SetStatus("Building summary…", "#FFD060");
        AppendLine("================================================");
        AppendLine("  SUMMARY");
        AppendLine("================================================");
        // Baseline = slowest (last Scalar) so speedups read ≥ 1.0
        double baseline = results[^1].r.ElapsedSeconds;
        AppendLine($"{"Cracker",-48} {"Time (s)",10}  {"K keys/s",10}  {"Speedup",8}");
        AppendLine(new string('-', 82));
        foreach (var (name, r) in results)
        {
            double speedup = baseline / r.ElapsedSeconds;
            double kps = r.KeysTried / r.ElapsedSeconds / 1000;
            AppendLine($"{name,-48} {r.ElapsedSeconds,10:F3}  {kps,10:F1}  {speedup,7:F2}x");
        }

        AppendLine();
        var best = results[0].r;   // GPU = first entry
        AppendLine($"GPU best key   : "
                 + $"{RotorData.RotorNames[best.L]} {RotorData.RotorNames[best.M]} {RotorData.RotorNames[best.R]}"
                 + $"  /  {(char)('A'+best.PL)} {(char)('A'+best.PM)} {(char)('A'+best.PR)}"
                 + $"  / IC={best.BestIc/100000.0:F5}");
        bool matches = best.L == trueKey.WL && best.M == trueKey.WM && best.R == trueKey.WR
                    && best.PL == origPL && best.PM == origPM && best.PR == origPR;
        AppendLine($"Matches truth  : {(matches ? "YES ✔" : "NO ✘")}");
        AppendLine();
    }

    static string FormatResult(CrackResult r)
        => $"{r.ElapsedSeconds,7:F3}s  ({r.KeysTried / r.ElapsedSeconds / 1000,7:F0} K/s)  "
         + $"found={r.Found}  bestIC={r.BestIc/100000.0:F5}";

    void SetStatus(string text, string colour)
    {
        StatusLabel.Text = text;
        StatusLabel.Foreground = Avalonia.Media.Brush.Parse(colour);
    }

    void Append(string s)
    {
        _log.Append(s);
        Log.Text = _log.ToString();
        Dispatcher.UIThread.Post(() => LogScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    void AppendLine(string s = "") => Append(s + Environment.NewLine);
}
