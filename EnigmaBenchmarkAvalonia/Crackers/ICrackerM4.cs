namespace EnigmaBenchmark.Crackers;

using EnigmaBenchmark.Core;

/// <summary>
/// M4 cracker contract — same CrackResult shape as M3 but takes an EnigmaM4
/// fixed-parts struct (has greek wheel + thin reflector). Kept as a separate
/// interface because the key-search space is genuinely different (extra
/// PG dimension) so making it generic on the machine type wouldn't earn us
/// more than duplication saves.
/// </summary>
public interface ICrackerM4
{
    string Name { get; }
    CrackResult Crack(byte[] ciphertext, EnigmaM4 fixedParts, CrackScope scope);
}
