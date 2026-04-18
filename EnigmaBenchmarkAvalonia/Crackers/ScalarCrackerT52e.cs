namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using EnigmaBenchmark.Core;

/// <summary>
/// Single-threaded T52e cracker. Brute-forces the 4 unknown wheel start
/// positions (indices 6, 7, 8, 9 — pin counts 67, 69, 71, 73) assuming the
/// other six are known from prior analysis. ~24 million candidates.
/// </summary>
public sealed class ScalarCrackerT52e : ICrackerT52e
{
    public string Name => "Scalar T52e (single-thread)";

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

        var machine = T52eMachine.Create(pins, switchMap, knownStart, ktf: false);
        var candidate = new byte[n];
        var startBuf = new int[10];
        Array.Copy(knownStart, startBuf, 6);

        int bestIc = 0;
        var bestStart = (int[])knownStart.Clone();
        long keysTried = 0;
        bool timedOut = false;
        long timeoutTicks = timeoutSec > 0
            ? (long)(timeoutSec * Stopwatch.Frequency)
            : long.MaxValue;

        int c6 = T52eMachine.PinCounts[6];
        int c7 = T52eMachine.PinCounts[7];
        int c8 = T52eMachine.PinCounts[8];
        int c9 = T52eMachine.PinCounts[9];

        for (int s9 = 0; s9 < c9; s9++)
        {
            startBuf[9] = s9;
            for (int s8 = 0; s8 < c8; s8++)
            {
                startBuf[8] = s8;
                for (int s7 = 0; s7 < c7; s7++)
                {
                    startBuf[7] = s7;
                    for (int s6 = 0; s6 < c6; s6++)
                    {
                        startBuf[6] = s6;

                        // Decrypt with candidate key — produces candidate plaintext.
                        machine.WheelPos = (int[])startBuf.Clone();
                        machine.Rr3 = 0;
                        for (int i = 0; i < n; i++)
                            candidate[i] = machine.Decrypt(ciphertext[i]);

                        int ic = IcScorer.ScoreBaudotInt(candidate);
                        keysTried++;

                        if (ic > bestIc)
                        {
                            bestIc = ic;
                            bestStart = (int[])startBuf.Clone();
                        }
                    }

                    if (sw.ElapsedTicks > timeoutTicks)
                    {
                        timedOut = true;
                        goto Done;
                    }
                }
            }
        }
    Done:
        sw.Stop();

        return new CrackResultT52e
        {
            Found = bestIc >= IcScorer.BaudotGermanThresholdInt,
            TimedOut = timedOut,
            KeysTried = keysTried,
            ElapsedSeconds = sw.Elapsed.TotalSeconds,
            WheelStart = bestStart,
            BestIc = bestIc,
        };
    }
}
