namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using EnigmaBenchmark.Core;

/// <summary>
/// Multi-threaded pure-C# brute force via Parallel.ForEach. Same algorithm as
/// ScalarCracker but parallelises the OUTER ring+wheel dimension so each
/// worker gets enough work to outweigh dispatch overhead.
/// </summary>
public sealed class ParallelScalarCracker : ICracker
{
    public string Name => $"Scalar (Parallel, {Environment.ProcessorCount} cores)";

    public CrackResult Crack(byte[] ciphertext, EnigmaM3 fixedParts, CrackScope scope)
    {
        var sw = Stopwatch.StartNew();
        long keysTried = 0;

        // Build outer work units: (rl, rm, rr, L, M, R)
        // Inner loop (pl, pm, pr) = 17,576 decryptions per unit.
        var units = new List<(int rl, int rm, int rr, int L, int M, int R)>();

        int rrMin = 0, rrMax = 26;
        int rmMin = fixedParts.RM, rmMax = fixedParts.RM + 1;
        int rlMin = fixedParts.RL, rlMax = fixedParts.RL + 1;
        if (scope < CrackScope.Normal)   { rrMin = fixedParts.RR; rrMax = fixedParts.RR + 1; }
        if (scope >= CrackScope.Hard)    { rmMin = 0; rmMax = 26; }
        if (scope >= CrackScope.Extreme) { rlMin = 0; rlMax = 26; }

        for (int rl = rlMin; rl < rlMax; rl++)
        for (int rm = rmMin; rm < rmMax; rm++)
        for (int rr = rrMin; rr < rrMax; rr++)
        foreach (var (L, M, R) in RotorData.AllWheelOrders())
            units.Add((rl, rm, rr, L, M, R));

        var best = new BestHolder { Ic = 0 };

        Parallel.ForEach(units,
            () => new ThreadLocalState(ciphertext.Length, fixedParts),
            (unit, _, local) =>
            {
                local.machine.RL = unit.rl; local.machine.RM = unit.rm; local.machine.RR = unit.rr;
                local.machine.WL = unit.L;  local.machine.WM = unit.M;  local.machine.WR = unit.R;

                for (int pl = 0; pl < 26; pl++)
                for (int pm = 0; pm < 26; pm++)
                for (int pr = 0; pr < 26; pr++)
                {
                    local.machine.PL = pl; local.machine.PM = pm; local.machine.PR = pr;

                    ciphertext.CopyTo(local.plaintext.AsSpan());
                    local.machine.Transform(local.plaintext);

                    int ic = IcScorer.ScoreInt(local.plaintext);
                    local.keysTried++;

                    if (ic > local.bestIc)
                    {
                        local.bestIc = ic;
                        local.bestL = unit.L; local.bestM = unit.M; local.bestR = unit.R;
                        local.bestPL = pl; local.bestPM = pm; local.bestPR = pr;
                        local.bestRR = unit.rr; local.bestRM = unit.rm; local.bestRL = unit.rl;
                    }
                }
                return local;
            },
            (local) =>
            {
                Interlocked.Add(ref keysTried, local.keysTried);
                lock (best)
                {
                    if (local.bestIc > best.Ic)
                    {
                        best.Ic = local.bestIc;
                        best.L = local.bestL; best.M = local.bestM; best.R = local.bestR;
                        best.PL = local.bestPL; best.PM = local.bestPM; best.PR = local.bestPR;
                        best.RR = local.bestRR; best.RM = local.bestRM; best.RL = local.bestRL;
                    }
                }
            });

        sw.Stop();

        return new CrackResult
        {
            Found = best.Ic >= IcScorer.GermanThresholdInt,
            KeysTried = keysTried,
            ElapsedSeconds = sw.Elapsed.TotalSeconds,
            L = best.L, M = best.M, R = best.R,
            PL = best.PL, PM = best.PM, PR = best.PR,
            RR_Ring = best.RR, RM_Ring = best.RM, RL_Ring = best.RL,
            BestIc = best.Ic,
        };
    }

    class BestHolder
    {
        public int Ic, L, M, R, PL, PM, PR, RR, RM, RL;
    }

    class ThreadLocalState
    {
        public EnigmaM3 machine;
        public byte[] plaintext;
        public long keysTried;
        public int bestIc, bestL, bestM, bestR, bestPL, bestPM, bestPR;
        public int bestRR, bestRM, bestRL;

        public ThreadLocalState(int len, EnigmaM3 fixedParts)
        {
            machine = fixedParts;
            plaintext = new byte[len];
            bestRR = fixedParts.RR; bestRM = fixedParts.RM; bestRL = fixedParts.RL;
        }
    }
}
