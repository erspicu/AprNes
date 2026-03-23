# AprNes CRT Television Simulation

> **Last revised**: 2026-03-23 19:27
>
> **Revision notes** (2026-03-23): Corrected 5 documentation errors based on NESdev forum feedback:
> 1. **BrightnessBoost**: Removed incorrect claim that "CRT circuits compensate by increasing drive voltage"; replaced with Gaussian beam overlap naturally filling gaps, boost compensating for discrete simulation brightness loss
> 2. **I/Q bandwidth asymmetry**: Added note that implementation follows the 1953 NTSC spec, but consumer TVs since the 1960s switched to symmetric quadrature demodulation (citing Poynton)
> 3. **Interlace Jitter**: Labeled as aesthetic effect — NES outputs 240p progressive; no physical mechanism for inter-field jitter exists
> 4. **Phosphor Persistence**: Labeled as artistic enhancement; added real P22 phosphor decay time data (far faster than simulated values)
> 5. **Beam Convergence**: Added nuance about variation by TV quality and age; cited SMPTE RP 167 acceptable tolerances
>
> The above are documentation corrections. Where the implementation itself is incorrect, code fixes will follow as appropriate.

## Introduction

Before LCD screens became ubiquitous, every home game console connected to a CRT (cathode-ray tube) television via analog signals. The NES did not output the crisp digital pixels we're familiar with today — it produced an **NTSC composite video** waveform, using the same standard as broadcast television of the era.

This means that when NES games were being designed, the artists saw a picture inherently shaped by all the "imperfections" of CRT televisions: blurred color transitions, scanline brightness patterns, color fringing at screen edges... These were not defects but part of the visual experience. Many games' artwork deliberately exploited these characteristics.

AprNes's analog mode faithfully simulates the entire signal chain from the NES to the CRT television, recreating the visual experience of that era.

### Effect Showcase

![CRT simulation showcase — Super Mario Bros. 3 world map](result.png)

*Level 3 UltraAnalog + RF connector, full effect. Observable: scanline texture (horizontal brightness bands), bloom (bright areas filling scanline gaps), screen curvature (subtle edge bending), vignette (darkened corners), color bleed (soft color transitions), RF snow noise (subtle brightness flicker), shadow mask (RGB phosphor stripes visible when zoomed in). All effects are natural results of physical simulation, not image filters.*

---

## Technical Highlights: Physical Simulation vs Image Filters

Most emulators' "CRT effects" are essentially **image post-processing filters** — they first render a clean digital pixel image, then "decorate" it to look like a CRT using blur, scanline overlays, noise, and similar techniques. It's like applying a vintage filter to a photograph: it captures the feel, but the details don't hold up under scrutiny.

AprNes takes a fundamentally different approach: **simulating the actual physical processes at the circuit level**.

### Real NTSC Signal Generation and Demodulation

AprNes doesn't "paint" analog effects onto digital pixels — it genuinely converts NES palette data into a **21.477 MHz NTSC composite waveform**, producing 4 samples per pixel. Each scanline is a continuous time-domain waveform of 1024 floating-point values. It then "decodes" this waveform the way a real television would:

- **Hann-windowed FIR coherent demodulation**: Luminance (Y) uses a 6-tap window, chrominance I uses 18-tap, Q uses 54-tap — doing exactly what the bandpass filters inside real TV chips do
- **IIR low-pass filtering**: SlewRate and ChromaBlur simulate the frequency response of analog circuits built from capacitors and inductors

This means dot crawl, color bleeding, and luminance blur are not effects that are "added on" — they are physical results that **naturally emerge** from the signal processing. If you change signal parameters (such as switching between RF / AV / S-Video), all these phenomena shift in intensity accordingly — because they are all different facets of the same set of physical equations.

### Ringing from Second-Order Dynamics, Not Edge Detection

Filter-based approaches typically detect brightness edges and then "draw" a bright line next to them. AprNes's ringing effect comes from a **damped spring model** — the signal tracker has "velocity" and "inertia," overshooting the target value when encountering abrupt signal changes and then bouncing back, just like the transient response of inductors and capacitors in real circuits. The shape, frequency, and decay rate of the ringing are all naturally determined by the physics equations, not hand-drawn.

