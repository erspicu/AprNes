namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using SkiaSharp;
using EnigmaBenchmark.Core;

/// <summary>
/// SkSL cracker: the Enigma trial decryption + IC scoring runs inside a
/// SkRuntimeEffect. One output pixel = one (PL, PM, PR) start-position combo
/// for a fixed (wheel-order, ring-setting) configured via uniforms. The CPU
/// side dispatches one 676×26 render pass per wheel-order × ring-setting.
///
/// This mirrors the SkSL backend AprNes uses for its CRT shader — a fragment
/// effect executed by Skia. In AprNes, GRContext makes that path GPU-backed;
/// here (a console app with no windowing) Skia falls back to the raster
/// pipeline, so numbers reported are CPU-side SkSL execution time. The point
/// of the benchmark is to let users see what the three backends cost on the
/// *same* workload, not to claim this is the fastest way to crack Enigma.
/// </summary>
public sealed class GpuCracker : ICracker
{
    // Optional GPU context. When null (console app path) Skia falls back to
    // the Raster Pipeline — still SkSL, but executed on CPU. When supplied
    // (Avalonia path, via ISkiaSharpApiLease.GrContext), SKSurface is GPU-
    // backed and the shader runs on D3D11/GL hardware.
    readonly GRContext? _grContext;

    public GpuCracker(GRContext? grContext = null) { _grContext = grContext; }

    public string Name => _grContext != null
        ? $"SkSL ({_grContext.Backend} GPU, 96-char IC)"
        : "SkSL GPU (96-char IC)";

    static readonly string ShaderSrc = LoadShader();

    static string LoadShader()
    {
        var exeDir = AppContext.BaseDirectory;
        var path = Path.Combine(exeDir, "Shaders", "enigma_crack.sksl");
        return File.ReadAllText(path);
    }

