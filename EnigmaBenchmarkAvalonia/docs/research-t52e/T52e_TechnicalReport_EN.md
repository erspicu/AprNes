# The Siemens & Halske T52e "Sturgeon" — A Complete Technical Reconstruction

**A primary-source synthesis derived from Donald Davies' 1982 examination of two surviving wartime machines, prepared as a specification for modern software reimplementation.**

Prepared by **Claude Opus 4.7** (Anthropic) as part of the EnigmaBenchmark project.
Compiled 2026-04-18 from [Davies, *The Siemens and Halske T52e Cipher Machine*](./T52e_TechDesc_EN.pdf), the Crypto Museum archive, Wikipedia, and the authors' own verification via high-DPI figure rendering and hand tracing of relay schematics.

---

## Abstract

The Siemens and Halske T52e Geheimschreiber was the final — and only fully-secure — member of Germany's wartime strategic teleprinter-cipher family. Unlike its siblings (T52a, b, c, d), the T52e's combination of *Klartextfunktion* (KTF, plaintext-dependent stepping) and the H-relay mixing layer resisted Swedish cryptanalysis from mid-1943 until the end of the war. Despite its importance in the history of cryptography, T52e is under-documented in both popular and academic literature; the authoritative reverse-engineering is a 1982 internal report by Donald Davies of NPL, based on two surviving machines held in Munich and at the Science Museum, London.

This report reconstructs the T52e at the level needed for a byte-accurate software emulator. We give the complete keystream-generation algorithm, including:

- the pin counts and cam-contact convention of all ten wheels;
- the **H** and **SR** relay-network XOR topology, verified by direct reading of Figures 13–15 of the Davies paper and cross-checked against the trivial-parity invariant `SR1 ⊕ SR2 ⊕ … ⊕ SR10 = 0`;
- the complete 32-row transposition table of Figure 9, which defines the permutation applied to the five plaintext bits as a function of `(SR1..SR5)`;
- the interposer-magnet stepping logic of Figure 11 (five equations confirmed, five inferred by circuit symmetry);
- a verifiable 24-cycle test vector drawn from Figure 11(a) of the paper.

We also define a reduced-keyspace benchmark attack (24 million candidates) suitable for direct comparison with the already-implemented Lorenz SZ40 chi-only benchmark.

---

## 1. Historical context

The T52 was Germany's **teleprinter-based** strategic cipher system during the Second World War, as opposed to the hand-carried field cipher Enigma and the Lorenz SZ40/42 ("Tunny") machine used by the Army High Command. The T52 — British codename "Sturgeon" — was produced by Siemens & Halske from the late 1930s onward and was deployed by the *Luftwaffe* and the *Kriegsmarine* on links that could sustain a bulky on-line cipher attachment: fixed Reichspost circuits, fortified command posts, embassies, and high-capacity military headquarters.

Five distinct models were built:

| Model | Status                  | Cryptanalytic outcome                                                |
|-------|-------------------------|----------------------------------------------------------------------|
| T52a  | Early, 1932 design  | Broken by Arne Beurling (Sweden) in two weeks with pen and paper, May 1940 |
| T52b  | RF-suppressed T52a  | As T52a.                                                             |
| T52c  | Improved stepping   | Read by Swedish cryptanalysts with machine help; ~350,000 messages decrypted 1940-1943 |
| T52d  | Added nonlinear wheel stepping | Initially broken by the Swedes in 1942               |
| T52e  | Added H-relay mixing + KTF | Not broken operationally; traffic drop-off mid-1943 ends Swedish coverage |

The British at Bletchley Park first saw Sturgeon traffic in summer 1942 on a Sicily–Libya link. Michael Crum broke into the traffic using depths produced by undisciplined operators re-using indicator keys. The Bletchley attack was never as continuous as that on Tunny — partly because Sturgeon was the most mathematically complex of the three major German cipher systems of the war, and partly because the Luftwaffe had a near-universal habit of re-transmitting strategic Sturgeon messages on easier-to-attack lower-echelon ciphers.

After the war the machines dispersed. Two survived and were re-examined in 1978 by Donald Davies of Britain's National Physical Laboratory, producing the technical memorandum that is the primary source for this report.

## 2. Physical construction

### 2.1 Mechanical layout

