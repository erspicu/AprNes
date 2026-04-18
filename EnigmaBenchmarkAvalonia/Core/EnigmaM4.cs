namespace EnigmaBenchmark.Core;

/// <summary>
/// Enigma M4 (Kriegsmarine U-Boat "Shark", 1942-02 to 1945). Same as M3 but
/// with a 4th rotor (greek wheel = Beta or Gamma) inserted between the left
/// rotor and the reflector. The greek wheel is STATIC — no stepping — only
/// its Grundstellung + Ringstellung vary.
///
/// Pairings (historically enforced):
///     Beta  ↔ UKW-B dünn
///     Gamma ↔ UKW-C dünn
///
/// Hot path is <see cref="EncryptChar"/> — called ~10^8 times per Quick
/// crack. Keep it allocation-free.
/// </summary>
public struct EnigmaM4
{
    // ── 3-rotor stack (same as M3) ──
    public int WL, WM, WR;       // wheel indices into RotorData.Fwd (0..4)
    public int RL, RM, RR;       // ring settings 0..25
    public int PL, PM, PR;       // grundstellung / current positions 0..25

    // ── 4th (greek) rotor ──
    public int WG;               // 0=Beta, 1=Gamma (indexes RotorData.GreekFwd)
    public int RG;               // ring setting 0..25
    public int PG;               // grundstellung / position 0..25 (NEVER advances)

    // Plugboard + thin reflector
    public byte[] Plugboard;
    public byte[] ThinReflector; // UkwBThin (paired w/ Beta) or UkwCThin (w/ Gamma)

    public static EnigmaM4 Default()
    {
        // A plausible Shark key. Not from a specific historical message —
        // just a valid M4 configuration for the benchmark's default scenario.
        return new EnigmaM4
        {
            WL = 1, WM = 3, WR = 0,           // II IV I
            RL = 0, RM = 0, RR = 0,           // A A A
            PL = 12, PM = 14, PR = 19,        // M O T
            WG = 0, RG = 0, PG = 22,          // Beta, ring A, pos W
            Plugboard = EnigmaM3.IdentityPlugboard(),
            ThinReflector = RotorData.UkwBThin,
        };
    }

    /// <summary>Advance 3-rotor stack one step (greek wheel never moves).</summary>
    public void Step()
    {
        bool midOnNotch   = PM == RotorData.NotchPos[WM];
        bool rightOnNotch = PR == RotorData.NotchPos[WR];

        if (midOnNotch)
        {
            PL = (PL + 1) % 26;
            PM = (PM + 1) % 26;   // double-step anomaly
        }
        else if (rightOnNotch)
        {
            PM = (PM + 1) % 26;
        }
        PR = (PR + 1) % 26;
    }

    /// <summary>Encrypt one character 0-25. Steps first (Enigma convention).</summary>
    public byte EncryptChar(byte c)
    {
        Step();

        c = Plugboard[c];

        // Forward: R → M → L → Greek
        c = (byte)((c + PR - RR + 26) % 26);
        c = RotorData.Fwd[WR][c];
        c = (byte)((c - PR + RR + 26) % 26);

        c = (byte)((c + PM - RM + 26) % 26);
        c = RotorData.Fwd[WM][c];
        c = (byte)((c - PM + RM + 26) % 26);

        c = (byte)((c + PL - RL + 26) % 26);
        c = RotorData.Fwd[WL][c];
        c = (byte)((c - PL + RL + 26) % 26);

        c = (byte)((c + PG - RG + 26) % 26);
        c = RotorData.GreekFwd[WG][c];
        c = (byte)((c - PG + RG + 26) % 26);

        // Thin reflector
        c = ThinReflector[c];

        // Reverse: Greek → L → M → R
        c = (byte)((c + PG - RG + 26) % 26);
        c = RotorData.GreekRev[WG][c];
        c = (byte)((c - PG + RG + 26) % 26);

        c = (byte)((c + PL - RL + 26) % 26);
        c = RotorData.Rev[WL][c];
        c = (byte)((c - PL + RL + 26) % 26);

        c = (byte)((c + PM - RM + 26) % 26);
        c = RotorData.Rev[WM][c];
        c = (byte)((c - PM + RM + 26) % 26);

        c = (byte)((c + PR - RR + 26) % 26);
        c = RotorData.Rev[WR][c];
        c = (byte)((c - PR + RR + 26) % 26);

        return Plugboard[c];
    }

    /// <summary>Transform span in-place (Enigma is reciprocal).</summary>
    public void Transform(Span<byte> buf)
    {
        for (int i = 0; i < buf.Length; i++) buf[i] = EncryptChar(buf[i]);
    }

    /// <summary>Fresh output with a given starting 3-rotor position (greek PG unchanged).</summary>
    public byte[] TransformFresh(byte[] input, int pl, int pm, int pr)
    {
        PL = pl; PM = pm; PR = pr;
        var output = new byte[input.Length];
        for (int i = 0; i < input.Length; i++) output[i] = EncryptChar(input[i]);
        return output;
    }
}
