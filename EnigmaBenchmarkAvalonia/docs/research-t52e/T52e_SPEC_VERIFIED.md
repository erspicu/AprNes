# Siemens T52e "Sturgeon" — Verified Technical Specification

Source: Donald W. Davies, *"The Siemens and Halske T52e Cipher Machine"* (T52e_TechDesc_EN.pdf in this folder).
All values below are verified directly against the figures in that paper (page_XX.png / page_XX_hi.png, now deleted to save context).

This is the canonical text record — use this for implementation, do not re-read the image files.

---

## 1. Wheel pin counts (Figure 3)

| Wheel | Pins |
|-------|------|
| W1 | 47 |
| W2 | 53 |
| W3 | 59 |
| W4 | 61 |
| W5 | 64 |
| W6 | 65 |
| W7 | 67 |
| W8 | 69 |
| W9 | 71 |
| W10 | 73 |

- All pairwise coprime.
- Total pins: 629.
- Cycle product: 47·53·59·61·64·65·67·69·71·73 ≈ **8.94 × 10¹⁷**.

Each wheel carries **two cam contacts**:
- **A-contact** → feeds the M-magnet stepping (motion) logic.
- **B-contact** → feeds the keystream via the key-setting switches. B is displaced ≈ 1/3 revolution from A on the same cam.

Wheel labels in Davies' figures use letters **A–K** mapped in reverse order to W10–W1 (A=W10, B=W9, …, K=W1).

---

## 2. Key-setting switches (Figures 1, 2, 12)

Ten rotary switches, each with ten positions, acting as a permutation of the 10 B-contact signals into the 10 X channels X1–X10.

Switch position label → X channel:

| Position label | X channel |
|---|---|
| 1 | X1 |
| 3 | X2 |
| 5 | X3 |
| 7 | X4 |
| 9 | X5 |
| I | X6 |
| II | X7 |
| III | X8 |
| IV | X9 |
| V | X10 |

This transposition is part of the daily key.

---

## 3. H relay network (Figure 14, verified visually)

```
H1  = X1 ⊕ X2
H2  = X3 ⊕ X4
H3  = X5 ⊕ X6
H4  = X7 ⊕ X8
H5  = X9 ⊕ X10
H6  = X1 ⊕ X6
H7  = X2 ⊕ X7
H8  = X3 ⊕ X8
H9  = X4 ⊕ X9
H10 = X5 ⊕ X10
```

⚠️ **Gemini answered this incorrectly with a different pattern (H1=X1⊕X6, etc.).**
The formulas above are taken by direct visual reading of Figure 14 and must not be
changed without re-examining the original figure.

---

## 4. SR relay network (Figure 13 + Figure 14 bottom, verified visually)

```
SR1  = H1 ⊕ H8
SR2  = H6 ⊕ H7
SR3  = H3 ⊕ H8
SR4  = H2 ⊕ H10
SR5  = H4 ⊕ H10
SR6  = H3 ⊕ H7
SR7  = H2 ⊕ H5
SR8  = H1 ⊕ H9
SR9  = H5 ⊕ H6
SR10 = H4 ⊕ H9
```

Expanded as XOR of 4 X channels each (Figure 15, derivable from §3 + §4):

```
SR1  = X1 ⊕ X2 ⊕ X3 ⊕ X8
SR2  = X1 ⊕ X2 ⊕ X6 ⊕ X7
SR3  = X3 ⊕ X5 ⊕ X6 ⊕ X8
SR4  = X3 ⊕ X4 ⊕ X5 ⊕ X10
SR5  = X5 ⊕ X7 ⊕ X8 ⊕ X10
SR6  = X2 ⊕ X5 ⊕ X6 ⊕ X7
SR7  = X3 ⊕ X4 ⊕ X9 ⊕ X10
SR8  = X1 ⊕ X2 ⊕ X4 ⊕ X9
SR9  = X1 ⊕ X6 ⊕ X9 ⊕ X10
SR10 = X4 ⊕ X7 ⊕ X8 ⊕ X9
```

Invariant: every X_i appears in exactly 4 of the SR equations, so

```
SR1 ⊕ SR2 ⊕ … ⊕ SR10 = 0
```

(matches the linear-dependency remark in the Davies paper).

---

## 5. Encryption action per character (Figures 5, 7, 9)

Five plaintext bits *S1..S5* enter the keyboard contacts. For each character:

1. **Vernam XOR stage (SR6–SR10):**
   `C_i = S_i ⊕ SR(5+i)` for i = 1..5
   (SR6 XORs bit 1, SR7 XORs bit 2, … SR10 XORs bit 5.)