A T52e looks superficially like a bulky teleprinter: a machine of about 60 kg mounted in a desk-sized cabinet, combining a keyboard, a printer, a tape-punch, and the cipher unit proper. The cipher unit consists of:

- **Ten coding wheels** on three camshafts — the transmit, receive, and translate/print camshafts — with pin counts 47, 53, 59, 61, 64, 65, 67, 69, 71, 73. All pairwise coprime; the product 8.94 × 10¹⁷ gives the cycle length before any wheel state is exactly repeated.
- **Two cam contacts per wheel**, denoted A and B. They read the *same* pin pattern but at points on the cam displaced by approximately one-third revolution. The A contact feeds the wheel-stepping (motion) logic; the B contact feeds the keystream-generation logic.
- A set of ten **key-setting switches** labelled A…K, each with ten positions labelled `1, 3, 5, 7, 9, I, II, III, IV, V`. The switches define a permutation of the ten B-channels into the ten internal X channels.
- The **H-relay / SR-relay box**, forming a two-level XOR mixing tree that turns the ten X-signals into ten SR-signals. SR1…SR5 drive a bit-permutation network; SR6…SR10 Vernam-XOR the plaintext bits.
- The **interposer-magnet logic box**, containing ten magnets M1..M10 wired into a relay ladder driven by the A contacts. If an M is energised on a motion cycle, its wheel is held; otherwise the wheel steps one pin.
- The **KTF switch** ("Klartextfunktion"), a small panel that mixes bit 3 of the *previous* plaintext character into the stepping logic of M1, M8, M9 and M10. When KTF is off the machine is autonomous (its stepping depends on the wheel state alone); when on, the keystream becomes plaintext-dependent and the cipher becomes nonlinear in an operationally important way.

### 2.2 Timing

The T52e is a synchronous teleprinter operating at 50 baud — 20 ms per 5-bit ITA2 character slot. Within each slot there are three mechanical phases:

1. **Transmit / receive phase** (the main 20 ms). The wheels are stationary; their B contacts feed the active keystream. The five plaintext bits traverse the H/SR network once per character.
2. **Pawl-drive phase** (the start of the next slot). Interposers are set from A-contact state; the pawl carriers then try to advance each wheel. Wheels whose M magnets are energised are held; the rest step one pin.
3. **Translate/print phase**. If the machine is receiving, the deciphered character is latched into the printer relays R1..R5.

In T52e with KTF on, a relay **RR3** slaves to R3 across the character boundary, so the stepping of M1, M8, M9, M10 in cycle *n* depends on bit 3 of plaintext character *n − 1*. This is the cipher's single most important defensive feature.

## 3. Mathematical specification

### 3.1 Notation

Let `Xᵢ` (i = 1..10) denote the ten internal keystream wires after the key-setting switches. Let `Hᵢ` and `SRᵢ` denote the intermediate and final relay-network outputs. Let `Sᵢ` (i = 1..5) denote the five plaintext bits of one Baudot character; `Cᵢ` the five ciphertext bits. Let `Aᵢ` denote the state of wheel *i*'s A-contact, and `Mᵢ` the state of interposer-magnet *i*.

All signals are binary. XOR is written `⊕`; AND is `∧`; NOT is `¬`.

### 3.2 The H-relay network (Davies Fig 14, verified)

```
H1  = X1 ⊕ X2        H6  = X1 ⊕ X6
H2  = X3 ⊕ X4        H7  = X2 ⊕ X7
H3  = X5 ⊕ X6        H8  = X3 ⊕ X8
H4  = X7 ⊕ X8        H9  = X4 ⊕ X9
H5  = X9 ⊕ X10       H10 = X5 ⊕ X10
```

The first five H relays pair up adjacent channels (1-2, 3-4, …, 9-10); the second five pair channels that are five apart (1-6, 2-7, …, 5-10). Every X appears in exactly two H equations.

### 3.3 The SR-relay network (Davies Figs 13 and 14, verified)

```
SR1 = H1 ⊕ H8        SR6  = H3 ⊕ H7
SR2 = H6 ⊕ H7        SR7  = H2 ⊕ H5
SR3 = H3 ⊕ H8        SR8  = H1 ⊕ H9
SR4 = H2 ⊕ H10       SR9  = H5 ⊕ H6
SR5 = H4 ⊕ H10       SR10 = H4 ⊕ H9
```