### RF Herringbone from a Real Oscillator

The diagonal interference pattern in RF mode is not a pasted texture. The program contains a **4.5 MHz recursive sine oscillator** that rotates phase at the precise physical frequency sample-by-sample, modulated by a 60 Hz audio envelope. The resulting interference signal is added directly to the composite waveform and passes through demodulation and filtering together — so the herringbone pattern interacts with picture content, changing its appearance as it crosses color boundaries, just like real RF interference.

### Directional Attenuation of Emphasis Bits

The NES emphasis feature (tinting the picture red/green/blue) works at the hardware level by reducing the subcarrier voltage at specific phases. Most emulators simplify this to "multiply overall brightness by a coefficient," but AprNes builds an **attenuation lookup table for 8 emphasis combinations × 12 phases**, then pre-computes the precise YIQ values for each combination via Fourier decomposition. The result is that emphasis not only changes brightness but also correctly affects hue and saturation in a directional manner.

### Scanlines Are Gaussian Beam Profiles, Not Stripe Patterns

Filter-based scanlines typically add a semi-transparent dark line every other row. AprNes's scanlines come from a **Gaussian beam spot model** `exp(-dy²/2σ²)` — each scanline's brightness falls off from center following a Gaussian shape determined by beam focus (σ). Combined with bloom, high-brightness scanlines naturally "swell" to fill the gaps while low-brightness ones remain clearly separated — this is why bright white text on a CRT appears to have almost no scanlines, while dark backgrounds show them distinctly.

### Phosphor Persistence is Per-Channel Decay

Phosphor persistence is not simple motion blur (blending consecutive frames). On a CRT screen, the decay of R/G/B phosphors is independent — red phosphor may decay faster than blue. AprNes implements **per-channel `max(current, previous × decay)`**: each color channel independently compares the current frame's value with the previous frame's decayed value and takes the brighter one. This means:
- Afterimage colors gradually shift as they decay (because channels decay at different rates)
- Trailing occurs in the direction of movement, not symmetrical blur
- Static images are completely unaffected (max ensures no darkening)

### Convergence Follows CRT Geometry

Electron gun convergence error is not a uniform RGB offset across the entire screen. In a real CRT, three electron guns are arranged in a triangle; convergence at the screen center is precisely factory-calibrated, but the edges cannot be perfectly compensated. AprNes's implementation makes the offset **increase linearly with distance from screen center** — zero deviation at the exact center (perfect convergence), with increasing horizontal displacement of R and B channels toward the edges. This matches the geometric optics of real CRTs.

### Connector Differences from a Single Physical Model

RF, AV, and S-Video are not three independent filter presets. They share the same signal processing pipeline with only different physical parameters — RF has more noise and narrower bandwidth; S-Video completely separates luminance and chrominance, eliminating dot crawl. When switching connectors, all effect changes are coherent and physically consistent, rather than "switching to another set of hand-tuned parameters."

### Three-Level Rendering Architecture

AprNes offers three rendering levels, from clean digital pixels to full physical simulation. Users can freely switch based on preference and hardware performance:

**Level 1 — Digital Pixels (Analog OFF)**

Analog mode disabled. NES palette values are directly converted to RGB pixel output — sharp, block-colored, exactly what a typical emulator produces by default. Suitable for users who prefer clean visuals or have lower-end hardware. No signal processing or CRT effects whatsoever.

**Level 2 — Fast Analog (Analog ON, UltraAnalog OFF)**

The fast path of analog mode. NES palette values are converted to YIQ color space via lookup table, with a 6-phase subcarrier LUT simulating dot crawl, then output through IIR filters. Several times faster than Level 3, already featuring dot crawl, color blur, luminance response lag, ringing, HBI, RF noise, and herringbone.

This level **does not pass through the CRT display layer** — no scanlines, bloom, shadow mask, phosphor persistence, or screen curvature. The picture looks like "digitizing an analog signal directly" — it has the flavor of analog signals without the texture of a television. Suitable for users who want NTSC color characteristics without the CRT appearance.