    public CrackResult Crack(byte[] ciphertext, EnigmaM3 fixedParts, CrackScope scope)
    {
        var sw = Stopwatch.StartNew();

        using var effect = SKRuntimeEffect.CreateShader(ShaderSrc, out var err);
        if (effect == null)
            throw new InvalidOperationException($"SkSL compile failed: {err}");

        // ── Build rotor table bitmap: 26 × 11 RGBA8 ──
        // rows 0..4 = Fwd I..V,  rows 5..9 = Rev I..V,  row 10 = UKW-B
        using var rotorsBmp = new SKBitmap(
            new SKImageInfo(26, 11, SKColorType.Bgra8888, SKAlphaType.Opaque));
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

        // ── Output surface: 676 × 26 RGBA8 per render pass (17,576 keys) ──
        const int TileW = 676;
        const int TileH = 26;
        var surfInfo = new SKImageInfo(TileW, TileH, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var surface = _grContext != null
            ? SKSurface.Create(_grContext, false, surfInfo)   // GPU-backed
            : SKSurface.Create(surfInfo);                      // raster fallback
        var pixels = new uint[TileW * TileH];

        // ── Scope → ring-range planning (matches ScalarCracker) ──
        int rrMin = 0, rrMax = 26;
        int rmMin = fixedParts.RM, rmMax = fixedParts.RM + 1;
        int rlMin = fixedParts.RL, rlMax = fixedParts.RL + 1;
        if (scope < CrackScope.Normal)   { rrMin = fixedParts.RR; rrMax = fixedParts.RR + 1; }
        if (scope >= CrackScope.Hard)    { rmMin = 0; rmMax = 26; }
        if (scope >= CrackScope.Extreme) { rlMin = 0; rlMax = 26; }

        int bestIc = 0;
        int bestL = 0, bestM = 0, bestR = 0, bestPL = 0, bestPM = 0, bestPR = 0;
        int bestRR = fixedParts.RR, bestRM = fixedParts.RM, bestRL = fixedParts.RL;
        long keysTried = 0;
        int passCount = 0;
        double firstPassSec = 0;

        Console.WriteLine($"[GpuCracker] Surface backend: {(surface.Context != null ? "GPU" : "raster")}"
                        + $"  (GrContext arg was {(_grContext != null ? "non-null" : "null")})");

        foreach (var (L, M, R) in RotorData.AllWheelOrders())
        for (int rl = rlMin; rl < rlMax; rl++)
        for (int rm = rmMin; rm < rmMax; rm++)
        for (int rr = rrMin; rr < rrMax; rr++)
        {
            var passSw = Stopwatch.StartNew();
            var uniforms = new SKRuntimeEffectUniforms(effect)
            {
                ["uCtLen"]  = (float)ciphertext.Length,
                ["uWL"]     = (float)L,
                ["uWM"]     = (float)M,
                ["uWR"]     = (float)R,
                ["uRL"]     = (float)rl,
                ["uRM"]     = (float)rm,
                ["uRR"]     = (float)rr,
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

            // Readback & scan tile for max IC
            unsafe
            {
                fixed (uint* p = pixels)
                {
                    var read = surface.ReadPixels(
                        surfInfo, (IntPtr)p, TileW * 4, 0, 0);
                    if (!read) throw new InvalidOperationException("ReadPixels failed");
                }
            }

            for (int py = 0; py < TileH; py++)
            for (int px = 0; px < TileW; px++)
            {
                uint bgra = pixels[py * TileW + px];
                // Bgra8888: bits [0..7]=B, [8..15]=G, [16..23]=R, [24..31]=A.
                // Shader wrote R=high, G=mid, B=low byte of icInt.
                int rByte = (int)((bgra >> 16) & 0xFF);
                int gByte = (int)((bgra >>  8) & 0xFF);
                int bByte = (int)( bgra        & 0xFF);
                int ic = (rByte << 16) | (gByte << 8) | bByte;

                if (ic > bestIc)
                {
                    bestIc = ic;
                    bestL = L; bestM = M; bestR = R;
                    bestPM = px / 26;
                    bestPR = px - bestPM * 26;
                    bestPL = py;
                    bestRR = rr; bestRM = rm; bestRL = rl;
                }
                keysTried++;
            }

            passSw.Stop();
            if (passCount == 0) firstPassSec = passSw.Elapsed.TotalSeconds;
            passCount++;
            if (passCount <= 3 || passCount % 50 == 0)
                Console.WriteLine($"[GpuCracker] pass {passCount,4}  {passSw.Elapsed.TotalMilliseconds:F1}ms");
        }

        Console.WriteLine($"[GpuCracker] total passes={passCount}, first pass={firstPassSec*1000:F1}ms");

        sw.Stop();

        return new CrackResult
        {
            Found = bestIc >= IcScorer.GermanThresholdInt,
            KeysTried = keysTried,
            ElapsedSeconds = sw.Elapsed.TotalSeconds,
            L = bestL, M = bestM, R = bestR,
            PL = bestPL, PM = bestPM, PR = bestPR,
            RR_Ring = bestRR, RM_Ring = bestRM, RL_Ring = bestRL,
            BestIc = bestIc,
        };
    }

    // ── Bitmap builders ──
    // Shader reads R channel, so we encode byte value into the R lane of BGRA.

    static unsafe void FillRotors(SKBitmap bmp)
    {
        IntPtr pp = bmp.GetPixels();
        uint* p = (uint*)pp;
        for (int rotor = 0; rotor < 5; rotor++)
        {
            for (int i = 0; i < 26; i++)
            {
                p[rotor * 26 + i] = EncodeByte(RotorData.Fwd[rotor][i]);
                p[(rotor + 5) * 26 + i] = EncodeByte(RotorData.Rev[rotor][i]);
            }
        }
        for (int i = 0; i < 26; i++)
            p[10 * 26 + i] = EncodeByte(RotorData.UkwB[i]);
        bmp.NotifyPixelsChanged();
    }

    static unsafe void FillByteRow(SKBitmap bmp, byte[] row)
    {
        IntPtr pp = bmp.GetPixels();
        uint* p = (uint*)pp;
        for (int i = 0; i < row.Length; i++) p[i] = EncodeByte(row[i]);
        bmp.NotifyPixelsChanged();
    }

    // Bgra8888 with value in R channel (bits 16-23), full-alpha elsewhere.
    static uint EncodeByte(byte v) => 0xFF000000u | ((uint)v << 16);
}
