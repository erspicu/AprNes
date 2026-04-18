namespace EnigmaBenchmark.Core;

/// <summary>
/// Siemens &amp; Halske T52e "Sturgeon" — the final and only fully-secure member
/// of Germany's wartime strategic teleprinter-cipher family. Reverse-engineered
/// from Donald Davies' 1982 NPL technical memorandum (see
/// docs/research-t52e/T52e_TechnicalReport_EN.md for the full specification).
///
/// Pipeline per 5-bit Baudot character:
///
///   B-contacts  → switch permutation → X1..X10
///   X1..X10    → H-relay XOR layer  → H1..H10   (pairwise)
///   H1..H10    → SR-relay XOR layer → SR1..SR10 (each SR = XOR of 4 X's)
///   plaintext  → Vernam XOR with SR6..SR10 → T1..T5
///   T1..T5     → transposition σ(SR1..SR5)   → ciphertext
///   A-contacts → M-magnet logic    → wheel step / hold
///
/// Each of the ten wheels has two cam contacts (A, B) reading the same pin
/// pattern but displaced by about one-third revolution on the cam. The A
/// contact drives stepping, the B contact drives the keystream.
///
/// Pin counts 47, 53, 59, 61, 64, 65, 67, 69, 71, 73 are mutually coprime;
/// full wheel-state cycle is their product ≈ 8.94 × 10¹⁷.
/// </summary>
public struct T52eMachine
{
    public static readonly int[] PinCounts = { 47, 53, 59, 61, 64, 65, 67, 69, 71, 73 };

    /// <summary>
    /// Figure 9, Davies 1982 p. 9. 32 rows (indexed by SR1 + 2·SR2 + 4·SR3 + 8·SR4 + 16·SR5);
    /// each row is the permutation σ(i) for i = 0..4, giving the plaintext-element
    /// index that appears at ciphertext position i. Values 0-based (subtract 1 from
    /// Davies' 1-based table). Row 31 (SR = 11111) is the identity — this is the
    /// "clear" condition of the main control switch.
    ///
    /// These permutations are NOT involutions in general (the paper describes an
    /// electromechanically reciprocal circuit, not a mathematically reciprocal
    /// permutation). Software decryption therefore uses the companion
    /// <see cref="Fig9PermInv"/> table, which is σ⁻¹ of each row.
    /// </summary>
    public static readonly byte[,] Fig9Perm = new byte[32, 5]
    {
        //  SR1 SR2 SR3 SR4 SR5    σ[0] σ[1] σ[2] σ[3] σ[4]   (0-based)
        /*  0 0 0 0 0 */ { 0, 2, 3, 4, 1 },
        /*  1 0 0 0 0 */ { 1, 2, 3, 4, 0 },
        /*  0 1 0 0 0 */ { 4, 2, 3, 0, 1 },
        /*  1 1 0 0 0 */ { 1, 2, 3, 0, 4 },
        /*  0 0 1 0 0 */ { 3, 2, 0, 4, 1 },
        /*  1 0 1 0 0 */ { 1, 2, 0, 4, 3 },
        /*  0 1 1 0 0 */ { 4, 2, 0, 3, 1 },
        /*  1 1 1 0 0 */ { 1, 2, 0, 3, 4 },
        /*  0 0 0 1 0 */ { 2, 0, 3, 4, 1 },
        /*  1 0 0 1 0 */ { 1, 0, 3, 4, 2 },
        /*  0 1 0 1 0 */ { 4, 0, 3, 2, 1 },
        /*  1 1 0 1 0 */ { 1, 0, 3, 2, 4 },
        /*  0 0 1 1 0 */ { 3, 0, 2, 4, 1 },
        /*  1 0 1 1 0 */ { 1, 0, 2, 4, 3 },
        /*  0 1 1 1 0 */ { 4, 0, 2, 3, 1 },
        /*  1 1 1 1 0 */ { 1, 0, 2, 3, 4 },
        /*  0 0 0 0 1 */ { 1, 2, 3, 4, 0 },
        /*  1 0 0 0 1 */ { 0, 2, 3, 4, 1 },
        /*  0 1 0 0 1 */ { 4, 2, 3, 1, 0 },
        /*  1 1 0 0 1 */ { 0, 2, 3, 1, 4 },
        /*  0 0 1 0 1 */ { 3, 2, 1, 4, 0 },
        /*  1 0 1 0 1 */ { 0, 2, 1, 4, 3 },
        /*  0 1 1 0 1 */ { 4, 2, 1, 3, 0 },
        /*  1 1 1 0 1 */ { 0, 2, 1, 3, 4 },
        /*  0 0 0 1 1 */ { 2, 1, 3, 4, 0 },
        /*  1 0 0 1 1 */ { 0, 1, 3, 4, 2 },
        /*  0 1 0 1 1 */ { 4, 1, 3, 2, 0 },
        /*  1 1 0 1 1 */ { 0, 1, 3, 2, 4 },
        /*  0 0 1 1 1 */ { 3, 1, 2, 4, 0 },
        /*  1 0 1 1 1 */ { 0, 1, 2, 4, 3 },
        /*  0 1 1 1 1 */ { 4, 1, 2, 3, 0 },
        /*  1 1 1 1 1 */ { 0, 1, 2, 3, 4 },   // identity
    };

