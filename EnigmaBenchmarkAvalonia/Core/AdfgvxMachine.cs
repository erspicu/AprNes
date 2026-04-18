namespace EnigmaBenchmark.Core;

using System.Text;

/// <summary>
/// ADFGVX — German army field cipher deployed March 1918 (as ADFGX, 5×5
/// Polybius) and upgraded June 1918 (as ADFGVX, 6×6 with digits). Broken
/// by Georges Painvin of the Bureau du Chiffre on 2 June 1918 with the
/// famous "Radiogram of Victory" that revealed German Spring Offensive
/// supply orders and let France reinforce Compiègne in time.
///
/// Two-stage cipher:
///   1. Polybius substitution — each plain char ↦ (row, col) letter pair
///      from {A, D, F, G, V, X}. These six letters were chosen for maximum
///      Morse-code distance (reduces radio-transmission error).
///   2. Columnar transposition under a keyword. Columns reordered by the
///      keyword's alphabetical rank; readout row-major.
///
/// Decryption reverses both steps with the same key material.
/// </summary>
public struct AdfgvxMachine
{
    public const string Alphabet = "ADFGVX";
    public const int GridSize = 36;  // 26 letters + 10 digits

    /// <summary>6×6 Polybius grid, flattened row-major. Each char A-Z or 0-9.</summary>
    public char[] Grid;

    /// <summary>Transposition keyword; uppercase letters, no repeats.</summary>
    public string Keyword;

    public static AdfgvxMachine Create(string grid, string keyword)
    {
        if (grid.Length != GridSize)
            throw new ArgumentException($"Polybius grid must be {GridSize} chars");
        return new AdfgvxMachine
        {
            Grid = grid.ToUpperInvariant().ToCharArray(),
            Keyword = keyword.ToUpperInvariant(),
        };
    }

    public string Encrypt(string plaintext)
    {
        var sub = PolybiusEncode(plaintext);
        return ColumnarTranspose(sub, Keyword);
    }

    public string Decrypt(string ciphertext)
    {
        var sub = ColumnarUntranspose(ciphertext, Keyword);
        return PolybiusDecode(sub);
    }

    /// <summary>Each plaintext char (A-Z or 0-9) ↦ two ADFGVX chars.</summary>
    private string PolybiusEncode(string plain)
    {
        var sb = new StringBuilder(plain.Length * 2);
        foreach (var ch in plain.ToUpperInvariant())
        {
            if (!IsAlnum(ch)) continue;   // drop spaces / punctuation
            int idx = Array.IndexOf(Grid, ch);
            if (idx < 0) continue;
            sb.Append(Alphabet[idx / 6]);
            sb.Append(Alphabet[idx % 6]);
        }
        return sb.ToString();
    }

    private string PolybiusDecode(string sub)
    {
        var sb = new StringBuilder(sub.Length / 2);
        for (int i = 0; i + 1 < sub.Length; i += 2)
        {
            int r = Alphabet.IndexOf(sub[i]);
            int c = Alphabet.IndexOf(sub[i + 1]);
            if (r < 0 || c < 0) { sb.Append('?'); continue; }
            sb.Append(Grid[r * 6 + c]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Columnar transposition: write `sub` into rows under `keyword`, then
    /// read columns out in the keyword's alphabetical order.
    /// </summary>
    public static string ColumnarTranspose(string sub, string keyword)
    {
        int k = keyword.Length;
        int rows = (sub.Length + k - 1) / k;
        var padded = sub.PadRight(rows * k, 'A');

        // Rank each keyword letter by its alphabetical position
        var order = KeywordOrder(keyword);

        var cols = new StringBuilder[k];
        for (int c = 0; c < k; c++) cols[c] = new StringBuilder(rows);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < k; c++)
                cols[c].Append(padded[r * k + c]);

        // Append columns in alphabetical key order
        var sb = new StringBuilder();
        for (int rank = 0; rank < k; rank++)
        {
            int c = Array.IndexOf(order, rank);
            sb.Append(cols[c]);
        }
        return sb.ToString();
    }

    /// <summary>Inverse of ColumnarTranspose.</summary>
    public static string ColumnarUntranspose(string cipher, string keyword)
    {
        int k = keyword.Length;
        int rows = (cipher.Length + k - 1) / k;
        var padded = cipher.PadRight(rows * k, 'A');

        var order = KeywordOrder(keyword);

        // Split cipher into column chunks in key-order, then reassemble row-major
        var cols = new char[k][];
        for (int c = 0; c < k; c++) cols[c] = new char[rows];
        int ptr = 0;
        for (int rank = 0; rank < k; rank++)
        {
            int c = Array.IndexOf(order, rank);
            for (int r = 0; r < rows; r++) cols[c][r] = padded[ptr++];
        }

        var sb = new StringBuilder(rows * k);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < k; c++)
                sb.Append(cols[c][r]);
        return sb.ToString();
    }

    /// <summary>
    /// Return an int[] where order[i] = alphabetical rank of keyword[i].
    /// E.g. "BEACH" → {1, 3, 0, 2, 4}.  Ties broken left-to-right.
    /// </summary>
    public static int[] KeywordOrder(string keyword)
    {
        int k = keyword.Length;
        var order = new int[k];
        var indices = Enumerable.Range(0, k).ToArray();
        Array.Sort(indices, (a, b) => keyword[a].CompareTo(keyword[b]));
        for (int rank = 0; rank < k; rank++) order[indices[rank]] = rank;
        return order;
    }

    private static bool IsAlnum(char c) => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
}
