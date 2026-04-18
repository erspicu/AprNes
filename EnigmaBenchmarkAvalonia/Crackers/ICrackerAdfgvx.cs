namespace EnigmaBenchmark.Crackers;

public interface ICrackerAdfgvx
{
    string Name { get; }

    /// <summary>
    /// Recover the keyword-column order. Assumes the Polybius grid is known
    /// (historically Painvin recovered it too via depths, but that's a much
    /// harder separate attack — we simplify for benchmark purposes).
    /// </summary>
    /// <param name="ciphertext">ADFGVX-alphabet ciphertext.</param>
    /// <param name="grid">Known Polybius grid (36 chars).</param>
    /// <param name="keywordLength">Length of the daily transposition keyword.</param>
    /// <param name="timeoutSec">0 = no limit.</param>
    CrackResultAdfgvx Crack(string ciphertext, string grid, int keywordLength, double timeoutSec = 0);
}

public struct CrackResultAdfgvx
{
    public bool Found;
    public bool TimedOut;
    public long KeysTried;
    public double ElapsedSeconds;
    /// <summary>Best column-order recovered, permutation of 0..K-1.</summary>
    public int[] KeyOrder;
    public int BestIc;  // ×100_000
    public string? DecodedPreview;
}
