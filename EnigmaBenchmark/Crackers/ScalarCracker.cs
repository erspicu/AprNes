namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using EnigmaBenchmark.Core;

/// <summary>
/// Single-threaded pure-C# brute force. Baseline for benchmark.
/// </summary>
public sealed class ScalarCracker : ICracker
{
    public string Name => "Scalar (single-thread)";

    public CrackResult Crack(byte[] ciphertext, EnigmaM3 fixedParts)
    {
        var sw = Stopwatch.StartNew();
        long keysTried = 0;
        int bestIc = 0;
        int bestL = 0, bestM = 0, bestR = 0, bestPL = 0, bestPM = 0, bestPR = 0;

        // Workspace: reuse one plaintext buffer
        var plaintext = new byte[ciphertext.Length];

        var machine = fixedParts; // copy-by-value for struct
        foreach (var (L, M, R) in RotorData.AllWheelOrders())
        {
            machine.WL = L; machine.WM = M; machine.WR = R;
            for (int pl = 0; pl < 26; pl++)
            for (int pm = 0; pm < 26; pm++)
            for (int pr = 0; pr < 26; pr++)
            {
                machine.PL = pl; machine.PM = pm; machine.PR = pr;

                // Decrypt in-place-like: copy ct → pt, then transform pt
                ciphertext.CopyTo(plaintext.AsSpan());
                machine.Transform(plaintext);

                int ic = IcScorer.ScoreInt(plaintext);
                keysTried++;

                if (ic > bestIc)
                {
                    bestIc = ic;
                    bestL = L; bestM = M; bestR = R;
                    bestPL = pl; bestPM = pm; bestPR = pr;
                }
            }
        }

        sw.Stop();

        return new CrackResult
        {
            Found = bestIc >= IcScorer.GermanThresholdInt,
            KeysTried = keysTried,
            ElapsedSeconds = sw.Elapsed.TotalSeconds,
            L = bestL, M = bestM, R = bestR,
            PL = bestPL, PM = bestPM, PR = bestPR,
            BestIc = bestIc,
        };
    }
}
