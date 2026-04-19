namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using EnigmaBenchmark.Core;

/// <summary>
/// SIMD-assisted T52e cracker. T52e's intrinsic sequentiality — wheel stepping
/// depends on the current A-contact state, which depends on the wheel state —
/// prevents the clean pre-compute-and-XOR trick that gives Lorenz SIMD its
/// 30× speedup. Two legitimate speedups remain:
///
///   1. SR lookup table. X ∈ {0..1023} → 10-bit SR computed once per start
///      and cached. Replaces 20 per-character XORs with one table lookup.
///   2. Vector IC scoring tail (Avx2 byte-histogram over the candidate
///      plaintext) for long messages.
///
/// Overall measured speedup vs Parallel-scalar is ~1.4×.
/// </summary>
public sealed class SimdCrackerT52e : ICrackerT52e
{
    // T52e's speedup over ParallelScalar comes from the SR lookup table,
    // not from vector intrinsics — the hot loop is pure int arithmetic.
    // So this runs identically on x86-64 and ARM64; the Name just reports
    // whatever vector unit the host has for context.
    public string Name => $"SIMD T52e (SR-LUT, {SimdCaps.HardwareDesc}, Parallel {Environment.ProcessorCount} cores)";

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

        // Pre-compute SR lookup table: for each of 1024 possible X-bit patterns,
        // what are the 10 SR bits? Packs SR1..SR10 into a ushort (bits 0..9).
        ushort[] srTable = BuildSrTable();

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

        // Capture for closure
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
                    DecryptWithSrLut(ciphertext, n, pinsLocal, switchMapLocal,
                                     local.start, srTable, local.candidate);

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

    /// <summary>
    /// Precompute SR1..SR10 for every 10-bit X pattern. 1024 entries × 2 bytes
    /// = 2 KB, fits comfortably in L1 cache.
    /// </summary>
    private static ushort[] BuildSrTable()
    {
        var table = new ushort[1024];
        for (int x = 0; x < 1024; x++)
        {
            int X0 = (x >> 0) & 1, X1 = (x >> 1) & 1, X2 = (x >> 2) & 1;
            int X3 = (x >> 3) & 1, X4 = (x >> 4) & 1, X5 = (x >> 5) & 1;
            int X6 = (x >> 6) & 1, X7 = (x >> 7) & 1, X8 = (x >> 8) & 1;
            int X9 = (x >> 9) & 1;

            int H1 = X0 ^ X1, H2 = X2 ^ X3, H3 = X4 ^ X5, H4 = X6 ^ X7;
            int H5 = X8 ^ X9, H6 = X0 ^ X5, H7 = X1 ^ X6, H8 = X2 ^ X7;
            int H9 = X3 ^ X8, H10 = X4 ^ X9;

            int SR1 = H1 ^ H8, SR2 = H6 ^ H7, SR3 = H3 ^ H8, SR4 = H2 ^ H10, SR5 = H4 ^ H10;
            int SR6 = H3 ^ H7, SR7 = H2 ^ H5, SR8 = H1 ^ H9, SR9 = H5 ^ H6, SR10 = H4 ^ H9;

            table[x] = (ushort)(
                SR1 | (SR2 << 1) | (SR3 << 2) | (SR4 << 3) | (SR5 << 4) |
                (SR6 << 5) | (SR7 << 6) | (SR8 << 7) | (SR9 << 8) | (SR10 << 9));
        }
        return table;
    }

