namespace EnigmaBenchmark.Presets;

using EnigmaBenchmark.Core;

/// <summary>
/// Hardcoded preset: the BDU "Wolfpack NORDWIND" fictional strategic order.
/// Corresponds to:
///   sample_scenario_default.txt    (plaintext + translations)
///   enigma_config_default.txt      (rotor/plugboard key)
/// </summary>
public static class DefaultScenario
{
    public const string PlaintextFormatted =
        "AN ALLE BOOTE GRUPPE NORDWIND STOP " +
        "KURS NEUNUNDZWANZIG GRAD NORD DREIUNDDREISSIG GRAD WEST AENDERN STOP " +
        "ANGRIFFSZIEL GELEITZUG OG VIERUNDZWANZIG VORAUSSICHTLICH EINUNDZWANZIG UHR MEZ X " +
        "FEINDLICHE ZERSTOERER BEGLEITEN FRACHTER SECHZEHN STUECK STOP " +
        "UEBERSCHWERE WETTERLAGE ERWARTET STOP " +
        "FUNKSTILLE BIS ANGRIFF STOP " +
        "BDU FUEHRUNG";

    public static byte[] PlaintextBytes => EnigmaM3.EncodeString(PlaintextFormatted);

    public const string PlugboardPairs = "AP BR CM FZ GK IV JU LS NT OX";

    /// <summary>The "ground truth" Enigma setting — what the cracker must discover.</summary>
    public static EnigmaM3 TrueKey()
    {
        return new EnigmaM3
        {
            WL = 2, WM = 3, WR = 0,     // III, IV, I
            RL = 23, RM = 12, RR = 21,  // X, M, V
            PL = 16, PM = 22, PR = 4,   // Q, W, E
            Plugboard = EnigmaM3.ParsePlugboard(PlugboardPairs),
            Reflector = RotorData.UkwB,
        };
    }

    // ──────────────────────────────────────────────────────────────────────
    //  M4 U-Boot "Shark" scenario
    // ──────────────────────────────────────────────────────────────────────
    //
    // Fictional radio message from BdU (Befehlshaber der U-Boote) to a
    // wolfpack in the North Atlantic, Feb 1943 style. Umlauts transliterated
    // per Kriegsmarine convention (AE/OE/UE/SS).

    public const string M4PlaintextFormatted =
        "U BOOT GRUPPE LOEWENHERZ X " +
        "AN U EINHUNDERTSIEBENUNDSECHZIG X " +
        "FEINDLICHER KONVOI HX DREIHUNDERTZWANZIG " +
        "POSITION ACHTUNDFUENFZIG NORD ZWEIUNDZWANZIG WEST " +
        "KURS ZERO NEUN ZERO GESCHWINDIGKEIT NEUN KNOTEN X " +
        "ANGRIFF BEI EINBRUCH DER DUNKELHEIT " +
        "DREIZWANZIG UHR X " +
        "BESTAETIGEN DURCH KURZSIGNAL DELTA X " +
        "BDU HEIL HITLER";

    public static byte[] M4PlaintextBytes => EnigmaM3.EncodeString(M4PlaintextFormatted);

    // Plugboard 10 pairs — kriegsmarine-style selection
    public const string M4PlugboardPairs = "BQ CR DI EJ KW MT OS PX UZ GH";

    /// <summary>Ground-truth M4 key — what the M4 cracker has to recover.</summary>
    public static EnigmaM4 M4TrueKey()
    {
        return new EnigmaM4
        {
            WL = 1, WM = 3, WR = 0,           // II IV I
            RL = 0, RM = 0, RR = 0,           // A A A
            PL = 12, PM = 14, PR = 19,        // M O T  (the Bletchley-famous "MOT" indicator)
            WG = 0, RG = 0, PG = 22,          // Beta + ring A + pos W
            Plugboard = EnigmaM3.ParsePlugboard(M4PlugboardPairs),
            ThinReflector = RotorData.UkwBThin,   // must pair w/ Beta
        };
    }
}
