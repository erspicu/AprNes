# AprNes AudioPlus Sound System Specification

> This document is written for two audiences: general users who want to understand what each mode does and what the settings mean, and technical readers who want to dive into the signal flow and gain calculations.

---

## 1. Design Philosophy: Why Three Modes?

The NES was born in 1983. Its five audio channels run at 1,789,773 Hz. Four decades later, everyone has a different idea of what "NES audio should sound like" — some want the lightest, most standard emulator experience; some want to relive the sound that came out of their childhood TV speakers; and some want to reinterpret these classic soundtracks with modern audio engineering.

AprNes's AudioPlus engine offers three modes. It's not about better or worse — it's about **what you want to hear**.

---

## 2. Three Modes at a Glance

| Mode | Name | One-line Description |
|------|------|---------------------|
| Mode 0 | **Pure Digital** | Standard emulator approach, lowest CPU overhead, straightforward |
| Mode 1 | **Authentic** | Recreates the analog circuit characteristics of real hardware |
| Mode 2 | **Modern** | Per-channel mixing + stereo + spatial effects, full creative control |

```
             APU (1,789,773 Hz, 5 channels + expansion audio)
                       |
          +------------+------------+
          |            |            |
     Mode 0        Mode 1       Mode 2
   Pure Digital   Authentic     Modern
          |            |            |
   LUT mixing     3D DAC LUT    5+N track normalize
   DC Killer      90Hz HPF      per-ch gain
          |            |            |
   ~41cyc/sample  256-Tap FIR   (5+N) x 256-Tap FIR
   (simple decim) (oversampled)  (per-track oversample)
          |            |            |
          |       Console LPF   Triangle Bass Boost
          |       + Buzz + RF    + Stereo Pan
          |            |         + Haas + Reverb
          |            |            |
     Dual Mono    Dual Mono    True Stereo
          |            |            |
          +------------+------------+
                       |
                  Soft Limiter
                       |
              WaveOutPlayer / Audio Output
            44.1 kHz / 16-bit / Stereo
```

---

## 3. Mode 0: Pure Digital — Standard Emulator Experience

### Design Intent

This is the approach used by most NES emulators: mix the five channels' digital output through lookup tables, apply minimal processing, and send it straight to the speakers. No fancy filtering, no post-processing.

The reason to choose this mode is simple: **it's fast, it's direct, and it's what many people remember emulators sounding like** — a bit rough around the edges with that digital character, but clean and punchy. For performance-sensitive scenarios, or when you just want to play the game without audio effects, Pure Digital is the right choice.

### Signal Processing Flow

