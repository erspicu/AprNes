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
}
