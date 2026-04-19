namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using EnigmaBenchmark.Core;

/// <summary>
/// AVX2 SIMD M4 cracker — same 8-lane pattern as the M3 version but with an
/// extra (static) greek rotor pass between the left rotor and the thin
/// reflector. Greek wheel + thin reflector are read from uniforms set once
/// per dispatch (they never vary within a pass).
/// </summary>
public sealed class SimdCrackerM4 : ICrackerM4
{
    public string Name
    {
        get
        {
            int n = Environment.ProcessorCount;
            if (SimdCaps.HasAvx2)
                return $"SIMD M4 (Vector256/AVX2 + hw gather, Parallel {n} cores)";
            if (SimdCaps.HasNeon)
                return $"SIMD M4 (Vector128/NEON + software gather, Parallel {n} cores)";
            return "SIMD M4 (unavailable → Parallel scalar fallback)";
        }
    }

    static readonly int[][] FwdInt;
    static readonly int[][] RevInt;
    static readonly int[][] GreekFwdInt;
    static readonly int[][] GreekRevInt;
    static readonly int[]   UkwBThinInt;
    static readonly int[]   UkwCThinInt;

    static SimdCrackerM4()
    {
        FwdInt = new int[5][];  RevInt = new int[5][];
        for (int r = 0; r < 5; r++)
        {
            FwdInt[r] = new int[26]; RevInt[r] = new int[26];
            for (int i = 0; i < 26; i++)
            {
                FwdInt[r][i] = RotorData.Fwd[r][i];
                RevInt[r][i] = RotorData.Rev[r][i];
            }
        }
        GreekFwdInt = new int[2][]; GreekRevInt = new int[2][];
        for (int g = 0; g < 2; g++)
        {
            GreekFwdInt[g] = new int[26]; GreekRevInt[g] = new int[26];
            for (int i = 0; i < 26; i++)
            {
                GreekFwdInt[g][i] = RotorData.GreekFwd[g][i];
                GreekRevInt[g][i] = RotorData.GreekRev[g][i];
            }
        }
        UkwBThinInt = new int[26];
        UkwCThinInt = new int[26];
        for (int i = 0; i < 26; i++)
        {
            UkwBThinInt[i] = RotorData.UkwBThin[i];
            UkwCThinInt[i] = RotorData.UkwCThin[i];
        }
    }

