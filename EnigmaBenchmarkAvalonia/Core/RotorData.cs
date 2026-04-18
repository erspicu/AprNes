namespace EnigmaBenchmark.Core;

/// <summary>
/// Historical Enigma M3 rotor + reflector wiring (publicly documented).
/// All tables are 26-entry byte arrays, index 0 = A, 25 = Z.
/// </summary>
public static class RotorData
{
    // Forward wiring of rotors I-V (Wehrmacht/Heer)
    public static readonly byte[][] Fwd =
    {
        MakeWiring("EKMFLGDQVZNTOWYHXUSPAIBRCJ"), // I
        MakeWiring("AJDKSIRUXBLHWTMCQGZNPYFVOE"), // II
        MakeWiring("BDFHJLCPRTXVZNYEIWGAKMUSQO"), // III
        MakeWiring("ESOVPZJAYQUIRHXLNFTGKDCMWB"), // IV
        MakeWiring("VZBRGITYUPSDNHLXAWMJQOFECK"), // V
    };

    // Reverse wiring (precomputed inverse of Fwd)
    public static readonly byte[][] Rev = new byte[5][];

    // Notch positions (the rotor step that triggers the pawl for the neighbor).
    // Rotor is "on notch" when its current position equals this value.
    public static readonly byte[] NotchPos =
    {
        (byte)('Q' - 'A'), // I:   Q → R
        (byte)('E' - 'A'), // II:  E → F
        (byte)('V' - 'A'), // III: V → W
        (byte)('J' - 'A'), // IV:  J → K
        (byte)('Z' - 'A'), // V:   Z → A
    };

    // Reflectors
    public static readonly byte[] UkwB = MakeWiring("YRUHQSLDPXNGOKMIEBFZCWVJAT");
    public static readonly byte[] UkwC = MakeWiring("FVPJIAOYEDRZXWGCTKUQSBNMHL");

    public static readonly string[] RotorNames = { "I", "II", "III", "IV", "V" };

    static RotorData()
    {
        for (int r = 0; r < 5; r++)
        {
            var inv = new byte[26];
            for (int i = 0; i < 26; i++) inv[Fwd[r][i]] = (byte)i;
            Rev[r] = inv;
        }
    }

    static byte[] MakeWiring(string s)
    {
        var w = new byte[26];
        for (int i = 0; i < 26; i++) w[i] = (byte)(s[i] - 'A');
        return w;
    }

    /// <summary>Enumerate the 60 possible 3-rotor orderings from {0..4}.</summary>
    public static IEnumerable<(int L, int M, int R)> AllWheelOrders()
    {
        for (int l = 0; l < 5; l++)
        for (int m = 0; m < 5; m++)
        {
            if (m == l) continue;
            for (int r = 0; r < 5; r++)
            {
                if (r == l || r == m) continue;
                yield return (l, m, r);
            }
        }
    }
}