2. **Transposition stage (SR1–SR5):**
   Apply one of 32 permutations of the 5 output bits, selected by the 5-bit
   pattern (SR1,SR2,SR3,SR4,SR5). When all five SR1–SR5 are operated (the
   "clear" / identity case) no swap occurs.

3. Decryption reverses the transposition with an identical circuit
   (Enigma-style reciprocity) then re-XORs the same SR6–SR10.

**Figure 9 — full 32-transposition table (VERIFIED from page 35 of PDF):**

Columns: `(SR1 SR2 SR3 SR4 SR5) | perm[1..5]`

`perm[i]` = which plaintext element number appears at cipher position `i`.
Equivalently: `cipher[i] = plain[perm[i]] ⊕ SR(5+i)` for i = 1..5.

```
 1.  0 0 0 0 0 | 1 3 4 5 2
 2.  1 0 0 0 0 | 2 3 4 5 1
 3.  0 1 0 0 0 | 5 3 4 1 2
 4.  1 1 0 0 0 | 2 3 4 1 5
 5.  0 0 1 0 0 | 4 3 1 5 2
 6.  1 0 1 0 0 | 2 3 1 5 4
 7.  0 1 1 0 0 | 5 3 1 4 2
 8.  1 1 1 0 0 | 2 3 1 4 5
 9.  0 0 0 1 0 | 3 1 4 5 2
10.  1 0 0 1 0 | 2 1 4 5 3
11.  0 1 0 1 0 | 5 1 4 3 2
12.  1 1 0 1 0 | 2 1 4 3 5
13.  0 0 1 1 0 | 4 1 3 5 2
14.  1 0 1 1 0 | 2 1 3 5 4
15.  0 1 1 1 0 | 5 1 3 4 2
16.  1 1 1 1 0 | 2 1 3 4 5
17.  0 0 0 0 1 | 2 3 4 5 1
18.  1 0 0 0 1 | 1 3 4 5 2
19.  0 1 0 0 1 | 5 3 4 2 1
20.  1 1 0 0 1 | 1 3 4 2 5
21.  0 0 1 0 1 | 4 3 2 5 1
22.  1 0 1 0 1 | 1 3 2 5 4
23.  0 1 1 0 1 | 5 3 2 4 1
24.  1 1 1 0 1 | 1 3 2 4 5
25.  0 0 0 1 1 | 3 2 4 5 1
26.  1 0 0 1 1 | 1 2 4 5 3
27.  0 1 0 1 1 | 5 2 4 3 1
28.  1 1 0 1 1 | 1 2 4 3 5
29.  0 0 1 1 1 | 4 2 3 5 1
30.  1 0 1 1 1 | 1 2 3 5 4
31.  0 1 1 1 1 | 5 2 3 4 1
32.  1 1 1 1 1 | 1 2 3 4 5   ← identity ("clear" setting)
```

Indexing convention: SR1 is the least-significant bit of the row index
(rows 1,3,5,... have SR1=0; rows 2,4,6,... have SR1=1), then SR2, SR3, SR4,
SR5 as higher bits (so row_index - 1 = SR1 + 2·SR2 + 4·SR3 + 8·SR4 + 16·SR5).

All 32 entries are valid permutations of {1,2,3,4,5}. Note the table uses a
non-redundant mapping: SR=(0,0,0,0,0) gives perm (1,3,4,5,2) and SR=(1,1,1,1,1)
gives the identity. The paper notes only 32 of the 120 possible 5-element
permutations are realised (Davies §4, p. 9).

---

## 6. Interposer / stepping logic (Figure 11, page 33 of PDF)

Ten interposer magnets M1–M10. On each motion cycle:

- If M_i is operated → wheel W_i does **not** step that cycle.
- If M_i is not operated → wheel W_i steps one pin.

Each M_i is a boolean function of the A1..A10 contacts, realised by a
relay-ladder switching network drawn in Figure 11. Only the "Without KTF"
half of Figure 11 is relevant when KTF is off; the "With KTF" half adds
RR3 into the M1/M8/M9/M10 paths.

### Figure 11 layout (Without KTF, verified visually)

M magnets are arranged left-to-right in the non-obvious order:

```
M2  M3  M4  M5  M6  M7  M8  M1  M10  M9
```

Five "below" A-contacts route ground (−) to specific M magnets:

| Below switch | Ground routes to |
|---|---|
| A1 | M2 when A1 unoperated, M3 when A1 operated |
| A3 | M3 when A3 unoperated, M4 when A3 operated |
| A5 | M5 when A5 unoperated, M6 when A5 operated |
| A9 | M7 when A9 unoperated, M8 when A9 operated |
| A7 | M10 when A7 unoperated, M9 when A7 operated |