**Level 3 — UltraAnalog Full Physical Simulation (Analog ON, UltraAnalog ON)**

The highest-fidelity complete simulation. First generates a 21.477 MHz NTSC composite waveform (4 samples per pixel), then uses Hann-windowed FIR coherent demodulation to separate Y/I/Q, outputting to a linear RGB buffer. The signal then passes through the CRT display layer (Stage 2), applying scanline beam profiles, bloom, horizontal beam spread, phosphor persistence, shadow mask, beam convergence, screen curvature, and all other television-side effects.

This is the level where all physical effects described in this document are fully operational. Every link in the chain from "NES hardware output" to "light on the CRT screen" has a physical counterpart. Highest performance cost, but also the closest to the real experience.

**Relationship between the three levels**:

```
Level 1:  NES palette ──→ RGB pixels ──→ screen
Level 2:  NES palette ──→ YIQ LUT ──→ IIR filtering ──→ RGB pixels ──→ screen
Level 3:  NES palette ──→ 21MHz waveform ──→ FIR demod ──→ linear RGB ──┬→ CRT display ──→ screen (CRT ON)
                          Stage 1                                        └→ direct output ──→ screen (CRT OFF)
                                                                             Stage 2
```

Each level adds another layer of physical simulation, bringing the visuals closer to reality. But even Level 2 already far exceeds most emulators' Blargg LUT filters — because it has full IIR state continuity and second-order ringing dynamics.

### Two-Stage Separation Architecture and CRT Toggle

Level 3's pipeline consists of two **completely independent** stages:

```
Stage 1 (Ntsc.cs)                      Stage 2 (CrtScreen.cs)
Signal gen → Demod → Linear RGB  ──→   Scanlines → Bloom → Mask → Persistence → Curvature → Output
              ↓ (CRT OFF)
         Direct output to screen
```

Stage 1 handles the physics of "NES to TV cable" — waveform generation, coherent demodulation, IIR filtering, ringing, RF interference, etc. Stage 2 handles the physics of "TV cable to light on screen" — scanline beam profiles, bloom, shadow mask, phosphor persistence, curvature, etc.

AprNes provides a **CRT display toggle** (`CrtEnabled`) that lets users independently disable Stage 2 while in Level 3. This option doesn't exist in typical emulators — they usually treat "NTSC filter" and "CRT effects" as a single unit, all-on or all-off.

**Level 3 + CRT ON**: Complete two-stage physical simulation, full signal-to-screen chain — the mode closest to the real CRT experience.

**Level 3 + CRT OFF**: Only executes Stage 1's high-precision NTSC signal demodulation, skipping all CRT television-side effects. The picture shows what "viewing an NTSC signal on a perfect monitor" would look like — complete dot crawl, color blur, ringing, and RF interference, but no scanlines, bloom, shadow mask, or screen curvature.

This separation design provides several unique advantages:

- **Precise effect control**: Users can enjoy high-precision NTSC color without scanlines obstructing the view, or conversely, observe CRT effect differences across different connectors
- **Performance flexibility**: Stage 2 includes multiple pixel-level post-processing operations (shadow mask, curvature remapping, phosphor decay); disabling it significantly reduces computation while retaining all Stage 1 signal-layer physics
- **Combinatorial freedom**: Level 2 (fast IIR), Level 3 + CRT OFF (precise demodulation without CRT), Level 3 + CRT ON (full physics) — three modes covering the complete spectrum from performance-first to fidelity-first, each physically consistent rather than a patchwork of filters

---

## Comparison with Common NTSC/CRT Filters

Several well-known NTSC or CRT effect solutions exist. The following compares their underlying principles, explaining why AprNes's approach is fundamentally different in terms of physical correctness.

### Blargg NTSC Video Filter (nes_ntsc / snes_ntsc)

Developed by Shay Green (blargg), this is currently the most widely used NTSC filter library, adopted by well-known emulators including Nestopia and Mesen.

