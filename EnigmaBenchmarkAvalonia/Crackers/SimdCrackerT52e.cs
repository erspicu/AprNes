namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using System.Runtime.Intrinsics;
using EnigmaBenchmark.Core;

/// <summary>
/// Lane-parallel SIMD T52e cracker. Runs <strong>4 candidate keys per SIMD
/// batch</strong> within each worker thread, using <c>Vector128&lt;int&gt;</c>
/// — which the JIT emits as SSE2 on x86-64 and NEON on ARM64. Combined with
/// the outer <c>Parallel.ForEach</c> over (s7, s8, s9) tuples, the total
/// throughput is (4 SIMD lanes) × (N CPU cores).
///
/// Pipeline per character (per 4-lane batch):
///   - 10 software gathers    → 4-lane A contacts from wheel pin arrays
///   - 10 software gathers    → 4-lane B contacts (pos + wheel B-offset)
///   - switch-map indirection → 10 X values (constant-time lookup into
///                              stackalloc'd Vector128 span)
///   - 20 Vector128.Xor       → H and SR layers (REPLACES scalar SR-LUT —
///                              in vector form, 20 XOR ops beat 1 gather)
///   - 5 software gathers     → Fig9 perm inverse row lookup (LUT kept
///                              here: column-transposed 5×32 table)
///   - 5 per-lane t_i lookups → inverse transposition of cipher bits
///   - 5 Vector128.Xor        → Vernam bits
///   - per-lane output assembly → 4 bytes written
///   - 10 Vector128 AND/NOT   → M-magnet equations
///   - 10 conditional increments → wheel stepping (compare-equal-zero mask)
///
/// Scalar tail covers the last 67 mod 4 = 3 s6 values per (s7,s8,s9) unit.
/// </summary>
public sealed class SimdCrackerT52e : ICrackerT52e
{
    public string Name => $"SIMD T52e (Vector128 4-lane + Fig9 LUT, {SimdCaps.HardwareDesc}, Parallel {Environment.ProcessorCount} cores)";

    // Column-transposed Fig9 perm inverse: permInvCol[col, row] = Fig9PermInv[row, col].
    // Indexing by (col, row) keeps every column in its own contiguous 32-byte run,
    // letting us do 5 cheap software gathers (one per output bit) per character.
    static readonly byte[,] PermInvCol = BuildPermInvCol();

    static byte[,] BuildPermInvCol()
    {
        var t = new byte[5, 32];
        for (int row = 0; row < 32; row++)
            for (int col = 0; col < 5; col++)
                t[col, row] = T52eMachine.Fig9PermInv[row, col];
        return t;
    }

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

        // B-offset per wheel (~1/3 revolution)
        int[] counts = T52eMachine.PinCounts;
        int[] bOffset = new int[10];
        for (int i = 0; i < 10; i++) bOffset[i] = counts[i] / 3;

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

        var pinsLocal = pins;
        var switchMapLocal = switchMap;
        var knownStartLocal = knownStart;

