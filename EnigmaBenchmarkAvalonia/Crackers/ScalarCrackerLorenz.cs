namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using EnigmaBenchmark.Core;

/// <summary>
/// Single-threaded Lorenz chi-start brute force. 22 M candidates × ~700-char
/// ciphertext = ~15 B ops on one thread — likely 1-3 minutes. Caller should
/// pass a timeout (~60 s) so the benchmark doesn't hang.
/// </summary>
public sealed class ScalarCrackerLorenz : ICrackerLorenz
{
    public string Name => "Scalar Lorenz (single-thread)";

    public CrackResultLorenz Crack(byte[] ciphertext, byte[][] chiPins, CrackScope scope, double timeoutSec = 0)
    {
        var sw = Stopwatch.StartNew();
        int n = ciphertext.Length;

        var plaintext = new byte[n];

        int bestIc = 0;
        int[] bestStart = { 0, 0, 0, 0, 0 };
        long keysTried = 0;
        bool timedOut = false;
        long timeoutTicks = timeoutSec > 0
            ? (long)(timeoutSec * Stopwatch.Frequency)
            : long.MaxValue;

        int[] counts = LorenzSZ40.ChiPinCounts;
        byte[] pin0 = chiPins[0], pin1 = chiPins[1], pin2 = chiPins[2],
               pin3 = chiPins[3], pin4 = chiPins[4];
        int c0 = counts[0], c1 = counts[1], c2 = counts[2], c3 = counts[3], c4 = counts[4];

        for (int s4 = 0; s4 < c4; s4++)
        for (int s3 = 0; s3 < c3; s3++)
        for (int s2 = 0; s2 < c2; s2++)
        for (int s1 = 0; s1 < c1; s1++)
        {
            // Inner loop over s0 (largest wheel, 41) — innermost for cache friendliness
            for (int s0 = 0; s0 < c0; s0++)
            {
                int p0 = s0, p1 = s1, p2 = s2, p3 = s3, p4 = s4;
                for (int i = 0; i < n; i++)
                {
                    int chi = pin0[p0]
                            | (pin1[p1] << 1)
                            | (pin2[p2] << 2)
                            | (pin3[p3] << 3)
                            | (pin4[p4] << 4);
                    plaintext[i] = (byte)((ciphertext[i] ^ chi) & 0x1F);

                    if (++p0 >= c0) p0 = 0;
                    if (++p1 >= c1) p1 = 0;
                    if (++p2 >= c2) p2 = 0;
                    if (++p3 >= c3) p3 = 0;
                    if (++p4 >= c4) p4 = 0;
                }

                int ic = IcScorer.ScoreBaudotInt(plaintext);
                keysTried++;

                if (ic > bestIc)
                {
                    bestIc = ic;
                    bestStart[0] = s0; bestStart[1] = s1; bestStart[2] = s2;
                    bestStart[3] = s3; bestStart[4] = s4;
                }
            }

            // Check timeout at outer boundary (avoid clock overhead in hot loop)
            if (sw.ElapsedTicks > timeoutTicks)
            {
                timedOut = true;
                goto Done;
            }
        }
    Done:

        sw.Stop();
        return new CrackResultLorenz
        {
            Found = bestIc >= IcScorer.BaudotGermanThresholdInt,
            TimedOut = timedOut,
            KeysTried = keysTried,
            ElapsedSeconds = sw.Elapsed.TotalSeconds,
            ChiStart = bestStart,
            BestIc = bestIc,
        };
    }
}
