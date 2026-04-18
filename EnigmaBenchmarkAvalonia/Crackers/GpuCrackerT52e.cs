namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using SkiaSharp;
using EnigmaBenchmark.Core;

/// <summary>
/// SkSL GPU cracker for T52e. 73 render passes (one per s9); each pass is a
/// 4623 × 71 tile covering (s6, s7, s8) starts. Shader computes the truth-
/// signal "decrypted byte is SPACE or E" count, which is 10σ above noise
/// for German plaintext. CPU re-ranks top-K with full Baudot IC.
///
/// Shader is much larger than the Lorenz shader because T52e has a
/// non-trivial H/SR XOR network, a 32×5 permutation table, and 10 coupled
/// M-magnet stepping equations — each translated to branch-free SkSL.
/// </summary>
public sealed class GpuCrackerT52e : ICrackerT52e
{
    readonly GRContext? _grContext;

    public GpuCrackerT52e(GRContext? grContext = null) { _grContext = grContext; }

    public string Name => _grContext != null
        ? $"SkSL T52e ({_grContext.Backend} GPU, 80-char space+E score)"
        : "SkSL T52e (Skia Raster Pipeline, CPU-side)";

    static readonly string ShaderTemplate = LoadShader();

    static string LoadShader()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Shaders", "t52e_crack.sksl");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Substitute the 10 switch-map placeholders (@@SM0@@..@@SM9@@) with their
    /// literal values. This lets us avoid dynamic array indexing — SkSL
    /// runtime effects only allow constant indices into local arrays.
    /// </summary>
    static string SpecializeShader(int[] switchMap)
    {
        var src = ShaderTemplate;
        for (int i = 0; i < 10; i++)
            src = src.Replace($"@@SM{i}@@", switchMap[i].ToString());
        return src;
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

        var shaderSrc = SpecializeShader(switchMap);
        using var effect = SKRuntimeEffect.CreateShader(shaderSrc, out var err);
        if (effect == null)
            throw new InvalidOperationException($"T52e SkSL compile failed: {err}");

        // ── Pin pattern bitmap: (widest=73) × 10 rows ──
        const int PinWidth = 73;
        using var pinsBmp = new SKBitmap(
            new SKImageInfo(PinWidth, 10, SKColorType.Bgra8888, SKAlphaType.Opaque));
        FillPins(pinsBmp, pins);

        using var cipherBmp = new SKBitmap(
            new SKImageInfo(ciphertext.Length, 1, SKColorType.Bgra8888, SKAlphaType.Opaque));
        FillByteRow(cipherBmp, ciphertext);

        using var permBmp = new SKBitmap(
            new SKImageInfo(32, 5, SKColorType.Bgra8888, SKAlphaType.Opaque));
        FillPermInv(permBmp);

        using var pinsShader = SKShader.CreateBitmap(pinsBmp, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
        using var cipherShader = SKShader.CreateBitmap(cipherBmp, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
        using var permShader = SKShader.CreateBitmap(permBmp, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);

        // Tile dims: x=(s6 + 67*s7) → width 67*69=4623, y=s8 → 71
        const int TileW = 67 * 69;
        const int TileH = 71;
        var surfInfo = new SKImageInfo(TileW, TileH, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var surface = _grContext != null
            ? SKSurface.Create(_grContext, false, surfInfo)
            : SKSurface.Create(surfInfo);

        var pixels = new uint[TileW * TileH];

        // Top-K min-heap
        const int TopK = 1024;
        const int IcFloor = 12000;
        var topQ = new PriorityQueue<Candidate, int>();

        long keysTried = 0;

        int c9 = T52eMachine.PinCounts[9];  // 73

        for (int s9 = 0; s9 < c9; s9++)
        {
            var uniforms = new SKRuntimeEffectUniforms(effect)
            {
                ["uCtLen"] = (float)ciphertext.Length,
                ["uS9"]    = (float)s9,
                ["uS0"]    = (float)knownStart[0],
                ["uS1"]    = (float)knownStart[1],
                ["uS2"]    = (float)knownStart[2],
                ["uS3"]    = (float)knownStart[3],
                ["uS4"]    = (float)knownStart[4],
                ["uS5"]    = (float)knownStart[5],
            };
            var children = new SKRuntimeEffectChildren(effect)
            {
                ["uPins"]    = pinsShader,
                ["uCipher"]  = cipherShader,
                ["uPermInv"] = permShader,
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
                    var ok = surface.ReadPixels(surfInfo, (IntPtr)p, TileW * 4, 0, 0);
                    if (!ok) throw new InvalidOperationException("ReadPixels failed");
                }
            }

            for (int y = 0; y < TileH; y++)
            {
                int s8 = y;
                for (int x = 0; x < TileW; x++)
                {
                    int s7 = x / 67;
                    if (s7 >= 69) continue;
                    int s6 = x - s7 * 67;

                    uint bgra = pixels[y * TileW + x];
                    int ic = (int)((bgra >> 16 & 0xFF) << 16)
                           | (int)((bgra >>  8 & 0xFF) <<  8)
                           | (int)( bgra       & 0xFF);
                    keysTried++;

                    if (ic < IcFloor) continue;

                    if (topQ.Count < TopK)
                    {
                        topQ.Enqueue(new Candidate(s6, s7, s8, s9, ic), ic);
                    }
                    else if (topQ.TryPeek(out _, out int minIc) && ic > minIc)
                    {
                        topQ.Dequeue();
                        topQ.Enqueue(new Candidate(s6, s7, s8, s9, ic), ic);
                    }
                }
            }
        }

        // CPU re-rank on full ciphertext
        var cand = new List<Candidate>(topQ.Count);
        while (topQ.Count > 0) cand.Add(topQ.Dequeue());

        int bestIc = 0;
        var bestStart = (int[])knownStart.Clone();
        var verifier = T52eMachine.Create(pins, switchMap, knownStart, ktf: false);
        var trial = new int[10];
        Array.Copy(knownStart, trial, 10);
        var decrypted = new byte[ciphertext.Length];

        foreach (var c in cand)
        {
            trial[6] = c.S6; trial[7] = c.S7; trial[8] = c.S8; trial[9] = c.S9;
            verifier.SetStart(trial);
            for (int i = 0; i < ciphertext.Length; i++) decrypted[i] = verifier.Decrypt(ciphertext[i]);
            int fullIc = IcScorer.ScoreBaudotInt(decrypted);
            if (fullIc > bestIc)
            {
                bestIc = fullIc;
                bestStart = (int[])trial.Clone();
            }
        }

        sw.Stop();

        return new CrackResultT52e
        {
            Found = bestIc >= IcScorer.BaudotGermanThresholdInt,
            TimedOut = false,
            KeysTried = keysTried,
            ElapsedSeconds = sw.Elapsed.TotalSeconds,
            WheelStart = bestStart,
            BestIc = bestIc,
        };
    }

    readonly record struct Candidate(int S6, int S7, int S8, int S9, int GpuIc);

    static unsafe void FillPins(SKBitmap bmp, byte[][] pins)
    {
        IntPtr pp = bmp.GetPixels();
        uint* p = (uint*)pp;
        for (int w = 0; w < 10; w++)
        {
            int len = T52eMachine.PinCounts[w];
            for (int i = 0; i < 73; i++)
            {
                byte v = i < len ? pins[w][i] : (byte)0;
                p[w * 73 + i] = 0xFF000000u | ((uint)v << 16);
            }
        }
        bmp.NotifyPixelsChanged();
    }

    static unsafe void FillByteRow(SKBitmap bmp, byte[] row)
    {
        IntPtr pp = bmp.GetPixels();
        uint* p = (uint*)pp;
        for (int i = 0; i < row.Length; i++)
            p[i] = 0xFF000000u | ((uint)(row[i] & 31) << 16);
        bmp.NotifyPixelsChanged();
    }

    static unsafe void FillPermInv(SKBitmap bmp)
    {
        IntPtr pp = bmp.GetPixels();
        uint* p = (uint*)pp;
        for (int i = 0; i < 5; i++)
            for (int row = 0; row < 32; row++)
                p[i * 32 + row] = 0xFF000000u | ((uint)T52eMachine.Fig9PermInv[row, i] << 16);
        bmp.NotifyPixelsChanged();
    }

}
