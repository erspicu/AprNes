namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using EnigmaBenchmark.Core;

/// <summary>
/// AVX2 SIMD cracker: batches 8 PR (right-rotor starting position) values per
/// lane via Vector256&lt;int&gt; with Avx2.GatherVector256 for rotor table lookups.
///
/// Each SIMD batch runs 8 Enigma machines in parallel on the same ciphertext,
/// differing only in their initial right-rotor position. Outer loops (wheel
/// order, ring, pl, pm) and outermost Parallel.ForEach match the scalar path.
///
/// If AVX2 is unavailable, falls back to ParallelScalarCracker (not SIMD, but
/// keeps the benchmark producing valid output on any CPU).
/// </summary>
public sealed class SimdCracker : ICracker
{
    public string Name
    {
        get
        {
            int n = Environment.ProcessorCount;
            if (SimdCaps.HasAvx2)
                return $"SIMD M3 (Vector256/AVX2 + hw gather, Parallel {n} cores)";
            if (SimdCaps.HasNeon)
                return $"SIMD M3 (Vector128/NEON + software gather, Parallel {n} cores)";
            return "SIMD M3 (unavailable → Parallel scalar fallback)";
        }
    }

    // Int-widened rotor/reflector tables (Avx2.GatherVector256 needs 32-bit src)
    static readonly int[][] FwdInt;
    static readonly int[][] RevInt;
    static readonly int[]   UkwBInt;

    static SimdCracker()
    {
        FwdInt = new int[5][];
        RevInt = new int[5][];
        for (int r = 0; r < 5; r++)
        {
            FwdInt[r] = new int[26];
            RevInt[r] = new int[26];
            for (int i = 0; i < 26; i++)
            {
                FwdInt[r][i] = RotorData.Fwd[r][i];
                RevInt[r][i] = RotorData.Rev[r][i];
            }
        }
        UkwBInt = new int[26];
        for (int i = 0; i < 26; i++) UkwBInt[i] = RotorData.UkwB[i];
    }