**Principle**: Pre-computes a massive lookup table (LUT) that, for each pair of adjacent NES palette values, directly looks up the blended RGB output. Each NES pixel always produces exactly 3 screen pixels.

**What it achieves**:
- Dot crawl
- Color blending between adjacent pixels (composite artifact colors)
- Basic luminance/chrominance separation residuals

**What it cannot do**:
- **No IIR state continuity**: Each pixel pair's computation is independent; there is no "filter state from the previous pixel." Real TV analog circuits have memory — the brightness of the preceding pixel affects the starting state of the next. Blargg's lookup table cannot express this
- **No CRT display effects**: No scanlines, bloom, shadow mask, phosphor persistence, or any television-side simulation. It only handles "signal decoding," not "screen display"
- **Fixed resolution**: Output is always 3:1 ratio, not adjustable
- **No RF-specific effects**: No noise, herringbone, audio interference
- **No ringing / No HBI**: Lookup tables cannot produce transient responses

Blargg's filter is an excellent fast approximation, but it is fundamentally "simulating the result via lookup" rather than "reproducing the physical process that produces the result."

> **Fun fact**: AprNes's signal source uses the NES PPU voltage level data researched by blargg (loLevels / hiLevels) — currently the most accurate NES analog output measurements available. Blargg's LUT filter and AprNes's time-domain simulation share the same signal origin but take completely different decoding paths.

### RetroArch CRT Shaders (CRT-Royale / CRT-Geom / zfast-crt)

RetroArch provides a series of GPU shader-based CRT effects, the most well-known being CRT-Royale (most complete) and CRT-Geom (lightweight).

**Principle**: Post-processing on the GPU applied to **already-decoded digital RGB images** — overlaying scanline textures, Gaussian blur for bloom, barrel distortion for curvature, texture mapping for shadow masks.

**What they achieve**:
- Scanlines, bloom, screen curvature, shadow mask
- CRT-Royale additionally has halation and phosphor mask
- Visual effects can look very convincing in static screenshots

**What they cannot do**:
- **No NTSC signal-domain simulation whatsoever**: Their input is clean digital pixels, so there will be no dot crawl, color blur, luminance lag, or any signal-layer effects. A red block next to a black background remains a sharp boundary after shader processing — something impossible on a real CRT
- **Scanlines are "painted on"**: Typically every N-th row is multiplied by a sine or step function to darken it, regardless of brightness. Real scanline gap brightness depends on pixel brightness — bright areas have gaps filled by bloom, dark areas show clear gaps
- **No inter-frame state**: Some shader architectures cannot read the previous frame's result, making phosphor persistence impossible

Some users chain Blargg NTSC filter with CRT shaders (Blargg for signal processing, then shader for display). This is better than using either alone, but the two stages' parameters are adjusted independently, unlike real hardware where a single set of physical laws drives everything coherently.

### Other Emulators' Built-in NTSC Effects

Most emulators (FCEUX, Mesen, etc.) call Blargg's nes_ntsc library directly for NTSC effects, making them fundamentally identical to the analysis above. A few emulators have their own implementations, but these are usually LUT-based or simplified FIR filtering.

### Comparison Summary

| Feature | Blargg LUT | CRT Shader | Blargg + Shader | **AprNes Lv2** | **AprNes Lv3** |
|---------|:----------:|:----------:|:---------------:|:--------------:|:--------------:|
| NTSC signal-domain sim | LUT approx | None | LUT approx | **IIR time-domain** | **FIR+IIR time-domain** |
| IIR state continuity | None | None | None | **Full scanline** | **Full scanline** |
| Dot crawl | Yes | None | Yes | **Naturally emergent** | **Naturally emergent** |
| Ringing / Gibbs | None | None | None | **2nd-order dynamics** | **2nd-order dynamics** |
| Scanlines | None | Sine overlay | Sine overlay | None | **Gaussian beam** |
| Bloom (brightness-dependent) | None | Partial | Partial | None | **Physically coupled** |
| Phosphor persistence | None | Rarely | Rarely | None | **Per-channel decay** |
| RF interference | None | None | None | **4.5MHz oscillator** | **4.5MHz oscillator** |
| Connector switching | Fixed | None | Manual preset swap | **Same pipeline** | **Same pipeline** |
| Signal↔display coherence | — | — | Separate | Signal layer complete | **Two-stage physically consistent** |
| Signal/CRT independent toggle | None | None | Manual disassembly | — | **Stage 2 independently toggleable** |