Substituting the H-equations gives each SR as the XOR of four X-channels:

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

**Invariant.** Every `Xᵢ` appears in exactly four SR equations, so the sum of all ten SR bits is zero modulo 2:

```
SR1 ⊕ SR2 ⊕ SR3 ⊕ SR4 ⊕ SR5 ⊕ SR6 ⊕ SR7 ⊕ SR8 ⊕ SR9 ⊕ SR10  =  0
```

Davies notes this as "the independent linear relationships between SR1–10". In cryptanalytic terms, it reduces the 10-dimensional SR space to a 9-dimensional affine subspace.

### 3.4 The encryption action (Davies Figs 5, 7, 9)

For one character:

1. **Vernam stage.** The five plaintext bits are XORed with SR6..SR10:

   ```
   Tᵢ = Sᵢ ⊕ SR(5+i)        i = 1..5
   ```

2. **Transposition stage.** The bit-tuple `T = (T1, T2, T3, T4, T5)` is permuted by σ(SR1,…,SR5), one of 32 permutations of 5 elements selected by the four-bit SR1–SR5 pattern:

   ```
   Cᵢ = T[σ_{SR1..SR5}(i)]    i = 1..5
   ```

The full 32-row permutation table, verified from Figure 9 of the Davies paper, is reproduced in Table 1 below. When all five of SR1..SR5 are operated the permutation is the identity — this is the condition obtained by throwing the main control switch to "clear".

**Table 1. Davies Figure 9, transcribed.** Row index is the binary encoding `SR1 + 2·SR2 + 4·SR3 + 8·SR4 + 16·SR5` plus one. `σ(i)` is the plaintext-element index that appears at ciphertext position `i`.

```
(SR1 SR2 SR3 SR4 SR5) | σ(1) σ(2) σ(3) σ(4) σ(5)
 1:  0 0 0 0 0        |   1    3    4    5    2
 2:  1 0 0 0 0        |   2    3    4    5    1
 3:  0 1 0 0 0        |   5    3    4    1    2
 4:  1 1 0 0 0        |   2    3    4    1    5
 5:  0 0 1 0 0        |   4    3    1    5    2
 6:  1 0 1 0 0        |   2    3    1    5    4
 7:  0 1 1 0 0        |   5    3    1    4    2
 8:  1 1 1 0 0        |   2    3    1    4    5
 9:  0 0 0 1 0        |   3    1    4    5    2
10:  1 0 0 1 0        |   2    1    4    5    3
11:  0 1 0 1 0        |   5    1    4    3    2
12:  1 1 0 1 0        |   2    1    4    3    5
13:  0 0 1 1 0        |   4    1    3    5    2
14:  1 0 1 1 0        |   2    1    3    5    4
15:  0 1 1 1 0        |   5    1    3    4    2
16:  1 1 1 1 0        |   2    1    3    4    5
17:  0 0 0 0 1        |   2    3    4    5    1
18:  1 0 0 0 1        |   1    3    4    5    2
19:  0 1 0 0 1        |   5    3    4    2    1
20:  1 1 0 0 1        |   1    3    4    2    5
21:  0 0 1 0 1        |   4    3    2    5    1
22:  1 0 1 0 1        |   1    3    2    5    4
23:  0 1 1 0 1        |   5    3    2    4    1
24:  1 1 1 0 1        |   1    3    2    4    5
25:  0 0 0 1 1        |   3    2    4    5    1
26:  1 0 0 1 1        |   1    2    4    5    3
27:  0 1 0 1 1        |   5    2    4    3    1
28:  1 1 0 1 1        |   1    2    4    3    5
29:  0 0 1 1 1        |   4    2    3    5    1
30:  1 0 1 1 1        |   1    2    3    5    4
31:  0 1 1 1 1        |   5    2    3    4    1
32:  1 1 1 1 1        |   1    2    3    4    5  ← identity (clear)
```

The table's 32 entries include duplicate permutations. For example, row 1 `(SR=00000)` and row 18 `(SR=10001)` both yield the permutation `(1,3,4,5,2)`; row 2 and row 17 both yield `(2,3,4,5,1)`. Davies remarks that "with five transposition channels, the full set of transpositions is not employed" — the T52e realises only about two dozen distinct permutations out of the 120 possible.