    public CrackResult Crack(byte[] ciphertext, EnigmaM4 fixedParts, CrackScope scope)
    {
        if (!SimdCaps.HasAnyVector)
            return new ParallelScalarCrackerM4().Crack(ciphertext, fixedParts, scope);

        var sw = Stopwatch.StartNew();
        long keysTried = 0;

        // ── Scope planning ──
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

        // Greek tables + reflector fixed per call
        var greekFwd = GreekFwdInt[fixedParts.WG];
        var greekRev = GreekRevInt[fixedParts.WG];
        var thinRefl = (fixedParts.ThinReflector == RotorData.UkwCThin) ? UkwCThinInt : UkwBThinInt;

        // Int ciphertext + plugboard for vector broadcast
        var ctInt = new int[ciphertext.Length];
        for (int i = 0; i < ciphertext.Length; i++) ctInt[i] = ciphertext[i];
        var plugboardInt = new int[26];
        for (int i = 0; i < 26; i++) plugboardInt[i] = fixedParts.Plugboard[i];

        var best = new BestHolder();

        bool useAvx2 = SimdCaps.HasAvx2;

        Parallel.ForEach(units,
            () => new TLS(ctInt.Length),
            (unit, _, local) =>
            {
                if (useAvx2)
                    RunUnit(ctInt, plugboardInt,
                            unit.L, unit.M, unit.R,
                            unit.rl, unit.rm, unit.rr,
                            unit.pg, fixedParts.RG,
                            greekFwd, greekRev, thinRefl,
                            local);
                else
                    RunUnitNeon(ctInt, plugboardInt,
                                unit.L, unit.M, unit.R,
                                unit.rl, unit.rm, unit.rr,
                                unit.pg, fixedParts.RG,
                                greekFwd, greekRev, thinRefl,
                                local);
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

    static unsafe void RunUnit(
        int[] ct, int[] plugboardInt,
        int WL, int WM, int WR,
        int RL, int RM, int RR,
        int PG, int RG,
        int[] greekFwd, int[] greekRev, int[] thinRefl,
        TLS local)
    {
        var FwdWR = FwdInt[WR]; var FwdWM = FwdInt[WM]; var FwdWL = FwdInt[WL];
        var RevWR = RevInt[WR]; var RevWM = RevInt[WM]; var RevWL = RevInt[WL];
        int NotchR = RotorData.NotchPos[WR];
        int NotchM = RotorData.NotchPos[WM];

        var v_Rr_inv = Vector256.Create(26 - RR);
        var v_Rm_inv = Vector256.Create(26 - RM);
        var v_Rl_inv = Vector256.Create(26 - RL);
        var v_Rg_inv = Vector256.Create(26 - RG);
        var v_Rr     = Vector256.Create(RR);
        var v_Rm     = Vector256.Create(RM);
        var v_Rl     = Vector256.Create(RL);
        var v_Rg     = Vector256.Create(RG);
        var v_Pg     = Vector256.Create(PG);     // greek pos: static, broadcast per unit
        var v_26     = Vector256.Create(26);
        var v_1      = Vector256.Create(1);
        var v_notchR = Vector256.Create(NotchR);
        var v_notchM = Vector256.Create(NotchM);

        int ctLen = ct.Length;

        Span<int> tmp = stackalloc int[8];

        fixed (int* pFwdR = FwdWR)
        fixed (int* pFwdM = FwdWM)
        fixed (int* pFwdL = FwdWL)
        fixed (int* pRevR = RevWR)
        fixed (int* pRevM = RevWM)
        fixed (int* pRevL = RevWL)
        fixed (int* pFwdG = greekFwd)
        fixed (int* pRevG = greekRev)
        fixed (int* pUkw  = thinRefl)
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

                    for (int i = 0; i < ctLen; i++)
                    {
                        // Step rotors (greek wheel doesn't move)
                        var midMask   = Vector256.Equals(PM_vec, v_notchM);
                        var rightMask = Vector256.Equals(PR_vec, v_notchR);
                        var midAdv    = midMask | rightMask;
                        var leftAdv   = midMask;

                        PL_vec = PL_vec + (leftAdv & v_1);
                        PM_vec = PM_vec + (midAdv  & v_1);
                        PR_vec = PR_vec + v_1;
                        PL_vec = Mod26(PL_vec, v_26);
                        PM_vec = Mod26(PM_vec, v_26);
                        PR_vec = Mod26(PR_vec, v_26);

                        var c_vec = Vector256.Create(pCt[i]);

                        c_vec = Avx2.GatherVector256(pPB, c_vec, 4);

                        c_vec = ForwardStep(c_vec, PR_vec, v_Rr_inv, v_Rr, pFwdR, v_26);
                        c_vec = ForwardStep(c_vec, PM_vec, v_Rm_inv, v_Rm, pFwdM, v_26);
                        c_vec = ForwardStep(c_vec, PL_vec, v_Rl_inv, v_Rl, pFwdL, v_26);
                        // ── M4 extra: static greek rotor forward ──
                        c_vec = ForwardStep(c_vec, v_Pg, v_Rg_inv, v_Rg, pFwdG, v_26);

                        // Thin reflector
                        c_vec = Avx2.GatherVector256(pUkw, c_vec, 4);

                        // ── M4 extra: static greek rotor reverse ──
                        c_vec = ForwardStep(c_vec, v_Pg, v_Rg_inv, v_Rg, pRevG, v_26);
                        c_vec = ForwardStep(c_vec, PL_vec, v_Rl_inv, v_Rl, pRevL, v_26);
                        c_vec = ForwardStep(c_vec, PM_vec, v_Rm_inv, v_Rm, pRevM, v_26);
                        c_vec = ForwardStep(c_vec, PR_vec, v_Rr_inv, v_Rr, pRevR, v_26);

                        c_vec = Avx2.GatherVector256(pPB, c_vec, 4);

                        c_vec.CopyTo(tmp);
                        for (int lane = 0; lane < 8; lane++)
                            local.outBuf[lane][i] = (byte)tmp[lane];
                    }

                    for (int lane = 0; lane < 8; lane++)
                    {
                        int ic = IcScorer.ScoreInt(local.outBuf[lane]);
                        local.keysTried++;

                        if (ic > local.bestIc)
                        {
                            local.bestIc = ic;
                            local.bestL = WL; local.bestM = WM; local.bestR = WR;
                            local.bestPL = pl; local.bestPM = pm; local.bestPR = prBase + lane;
                            local.bestPG = PG;
                            local.bestRR = RR; local.bestRM = RM; local.bestRL = RL;
                        }
                    }
                }

                // Scalar tail for pr=24, pr=25
                for (int pr = 24; pr < 26; pr++)
                {
                    var m = new EnigmaM4
                    {
                        WL = WL, WM = WM, WR = WR,
                        RL = RL, RM = RM, RR = RR,
                        PL = pl, PM = pm, PR = pr,
                        WG = 0, RG = RG, PG = PG,   // WG choice bound by greekFwd; use 0, Plugboard is used but greekFwd/Rev are preselected
                        Plugboard = new byte[26],
                        ThinReflector = new byte[26],
                    };
                    // Rebuild the scalar helper from pointers for tail so we
                    // don't need to reconstruct EnigmaM4's greek tables; run
                    // the sequence inline:
                    for (int k = 0; k < 26; k++) m.Plugboard[k] = (byte)plugboardInt[k];
                    for (int k = 0; k < 26; k++) m.ThinReflector[k] = (byte)thinRefl[k];

                    // EnigmaM4 uses RotorData.GreekFwd[WG]/GreekRev[WG]; the
                    // tables we were passed (greekFwd/greekRev) already fix
                    // the wheel choice through the WG match in the caller,
                    // so as long as fixedParts.WG matched, m.WG=0 or 1 gives
                    // the right table. We use fixedParts.WG when available.
                    // For simplicity we encrypt manually with our pointers.
                    int PLp = pl, PMp = pm, PRp = pr;
                    for (int i = 0; i < ctLen; i++)
                    {
                        bool midOnNotch   = PMp == NotchM;
                        bool rightOnNotch = PRp == NotchR;
                        if (midOnNotch)   { PLp = (PLp + 1) % 26; PMp = (PMp + 1) % 26; }
                        else if (rightOnNotch) { PMp = (PMp + 1) % 26; }
                        PRp = (PRp + 1) % 26;

                        int c = plugboardInt[ct[i]];

                        c = Fwd(c, PRp, RR, FwdWR);
                        c = Fwd(c, PMp, RM, FwdWM);
                        c = Fwd(c, PLp, RL, FwdWL);
                        c = Fwd(c, PG,  RG, greekFwd);

                        c = thinRefl[c];

                        c = Fwd(c, PG,  RG, greekRev);
                        c = Fwd(c, PLp, RL, RevWL);
                        c = Fwd(c, PMp, RM, RevWM);
                        c = Fwd(c, PRp, RR, RevWR);

                        c = plugboardInt[c];

                        local.outBuf[0][i] = (byte)c;
                    }

                    int ic = IcScorer.ScoreInt(local.outBuf[0]);
                    local.keysTried++;

                    if (ic > local.bestIc)
                    {
                        local.bestIc = ic;
                        local.bestL = WL; local.bestM = WM; local.bestR = WR;
                        local.bestPL = pl; local.bestPM = pm; local.bestPR = pr;
                        local.bestPG = PG;
                        local.bestRR = RR; local.bestRM = RM; local.bestRL = RL;
                    }
                }
            }
        }
    }

