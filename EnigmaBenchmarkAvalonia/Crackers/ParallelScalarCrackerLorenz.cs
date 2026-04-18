namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using EnigmaBenchmark.Core;

/// <summary>
/// Multi-threaded Lorenz chi-start brute force via Parallel.ForEach. Outer
/// units = (s1, s2, s3, s4) tuples; each worker iterates s0 (41 values)
/// inline for cache locality.
/// </summary>
public sealed class ParallelScalarCrackerLorenz : ICrackerLorenz
{
    public string Name => $"Scalar Lorenz (Parallel, {Environment.ProcessorCount} cores)";

    public CrackResultLorenz Crack(byte[] ciphertext, byte[][] chiPins, CrackScope scope, double timeoutSec = 0)
    {
        var sw = Stopwatch.StartNew();
        int n = ciphertext.Length;
        int[] counts = LorenzSZ40.ChiPinCounts;

        var units = new List<(int s1, int s2, int s3, int s4)>(
            counts[1] * counts[2] * counts[3] * counts[4]);
        for (int s4 = 0; s4 < counts[4]; s4++)
        for (int s3 = 0; s3 < counts[3]; s3++)
        for (int s2 = 0; s2 < counts[2]; s2++)
        for (int s1 = 0; s1 < counts[1]; s1++)
            units.Add((s1, s2, s3, s4));

        long keysTried = 0;
        var best = new BestHolder();
        bool timedOut = false;
        long timeoutTicks = timeoutSec > 0
            ? (long)(timeoutSec * Stopwatch.Frequency)
            : long.MaxValue;

        byte[] pin0 = chiPins[0], pin1 = chiPins[1], pin2 = chiPins[2],
               pin3 = chiPins[3], pin4 = chiPins[4];
        int c0 = counts[0], c1 = counts[1], c2 = counts[2], c3 = counts[3], c4 = counts[4];

        Parallel.ForEach(units,
            () => new TLS(n),
            (unit, state, local) =>
            {
                if (sw.ElapsedTicks > timeoutTicks) { state.Stop(); return local; }

                int s1 = unit.s1, s2 = unit.s2, s3 = unit.s3, s4 = unit.s4;
                for (int s0 = 0; s0 < c0; s0++)
                {
                    int p0 = s0, p1 = s1, p2 = s2, p3 = s3, p4 = s4;
                    for (int i = 0; i < n; i++)
                    {
                        int chi = pin0[p0]
                                | (pin1[p1] << 1)
                                | (pin2[p2] << 2)
                                | (pin3[p3] << 3)
                                | (pin4[p4] << 4);
                        local.plaintext[i] = (byte)((ciphertext[i] ^ chi) & 0x1F);

                        if (++p0 >= c0) p0 = 0;
                        if (++p1 >= c1) p1 = 0;
                        if (++p2 >= c2) p2 = 0;
                        if (++p3 >= c3) p3 = 0;
                        if (++p4 >= c4) p4 = 0;
                    }

                    int ic = IcScorer.ScoreBaudotInt(local.plaintext);
                    local.keysTried++;

                    if (ic > local.bestIc)
                    {
                        local.bestIc = ic;
                        local.bestStart[0] = s0; local.bestStart[1] = s1;
                        local.bestStart[2] = s2; local.bestStart[3] = s3;
                        local.bestStart[4] = s4;
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
                        Array.Copy(local.bestStart, best.Start, 5);
                    }
                }
            });

        if (sw.ElapsedTicks > timeoutTicks) timedOut = true;

        sw.Stop();
        return new CrackResultLorenz
        {
            Found = best.Ic >= IcScorer.BaudotGermanThresholdInt,
            TimedOut = timedOut,
            KeysTried = keysTried,
            ElapsedSeconds = sw.Elapsed.TotalSeconds,
            ChiStart = best.Start,
            BestIc = best.Ic,
        };
    }

    class BestHolder
    {
        public int Ic;
        public int[] Start = new int[5];
    }

    class TLS
    {
        public byte[] plaintext;
        public long keysTried;
        public int bestIc;
        public int[] bestStart = new int[5];
        public TLS(int n) { plaintext = new byte[n]; }
    }
}
