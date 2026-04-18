namespace EnigmaBenchmark.Crackers;

using System.Collections.Concurrent;
using System.Diagnostics;
using EnigmaBenchmark.Core;

/// <summary>
/// Multi-threaded pure-C# brute force via Parallel.ForEach. Same algorithm as
/// ScalarCracker but distributes wheel orders across CPU cores.
/// </summary>
public sealed class ParallelScalarCracker : ICracker
{
    public string Name => $"Scalar (Parallel, {Environment.ProcessorCount} cores)";

    public CrackResult Crack(byte[] ciphertext, EnigmaM3 fixedParts)
    {
        var sw = Stopwatch.StartNew();
        long keysTried = 0;

        var orders = RotorData.AllWheelOrders().ToList();
        var best = new BestHolder();

        Parallel.ForEach(orders,
            () => new ThreadLocalState(ciphertext.Length, fixedParts),
            (order, loopState, local) =>
            {
                local.machine.WL = order.L;
                local.machine.WM = order.M;
                local.machine.WR = order.R;

                for (int pl = 0; pl < 26; pl++)
                for (int pm = 0; pm < 26; pm++)
                for (int pr = 0; pr < 26; pr++)
                {
                    local.machine.PL = pl;
                    local.machine.PM = pm;
                    local.machine.PR = pr;

                    ciphertext.CopyTo(local.plaintext.AsSpan());
                    local.machine.Transform(local.plaintext);

                    int ic = IcScorer.ScoreInt(local.plaintext);
                    local.keysTried++;

                    if (ic > local.bestIc)
                    {
                        local.bestIc = ic;
                        local.bestL = order.L; local.bestM = order.M; local.bestR = order.R;
                        local.bestPL = pl; local.bestPM = pm; local.bestPR = pr;
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
            BestIc = best.Ic,
        };
    }

    class BestHolder
    {
        public int Ic, L, M, R, PL, PM, PR;
    }

    class ThreadLocalState
    {
        public EnigmaM3 machine;
        public byte[] plaintext;
        public long keysTried;
        public int bestIc, bestL, bestM, bestR, bestPL, bestPM, bestPR;

        public ThreadLocalState(int len, EnigmaM3 fixedParts)
        {
            machine = fixedParts;
            plaintext = new byte[len];
        }
    }
}
