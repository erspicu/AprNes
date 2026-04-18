namespace EnigmaBenchmark.Core;

using System.Text;

/// <summary>
/// Simplified simulation of the German Foreign Office code <strong>0075</strong>
/// used for the January 1917 Zimmermann Telegram. A codebook cipher is
/// fundamentally different from a mechanical one: each <em>word</em> of plaintext
/// is replaced by a numeric code group looked up in a shared book. Without the
/// book, no statistical attack on the ciphertext alone can recover plaintext —
/// this is why the real cipher was never "brute-forced". Room 40 reconstructed
/// 0075 over years by accumulating intercepts and exploiting known-plaintext
/// (formulaic diplomatic headers and signatures).
///
/// Our benchmark scenario mirrors that historical reality:
///   1. Encrypt a Zimmermann-style message using a fixed codebook.
///   2. Give the cracker only a CRIB (a few known-plaintext fragments) and
///      the ciphertext. The cracker recovers whichever codebook entries the
///      crib reveals, then partially decodes the rest.
///   3. Report a decode percentage + recovered codebook entries.
///
/// No massive keyspace; no parallelism needed — just a dictionary lookup and
/// alignment pass, which is why a single-thread Scalar runner is sufficient.
/// </summary>
public struct ZimmermannCodebook
{
    /// <summary>Word → 4-digit code group. Immutable after construction.</summary>
    public Dictionary<string, string> Forward;

    /// <summary>Reverse map for decryption.</summary>
    public Dictionary<string, string> Reverse;

    public static ZimmermannCodebook Create(Dictionary<string, string> forward)
    {
        var rev = new Dictionary<string, string>(forward.Count);
        foreach (var kv in forward) rev[kv.Value] = kv.Key;
        return new ZimmermannCodebook { Forward = forward, Reverse = rev };
    }

    /// <summary>Encrypt a plaintext (space-separated words) to a cipher group list.</summary>
    public string Encrypt(string plaintext)
    {
        var sb = new StringBuilder();
        var words = plaintext.ToUpperInvariant().Split(' ',
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var w in words)
        {
            var clean = new string(w.Where(char.IsLetterOrDigit).ToArray());
            if (Forward.TryGetValue(clean, out var code))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(code);
            }
            else
            {
                // Unknown word — encode as literal in square brackets
                // (historical codebooks had this fallback too, called "spelling out")
                if (sb.Length > 0) sb.Append(' ');
                sb.Append('[').Append(clean).Append(']');
            }
        }
        return sb.ToString();
    }

    /// <summary>Decrypt a cipher group list back to plaintext using a possibly-partial codebook.</summary>
    public string Decrypt(string cipher)
    {
        var sb = new StringBuilder();
        var groups = cipher.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var g in groups)
        {
            if (sb.Length > 0) sb.Append(' ');
            if (g.StartsWith('[') && g.EndsWith(']'))
                sb.Append(g[1..^1]);
            else if (Reverse.TryGetValue(g, out var w))
                sb.Append(w);
            else
                sb.Append('?');   // unknown code group — codebook hole
        }
        return sb.ToString();
    }
}
