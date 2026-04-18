using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
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
        CopyLogBtn.Click += OnCopyLogClick;
        ClearLogBtn.Click += OnClearLogClick;
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
            case 0:   // Zimmermann / codebook
            {
                var cb = ZimmermannCodebook.Create(DefaultScenario.Zimmermann0075());
                var cipher = cb.Encrypt(DefaultScenario.ZimmermannTargetPlain);
                Reveal.SetCipherString(cipher.Length > 400 ? cipher[..400] + "…" : cipher);
                break;
            }
            case 1:   // ADFGVX
            {
                var m = AdfgvxMachine.Create(DefaultScenario.AdfgvxGrid, DefaultScenario.AdfgvxKeyword);
                var cipher = m.Encrypt(DefaultScenario.AdfgvxPlaintextFormatted);
                Reveal.SetCipherString(cipher);
                break;
            }
            case 5:   // T52e
            {
                var plaintext = DefaultScenario.T52ePlaintextBytes;
                var pins      = DefaultScenario.T52ePins();
                var sm        = DefaultScenario.T52eSwitchMap;
                var start     = DefaultScenario.T52eWheelStart;
                var machine   = T52eMachine.Create(pins, sm, start, ktf: false);
                var cipher    = machine.EncryptFresh(plaintext, start);
                Reveal.SetCipherString(Baudot.Decode(cipher));
                break;
            }
            case 4:   // Lorenz
            {
                var plaintext = DefaultScenario.LorenzPlaintextBytes;
                var pins = DefaultScenario.LorenzChiPins();
                var machine = LorenzSZ40.Create(pins, DefaultScenario.LorenzChiStart);
                var cipher = machine.TransformFresh(plaintext, DefaultScenario.LorenzChiStart);
                // Lorenz cipher bytes are Baudot 0-31 — decode to A-Z + '·'
                Reveal.SetCipherString(Baudot.Decode(cipher));
                break;
            }
            case 3:   // M4
            {
                var plaintext = DefaultScenario.M4PlaintextBytes;
                var trueKey   = DefaultScenario.M4TrueKey();
                var ct = trueKey.TransformFresh(plaintext, trueKey.PL, trueKey.PM, trueKey.PR);
                Reveal.SetCipher(ct);
                break;
            }
            default:  // M3 (index 2 or unknown)
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

    async void OnCopyLogClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var sb = new StringBuilder();
            if (Log.Inlines != null)
            {
                foreach (var inline in Log.Inlines)
                    if (inline is Run r) sb.Append(r.Text);
            }

            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard == null)
            {
                AppendColored("[!] Clipboard unavailable on this platform\n", BrushRed);
                return;
            }
            await top.Clipboard.SetTextAsync(sb.ToString());
            SetStatus($"Log copied — {sb.Length:N0} chars", BrushGreen);
        }
        catch (Exception ex)
        {
            AppendColored($"[!] Copy failed: {ex.Message}\n", BrushRed);
        }
    }

    void OnClearLogClick(object? sender, RoutedEventArgs e)
    {
        Log.Inlines?.Clear();
        SetStatus("Log cleared", BrushGreen);
    }

    async Task RunBenchmark(CrackScope scope)
    {
        switch (CipherBox.SelectedIndex)
        {
            case 0:  await RunBenchmarkZimmermann(); break;
            case 1:  await RunBenchmarkAdfgvx();     break;
            case 2:  await RunBenchmarkM3(scope);    break;
            case 3:  await RunBenchmarkM4(scope);    break;
            case 4:  await RunBenchmarkLorenz(scope); break;
            case 5:  await RunBenchmarkT52e(scope);   break;
        }
    }

    bool RunGpu      => ChkGpu.IsChecked      ?? true;
    bool RunSimd     => ChkSimd.IsChecked     ?? true;
    bool RunParallel => ChkParallel.IsChecked ?? true;
    bool RunScalar   => ChkScalar.IsChecked   ?? true;

    void WarnIfNoBackends()
    {
        if (!RunGpu && !RunSimd && !RunParallel && !RunScalar)
            AppendColored("  (no backends selected — skipping)\n\n", BrushRed);
    }

    async Task RunBenchmarkZimmermann()
    {
        var codebook = ZimmermannCodebook.Create(DefaultScenario.Zimmermann0075());
        var targetCipher = codebook.Encrypt(DefaultScenario.ZimmermannTargetPlain);
        Reveal.SetCipherString(targetCipher.Length > 400 ? targetCipher[..400] + "…" : targetCipher);

        // Build (plaintext, ciphertext) crib pairs using the same codebook.
        var cribs = new List<(string, string)>();
        foreach (var plain in DefaultScenario.ZimmermannCribsPlain)
            cribs.Add((plain, codebook.Encrypt(plain)));

        AppendColored("──── RUN  Zimmermann Telegram / Code 0075 (WWI, 1917) ────\n", BrushAmber);
        AppendColored("Historical context: January 1917, Arthur Zimmermann offers Mexico\n"
                    + "  Texas / New Mexico / Arizona if they attack the US. British Room 40\n"
                    + "  intercepts the cable, but the codebook is not brute-force breakable;\n"
                    + "  Nigel de Grey & Rev. Montgomery recover code groups word-by-word\n"
                    + "  from accumulated known-plaintext intercepts. April 6 1917, US at war.\n\n", BrushMuted);
        AppendColored($"Ciphertext groups: {targetCipher.Split(' ').Length}   "
                    + $"Known-plaintext cribs: {cribs.Count}\n\n", BrushCyan);

        var cracker = new ScalarCrackerZimmermann();
        AppendColored("  [", BrushMuted);
        AppendColored(cracker.Name, BrushCyan);
        AppendColored("] measured… ", BrushMuted);

        var r = await Task.Run(() => cracker.Crack(targetCipher, cribs));

        AppendColored($"{r.ElapsedSeconds * 1000,6:F1}ms  ", BrushAmber);
        AppendColored($"codebook entries recovered: {r.CodebookEntriesRecovered}   ", BrushGreen);
        AppendColored($"decoded {r.DecodedGroups}/{r.TotalCodeGroupsInTarget} "
                    + $"({r.DecodedRatio * 100:F1}%)\n\n", BrushCyan);

        AppendColored("Recovered plaintext (? marks unrecovered code groups):\n", BrushMuted);
        AppendColored("  " + r.PartialPlaintext + "\n\n", BrushText);

        AppendColored("歷史教訓: Room 40 的優勢不是算力，是存取（accumulated intercepts）\n"
                    + "  + 情報運作（把「墨西哥偷的」假故事賣給美國以保護監聽源）。即便\n"
                    + "  演算法本身無法暴力破解，金鑰載體（codebook）與 traffic 存取\n"
                    + "  決定了一切。\n", BrushMuted);
    }

    async Task RunBenchmarkAdfgvx()
    {
        var plain = DefaultScenario.AdfgvxPlaintextFormatted;
        var grid  = DefaultScenario.AdfgvxGrid;
        var kw    = DefaultScenario.AdfgvxKeyword;
        var m = AdfgvxMachine.Create(grid, kw);
        var cipher = m.Encrypt(plain);
        Reveal.SetCipherString(cipher);

        int K = kw.Length;
        long totalKeys = 1;
        for (int i = 2; i <= K; i++) totalKeys *= i;

        AppendColored($"──── RUN  ADFGVX (WWI, 1918)  ({totalKeys:N0} keyword orders) ────\n", BrushAmber);
        AppendColored("Historical context: June 1918, Germany's Spring Offensive. Capt.\n"
                    + "  Georges Painvin of the Bureau du Chiffre cracked the refreshed\n"
                    + "  ADFGVX in a three-week sprint, lost ~15 kg from sleep deprivation,\n"
                    + "  and handed France the decrypt that pinned the next attack's axis\n"
                    + "  at Compiègne — stopping Ludendorff cold.\n\n", BrushMuted);
        AppendColored($"True keyword : {kw}  "
                    + $"(length {K} → {totalKeys:N0} permutations)\n\n", BrushCyan);

        var cracker = new ScalarCrackerAdfgvx();
        AppendColored("  [", BrushMuted);
        AppendColored(cracker.Name, BrushCyan);
        AppendColored("] measured… ", BrushMuted);

        var r = await Task.Run(() => cracker.Crack(cipher, grid, K));

        double kps = r.KeysTried / r.ElapsedSeconds / 1000;
        AppendColored($"{r.ElapsedSeconds,6:F3}s  ({kps,5:F0} Kkeys/s)  ", BrushAmber);
        AppendColored($"tried {r.KeysTried:N0}   ", BrushGreen);
        AppendColored($"IC={r.BestIc / 100000.0:F5}\n\n", BrushCyan);

        AppendColored("Decoded preview:\n", BrushMuted);
        AppendColored("  " + r.DecodedPreview + "\n\n", BrushText);
        AppendColored("歷史教訓: 單次金鑰重用就把 8! = 40,320 個可能性直接送給密碼分析。\n"
                    + "  那就是德軍紀律崩壞那段時期 Painvin 抓到的破口。純統計攻擊。\n", BrushMuted);
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

        AppendColored($"──── RUN  Lorenz SZ42 χ-recovery (Colossus stage 1)  "
                    + $"({totalKeys:N0} keys) ────\n", BrushAmber);
        AppendColored("Historical context: Bletchley's Tunny attack had three\n",
                      BrushMuted);
        AppendColored("  stages — (1) χ-wheel recovery via Δ-statistics (this),\n"
                    + "  (2) χ pattern deduction,  (3) ψ/μ recovery in the Testery.\n"
                    + "  Colossus Mark II was built to do Stage 1 at speed.\n\n", BrushMuted);
        AppendColored("True χ start : ", BrushMuted);
        AppendColored($"[{trueStart[0]}, {trueStart[1]}, {trueStart[2]}, "
                    + $"{trueStart[3]}, {trueStart[4]}]  "
                    + $"(pin counts 41/31/29/26/23)\n\n", BrushCyan);

        var results = new List<(string name, CrackResultLorenz r)>();

        WarnIfNoBackends();

        // GPU first
        if (RunGpu)
        {
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
        }

        // CPU backends
        var cpuList  = new List<(ICrackerLorenz c, double to)>();
        if (RunSimd)     cpuList.Add((new SimdCrackerLorenz(),           0));
        if (RunParallel) cpuList.Add((new ParallelScalarCrackerLorenz(), 90));
        if (RunScalar)   cpuList.Add((new ScalarCrackerLorenz(),         90));

        foreach (var (c, to) in cpuList)
        {
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

        if (results.Count == 0) return;

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

        // Colossus Mark II at Bletchley processed Stage 1 χ-recovery for a
        // single message in roughly an hour — that's the number we compare
        // against. Stage 2 (χ patterns) and Stage 3 (ψ+μ) were additional
        // hours to days of hand work by the Testery.
        ShowHistoricalCard(results[0].r.ElapsedSeconds,
                           "Colossus II χ-stage (1944)", "~1 hour", 3600.0);

        // Reveal the plaintext (decode Baudot → string). In a real 1944
        // Bletchley pipeline, recovering the plaintext required stages 2-3
        // (χ pins + ψ/μ) after χ-start recovery. Our simplified scenario
        // lets us display it immediately.
        SetStatus("Decrypting…", BrushAmber);
        var revealed = Baudot.Decode(plaintext);
        await Reveal.RevealStringAsync(revealed,
            "\u201CTo OKW Keitel from Wolfsschanze: Operation Watch on the Rhine "
          + "begins 16 December 1944 — Ardennes offensive, Army Group B.  "
          + "(Colossus stage 1 recovered χ starts; stages 2-3 follow.)\u201D");
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

    void AppendT52eResult(CrackResultT52e r)
    {
        double kps = r.KeysTried / r.ElapsedSeconds / 1000;
        AppendColored($"{r.ElapsedSeconds,7:F3}s", BrushAmber);
        AppendColored($" ({kps,7:F0} K/s)", BrushGreen);
        AppendColored("  found=", BrushMuted);
        AppendColored($"{r.Found}", r.Found ? BrushGreen : BrushRed);
        AppendColored($"  bestIC={r.BestIc/100000.0:F5}", BrushCyan);
        AppendColored(r.TimedOut ? "  [TIMEOUT]\n" : "\n",
                      r.TimedOut ? BrushRed : BrushText);
        if (!string.IsNullOrEmpty(r.Diagnostic))
        {
            AppendColored("      " + r.Diagnostic + "\n", BrushMuted);
        }
    }

    async Task RunBenchmarkT52e(CrackScope scope)
    {
        var plaintext = DefaultScenario.T52ePlaintextBytes;
        var pins      = DefaultScenario.T52ePins();
        var sm        = DefaultScenario.T52eSwitchMap;
        var trueStart = DefaultScenario.T52eWheelStart;

        var encMachine = T52eMachine.Create(pins, sm, trueStart, ktf: false);
        var ciphertext = encMachine.EncryptFresh(plaintext, trueStart);

        Reveal.SetCipherString(Baudot.Decode(ciphertext));

        // W1..W6 "known" (from prior Testery depth work); W7..W10 to be brute-forced.
        var knownStart = (int[])trueStart.Clone();
        knownStart[6] = 0; knownStart[7] = 0; knownStart[8] = 0; knownStart[9] = 0;

        long totalKeys = (long)T52eMachine.PinCounts[6]
                       * T52eMachine.PinCounts[7]
                       * T52eMachine.PinCounts[8]
                       * T52eMachine.PinCounts[9];

        AppendColored($"──── RUN  T52e Sturgeon 4-wheel reduced brute force  "
                    + $"({totalKeys:N0} keys) ────\n", BrushAmber);
        AppendColored("Historical context: T52e was the only Sturgeon variant the\n"
                    + "  Bletchley Testery never routinely broke in operation.\n"
                    + "  The attack models prior recovery of 6 wheels via depths,\n"
                    + "  then brute-forces the remaining four (W7..W10, pin counts\n"
                    + "  67 × 69 × 71 × 73 ≈ 24 M combinations).\n\n", BrushMuted);
        AppendColored("True W7..W10: ", BrushMuted);
        AppendColored($"[{trueStart[6]}, {trueStart[7]}, {trueStart[8]}, {trueStart[9]}]\n\n", BrushCyan);

        var results = new List<(string name, CrackResultT52e r)>();

        WarnIfNoBackends();

        // GPU first
        if (RunGpu)
        {
            SetStatus("Running GPU T52e (warmup)…", BrushAmber);
            AppendColored("  [", BrushMuted);
            AppendColored("SkSL GPU T52e", BrushCyan);
            AppendColored("] warmup… ", BrushMuted);
            await Bench.RunGpuT52eAsync(ciphertext, pins, sm, knownStart, scope);
            AppendColored("done\n", BrushGreen);

            SetStatus("Running GPU T52e…", BrushAmber);
            AppendColored("  [", BrushMuted);
            AppendColored("SkSL GPU T52e", BrushCyan);
            AppendColored("] measured… ", BrushMuted);
            var gpu = await Bench.RunGpuT52eAsync(ciphertext, pins, sm, knownStart, scope);
            AppendT52eResult(gpu);
            results.Add(("SkSL GPU T52e (Avalonia)", gpu));
            AppendLine();
        }

        // T52e per-key work is ~5× Lorenz's (H/SR XOR network + 32-row perm
        // lookup + 10 wheels stepping). Scalar needs ~9 min for full 24M.
        var cpuList  = new List<(ICrackerT52e c, double to)>();
        if (RunSimd)     cpuList.Add((new SimdCrackerT52e(),           0));
        if (RunParallel) cpuList.Add((new ParallelScalarCrackerT52e(), 180));
        if (RunScalar)   cpuList.Add((new ScalarCrackerT52e(),         900));

        foreach (var (c, to) in cpuList)
        {
            SetStatus($"Running {c.Name} (warmup)…", BrushAmber);
            AppendColored("  [", BrushMuted);
            AppendColored(c.Name, BrushCyan);
            AppendColored("] warmup… ", BrushMuted);
            await Task.Run(() => c.Crack(ciphertext, pins, sm, knownStart, scope, 3));
            AppendColored("done\n", BrushGreen);

            SetStatus($"Running {c.Name}…", BrushAmber);
            AppendColored("  [", BrushMuted);
            AppendColored(c.Name, BrushCyan);
            AppendColored($"] measured (timeout {to}s)… ", BrushMuted);
            var r = await Task.Run(() => c.Crack(ciphertext, pins, sm, knownStart, scope, to));
            AppendT52eResult(r);
            results.Add((c.Name, r));
            AppendLine();
        }

        if (results.Count == 0) return;

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

        // Per-backend key recovery
        AppendColored("Per-backend key recovery:\n", BrushMuted);
        foreach (var (name, r) in results)
        {
            bool m = r.WheelStart.Length == 10
                  && r.WheelStart[6] == trueStart[6]
                  && r.WheelStart[7] == trueStart[7]
                  && r.WheelStart[8] == trueStart[8]
                  && r.WheelStart[9] == trueStart[9];
            AppendColored($"  {name,-40} ", BrushText);
            AppendColored(m ? "✓ recovered\n" : "✗ miss\n", m ? BrushGreen : BrushRed);
        }
        AppendLine();

        HistCard.IsVisible = true;
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

        WarnIfNoBackends();

        // GPU first
        if (RunGpu)
        {
            await RunOneM3("SkSL GPU", scope,
                () => Bench.RunGpuAsync(ciphertext, fixedParts, CrackScope.Quick),
                () => Bench.RunGpuAsync(ciphertext, fixedParts, scope),
                "SkSL GPU (Avalonia)", results);
        }

        var cpu = new List<ICracker>();
        if (RunSimd)     cpu.Add(new SimdCracker());
        if (RunParallel) cpu.Add(new ParallelScalarCracker());
        if (RunScalar)   cpu.Add(new ScalarCracker());
        foreach (var c in cpu)
        {
            await RunOneM3(c.Name, scope,
                () => Task.Run(() => c.Crack(ciphertext, fixedParts, CrackScope.Quick)),
                () => Task.Run(() => c.Crack(ciphertext, fixedParts, scope)),
                c.Name, results);
        }

        if (results.Count == 0) return;

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

        WarnIfNoBackends();

        if (RunGpu)
        {
            await RunOneM4("SkSL GPU M4", scope,
                () => Bench.RunGpuM4Async(ciphertext, fixedParts, CrackScope.Quick),
                () => Bench.RunGpuM4Async(ciphertext, fixedParts, scope),
                "SkSL GPU M4 (Avalonia)", results);
        }

        var cpu = new List<ICrackerM4>();
        if (RunSimd)     cpu.Add(new SimdCrackerM4());
        if (RunParallel) cpu.Add(new ParallelScalarCrackerM4());
        if (RunScalar)   cpu.Add(new ScalarCrackerM4());
        foreach (var c in cpu)
        {
            await RunOneM4(c.Name, scope,
                () => Task.Run(() => c.Crack(ciphertext, fixedParts, CrackScope.Quick)),
                () => Task.Run(() => c.Crack(ciphertext, fixedParts, scope)),
                c.Name, results);
        }

        if (results.Count == 0) return;

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