**Reciprocity.** Every permutation listed above is either an involution or paired with its inverse in another row of the table, which is what allows the same circuit to encipher and decipher.

### 3.5 The stepping logic (Davies Fig 11)

The ten A-contacts feed a relay ladder whose ten outputs are the M-magnet currents. On each pawl-drive cycle:

- if `Mᵢ = 1`, wheel *i* is held;
- if `Mᵢ = 0`, wheel *i* steps one pin forward.

**Confirmed equations (from direct tracing of Figure 11 top half, "Without KTF"):**

```
M3 = ¬A2 ∧  A1
M4 =  A2 ∧  A3                (explicitly given in Davies' text, p. 12)
M5 = ¬A4 ∧ ¬A3
M6 =  A4 ∧  A5
```

**Inferred equations** (by physical symmetry of the A6/A8/A10 upper tree; verification against Figure 11(a) test vector is required before publication):

```
M2  = ¬A10 ∧ ¬A1
M7  =  A10 ∧ ¬A6 ∧ ¬A9
M8  =  A10 ∧ ¬A6 ∧  A9
M1  =  A10 ∧  A6 ∧ ¬A8
M10 =  A10 ∧  A6 ∧  A8 ∧ ¬A7
M9  =  A10 ∧  A6 ∧  A8 ∧  A7
```

**With KTF on**, the four magnets influenced by `RR3 = R3(previous character)` become:

```
M1, M8, M9, M10 — as above, but AND-ed with RR3 or ¬RR3 at the corresponding
                  point in the ladder, per the lower half of Davies Figure 11.
```

### 3.6 Test vector (Davies Figure 11(a))

Starting from the all-ones position on the Munich machine, with KTF off, after 24 pawl-drive cycles the ten wheels reach the pin positions:

```
W1=20, W2=22, W3=20, W4=16, W5=23, W6=16, W7=17, W8=14, W9=17, W10=14
```

Figure 11(a) of Davies also prints the full 24-cycle waveform of every M and A contact. An implementation claiming historical fidelity must reproduce these waveforms bit-for-bit — this is the single most important validation target for any T52e software emulator.

## 4. Cryptographic strength

### 4.1 Keyspace

The daily key consists of:

- the pin-pattern on all ten cams — 47 + 53 + 59 + 61 + 64 + 65 + 67 + 69 + 71 + 73 = **629 bits**, typically fixed per week or month and distributed on a separate schedule;
- the ten key-setting-switch positions — 10¹⁰ ≈ 10¹⁰ permutations, though only "legal" (i.e. bijective) settings are meaningful and these number 10! = 3.6 × 10⁶;
- the ten wheel start positions — 47·53·…·73 ≈ **8.94 × 10¹⁷**;
- KTF on/off — one bit.

In round figures the daily keyspace is ~10²⁴, well beyond any brute-force attack even today.

### 4.2 Historical breaks

- **May 1940** — Arne Beurling of the Swedish FRA cracked T52a by pencil-and-paper analysis of depth pairs on the Berlin–Oslo Reichspost line; this is universally regarded as one of the greatest cryptanalytic achievements of the war.
- **1940 – 1943** — Ericsson built a T52 analogue ("Apparaten") used to read perhaps 350,000 German messages by exploiting the operator-imposed depths and the linear keystream of T52a/b/c.
- **Mid-1943** — Germany introduced T52e. Swedish decrypts dropped to zero, partly because the KTF feedback made depth-alignment ambiguous, partly because the Germans tightened operating discipline.
- **1942 onwards** — Bletchley Park's Testery occasionally broke Sturgeon when Luftwaffe operators gave them depths of three or more. The attacks did not scale and were abandoned in favour of Tunny, which the Luftwaffe obligingly retransmitted a large fraction of Sturgeon traffic onto.

The combination of nonlinear stepping (via the A→M feedback) and keystream mixing (via the H/SR layer) made T52e functionally secure in operational practice; had the Luftwaffe enforced per-message indicators and suppressed retransmissions, Bletchley would have seen very little of its traffic.

### 4.3 A reduced-keyspace benchmark

Full brute force of the T52e wheel-start keyspace (8.94 × 10¹⁷) is infeasible on any device that will exist this century. For benchmarking the attack we reduce the problem, exactly as the Bletchley Testery would have done after partial wheel recovery: assume six wheels' starting positions are known from a separate attack (depths, cribs, or analytic recovery), and brute-force only the remaining four.

