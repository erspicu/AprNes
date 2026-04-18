namespace EnigmaBenchmark.Crackers;

using EnigmaBenchmark.Core;

public struct CrackResult
{
    public bool Found;
    public long KeysTried;
    public double ElapsedSeconds;
    public int L, M, R;             // wheel order (indices into RotorData)
    public int PL, PM, PR;           // starting positions
    public int BestIc;              // × 100_000
}

public interface ICracker
{
    string Name { get; }
    /// <summary>
    /// Brute-force wheel order (60) × grundstellung (17,576) = ~10^6 keys.
    /// Known: Ringstellung, Plugboard, Reflector (same as ciphertext producer).
    /// </summary>
    CrackResult Crack(byte[] ciphertext, EnigmaM3 fixedParts);
}
