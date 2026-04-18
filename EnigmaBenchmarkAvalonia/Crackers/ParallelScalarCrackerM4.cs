namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using EnigmaBenchmark.Core;

/// <summary>
/// Multi-threaded M4 brute force. Parallelises the outer (pg × ring × wheel)
/// work into units big enough to absorb dispatch cost.
/// </summary>
public sealed class ParallelScalarCrackerM4 : ICrackerM4
{
    public string Name => $"Scalar M4 (Parallel, {Environment.ProcessorCount} cores)";

    public CrackResult Crack(byte[] ciphertext, EnigmaM4 fixedParts, CrackScope scope)
    {
        var sw = Stopwatch.StartNew();
        long keysTried = 0;

        int pgMin = fixedParts.PG, pgMax = fixedParts.PG + 1;
        int rrMin = fixedParts.RR, rrMax = fixedParts.RR + 1;
        int rmMin = fixedParts.RM, rmMax = fixedParts.RM + 1;
        int rlMin = fixedParts.RL, rlMax = fixedParts.RL + 1;
        if (scope >= CrackScope.Normal)  { pgMin = 0; pgMax = 26; }
        if (scope >= CrackScope.Hard)    { rrMin = 0; rrMax = 26; }
        if (scope >= CrackScope.Extreme) { rmMin = 0; rmMax = 26; }

        var units = new List<(int pg, int rl, int rm, int rr, int L, int M, int R)>();
        for (int pg = pgMin; pg < pgMax; pg++)
        for (int rl = rlMin; rl < rlMax; rl++)
        for (int rm = rmMin; rm < rmMax; rm++)
        for (int rr = rrMin; rr < rrMax; rr++)
        foreach (var (L, M, R) in RotorData.AllWheelOrders())
            units.Add((pg, rl, rm, rr, L, M, R));

        var best = new BestHolder();

        Parallel.ForEach(units,
            () => new TLS(ciphertext.Length, fixedParts),
            (unit, _, local) =>
            {
                local.machine.PG = unit.pg;
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
                        local.bestPG = unit.pg;
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
                        best.PG = local.bestPG;
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
            WG = fixedParts.WG,
            PG = best.PG,
        };
    }

    class BestHolder
    {
        public int Ic, L, M, R, PL, PM, PR, PG, RR, RM, RL;
    }

    class TLS
    {
        public EnigmaM4 machine;
        public byte[] plaintext;
        public long keysTried;
        public int bestIc, bestL, bestM, bestR, bestPL, bestPM, bestPR, bestPG;
        public int bestRR, bestRM, bestRL;

        public TLS(int len, EnigmaM4 fixedParts)
        {
            machine = fixedParts;
            plaintext = new byte[len];
            bestRR = fixedParts.RR; bestRM = fixedParts.RM; bestRL = fixedParts.RL;
            bestPG = fixedParts.PG;
        }
    }
}
