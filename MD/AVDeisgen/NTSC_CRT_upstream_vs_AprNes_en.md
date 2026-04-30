# NTSC + CRT Implementation Comparison: LMP88959 NTSC-CRT (upstream) vs AprNes

**Research date**: 2026-04-30
**Scope**: Read-only code analysis; no code on either side is modified
**Paths under comparison**:
- Upstream: `C:\ai_project\AprNes\temp\NTSC-CRT\` (EMMIR / LMP88959, 2018-2023, v2.3.2)
- Ours: `C:\ai_project\AprNes\AprNes\NesCore\NTSC_CRT\` (shared between AprNes and AprNesAvalonia)

Both sides do the same thing: turn NES PPU output (or generic RGB) into "what a 1980s-90s television would have shown given a degraded signal." But the route, intent, target platform, and performance strategy are completely different. Below is a structured breakdown of every difference, useful both for deciding which features might be worth porting in either direction.

---

## 1. TL;DR — Profile of Each Side

| Dimension | Upstream NTSC-CRT | AprNes (Ntsc.cs + CrtScreen.\*.cs) |
|-----------|-------------------|------------------------------------|
| Language / platform | C89, cross-platform (Linux/macOS/Windows) | C# .NET Framework 4.8.1 + .NET 10, Windows-centric |
| Numeric precision | **Integer fixed-point throughout** (`signed char` analog, 14-bit sin/cos, EXP_P=11 fixed-point) | **`float`-dominant + selective 16.16 fixed-point** (e.g. `ResampleH_Bilinear` uses fixed-point; YIQ→RGB goes through `gammaLUT[4096]` 12-bit fixed-point lookup) |
| Threading | **Strictly single-threaded** (project rule) | **`Parallel.For`** demods 240 rows simultaneously; CRT post is also row-parallel |
| SIMD | **Explicitly forbidden** (project rule #5) | `Vector<T>` (Vector256/AVX2), with an additional `Avx2.GatherVector256` SIMD path |
| Sample rate | NES: `CRT_HRES = 2273*4/10 ≈ 909`/line; NTSC: `2275*4/10 ≈ 910`/line | `kOutW = 1024` (default) or **HD_NTSC `2048`** (12× Fsc oversample, .NET 10 only) |
| Samples per dot | NES dot ≈ 909/256 ≈ 3.55 sample (non-integer) | `kSampDot = 4` (1024) or `8` (HD_NTSC) — clean divides, SIMD-friendly |
| FIR / decoder | `EQF` 3-band IIR filter (low-pass) running a 4-stage cascade (`fL[4]/fH[4]`) | **Hann window FIR**, `kWinY=6, kWinI=18, kWinQ=54` (HD: 12/36/108); I/Q mode toggleable between 1953 asymmetric and 1960s symmetric |
| CRT post effects | **scanlines + bloom + blend** (3 knobs) | scanlines + bloom + shadow-mask/aperture-grille + curvature + phosphor decay + convergence + vignette + interlace jitter + horizontal beam spread (~9 knobs) |
| Distribution model | C lib: `crt_init / crt_modulate / crt_demodulate / crt_draw` global-struct API | `partial class NesCore` static fields + `Crt_Init / Crt_ApplyConfig / Crt_Render` + backend `Crt_SetBackend(Scalar/Simd/Gpu)` |
| GPU support | None | `CrtScreen.Gpu.cs` SkSL runtime effect (D3D11/Metal/GL via SkiaSharp) |
| LOC (core) | crt_core 666 + crt_nes 310 + crt_ntsc 331 = ~1300 lines (whole lib incl. main ~2300) | Ntsc 1129 + CrtScreen 624 + Simd 1005 + Gpu 203 + Shared 156 = **3117 lines** |
| NES specials | dot-skip-on-odd-frame, NES-specific HBI timing, border colour, 9-bit emphasis | dot crawl phase via `scanPhase6 / scanPhaseBase`, RF herringbone (audio→video buzz), color-burst jitter, RF/AV/SVideo profile triplet |

---

## 2. Pipeline Architecture Walk-through

Both sides logically do:

```
raw RGB / NES palette → NTSC composite signal → (noise/blur) → demod → YIQ → RGB → CRT post → screen
```

But the *data form* at each step differs significantly.

### 2.1 Modulation (encode side)

**Upstream (`crt_nes.c:106` `crt_modulate`)**
- Input: `unsigned short data[]` 9-bit NES pixel (or 6-bit without emphasis).
- For each dot, runs `square_sample(p, phase + 0..3)` four times, treats the NES colour as a "square wave," sums the integer IRE voltage. Writes into `signed char analog[CRT_INPUT_SIZE]` — the entire signal is one flat `signed char` 1-D array (909 samples × 262 lines).
- `setup_field()` writes vertical/horizontal sync into `analog[]` once; thereafter only the active video portion gets overwritten (`crt_nes.c:81-104`).
- The three dot-crawl phases come from `phasetab[CRT_CC_VPER] = { 0, 4, 8 }` (`crt_nes.c:116`); `CRT_CC_VPER=3` reflects the NES-specific 3-line repeat pattern.

**Ours (`Ntsc.cs:553` `GenerateSignal` for the fast path; `Ntsc.cs:753` `GenerateWaveform` for the ultra-analog path)**
- Two encoding paths:
  - **Fast / `_Fast` path** — Skips full waveform reconstruction. For each dot, reads pre-integrated (Y, I, Q) directly from `yBaseE/iBaseE/qBaseE[64*8]` (`Ntsc.cs:559`). Avoids the 909-sample analog buffer entirely; gives 256 dots' worth of demod result directly. Very fast but doesn't model LTI bandlimit.
  - **Physical / `_Physical` path (ultraAnalog=true)** — For each (dot × `kSampDot`) pair, looks up `waveTable[64 * kPhaseEntries * kSampDot]` precomputed waveforms, applies `emphAtten`, herring, xorshift noise, then runs a single-pole low-pass `vPrev += vVel * ringDamp + (x - vPrev) * SlewRate` (`Ntsc.cs:873`). This is the real "round-trip the signal through an LTI system" path.
- Signal format: `float waveBuf[kBufLen]` (kBufLen = 30+1024+30 = 1084, or doubled in HD), float throughout, with `kLeadPad=30` warmup padding on each end for the LTI filter.
- Dot crawl phase: two counters — `scanPhase6` (used by fast path) and `scanPhaseBase` (used by physical path); both `+= kPhaseStepLine` per scanline. HD_NTSC mode auto-doubles them to keep physical consistency (`Ntsc.cs:99-110`).

**Conclusion**: Upstream computes the full per-line signal as a 1 byte/sample integer array; our fast path skips signal generation and goes straight to demod result; our physical path matches upstream in level of fidelity but with higher precision (float + precomputed LUT + LTI filter).

### 2.2 Demodulation (decode side)

**Upstream (`crt_core.c:291` `crt_demodulate`)**
- The whole `crt_demodulate` runs noise → VSYNC → HSYNC → color-burst integration → I/Q wave reconstruction → FIR EQ → YIQ → RGB → write `out[]`, all inside a `for (line = CRT_TOP; line < CRT_BOT; line++)` loop.
- Color burst detection uses an accumulator IIR `ccr[CRT_CC_VPER][CRT_CC_SAMPLES]` (`crt_core.c:462-467`):
  ```c
  ccr[i % CRT_CC_SAMPLES] = p + n;  /* 7/8 prev + 1/8 new */
  ```
  This is a signal-domain IIR that locks onto the burst phase by itself.
- I/Q demodulation: in 4-sample-per-cycle mode, uses 4 fixed `wave[0..3]` constants (cos/sin sampled at 4 points), multiplies each sample, then feeds into `eqf()` 3-band IIR low-pass.
- YIQ → RGB is pure integer matrix (`crt_core.c:573-575`):
  ```c
  r = (((y + 3879 * i + 2556 * q) >> 12) * v->contrast) >> 8;
  ```

**Ours (`Ntsc.cs:976` `DemodulateRow` → `DemodulateRow_Core`)**
- Sync detection is skipped entirely — we already know exactly where every dot/scanline is (the emulator hands us precise PPU timing), so HSYNC/VSYNC search loops are unnecessary.
- Two demod strategies:
  - **Composite**: chroma extracted from the same luma waveBuf using a Hann FIR (`combinedI / combinedQ` precomputed as `hann[n] * cos/sin[(ph+n) % kPhaseEntries]`); window sizes `kWinI=18 / kWinQ=54` (asymmetric 1953) or 18/18 (symmetric 1960s), gated by `SymmetricIQ`.
  - **S-Video**: chroma extracted from a separate clean `cBuf` channel — no luma-chroma cross-talk.
- Inner loop uses `Vector<float>` SIMD `Vector.MultiplyAddEstimate` (.NET 10 → vfmadd231) for dot product.
- Y uses a shorter `kWinY=6` Hann window, manually unrolled:
  ```csharp
  yAcc = hannY[0]*wvY[0] + hannY[1]*wvY[1] + ... + hannY[5]*wvY[5];
  ```
  This is a 6-tap symmetric FIR equivalent to a boxcar low-pass (`Ntsc.cs:1036`).
- YIQ → RGB + Gamma: `YiqToRgb()` uses `gammaLUT[4096]` 12-bit fixed-point lookup (`Ntsc.cs:1122-1128`); the SIMD path inlines FMA + `Vector.MultiplyAddEstimate(vGC, R, v1_minus_GC)` for gamma.

**Conclusion**: Upstream's decoder is a full RF-receiver simulation (must handle sync drift, burst lock, noise); ours is "I already know where every dot is and what its phase is, so I just convolve a FIR for I/Q" — closer to a discrete-signal-processing view than an analog-receiver view.

### 2.3 CRT Post-process

**Upstream**: post-processing barely exists. `crt_core.c` does only two things at the tail of demod:
1. `if (v->scanlines)` — skips the bottom `end - v->scanlines` rows, leaving them black (`crt_core.c:662`).
2. `if (v->blend)` — 50/50 blend with the previous frame's pixel (`crt_core.c:584-608`).
3. (optional) `CRT_DO_BLOOM=1`: accumulates energy in `prev_e`, modulates `line_w` to vary effective row width. NES mode disables this (`crt_core.h:70` comment "does not work for NES").

**Ours**: `CrtScreen.cs` Render() is an independent stage 2. Takes `linearBuffer[3 plane × 1024 × 240]` float RGB and outputs `crt_analogScreenBuf[Crt_DstW × Crt_DstH]`. Pipeline:
1. `PrecomputeScanlineWeights()` — computes per-row Gaussian scanline weights (driven by BeamSigma + interlace jitter, `CrtScreen.cs:95-137`).
2. `ApplyHorizontalBlur()` — 3-tap source-pixel-space horizontal beam spread (SIMD, `CrtScreen.cs:192-247`).
3. Main `Parallel.For` row loop (`CrtScreen.cs:289`): upscale + ProcessPixel (brightness boost + bloom + gamma + clamp).
4. `ProcessRowMask_SWAR` / `ProcessRowMaskPhosphor_SWAR` — shadow mask + phosphor decay (SWAR 32-bit pack, `CrtScreen.cs:452-522`).
5. `ProcessRowConvergence` — R/G/B horizontal offsets that simulate electron-gun misalignment (fixed-point 16.16 accumulator, `CrtScreen.cs:524-544`).
6. `ApplyFullFrameCurvatureAndConvergence` — barrel distortion + convergence (`CrtScreen.cs:546-623`).

**Conclusion**: Upstream has no proper CRT post-processing — the "TV look" comes entirely from the demod's internal FIR softness + scanline + blend. We split demod and CRT post completely (Stage 1 NTSC demod writes `linearBuffer`, Stage 2 CRT post computes final pixels from `linearBuffer`), gaining many monitor-side adjustable knobs.

---

## 3. NES-Specific Specialisation

### 3.1 Palette → signal

| Topic | Upstream | Ours |
|-------|----------|------|
| Colour-definition entry point | `crt_nes.c:21` `square_sample(p, phase)` computes IRE per sub-sample on the fly | `Ntsc.cs:266-274` precomputes `yBase/iBase/qBase[64]` once; then `yBaseE/iBaseE/qBaseE[64*8]` integrates with emphasis applied |
| Emphasis handling | 9-bit pixel: `(p & 0x700)` three emphasis bits gate the `active[6]` table inside `square_sample` to attenuate certain phase tiers | All emphasis 0..7 baked into LUT once at init; runtime touches no recompute (`Ntsc.cs:294-326`) |
| Black handling | `crt_nes.c:47` hardcodes `if (hue >= 0x0e) return 0` | `Ntsc.cs:270` `if (color == 0) lo = hi; else if (color == 0x0D) hi = lo; else if (color > 0x0D) lo = hi = 0f` |
| IRE table | `crt_nes.c:26-35` 16-entry signed int, raw mV scaled ×1024 | Implicit — `loLevels/hiLevels` 4-entry float tables `{-0.12, 0.00, 0.31, 0.72}` / `{0.40, 0.68, 1.00, 1.00}` (`Ntsc.cs:217, 225`) |

Both sides reference the same source: [NESdev wiki - Brightness Levels](https://www.nesdev.org/wiki/NTSC_video#Brightness_Levels). Upstream stays in raw integer mV; we work in normalised ±1 floats.

### 3.2 NES-specific timing

| Topic | Upstream | Ours |
|-------|----------|------|
| Dot-skip-on-odd-frame | Caller-controlled via `s->xoffset`; upstream itself only accepts a sample-space x offset | Not handled explicitly — our emulator manages PPU timing directly, dot crawl is carried by `scanPhaseBase` |
| NES-specific HBI | `crt_nes.h:71-104` carves the 341 PPU px line into 9/25/4/15/5/1/15/256/11; sync_separator on `line ≥ 259` | None — we don't simulate HBI/sync. The `HbiSimulation` flag merely chooses whether to feed a fake zero into leftPad to seed the left-edge filter ring (`Ntsc.cs:776`) |
| Three-line dot crawl | `CRT_CC_VPER = 3`, `phasetab {0, 4, 8}` (`crt_nes.c:116`) | `kSampDot=4` × 3-row loop implicitly produces the same 12-phase repeat. Physically, NES master = 6×Fsc, so 1 line = 1364 master cycles, 1364 mod 6 = 2, so it takes 3 lines to return to phase 0 |
| Border colour | `NES_BORDER` is disabled in NES_OPTIMIZED path (`crt_nes.c:69`), but the macros remain | Not handled — overscan is the emulator's concern, not the NTSC pipeline's |

### 3.3 Color burst and jitter

- Upstream's color burst is *actually* written into the analog `signed char` array, then recovered via the `ccr[]` IIR loop in `crt_demodulate`.
- We skip color burst entirely — `phase0` comes straight from `scanPhase6`, i.e. "I already know where the burst is, no need to decode it."
- Only when ultra-analog + RF + `ColorBurstJitter` is enabled, `Ntsc.cs:730-734` occasionally (1/32 chance) nudges `phase0` by ±1 master tick to model phase drift from signal degradation.

---

## 4. CRT Post Effects Matrix

| Effect | Upstream | Ours (Scalar/SIMD) | Ours (GPU) | Notes |
|--------|----------|---------------------|------------|-------|
| Scanline gap | ✓ Skip rows, leave black (`crt_core.c:662`) | ✓ Per-row Gaussian weight (`PrecomputeScanlineWeights`) | ✓ uScanlineStrength uniform | Upstream is binary on/off; ours is a smooth gradient |
| Beam Gaussian bloom | ✓ But disabled for NES (`CRT_DO_BLOOM`) | ✓ `BloomStrength` × row brightness, `bright * constB` | ✓ uBloomStrength | Upstream models it as line width; we model as brightness boost |
| Horizontal beam spread (3-tap blur) | ✗ | ✓ `ApplyHorizontalBlur`, SIMD 8-pixel | ✓ uHBlurAlpha uniform | Ours-only |
| Shadow mask (RGB stripe / honeycomb) | ✗ | ✓ `ProcessRowMask_SWAR`, two modes (ApertureGrille / ShadowMask) | ✓ uMaskType (0=none,1=AG,2=SM) | Upstream has nothing equivalent |
| Curvature (barrel distortion) | ✗ | ✓ `ApplyFullFrameCurvatureAndConvergence`, precomputed `_curvMap[]` reverse map | ✓ uCurvature | Ours-only, with map cache |
| Phosphor decay (max(N, N-1)) | ✗ | ✓ `ProcessRowPhosphor_SWAR`, max-blend with `_prevFrame` | ✓ uPhosphorDecay (ping-pong SKSurface) | Ours-only |
| Convergence (R/G/B horizontal offset) | ✗ | ✓ `ProcessRowConvergence`, fixed-point 16.16 | ✓ uConvergence | Ours-only |
| Vignette (corner darkening) | ✗ | ✓ Embedded in `_boostRow[ty]`: `bb * (1 - vs4 * vy * vy)` | ✓ uVignetteStrength | Ours-only |
| Interlace jitter (sub-pixel jitter every other field) | NTSC mode has even/odd phase switching at the signal level (`crt_ntsc.c:217-228`) | ✓ `InterlaceJitter` flag → `±0.25f` Y offset | (uniforms exist; shader version-dependent) | Different beasts: upstream is signal-level even/odd field; ours is monitor-side scanline jitter |
| Dot crawl | ✓ Truly emerges from `CRT_CC_VPER=3` phase repeat | ✓ Naturally produced by `scanPhase6` carry | ✓ Same as SIMD | Both sides do this |
| Frame blend (50/50 with prev frame) | ✓ `crt->blend = 1` (`crt_core.c:584-608`) | ✗ We use phosphor decay to model persistence instead | ✗ | Upstream's blend is essentially a simplified phosphor decay |
| Signal noise injection | ✓ `crt_demodulate(noise)` adds `(rn>>16 & 0xff - 0x7f) * noise / 256` directly to inp | ✓ `NoiseIntensity` × xorshift (profile-driven, RF=0.04, AV=0.003, SVideo=0) | (Shader has none; noise is added at the NTSC stage) | Both sides do this |
| Monochrome toggle (`as_color=0`) | ✓ `crt_ntsc.c:184-187` zeroes ccmodI/Q/burst via memset | ✗ No explicit toggle, though `SVideo` profile (`ChromaBlur` high + emphAtten) approximates it | ✗ | Upstream-only |
| Raw artifact-color image input (`raw=1`) | ✓ `crt_ntsc.c:148-172` skips luma scaling, feeds raw image | ✗ | ✗ | Upstream-only — used to decode dithered B/W → colour |
| VHS-style bottom-noise jitter | ✓ `crt_ntscvhs.c` dedicated mode | ✗ | ✗ | Upstream-only |
| RF herringbone (audio → video) | ✗ | ✓ ultra-analog + RF + `RfAudioLevel` driving 1.31683 rad/dot rotation complex (`Ntsc.cs:577-587, 759-770`) | (Probably not implemented) | Ours-only — corresponds to real RF audio bleed-through |
| Color burst phase jitter | ✗ | ✓ `ColorBurstJitter` 1/32 chance ±1 master-tick nudge (`Ntsc.cs:730-734`) | ✗ | Ours-only |

---

## 5. Performance Strategy

### Upstream
- Strictly single-threaded. Project rule #6 is literally "Single threaded."
- Strictly integer-only. `signed char` analog buffer + 14-bit interpolated sin/cos table (`crt_core.c:19-40`) + `EXP_P=11` fixed-point `expx()` (`crt_ntsc.c:32-83`).
- Strictly no SIMD (project rule #5).
- The README explicitly mentions "L. Spiro AVX accelerated version" living in a BeesNES fork — i.e. upstream tells the reader "want speed? fork it."
- In other words: upstream optimises for *maximum portability*, not maximum performance. Any loop that *could* be SIMD-accelerated is deliberately not.

### Ours
Three-tier backend, runtime-dispatched (`CrtScreen.Shared.cs:31`):

1. **Scalar (CrtScreen.cs)** — default for the net48 build; relies on `Vector<T>` auto-SIMD (much hand-unrolled), `Parallel.For` distributes 240 rows across the thread pool.
2. **Simd (CrtScreen.Simd.cs)** — .NET 10 + AVX2 explicit (`Vector256<T>`, `Avx2.GatherVector256`, `Vector.MultiplyAddEstimate`, `[SkipLocalsInit]`).
3. **Gpu (CrtScreen.Gpu.cs)** — SkiaSharp `SKRuntimeEffect` running an SkSL shader, can target D3D11/Metal/GL; currently raster SKSurface based, with phase 3 plans to lease Avalonia's GPU canvas directly.

The NTSC demod side (`Ntsc.cs`) doesn't have backend switching, but uses several techniques:
- **HD_NTSC compile-time switch** — `kOutW` 1024 vs 2048, `kSampDot` 4 vs 8, `kPhaseEntries` 6 vs 12. `kSampleRateScale = 0.5f` rescales the IIR coefficients to maintain physical consistency.
- **Generic struct dispatch** (`Scale2/4/6/8`) — JIT specialises the loop per `analogSize`, turning `int d = x / N` into a compile-time constant divide; for power-of-2 N it becomes a shift (`Ntsc.cs:23-27, 591-598`).
- **Code splitting** — `addNoise / herring` as two booleans yield 4 branch-hoisted JIT-specialised paths (`Ntsc.cs:780-783`).
- **Branchless modular wrap** — sign-bit extension instead of `if`:
  ```csharp
  ph += kPhaseStepOutPx + (((kThreshOutPx - ph) >> 31) & kPhaseWrap);
  ```
  Used everywhere in the file (`Ntsc.cs:637, 884, 970...`).
- **Per-thread scratch via `[ThreadStatic]`** — replaces per-frame `stackalloc`, saves ~720 stackallocs/frame (`Ntsc.cs:459-463`, `CrtScreen.cs:49`).

**Qualitative comparison**: Upstream's math is fully amenable to SIMD (BeesNES's AVX fork proves it), but the author chose "algorithmic correctness + portability" over speed. We sit at the opposite extreme: maximise the .NET 10 + AVX2 + TieredPGO performance envelope, at the cost of ~2.4× more code, Windows-centric assumptions, and unsafe pointers.

---

## 6. API / Integration Shape

### Upstream usage (C lib, global struct)

```c
#include "crt_core.h"

