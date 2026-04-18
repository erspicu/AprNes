namespace EnigmaBenchmark.Core;

/// <summary>
/// ITA2 (International Telegraph Alphabet No. 2) 5-bit code used by Lorenz
/// SZ40/SZ42. For the benchmark we only handle letters A–Z plus SPACE —
/// no LTRS/FIGS shift, no digits. That keeps keystream XOR reciprocal and
/// the scoring straightforward.
///
/// Bit ordering: bit 1 is least significant. Value = b5·16 + b4·8 + b3·4
/// + b2·2 + b1·1.
/// </summary>
public static class Baudot
{
    public const int ALPHABET_SIZE = 32;   // 5-bit symbol space

    public const byte SPACE = 0x04;

    // ITA2 letter-shift mapping (A–Z only). Values in 1..30 range.
    public static readonly byte[] LetterToCode = new byte[26]
    {
        /*A*/ 0x03, /*B*/ 0x19, /*C*/ 0x0E, /*D*/ 0x09, /*E*/ 0x01,
        /*F*/ 0x0D, /*G*/ 0x1A, /*H*/ 0x14, /*I*/ 0x06, /*J*/ 0x0B,
        /*K*/ 0x0F, /*L*/ 0x12, /*M*/ 0x1C, /*N*/ 0x0C, /*O*/ 0x18,
        /*P*/ 0x16, /*Q*/ 0x17, /*R*/ 0x0A, /*S*/ 0x05, /*T*/ 0x10,
        /*U*/ 0x07, /*V*/ 0x1E, /*W*/ 0x13, /*X*/ 0x1D, /*Y*/ 0x15,
        /*Z*/ 0x11,
    };

    // Reverse lookup: 5-bit code → 'A'+offset if a letter, or '·' for other.
    public static readonly char[] CodeToChar = BuildReverse();

    static char[] BuildReverse()
    {
        var r = new char[32];
        for (int i = 0; i < 32; i++) r[i] = '·';
        for (int i = 0; i < 26; i++) r[LetterToCode[i]] = (char)('A' + i);
        r[SPACE] = ' ';
        return r;
    }

    /// <summary>Encode an A–Z string (ignores non-letters except whitespace→SPACE) into ITA2 bytes.</summary>
    public static byte[] Encode(string s)
    {
        var list = new List<byte>(s.Length);
        foreach (char ch in s)
        {
            char u = char.ToUpperInvariant(ch);
            if (u >= 'A' && u <= 'Z') list.Add(LetterToCode[u - 'A']);
            else if (u == ' ')        list.Add(SPACE);
            // ignore other punctuation
        }
        return list.ToArray();
    }

    /// <summary>Decode ITA2 bytes back to A–Z / space / · for non-letter codes.</summary>
    public static string Decode(byte[] codes)
    {
        var chars = new char[codes.Length];
        for (int i = 0; i < codes.Length; i++)
            chars[i] = CodeToChar[codes[i] & 0x1F];
        return new string(chars);
    }
}