AprNes's core difference is not "more effects" but that **all effects are different facets of the same set of physical equations**. Adjusting one parameter causes all related phenomena to shift naturally — because that's how the real world works. Even the faster Level 2 already exceeds the Blargg LUT approach in signal-layer physical correctness. Level 3 further adds the complete CRT display layer, achieving full-chain physical simulation from signal to screen.

---

## Signal Chain Overview

Below is the complete signal chain for Level 3 (UltraAnalog) — the full path of real hardware:

```
NES Console → NTSC Encoding → Cable (RF/AV/S-Video) → CRT Television Display
              Stage 1: Ntsc.cs                         Stage 2: CrtScreen.cs
```

- **Stage 1 (Signal Processing)**: Simulates how the NES encodes its picture into a television signal, and the various effects of cable transmission. Both Level 2 and Level 3 pass through this stage; the difference is Level 2 uses fast LUT approximation while Level 3 performs full waveform generation and demodulation
- **Stage 2 (CRT Display)**: Simulates how the CRT television "paints" the signal onto the screen. Only Level 3 enters this stage — scanlines, bloom, shadow mask, phosphor persistence, and all television-side effects happen here

Users can choose three connection types (applicable to both Level 2 and Level 3), from lowest to highest quality:
- **RF (antenna cable)**: Most noise and interference, but the most "retro" feel
- **AV (yellow-white-red cables)**: Medium quality, most people's childhood memory
- **S-Video**: Cleanest, best color separation

---

## Core Analog Engine

The following describes the complete engine architecture under Level 3 (UltraAnalog) mode. These form the foundation of the entire physical simulation system; all subsequent effects are built upon them.

### NTSC Composite Waveform Generation

The NES's PPU (picture processing unit) does not output RGB — it outputs an analog waveform that combines luminance and color. AprNes precisely reconstructs this process: at a 21.477 MHz sampling rate, generating 4 floating-point samples per pixel, encoding color information into the waveform using a 6-phase subcarrier. Each scanline is a continuous 1024-sample floating-point waveform, mathematically equivalent to the signal that real NES hardware outputs onto the television cable.

Signal voltage levels use blargg's measured NES PPU data: 4 brightness levels (loLevels / hiLevels), with 16 hues each corresponding to different subcarrier phase amplitudes. These are not approximations but measurements taken from real hardware.

### Coherent Demodulation (Decoding Like a Real Television)

After generating the composite waveform, the emulator plays the role of a "television," using coherent demodulation to separate luminance (Y) and chrominance (I, Q):

- **Y channel**: 6-tap Hann-windowed FIR low-pass, retaining only luminance
- **I channel**: 18-tap Hann-windowed FIR bandpass, with in-phase subcarrier multiplication demodulation
- **Q channel**: 54-tap Hann-windowed FIR bandpass, with quadrature subcarrier multiplication demodulation

I and Q have different bandwidths (I is wider, Q is narrower) — this follows the original 1953 NTSC standard (FCC Title 47 §73.682), which specified I: 0–1.3 MHz and Q: 0–0.5 MHz. However, as Charles Poynton documents in "Digital Video and HD: Algorithms and Interfaces" (2nd ed., §22.5), by the early 1960s virtually all consumer TV sets switched to symmetric quadrature demodulation with ~0.5 MHz bandwidth for both chroma axes (B-Y/R-Y rather than I/Q), as asymmetric demodulation was more expensive to implement with minimal perceptual benefit. The emulator currently faithfully reproduces the 1953 standard's asymmetry (kWinI=18, kWinQ=54), but this does not match what NES-era (1985–1995) consumer TVs actually did. Blargg's widely-used NES NTSC filter also uses symmetric bandwidth.

### IIR Analog Circuit Simulation

