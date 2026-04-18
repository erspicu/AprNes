namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using EnigmaBenchmark.Core;

/// <summary>
/// Single-threaded pure-C# brute force. Baseline for benchmark.
/// Scope adds extra ring-setting dimensions to enlarge the search space.
/// </summary>
public sealed class ScalarCracker : ICracker
{
    public string Name => "Scalar (single-thread)";

    public CrackResult Crack(byte[] ciphertext, EnigmaM3 fixedParts, CrackScope scope)
    {
        var sw = Stopwatch.StartNew();
        long keysTried = 0;
        int bestIc = 0;
        int bestL = 0, bestM = 0, bestR = 0, bestPL = 0, bestPM = 0, bestPR = 0;
        int bestRR = fixedParts.RR, bestRM = fixedParts.RM, bestRL = fixedParts.RL;

        var plaintext = new byte[ciphertext.Length];
        var machine = fixedParts;

        int rrMin = 0, rrMax = 26;
        int rmMin = fixedParts.RM, rmMax = fixedParts.RM + 1;
        int rlMin = fixedParts.RL, rlMax = fixedParts.RL + 1;

        // Based on scope, search over more ring dimensions. Outer ring dims
        // vary slower so cache locality stays decent.
        if (scope < CrackScope.Normal)  { rrMin = fixedParts.RR; rrMax = fixedParts.RR + 1; }
        if (scope >= CrackScope.Hard)   { rmMin = 0; rmMax = 26; }
        if (scope >= CrackScope.Extreme){ rlMin = 0; rlMax = 26; }

        for (int rl = rlMin; rl < rlMax; rl++)
        for (int rm = rmMin; rm < rmMax; rm++)
        for (int rr = rrMin; rr < rrMax; rr++)
        {
            machine.RL = rl; machine.RM = rm; machine.RR = rr;
            foreach (var (L, M, R) in RotorData.AllWheelOrders())
            {
                machine.WL = L; machine.WM = M; machine.WR = R;
                for (int pl = 0; pl < 26; pl++)
                for (int pm = 0; pm < 26; pm++)
                for (int pr = 0; pr < 26; pr++)
                {
                    machine.PL = pl; machine.PM = pm; machine.PR = pr;

                    ciphertext.CopyTo(plaintext.AsSpan());
                    machine.Transform(plaintext);

                    int ic = IcScorer.ScoreInt(plaintext);
                    keysTried++;

                    if (ic > bestIc)
                    {
                        bestIc = ic;
                        bestL = L; bestM = M; bestR = R;
                        bestPL = pl; bestPM = pm; bestPR = pr;
                        bestRR = rr; bestRM = rm; bestRL = rl;
                    }
                }
            }
        }

        sw.Stop();

        return new CrackResult
        {
            Found = bestIc >= IcScorer.GermanThresholdInt,
            KeysTried = keysTried,
            ElapsedSeconds = sw.Elapsed.TotalSeconds,
            L = bestL, M = bestM, R = bestR,
            PL = bestPL, PM = bestPM, PR = bestPR,
            RR_Ring = bestRR, RM_Ring = bestRM, RL_Ring = bestRL,
            BestIc = bestIc,
        };
    }
}