1. **Non-linear lookup table mixing**: The NES DAC is not a linear adder. Pulse 1 + Pulse 2 are combined into a `SQUARELOOKUP[31]` table; Triangle + Noise + DMC are combined into a `TNDLOOKUP[203]` table (index formula: `3*tri + 2*noise + dmc`). This is the standard approximation documented on the [NESdev Wiki — APU Mixer](https://www.nesdev.org/wiki/APU_Mixer).
2. **Expansion audio summation**: Mapper expansion channels (VRC6, VRC7, N163, etc.) are summed as integers and added to the lookup table result.
3. **DC Killer**: A simple high-pass filter removes DC offset, preventing the speaker cone from being pushed to one side.
4. **Master volume scaling**: Multiplied by the user's volume setting, clamped to 16-bit signed range.
5. **Output**: Dual Mono (left and right channels identical).

### User-Adjustable Settings

| Setting | Description |
|---------|-------------|
| Master Volume | Main volume slider, affects final output level |
| Channel Enable | Per-channel mute/unmute (NES 5ch + expansion channels) |

> In Pure Digital mode, individual channel Volume TrackBars **have no effect** — the mixing ratios are determined by the hardware formula and are not overridden. This is by design: maintaining the simplicity of a standard emulator.

---

## 4. Mode 1: Authentic — Recreating Real Hardware

### Design Intent

A real NES doesn't send digital signals directly to your ears. The signal passes through a DAC converting it to analog voltage, through the console's internal circuit filtering, through RF cables or AV connectors, and finally out of the TV speakers. Each stage changes the character of the sound.

Authentic mode aims to **faithfully recreate this entire analog path**. Choose a different console model and you'll hear noticeably different tonal character — just like the NES at your friend's house sounded different from the one at home.

### Signal Processing Flow

1. **Physics-level DAC lookup**: Using the precise DAC formula documented on the [NESdev Wiki — APU Mixer](https://www.nesdev.org/wiki/APU_Mixer), a complete 3D lookup table (`authMix_tndTable[]`, indexed by `tri<<11 | noise<<7 | dmc`) is precomputed. This is more accurate than Pure Digital's linear approximation, outputting floating-point voltage values.
2. **Expansion audio integration**: Expansion channels are normalized by `expansionAudio / 98302.0` and added to the total voltage.
3. **256-Tap FIR oversampled decimation**: The full 1.79 MHz waveform is decimated to 44.1 kHz using 128 polyphase Blackman-windowed sinc FIR filter kernels, achieving > 58 dB sidelobe attenuation and eliminating aliasing. (See [Chapter 6: Oversampling Engine](#6-oversampling-engine-design-philosophy) for details.)
4. **Console model filtering (CMF)**: A low-pass filter matching the selected console model's analog characteristics is applied.
5. **Optional effects**: 60 Hz buzz (AC mains hum), RF crosstalk (video signal leaking into audio).
6. **Output**: Dual Mono.

### Six Console Tonal Profiles

Different hardware revisions of the NES sound distinctly different due to internal circuit design variations:

| Console Model | LPF Cutoff | Sound Character |
|---------------|------------|-----------------|
| Famicom (HVC-001) | ~14 kHz | Bright and clear, classic Famicom sound |
| Front-Loader (NES-001) | ~4.7 kHz | Warm and thick, many Western players' childhood memory |
| Top-Loader (NES-101) | ~20 kHz | Sharp and transparent, with faint 60 Hz AC hum |
| AV Famicom (HVC-101) | ~19 kHz | Clean direct output, later revision |
| Sharp Twin Famicom | ~12 kHz | Slightly darker than the original, Sharp co-branded |
| Sharp Famicom Titler | ~16 kHz | S-Video quality audio |
| **Custom** | 1,000–22,000 Hz | Freely configure cutoff, buzz, and RF parameters |

> Console filter characteristics are primarily based on [NESdev Wiki — APU](https://www.nesdev.org/wiki/APU) and community measurements. As precise cutoff data is unavailable for some models, values may vary from individual units.

### User-Adjustable Settings

| Setting | Description | Range |
|---------|-------------|-------|
| Console Model | Console model selection | 0–6 (including Custom) |
| RF Crosstalk | RF audio/video interference toggle | On/Off |
| Custom LPF Cutoff | Custom low-pass cutoff (Custom mode only) | 1,000–22,000 Hz |
| Custom Buzz | Custom AC hum toggle (Custom mode only) | On/Off |
| Buzz Amplitude | Hum amplitude (all models with buzz) | 0–100 |
| Buzz Freq | Hum frequency | 50 Hz (PAL) / 60 Hz (NTSC) |
| RF Volume | RF crosstalk volume | 0–200 |
| Channel Enable | Per-channel mute/unmute | On/Off per channel |

> As with Pure Digital, individual channel Volume TrackBars **have no effect** in Authentic mode. NES 5ch volume ratios are determined by the DAC lookup, and expansion channels use a unified overall gain — preserving analog simulation consistency.

---

## 5. Mode 2: Modern — The Mixing Console

### Design Intent

Modern mode breaks free from hardware emulation constraints. Its core concept is: **treat each channel as an independent studio track and let the user mix them like a sound engineer**.

The original NES outputs mono — once the five channels are mixed together, they can never be separated again. Modern mode isolates each channel before decimation, processing them independently. This is what enables per-channel volume control, stereo positioning, and channel-specific EQ.

### Signal Processing Flow

1. **Per-track normalization**: Each channel is multiplied by its own `mmix_chGain[]`, normalizing raw integer values to the 0–1.0 floating-point range. NES 5ch use `normScale` (Pulse: 1/15, DMC: 1/127); expansion channels use `Mode2ExpChNorm[]` (varying by chip output range).
2. **User volume overlay**: `chGain = normScale * (ChannelVolume / 70)`. 70% corresponds to the 1.0x calibrated baseline, 100% to 1.43x gain, 0% to silence.
3. **(5+N) x 256-Tap FIR oversampled decimation**: Each channel has its own independent oversampling engine — decimate first, then mix. (See [Chapter 6: Oversampling Engine](#6-oversampling-engine-design-philosophy) for details.)
4. **Triangle Bass Boost**: A Low-Shelf Biquad EQ (80–300 Hz, 0–12 dB) targeting only the Triangle channel, compensating for the inherent low-frequency thinness of its 4-bit stepped waveform. Other channels are unaffected. The EQ formula follows the standard Low-Shelf design from the [Audio EQ Cookbook](https://www.w3.org/2011/audio/audio-eq-cookbook.html) (Robert Bristow-Johnson).
5. **Stereo Pan**: Pulse 1 panned left (0.7), Pulse 2 panned right (0.7), others centered. StereoWidth adjusts separation.
6. **[Haas Effect](https://en.wikipedia.org/wiki/Precedence_effect)**: Right channel delayed 10–30 ms + crossfeed, leveraging the psychoacoustic precedence effect to widen the soundstage. Particularly effective with headphones.
7. **Micro-Room Reverb**: Four parallel [Comb Filters](https://en.wikipedia.org/wiki/Comb_filter) with coprime delay lengths (1116, 1188, 1277, 1356 samples, based on the [Schroeder reverb](https://ccrma.stanford.edu/~jos/pasp/Schroeder_Reverberators.html) architecture) + high-frequency damping, simulating a small room ambience. Maximum wetness is 30%, ensuring the effect enhances rather than interferes.
8. **Soft Limiter**: Asymptotic compression — signals below 80% of dynamic range pass through linearly; above that, they are smoothly compressed. Eliminates hard clipping artifacts.
9. **Output**: True Stereo.

### User-Adjustable Settings

| Setting | Description | Range | Default |
|---------|-------------|-------|---------|
| Stereo Width | Stereo separation | 0–100% (0=mono) | 50 |
| Haas Delay | Right channel delay | 10–30 ms | 20 |
| Haas Crossfeed | Delayed signal feedback ratio | 0–80% | 40 |
| Reverb Wet | Reverb wetness | 0–30% (0=Off) | 0 |
| Comb Feedback | Reverb length | 30–90% | 70 |
| Comb Damp | High-frequency damping | 10–70% | 30 |
| Bass Boost dB | Triangle low-frequency boost | 0–12 dB (0=Off) | 0 |
| Bass Boost Freq | Boost center frequency | 80–300 Hz | 150 |
| Channel Volume | Per-track independent volume | 0–100% (70%=calibrated) | 70 |
| Channel Enable | Per-track mute/unmute | On/Off | On |

---

## 6. Oversampling Engine Design Philosophy

### Why Is Oversampling Needed?

The NES APU runs at 1,789,773 Hz, but the human ear can only perceive frequencies between 20 Hz and 20 kHz. Converting the 1.79 MHz signal to 44,100 Hz audio output requires **downsampling (decimation)**.

The simplest approach is to grab one sample every ~41 CPU cycles — that's what Pure Digital does. But according to the [Nyquist–Shannon sampling theorem](https://en.wikipedia.org/wiki/Nyquist%E2%80%93Shannon_sampling_theorem), if frequencies above 22.05 kHz are not filtered out before decimation, they will "fold back" into the audible range, creating spurious frequencies that don't exist in the original signal. This is **aliasing** — square waves sound harsh, high frequencies become gritty, and the overall audio has a rough digital edge.

For many users, this roughness is "the emulator sound," so Pure Digital preserves it. But if you want cleaner audio quality, proper low-pass filtering before decimation is essential — that's exactly what the oversampling engine does.

### AprNes's Approach: Native-Rate Oversampling + Polyphase FIR Decimation

AprNes's Authentic and Modern modes take a direct approach: **record the complete waveform at every CPU cycle, then decimate with a precision FIR filter**.

Specifically:

1. **Every APU cycle (1/1,789,773 second)**, the current signal value is written into a 64K-entry ring buffer.
2. **After accumulating ~40.58 samples**, a 256-coefficient FIR filter is convolved with the buffer to produce one 44.1 kHz output sample.
3. The FIR coefficients use a [Blackman window](https://en.wikipedia.org/wiki/Window_function#Blackman_window)-weighted [sinc function](https://en.wikipedia.org/wiki/Sinc_function), with a cutoff at 20 kHz (the human hearing limit), achieving > 58 dB sidelobe attenuation.
4. To accurately handle the non-integer decimation ratio of 1,789,773 / 44,100 = **40.584...**, **128 polyphase kernels** are used, each corresponding to a different fractional phase delay, ensuring output samples land at precisely the correct time positions.

In Modern mode, this engine is **replicated 5+N times** (NES 5 channels + up to 8 expansion channels), each channel decimated independently before mixing. This is a "decimate first, mix later" architecture — in contrast to Authentic mode's "mix first, decimate later" approach.

### Why This "Brute Force" Approach?

This is a computationally heavier method — every cycle writes to the buffer and every decimation step performs a full 256-tap convolution, regardless of whether channels are producing sound. But on 2020s hardware, we believe it's worthwhile:

**Pursuing the Highest Anti-Aliasing Quality**

The 256-tap FIR achieves > 58 dB sidelobe attenuation, meaning residual aliasing energy is extremely low. Compared to shorter-impulse approaches (e.g., 16-tap at ~40 dB), this is nearly 100x lower. The audible result: cleaner high frequencies, smoother square wave edges, and no metallic fringing artifacts.

**Modern Hardware Can Easily Handle It**

At 44,100 output samples per second, each requiring 256 float multiply-accumulate operations: one frame (735 samples) needs only ~188,160 FLOPs. Even with Modern mode running 13 tracks simultaneously (5+8), that's only ~2.45 million — trivial for modern CPUs. With [SIMD vector instructions](https://en.wikipedia.org/wiki/Single_instruction,_multiple_data) (SSE/AVX), 256 taps of contiguous convolution can be done in just 32 vector operations.

**Architectural Flexibility**

The oversampling output is a standard floating-point PCM stream, freely chainable with any DSP module — EQ, reverb, pan, delay — without special adapters. This lets Mode 1's analog filter chain and Mode 2's effects processors plug in naturally.

### Comparison with blargg's blip_buf

In the NES emulator audio domain, blargg's (Shay Green) **[blip_buf](https://github.com/blargg-dev/blip-buf)** is the most widely known audio processing solution. It is used by [Mesen](https://github.com/SourMesen/Mesen2), Nestopia, [Mednafen](https://mednafen.github.io/), and other well-known emulators. Understanding the differences helps explain why AprNes took a different path.

#### blip_buf's Approach: Band-Limited Step Synthesis

blip_buf's core idea is elegant: **only record changes, not absolute values**.

When a square wave jumps from 0 to 15, blip_buf doesn't write an instantaneous vertical edge. Instead, it injects a precomputed smooth S-curve — a [band-limited step](https://ccrma.stanford.edu/~jos/resample/Theory_Ideal_Bandlimited_Interpolation.html) (the integral of a sinc function). This curve precisely contains only frequency components below the Nyquist frequency, so it produces no aliasing.

When a channel is silent and unchanged, blip_buf performs zero computation. Only at the instant a voltage change occurs does it inject a short (~16 sample) impulse. At frame end, all accumulated deltas are integrated to produce the final waveform.

This is an **event-driven** architecture: zero overhead during silence, computation only on waveform transitions. It was designed for 2000s-era hardware, providing excellent efficiency/quality balance under resource constraints.

#### Side-by-Side Comparison

| Aspect | blip_buf (Band-Limited Step) | AprNes Oversampling (FIR Decimation) |
|--------|------------------------------|--------------------------------------|
| Core approach | Record only waveform changes (deltas) | Record every cycle's absolute value |
| Anti-aliasing quality | 16-tap impulse (sidelobe ~40 dB) | 256-tap Blackman sinc (sidelobe > 58 dB) |
| Silence efficiency | Very high (zero computation) | Fixed overhead (writes every cycle) |
| Post-processing flexibility | Lower (delta domain unsuitable for frequency-domain filtering) | High (standard PCM stream, freely chainable with EQ, reverb, pan) |
| Non-square-wave channels | Requires special handling (Triangle's slow ramps don't suit the delta model) | Uniform processing (all channels same pipeline) |
| Per-channel independence | Requires separate blip buffer per channel | Natively supported (one ring buffer + oversampler per channel) |
| SIMD friendliness | Lower (scattered delta injection, hard to vectorize) | High (256-tap contiguous convolution, directly accelerated by [Vector\<float\>](https://learn.microsoft.com/en-us/dotnet/api/system.numerics.vector-1)) |
| Expansion audio integration | Requires per-mapper blip interface adaptation | Feed directly into native-rate buffer, unified decimation |

#### Why AprNes Chose Oversampling

Three core reasons:

**1. Higher Filter Quality**

blip_buf's 16-sample impulse (half_width=8) achieves ~40 dB sidelobe attenuation. AprNes's 256-tap Blackman FIR exceeds 58 dB — residual aliasing energy is nearly 100x lower. The audible difference: cleaner high frequencies, smoother square wave edges. This is the most direct improvement to audio quality.

**2. Architectural Flexibility for Post-Processing**

AprNes's three-mode architecture requires extensive audio post-processing: console model filtering, stereo panning, Triangle EQ, Haas delay, Comb reverb. These operations all need to work on standard time-domain PCM signals. blip_buf's delta accumulation model holds change values rather than absolute values before integration, making it less suitable for direct application of these processors. The oversampler's output is standard floating-point PCM, freely chainable with any DSP module.

**3. SIMD Advantages on Modern Hardware**

On 2020s CPUs, a 256-tap FIR convolution can leverage AVX2/SSE instructions to process multiple floats simultaneously. Per-frame decimation cost is in the sub-millisecond range. blip_buf's delta injection involves scattered memory access patterns that are harder to exploit with SIMD and cache locality.

At the NES emulation scale, the "zero overhead during silence" advantage has been thoroughly diluted by modern CPUs' absolute computational power — even writing every cycle amounts to ~1.79 million float writes per second plus 44,100 256-tap convolutions, well within any modern processor's capability.

#### Summary

blip_buf is an ingenious algorithm designed for earlier hardware constraints, providing excellent efficiency/quality balance under limited resources, and it continues to be used by many excellent emulators. AprNes's oversampling engine is a design choice aimed at modern hardware — trading higher absolute computation for better filter quality, greater architectural flexibility, and more SIMD-friendly parallelism. Both approaches converge on the same fundamental goal: turning 1.789 MHz digital square waves into the clean music your ears hear.

---

## 7. Channel Volume and Enable Control — Cross-Mode Behavior

This is the part of the audio system that most needs to be understood clearly. Different controls behave differently across modes, and this is by design.

### NES Built-in 5 Channels (Pulse 1, Pulse 2, Triangle, Noise, DMC)

| Control | Pure (0) | Authentic (1) | Modern (2) |
|---------|----------|---------------|------------|
| **Enable (checkbox)** | ✅ Active | ✅ Active | ✅ Active |
| **Volume (trackbar)** | — No effect | — No effect | ✅ Per-track independent |

**Why can't volume be adjusted in Pure / Authentic?**

Pure and Authentic modes use hardware-formula lookup table mixing (non-linear DAC lookup tables), where the volume ratios between the five channels are determined by the NES hardware circuitry. Allowing users to arbitrarily adjust individual channel volumes would break this hardware characteristic — Pulse 1 outputting 7 plus Pulse 2 outputting 8 through the [non-linear DAC](https://www.nesdev.org/wiki/APU_Mixer#Lookup_Table) produces a fundamentally different result than scaling each by different factors before lookup.

Modern mode is entirely different: it normalizes each channel independently and mixes linearly. Volume adjustment is applied after normalization, so there's no non-linear interaction issue.

**Why does Enable work across all modes?**

Mute is a utility function — equivalent to physically disconnecting a channel. It doesn't change the mixing behavior of other channels; it simply forces the muted channel's output to zero. This is safe and meaningful in any mode (e.g., isolating a specific channel to listen to, or eliminating interference from a particular channel).

### Mapper Expansion Channels (VRC6, VRC7, N163, Sunsoft 5B, MMC5, FDS)

| Control | Pure (0) | Authentic (1) | Modern (2) |
|---------|----------|---------------|------------|
| **Enable (checkbox)** | ✅ Per-track | ✅ Per-track | ✅ Per-track |
| **Volume (trackbar)** | ⚠️ Overall gain | ⚠️ Overall gain | ✅ Per-track independent |

**What does "overall gain" mean in Pure / Authentic?**

In Mode 0 and Mode 1, all enabled expansion channels share a single unified gain value `ap_mode01ExpGain`. This value is computed by averaging the Volume settings of all enabled channels, then multiplying by the chip's base gain (`DefaultChipGain`).

This means: if you set VRC6's three channels to 100%, 50%, and 20%, in Mode 0/1 they'll be averaged to (100+50+20)/3 ≈ 57% as a unified gain — the ratio between the three channels won't change, only the overall level decreases.

In Mode 2, it's truly per-track independent: the 100% channel is noticeably louder than the 20% one.

**Why do expansion channels allow volume adjustment in Pure / Authentic (even if it's overall)?**

On real hardware, expansion audio levels depend on cartridge circuit design, varying significantly across games and cartridge revisions. Unlike the NES built-in 5ch which follows precise [DAC formulas](https://www.nesdev.org/wiki/APU_Mixer#Emulation), there's no canonical reference level, so providing overall volume adjustment is reasonable even in hardware-emulation-focused modes.

### UI Label Reference

The gray hint text in the settings interface corresponds to:

| Area | Hint Text | Meaning |
|------|-----------|---------|
| Below NES 5 Channels | Enable: All modes \| Volume: Modern only | Checkbox works in all three modes, trackbar only affects Modern |
| Above Expansion Channels | Enable: All modes \| Volume: Modern per-ch, others overall | Checkbox works in all three modes, trackbar is per-track in Modern, averaged overall gain in Pure/Authentic |

---

## 8. The 70% Calibrated Baseline

Modern mode's channel Volume TrackBar ranges from 0–100%, but **the default is 70%, not 100%**.

This is by design:

- **70% = 1.0x calibrated gain**: This is the baseline we established after actually calibrating the volume balance across all chips — representing "the calibrated comfortable volume."
- **100% = 1.43x gain**: Provides ~43% headroom for users who prefer certain channels to be more prominent.
- **0% = silence**.

Why not set the calibration point at 100%? Because then users could only turn things down, never up. The 70% baseline makes volume control **bidirectional** — you can reduce or boost, which better matches real-world usage.

Boosting above 1.0x gain may cause peaks to exceed 16-bit range. The Soft Limiter handles this: signals below 80% of dynamic range pass through linearly, while peaks above that are asymptotically compressed, preventing hard clipping.

### Technical Details: Gain Formulas

```
NES Channels (Modern):
  mmix_chGain[i] = ChannelEnabled[i]
    ? normScale[i] * (ChannelVolume[i] / 70.0)
    : 0

Expansion Channels (Modern):
  mmix_chGain[5+i] = ChannelEnabled[5+i]
    ? Mode2ExpChNorm[chipType] * (ChannelVolume[5+i] / 70.0)
    : 0

Expansion Channels (Pure / Authentic):
  avgVol = sum(enabled channels' ChannelVolume) / totalChannelCount / 70.0
  ap_mode01ExpGain = DefaultChipGain[chipType] * avgVol
```

### Per-Chip Normalization Parameters

**Mode 2 (Modern) — `Mode2ExpChNorm[]`**

| Chip | Normalization | Notes |
|------|---------------|-------|
| VRC6 | 1/15 | Pulse 0–15 → 1.0, Saw 0–31 overflow handled by Soft Limiter |
| VRC7 | 1/2000 | [OPLL](https://www.nesdev.org/wiki/VRC7_audio) mixed output ±12285, conservative normalization |
| N163 | 0.25/15 | Mapper already divides by activeCh, per-ch ~0–15, then x0.25 |
| Sunsoft 5B | 1/32 | [AY-3-8910](https://en.wikipedia.org/wiki/General_Instrument_AY-3-8910) log DAC, games typically use vol 10–13, volumeLut[10]=31 ≈ 1.0 |
| MMC5 | 1/15 | Same as NES Pulse ([MMC5 audio](https://www.nesdev.org/wiki/MMC5_audio)) |
| FDS | 1/63 | 6-bit wavetable 0–63 ([FDS audio](https://www.nesdev.org/wiki/FDS_audio)) |

**Mode 0/1 (Pure / Authentic) — `DefaultChipGain[]`**

| Chip | Gain | Target |
|------|------|--------|
| VRC6 | 740 | Max output ≈ 1/2 of APU range (~45000) |
| VRC7 | 3 | OPLL raw ±12285 x 3 → ~37000 |
| N163 | 500 | Mapper divides by (numCh+1), x500 → ~60000 |
| Sunsoft 5B | 120 | sum x 120 → ~64000 |
| MMC5 | 43 | — |
| FDS | 20 | — |

---

## 9. Mapper Expansion Audio Chip Reference

The Mapper Sound Chip dropdown in the Channel Volume settings maps to the following mapper numbers:

| Chip Name | Mapper(s) | Channels | Channel Names | NESdev Reference |
|-----------|-----------|----------|---------------|-----------------|
| VRC6 | 024 (VRC6a), 026 (VRC6b) | 3 | Pulse 1, Pulse 2, Saw | [VRC6 audio](https://www.nesdev.org/wiki/VRC6_audio) |
| VRC7 | 085 | 1 (mixed output) | FM | [VRC7 audio](https://www.nesdev.org/wiki/VRC7_audio) |
| Namco 163 | 019 | Up to 8 | Ch1–Ch8 | [Namco 163 audio](https://www.nesdev.org/wiki/Namco_163_audio) |
| Sunsoft 5B | 069 (FME-7/5B) | 3 | Ch A, Ch B, Ch C | [Sunsoft 5B audio](https://www.nesdev.org/wiki/Sunsoft_5B_audio) |
| MMC5 | 005 | 2 | Pulse 1, Pulse 2 | [MMC5 audio](https://www.nesdev.org/wiki/MMC5_audio) |
| FDS | Famicom Disk System | 1 | Wave | [FDS audio](https://www.nesdev.org/wiki/FDS_audio) |

Selecting a different chip automatically displays the corresponding number of channel controls. Each chip's volume and enable settings are **stored independently** — switching chips won't overwrite other chips' settings.

Settings are persisted to `AprNesAudioPlus.ini` with per-chip prefixes (e.g., `ChVol_VRC6_0=70`, `ChVol_N163_3=70`). When loading a game, the emulator automatically detects the game's expansion chip and applies the corresponding settings.

---

## 10. UI Settings Interface Structure

The AudioPlus Settings window is divided into four sections:

### 1. Authentic Settings Group
Console Model selection, RF Crosstalk toggle, Custom mode-specific parameters (LPF Cutoff, Buzz), universal fine-tuning (Buzz Amplitude/Freq, RF Volume). Affects Mode 1 only.

### 2. Modern Settings Group
Stereo Width, Haas Effect (Delay + Crossfeed), Reverb (Wet + Feedback + Damping), Bass Boost (dB + Freq). Affects Mode 2 only.

### 3. Channel Volume Group

NES 5 built-in channels on the left, Mapper expansion channels on the right. Each channel includes:

- **CheckBox** (enable/disable): Controls whether the channel produces audio output
- **Label** (channel name): Displays the channel identifier
- **TrackBar** (volume 0–100%): Controls the channel's volume level
- **Value Label** (numeric display): Shows the current percentage in real-time while dragging the TrackBar

Above the expansion channel area, gray hint text displays the selected chip's mapper numbers and which modes each control affects.

### 4. OK / Cancel Buttons
OK writes all settings back to NesCore and applies them immediately (updates gains, rebuilds audio pipeline), while also saving to the INI file. Cancel closes without saving.

### Localization

The interface supports English (en-us), Traditional Chinese (zh-tw), and Simplified Chinese (zh-cn). All UI text (titles, group names, labels, hint text, buttons) is loaded from `AprNesLang.ini`, allowing new languages to be added without code changes.

---

## 11. Settings File Persistence

All AudioPlus-related settings are stored in `configure/AprNesAudioPlus.ini`:

- **Authentic parameters**: ConsoleModel, RfCrosstalk, CustomLpfCutoff, CustomBuzz, BuzzAmplitude, BuzzFreq, RfVolume
- **Modern parameters**: StereoWidth, HaasDelay, HaasCrossfeed, ReverbWet, CombFeedback, CombDamp, BassBoostDb, BassBoostFreq
- **NES channels**: ChVol_Pulse1/Pulse2/Triangle/Noise/DMC, ChEn_ (same)
- **Expansion channels**: Prefixed by chip name, ChVol_VRC6_0~7, ChEn_VRC6_0~7, ... ChVol_FDS_0~7, ChEn_FDS_0~7

If the INI file does not exist, the system operates normally using built-in defaults (all volumes default to 70, all channels enabled), and automatically creates a complete INI file when the user first saves settings.

AudioMode (Pure/Authentic/Modern selection) is stored in the main settings file `AprNes.ini`.

---

## 12. Technical Highlights

- **256-Tap [Blackman](https://en.wikipedia.org/wiki/Window_function#Blackman_window)-windowed [sinc](https://en.wikipedia.org/wiki/Sinc_function) FIR decimation**: > 58 dB sidelobe attenuation, 128 polyphase kernels for non-integer decimation ratio (1,789,773 / 44,100 ≈ 40.584), eliminating aliasing
- **Per-track independent oversampling (Modern)**: Each channel decimated separately before mixing, enabling true per-channel processing
- **Physics-level 3D DAC lookup (Authentic)**: Complete Triangle x Noise x DMC 3D lookup table, reproducing the [non-linear resistor ladder network](https://www.nesdev.org/wiki/APU_Mixer) interactions
- **Six real console filter profiles**: IIR low-pass filters based on community measurements, plus optional 60 Hz buzz and RF crosstalk
- **Soft Limiter**: Asymptotic compression replaces hard clipping — linear below ±80%, smoothly compressed above, capped at ±32767
- **70% calibrated baseline**: Bidirectional volume control (reduce to silence or boost to 1.43x), paired with Soft Limiter to prevent clipping
- **Per-chip INI persistence**: Switching between different mapper chips preserves independent settings

---

## 13. References

| Resource | Link | Usage |
|----------|------|-------|
| NESdev Wiki — APU Mixer | https://www.nesdev.org/wiki/APU_Mixer | DAC mixing formulas, non-linear lookup tables |
| NESdev Wiki — APU | https://www.nesdev.org/wiki/APU | APU architecture, channel specifications |
| NESdev Wiki — VRC6 audio | https://www.nesdev.org/wiki/VRC6_audio | VRC6 expansion audio spec |
| NESdev Wiki — VRC7 audio | https://www.nesdev.org/wiki/VRC7_audio | VRC7 (OPLL) FM synthesis spec |
| NESdev Wiki — Namco 163 audio | https://www.nesdev.org/wiki/Namco_163_audio | N163 wavetable synthesis spec |
| NESdev Wiki — Sunsoft 5B audio | https://www.nesdev.org/wiki/Sunsoft_5B_audio | 5B (AY-3-8910) PSG spec |
| NESdev Wiki — MMC5 audio | https://www.nesdev.org/wiki/MMC5_audio | MMC5 expansion pulse spec |
| NESdev Wiki — FDS audio | https://www.nesdev.org/wiki/FDS_audio | FDS wavetable synthesis spec |
| Audio EQ Cookbook | https://www.w3.org/2011/audio/audio-eq-cookbook.html | Biquad EQ formulas (Bass Boost) |
| Schroeder Reverberators — CCRMA | https://ccrma.stanford.edu/~jos/pasp/Schroeder_Reverberators.html | Comb filter reverb architecture |
| blargg's blip_buf | https://github.com/blargg-dev/blip-buf | Band-limited step synthesis reference |
| Nyquist–Shannon sampling theorem | https://en.wikipedia.org/wiki/Nyquist%E2%80%93Shannon_sampling_theorem | Sampling theorem (anti-aliasing theory) |

---

## 14. Feedback Welcome

This audio system has gone through multiple iterations of design and calibration, but we recognize that in the fields of signal processing, psychoacoustics, and hardware emulation, there may still be blind spots in our design, misunderstandings in our approach, or areas worth improving.

If you find:
- A mode doesn't sound as expected
- Volume balance for a particular mapper chip feels off
- There's room for improvement in the technical implementation
- A reference citation is inaccurate or a better source exists
- Or simply feel "this could be done better"

We warmly welcome your feedback and suggestions.