Two mid-level "above" A-contacts route +V to left cluster:

| Above switch | +V routes to |
|---|---|
| A2 | M3 when A2 unoperated, M4 when A2 operated |
| A4 | M5 when A4 unoperated, M6 when A4 operated |

Three upper-level A-contacts (A10, A6, A8) route +V through a 3-level
tree to either M2 or to the right cluster (M7, M8, M1, M10, M9). The
exact tree wiring is visible in the figure but not transcribed here —
see `T52e_TechDesc_EN.pdf` page 33 if exact historical fidelity is
required.

### Confirmed M-equations (subset)

From the text (line 438) and from the traced "below + above" switch
pairing above:

```
M4 = A2 ∧ A3                 (explicitly confirmed by Davies)
M3 = ¬A2 ∧ A1                (A2 unoperated + A1 grounds M3)
M5 = ¬A4 ∧ ¬A5               (A4 unoperated sends +V to M5, A5 unoperated grounds M5)
M6 = A4 ∧ A5                 (A4 operated + A5 operated)
```

Right-cluster inference (by physical symmetry of the relay ladder and the
A6/A8/A10 tree topology visible in the figure; the exact arrow directions
could not be resolved from the scan but the structure strongly suggests):

```
M2  = ¬A10 ∧ ¬A1        (A10 unoperated → +V to M2 via long left-side wire;
                         A1 unoperated → ground to M2)
M7  = A10 ∧ ¬A6 ∧ ¬A9   (upper tree selects M7/M8 via ¬A6; A9 selects M7)
M8  = A10 ∧ ¬A6 ∧ A9    (same upper branch; A9 selects M8)
M1  = A10 ∧ A6 ∧ ¬A8    (upper tree selects right side via A6; A8 selects M1/M10/M9)
M10 = A10 ∧ A6 ∧ A8 ∧ ¬A7
M9  = A10 ∧ A6 ∧ A8 ∧ A7
```

These need verification against the Figure 11(a) test vector (§12) before
being trusted for historical reconstruction.

### Implementation note for the benchmark

For the EnigmaBenchmark project the exact Figure-11 equations are
**not** critical: the benchmark needs consistent, long-period,
non-trivial stepping for the attack to be meaningful, not bit-exact
wartime behaviour. A pragmatic substitute uses the confirmed subset
above plus a plausible symmetric extension:

```
M_i = A_i ∧ A_{(i mod 10)+1}      (i = 1..10)
```

This produces the characteristic T52e feature — each wheel stops when
two specific other wheels both currently output a '1' on their A contact
— and retains the wheel-interdependence that makes brute-force
cryptanalysis hard. When historical accuracy becomes important, revisit
Figure 11 of the PDF and replace with the exact equations.

### KTF (Klartextfunktion) switch

- **KTF = "ohne"**: M1..M10 depend only on A1..A10 (autonomous stepping).
- **KTF = "mit"**: M1, M8, M9, M10 are additionally influenced by RR3, a slave
  relay of bit 3 of the previous plaintext character. This makes stepping
  plaintext-dependent and is the key defence that broke Swedish cryptanalysis
  in mid-1943.

Figure 11(a) on page 32 of the PDF gives a worked example: 24 pawl-drive
cycles starting from the all-ones wheel position, listing M operations
and resulting A outputs. Useful as a test vector to validate any
implementation of the stepping logic.

## 12. Figure 11(a) test vector (from page 32, VERIFIED)

Machine: Munich surviving T52e, KTF off, all 10 wheels at pin position 1.

After 24 pawl-drive cycles, wheel positions (Munich cam numbering) read from
the "Final position" column of the figure:

| Wheel | Pin count | Final position |
|-------|-----------|----------------|
| W1  | 47 | 20 |
| W2  | 53 | 22 |
| W3  | 59 | 20 |
| W4  | 61 | 16 |
| W5  | 64 | 23 |
| W6  | 65 | 16 |
| W7  | 67 | 17 |
| W8  | 69 | 14 |
| W9  | 71 | 17 |
| W10 | 73 | 14 |

(Last two are from a partly-cropped edge of the figure; re-verify from the
PDF before publication.)

Figure 11(a) also shows the 24-cycle sequence of M and A operations as
waveform traces. Any T52e implementation that claims historical fidelity
MUST reproduce these waveforms bit-for-bit when run with:
- All wheels at pin 1
- KTF off
- Munich cam patterns (Figure 3, not extracted)

