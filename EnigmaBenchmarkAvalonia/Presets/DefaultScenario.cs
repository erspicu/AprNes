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

    // ──────────────────────────────────────────────────────────────────────
    //  Lorenz SZ42 "Tunny" scenario — Wolfsschanze → OKW, Dec 1944 style.
    //
    //  Fictional. The Ardennes offensive (Wacht am Rhein / Battle of the
    //  Bulge) was exactly the kind of strategic order Lorenz actually
    //  carried, and Bletchley's Tunny section was reading its traffic
    //  by late 1944.
    //
    //  ── Historical accuracy note ──────────────────────────────────────────
    //  Real SZ42b has 12 pinwheels (5 χ + 5 ψ + 2 μ motor) and encrypts via
    //       Z = P ⊕ χ ⊕ ψ
    //  with ψ stepping conditioned on μ. Bletchley's attack proceeded in 3
    //  stages:
    //    1. χ-wheel start recovery  (Colossus, ~1 hr per message)
    //    2. χ pattern deduction     (hand work, Testery)
    //    3. ψ/μ recovery            (hand work, Testery)
    //
    //  Our scenario implements stage-1 only: the cipher used here is the
    //  reduced Z = P ⊕ χ form. Attacking full SZ42b over the 22 M χ keyspace
    //  doesn't work as a single brute force because the historical Δ-1+2
    //  statistic collapses the effective keyspace to ~900 (only χ1 and χ2
    //  participate in the signal) — that doesn't exercise the GPU at all.
    //  So we model what Colossus actually *did* at Stage 1: 22 M χ starts,
    //  IC-style statistical scoring, full-text CPU verification. The
    //  benchmark's relative-speed story (GPU vs CPU backends) is valid;
    //  just know that the machine simulation here is Colossus-stage-1 level,
    //  not the full Tunny machine.
    // ──────────────────────────────────────────────────────────────────────

    public const string LorenzPlaintextFormatted =
        "AN GENERALFELDMARSCHALL KEITEL OBERKOMMANDO DER WEHRMACHT BERLIN " +
        "VON FUEHRERHAUPTQUARTIER WOLFSCHANZE OSTPREUSSEN " +
        "ZEITANGABE SECHZEHN EINHUNDERTZWANZIG UHR MITTELEUROPAEISCHER ZEIT STOP " +
        "DER FUEHRER HAT BEFOHLEN DIE UNTERNEHMUNG WACHT AM RHEIN " +
        "BEGINNT AM SECHZEHNTEN DEZEMBER NEUNZEHNHUNDERTVIERUNDVIERZIG " +
        "MIT OFFENSIVE DURCH DIE ARDENNEN STOP " +
        "HEERESGRUPPE B UNTER GENERALFELDMARSCHALL MODEL WIRD DIE " +
        "SECHSTE PANZERARMEE UND DIE FUENFTE PANZERARMEE KOMMANDIEREN " +
        "ZIEL ANTWERPEN STOP LUFTWAFFE UNTERSTUETZUNG DURCH VERBAENDE " +
        "DER SIEBENTEN JAGDDIVISION STOP " +
        "ABSOLUTE GEHEIMHALTUNG IST BEFEHL HEIL HITLER";

    public static byte[] LorenzPlaintextBytes => Baudot.Encode(LorenzPlaintextFormatted);

    /// <summary>Deterministic "Kenngruppenbuch" for the scenario — pin patterns per wheel.</summary>
    public static byte[][] LorenzChiPins()
    {
        // Stable PRNG seed so the scenario is reproducible run-to-run.
        var rng = new Random(unchecked((int)0xDEADBEEF));
        var pins = new byte[5][];
        for (int w = 0; w < 5; w++)
        {
            int len = LorenzSZ40.ChiPinCounts[w];
            pins[w] = new byte[len];
            // Balanced pin distribution (roughly half 1s, half 0s) keeps the
            // chi keystream close to uniformly random so scoring works.
            for (int i = 0; i < len; i++) pins[w][i] = (byte)(rng.Next(2));
        }
        return pins;
    }

    /// <summary>Ground-truth chi start positions for the Lorenz scenario.</summary>
    public static readonly int[] LorenzChiStart = { 17, 11, 23, 5, 19 };

    // ──────────────────────────────────────────────────────────────────────
    //  Siemens T52e "Sturgeon" scenario — Luftflotte 2 → OKL, 1944
    //
    //  Fictional but period-plausible Luftwaffe operational order. T52e was
    //  the actual Sturgeon variant that Bletchley's Testery worked against
    //  late in the war; typical traffic was high-level air force command.
    //
    //  Reduced-keyspace attack (matches paper §10 of T52e_TechnicalReport):
    //    - Pin patterns + switch map known (from prior analysis/capture)
    //    - Wheels W1..W6 starts known (from prior Testery depth work)
    //    - Brute-force W7..W10 starts:  67×69×71×73 ≈ 23.95 M candidates
    // ──────────────────────────────────────────────────────────────────────

    public const string T52ePlaintextFormatted =
        "AN LUFTWAFFENGRUPPE WEST GENERAL SPERRLE " +
        "VON OBERKOMMANDO DER LUFTWAFFE BERLIN " +
        "ZEITANGABE EINUNDZWANZIG EINHUNDERT UHR MEZ STOP " +
        "FEINDLICHE LANDUNGSFLOTTE IM AERMELKANAL GESICHTET " +
        "SCHWERPUNKT SUEDOESTLICH WIGHT ENTFERNUNG ACHTZIG SEEMEILEN STOP " +
        "SOFORTIGE AUFKLAERUNG DURCH FERNAUFKLAERER " +
        "GESCHWADER EINS UND DREI BEFOHLEN STOP " +
        "ANGRIFFSVORBEREITUNG SAEMTLICHER BOMBERGESCHWADER " +
        "LUFTFLOTTE ZWEI UND DREI SOFORT EINLEITEN " +
        "KAMPFGRUPPEN STEHEN BEREIT STOP " +
        "FIGHTER COMMAND REAKTION DURCH JAGDFLIEGER ABFANGEN " +
        "JAGDGESCHWADER ZWANZIG EINUNDZWANZIG UND ZWEIUNDZWANZIG " +
        "VOLLER EINSATZ STOP HEIL HITLER";

    public static byte[] T52ePlaintextBytes => Baudot.Encode(T52ePlaintextFormatted);

    /// <summary>Deterministic pin patterns seeded reproducibly per wheel.</summary>
    public static byte[][] T52ePins() => T52eMachine.GenerateAllPins(seedBase: 0x5A);

    /// <summary>Daily-key switch map: bijective permutation of {0..9}.</summary>
    public static readonly int[] T52eSwitchMap = { 3, 7, 0, 5, 9, 2, 8, 1, 4, 6 };

    /// <summary>Ground-truth wheel starts; W7..W10 are the "unknown" four.</summary>
    public static readonly int[] T52eWheelStart = { 10, 20, 30, 40, 50, 55, 17, 0, 0, 0 };
}
