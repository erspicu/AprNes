namespace EnigmaBenchmark.Crackers;

using EnigmaBenchmark.Core;

public struct CrackResult
{
    public bool Found;
    public long KeysTried;
    public double ElapsedSeconds;
    public int L, M, R;                   // wheel order (indices into RotorData)
    public int PL, PM, PR;                // starting positions
    public int RR_Ring;                   // right ring found (normal scope +)
    public int RM_Ring;                   // middle ring found (hard scope +)
    public int RL_Ring;                   // left ring found (extreme scope)
    public int BestIc;                    // × 100_000

    // M4-only: greek wheel (0=Beta, 1=Gamma) + its start position. M3
    // crackers leave these 0; callers ignore them for M3.
    public int WG;
    public int PG;

    // Timeout: set by Scalar/Parallel/SIMD when they bail out on a budget
    // before the full keyspace is searched.
    public bool TimedOut;
}

public enum CrackScope
{
    /// <summary>wheel × 26³ positions = ~1M keys</summary>
    Quick = 0,
    /// <summary>+ right ring (26) = ~27M keys</summary>
    Normal = 1,
    /// <summary>+ middle ring (26²) = ~713M keys</summary>
    Hard = 2,
    /// <summary>+ left ring (26³) = ~18.5G keys</summary>
    Extreme = 3,
}

public static class CrackScopeExt
{
    public static long TotalKeys(this CrackScope s)
    {
        long total = 60L * 26 * 26 * 26;
        for (int i = 0; i < (int)s; i++) total *= 26;
        return total;
    }

    public static string Describe(this CrackScope s) => s switch
    {
        CrackScope.Quick   => "wheel × pos³",
        CrackScope.Normal  => "wheel × pos³ × ring¹",
        CrackScope.Hard    => "wheel × pos³ × ring²",
        CrackScope.Extreme => "wheel × pos³ × ring³",
        _ => "?",
    };
}

public interface ICracker
{
    string Name { get; }
    CrackResult Crack(byte[] ciphertext, EnigmaM3 fixedParts, CrackScope scope);
}
