# SIGABA / ECM Mark II — Research Notes and Feasibility for EnigmaBenchmark

Consolidated from a 2026-04-19 conversation. Future-reference memo for if/when SIGABA joins the benchmark family.

---

## TL;DR

- **SIGABA is the only major WWII cipher system that was never broken** by any adversary, during or after the war. This isn't a flourish; it's the historical record.
- **Algorithm structure was declassified by the NSA in 2001** and has been fully reconstructed by academia (Savard-Pekelney 1999's pre-declassification reconstruction turned out to be largely correct).
- **A simulator is implementable** — several open-source versions exist and encrypt/decrypt round-trip is verifiable.
- **But a "crack" benchmark on SIGABA will fail** — not for lack of compute, but because **the IC scorer cannot find a signal**. That failure is itself the educational point: it demonstrates what a structurally secure rotor machine looks like.
- **The specific wartime rotor wirings remain mostly unpublished**, but reconstructed / test / PRNG-seeded wirings are all legitimate substitutes for benchmark purposes (same approach as T52e).

---

## 1. Historical Position

Strategic cipher machine of the US Army and Navy, from WWII through the mid-1950s. Also called **ECM Mark II** (Electric Cipher Machine), with US Army designation **SIGABA** and US Navy designations **CSP-889 / CSP-2900**.

- Designed 1935 by William Friedman and Frank Rowlett
- Entered operational service around 1940
- Throughout the war, every Axis intelligence agency — German, Japanese, Italian — attempted cryptanalysis. **All failed.**
- Continued in use through the early Cold War, retired mid-1950s
- NSA regards it as "still embodying deep security design principles"
- Officially declassified October 2001

## 2. Why SIGABA Held Up

Superficially a rotor-machine cousin of Enigma, but it fundamentally inverts Enigma's weakness at the **stepping mechanism** level.

### Mechanism Comparison

| Mechanism | Enigma (M3/M4) | SIGABA (ECM Mark II) |
|-----------|---------------|---------------------|
| **Rotor count** | 3 or 4 | **15**, split into three banks (5+5+5) |
| **Grouping** | One bank, all used for encryption | **Cipher (5) + Control (5) + Index (5)**, each with its own role |
| **Stepping pattern** | Fixed notch rules, nearly every keystroke = step | **Fully irregular** — every keystroke advances **1 to 4** cipher rotors, with the choice driven by Control + Index two-layer pseudo-randomness |
| **Depth attack viable?** | Yes (Bletchley's main weapon) | **No** — encrypting two messages under the same key produces different cipher-rotor stepping sequences |
| **Known-plaintext attack** | Turing's crib attack mainstay | Broken by the Control+Index feedback structure; cribs can't be extended |
| **IC-based scoring** | Effective | **Signal very weak** — irregular stepping spreads ciphertext statistics close to uniform random |

### The Core Design: Two-Layer Chaotic Stepping

1. **Control rotors (5)** step regularly on every keystroke (like an Enigma fast rotor), but they don't encrypt — they only generate a pseudo-random signal
2. **Control outputs pass through the Index rotors (5)** as a permutation / mixing layer. Index rotors **do not step** during encryption — they're a static lookup once the daily key is set.
3. **The 10 Index outputs feed logic circuitry that decides which 0–4 Cipher rotors step this cycle**
4. **Cipher rotors (5)** do the actual Enigma-style character encryption

Key feedback: **which Cipher rotor advances is determined by Control's current state combined with Index's static wiring**. This turns cipher-rotor stepping into a pseudo-random sequence of length ~10²⁰ — not a predictable periodic function.

This directly addresses Enigma's cardinal weakness: once notch positions are public, Bletchley could enumerate the stepping state around any known time point. SIGABA folds stepping into the keystream itself — **the stepping pattern is part of the secret**.

## 3. Break Record: Zero

- **Wartime (1940–45)**: Axis intelligence agencies all tried; no recorded success
- **Early Cold War (1945–55)**: KGB is rumoured to have analysed it (parallel to the VENONA effort), but no public claim of success
- **Post-declassification (2001–present)**:
  - **Stamp & Chan 2007** — *"A Ciphertext-Only Attack on SIGABA"* (Cryptologia) — statistical attack viable against **simplified variants** (Index layer reduced to 3 rotors or fewer), full SIGABA still unbroken
  - **Lee 2003** — upper-bound analysis of known-plaintext attack feasibility; conclusion: impractical
  - **24 years since declassification with no public claim of a real break**

Compare with Enigma: Winterbotham's 1974 *Ultra Secret* made the break public knowledge. Compare with SIGABA: 24 years after declassification, still no "we broke it" announcement.

## 4. Declassification Status — Layered Detail

| Item | Public? | Confidence |
|------|--------|-----------|
| **Overall algorithm structure** | ✅ NSA declassified October 2001 | Complete |
| Three-bank rotor layout (5+5+5) | ✅ Public | Certain |
| Control rotor stepping rules | ✅ Public | Certain |
| Index rotor permutation logic | ✅ Public | Certain |
| Control → Index → Cipher feedback circuit | ✅ Public | Certain |
| Cipher rotor wiring "structure" (Enigma-style 26×26 double-sided) | ✅ Public | Certain |
| **Specific wartime rotor wirings (what 26-letter substitution each rotor implemented)** | ⚠️ **Mostly unpublished or not centrally collected** | Incomplete |
| Daily key-generation procedures | ⚠️ Partially public | Incomplete |
| Unit-specific key schedules | ❌ Mostly still classified | Minimal |

## 5. Academic Reconstruction Sources (Chronological)

### 1999 · Savard & Pekelney (Cryptologia)
*"The ECM Mark II: Design, History, and Cryptology"*

**Two years before the NSA's formal declassification**, they derived a complete working model from:
- US patents (Friedman, Rowlett, etc.)
- Congressional testimony transcripts
- Partially declassified NSA fragments
- Photographs of surviving machines

Post-2001 comparison showed their reconstruction was **largely correct**, with minor deviations in specific wire paths. One of the most impressive reverse-engineering results in cipher-machine history.

### 2003 · Mark Stamp (simulator)

Java SIGABA simulator using publicly reconstructed rotor wirings and the full three-bank stepping logic. Still available on Stamp's academic page.

### 2007 · Stamp & Chan (Cryptologia)
*"A Ciphertext-Only Attack on SIGABA"*

Statistical attack against **simplified variants**:
- Index layer reduced to 3 rotors or fewer: attack feasible in weeks of CPU time
- Full 5-Index SIGABA: the required ciphertext volume plus compute still exceeds practicality
- Attack principle: exploit statistical biases introduced after Index mixing, but the full configuration dilutes those biases below noise

### 2008 · Stamp, *Applied Cryptanalysis* (book)

Chapter 6 is dedicated to analytic approaches to SIGABA, consolidating the above papers plus a synthesis of historical sources.

### Others

- **Lee 2003** — upper-bound analysis of known-plaintext attack
- **Sullivan & Weierud** — scattered notes on crypto-history enthusiast sites
- **Multiple non-commercial GitHub reconstructions** — mostly built on Savard-Pekelney + Stamp

## 6. Implementation Feasibility (for EnigmaBenchmark)

### 6.1 Machine implementation — feasible

All the logic we'd need is public:

```
input char
  → Cipher rotors forward (5 Enigma-style rotors)
  → reflector (or direct reversal)
  → Cipher rotors reverse
  → output char

After each keystroke, stepping logic:
  1. Control rotors: fast rotor steps every keystroke; medium/slow step on notches
  2. Control outputs statically permuted through Index rotors
  3. Index outputs feed magnet assembly; 4 of the Cipher rotors each
     independently selected ON/OFF
  4. Selected Cipher rotors advance one step (one version advances "backward")
  5. Return to input
```

Effort estimate: ~400-600 lines of C#, slightly less than T52e (no plaintext-feedback loop like KTF; Index is a static layer during encryption).

### 6.2 Rotor wirings — three options

| Option | Source | Historical fidelity | Viability |
|--------|--------|-------------------|----------|
| A. Wartime actual wirings | Mostly unpublished | 100 % | ❌ Unobtainable |
| B. Stamp simulator's test wirings | Public, with attribution | 0 % (fabricated) | ✅ Usable, attribution required |
| C. Adapted Enigma-V wirings | Similar structure | 0 % (fabricated) | ✅ Usable |
| D. PRNG-seeded generation | Freshly produced | 0 % (fabricated) | ✅ Consistent with T52e's approach |

**Recommend Option D** — consistent with the benchmark's existing practice for T52e, ADFGVX, etc. Use `Random(seed)` to generate a legal wiring set. Avoids any copyright/provenance concerns.

### 6.3 Benchmark search space — this is the pain point

| Reduction strategy | Keyspace | Comment |
|-------------------|----------|---------|
| Search Cipher rotor start positions only | 26⁵ = **11,881,376** | Tractable |
| Add Control rotor starts | 26⁵ × 26⁵ = 10¹⁴ | Intractable |
| Add Index rotor starts | 10¹⁴ × 10⁵ = 10¹⁹ | Intractable |
| Full keyspace | ~10²¹+ | Intractable |

So benchmark-wise, we can only do the 26⁵ = 12M scope. That size is in the same ballpark as Lorenz chi-only (22M) and T52e (24M) — **compute-wise it runs fine**.

### 6.4 But "the scorer finds no signal" — the real challenge

Among 12M candidates only 1 is the true key. To pick that 1 out of 12M decrypt results, an effective **scorer** is needed:

- **Enigma**: 26-letter IC has a 10σ+ signal, reliably separates truth from noise
- **Lorenz χ-only**: Baudot IC + Δ-statistic has a clear signal
- **T52e**: Baudot IC with KTF-off mode has a signal
- **SIGABA**: **irregular stepping dilutes all such signals toward uniform**. 12M candidate IC values will mostly fall in the 0.033–0.037 random range, with the true key maybe only 0.001 above noise — **drowned in noise**

This is exactly why Stamp-Chan had to reduce the Index layer to 3 rotors before their statistical attack worked — with all 5 Index rotors active, the statistical bias sits below noise.

### 6.5 Expected benchmark output after implementation

The running log would look something like:

```
──── RUN  SIGABA (ECM Mark II) — Cipher rotor start recovery  (11,881,376 keys) ────

Historical context: SIGABA was the US Army/Navy strategic cipher used
  1940-1955. Never operationally broken by any adversary during or
  after WWII. The irregular three-bank stepping (Cipher/Control/Index)
  was specifically designed to defeat depth and known-plaintext attacks.

True Cipher starts: [A, B, C, D, E] (5 positions)

  [Scalar SIGABA]           11,881,376 keys / 85.2s (139 K/s)  bestIC=0.0342  found=False
  [Parallel 16 cores]       11,881,376 keys /  6.3s (1.9 M/s)  bestIC=0.0341  found=False
  [SIMD]                    11,881,376 keys /  2.8s (4.2 M/s)  bestIC=0.0337  found=False
  [SkSL GPU]                11,881,376 keys /  0.3s (39 M/s)   bestIC=0.0346  found=False

  Best-scoring recovered: [K, Q, M, A, T]   (WRONG — not true key)
  All backends terminate the full search with IC below 0.045 threshold.

HISTORICAL VERIFICATION: no backend broke SIGABA. This is the expected
  result. Irregular stepping dilutes the IC signal below statistical
  separability, and no known analytic attack exists against the full
  machine. The GPU's 200× speedup over scalar cannot substitute for
  a structural weakness in the cipher.
```

**This output is EnigmaBenchmark's keystone** — it tells viewers "same rotor-machine family; Enigma falls to a GPU, SIGABA doesn't" and demonstrates that **structure** matters more than **raw compute** for real-world cipher security.

## 7. Integration Recommendations

### Option evaluation

| Plan | Description | Effort | Educational value | Technical value |
|------|-------------|--------|------------------|----------------|
| **A. Pure simulator + demo** | Machine + encrypt/decrypt round-trip, no cracker | ~2 days | Medium | Low |
| **B. Simulator + cracker (expected to fail)** | All four backends + statistical scorer | ~3 days | **High** | Medium |
| **C. Simulator + Stamp-Chan attack on reduced variant** | Full-version fails, reduced-Index succeeds, contrast presented | ~5 days | Very high | High |
| **D. Skip** | Keep current 6 cipher machines | 0 | 0 | 0 |

### Recommended path: Plan B

- Implement the full SIGABA (conforming to 2001 NSA-declassified documents)
- Generate rotor wirings via PRNG (same pattern as T52e)
- Provide four backends (Scalar / Parallel / SIMD / GPU) running the 12M Cipher rotor search
- **All backends are expected to fail** (bestIC < 0.045 threshold)
- When user selects SIGABA in the cipher dropdown, UI shows an explanatory banner: "This benchmark demonstrates why SIGABA was never broken — even the fastest GPU cannot locate the true key under the IC scorer."
- Add a seventh cipher card to `readme.html` titled "**SIGABA — The Uncrackable**"

This is far more meaningful than just "another cipher we can break" — it makes EnigmaBenchmark's thesis explicit: **we're not celebrating GPU raw power, we're contrasting cipher structure strength against structure weakness**.

### About Plan C...

Technically the most interesting, but implementing the Stamp-Chan attack requires careful reading of the paper, tuning statistical parameters, and handling the simplified-vs-full variant contrast. Effort balloons. Start with B; if appetite remains, add C later as a dedicated demonstration.

## 8. Development Risk Points

1. **Three-bank stepping correctness verification**
   - Off-by-one stepping will immediately break encrypt/decrypt round-trip
   - Verify against Stamp simulator's known plaintext/ciphertext pairs
   - Or write self-consistent round-trip tests (encrypt twice and confirm identity)

2. **Index rotor static-permutation correctness**
   - Index does NOT step during encryption — it's a static lookup after daily-key setup
   - Easy to mistakenly code as "steps every keystroke", silently wrong

3. **Control rotor notch rules**
   - Public docs describe fast rotor stepping every keystroke, medium/slow on notches
   - Some SIGABA versions differ from Enigma's notch pattern; match the exact target version

4. **Cipher rotor "backward step"**
   - Some SIGABA versions advance cipher rotors in the opposite direction from Control
   - Check the implementation-target version carefully

5. **Scorer design**
   - If IC threshold is too low, noise triggers false-positive "found"
   - Recommend: borrow Enigma M3's 0.055 German threshold so SIGABA legitimately reports `found=False`

## 9. Reference Material (obtain first)

Before implementation starts, acquire:

- **Savard & Pekelney 1999** (Cryptologia) — full machine reconstruction
- **Mark Stamp's SIGABA simulator** — reference implementation (Java)
- **NSA 2001 declassification set** — search for `sigaba-ecm-declassified-2001.pdf` or similar
- **Frode Weierud's CryptoCellar** — photos and document links
- **Wikipedia English article** — decent structural overview, good starting point

---

## Conclusion

SIGABA's **algorithm structure** is public enough for faithful implementation. The absence of specific wartime rotor wiring values doesn't block legal implementation (use reconstructed or PRNG-seeded wirings). The most meaningful way to add SIGABA to EnigmaBenchmark is **Plan B** — all four backends run the full 12M keyspace and all report `found=False`, showing how **structural security** defeats **raw-compute brute force**. That becomes the benchmark's closing statement.

Before starting, read Savard-Pekelney 1999 first and validate round-trip against the Stamp simulator, then begin the C# implementation.

— Conversation-generated 2026-04-19
