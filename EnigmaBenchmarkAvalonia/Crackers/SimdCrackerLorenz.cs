namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using EnigmaBenchmark.Core;

/// <summary>
/// AVX2 SIMD Lorenz cracker. Key optimisation is a per-tile pre-computation:
///   z'[i] = ciphertext[i] ⊕ (χ2⊕χ3⊕χ4⊕χ5 stream at position i)
/// We then strip only χ1 inside the inner loop, which reduces to a single
/// vector XOR per 32 characters. That's 30×+ cheaper than recomputing the
/// full 5-wheel chi stream for every chi1 start.
/// </summary>
public sealed class SimdCrackerLorenz : ICrackerLorenz
{
    public string Name => Avx2.IsSupported
        ? $"SIMD Lorenz (Vector256/AVX2, Parallel {Environment.ProcessorCount} cores)"
        : "SIMD Lorenz (unavailable → Parallel scalar fallback)";

    public CrackResultLorenz Crack(byte[] ciphertext, byte[][] chiPins, CrackScope scope, double timeoutSec = 0)
    {
        if (!Avx2.IsSupported)
            return new ParallelScalarCrackerLorenz().Crack(ciphertext, chiPins, scope, timeoutSec);

        var sw = Stopwatch.StartNew();
        int n = ciphertext.Length;
        int[] counts = LorenzSZ40.ChiPinCounts;
        byte[] pin0 = chiPins[0], pin1 = chiPins[1], pin2 = chiPins[2],
               pin3 = chiPins[3], pin4 = chiPins[4];
        int c0 = counts[0], c1 = counts[1], c2 = counts[2], c3 = counts[3], c4 = counts[4];

        // Replicate pin0 so positions s0..s0+n-1 don't need per-iteration wrap
        var pin0Ext = new byte[n + c0];
        for (int i = 0; i < pin0Ext.Length; i++) pin0Ext[i] = pin0[i % c0];

        var units = new List<(int s1, int s2, int s3, int s4)>(c1 * c2 * c3 * c4);
        for (int s4 = 0; s4 < c4; s4++)
        for (int s3 = 0; s3 < c3; s3++)
        for (int s2 = 0; s2 < c2; s2++)
        for (int s1 = 0; s1 < c1; s1++)
            units.Add((s1, s2, s3, s4));

        long keysTried = 0;
        var best = new BestHolder();
        bool timedOut = false;
        long timeoutTicks = timeoutSec > 0
            ? (long)(timeoutSec * Stopwatch.Frequency)
            : long.MaxValue;

        Parallel.ForEach(units,
            () => new TLS(n),
            (unit, state, local) =>
            {
                if (sw.ElapsedTicks > timeoutTicks) { state.Stop(); return local; }

                int s1 = unit.s1, s2 = unit.s2, s3 = unit.s3, s4 = unit.s4;

                // ── Pre-compute cipher ⊕ (χ2..χ5 stream) once per tile ──
                int p1 = s1, p2 = s2, p3 = s3, p4 = s4;
                for (int i = 0; i < n; i++)
                {
                    int chiOthers = (pin1[p1] << 1)
                                  | (pin2[p2] << 2)
                                  | (pin3[p3] << 3)
                                  | (pin4[p4] << 4);
                    local.stripped[i] = (byte)(ciphertext[i] ^ chiOthers);
                    if (++p1 >= c1) p1 = 0;
                    if (++p2 >= c2) p2 = 0;
                    if (++p3 >= c3) p3 = 0;
                    if (++p4 >= c4) p4 = 0;
                }

                // ── For each χ1 start, XOR stripped stream with pin0 slice ──
                for (int s0 = 0; s0 < c0; s0++)
                {
                    XorSimd(local.stripped, pin0Ext, s0, local.plaintext, n);

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

    /// <summary>out[i] = stripped[i] ⊕ pin0Ext[offset+i] for i in [0, n). 32-byte vector chunks + scalar tail.</summary>
    static unsafe void XorSimd(byte[] stripped, byte[] pin0Ext, int offset, byte[] output, int n)
    {
        fixed (byte* pS = stripped)
        fixed (byte* pP = pin0Ext)
        fixed (byte* pO = output)
        {
            int i = 0;
            for (; i <= n - 32; i += 32)
            {
                var vs = Avx.LoadVector256(pS + i);
                var vp = Avx.LoadVector256(pP + offset + i);
                var vr = Avx2.Xor(vs, vp);
                Avx.Store(pO + i, vr);
            }
            for (; i < n; i++)
                output[i] = (byte)(stripped[i] ^ pin0Ext[offset + i]);
        }
    }

    class BestHolder
    {
        public int Ic;
        public int[] Start = new int[5];
    }

    class TLS
    {
        public byte[] stripped;
        public byte[] plaintext;
        public long keysTried;
        public int bestIc;
        public int[] bestStart = new int[5];
        public TLS(int n) { stripped = new byte[n]; plaintext = new byte[n]; }
    }
}
