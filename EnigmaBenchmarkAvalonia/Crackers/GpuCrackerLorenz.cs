namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using SkiaSharp;
using EnigmaBenchmark.Core;

/// <summary>
/// SkSL GPU cracker for Lorenz SZ40 chi-only. 23 render passes (one per s4
/// start); each pass is a 1271×754 tile covering all (s0,s1,s2,s3) starts.
/// Truth's IC margin over noise at shader CAP=320 isn't large enough on the
/// 22 M keyspace, so we keep a top-K min-heap and let the CPU re-rank on
/// the FULL ciphertext — full-text IC has ~sqrt(CAP/N) tighter variance
/// and cleanly picks truth.
/// </summary>
public sealed class GpuCrackerLorenz : ICrackerLorenz
{
    readonly GRContext? _grContext;

    public GpuCrackerLorenz(GRContext? grContext = null) { _grContext = grContext; }

    public string Name => _grContext != null
        ? $"SkSL Lorenz ({_grContext.Backend} GPU, 180-char space+E score)"
        : "SkSL GPU Lorenz (180-char space+E score)";

    static readonly string ShaderSrc = LoadShader();

    static string LoadShader()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Shaders", "lorenz_crack.sksl");
        return File.ReadAllText(path);
    }

    public CrackResultLorenz Crack(byte[] ciphertext, byte[][] chiPins, CrackScope scope, double timeoutSec = 0)
    {
        var sw = Stopwatch.StartNew();

        using var effect = SKRuntimeEffect.CreateShader(ShaderSrc, out var err);
        if (effect == null)
            throw new InvalidOperationException($"Lorenz SkSL compile failed: {err}");

        // ── Pin pattern bitmap: 64 × 5, row w = wheel w's pin pattern ──
        using var pinsBmp = new SKBitmap(
            new SKImageInfo(64, 5, SKColorType.Bgra8888, SKAlphaType.Opaque));
        FillChiPins(pinsBmp, chiPins);

        using var cipherBmp = new SKBitmap(
            new SKImageInfo(ciphertext.Length, 1, SKColorType.Bgra8888, SKAlphaType.Opaque));
        FillByteRow(cipherBmp, ciphertext);

        using var pinsShader = SKShader.CreateBitmap(
            pinsBmp, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
        using var cipherShader = SKShader.CreateBitmap(
            cipherBmp, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);

        // ── 1271 × 754 tile: x encodes (s0, s1), y encodes (s2, s3) ──
        const int TileW = 1271;   // 41 × 31
        const int TileH = 754;    // 29 × 26
        var surfInfo = new SKImageInfo(TileW, TileH, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var surface = _grContext != null
            ? SKSurface.Create(_grContext, false, surfInfo)
            : SKSurface.Create(surfInfo);

        var pixels = new uint[TileW * TileH];

        // ── Top-K min-heap ──
        // Shader's "space+E count × 1000" scorer gives 10σ+ separation so
        // truth is essentially always in the very top handful. K=512 with a
        // 15000 floor (≈ 15 hits at 1000× scale) drops obvious noise cheaply.
        const int TopK = 512;
        const int IcFloor = 15000;
        var topQ = new PriorityQueue<Candidate, int>();

        long keysTried = 0;

        // ── 23 passes, one per s4 ──
        int s4Start = 0, s4End = LorenzSZ40.ChiPinCounts[4];
        for (int s4 = s4Start; s4 < s4End; s4++)
        {
            var uniforms = new SKRuntimeEffectUniforms(effect)
            {
                ["uCtLen"] = (float)ciphertext.Length,
                ["uS4"]    = (float)s4,
            };
            var children = new SKRuntimeEffectChildren(effect)
            {
                ["uChiPins"] = pinsShader,
                ["uCipher"]  = cipherShader,
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

            // Scan pixel buffer, insert into top-K heap
            for (int y = 0; y < TileH; y++)
            {
                int s3 = y / 29;
                if (s3 >= 26) continue;
                int s2 = y - s3 * 29;
                for (int x = 0; x < TileW; x++)
                {
                    int s1 = x / 41;
                    if (s1 >= 31) continue;
                    int s0 = x - s1 * 41;

                    uint bgra = pixels[y * TileW + x];
                    int ic = (int)((bgra >> 16 & 0xFF) << 16)
                           | (int)((bgra >>  8 & 0xFF) <<  8)
                           | (int)( bgra       & 0xFF);
                    keysTried++;

                    if (ic < IcFloor) continue;

                    if (topQ.Count < TopK)
                    {
                        topQ.Enqueue(new Candidate(s0, s1, s2, s3, s4, ic), ic);
                    }
                    else if (topQ.TryPeek(out _, out int minIc) && ic > minIc)
                    {
                        topQ.Dequeue();
                        topQ.Enqueue(new Candidate(s0, s1, s2, s3, s4, ic), ic);
                    }
                }
            }
        }

        // ── CPU re-rank on full ciphertext ──
        var cand = new List<Candidate>(topQ.Count);
        while (topQ.Count > 0) cand.Add(topQ.Dequeue());

        int bestIc = 0;
        int[] bestStart = { 0, 0, 0, 0, 0 };
        var verifier = LorenzSZ40.Create(chiPins, new[] { 0, 0, 0, 0, 0 });
        foreach (var c in cand)
        {
            int[] start = { c.S0, c.S1, c.S2, c.S3, c.S4 };
            byte[] decrypted = verifier.TransformFresh(ciphertext, start);
            int fullIc = IcScorer.ScoreBaudotInt(decrypted);
            if (fullIc > bestIc)
            {
                bestIc = fullIc;
                bestStart = start;
            }
        }

        sw.Stop();

        return new CrackResultLorenz
        {
            Found = bestIc >= IcScorer.BaudotGermanThresholdInt,
            TimedOut = false,
            KeysTried = keysTried,
            ElapsedSeconds = sw.Elapsed.TotalSeconds,
            ChiStart = bestStart,
            BestIc = bestIc,
        };
    }

    readonly record struct Candidate(int S0, int S1, int S2, int S3, int S4, int GpuIc);

    static unsafe void FillChiPins(SKBitmap bmp, byte[][] chiPins)
    {
        IntPtr pp = bmp.GetPixels();
        uint* p = (uint*)pp;
        // 64-wide × 5-tall, clamped rows
        for (int w = 0; w < 5; w++)
        {
            int len = LorenzSZ40.ChiPinCounts[w];
            for (int i = 0; i < 64; i++)
            {
                byte v = i < len ? chiPins[w][i] : (byte)0;
                p[w * 64 + i] = 0xFF000000u | ((uint)v << 16);
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
}