    static int Fwd(int c, int pos, int ring, int[] table)
    {
        int s = (c + pos - ring + 26) % 26;
        int r = table[s];
        return (r - pos + ring + 26) % 26;
    }

    static unsafe Vector256<int> ForwardStep(
        Vector256<int> c, Vector256<int> pos,
        Vector256<int> ringInv, Vector256<int> ring,
        int* rotorTable, Vector256<int> v_26)
    {
        var shift = c + pos + ringInv;
        shift = Mod26(shift, v_26);
        shift = Mod26(shift, v_26);

        var looked = Avx2.GatherVector256(rotorTable, shift, 4);

        var result = looked + ring - pos + v_26;
        result = Mod26(result, v_26);
        result = Mod26(result, v_26);
        return result;
    }

    static Vector256<int> Mod26(Vector256<int> v, Vector256<int> v_26)
    {
        var mask = Vector256.GreaterThanOrEqual(v, v_26);
        return v - (mask & v_26);
    }

    // ── ARM64 NEON path — mirrors RunUnit but Vector128 (4 lanes) +
    // software gather. Same Parallel.ForEach outer so multi-core scaling
    // stays. Expected ~2-3× vs ParallelScalar on Apple Silicon.
    static unsafe void RunUnitNeon(
        int[] ct, int[] plugboardInt,
        int WL, int WM, int WR,
        int RL, int RM, int RR,
        int PG, int RG,
        int[] greekFwd, int[] greekRev, int[] thinRefl,
        TLS local)
    {
        var FwdWR = FwdInt[WR]; var FwdWM = FwdInt[WM]; var FwdWL = FwdInt[WL];
        var RevWR = RevInt[WR]; var RevWM = RevInt[WM]; var RevWL = RevInt[WL];
        int NotchR = RotorData.NotchPos[WR];
        int NotchM = RotorData.NotchPos[WM];

        var v_Rr_inv = Vector128.Create(26 - RR);
        var v_Rm_inv = Vector128.Create(26 - RM);
        var v_Rl_inv = Vector128.Create(26 - RL);
        var v_Rg_inv = Vector128.Create(26 - RG);
        var v_Rr     = Vector128.Create(RR);
        var v_Rm     = Vector128.Create(RM);
        var v_Rl     = Vector128.Create(RL);
        var v_Rg     = Vector128.Create(RG);
        var v_Pg     = Vector128.Create(PG);
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
        fixed (int* pFwdG = greekFwd)
        fixed (int* pRevG = greekRev)
        fixed (int* pUkw  = thinRefl)
        fixed (int* pPB   = plugboardInt)
        fixed (int* pCt   = ct)
        {
            for (int pl = 0; pl < 26; pl++)
            for (int pm = 0; pm < 26; pm++)
            {
                // 6 batches of 4 (0-3, 4-7, ..., 20-23) + scalar tail pr=24,25
                for (int prBase = 0; prBase <= 20; prBase += 4)
                {
                    var PR_vec = Vector128.Create(prBase, prBase+1, prBase+2, prBase+3);
                    var PL_vec = Vector128.Create(pl);
                    var PM_vec = Vector128.Create(pm);

                    for (int i = 0; i < ctLen; i++)
                    {
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
                        c_vec = ForwardStep128(c_vec, v_Pg,   v_Rg_inv, v_Rg, pFwdG, v_26);
                        c_vec = GatherSw(pUkw, c_vec);
                        c_vec = ForwardStep128(c_vec, v_Pg,   v_Rg_inv, v_Rg, pRevG, v_26);
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
                            local.bestPG = PG;
                            local.bestRR = RR; local.bestRM = RM; local.bestRL = RL;
                        }
                    }
                }

                // Scalar tail pr=24, 25 — borrow the AVX2 path's scalar
                // encryption logic verbatim (same `Fwd` helper)
                for (int pr = 24; pr < 26; pr++)
                {
                    int PLp = pl, PMp = pm, PRp = pr;
                    for (int i = 0; i < ctLen; i++)
                    {
                        bool midOnNotch   = PMp == NotchM;
                        bool rightOnNotch = PRp == NotchR;
                        if (midOnNotch)   { PLp = (PLp + 1) % 26; PMp = (PMp + 1) % 26; }
                        else if (rightOnNotch) { PMp = (PMp + 1) % 26; }
                        PRp = (PRp + 1) % 26;

                        int c = plugboardInt[ct[i]];
                        c = Fwd(c, PRp, RR, FwdWR);
                        c = Fwd(c, PMp, RM, FwdWM);
                        c = Fwd(c, PLp, RL, FwdWL);
                        c = Fwd(c, PG,  RG, greekFwd);
                        c = thinRefl[c];
                        c = Fwd(c, PG,  RG, greekRev);
                        c = Fwd(c, PLp, RL, RevWL);
                        c = Fwd(c, PMp, RM, RevWM);
                        c = Fwd(c, PRp, RR, RevWR);
                        c = plugboardInt[c];
                        local.outBuf[0][i] = (byte)c;
                    }

                    int ic = IcScorer.ScoreInt(local.outBuf[0]);
                    local.keysTried++;
                    if (ic > local.bestIc)
                    {
                        local.bestIc = ic;
                        local.bestL = WL; local.bestM = WM; local.bestR = WR;
                        local.bestPL = pl; local.bestPM = pm; local.bestPR = pr;
                        local.bestPG = PG;
                        local.bestRR = RR; local.bestRM = RM; local.bestRL = RL;
                    }
                }
            }
        }
    }

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
        public int Ic, L, M, R, PL, PM, PR, PG, RR, RM, RL;
    }

    class TLS
    {
        public long keysTried;
        public int bestIc, bestL, bestM, bestR, bestPL, bestPM, bestPR, bestPG;
        public int bestRR, bestRM, bestRL;
        public byte[][] outBuf;

        public TLS(int ctLen)
        {
            outBuf = new byte[8][];
            for (int i = 0; i < 8; i++) outBuf[i] = new byte[ctLen];
        }
    }
}
