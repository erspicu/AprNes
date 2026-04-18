namespace EnigmaBenchmark.Core;

/// <summary>
/// Lorenz SZ40 — simplified chi-only variant.
///
/// Real SZ40 has 12 pinwheels (5 χ, 5 ψ, 2 μ). Bletchley's historical attack
/// (Tutte's 1+2 break on Colossus) tackled the χ wheels first via statistics,
/// then ψ, then μ. This benchmark models the first stage only: Z = P ⊕ χ
/// where χ is the 5-bit XOR of the five χ-wheel pins at the current rotation.
/// All five χ wheels step every character.
///
/// Scoring in the cracker uses IC on the 32-symbol Baudot alphabet; with
/// chi-only there's no ψ noise to de-mix, so correct χ gives a clean
/// plaintext histogram.
///
/// Wheel pin counts are historical:  41, 31, 29, 26, 23 (all mutually coprime
/// → keystream period is the product = 22,041,682).
/// </summary>
public struct LorenzSZ40
{
    public static readonly int[] ChiPinCounts = { 41, 31, 29, 26, 23 };

    /// <summary>Pin patterns — one boolean-per-pin per wheel. Known at crack time (= daily Kenngruppenbuch).</summary>
    public byte[][] ChiPins;   // ChiPins[wheel][position] ∈ {0, 1}

    /// <summary>Current positions. Advances every character.</summary>
    public int[] ChiPos;       // length 5

    public static LorenzSZ40 Create(byte[][] pins, int[] startPos)
    {
        if (pins.Length != 5 || startPos.Length != 5)
            throw new ArgumentException("Lorenz SZ40 needs exactly 5 chi wheels");
        return new LorenzSZ40 { ChiPins = pins, ChiPos = (int[])startPos.Clone() };
    }

    /// <summary>Read the 5-bit χ value at current position (XOR of per-wheel pins).</summary>
    public byte CurrentChi()
    {
        byte bit0 = ChiPins[0][ChiPos[0]];
        byte bit1 = ChiPins[1][ChiPos[1]];
        byte bit2 = ChiPins[2][ChiPos[2]];
        byte bit3 = ChiPins[3][ChiPos[3]];
        byte bit4 = ChiPins[4][ChiPos[4]];
        return (byte)(bit0 | (bit1 << 1) | (bit2 << 2) | (bit3 << 3) | (bit4 << 4));
    }

    /// <summary>Advance all 5 chi wheels by one position.</summary>
    public void Step()
    {
        for (int i = 0; i < 5; i++)
        {
            ChiPos[i]++;
            if (ChiPos[i] >= ChiPinCounts[i]) ChiPos[i] = 0;
        }
    }

    /// <summary>Encrypt one Baudot symbol (0-31). Stream cipher, so Encrypt == Decrypt.</summary>
    public byte Crypt(byte c)
    {
        byte chi = CurrentChi();
        byte result = (byte)((c ^ chi) & 0x1F);
        Step();
        return result;
    }

    /// <summary>Transform a buffer with a fresh start position (doesn't mutate caller's pos state).</summary>
    public byte[] TransformFresh(byte[] input, int[] startPos)
    {
        ChiPos = (int[])startPos.Clone();
        var output = new byte[input.Length];
        for (int i = 0; i < input.Length; i++) output[i] = Crypt(input[i]);
        return output;
    }
}
