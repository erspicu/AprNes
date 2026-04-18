namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using System.Text;
using EnigmaBenchmark.Core;

/// <summary>
/// Single-threaded ADFGVX cracker. Brute-forces all K! column orders,
/// reverses the transposition, reverses the Polybius substitution, and
/// scores the candidate plaintext by letter index-of-coincidence.
///
/// Matching Painvin's threat model simplified: he also had to recover the
/// 6×6 Polybius, which is its own harder attack. We assume grid known so
/// the benchmark stays focused on the keyword-order search.
/// </summary>
public sealed class ScalarCrackerAdfgvx : ICrackerAdfgvx
{
    public string Name => "Scalar ADFGVX (single-thread)";

    public CrackResultAdfgvx Crack(string ciphertext, string grid, int keywordLength, double timeoutSec = 0)
    {
        var sw = Stopwatch.StartNew();
        int K = keywordLength;

        int bestIc = 0;
        int[] bestOrder = new int[K];
        string? bestPreview = null;
        long keysTried = 0;
        bool timedOut = false;
        long timeoutTicks = timeoutSec > 0 ? (long)(timeoutSec * Stopwatch.Frequency) : long.MaxValue;

        // Iterate every permutation of {0..K-1}
        var perm = Enumerable.Range(0, K).ToArray();
        do
        {
            var candidate = DecryptWith(ciphertext, grid, perm);
            int ic = IcScorer.ScoreInt(AsciiLettersOnly(candidate));
            keysTried++;

            if (ic > bestIc)
            {
                bestIc = ic;
                Array.Copy(perm, bestOrder, K);
                bestPreview = candidate.Length > 60 ? candidate.Substring(0, 60) + "…" : candidate;
            }

            if (sw.ElapsedTicks > timeoutTicks) { timedOut = true; break; }
        }
        while (NextPermutation(perm));

        sw.Stop();
        return new CrackResultAdfgvx
        {
            Found = bestIc >= IcScorer.GermanThresholdInt,
            TimedOut = timedOut,
            KeysTried = keysTried,
            ElapsedSeconds = sw.Elapsed.TotalSeconds,
            KeyOrder = bestOrder,
            BestIc = bestIc,
            DecodedPreview = bestPreview,
        };
    }

    /// <summary>
    /// Reverse ADFGVX given ciphertext, known grid, and a candidate column order.
    /// `order` is a permutation where order[i] = alphabetical rank of keyword[i].
    /// </summary>
    public static string DecryptWith(string cipher, string grid, int[] order)
    {
        int k = order.Length;
        int rows = (cipher.Length + k - 1) / k;
        var padded = cipher.PadRight(rows * k, 'A');

        var cols = new char[k][];
        for (int c = 0; c < k; c++) cols[c] = new char[rows];
        int ptr = 0;
        for (int rank = 0; rank < k; rank++)
        {
            int c = Array.IndexOf(order, rank);
            for (int r = 0; r < rows; r++) cols[c][r] = padded[ptr++];
        }

        // Row-major reassembly
        var sub = new StringBuilder(rows * k);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < k; c++)
                sub.Append(cols[c][r]);

        // Polybius decode
        var plain = new StringBuilder(sub.Length / 2);
        for (int i = 0; i + 1 < sub.Length; i += 2)
        {
            int rr = AdfgvxMachine.Alphabet.IndexOf(sub[i]);
            int cc = AdfgvxMachine.Alphabet.IndexOf(sub[i + 1]);
            if (rr < 0 || cc < 0) { plain.Append('?'); continue; }
            plain.Append(grid[rr * 6 + cc]);
        }
        return plain.ToString();
    }

    private static byte[] AsciiLettersOnly(string s)
    {
        var buf = new byte[s.Length];
        int n = 0;
        foreach (var c in s)
        {
            if (c >= 'A' && c <= 'Z') buf[n++] = (byte)(c - 'A');
        }
        return buf[..n];
    }

    /// <summary>Lexicographic next permutation in place (classic algorithm).</summary>
    public static bool NextPermutation(int[] a)
    {
        int i = a.Length - 2;
        while (i >= 0 && a[i] >= a[i + 1]) i--;
        if (i < 0) return false;
        int j = a.Length - 1;
        while (a[j] <= a[i]) j--;
        (a[i], a[j]) = (a[j], a[i]);
        Array.Reverse(a, i + 1, a.Length - i - 1);
        return true;
    }
}
