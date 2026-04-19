namespace EnigmaBenchmark.Core;

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

/// <summary>
/// Central capability probe — tells every SIMD cracker which vector path
/// it can use and what label to show in the UI.
///
/// Three tiers in priority order:
///   1. AVX2 (x86-64, 256-bit) — best throughput
///   2. AdvSimd / NEON (ARM64, 128-bit) — Apple Silicon, ARM Linux, ARM Windows
///   3. scalar fallback (everything else — defers to ParallelScalar in crackers)
/// </summary>
public static class SimdCaps
{
    public static bool HasAvx2  => Avx2.IsSupported;
    public static bool HasNeon  => AdvSimd.IsSupported;
    public static bool HasAnyVector => HasAvx2 || HasNeon;

    /// <summary>"AVX2" / "NEON" / "scalar"</summary>
    public static string ActivePath
    {
        get
        {
            if (HasAvx2) return "AVX2";
            if (HasNeon) return "NEON";
            return "scalar";
        }
    }

    /// <summary>Used in cracker Name properties, e.g. "x64 / AVX2" or "Arm64 / NEON".</summary>
    public static string HardwareDesc
        => $"{RuntimeInformation.ProcessArchitecture} / {ActivePath}";
}
