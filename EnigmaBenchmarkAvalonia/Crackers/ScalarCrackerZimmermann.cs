namespace EnigmaBenchmark.Crackers;

using System.Diagnostics;
using System.Text;

/// <summary>
/// Room 40 in a single C# method. Walk each known-plaintext crib pair in
/// lock-step (word N of plaintext pairs with code group N of ciphertext),
/// collect the resulting word↔code mappings, then apply the recovered
/// codebook to the target ciphertext and count the percentage decoded.
///
/// Single-pass linear in the total crib + target size; no search, no
/// parallelism. This matches the historical attack shape — Room 40's
/// edge was ACCESS to intercepts, not CPU.
/// </summary>
public sealed class ScalarCrackerZimmermann : ICrackerZimmermann
{
    public string Name => "Scalar Zimmermann / 0075 (known-plaintext)";

    public CrackResultZimmermann Crack(
        string targetCipher,
        IReadOnlyList<(string plain, string cipher)> knownPairs,
        double timeoutSec = 0)
    {
        var sw = Stopwatch.StartNew();

        var recovered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long alignmentsTried = 0;

        foreach (var (plain, cipher) in knownPairs)
        {
            var plainWords = plain.ToUpperInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cipherGroups = cipher.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            int n = Math.Min(plainWords.Length, cipherGroups.Length);
            for (int i = 0; i < n; i++)
            {
                alignmentsTried++;
                var w = new string(plainWords[i].Where(char.IsLetterOrDigit).ToArray());
                var g = cipherGroups[i];
                if (g.StartsWith('[')) continue;     // literal spell-out, no code group
                if (!recovered.ContainsKey(g))
                    recovered[g] = w;
            }
        }

        // Apply recovered codebook to the target
        var targetGroups = targetCipher.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        int decoded = 0;
        foreach (var g in targetGroups)
        {
            if (sb.Length > 0) sb.Append(' ');
            if (g.StartsWith('[') && g.EndsWith(']'))
            {
                sb.Append(g[1..^1]);      // spell-out works regardless
                decoded++;
            }
            else if (recovered.TryGetValue(g, out var w))
            {
                sb.Append(w);
                decoded++;
            }
            else
            {
                sb.Append('?').Append(g);
            }
        }

        sw.Stop();
        return new CrackResultZimmermann
        {
            Found = decoded == targetGroups.Length,
            TimedOut = false,
            KeysTried = alignmentsTried,
            ElapsedSeconds = sw.Elapsed.TotalSeconds,
            CodebookEntriesRecovered = recovered.Count,
            TotalCodeGroupsInTarget = targetGroups.Length,
            DecodedGroups = decoded,
            PartialPlaintext = sb.ToString(),
        };
    }
}
