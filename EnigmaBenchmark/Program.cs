namespace EnigmaBenchmark;

using EnigmaBenchmark.Core;
using EnigmaBenchmark.Crackers;
using EnigmaBenchmark.Presets;

public static class Program
{
    public static int Main(string[] args)
    {
        // ── Parse --scope ──
        CrackScope scope = CrackScope.Normal;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--scope")
            {
                scope = args[i + 1].ToLowerInvariant() switch
                {
                    "quick"   => CrackScope.Quick,
                    "normal"  => CrackScope.Normal,
                    "hard"    => CrackScope.Hard,
                    "extreme" => CrackScope.Extreme,
                    _ => CrackScope.Normal,
                };
            }
        }

        Console.WriteLine("================================================");
        Console.WriteLine("  EnigmaBenchmark — Phase A (CLI, timing only)");
        Console.WriteLine("================================================");
        Console.WriteLine();
        Console.WriteLine($"Scope                : {scope}  ({scope.Describe()})");
        Console.WriteLine($"Total keys to search : {scope.TotalKeys():N0}");
        Console.WriteLine();

        // ── Prepare scenario ──
        var plaintext = DefaultScenario.PlaintextBytes;
        Console.WriteLine($"Plaintext length     : {plaintext.Length} chars");
        Console.WriteLine($"Plaintext preview    : {EnigmaM3.DecodeBytes(plaintext)[..Math.Min(60, plaintext.Length)]}...");

        var trueKey = DefaultScenario.TrueKey();
        Console.WriteLine($"True wheel order     : {RotorData.RotorNames[trueKey.WL]} {RotorData.RotorNames[trueKey.WM]} {RotorData.RotorNames[trueKey.WR]}");
        Console.WriteLine($"True ring setting    : {(char)('A' + trueKey.RL)} {(char)('A' + trueKey.RM)} {(char)('A' + trueKey.RR)}");
        Console.WriteLine($"True grundstellung   : {(char)('A' + trueKey.PL)} {(char)('A' + trueKey.PM)} {(char)('A' + trueKey.PR)}");
        Console.WriteLine($"True plugboard       : {DefaultScenario.PlugboardPairs}");
        Console.WriteLine();

        // Save original positions — struct method calls mutate the receiver's
        // fields, so we need fresh copies for each TransformFresh invocation.
        int origPL = trueKey.PL, origPM = trueKey.PM, origPR = trueKey.PR;

        // ── Produce ciphertext ──
        var ciphertext = trueKey.TransformFresh(plaintext, origPL, origPM, origPR);
        Console.WriteLine($"Ciphertext preview   : {EnigmaM3.DecodeBytes(ciphertext)[..Math.Min(60, ciphertext.Length)]}...");
        Console.WriteLine();

        // ── Verify round-trip on true key ──
        var rt = trueKey.TransformFresh(ciphertext, origPL, origPM, origPR);
        bool roundTripOk = rt.AsSpan().SequenceEqual(plaintext.AsSpan());
        Console.WriteLine($"Round-trip verify    : {(roundTripOk ? "OK" : "FAIL")}");
        if (!roundTripOk)
        {
            Console.Error.WriteLine("[!] Enigma implementation bug — aborting");
            return 1;
        }

        int trueIc = IcScorer.ScoreInt(plaintext);
        Console.WriteLine($"True-plaintext IC    : {trueIc / 100000.0:F5} (threshold = {IcScorer.GermanThreshold:F4})");
        Console.WriteLine();

        // ── Fixed-parts helper: cracker needs to know ring setting + plugboard + reflector ──
        var fixedParts = new EnigmaM3
        {
            // Wheel order + positions will be set by cracker per attempt
            RL = trueKey.RL, RM = trueKey.RM, RR = trueKey.RR,
            Plugboard = trueKey.Plugboard,
            Reflector = trueKey.Reflector,
        };

        // ── Run crackers ──
        var crackers = new ICracker[]
        {
            new ScalarCracker(),
            new ParallelScalarCracker(),
            new SimdCracker(),
        };

        Console.WriteLine("Running crackers (2 runs each: Run 1 = JIT warmup, Run 2 = measured)");
        Console.WriteLine();

        var results = new List<(string name, CrackResult result)>();

        // JIT warmup uses the SMALLEST scope to avoid wasting minutes on an
        // extreme-scope warmup run
        var warmupScope = CrackScope.Quick;

        foreach (var c in crackers)
        {
            Console.Write($"  [{c.Name}] JIT warmup ({warmupScope}) ... ");
            _ = c.Crack(ciphertext, fixedParts, warmupScope);
            Console.WriteLine("done");

            Console.Write($"  [{c.Name}] measured ({scope}) ... ");
            var r = c.Crack(ciphertext, fixedParts, scope);
            Console.WriteLine($"{r.ElapsedSeconds:F3}s  ({r.KeysTried / r.ElapsedSeconds / 1000:F0} K keys/s)  found={r.Found}  bestIC={r.BestIc / 100000.0:F5}");
            results.Add((c.Name, r));
            Console.WriteLine();
        }

        // ── Summary table ──
        Console.WriteLine("================================================");
        Console.WriteLine("  SUMMARY");
        Console.WriteLine("================================================");
        Console.WriteLine();
        Console.WriteLine($"{"Cracker",-35} {"Time (s)",10}  {"K keys/s",10}  {"Speedup",8}  {"Found?",6}");
        Console.WriteLine(new string('-', 80));

        double baseline = results[0].result.ElapsedSeconds;
        foreach (var (name, r) in results)
        {
            double speedup = baseline / r.ElapsedSeconds;
            double keysPerSec = r.KeysTried / r.ElapsedSeconds / 1000;
            Console.WriteLine($"{name,-35} {r.ElapsedSeconds,10:F3}  {keysPerSec,10:F1}  {speedup,7:F2}x  {(r.Found ? "YES" : "NO"),6}");
        }

        Console.WriteLine();
        var best = results[^1].result;
        Console.WriteLine($"Best key found:");
        Console.WriteLine($"  Wheel order     : {RotorData.RotorNames[best.L]} {RotorData.RotorNames[best.M]} {RotorData.RotorNames[best.R]}");
        Console.WriteLine($"  Grundstellung   : {(char)('A' + best.PL)} {(char)('A' + best.PM)} {(char)('A' + best.PR)}");
        Console.WriteLine($"  IC              : {best.BestIc / 100000.0:F5}");

        bool matchTrue = best.L == trueKey.WL && best.M == trueKey.WM && best.R == trueKey.WR
                      && best.PL == origPL && best.PM == origPM && best.PR == origPR;
        Console.WriteLine($"  Matches truth   : {(matchTrue ? "YES" : "NO")}");

        return 0;
    }
}