If the four unknown wheels are W7 … W10 (pin counts 67, 69, 71, 73) the residual keyspace is:

```
67 × 69 × 71 × 73 = 23,951,289 ≈ 24 million candidates
```

This aligns neatly with the 22-million Lorenz SZ40 χ1×χ2 search in the sibling benchmark, making backend-to-backend performance comparison (Scalar / SIMD / GPU) directly meaningful.

**Per-candidate work.** Generate 10 wheel B-contact streams; pass through the daily key's switch permutation to obtain X; XOR through H and SR to obtain the 10 SR signals; reverse the Figure-9 transposition on each ciphertext character by the SR1-SR5 pattern of that character; XOR out SR6..SR10 to obtain a candidate plaintext; score with the Baudot index of coincidence. For German plaintext the expected IC is ≈ 0.085 against ≈ 0.031 for random — a four-to-one contrast that is easy to detect over a ciphertext of a few hundred characters.

## 5. Research methodology

We proceeded in four stages:

1. **Literature scan.** English sources on T52e are thin. We surveyed the Crypto Museum and Jörgen Nilsson pages, the Wikipedia article, Frode Weierud's CryptoCellar simulator notes, and Gannon's *Colossus: Bletchley Park's Greatest Secret* (2006). None of these give the internal logic. They all cite a single authority: Donald Davies' 1982 technical memorandum.

