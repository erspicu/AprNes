namespace EnigmaBenchmark.Crackers;

using EnigmaBenchmark.Core;

/// <summary>
/// T52e "Sturgeon" reduced-keyspace cracker. Historically even a partial wheel
/// recovery was beyond any in-the-field machine; Bletchley's Testery (when it
/// bothered with Sturgeon at all) relied on depths to pin down six of the ten
/// wheels, then brute-forced the remaining four. We model that final stage:
/// assume pin patterns + switch map + the first six wheel start positions are
/// known; search the last four (W7 W8 W9 W10 — pin counts 67 × 69 × 71 × 73
/// = 23,951,289 candidates).
/// </summary>
public interface ICrackerT52e
{
    string Name { get; }

    /// <summary>
    /// Find the 4 wheel start positions that best decrypt the ciphertext.
    /// </summary>
    /// <param name="ciphertext">ITA2 5-bit codes (values 0..31).</param>
    /// <param name="pins">Known pin patterns for all 10 wheels.</param>
    /// <param name="switchMap">Daily key — bijective permutation of {0..9}.</param>
    /// <param name="knownStart">Start positions for W1..W6 (indices 0..5). Indices 6..9 are ignored.</param>
    /// <param name="scope">Currently unused for T52e (keyspace is fixed at ~24M).</param>
    /// <param name="timeoutSec">Bail out early; 0 = no limit.</param>
    CrackResultT52e Crack(
        byte[] ciphertext,
        byte[][] pins,
        int[] switchMap,
        int[] knownStart,
        CrackScope scope,
        double timeoutSec = 0);
}

public struct CrackResultT52e
{
    public bool Found;
    public bool TimedOut;
    public long KeysTried;
    public double ElapsedSeconds;
    /// <summary>Best-scoring full 10-element wheel start array (W1..W10, 0-based).</summary>
    public int[] WheelStart;
    public int BestIc;       // × 100_000

    /// <summary>Optional per-backend diagnostic line for the UI log.</summary>
    public string? Diagnostic;
}
