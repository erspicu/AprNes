namespace EnigmaBenchmark.Crackers;

public interface ICrackerZimmermann
{
    string Name { get; }

    /// <summary>
    /// Room 40-style attack: given a ciphertext and a known-plaintext crib
    /// (whole messages whose plaintext has been deduced from context), recover
    /// as many codebook entries as possible and decode the target ciphertext.
    /// </summary>
    /// <param name="targetCipher">The ciphertext to decode.</param>
    /// <param name="knownPairs">
    /// List of (plaintext, ciphertext) pairs where BOTH are known. Each pair
    /// contributes word ↔ code-group mappings to the recovered codebook.
    /// </param>
    /// <param name="timeoutSec">0 = no limit.</param>
    CrackResultZimmermann Crack(string targetCipher, IReadOnlyList<(string plain, string cipher)> knownPairs, double timeoutSec = 0);
}

public struct CrackResultZimmermann
{
    public bool Found;
    public bool TimedOut;
    public long KeysTried;       // "keys" here = crib-word alignments tried
    public double ElapsedSeconds;
    public int CodebookEntriesRecovered;
    public int TotalCodeGroupsInTarget;
    public int DecodedGroups;
    public double DecodedRatio => TotalCodeGroupsInTarget == 0 ? 0 :
        (double)DecodedGroups / TotalCodeGroupsInTarget;
    public string? PartialPlaintext;
}