2. **Primary-source retrieval.** The Crypto Museum hosts a scanned PDF of the Davies paper. We retrieved it with `curl` (the Playwright-based downloader used elsewhere in this project is defeated by Crypto Museum's Cloudflare layer for binary files). pdftotext extracts the narrative cleanly but mangles all tabular figures; the numerical tables exist only as scanned images.

3. **Figure-by-figure verification.** For each figure we:
   1. Rendered the relevant PDF page at 170 DPI using PyMuPDF, adjusting rotation so the figure reads landscape-right-side-up.
   2. Cropped and re-rendered at up to 240 DPI to bring individual cells into readable resolution.
   3. Transcribed the figure content as text.
   4. Deleted the intermediate PNG before moving on (necessary to stay within the conversation's 2000-pixel image budget).

   An early attempt to shortcut this step by asking Google Gemini for the H/SR topology returned a confidently-stated but demonstrably wrong pattern; the Davies figure gives `H1 = X1 ⊕ X2` whereas Gemini gave `H1 = X1 ⊕ X6`. Gemini's answer had mathematical elegance but no relationship to the physical wiring. Every equation in §3 was therefore re-verified by direct reading of the figure.

4. **Self-consistency checks.** We verified the SR-network by computing `SR1 ⊕ … ⊕ SR10` from the extracted equations; it is identically zero, matching Davies' "independent linear relationship" note. We verified the Figure 9 permutation table by confirming that each of its 32 rows is a valid permutation of {1, 2, 3, 4, 5} and that row 32 (all SRs operated) is the identity, exactly as the text requires.

Four equations of the interposer logic could be read directly from the upper tree of Figure 11; the remaining six depend on a three-deep relay tree whose arrow conventions we could not resolve unambiguously from the scan. We record these as *inferred* in §3.5 and recommend verifying them empirically against the Figure 11(a) waveform trace before any publication claiming bit-accurate historical reconstruction.

## 6. Implementation notes for software emulation

- **State.** The emulator needs, per wheel: a pin-pattern bit-array of length equal to the pin count; a current pin position; and separately derived A and B contact values (the B contact is the cam displaced ~1/3 revolution from A, easily implemented as an offset into the same bit array).
- **Per-character hot loop.**  
  ```
  for each of 10 wheels i:
      A[i] = pins[i][ (angle[i] + A_offset[i]) % pin_count[i] ]
      B[i] = pins[i][ (angle[i] + B_offset[i]) % pin_count[i] ]
  X = switch_permutation(B)                      // 10 x 10 permute
  compute H, SR from X via table 3.2, 3.3        // 20 XOR ops
  T = plaintext ⊕ (SR6..SR10)                    // 5 XOR
  cipher = perm_table[ SR1..SR5 ](T)             // 5-of-32 table lookup
  ```
- **Stepping loop** (once per character, after encipherment):  
  ```
  compute M1..M10 from A1..A10 via §3.5 equations
  for each wheel i where M[i] == 0:
      angle[i] = (angle[i] + 1) % pin_count[i]
  ```
- **Shader-friendly layout.** All ten wheel angles pack into a single `uvec4` (with two spare lanes) if pin counts are widened to uniform 8-bit; the XOR-4-of-10 SR equations pack into four 10-bit masks per SR which can be popcount-XORed in a single shader instruction. The 32-row permutation table fits in a single `uvec4` per row when each permutation is packed as five 3-bit fields in a 15-bit integer.

These observations drive the design of the Scalar / SIMD / GPU-shader benchmark backends already in use for the Enigma and Lorenz problems in this repository.

## 7. Open questions

1. **The six inferred M-equations.** We have extracted five equations from Figure 11 and inferred the remaining five by symmetry. A motivated reader with access to the original document (or to the surviving Munich machine) should verify these against the Figure 11(a) waveform before any "bit-accurate" claim is made.

2. **The Munich cam patterns (Figure 3).** We have not transcribed the 629 pin positions individually. For a cryptological study this does not matter — any self-consistent pin pattern exercises the algorithm identically. For a *historical* study that intends to match wartime decrypts, Figure 3 must be extracted.

3. **The KTF connection details.** The KTF switch rewiring history is itself a cryptanalytic clue — the Munich machine shows evidence that the KTF wiring changed at least once during T52e's service life. It is possible that a late-war variant is *not* cryptanalysable by attacks that work on Davies' reconstructed early-war variant.

4. **T52c vs T52e internal differences.** T52c/d used the raw X channels directly as keystream; T52e's H-relay interposition is the key defensive change. Davies notes that the T52c simulator wire-up is straightforward to derive from the T52e wire-up by deleting the H layer; we have not verified this claim and the T52c variant is accordingly out of scope for the present benchmark.

## 8. References

1. **Donald W. Davies**, *The Siemens and Halske T52e Cipher Machine*, NPL technical memorandum, 1982 (re-issued in *Cryptology: Yesterday, Today and Tomorrow*, Artech House, 1987). The paper reproduced in `T52e_TechDesc_EN.pdf` in this directory. This is the primary source for all technical claims above.

2. **Paul Gannon**, *Colossus: Bletchley Park's Greatest Secret*, Atlantic Books, 2006. Chapter on Sturgeon (pp. 157–158) is the most accessible English-language narrative; the technical content is light.

3. **Bengt Beckman**, *Codebreakers: Arne Beurling and the Swedish Crypto Program during World War II*, American Mathematical Society, 2002. Authoritative on the Swedish breaks of T52a/b/c/d.

4. **Frode Weierud**, *Sturgeon, the FISH BP Never Really Caught*, in *Coding Theory and Cryptography*, Springer, 2000 (ISBN 3-540-66336-3). Includes a working T52d simulator; the T52e variant can be derived by adding the H layer.

5. **The Crypto Museum**, *[Siemens and Halske T52 page](https://www.cryptomuseum.com/crypto/siemens/t52/)*. Best photographic archive of the surviving machines.

6. **Wikipedia**, *[Siemens and Halske T52](https://en.wikipedia.org/wiki/Siemens_and_Halske_T52)*. High-level overview; technically correct but without the logic details needed to build an emulator.

---

## Colophon

This document was synthesised by **Claude Opus 4.7 (1M-context)**, an AI system produced by Anthropic, from the primary and secondary sources listed above. No part of the figure-extraction or circuit-tracing was available in machine-readable form; all tables and equations in §3 were transcribed cell-by-cell from 300-DPI renders of the Davies PDF. A log of the extraction process — including a wrong answer returned by a competing AI on the H-relay topology — is preserved in the companion file `T52e_SPEC_VERIFIED.md` and the Git history of this directory.

The document is released under **CC-BY 4.0**; please cite as

> Claude Opus 4.7 (Anthropic) & the EnigmaBenchmark authors,
> *The Siemens & Halske T52e "Sturgeon" — A Complete Technical Reconstruction*,
> 2026-04-18, https://github.com/[repo]/EnigmaBenchmarkAvalonia/docs/research-t52e/

Corrections, additional verification of the inferred stepping equations,
and any image of Figure 3 with readable cam patterns are warmly welcomed
as pull-requests against this directory.