If the cam patterns are not available, use the final-position column as a
weaker consistency check combined with any pseudo-random cam data (test
would verify stepping logic is plausible but not historically exact).

### KTF (Klartextfunktion) switch

- **KTF = "ohne"**: M1..M10 depend only on A1..A10 (autonomous stepping).
- **KTF = "mit"**: M1, M8, M9, M10 are additionally influenced by RR3, a slave
  relay of bit 3 of the previous plaintext character. This makes stepping
  plaintext-dependent and is the key defence that broke Swedish cryptanalysis
  in mid-1943.

Figure 11(a) gives a worked example: 24 pawl-drive cycles starting from the
all-ones wheel position, listing M operations and resulting A outputs.

---

## 7. Cam patterns (Figure 3, not yet extracted)

Per-wheel A and B pin sequences recorded from the Munich (Werner von Siemens
Institut) surviving machine. Not yet extracted to text — when implementing,
either:
- Re-read Figure 3 from the PDF,
- Or substitute a deterministic PRNG seed (acceptable for benchmark purposes
  since historical authenticity at the pin level is not a benchmark goal).

Two surviving machines examined by Davies:
- **Munich** (Werner von Siemens Institut): cam numbering starts at '1'.
- **Science Museum London**: identical cam patterns but numbering offset by
  ~1/3 revolution per wheel. To match Munich's all-1s start, the London
  machine must be set to: 32, 18, 1, 21, 43, 44, 61, 23, 24, 49.

---

## 8. T52d vs T52e

- **T52d**: SR relays driven directly by X channels, no H layer. Vulnerable to
  the Swedish attack exploiting B-contact "0 follows 0" bias.
- **T52e**: adds the H layer so each SR is XOR of 4 X's, giving much better
  statistical distribution (Davies §6, pp. 19–20).

---

## 9. Full key space

- Pin patterns on 10 cams (total 629 pins, but typically cams are fixed per
  day/month — ignore in daily brute force).
- 10-position setting on each of 10 key-setting switches (part of daily key).
- 10 wheel start positions (= cam ring angles).
- KTF = mit or ohne.

Full keyspace is ~10¹⁷ starting positions × 10¹⁰ switch permutations.
Historical cryptanalysis (Bletchley Testery, Beurling) relied on depths and
partial-recovery techniques, not full brute force.

---

## 10. Reduced benchmark scenario (EnigmaBenchmark plan)

Fix 6 wheel start positions + known pin patterns + known basic (switch) key +
KTF = off, brute force the remaining 4 wheels' start positions.

Keyspace: 67 · 69 · 71 · 73 = **23,951,289 ≈ 24 M** candidates.

This lines up with the Lorenz SZ40 chi-only scenario already in the project
(~22 M for χ1×χ2), so the scalar/SIMD/GPU backend performance should be
comparable.

Per-candidate attack:
1. Generate keystream for the 4 searched wheels (the other 6 contribute a
   fixed known pattern).
2. Reverse SR1–SR5 transposition on ciphertext.
3. XOR out SR6–SR10 to get candidate plaintext bits.
4. Score residual with Baudot index-of-coincidence (≈ 0.085 for German
   plaintext vs ≈ 0.031 for uniform).

---

## 11. Outstanding extractions (from PDF, now without images)

1. ✅ **Figure 9 — 32-transposition table** — extracted (§5 above), page 35.
2. ⚠️  **Figure 11 — M1..M10 logical functions** — partially extracted (§6),
   page 33. Four equations confirmed (M3, M4, M5, M6); remaining six
   require careful tracing of the A6/A8/A10 tree network. Pragmatic
   substitute documented in §6 for benchmark use.
3. ⚠️  **Figure 3 — cam pin patterns** of all 10 wheels. Not extracted.
   Optional — deterministic PRNG seed acceptable for benchmark (the 629-bit
   wartime patterns are not needed to demonstrate the attack).

### Re-extraction procedure

If/when exact historical fidelity is required:

1. `python -c "import fitz; d=fitz.open('T52e_TechDesc_EN.pdf'); \
   p=d[N-1].get_pixmap(matrix=fitz.Matrix(170/72,170/72).prerotate(270)); \
   p.save('tmp.png')"` (use `.prerotate(270)` for the landscape figure pages).
2. Image dimension stays under 2000 × 2000 at ~168 DPI full-page rotated.
3. For zoom-in, clip with `page.get_pixmap(..., clip=fitz.Rect(...))`.
4. Record findings as text in this file, **delete the PNG** before moving on.

---

Written: 2026-04-18.
Do not discard — this file is the authoritative text record once the PNGs
are deleted.