The filter circuits built from capacitors and inductors inside real televisions are not ideal digital filters — they have "inertia." The emulator uses two IIR (Infinite Impulse Response) filters to simulate this:

- **SlewRate**: Bandwidth limit of the luminance channel, determining the speed of brightness transitions. Lower values mean slower transitions (blurrier picture)
- **ChromaBlur**: Low-pass filtering of the chrominance channel, determining color boundary sharpness

These two filters' states are continuous across the entire scanline — the filter state at the 100th pixel depends on the history of the preceding 99 pixels, just like a real circuit.

### Three Connector Types

The same simulation engine, through different physical parameter combinations, reproduces the differences between three connection types:

| Connector | Noise | Luma Bandwidth | Chroma Bandwidth | Distinctive Phenomena |
|-----------|:-----:|:--------------:|:----------------:|----------------------|
| **RF** | High | Narrow | Narrow | Herringbone, audio interference, color burst jitter |
| **AV** | Minimal | Medium | Medium | Standard dot crawl |
| **S-Video** | None | Wide | Wide | No dot crawl (Y/C transmitted separately) |

S-Video has no dot crawl not because "dot crawl was turned off" but because luminance and chrominance travel on separate wires from the start — the mixing problem simply doesn't exist. This is the fundamental difference between physical simulation and image filters: the presence or absence of effects is determined by physical structure, not by switches.

### Gaussian Scanline Beam Profile

The electron beam hitting the CRT screen does not form a line but a Gaussian-distributed spot `exp(-dy²/2σ²)`. The emulator maps the NES's 240 scanlines to higher-resolution output (supporting 2x/4x/6x/8x), with each output pixel's brightness determined by Gaussian weights from the nearest scanlines. σ (beam focus) varies by connector type: RF has a wider beam (blurrier), S-Video has the narrowest (sharpest).

### Bloom (Highlight Overflow)

The more energy in the electron beam, the larger the spot. The emulator calculates each pixel's brightness — the brighter it is, the further the Gaussian weight's "tail" extends, filling the dark gaps between scanlines. The effect: bright white areas show almost no scanlines (the beam spot fills the gaps), while dark areas show clearly defined scanline texture.

### BrightnessBoost (Brightness Compensation)

The dark gaps between scanlines make the overall picture appear darker than the original signal. On a real CRT, the Gaussian beam profile's tails spread beyond the intended scanline, causing adjacent scanlines to physically overlap — so the dark gaps are less pronounced than in a discrete digital simulation. The emulator uses a BrightnessBoost coefficient to compensate for the inherent brightness loss of discrete simulation, a standard practice in CRT shaders (CRT-Royale, CRT-Geom, etc. all use a similar brightness compensation factor), ensuring analog mode's overall brightness matches non-analog mode.

### Multi-Resolution Rendering

Output resolution supports 2x (512×420), 4x (1024×840), 6x (1536×1260), 8x (2048×1680), all maintaining the NES's native 8:7 pixel aspect ratio. Higher resolutions produce finer scanline texture and more realistic shadow mask patterns, but at greater performance cost. Horizontal rendering is accelerated using SIMD vectorization (SSE2/AVX2).

---

## Visible Effects

The above is the engine driving everything. The following describes each phenomenon users actually see on screen — all of them are results of the physical simulation described above, not separately "added on."

### Signal Layer (The Radio Wave World You Can't Hear But Can See)

#### Dot Crawl
The NES's luminance and color information are carried on the same signal. The television cannot perfectly separate them, so small crawling bright dots appear at color edges — that's "dot crawl." Look closely at red text on an old TV, and you'll see tiny dots slowly moving along the edges.

#### Chroma Blur
The color signal has much narrower bandwidth than the luminance signal, so color changes always lag "half a beat" behind brightness changes. The practical effect is that color block boundaries are always soft gradients — never sharp color boundaries.

#### Luminance Response Lag (Slew Rate)
When the signal jumps from dark to bright (or vice versa), it doesn't happen instantly — there's a "climbing" process. This creates soft transitions at brightness boundaries, eliminating the hard pixel edges of digital images.

