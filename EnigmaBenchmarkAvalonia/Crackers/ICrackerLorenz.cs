namespace EnigmaBenchmark.Crackers;

using EnigmaBenchmark.Core;

/// <summary>
/// Lorenz chi-only cracker. Pin patterns are taken as known (Kenngruppenbuch
/// would have been captured or deduced); we're searching the 5 chi-wheel
/// START POSITIONS (41×31×29×26×23 ≈ 22 M combinations).
/// </summary>
public interface ICrackerLorenz
{
    string Name { get; }

    /// <summary>
    /// Find the chi starts that best decrypt the ciphertext.
    /// </summary>
    /// <param name="ciphertext">ITA2 5-bit codes (values 0..31).</param>
    /// <param name="chiPins">Known pin patterns, [wheel][pos] ∈ {0,1}.</param>
    /// <param name="scope">Currently unused for Lorenz (keyspace is fixed).</param>
    /// <param name="timeoutSec">Bail out early if this many seconds elapse (0 = no limit).</param>
    CrackResultLorenz Crack(byte[] ciphertext, byte[][] chiPins, CrackScope scope, double timeoutSec = 0);
}

public struct CrackResultLorenz
{
    public bool Found;
    public bool TimedOut;
    public long KeysTried;
    public double ElapsedSeconds;
    public int[] ChiStart;   // length 5
    public int BestIc;       // ×100_000
}