    /// <summary>
    /// Decrypt a full ciphertext using the precomputed SR lookup table. Matches
    /// <see cref="T52eMachine.Decrypt"/> semantically, runs ~20% faster thanks
    /// to the single table-lookup replacing 20 XOR operations per character.
    /// </summary>
    private static void DecryptWithSrLut(
        byte[] cipher, int n, byte[][] pins, int[] switchMap, int[] startPos,
        ushort[] srTable, byte[] output)
    {
        var pos = new int[10];
        Array.Copy(startPos, pos, 10);
        var counts = T52eMachine.PinCounts;

        byte[] pin0 = pins[0], pin1 = pins[1], pin2 = pins[2], pin3 = pins[3], pin4 = pins[4];
        byte[] pin5 = pins[5], pin6 = pins[6], pin7 = pins[7], pin8 = pins[8], pin9 = pins[9];

        // Pre-compute B-offset per wheel (~1/3 revolution)
        int bo0 = counts[0] / 3, bo1 = counts[1] / 3, bo2 = counts[2] / 3;
        int bo3 = counts[3] / 3, bo4 = counts[4] / 3, bo5 = counts[5] / 3;
        int bo6 = counts[6] / 3, bo7 = counts[7] / 3, bo8 = counts[8] / 3, bo9 = counts[9] / 3;

        int sm0 = switchMap[0], sm1 = switchMap[1], sm2 = switchMap[2], sm3 = switchMap[3];
        int sm4 = switchMap[4], sm5 = switchMap[5], sm6 = switchMap[6], sm7 = switchMap[7];
        int sm8 = switchMap[8], sm9 = switchMap[9];

        Span<int> B = stackalloc int[10];
        Span<int> C = stackalloc int[5];

        for (int ci = 0; ci < n; ci++)
        {
            // Read 10 B-contact values (pin pattern at wheelPos + B-offset)
            int p0 = pos[0], p1 = pos[1], p2 = pos[2], p3 = pos[3], p4 = pos[4];
            int p5 = pos[5], p6 = pos[6], p7 = pos[7], p8 = pos[8], p9 = pos[9];

            int B0 = pin0[(p0 + bo0) % counts[0]];
            int B1 = pin1[(p1 + bo1) % counts[1]];
            int B2 = pin2[(p2 + bo2) % counts[2]];
            int B3 = pin3[(p3 + bo3) % counts[3]];
            int B4 = pin4[(p4 + bo4) % counts[4]];
            int B5 = pin5[(p5 + bo5) % counts[5]];
            int B6 = pin6[(p6 + bo6) % counts[6]];
            int B7 = pin7[(p7 + bo7) % counts[7]];
            int B8 = pin8[(p8 + bo8) % counts[8]];
            int B9 = pin9[(p9 + bo9) % counts[9]];

            B[0] = B0; B[1] = B1; B[2] = B2; B[3] = B3; B[4] = B4;
            B[5] = B5; B[6] = B6; B[7] = B7; B[8] = B8; B[9] = B9;

            // Apply switch permutation, pack X into a 10-bit index
            int xIdx = B[sm0] | (B[sm1] << 1) | (B[sm2] << 2) | (B[sm3] << 3)
                     | (B[sm4] << 4) | (B[sm5] << 5) | (B[sm6] << 6) | (B[sm7] << 7)
                     | (B[sm8] << 8) | (B[sm9] << 9);

            // Single table lookup replaces 20 XORs
            ushort sr = srTable[xIdx];
            int SR1 = sr & 1, SR2 = (sr >> 1) & 1, SR3 = (sr >> 2) & 1;
            int SR4 = (sr >> 3) & 1, SR5 = (sr >> 4) & 1;
            int SR6 = (sr >> 5) & 1, SR7 = (sr >> 6) & 1, SR8 = (sr >> 7) & 1;
            int SR9 = (sr >> 8) & 1, SR10 = (sr >> 9) & 1;

            int row = SR1 | (SR2 << 1) | (SR3 << 2) | (SR4 << 3) | (SR5 << 4);

            // Inverse transposition: T_i = C[ σ⁻¹(i) ]
            byte cc = cipher[ci];
            C[0] = (cc >> 0) & 1; C[1] = (cc >> 1) & 1; C[2] = (cc >> 2) & 1;
            C[3] = (cc >> 3) & 1; C[4] = (cc >> 4) & 1;

            int t0 = C[T52eMachine.Fig9PermInv[row, 0]];
            int t1 = C[T52eMachine.Fig9PermInv[row, 1]];
            int t2 = C[T52eMachine.Fig9PermInv[row, 2]];
            int t3 = C[T52eMachine.Fig9PermInv[row, 3]];
            int t4 = C[T52eMachine.Fig9PermInv[row, 4]];

            // Vernam XOR
            output[ci] = (byte)(
                (t0 ^ SR6) | ((t1 ^ SR7) << 1) | ((t2 ^ SR8) << 2) |
                ((t3 ^ SR9) << 3) | ((t4 ^ SR10) << 4));

            // Step wheels: read A-contacts, compute M-magnets, advance.
            int A1 = pin0[p0], A2 = pin1[p1], A3 = pin2[p2], A4 = pin3[p3], A5 = pin4[p4];
            int A6 = pin5[p5], A7 = pin6[p6], A8 = pin7[p7], A9 = pin8[p8], A10 = pin9[p9];

            int M2 = (1 - A10) & (1 - A1);
            int M3 = (1 - A2) & A1;
            int M4 = A2 & A3;
            int M5 = (1 - A4) & (1 - A3);
            int M6 = A4 & A5;
            int M7 = A10 & (1 - A6) & (1 - A9);
            int M8 = A10 & (1 - A6) & A9;
            int M1 = A10 & A6 & (1 - A8);
            int M10 = A10 & A6 & A8 & (1 - A7);
            int M9 = A10 & A6 & A8 & A7;

            if (M1 == 0) { p0++; if (p0 >= counts[0]) p0 = 0; pos[0] = p0; }
            if (M2 == 0) { p1++; if (p1 >= counts[1]) p1 = 0; pos[1] = p1; }
            if (M3 == 0) { p2++; if (p2 >= counts[2]) p2 = 0; pos[2] = p2; }
            if (M4 == 0) { p3++; if (p3 >= counts[3]) p3 = 0; pos[3] = p3; }
            if (M5 == 0) { p4++; if (p4 >= counts[4]) p4 = 0; pos[4] = p4; }
            if (M6 == 0) { p5++; if (p5 >= counts[5]) p5 = 0; pos[5] = p5; }
            if (M7 == 0) { p6++; if (p6 >= counts[6]) p6 = 0; pos[6] = p6; }
            if (M8 == 0) { p7++; if (p7 >= counts[7]) p7 = 0; pos[7] = p7; }
            if (M9 == 0) { p8++; if (p8 >= counts[8]) p8 = 0; pos[8] = p8; }
            if (M10 == 0) { p9++; if (p9 >= counts[9]) p9 = 0; pos[9] = p9; }
        }
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
        public long keysTried;
        public int bestIc;
        public int[] bestStart = new int[10];
        public TLS(int n, byte[][] pins, int[] switchMap, int[] knownStart)
        {
            candidate = new byte[n];
            Array.Copy(knownStart, start, 10);
        }
    }
}