#### Ringing
When the signal undergoes abrupt brightness changes (such as a white object next to a black background), it doesn't just transition slowly — it "overshoots" and bounces back, producing several decaying oscillations like a spring. This creates faint bright "halos" next to sharp edges, a physical characteristic of analog transmission.

#### Horizontal Blanking Interval Effect (HBI)
After scanning one line, the TV's electron gun must fly back to the left to begin the next line. During this "return trip," the signal is at blank level. When a new line begins, the circuit starts from blank level and needs a few pixels' worth of time to "warm up" to the correct brightness — so the leftmost few pixels of each line are slightly darker.

#### RF Herringbone
When connected via RF antenna cable, the audio carrier (4.5 MHz) leaks into the video signal, creating fine diagonal stripes across the picture. These stripes fluctuate with the game music's volume — prominent when the music is loud, nearly invisible when quiet.

#### RF Snow Noise
RF transmission introduces random noise, appearing as subtle "snow" flicker on screen. AV cables also have trace amounts of noise; S-Video has virtually none.

#### Color Burst Jitter
In RF mode, the "color burst" signal used by the TV to calibrate color occasionally experiences tiny phase jumps, causing the entire line's hue to shift slightly. This occurs roughly once every 30 lines, with very subtle effect.

#### Emphasis Color Enhancement
The NES hardware has a lesser-known feature: selectively enhancing red, green, or blue display. This works by modifying the signal voltage at specific phases; the emulator precisely reproduces this per-phase directional attenuation.

---

### Color and Gamma

#### Adjustable Color Temperature
Different eras and brands of CRT televisions produced different "warmth" of white. Japanese TVs leaned blue-white (9300K), while Western standards leaned warm-white (6500K). Users can adjust the RGB ratios to recreate the color tone from their memories.

#### Gamma Correction
CRT television brightness response is not linear — doubling the input voltage doesn't exactly double the brightness. This nonlinearity is called Gamma; it gives shadows more depth and highlights more punch. The emulator uses an adjustable coefficient to precisely match this curve.

---

### CRT Television Layer (Light and Shadow on the Glass Screen)

The principles of scanlines and bloom were explained in the "Core Analog Engine" section. The following are additional display effects built on that foundation.

#### Horizontal Beam Spread
The electron beam has width not only vertically but horizontally as well. This causes light from adjacent pixels to bleed into each other, producing a subtle horizontal softening effect. At higher brightness, the beam spot is larger and blur more pronounced.

#### Phosphor Persistence [Artistic Enhancement]
P22 phosphor (standard for color CRT TVs) has approximate persistence times to 10% brightness: Red (Y₂O₂S:Eu) ~1–3 ms, Green (ZnS:Cu,Al) ~1 ms, Blue (ZnS:Ag) ~20–60 μs. At 60 Hz NTSC, the frame period is 16.7 ms — after one full frame, even the slowest P22 component has decayed to well below 1%, essentially invisible. The emulator's PhosphorDecay = 0.6 (60% carryover per frame) is vastly exaggerated compared to real P22 physics. This feature is an artistic enhancement, similar to the "ghosting" option in many CRT shaders, providing a visual style some users enjoy, but it is not a faithful reproduction of real P22 phosphor behavior.

#### Interlace Field Jitter [Aesthetic Effect]
The NES outputs 240p progressive video (262 lines/frame, non-interlaced). Every frame starts at the same vertical position — there are no alternating fields with half-scanline offsets. The CRT vertical deflection circuit simply follows the sync pulses, which are identical every frame for 240p. There is no physical mechanism for "inter-field jitter" on a progressive signal. This feature is a purely aesthetic/artistic effect, not a faithful simulation of hardware behavior. For authentic slow visual oscillation, one should simulate the beat frequency between mains power (60 Hz) and NES vsync (60.0988 Hz), which produces a ~17-second beat pattern known as the "hum bar" effect.

#### Shadow Mask / Aperture Grille
Each "pixel" on a CRT screen actually consists of red, green, and blue phosphor dots separated by a metal mask. The emulator provides two types:
- **Aperture Grille**: Vertical RGB stripes, representing models like the Sony Trinitron
- **Shadow Mask**: Triangularly arranged RGB dots, with even and odd rows staggered