    /// <summary>σ⁻¹ for every row in <see cref="Fig9Perm"/>. Computed at type init.</summary>
    public static readonly byte[,] Fig9PermInv = ComputeInverse(Fig9Perm);

    private static byte[,] ComputeInverse(byte[,] perm)
    {
        var inv = new byte[32, 5];
        for (int r = 0; r < 32; r++)
            for (int i = 0; i < 5; i++)
                inv[r, perm[r, i]] = (byte)i;
        return inv;
    }

    /// <summary>Pin patterns, one 0/1 byte per pin per wheel. [wheel][pin-position].</summary>
    public byte[][] Pins;

    /// <summary>
    /// Switch permutation: SwitchMap[i] ∈ 0..9 gives the B-channel wired into X_i.
    /// Must be a bijection of {0..9}. Part of the daily key.
    /// </summary>
    public int[] SwitchMap;

    /// <summary>Current pin angles. Advances after each character (subject to M-logic).</summary>
    public int[] WheelPos;

    /// <summary>If true, M1/M8/M9/M10 are additionally gated by the previous plaintext bit 3.</summary>
    public bool KtfEnabled;

    /// <summary>State of the RR3 slave relay — bit 3 of the previous plaintext character (0 or 1).</summary>
    public int Rr3;

    /// <summary>B-contact cam offset, in pins, per wheel. Davies: ~1/3 revolution.</summary>
    public int[] BOffset;

    /// <summary>Construct a fresh machine with given pin patterns and switch map.</summary>
    public static T52eMachine Create(byte[][] pins, int[] switchMap, int[] startPos, bool ktf = false)
    {
        if (pins.Length != 10 || switchMap.Length != 10 || startPos.Length != 10)
            throw new ArgumentException("T52e needs exactly 10 wheels");

        var boff = new int[10];
        for (int i = 0; i < 10; i++) boff[i] = PinCounts[i] / 3;   // ~1/3 revolution

        return new T52eMachine
        {
            Pins = pins,
            SwitchMap = (int[])switchMap.Clone(),
            WheelPos = (int[])startPos.Clone(),
            BOffset = boff,
            KtfEnabled = ktf,
            Rr3 = 0,
        };
    }

    /// <summary>Read A-contact value for wheel i (0..9) at current wheel angle.</summary>
    public int ReadA(int i) => Pins[i][WheelPos[i]];

    /// <summary>Read B-contact value for wheel i at current wheel angle (offset by ~1/3 rev).</summary>
    public int ReadB(int i) => Pins[i][(WheelPos[i] + BOffset[i]) % PinCounts[i]];

    /// <summary>Encrypt one Baudot symbol (5 bits) and step the wheels.</summary>
    public byte Encrypt(byte plain) => Process(plain, decrypt: false);

    /// <summary>Decrypt one Baudot symbol (5 bits) and step the wheels.</summary>
    public byte Decrypt(byte cipher) => Process(cipher, decrypt: true);

    /// <summary>
    /// Legacy single-method encryption — kept for backwards compatibility. Prefer
    /// <see cref="Encrypt"/> / <see cref="Decrypt"/> since T52e is physically reciprocal
    /// but not mathematically self-inverse (the Fig 9 permutations are not involutions).
    /// </summary>
    public byte Crypt(byte symbol) => Encrypt(symbol);

