namespace EnigmaBenchmark.Core;

/// <summary>
/// Index of Coincidence — statistical measure used to distinguish real German text
/// from random (unsuccessfully-decrypted) noise.
///
///   IC = Σᵢ nᵢ(nᵢ - 1) / (N(N - 1))
///
/// Expected values:
///   - German plaintext: ~0.0762
///   - Random cipher:    ~0.0385
///
/// A threshold of 0.065 reliably separates the two for texts ≥ ~200 chars.
/// </summary>
public static class IcScorer
{
    // Constructed military text with heavy STOP / X padding can IC slightly below
    // natural prose (~0.061 vs 0.076). 0.055 safely separates from random 0.0385.
    public const double GermanThreshold = 0.055;

    /// <summary>
    /// Compute IC × 100,000 as an integer (avoids float math in hot path, still gives
    /// 5 decimal precision). Higher = more language-like.
    /// </summary>
    public static int ScoreInt(ReadOnlySpan<byte> text)
    {
        if (text.Length < 2) return 0;

        Span<int> freq = stackalloc int[26];
        foreach (var b in text) freq[b]++;

        long numer = 0;
        for (int i = 0; i < 26; i++)
            numer += (long)freq[i] * (freq[i] - 1);

        long denom = (long)text.Length * (text.Length - 1);
        // IC × 100_000; cast after multiplication to keep precision
        return (int)(numer * 100_000 / denom);
    }

    public const int GermanThresholdInt = 5500; // 0.055 × 100_000
}
