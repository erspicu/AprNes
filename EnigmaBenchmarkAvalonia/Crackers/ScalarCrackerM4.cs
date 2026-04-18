namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using EnigmaBenchmark.Core;

/// <summary>
/// Single-threaded M4 brute force. Scope expansion mirrors M3 except that a
/// new PG (greek-wheel position) dimension kicks in at Normal+.
///
///   Quick   : greek pos + greek wheel known → 60 × 26³      ≈ 1.05 M keys
///   Normal  : + 26 greek positions          → 60 × 26 × 26³ ≈ 27.4 M keys
///   Hard    : + 26 right rings              →              ≈ 712 M keys
///   Extreme : + 26 middle rings             →              ≈ 18.5 G keys
///
/// Greek wheel (Beta/Gamma) and thin reflector are treated as known (part
/// of daily Kenngruppenbuch in 1942) to keep the benchmark tractable.
/// </summary>
public sealed class ScalarCrackerM4 : ICrackerM4
{
    public string Name => "Scalar M4 (single-thread)";

    public CrackResult Crack(byte[] ciphertext, EnigmaM4 fixedParts, CrackScope scope)
    {
        var sw = Stopwatch.StartNew();
        long keysTried = 0;
        int bestIc = 0;
        int bestL = 0, bestM = 0, bestR = 0;
        int bestPL = 0, bestPM = 0, bestPR = 0;
        int bestPG = fixedParts.PG;
        int bestRR = fixedParts.RR, bestRM = fixedParts.RM, bestRL = fixedParts.RL;

        var plaintext = new byte[ciphertext.Length];
        var machine = fixedParts;

        // ── Scope → search dimensions ──
        int pgMin = fixedParts.PG, pgMax = fixedParts.PG + 1;
        int rrMin = fixedParts.RR, rrMax = fixedParts.RR + 1;
        int rmMin = fixedParts.RM, rmMax = fixedParts.RM + 1;
        int rlMin = fixedParts.RL, rlMax = fixedParts.RL + 1;
        if (scope >= CrackScope.Normal)  { pgMin = 0; pgMax = 26; }
        if (scope >= CrackScope.Hard)    { rrMin = 0; rrMax = 26; }
        if (scope >= CrackScope.Extreme) { rmMin = 0; rmMax = 26; }

        for (int pg = pgMin; pg < pgMax; pg++)
        for (int rl = rlMin; rl < rlMax; rl++)
        for (int rm = rmMin; rm < rmMax; rm++)
        for (int rr = rrMin; rr < rrMax; rr++)
        {
            machine.PG = pg;
            machine.RL = rl; machine.RM = rm; machine.RR = rr;

            foreach (var (L, M, R) in RotorData.AllWheelOrders())
            {
                machine.WL = L; machine.WM = M; machine.WR = R;
                for (int pl = 0; pl < 26; pl++)
                for (int pmPos = 0; pmPos < 26; pmPos++)
                for (int prPos = 0; prPos < 26; prPos++)
                {
                    machine.PL = pl; machine.PM = pmPos; machine.PR = prPos;

                    ciphertext.CopyTo(plaintext.AsSpan());
                    machine.Transform(plaintext);

                    int ic = IcScorer.ScoreInt(plaintext);
                    keysTried++;

                    if (ic > bestIc)
                    {
                        bestIc = ic;
                        bestL = L; bestM = M; bestR = R;
                        bestPL = pl; bestPM = pmPos; bestPR = prPos;
                        bestPG = pg;
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
            WG = fixedParts.WG,   // greek wheel assumed known
            PG = bestPG,
        };
    }
}