#### Screen Curvature
CRT television screens are not flat — they bulge slightly outward. This produces mild barrel distortion — objects at the center appear slightly larger than at the edges. The curvature is more noticeable on larger screen TVs.

#### Vignette (Edge Darkening)
CRT screen edges and corners are darker than the center. This is because the electron beam must deflect at greater angles to reach the screen edges — longer path, more dispersed energy. The effect is brightest at center, gradually darkening toward the periphery.

#### Beam Convergence
CRT televisions have three electron guns responsible for red, green, and blue respectively. Ideally, all three beams should hit exactly the same spot. Quality CRT TVs had static and dynamic convergence adjustments, and a well-calibrated set would show minimal convergence error. However, many cheap consumer TVs (the kind most NES players used) had mediocre convergence, especially at edges/corners; convergence also degraded over time as components aged. Even well-adjusted TVs typically showed 0.5–2mm misconvergence at screen edges (per SMPTE RP 167 acceptable tolerances). The default ConvergenceStrength = 2.0 pixels is on the high side for a well-maintained TV but reasonable for an aged or cheap set. Convergence is best at the screen center and worsens toward the edges; this parameter is adjustable (including 0 = off).

---

## Effects Reserved (Not Implemented)

The following effects were evaluated and deliberately excluded from the simulation. The reason is not technical limitation but **unfavorable cost-benefit ratio** — they either produce minimal visual difference or don't belong to the essential characteristics of CRT televisions.

### Comb Filter
**What is it?** Higher-end CRT televisions use cross-line comparison to more precisely separate luminance and chrominance signals, at the cost of introducing vertical color blur.

**Why not implement it?** This essentially simulates "high-end TV" vs "budget TV" differences. The current single-line demodulation corresponds to the budget TVs most households used, and the presence of dot crawl is actually closer to most people's memories. Additionally, implementation would require major changes to the existing line-independent demodulation architecture, with the highest performance cost of all items.

### Precise Subcarrier Frequency
**What is it?** The ratio of NTSC's color subcarrier frequency to sampling frequency is irrational (approximately 5.9966:1), but the emulator simplifies it to an integer 6:1.

**Why not implement it?** The difference is only 0.057%, requiring hundreds of frames to accumulate a perceptible phase shift. In actual gameplay, this difference is completely imperceptible to the human eye.

### RF Tuner / IF Chain
**What is it?** Real RF reception from antenna to screen passes through: tuner → intermediate frequency amplification → envelope detection → composite video, with each stage having its own frequency response and noise characteristics.

**Why not implement it?** The current RF simulation (noise + audio interference + herringbone) already captures the main visual characteristics of RF transmission. A complete IF chain would only add subtle frequency response curve differences, effects that largely overlap with adjusting existing parameters.

### Multipath Ghosting
**What is it?** When RF signals are reflected by walls or buildings, the delayed copies superimpose on the original signal, producing semi-transparent offset ghost images on screen.

**Why not implement it?** Ghosting is a "reception environment" issue, not an essential characteristic of CRT televisions or NTSC signals. It depends on antenna position, room structure, and other external factors — everyone's experience is different. Moreover, most players' gaming memories don't include ghosting — if ghosting was severe enough to affect gameplay, they would have adjusted the antenna back then.

---

## Statistics

| Category | Count | Description |
|----------|:-----:|-------------|
| Core analog engine | 8 | NTSC waveform generation, coherent demodulation, IIR filtering, three connectors, scanlines, bloom, brightness compensation, multi-resolution |
| Advanced physical effects | 14 | Signal layer 9 + color layer 2 + CRT display layer 8 (some cross-layer), all completed |
| Reserved (not implemented) | 4 | Low visual impact or not essential characteristics |

All effects can be adjusted in intensity or completely disabled via parameters. Combined with the three-level rendering and independent CRT toggle, users can freely choose their preferred combination between "authentic retro feel" and "clean modern picture," as well as between "NTSC color only" and "full CRT experience."