static struct CRT crt;
static struct NTSC_SETTINGS ntsc;

/* init */
crt_init(&crt, screen_width, screen_height, CRT_PIX_FORMAT_BGRA, screen_buffer);
crt.blend = 1;
crt.scanlines = 1;

/* per frame */
ntsc.data = video_buffer;       /* unsigned short[] for NES, unsigned char[] for NTSC */
ntsc.format = CRT_PIX_FORMAT_BGRA;  /* not for NES (NES has fixed format) */
ntsc.w = video_width;
ntsc.h = video_height;
ntsc.as_color = color;
ntsc.field = field & 1;
ntsc.raw = raw;
ntsc.hue = hue;
if (ntsc.field == 0) ntsc.frame ^= 1;
crt_modulate(&crt, &ntsc);
crt_demodulate(&crt, noise);
field ^= 1;
```

Adding a new "system" = drop a `CRT_SYSTEM_*` enum entry into `crt_core.h` + write a `crt_<sys>.h/c` containing timing constants and `crt_modulate`. Done (README §"Writing a port for a certain system").

### Ours usage (partial-class static API)

```csharp
// init
NesCore.Crt_SetBackend(NesCore.CrtBackend.Simd);  // or Scalar / Gpu
NesCore.Ntsc_Init();
NesCore.Crt_Init();
NesCore.Ntsc_ApplyConfig(
    analogOutput: (int)AnalogOutputMode.AV,
    ultraAnalog: true,
    analogSize: 4,
    crtEnabled: true,
    analogScreenBuf: screenBuf);