    public CrackResult Crack(byte[] ciphertext, EnigmaM3 fixedParts, CrackScope scope)
    {
        if (!SimdCaps.HasAnyVector)
            return new ParallelScalarCracker().Crack(ciphertext, fixedParts, scope);

        var sw = Stopwatch.StartNew();
        long keysTried = 0;

        // Build work units identical to ParallelScalarCracker
        int rrMin = 0, rrMax = 26;
        int rmMin = fixedParts.RM, rmMax = fixedParts.RM + 1;
        int rlMin = fixedParts.RL, rlMax = fixedParts.RL + 1;
        if (scope < CrackScope.Normal)   { rrMin = fixedParts.RR; rrMax = fixedParts.RR + 1; }
        if (scope >= CrackScope.Hard)    { rmMin = 0; rmMax = 26; }
        if (scope >= CrackScope.Extreme) { rlMin = 0; rlMax = 26; }

        var units = new List<(int rl, int rm, int rr, int L, int M, int R)>();
        for (int rl = rlMin; rl < rlMax; rl++)
        for (int rm = rmMin; rm < rmMax; rm++)
        for (int rr = rrMin; rr < rrMax; rr++)
        foreach (var (L, M, R) in RotorData.AllWheelOrders())
            units.Add((rl, rm, rr, L, M, R));

        var best = new BestHolder();

        // Int ciphertext for vector broadcast
        var ctInt = new int[ciphertext.Length];
        for (int i = 0; i < ciphertext.Length; i++) ctInt[i] = ciphertext[i];

        var plugboardInt = new int[26];
        for (int i = 0; i < 26; i++) plugboardInt[i] = fixedParts.Plugboard[i];

        bool useAvx2 = SimdCaps.HasAvx2;

        Parallel.ForEach(units,
            () => new TLS(ctInt.Length),
            (unit, _, local) =>
            {
                if (useAvx2)
                    RunUnit(ctInt, plugboardInt, unit.L, unit.M, unit.R,
                            unit.rl, unit.rm, unit.rr, local);
                else
                    RunUnitNeon(ctInt, plugboardInt, unit.L, unit.M, unit.R,
                                unit.rl, unit.rm, unit.rr, local);
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

    static unsafe void RunUnit(
        int[] ct, int[] plugboardInt,
        int WL, int WM, int WR,
        int RL, int RM, int RR,
        TLS local)
    {
        var FwdWR = FwdInt[WR]; var FwdWM = FwdInt[WM]; var FwdWL = FwdInt[WL];
        var RevWR = RevInt[WR]; var RevWM = RevInt[WM]; var RevWL = RevInt[WL];
        int NotchR = RotorData.NotchPos[WR];
        int NotchM = RotorData.NotchPos[WM];

        // Precompute (26 - ring) adds as constants
        var v_Rr_inv = Vector256.Create(26 - RR);
        var v_Rm_inv = Vector256.Create(26 - RM);
        var v_Rl_inv = Vector256.Create(26 - RL);
        var v_Rr     = Vector256.Create(RR);
        var v_Rm     = Vector256.Create(RM);
        var v_Rl     = Vector256.Create(RL);
        var v_26     = Vector256.Create(26);
        var v_1      = Vector256.Create(1);
        var v_notchR = Vector256.Create(NotchR);
        var v_notchM = Vector256.Create(NotchM);

        int ctLen = ct.Length;

        // Hoisted OUT of all inner loops. stackalloc inside a C# loop does NOT
        // release until the method returns, so an inner-loop stackalloc
        // accumulates to hundreds of MB and blows the stack.
        Span<int> tmp = stackalloc int[8];

        fixed (int* pFwdR = FwdWR)
        fixed (int* pFwdM = FwdWM)
        fixed (int* pFwdL = FwdWL)
        fixed (int* pRevR = RevWR)
        fixed (int* pRevM = RevWM)
        fixed (int* pRevL = RevWL)
        fixed (int* pUkwB = UkwBInt)
        fixed (int* pPB   = plugboardInt)
        fixed (int* pCt   = ct)
        {
            for (int pl = 0; pl < 26; pl++)
            for (int pm = 0; pm < 26; pm++)
            {
                // 3 full batches (0-7, 8-15, 16-23) + scalar tail for 24, 25
                for (int prBase = 0; prBase <= 16; prBase += 8)
                {
                    var PR_vec = Vector256.Create(
                        prBase, prBase+1, prBase+2, prBase+3,
                        prBase+4, prBase+5, prBase+6, prBase+7);
                    var PL_vec = Vector256.Create(pl);
                    var PM_vec = Vector256.Create(pm);

                    // Run full encryption per character, keep per-lane output in local.outBuf
                    for (int i = 0; i < ctLen; i++)
                    {
                        // ── Step rotors per lane ──
                        var midMask   = Vector256.Equals(PM_vec, v_notchM);
                        var rightMask = Vector256.Equals(PR_vec, v_notchR);
                        var midAdv    = midMask | rightMask;          // mid steps if mid OR right on notch
                        var leftAdv   = midMask;                       // left steps only on mid-notch (double-step)

                        PL_vec = PL_vec + (leftAdv & v_1);
                        PM_vec = PM_vec + (midAdv  & v_1);
                        PR_vec = PR_vec + v_1;
                        PL_vec = Mod26(PL_vec, v_26);
                        PM_vec = Mod26(PM_vec, v_26);
                        PR_vec = Mod26(PR_vec, v_26);

                        // ── Load broadcast ciphertext char ──
                        var c_vec = Vector256.Create(pCt[i]);

                        // ── Plugboard in ──
                        c_vec = Avx2.GatherVector256(pPB, c_vec, 4);

                        // ── Right rotor forward ──
                        c_vec = ForwardStep(c_vec, PR_vec, v_Rr_inv, v_Rr, pFwdR, v_26);

                        // ── Middle rotor forward ──
                        c_vec = ForwardStep(c_vec, PM_vec, v_Rm_inv, v_Rm, pFwdM, v_26);

                        // ── Left rotor forward ──
                        c_vec = ForwardStep(c_vec, PL_vec, v_Rl_inv, v_Rl, pFwdL, v_26);

                        // ── Reflector UKW-B ──
                        c_vec = Avx2.GatherVector256(pUkwB, c_vec, 4);

                        // ── Left rotor reverse ──
                        c_vec = ForwardStep(c_vec, PL_vec, v_Rl_inv, v_Rl, pRevL, v_26);

                        // ── Middle rotor reverse ──
                        c_vec = ForwardStep(c_vec, PM_vec, v_Rm_inv, v_Rm, pRevM, v_26);

                        // ── Right rotor reverse ──
                        c_vec = ForwardStep(c_vec, PR_vec, v_Rr_inv, v_Rr, pRevR, v_26);

                        // ── Plugboard out ──
                        c_vec = Avx2.GatherVector256(pPB, c_vec, 4);

                        // Store per-lane output (column-major: outBuf[i*8 + lane])
                        c_vec.CopyTo(tmp);
                        for (int lane = 0; lane < 8; lane++)
                            local.outBuf[lane][i] = (byte)tmp[lane];
                    }

                    // IC scoring per lane (scalar — cheap)
                    for (int lane = 0; lane < 8; lane++)
                    {
                        int ic = IcScorer.ScoreInt(local.outBuf[lane]);
                        local.keysTried++;

                        if (ic > local.bestIc)
                        {
                            local.bestIc = ic;
                            local.bestL = WL; local.bestM = WM; local.bestR = WR;
                            local.bestPL = pl; local.bestPM = pm; local.bestPR = prBase + lane;
                            local.bestRR = RR; local.bestRM = RM; local.bestRL = RL;
                        }
                    }
                }

                // Scalar tail for pr=24, pr=25 (not covered by 3 × 8-batches)
                for (int pr = 24; pr < 26; pr++)
                {
                    // Reuse a scalar Enigma from Core
                    var m = new EnigmaM3
                    {
                        WL = WL, WM = WM, WR = WR,
                        RL = RL, RM = RM, RR = RR,
                        PL = pl, PM = pm, PR = pr,
                        Plugboard = new byte[26],
                        Reflector = RotorData.UkwB,
                    };
                    for (int k = 0; k < 26; k++) m.Plugboard[k] = (byte)plugboardInt[k];

                    for (int i = 0; i < ctLen; i++)
                        local.outBuf[0][i] = m.EncryptChar((byte)ct[i]);

                    int ic = IcScorer.ScoreInt(local.outBuf[0]);
                    local.keysTried++;

                    if (ic > local.bestIc)
                    {
                        local.bestIc = ic;
                        local.bestL = WL; local.bestM = WM; local.bestR = WR;
                        local.bestPL = pl; local.bestPM = pm; local.bestPR = pr;
                        local.bestRR = RR; local.bestRM = RM; local.bestRL = RL;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Rotor pass: shift = (c + pos + (26-ring)) mod 26 → gather rotor[shift] → (gather - pos + ring) mod 26
    /// </summary>
    static unsafe Vector256<int> ForwardStep(
        Vector256<int> c, Vector256<int> pos,
        Vector256<int> ringInv, Vector256<int> ring,
        int* rotorTable, Vector256<int> v_26)
    {
        var shift = c + pos + ringInv;
        shift = Mod26(shift, v_26);
        shift = Mod26(shift, v_26);   // range may be 0..76, needs 2 iters max

        var looked = Avx2.GatherVector256(rotorTable, shift, 4);

        var result = looked + ring - pos + v_26;
        result = Mod26(result, v_26);
        result = Mod26(result, v_26);
        return result;
    }

    /// <summary>Single-iteration mod-26 (subtract 26 where v ≥ 26). Two calls suffice for inputs ≤ 77.</summary>
    static Vector256<int> Mod26(Vector256<int> v, Vector256<int> v_26)
    {
        var mask = Vector256.GreaterThanOrEqual(v, v_26);
        return v - (mask & v_26);
    }

    // ── ARM64 NEON path (Vector128, 4 lanes, software gather) ────────────
    //
    // Mirrors RunUnit() but with:
    //   - Vector128<int>  4 lanes  (vs AVX2's 8)
    //   - 6 batches of 4 PR values  (vs AVX2's 3 batches of 8)
    //   - Software gather:  4 scalar rotor-table loads, assembled via
    //     Vector128.Create.  Rotor tables are 26 × 4 B = 104 B, fully
    //     in L1 cache — the gather overhead is tolerable.
    //   - Everything else (broadcast / add / and / compare) uses the
    //     cross-platform Vector128 API, which the JIT emits as NEON
    //     on ARM64 and SSE2 on x86 without AVX.
    //
    // Benefits over ParallelScalar on ARM:
    //   - 4-lane vectorised arithmetic for ~80% of per-character work
    //   - Same Parallel.ForEach outer structure (16-core scaling stays)
    //   - Expected 2-3× vs ParallelScalar on Apple Silicon / ARM Linux
    static unsafe void RunUnitNeon(
        int[] ct, int[] plugboardInt,
        int WL, int WM, int WR,
        int RL, int RM, int RR,
        TLS local)
    {
        var FwdWR = FwdInt[WR]; var FwdWM = FwdInt[WM]; var FwdWL = FwdInt[WL];
        var RevWR = RevInt[WR]; var RevWM = RevInt[WM]; var RevWL = RevInt[WL];
        int NotchR = RotorData.NotchPos[WR];
        int NotchM = RotorData.NotchPos[WM];

        var v_Rr_inv = Vector128.Create(26 - RR);
        var v_Rm_inv = Vector128.Create(26 - RM);
        var v_Rl_inv = Vector128.Create(26 - RL);
        var v_Rr     = Vector128.Create(RR);
        var v_Rm     = Vector128.Create(RM);
        var v_Rl     = Vector128.Create(RL);
        var v_26     = Vector128.Create(26);
        var v_1      = Vector128.Create(1);
        var v_notchR = Vector128.Create(NotchR);
        var v_notchM = Vector128.Create(NotchM);

        int ctLen = ct.Length;
        Span<int> tmp = stackalloc int[4];

        fixed (int* pFwdR = FwdWR)
        fixed (int* pFwdM = FwdWM)
        fixed (int* pFwdL = FwdWL)
        fixed (int* pRevR = RevWR)
        fixed (int* pRevM = RevWM)
        fixed (int* pRevL = RevWL)
        fixed (int* pUkwB = UkwBInt)
        fixed (int* pPB   = plugboardInt)
        fixed (int* pCt   = ct)
        {
            for (int pl = 0; pl < 26; pl++)
            for (int pm = 0; pm < 26; pm++)
            {
                // 6 batches (0-3, 4-7, ... 20-23) + scalar tail for 24, 25
                for (int prBase = 0; prBase <= 20; prBase += 4)
                {
                    var PR_vec = Vector128.Create(prBase, prBase+1, prBase+2, prBase+3);
                    var PL_vec = Vector128.Create(pl);
                    var PM_vec = Vector128.Create(pm);

                    for (int i = 0; i < ctLen; i++)
                    {
                        // Rotor stepping (same logic as AVX2 path)
                        var midMask   = Vector128.Equals(PM_vec, v_notchM);
                        var rightMask = Vector128.Equals(PR_vec, v_notchR);
                        var midAdv    = midMask | rightMask;
                        var leftAdv   = midMask;

                        PL_vec = PL_vec + (leftAdv & v_1);
                        PM_vec = PM_vec + (midAdv  & v_1);
                        PR_vec = PR_vec + v_1;
                        PL_vec = Mod26_128(PL_vec, v_26);
                        PM_vec = Mod26_128(PM_vec, v_26);
                        PR_vec = Mod26_128(PR_vec, v_26);

                        var c_vec = Vector128.Create(pCt[i]);

                        c_vec = GatherSw(pPB,   c_vec);
                        c_vec = ForwardStep128(c_vec, PR_vec, v_Rr_inv, v_Rr, pFwdR, v_26);
                        c_vec = ForwardStep128(c_vec, PM_vec, v_Rm_inv, v_Rm, pFwdM, v_26);
                        c_vec = ForwardStep128(c_vec, PL_vec, v_Rl_inv, v_Rl, pFwdL, v_26);
                        c_vec = GatherSw(pUkwB, c_vec);
                        c_vec = ForwardStep128(c_vec, PL_vec, v_Rl_inv, v_Rl, pRevL, v_26);
                        c_vec = ForwardStep128(c_vec, PM_vec, v_Rm_inv, v_Rm, pRevM, v_26);
                        c_vec = ForwardStep128(c_vec, PR_vec, v_Rr_inv, v_Rr, pRevR, v_26);
                        c_vec = GatherSw(pPB,   c_vec);

                        c_vec.CopyTo(tmp);
                        for (int lane = 0; lane < 4; lane++)
                            local.outBuf[lane][i] = (byte)tmp[lane];
                    }

                    for (int lane = 0; lane < 4; lane++)
                    {
                        int ic = IcScorer.ScoreInt(local.outBuf[lane]);
                        local.keysTried++;
                        if (ic > local.bestIc)
                        {
                            local.bestIc = ic;
                            local.bestL = WL; local.bestM = WM; local.bestR = WR;
                            local.bestPL = pl; local.bestPM = pm; local.bestPR = prBase + lane;
                            local.bestRR = RR; local.bestRM = RM; local.bestRL = RL;
                        }
                    }
                }

                // Scalar tail for pr=24, 25
                for (int pr = 24; pr < 26; pr++)
                {
                    var m = new EnigmaM3
                    {
                        WL = WL, WM = WM, WR = WR,
                        RL = RL, RM = RM, RR = RR,
                        PL = pl, PM = pm, PR = pr,
                        Plugboard = new byte[26],
                        Reflector = RotorData.UkwB,
                    };
                    for (int k = 0; k < 26; k++) m.Plugboard[k] = (byte)plugboardInt[k];

                    for (int i = 0; i < ctLen; i++)
                        local.outBuf[0][i] = m.EncryptChar((byte)ct[i]);

                    int ic = IcScorer.ScoreInt(local.outBuf[0]);
                    local.keysTried++;
                    if (ic > local.bestIc)
                    {
                        local.bestIc = ic;
                        local.bestL = WL; local.bestM = WM; local.bestR = WR;
                        local.bestPL = pl; local.bestPM = pm; local.bestPR = pr;
                        local.bestRR = RR; local.bestRM = RM; local.bestRL = RL;
                    }
                }
            }
        }
    }

    /// <summary>Software gather: 4 scalar loads from `table` at indices given by vector lanes.</summary>
    static unsafe Vector128<int> GatherSw(int* table, Vector128<int> indices)
    {
        return Vector128.Create(
            table[indices.GetElement(0)],
            table[indices.GetElement(1)],
            table[indices.GetElement(2)],
            table[indices.GetElement(3)]);
    }

    static unsafe Vector128<int> ForwardStep128(
        Vector128<int> c, Vector128<int> pos,
        Vector128<int> ringInv, Vector128<int> ring,
        int* rotorTable, Vector128<int> v_26)
    {
        var shift = c + pos + ringInv;
        shift = Mod26_128(shift, v_26);
        shift = Mod26_128(shift, v_26);

        var looked = GatherSw(rotorTable, shift);

        var result = looked + ring - pos + v_26;
        result = Mod26_128(result, v_26);
        result = Mod26_128(result, v_26);
        return result;
    }

    static Vector128<int> Mod26_128(Vector128<int> v, Vector128<int> v_26)
    {
        var mask = Vector128.GreaterThanOrEqual(v, v_26);
        return v - (mask & v_26);
    }

    class BestHolder
    {
        public int Ic, L, M, R, PL, PM, PR, RR, RM, RL;
    }

    class TLS
    {
        public long keysTried;
        public int bestIc, bestL, bestM, bestR, bestPL, bestPM, bestPR;
        public int bestRR, bestRM, bestRL;
        public byte[][] outBuf;

        public TLS(int ctLen)
        {
            outBuf = new byte[8][];
            for (int i = 0; i < 8; i++) outBuf[i] = new byte[ctLen];
        }
    }
}