    private byte Process(byte symbol, bool decrypt)
    {
        // 1. Read all 10 B-contacts → form raw keystream
        int b0 = ReadB(0), b1 = ReadB(1), b2 = ReadB(2), b3 = ReadB(3), b4 = ReadB(4);
        int b5 = ReadB(5), b6 = ReadB(6), b7 = ReadB(7), b8 = ReadB(8), b9 = ReadB(9);

        // 2. Apply switch permutation: X_i = B[ SwitchMap[i] ]
        Span<int> B = stackalloc int[10] { b0, b1, b2, b3, b4, b5, b6, b7, b8, b9 };
        Span<int> X = stackalloc int[10];
        for (int i = 0; i < 10; i++) X[i] = B[SwitchMap[i]];

        // 3. H-relay XOR layer (Davies Fig 14)
        int H1 = X[0] ^ X[1], H2 = X[2] ^ X[3], H3 = X[4] ^ X[5], H4 = X[6] ^ X[7];
        int H5 = X[8] ^ X[9], H6 = X[0] ^ X[5], H7 = X[1] ^ X[6], H8 = X[2] ^ X[7];
        int H9 = X[3] ^ X[8], H10 = X[4] ^ X[9];

        // 4. SR-relay XOR layer (Davies Figs 13-14)
        int SR1 = H1 ^ H8, SR2 = H6 ^ H7, SR3 = H3 ^ H8, SR4 = H2 ^ H10, SR5 = H4 ^ H10;
        int SR6 = H3 ^ H7, SR7 = H2 ^ H5, SR8 = H1 ^ H9, SR9 = H5 ^ H6, SR10 = H4 ^ H9;

        int row = SR1 | (SR2 << 1) | (SR3 << 2) | (SR4 << 3) | (SR5 << 4);

        // Pipeline:
        //   Encrypt: Plain → Vernam ⊕ (SR6..SR10) → Transpose σ → Cipher
        //   Decrypt: Cipher → Transpose σ⁻¹ → Vernam ⊕ (SR6..SR10) → Plain
        byte output;
        if (!decrypt)
        {
            int t0 = ((symbol >> 0) & 1) ^ SR6;
            int t1 = ((symbol >> 1) & 1) ^ SR7;
            int t2 = ((symbol >> 2) & 1) ^ SR8;
            int t3 = ((symbol >> 3) & 1) ^ SR9;
            int t4 = ((symbol >> 4) & 1) ^ SR10;
            Span<int> T = stackalloc int[5] { t0, t1, t2, t3, t4 };

            int c0 = T[Fig9Perm[row, 0]];
            int c1 = T[Fig9Perm[row, 1]];
            int c2 = T[Fig9Perm[row, 2]];
            int c3 = T[Fig9Perm[row, 3]];
            int c4 = T[Fig9Perm[row, 4]];
            output = (byte)(c0 | (c1 << 1) | (c2 << 2) | (c3 << 3) | (c4 << 4));
        }
        else
        {
            // Inverse transposition first
            int c0 = (symbol >> 0) & 1, c1 = (symbol >> 1) & 1, c2 = (symbol >> 2) & 1;
            int c3 = (symbol >> 3) & 1, c4 = (symbol >> 4) & 1;
            Span<int> C = stackalloc int[5] { c0, c1, c2, c3, c4 };

            int t0 = C[Fig9PermInv[row, 0]];
            int t1 = C[Fig9PermInv[row, 1]];
            int t2 = C[Fig9PermInv[row, 2]];
            int t3 = C[Fig9PermInv[row, 3]];
            int t4 = C[Fig9PermInv[row, 4]];

            int p0 = t0 ^ SR6;
            int p1 = t1 ^ SR7;
            int p2 = t2 ^ SR8;
            int p3 = t3 ^ SR9;
            int p4 = t4 ^ SR10;
            output = (byte)(p0 | (p1 << 1) | (p2 << 2) | (p3 << 3) | (p4 << 4));
        }

        // Step wheels via M-magnet logic (A-contact driven)
        StepWheels();

        // KTF feedback: latch bit 3 of the PLAINTEXT-SIDE symbol for next character.
        // For encrypt, plaintext is the input. For decrypt, plaintext is the output.
        if (KtfEnabled)
            Rr3 = decrypt ? ((output >> 2) & 1) : ((symbol >> 2) & 1);

        return output;
    }