NesCore.Crt_ApplyConfig(
    analogOutput: (int)AnalogOutputMode.AV,
    analogSize: 4,
    analogScreenBuf: screenBuf);

// per scanline (called from PPU at sl × cx==260)
NesCore.Ntsc_CaptureScanline(sl, emphasisBits);

// per frame end
NesCore.Ntsc_FlushPendingRows();   // parallel demod 240 rows → linearBuffer
NesCore.Crt_Render();              // CRT post → analogScreenBuf
```

The integration model is "embed inside our partial class, share all NesCore static state, use `[ThreadStatic]` per-worker scratch." Not pluggable, but in exchange the integration overhead is zero: `palBuf = ntsc_rowPalettes + sl * 256` reads directly from the buffer the PPU writes, without copying.

---

## 7. Things Upstream Has That We Don't (Port Candidates / Inspiration)

| Feature | Upstream location | Why it might be worth taking |
|---------|-------------------|------------------------------|
| **Monochrome toggle (`as_color=0`)** | `crt_ntsc.c:184-187` | A one-line memset; gives users a "B/W old TV" option |
| **Raw image / artifact-color input** | `crt_ntsc.c:148-172` `s->raw` path | Decodes dithered B/W → colour (the `rainbow.png` art trick), an interesting demo mode |
| **VHS mode** | `crt_ntscvhs.c` whole file + `CRT_VHS_NOISE` | Three-tier bandwidth (SP/LP/EP) + bottom-noise band, evokes a "tape rip" feel |
| **Programmable hue offset (global `crt.hue`)** | `crt_core.c:318-321` | We have `iPhase[]/qPhase[]` tables but no user-tunable global hue rotation |
| **Real VSYNC/HSYNC search** | `crt_core.c:369-397` (VSYNC), `:434-451` (HSYNC) | We currently skip these because "the emulator already knows everything." But if we ever feed signal in from outside (e.g. Avalonia capture from another window), we'd need this back |
| **Interlaced even/odd field** | `crt_ntsc.c:197-200, 217-228` | NTSC mode has true field-alternating equalising pulses; our `InterlaceJitter` is a monitor-side visual hack, not a signal-layer feature |
| **Signal noise (`crt_demodulate(noise)` global knob)** | `crt_core.c:362` exposes a single user-facing noise scalar | We have `RF_NoiseIntensity / AV_NoiseIntensity / SV_NoiseIntensity` bound to profiles, but no user override |
| **3-band EQ (USE_CONVOLUTION=0 path)** | `crt_core.c:158-233` `EQF` struct | Our Hann window FIR is a fixed shape; upstream's IIR 3-band lets you change gain at runtime, with a different visual feel. Not necessarily worth copying, but the trade-off is worth understanding |
| **Bloom modulating line width, not brightness** | `crt_core.c:399-402, 512-526` | Models the physical "bright scenes broaden the scan line itself" effect; ours uses brightness boost as an approximation |

## 8. Things We Have That Upstream Doesn't

| Feature | Our location |
|---------|-------------|
| **HD_NTSC 12× Fsc oversampling (2048 samples/scanline)** | `Ntsc.cs:73-85` `#if HD_NTSC` ladder — `kPhaseEntries=12`, Hann window scaled to 12/36/108 |
| **Full CRT post-process pipeline (scanline+mask+curvature+phosphor+convergence+vignette)** | `CrtScreen.cs` whole file |
| **GPU SkSL shader path** | `CrtScreen.Gpu.cs` + `crt_core_v1.sksl` (referenced) |
| **Runtime backend dispatch (Scalar/Simd/Gpu)** | `CrtScreen.Shared.cs:31-61` |
| **Three terminal profiles (RF/AV/SVideo)** | `CrtScreen.Shared.cs:81-94` (Beam / Bloom / Brightness) + `Ntsc.cs:129-138` (Noise / Slew / ChromaBlur) |
| **RF herringbone (audio-driven visual buzz)** | `Ntsc.cs:577-587, 759-770` — `RfAudioLevel * 0.06f * sin(line/240+phase)` |
| **Color burst phase jitter** | `Ntsc.cs:730-734`, 1/32 chance ±1 master-tick |
| **Symmetric vs asymmetric I/Q (1953 vs 1960s NTSC standard)** | `Ntsc.cs:210, 374-384` `SymmetricIQ` flag |
| **Color temperature warm/cool** | `Ntsc.cs:194-203, 338-350` `ColorTempR/G/B` triple multiplier |
| **JIT generic-struct specialisation for `analogSize`** | `Ntsc.cs:23-27, 591-598` `Scale2/4/6/8` interface |
| **`[ThreadStatic]` per-worker scratch (avoid per-frame stackalloc)** | `Ntsc.cs:459-463`, `CrtScreen.cs:49-56` |
| **Parallel demod (240 rows on the worker pool)** | `Ntsc.cs:509-522` `Ntsc_FlushPendingRows` |
| **Pre-integrated palette LUT (yBaseE/iBaseE/qBaseE for 64×8)** | `Ntsc.cs:311-326` skips runtime sub-sample summation |
| **Branchless modular wrap (`(threshold - x) >> 31) & wrap`)** | scattered through `Ntsc.cs` |
| **SWAR 32-bit pixel processing (mask/phosphor/convergence)** | `CrtScreen.cs:452-544` |