        Parallel.ForEach(units,
            () => new TLS(n, pinsLocal, switchMapLocal, knownStartLocal, bOffset, counts),
            (unit, state, local) =>
            {
                if (sw.ElapsedTicks > timeoutTicks) { state.Stop(); return local; }

                local.start[7] = unit.s7;
                local.start[8] = unit.s8;
                local.start[9] = unit.s9;

                // Process s6 in groups of 4 via SIMD
                int s6 = 0;
                for (; s6 + 3 < c6; s6 += 4)
                {
                    DecryptSimd4(ciphertext, n, pinsLocal, bOffset, counts,
                                 switchMapLocal, local.start, s6, local.outBufs);

                    for (int lane = 0; lane < 4; lane++)
                    {
                        int ic = IcScorer.ScoreBaudotInt(local.outBufs[lane]);
                        local.keysTried++;
                        if (ic > local.bestIc)
                        {
                            local.bestIc = ic;
                            Array.Copy(local.start, local.bestStart, 10);
                            local.bestStart[6] = s6 + lane;
                        }
                    }
                }

                // Scalar tail for last < 4 candidates
                for (; s6 < c6; s6++)
                {
                    local.start[6] = s6;
                    local.machine.SetStart(local.start);
                    for (int i = 0; i < n; i++)
                        local.outBufs[0][i] = local.machine.Decrypt(ciphertext[i]);

                    int ic = IcScorer.ScoreBaudotInt(local.outBufs[0]);
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
    /// Decrypt 4 candidates in parallel via Vector128 lanes. s6 values are
    /// (s6Base, s6Base+1, s6Base+2, s6Base+3); all other wheel starts are
    /// the same across the 4 lanes and read from <paramref name="startBase"/>.
    /// </summary>
    private static unsafe void DecryptSimd4(
        byte[] cipher, int n,
        byte[][] pins, int[] bOffset, int[] counts,
        int[] switchMap, int[] startBase, int s6Base,
        byte[][] outBuf4)
    {
        // Wheel positions — 10 Vector128<int>, each lane = one candidate
        var pos0 = Vector128.Create(startBase[0]);
        var pos1 = Vector128.Create(startBase[1]);
        var pos2 = Vector128.Create(startBase[2]);
        var pos3 = Vector128.Create(startBase[3]);
        var pos4 = Vector128.Create(startBase[4]);
        var pos5 = Vector128.Create(startBase[5]);
        var pos6 = Vector128.Create(s6Base, s6Base + 1, s6Base + 2, s6Base + 3);
        var pos7 = Vector128.Create(startBase[7]);
        var pos8 = Vector128.Create(startBase[8]);
        var pos9 = Vector128.Create(startBase[9]);

        // Hoisted constants
        var v_1 = Vector128.Create(1);
        var v_0 = Vector128<int>.Zero;
        var count0 = Vector128.Create(counts[0]);
        var count1 = Vector128.Create(counts[1]);
        var count2 = Vector128.Create(counts[2]);
        var count3 = Vector128.Create(counts[3]);
        var count4 = Vector128.Create(counts[4]);
        var count5 = Vector128.Create(counts[5]);
        var count6 = Vector128.Create(counts[6]);
        var count7 = Vector128.Create(counts[7]);
        var count8 = Vector128.Create(counts[8]);
        var count9 = Vector128.Create(counts[9]);
        var bo0 = Vector128.Create(bOffset[0]);
        var bo1 = Vector128.Create(bOffset[1]);
        var bo2 = Vector128.Create(bOffset[2]);
        var bo3 = Vector128.Create(bOffset[3]);
        var bo4 = Vector128.Create(bOffset[4]);
        var bo5 = Vector128.Create(bOffset[5]);
        var bo6 = Vector128.Create(bOffset[6]);
        var bo7 = Vector128.Create(bOffset[7]);
        var bo8 = Vector128.Create(bOffset[8]);
        var bo9 = Vector128.Create(bOffset[9]);

        int sm0 = switchMap[0], sm1 = switchMap[1], sm2 = switchMap[2];
        int sm3 = switchMap[3], sm4 = switchMap[4], sm5 = switchMap[5];
        int sm6 = switchMap[6], sm7 = switchMap[7], sm8 = switchMap[8];
        int sm9 = switchMap[9];

        byte[] pin0 = pins[0], pin1 = pins[1], pin2 = pins[2], pin3 = pins[3], pin4 = pins[4];
        byte[] pin5 = pins[5], pin6 = pins[6], pin7 = pins[7], pin8 = pins[8], pin9 = pins[9];

        Span<Vector128<int>> B = stackalloc Vector128<int>[10];
        Span<int> ext = stackalloc int[4];
        Span<int> cBits = stackalloc int[5];

        fixed (byte* pp0 = pin0) fixed (byte* pp1 = pin1) fixed (byte* pp2 = pin2)
        fixed (byte* pp3 = pin3) fixed (byte* pp4 = pin4) fixed (byte* pp5 = pin5)
        fixed (byte* pp6 = pin6) fixed (byte* pp7 = pin7) fixed (byte* pp8 = pin8)
        fixed (byte* pp9 = pin9)
        fixed (byte* pPermInv = &PermInvCol[0, 0])
        {
            for (int ci = 0; ci < n; ci++)
            {
                // ── Read A contacts at current pos (10 software gathers) ──
                var A0 = GatherByte(pp0, pos0);
                var A1 = GatherByte(pp1, pos1);
                var A2 = GatherByte(pp2, pos2);
                var A3 = GatherByte(pp3, pos3);
                var A4 = GatherByte(pp4, pos4);
                var A5 = GatherByte(pp5, pos5);
                var A6 = GatherByte(pp6, pos6);
                var A7 = GatherByte(pp7, pos7);
                var A8 = GatherByte(pp8, pos8);
                var A9 = GatherByte(pp9, pos9);

                // ── Read B contacts (pos + B-offset, mod count) ──
                B[0] = GatherByte(pp0, ModWrap(pos0 + bo0, count0));
                B[1] = GatherByte(pp1, ModWrap(pos1 + bo1, count1));
                B[2] = GatherByte(pp2, ModWrap(pos2 + bo2, count2));
                B[3] = GatherByte(pp3, ModWrap(pos3 + bo3, count3));
                B[4] = GatherByte(pp4, ModWrap(pos4 + bo4, count4));
                B[5] = GatherByte(pp5, ModWrap(pos5 + bo5, count5));
                B[6] = GatherByte(pp6, ModWrap(pos6 + bo6, count6));
                B[7] = GatherByte(pp7, ModWrap(pos7 + bo7, count7));
                B[8] = GatherByte(pp8, ModWrap(pos8 + bo8, count8));
                B[9] = GatherByte(pp9, ModWrap(pos9 + bo9, count9));

                // ── Switch perm: X[i] = B[switchMap[i]] ──
                var X0 = B[sm0];
                var X1 = B[sm1];
                var X2 = B[sm2];
                var X3 = B[sm3];
                var X4 = B[sm4];
                var X5 = B[sm5];
                var X6 = B[sm6];
                var X7 = B[sm7];
                var X8 = B[sm8];
                var X9 = B[sm9];

                // ── H layer (10 vector XORs) ──
                var H1  = X0 ^ X1;
                var H2  = X2 ^ X3;
                var H3  = X4 ^ X5;
                var H4  = X6 ^ X7;
                var H5  = X8 ^ X9;
                var H6  = X0 ^ X5;
                var H7  = X1 ^ X6;
                var H8  = X2 ^ X7;
                var H9  = X3 ^ X8;
                var H10 = X4 ^ X9;

                // ── SR layer (10 vector XORs) ──
                var SR1  = H1 ^ H8;
                var SR2  = H6 ^ H7;
                var SR3  = H3 ^ H8;
                var SR4  = H2 ^ H10;
                var SR5  = H4 ^ H10;
                var SR6  = H3 ^ H7;
                var SR7  = H2 ^ H5;
                var SR8  = H1 ^ H9;
                var SR9  = H5 ^ H6;
                var SR10 = H4 ^ H9;

                // ── Row index = SR1 | SR2<<1 | ... | SR5<<4 ──
                var row = SR1
                        | (SR2 << 1)
                        | (SR3 << 2)
                        | (SR4 << 3)
                        | (SR5 << 4);

                // ── Gather Fig9 perm inverse columns (5 gathers) ──
                // PermInvCol layout: permInvCol[col, row] → offset = col * 32 + row
                var v_32 = Vector128.Create(32);
                var inv0 = GatherByte(pPermInv + 0 * 32, row);
                var inv1 = GatherByte(pPermInv + 1 * 32, row);
                var inv2 = GatherByte(pPermInv + 2 * 32, row);
                var inv3 = GatherByte(pPermInv + 3 * 32, row);
                var inv4 = GatherByte(pPermInv + 4 * 32, row);

                // ── Cipher bits (scalar, same for all 4 lanes) ──
                byte cc = cipher[ci];
                int cb0 = (cc >> 0) & 1, cb1 = (cc >> 1) & 1, cb2 = (cc >> 2) & 1;
                int cb3 = (cc >> 3) & 1, cb4 = (cc >> 4) & 1;

                // ── Apply inverse transposition: t_i = cipherBits[inv_i] ──
                // inv_i is per-lane int in {0..4}; cipherBits is 5 scalar ints.
                cBits[0] = cb0; cBits[1] = cb1; cBits[2] = cb2; cBits[3] = cb3; cBits[4] = cb4;
                fixed (int* pCb = cBits)
                {
                    var t0 = GatherInt(pCb, inv0);
                    var t1 = GatherInt(pCb, inv1);
                    var t2 = GatherInt(pCb, inv2);
                    var t3 = GatherInt(pCb, inv3);
                    var t4 = GatherInt(pCb, inv4);

                    // ── Vernam XOR ──
                    var pBit0 = t0 ^ SR6;
                    var pBit1 = t1 ^ SR7;
                    var pBit2 = t2 ^ SR8;
                    var pBit3 = t3 ^ SR9;
                    var pBit4 = t4 ^ SR10;

                    // ── Assemble output: out = b0 | (b1<<1) | ... | (b4<<4) ──
                    var outVec = pBit0
                               | (pBit1 << 1)
                               | (pBit2 << 2)
                               | (pBit3 << 3)
                               | (pBit4 << 4);

                    outVec.CopyTo(ext);
                    outBuf4[0][ci] = (byte)ext[0];
                    outBuf4[1][ci] = (byte)ext[1];
                    outBuf4[2][ci] = (byte)ext[2];
                    outBuf4[3][ci] = (byte)ext[3];
                }

                // ── M-magnet equations (Vector128 AND/XOR for NOT-of-bit) ──
                // For single-bit values, NOT = XOR with 1
                var nA0 = A0 ^ v_1; var nA1 = A1 ^ v_1; var nA2 = A2 ^ v_1;
                var nA3 = A3 ^ v_1; var nA5 = A5 ^ v_1; var nA6 = A6 ^ v_1;
                var nA7 = A7 ^ v_1; var nA8 = A8 ^ v_1; var nA9 = A9 ^ v_1;

                // Spec (§3.5): M_i uses A1..A10 which are 1-indexed.
                // Our A0..A9 are 0-indexed: A0 = paper's A1, etc.
                var M1  = A9 & A5 & nA7;           // A10 ∧ A6 ∧ ¬A8
                var M2  = nA9 & nA0;                // ¬A10 ∧ ¬A1
                var M3  = nA1 & A0;                 // ¬A2 ∧ A1
                var M4  = A1 & A2;                  // A2 ∧ A3
                var M5  = nA3 & nA2;                // ¬A4 ∧ ¬A3
                var M6  = A3 & A4;                  // A4 ∧ A5
                var M7  = A9 & nA5 & nA8;           // A10 ∧ ¬A6 ∧ ¬A9
                var M8  = A9 & nA5 & A8;            // A10 ∧ ¬A6 ∧ A9
                var M9  = A9 & A5 & A7 & A6;        // A10 ∧ A6 ∧ A8 ∧ A7
                var M10 = A9 & A5 & A7 & nA6;       // A10 ∧ A6 ∧ A8 ∧ ¬A7

                // ── Wheel step: pos_i += (M_i == 0) ? 1 : 0; then wrap mod count ──
                // advance_i = Vector128.Equals(M_i, 0) & 1
                // Equals returns all-ones for equal lanes; AND with 1 isolates.
                pos0 = ModWrap(pos0 + (Vector128.Equals(M1,  v_0) & v_1), count0);
                pos1 = ModWrap(pos1 + (Vector128.Equals(M2,  v_0) & v_1), count1);
                pos2 = ModWrap(pos2 + (Vector128.Equals(M3,  v_0) & v_1), count2);
                pos3 = ModWrap(pos3 + (Vector128.Equals(M4,  v_0) & v_1), count3);
                pos4 = ModWrap(pos4 + (Vector128.Equals(M5,  v_0) & v_1), count4);
                pos5 = ModWrap(pos5 + (Vector128.Equals(M6,  v_0) & v_1), count5);
                pos6 = ModWrap(pos6 + (Vector128.Equals(M7,  v_0) & v_1), count6);
                pos7 = ModWrap(pos7 + (Vector128.Equals(M8,  v_0) & v_1), count7);
                pos8 = ModWrap(pos8 + (Vector128.Equals(M9,  v_0) & v_1), count8);
                pos9 = ModWrap(pos9 + (Vector128.Equals(M10, v_0) & v_1), count9);
            }
        }
    }

    /// <summary>Software gather: 4 byte reads from <paramref name="table"/> at per-lane indices; result zero-extended into Vector128&lt;int&gt;.</summary>
    private static unsafe Vector128<int> GatherByte(byte* table, Vector128<int> indices)
    {
        return Vector128.Create(
            (int)table[indices.GetElement(0)],
            (int)table[indices.GetElement(1)],
            (int)table[indices.GetElement(2)],
            (int)table[indices.GetElement(3)]);
    }

    /// <summary>Software gather: 4 int reads from table at per-lane indices.</summary>
    private static unsafe Vector128<int> GatherInt(int* table, Vector128<int> indices)
    {
        return Vector128.Create(
            table[indices.GetElement(0)],
            table[indices.GetElement(1)],
            table[indices.GetElement(2)],
            table[indices.GetElement(3)]);
    }

    /// <summary>Single-iteration mod: if lane &gt;= count, subtract count. Sufficient when lane &lt; 2×count.</summary>
    private static Vector128<int> ModWrap(Vector128<int> v, Vector128<int> count)
    {
        var mask = Vector128.GreaterThanOrEqual(v, count);
        return v - (mask & count);
    }

    class BestHolder
    {
        public int Ic;
        public int[] Start = new int[10];
    }

    class TLS
    {
        public byte[][] outBufs;          // 4 × byte[n] for lane outputs
        public int[] start = new int[10];
        public long keysTried;
        public int bestIc;
        public int[] bestStart = new int[10];
        public T52eMachine machine;       // for scalar tail

        public TLS(int n, byte[][] pins, int[] switchMap, int[] knownStart,
                   int[] _bo, int[] _counts)
        {
            outBufs = new byte[4][];
            for (int i = 0; i < 4; i++) outBufs[i] = new byte[n];
            Array.Copy(knownStart, start, 10);
            machine = T52eMachine.Create(pins, switchMap, knownStart, ktf: false);
        }
    }
}
