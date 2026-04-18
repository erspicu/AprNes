namespace EnigmaBenchmark.Core;

/// <summary>
/// Enigma M3 (3-rotor Wehrmacht) cipher machine.
/// All positions/rings are 0-25 (A=0). Plugboard is a 26-entry identity-by-default
/// substitution table.
///
/// The hot path is <see cref="EncryptChar"/>; cracker code calls it ~10^6 times × 310 chars per
/// search → keep allocations out, use preallocated state.
/// </summary>
public struct EnigmaM3
{
    // Wheel order: WL, WM, WR (left/middle/right); indices into RotorData.Fwd
    public int WL, WM, WR;
    // Ringstellung (Ring setting) 0-25 for each position
    public int RL, RM, RR;
    // Current rotor positions (Grundstellung at start, advances per keypress)
    public int PL, PM, PR;
    // Plugboard substitution (A-Z pairwise swaps, identity if disabled)
    public byte[] Plugboard;
    // Reflector (UKW-B or UKW-C)
    public byte[] Reflector;

    public static EnigmaM3 Default()
    {
        return new EnigmaM3
        {
            WL = 2, WM = 3, WR = 0, // III IV I
            RL = 23, RM = 12, RR = 21, // X M V (0-based: X=23, M=12, V=21)
            PL = 16, PM = 22, PR = 4,  // Q W E
            Plugboard = IdentityPlugboard(),
            Reflector = RotorData.UkwB,
        };
    }

    public static byte[] IdentityPlugboard()
    {
        var p = new byte[26];
        for (int i = 0; i < 26; i++) p[i] = (byte)i;
        return p;
    }

    /// <summary>Parse plugboard like "AP BR CM FZ GK IV JU LS NT OX" → swap table.</summary>
    public static byte[] ParsePlugboard(string pairs)
    {
        var p = IdentityPlugboard();
        foreach (var token in pairs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length != 2) continue;
            byte a = (byte)(char.ToUpperInvariant(token[0]) - 'A');
            byte b = (byte)(char.ToUpperInvariant(token[1]) - 'A');
            p[a] = b;
            p[b] = a;
        }
        return p;
    }

    /// <summary>Advance rotors one step with double-step anomaly.</summary>
    public void Step()
    {
        bool midOnNotch   = PM == RotorData.NotchPos[WM];
        bool rightOnNotch = PR == RotorData.NotchPos[WR];

        if (midOnNotch)
        {
            PL = (PL + 1) % 26;
            PM = (PM + 1) % 26; // double-step
        }
        else if (rightOnNotch)
        {
            PM = (PM + 1) % 26;
        }
        PR = (PR + 1) % 26;
    }

    /// <summary>Encrypt one character (0-25). Advances rotors before substitution (Enigma convention).</summary>
    public byte EncryptChar(byte c)
    {
        Step();

        // Plugboard in
        c = Plugboard[c];

        // Right rotor forward
        c = (byte)((c + PR - RR + 26) % 26);
        c = RotorData.Fwd[WR][c];
        c = (byte)((c - PR + RR + 26) % 26);

        // Middle rotor forward
        c = (byte)((c + PM - RM + 26) % 26);
        c = RotorData.Fwd[WM][c];
        c = (byte)((c - PM + RM + 26) % 26);

        // Left rotor forward
        c = (byte)((c + PL - RL + 26) % 26);
        c = RotorData.Fwd[WL][c];
        c = (byte)((c - PL + RL + 26) % 26);

        // Reflector
        c = Reflector[c];

        // Left rotor reverse
        c = (byte)((c + PL - RL + 26) % 26);
        c = RotorData.Rev[WL][c];
        c = (byte)((c - PL + RL + 26) % 26);

        // Middle rotor reverse
        c = (byte)((c + PM - RM + 26) % 26);
        c = RotorData.Rev[WM][c];
        c = (byte)((c - PM + RM + 26) % 26);

        // Right rotor reverse
        c = (byte)((c + PR - RR + 26) % 26);
        c = RotorData.Rev[WR][c];
        c = (byte)((c - PR + RR + 26) % 26);

        // Plugboard out
        c = Plugboard[c];

        return c;
    }

    /// <summary>Transform a whole byte span in-place (encrypt or decrypt — Enigma is reciprocal).</summary>
    public void Transform(Span<byte> buf)
    {
        for (int i = 0; i < buf.Length; i++)
            buf[i] = EncryptChar(buf[i]);
    }

    /// <summary>Convenience: transform to a fresh array given a starting position (resets positions each call).</summary>
    public byte[] TransformFresh(byte[] input, int pl, int pm, int pr)
    {
        PL = pl; PM = pm; PR = pr;
        var output = new byte[input.Length];
        for (int i = 0; i < input.Length; i++)
            output[i] = EncryptChar(input[i]);
        return output;
    }

    /// <summary>Convert A-Z ASCII string to byte[] (0-25). Ignores non-letters.</summary>
    public static byte[] EncodeString(string s)
    {
        var list = new List<byte>(s.Length);
        foreach (char ch in s)
        {
            char u = char.ToUpperInvariant(ch);
            if (u >= 'A' && u <= 'Z') list.Add((byte)(u - 'A'));
        }
        return list.ToArray();
    }

    /// <summary>Decode byte[] (0-25) back to A-Z string.</summary>
    public static string DecodeBytes(byte[] b)
    {
        var chars = new char[b.Length];
        for (int i = 0; i < b.Length; i++) chars[i] = (char)('A' + b[i]);
        return new string(chars);
    }
}