---

## 9. Final Assessment

The two implementations have **different goals**; "which is better" isn't a meaningful question:

- **Upstream wins on**: portability (C89 + integer-only goes anywhere), simplicity (~1300 LOC core, the whole lib fits in 4 `.c` files), self-containment (zero dependencies), signal-simulation completeness (real sync detection / VHS mode / monochrome). **It's a library**.
- **We win on**: decode precision (HD_NTSC 12× oversampling, Hann FIR, SIMD FMA), performance (parallel + SIMD + GPU three tiers), CRT post richness (9+ independent knobs vs upstream's ~3), zero-overhead emulator integration (`partial class NesCore` directly shares the PPU buffer). **It's an emulator-internal stage**.

**When to use which**:
- Building a generic NTSC filter (e.g. video-effect plugin, an option for another emulator) → use upstream; ship the whole 1300-LOC bundle as-is.
- Building an NES-emulator-specific high-end signal chain that shares buffers with the PPU, runs a GPU shader, and switches between RF/AV/SVideo → use ours.

**Cross-pollination opportunities**:
1. We could pick up `monochrome toggle` and the `raw artifact-color path` from upstream (small effort, fun results).
2. We could mine upstream's "bloom modulates line width" approach as an alternative model for `BloomStrength`.
3. Conversely, upstream already has a downstream AVX fork via BeesNES; if EMMIR ever wants multi-thread or a SkSL GPU path, our `Ntsc.cs` parallel demod and `CrtScreen.Gpu.cs` are ready-made design references.

**Two principle differences worth remembering**:

| Principle | Upstream | Ours |
|-----------|----------|------|
| Signal vs. resolution | Compute the full signal then decode it; CRT simulates the entire receiver chain | "We already know where every dot is and what its phase is" — skip sync detection, spend the budget on more refined demod (Hann FIR + HD oversample) instead |
| Abstraction vs. integration | Strict black-box library, multiple systems share `crt_core` | Partial class, static fields, unsafe pointers, tightly coupled to NesCore |

Both are valid choices — different engineering aesthetics.

---

## Appendix A: Key file:line Index (For Future Navigation)

### Upstream
- `crt_core.h:30-56` — system enum + include dispatch
- `crt_core.h:74-92` — `struct CRT` master state
- `crt_core.c:101-147` — convolution-based EQ (USE_CONVOLUTION=1)
- `crt_core.c:158-233` — IIR 3-band EQ (default)
- `crt_core.c:264-289` — `crt_init` + EQ setup
- `crt_core.c:291-666` — `crt_demodulate` whole body (VSYNC/HSYNC/burst/I-Q wave/EQ/YIQ→RGB/blend)
- `crt_nes.c:21-61` — `square_sample`, NES IRE table
- `crt_nes.c:82-104` — `setup_field` (vertical sync written once)
- `crt_nes.c:106-201` — `crt_modulate` NES_OPTIMIZED path
- `crt_nes.h:65-104` — NES line-timing comments + constants
- `crt_ntsc.c:32-83` — fixed-point `expx()`
- `crt_ntsc.c:90-126` — IIR low-pass for bandlimit
- `crt_ntsc.c:128-330` — `crt_modulate` NTSC path (with even/odd field equalising pulses)
- `crt_ntscvhs.h:102-124` — VHS SP/LP/EP bandwidth modes

### Ours
- `Ntsc.cs:73-85` — HD_NTSC compile switch
- `Ntsc.cs:96-123` — phase step constants (HD scaling)
- `Ntsc.cs:212-336` — `Ntsc_Init` precomputes all LUTs (64×8 yBaseE/iBaseE/qBaseE, emphAtten, combinedI/Q)
- `Ntsc.cs:490-522` — `Ntsc_CaptureScanline` + `Ntsc_FlushPendingRows` (PPU-thread snapshot + parallel demod entry)
- `Ntsc.cs:535-561` — `DecodeScanline_Fast` (skip-waveform fast path)
- `Ntsc.cs:563-711` — `DecodeAV_Composite` / `DecodeAV_SVideo` + `DispatchDecodeLoop<TScale>` JIT specialisation
- `Ntsc.cs:713-892` — `DecodeScanline_Physical` + `GenerateWaveform` (full LTI signal reconstruction)
- `Ntsc.cs:976-1118` — `DemodulateRow_Core` (Hann FIR + Vector<T> SIMD)
- `Ntsc.cs:1122-1128` — `YiqToRgb` (gammaLUT 12-bit fixed-point)
- `CrtScreen.Shared.cs:30-156` — backend dispatch + shared config
- `CrtScreen.cs:95-137` — `PrecomputeScanlineWeights` (Gauss + jitter)
- `CrtScreen.cs:139-190` — `PrecomputeCurvature` (reverse map)
- `CrtScreen.cs:192-247` — `ApplyHorizontalBlur` (3-tap SIMD)
- `CrtScreen.cs:249-401` — `Render` (Parallel.For main loop)
- `CrtScreen.cs:452-544` — SWAR mask / phosphor / convergence
- `CrtScreen.cs:546-623` — `ApplyFullFrameCurvatureAndConvergence`
- `CrtScreen.Simd.cs:1-1005` — .NET 10 + AVX2 explicit fork
- `CrtScreen.Gpu.cs:1-203` — SkiaSharp `SKRuntimeEffect` GPU path

---

## Appendix B: LOC Statistics

| File | LOC | Role |
|------|-----|------|
| **Upstream core** | | |
| `crt_core.h` | 145 | Public struct + enum + sin/cos API |
| `crt_core.c` | 666 | Demod whole body (incl. both EQ filter variants) |
| `crt_nes.h` | 149 | NES system timing constants |
| `crt_nes.c` | 310 | NES encoder + `square_sample` |
| `crt_ntsc.h` | 130 | Standard NTSC system constants |
| `crt_ntsc.c` | 331 | Standard NTSC encoder (with even/odd field) |
| `crt_main.c` | 557 | Demo CLI (not part of the lib) |
| **Upstream core LOC total (excl. main)** | **1731** | |
| **Ours** | | |
| `Ntsc.cs` | 1129 | NTSC mod + demod (NES-specific) |
| `CrtScreen.Shared.cs` | 156 | Backend dispatch + shared config |
| `CrtScreen.cs` | 624 | Scalar backend |
| `CrtScreen.Simd.cs` | 1005 | SIMD backend (.NET 10) |
| `CrtScreen.Gpu.cs` | 203 | GPU backend (SkSL) |
| **AprNes core LOC total** | **3117** | |

LOC ratio ≈ 1.8×, but `CrtScreen.Simd.cs` is a SIMD-rewritten fork of `CrtScreen.cs`; subtracting those 1005 lines, the additional functional code is ~2112 lines, only ~1.2× larger than upstream — reasonable, given we add Hann FIR / HD_NTSC / GPU shader / 9-knob CRT post / three profiles / parallel + JIT specialisation.
