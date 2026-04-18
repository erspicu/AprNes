namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using SkiaSharp;
using EnigmaBenchmark.Core;

/// <summary>
/// M4 SkSL cracker — same pattern as GpuCracker (M3) but ships the M4
/// rotor table (16 rows: 5 Fwd + 5 Rev + 2 GreekFwd + 2 GreekRev + 2 Thin)
/// and uses enigma_m4_crack.sksl which has an extra static greek pass.
/// </summary>
public sealed class GpuCrackerM4 : ICrackerM4
{
    readonly GRContext? _grContext;

    public GpuCrackerM4(GRContext? grContext = null) { _grContext = grContext; }

    public string Name => _grContext != null
        ? $"SkSL M4 ({_grContext.Backend} GPU, 80-char IC)"
        : "SkSL M4 (Skia Raster Pipeline, CPU-side, 80-char IC)";

    static readonly string ShaderSrc = LoadShader();

    static string LoadShader()
    {
        var exeDir = AppContext.BaseDirectory;
        var path = Path.Combine(exeDir, "Shaders", "enigma_m4_crack.sksl");
        return File.ReadAllText(path);
    }

    public CrackResult Crack(byte[] ciphertext, EnigmaM4 fixedParts, CrackScope scope)
    {
        var sw = Stopwatch.StartNew();

        using var effect = SKRuntimeEffect.CreateShader(ShaderSrc, out var err);
        if (effect == null)
            throw new InvalidOperationException($"M4 SkSL compile failed: {err}");

        // ── Rotor table bitmap 26 × 16 ──
        // rows 0..4   Fwd I..V
        // rows 5..9   Rev I..V
        // rows 10..11 GreekFwd Beta, Gamma
        // rows 12..13 GreekRev Beta, Gamma
        // rows 14..15 UKW-B_thin, UKW-C_thin
        using var rotorsBmp = new SKBitmap(
            new SKImageInfo(26, 16, SKColorType.Bgra8888, SKAlphaType.Opaque));
        FillRotors(rotorsBmp);

        using var plugboardBmp = new SKBitmap(
            new SKImageInfo(26, 1, SKColorType.Bgra8888, SKAlphaType.Opaque));
        FillByteRow(plugboardBmp, fixedParts.Plugboard);

        using var cipherBmp = new SKBitmap(
            new SKImageInfo(ciphertext.Length, 1, SKColorType.Bgra8888, SKAlphaType.Opaque));
        FillByteRow(cipherBmp, ciphertext);

        using var rotorsShader = SKShader.CreateBitmap(
            rotorsBmp, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
        using var plugShader = SKShader.CreateBitmap(
            plugboardBmp, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
        using var cipherShader = SKShader.CreateBitmap(
            cipherBmp, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);

        const int TileW = 676;
        const int TileH = 26;
        var surfInfo = new SKImageInfo(TileW, TileH, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var surface = _grContext != null
            ? SKSurface.Create(_grContext, false, surfInfo)
            : SKSurface.Create(surfInfo);
        var pixels = new uint[TileW * TileH];

        int pgMin = fixedParts.PG, pgMax = fixedParts.PG + 1;
        int rrMin = fixedParts.RR, rrMax = fixedParts.RR + 1;
        int rmMin = fixedParts.RM, rmMax = fixedParts.RM + 1;
        int rlMin = fixedParts.RL, rlMax = fixedParts.RL + 1;
        if (scope >= CrackScope.Normal)  { pgMin = 0; pgMax = 26; }
        if (scope >= CrackScope.Hard)    { rrMin = 0; rrMax = 26; }
        if (scope >= CrackScope.Extreme) { rmMin = 0; rmMax = 26; }

        // ── Top-K + CPU re-rank ────────────────────────────────────────
        // Shader can only score the first ~80 chars of ciphertext (SkSL
        // program-size limit). At 80 chars × 1M keys, IC noise can lift a
        // wrong key above the true key. So we keep the top-K candidates by
        // 80-char IC and re-rank them on the FULL ciphertext using CPU —
        // full-text IC is ~1/sqrt(3) tighter and easily separates truth
        // from noise.  Only top-K survive to CPU; everything else is GPU-
        // only and stays fast.
        const int TopK = 256;
        var topQ = new PriorityQueue<Candidate, int>();
        long keysTried = 0;

        float thinRIdx = fixedParts.ThinReflector == RotorData.UkwCThin ? 1f : 0f;

        foreach (var (L, M, R) in RotorData.AllWheelOrders())
        for (int pg = pgMin; pg < pgMax; pg++)
        for (int rl = rlMin; rl < rlMax; rl++)
        for (int rm = rmMin; rm < rmMax; rm++)
        for (int rr = rrMin; rr < rrMax; rr++)
        {
            var uniforms = new SKRuntimeEffectUniforms(effect)
            {
                ["uCtLen"]  = (float)ciphertext.Length,
                ["uWL"]     = (float)L,
                ["uWM"]     = (float)M,
                ["uWR"]     = (float)R,
                ["uWG"]     = (float)fixedParts.WG,
                ["uThinR"]  = thinRIdx,
                ["uRL"]     = (float)rl,
                ["uRM"]     = (float)rm,
                ["uRR"]     = (float)rr,
                ["uRG"]     = (float)fixedParts.RG,
                ["uPG"]     = (float)pg,
                ["uNotchL"] = (float)RotorData.NotchPos[L],
                ["uNotchM"] = (float)RotorData.NotchPos[M],
                ["uNotchR"] = (float)RotorData.NotchPos[R],
            };

            var children = new SKRuntimeEffectChildren(effect)
            {
                ["uRotors"]    = rotorsShader,
                ["uPlugboard"] = plugShader,
                ["uCipher"]    = cipherShader,
            };

            using var runtimeShader = effect.ToShader(uniforms, children);
            using var paint = new SKPaint { Shader = runtimeShader, IsAntialias = false };

            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Black);
            canvas.DrawRect(0, 0, TileW, TileH, paint);
            canvas.Flush();

            unsafe
            {
                fixed (uint* p = pixels)
                {
                    var read = surface.ReadPixels(surfInfo, (IntPtr)p, TileW * 4, 0, 0);
                    if (!read) throw new InvalidOperationException("ReadPixels failed");
                }
            }

            for (int py = 0; py < TileH; py++)
            for (int px = 0; px < TileW; px++)
            {
                uint bgra = pixels[py * TileW + px];
                int rByte = (int)((bgra >> 16) & 0xFF);
                int gByte = (int)((bgra >>  8) & 0xFF);
                int bByte = (int)( bgra        & 0xFF);
                int ic = (rByte << 16) | (gByte << 8) | bByte;
                keysTried++;

                // Maintain a min-heap of top-K candidates. Only keep entries
                // that could plausibly be the true key (below 5000 ≈ 0.05
                // IC is noise-floor territory even on short text).
                if (ic < 3000) continue;
                if (topQ.Count < TopK)
                {
                    topQ.Enqueue(new Candidate(
                        L, M, R,
                        py, px / 26, px - (px / 26) * 26,
                        pg, rr, rm, rl,
                        ic), ic);
                }
                else if (topQ.TryPeek(out _, out int minIc) && ic > minIc)
                {
                    topQ.Dequeue();
                    topQ.Enqueue(new Candidate(
                        L, M, R,
                        py, px / 26, px - (px / 26) * 26,
                        pg, rr, rm, rl,
                        ic), ic);
                }
            }
        }

        // ── CPU re-rank on full ciphertext ──
        var cand = new List<Candidate>(topQ.Count);
        while (topQ.Count > 0) cand.Add(topQ.Dequeue());

        int bestIc = 0;
        var verifier = new EnigmaM4
        {
            WG = fixedParts.WG,
            RG = fixedParts.RG,
            Plugboard = fixedParts.Plugboard,
            ThinReflector = fixedParts.ThinReflector,
        };
        Candidate bestC = cand.Count > 0 ? cand[0] : default;

        foreach (var c in cand)
        {
            verifier.WL = c.L; verifier.WM = c.M; verifier.WR = c.R;
            verifier.PG = c.PG;
            verifier.RL = c.RL; verifier.RM = c.RM; verifier.RR = c.RR;
            byte[] decrypted = verifier.TransformFresh(ciphertext, c.PL, c.PM, c.PR);
            int fullIc = IcScorer.ScoreInt(decrypted);
            if (fullIc > bestIc)
            {
                bestIc = fullIc;
                bestC = c;
            }
        }

        sw.Stop();

        return new CrackResult
        {
            Found = bestIc >= IcScorer.GermanThresholdInt,
            KeysTried = keysTried,
            ElapsedSeconds = sw.Elapsed.TotalSeconds,
            L = bestC.L, M = bestC.M, R = bestC.R,
            PL = bestC.PL, PM = bestC.PM, PR = bestC.PR,
            RR_Ring = bestC.RR, RM_Ring = bestC.RM, RL_Ring = bestC.RL,
            BestIc = bestIc,
            WG = fixedParts.WG,
            PG = bestC.PG,
        };
    }

    readonly record struct Candidate(
        int L, int M, int R,
        int PL, int PM, int PR,
        int PG, int RR, int RM, int RL,
        int GpuIc);

    static unsafe void FillRotors(SKBitmap bmp)
    {
        IntPtr pp = bmp.GetPixels();
        uint* p = (uint*)pp;
        // rows 0..4 Fwd, 5..9 Rev
        for (int r = 0; r < 5; r++)
        for (int i = 0; i < 26; i++)
        {
            p[r * 26 + i]       = EncodeByte(RotorData.Fwd[r][i]);
            p[(r + 5) * 26 + i] = EncodeByte(RotorData.Rev[r][i]);
        }
        // rows 10..11 GreekFwd, 12..13 GreekRev
        for (int g = 0; g < 2; g++)
        for (int i = 0; i < 26; i++)
        {
            p[(10 + g) * 26 + i] = EncodeByte(RotorData.GreekFwd[g][i]);
            p[(12 + g) * 26 + i] = EncodeByte(RotorData.GreekRev[g][i]);
        }
        // rows 14, 15: thin reflectors
        for (int i = 0; i < 26; i++)
        {
            p[14 * 26 + i] = EncodeByte(RotorData.UkwBThin[i]);
            p[15 * 26 + i] = EncodeByte(RotorData.UkwCThin[i]);
        }
        bmp.NotifyPixelsChanged();
    }

    static unsafe void FillByteRow(SKBitmap bmp, byte[] row)
    {
        IntPtr pp = bmp.GetPixels();
        uint* p = (uint*)pp;
        for (int i = 0; i < row.Length; i++) p[i] = EncodeByte(row[i]);
        bmp.NotifyPixelsChanged();
    }

    static uint EncodeByte(byte v) => 0xFF000000u | ((uint)v << 16);
}