    /// <summary>
    /// Advance wheels via M-magnet logic. M_i = 1 → wheel i is HELD (does not step);
    /// M_i = 0 → wheel i steps one pin. Equations from Davies Fig 11 (see spec §3.5).
    /// </summary>
    private void StepWheels()
    {
        int A1 = ReadA(0), A2 = ReadA(1), A3 = ReadA(2), A4 = ReadA(3), A5 = ReadA(4);
        int A6 = ReadA(5), A7 = ReadA(6), A8 = ReadA(7), A9 = ReadA(8), A10 = ReadA(9);

        int rr3 = KtfEnabled ? Rr3 : 0;

        // Confirmed equations (Davies Fig 11, top half, "Without KTF")
        int M2 = (1 - A10) & (1 - A1);
        int M3 = (1 - A2) & A1;
        int M4 = A2 & A3;
        int M5 = (1 - A4) & (1 - A3);
        int M6 = A4 & A5;

        // Inferred equations (right-cluster tree). See spec §3.5 for the derivation.
        int M7 = A10 & (1 - A6) & (1 - A9);
        int M8 = A10 & (1 - A6) & A9;
        int M1 = A10 & A6 & (1 - A8);
        int M10 = A10 & A6 & A8 & (1 - A7);
        int M9 = A10 & A6 & A8 & A7;

        // KTF gating — for M1, M8, M9, M10, AND with RR3 (or 1-RR3 in half the branches).
        // This is the simplified per-spec: if RR3 = 1, these four can operate; if 0, held operational.
        // Benchmark keeps KTF off, so these lines are no-ops.
        if (KtfEnabled)
        {
            // Simplified KTF modulation — real Fig 11 "With KTF" has more complex branching.
            M1 &= rr3;
            M8 &= rr3;
            M9 &= rr3;
            M10 &= rr3;
        }

        // Step every wheel whose M is 0 (not held)
        if (M1 == 0) AdvanceWheel(0);
        if (M2 == 0) AdvanceWheel(1);
        if (M3 == 0) AdvanceWheel(2);
        if (M4 == 0) AdvanceWheel(3);
        if (M5 == 0) AdvanceWheel(4);
        if (M6 == 0) AdvanceWheel(5);
        if (M7 == 0) AdvanceWheel(6);
        if (M8 == 0) AdvanceWheel(7);
        if (M9 == 0) AdvanceWheel(8);
        if (M10 == 0) AdvanceWheel(9);
    }

    private void AdvanceWheel(int i)
    {
        WheelPos[i]++;
        if (WheelPos[i] >= PinCounts[i]) WheelPos[i] = 0;
    }

    /// <summary>
    /// Encrypt a full buffer with a fresh start position (doesn't mutate
    /// caller's pos array).
    /// </summary>
    public byte[] EncryptFresh(byte[] plaintext, int[] startPos)
    {
        WheelPos = (int[])startPos.Clone();
        Rr3 = 0;
        var output = new byte[plaintext.Length];
        for (int i = 0; i < plaintext.Length; i++) output[i] = Encrypt(plaintext[i]);
        return output;
    }

    /// <summary>
    /// Decrypt a full buffer with a fresh start position (doesn't mutate
    /// caller's pos array). Inverse of EncryptFresh when given the same key.
    /// </summary>
    public byte[] DecryptFresh(byte[] ciphertext, int[] startPos)
    {
        WheelPos = (int[])startPos.Clone();
        Rr3 = 0;
        var output = new byte[ciphertext.Length];
        for (int i = 0; i < ciphertext.Length; i++) output[i] = Decrypt(ciphertext[i]);
        return output;
    }

    /// <summary>Legacy alias for EncryptFresh — kept for code that called the old API.</summary>
    public byte[] TransformFresh(byte[] input, int[] startPos) => EncryptFresh(input, startPos);

    /// <summary>Generate a pseudo-random but deterministic cam pattern for a wheel. Used for tests.</summary>
    public static byte[] GeneratePins(int count, int seed)
    {
        var rng = new Random(seed);
        var p = new byte[count];
        for (int i = 0; i < count; i++) p[i] = (byte)(rng.Next() & 1);
        // Guard against all-0 or all-1 which would produce a degenerate cipher
        if (Array.TrueForAll(p, b => b == 0)) p[0] = 1;
        if (Array.TrueForAll(p, b => b == 1)) p[0] = 0;
        return p;
    }

    /// <summary>Generate a test pin set: deterministic PRNG seeded per wheel.</summary>
    public static byte[][] GenerateAllPins(int seedBase = 42)
    {
        var pins = new byte[10][];
        for (int i = 0; i < 10; i++)
            pins[i] = GeneratePins(PinCounts[i], seedBase * 100 + i);
        return pins;
    }
}
