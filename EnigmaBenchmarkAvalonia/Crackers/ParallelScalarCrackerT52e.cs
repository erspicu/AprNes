namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using EnigmaBenchmark.Core;

/// <summary>
/// Multi-threaded T52e brute force via Parallel.ForEach. Each work unit is
/// a (s7, s8, s9) tuple — the outer 3 wheels; every worker iterates the
/// innermost s6 loop locally (67 positions) for cache locality.
/// </summary>
public sealed class ParallelScalarCrackerT52e : ICrackerT52e
{
    public string Name => $"Scalar T52e (Parallel, {Environment.ProcessorCount} cores)";

    public CrackResultT52e Crack(
        byte[] ciphertext,
        byte[][] pins,
        int[] switchMap,
        int[] knownStart,
        CrackScope scope,
        double timeoutSec = 0)
    {
        var sw = Stopwatch.StartNew();
        int n = ciphertext.Length;

        int c6 = T52eMachine.PinCounts[6];
        int c7 = T52eMachine.PinCounts[7];
        int c8 = T52eMachine.PinCounts[8];
        int c9 = T52eMachine.PinCounts[9];

        var units = new List<(int s7, int s8, int s9)>(c7 * c8 * c9);
        for (int s9 = 0; s9 < c9; s9++)
        for (int s8 = 0; s8 < c8; s8++)
        for (int s7 = 0; s7 < c7; s7++)
            units.Add((s7, s8, s9));

        long keysTried = 0;
        var best = new BestHolder();
        bool timedOut = false;
        long timeoutTicks = timeoutSec > 0
            ? (long)(timeoutSec * Stopwatch.Frequency)
            : long.MaxValue;

        // Capture pins/switchMap/knownStart by closure so each worker thread can
        // build its own T52eMachine without sharing state.
        var pinsLocal = pins;
        var switchMapLocal = switchMap;
        var knownStartLocal = knownStart;

        Parallel.ForEach(units,
            () => new TLS(n, pinsLocal, switchMapLocal, knownStartLocal),
            (unit, state, local) =>
            {
                if (sw.ElapsedTicks > timeoutTicks) { state.Stop(); return local; }

                local.start[7] = unit.s7;
                local.start[8] = unit.s8;
                local.start[9] = unit.s9;

                for (int s6 = 0; s6 < c6; s6++)
                {
                    local.start[6] = s6;
                    local.machine.SetStart(local.start);
                    for (int i = 0; i < n; i++)
                        local.candidate[i] = local.machine.Decrypt(ciphertext[i]);

                    int ic = IcScorer.ScoreBaudotInt(local.candidate);
                    local.keysTried++;

                    if (ic > local.bestIc)
                    {
                        local.bestIc = ic;
                        Array.Copy(local.start, local.bestStart, 10);
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
                        Array.Copy(local.bestStart, best.Start, 10);
                    }
                }
            });

        if (sw.ElapsedTicks > timeoutTicks) timedOut = true;

        sw.Stop();
        return new CrackResultT52e
        {
            Found = best.Ic >= IcScorer.BaudotGermanThresholdInt,
            TimedOut = timedOut,
            KeysTried = keysTried,
            ElapsedSeconds = sw.Elapsed.TotalSeconds,
            WheelStart = best.Start,
            BestIc = best.Ic,
        };
    }

    class BestHolder
    {
        public int Ic;
        public int[] Start = new int[10];
    }

    class TLS
    {
        public byte[] candidate;
        public int[] start = new int[10];
        public T52eMachine machine;
        public long keysTried;
        public int bestIc;
        public int[] bestStart = new int[10];
        public TLS(int n, byte[][] pins, int[] switchMap, int[] knownStart)
        {
            candidate = new byte[n];
            Array.Copy(knownStart, start, 10);
            machine = T52eMachine.Create(pins, switchMap, knownStart, ktf: false);
        }
    }
}
