# AudioPlus — AprNes Audio Engine Design Philosophy

## The Problem We Set Out to Solve

The NES audio hardware was born in 1983. Five channels — two pulse waves, one triangle wave, one noise generator, and one DPCM sampler — run at the CPU clock rate of 1,789,773 Hz, roughly 40 times faster than CD-quality 44,100 Hz.

Every emulator faces the same fundamental challenge: **how do you correctly convert this ultra-high-speed digital signal into something your ears can hear?**

The simplest approach is to grab one sample every ~41 CPU cycles. This is like photographing a TV screen with your phone — you get moire patterns. In audio, this is called **aliasing**: phantom frequencies that don't exist in the original signal creep into the output. Square waves sound harsh, highs become gritty, and everything carries a thin layer of digital roughness.

AprNes's AudioPlus engine was designed to eliminate this problem entirely.

---

## Three Modes, Three Listening Philosophies

AudioPlus offers three audio modes, each tailored to a different listening preference. It's not about "better or worse" — it's about "what do you want to hear."

### Mode 0: Pure Digital — Raw Output

**"Give me the fastest, cleanest, most unprocessed sound."**

This is the most traditional emulator audio path: five channels mixed through lookup tables, passed through a simple DC-removal filter, and sent straight to your speakers. No oversampling, no extra filtering, no post-processing of any kind.

Why would anyone choose this mode? Because it has the lowest latency, the smallest CPU footprint, and for many people, this is exactly what they remember NES sounding like — a bit rough around the edges, but full of energy.

### Mode 1: Authentic — Faithful Reproduction

**"I want to hear what a 1985 Famicom sounded like coming through a TV speaker."**

A real NES doesn't send digital signals directly to your ears. The signal passes through a DAC (digital-to-analog converter), the console's internal analog circuitry, output cable attenuation, and finally the TV's speaker. Every link in this chain shapes the sound.

Authentic mode faithfully recreates this entire path:

**Physical DAC Simulation**

The NES DAC doesn't do simple linear addition. When Pulse 1 outputs 7 and Pulse 2 outputs 8, the mixed result isn't a simple linear mapping of 7+8=15 — the resistor-ladder network creates nonlinear interactions between channels. We use the precise DAC formulas documented by the NESdev community, precomputed into a 128KB 3D lookup table (the full combinatorial space of Triangle x Noise x DMC), queried at every CPU cycle to obtain the exact analog voltage.

**256-Tap Oversampled Decimation**

This is the single biggest factor in Authentic mode's audio quality improvement. Instead of crudely grabbing one sample every ~41 cycles like Pure Digital, we record the complete waveform at 1.79 MHz, then extract the 44.1 kHz audio using a 256-coefficient FIR filter (a Blackman-windowed sinc function). The filter's cutoff is set at 20 kHz — right at the edge of human hearing — preserving all audible frequencies while eliminating ultrasonic content that would cause aliasing.

To handle the non-integer decimation ratio of 1,789,773 / 44,100 = 40.584..., we employ 128 polyphase filter banks, each corresponding to a different fractional delay. This ensures output samples land at precisely the right points in time with no phase error.

**Console Model Filtering**

Different NES hardware revisions sound remarkably different. The original Famicom's (HVC-001) RF output is bright and clear; the American Front-Loader (NES-001) has a sealed enclosure that rolls off the highs noticeably, producing a warm, mellow tone; the later Top-Loader (NES-101) applies almost no filtering, yielding a sharp sound but with a 60 Hz AC hum — caused by poor PCB trace routing.

We've built in the filter characteristics of six real console models:

| Console | Cutoff Frequency | Sound Character |
|---------|-----------------|-----------------|
| Famicom (HVC-001) | 14 kHz | Bright and clear, the classic Famicom tone |
| Front-Loader (NES-001) | 4.7 kHz | Warm and mellow, many Western gamers' childhood memory |
| Top-Loader (NES-101) | 20 kHz | Sharp and transparent, with a subtle AC hum |
| AV Famicom (HVC-101) | 19 kHz | Clean direct output, the late-era revision |
| Sharp Twin Famicom | 12 kHz | Slightly darker than the original, Sharp co-branded |
| Sharp Famicom Titler | 16 kHz | S-Video grade audio quality |

There's also a seventh option: fully custom. You can freely set the low-pass cutoff (1,000–22,000 Hz), AC hum frequency and amplitude (50/60 Hz), and RF crosstalk level to dial in the sound of the exact console you remember.

**RF Crosstalk**

Early Famicoms output audio and video over RF cables. The video signal in the RF line bleeds slightly into the audio channel, producing a faint noise that shifts with the screen's brightness. Most people never consciously notice it, but when you turn it off, something feels missing. Authentic mode optionally includes this effect, simulated using a 59.94 Hz sawtooth wave modulated by screen luminance.

### Mode 2: Modern — Contemporary Stereo

**"NES music is great, but I want to reimagine it with modern audio engineering."**

Modern mode breaks free from hardware emulation constraints. Its core philosophy: **preserve each channel's independence and let them shine individually**.

**Five-Track Independent Oversampling**

Unlike Authentic mode's "mix first, then decimate" approach, Modern mode gives each of the five channels its own dedicated 256-tap FIR decimation engine. Decimate first, then mix — this means every channel is already a clean 44.1 kHz signal before it hits the mixing console, ready for independent control.

Why go to this trouble? Because once signals are mixed together, you can't separate them. It's like a recording studio where each instrument has its own microphone and track — the mixing engineer needs that separation to balance things properly.

**Stereo Separation**

The original NES outputs mono audio. Modern mode redistributes the five channels across the stereo field:

- Pulse 1 pans left, Pulse 2 pans right — the two square wave channels sit on opposite sides, creating a sense of width
- Triangle, Noise, and DMC anchor to the center — keeping low frequencies and rhythmic elements stable

The separation amount is adjustable (StereoWidth 0–100%). At 0% it's pure mono, at 100% it's maximum separation.

**Triangle Bass Boost**

The NES Triangle channel is a 4-bit stepped triangle wave — only 16 voltage levels. This inherently lacks low-frequency fullness, sounding thin in the bass range. We apply a Low-Shelf Biquad equalizer (Audio EQ Cookbook standard formula) specifically to the Triangle channel, boosting the 80–300 Hz range by 0–12 dB. Only the Triangle is affected; other channels remain untouched — this is the benefit of independent oversampling.

**Haas Effect — Spatial Width**

One of the key ways human ears determine sound direction is the arrival-time difference between left and right ears. When the same sound reaches your left ear first and your right ear 10–30 milliseconds later, your brain doesn't hear it as an echo — instead, it perceives the sound source as "wider" and "more spacious." This is the Haas Effect (precedence effect).

We delay the right channel by 10–30 ms (adjustable) and crossfeed the delayed signal back into the left channel at a configurable ratio. This spreads the NES audio — which would otherwise be locked dead-center in your head — into a wide soundstage. Especially effective with headphones.

**Micro-Room Reverb**

Four parallel comb filters with delay lengths of 1116, 1188, 1277, and 1356 samples — these numbers are coprime, avoiding metallic resonance. Each comb filter's feedback path includes a first-order low-pass filter that simulates how real room walls absorb high frequencies faster than lows.

This isn't a concert-hall reverb (that would turn NES's fast-paced music into mush), but a subtle sense of being played in a small room. The wet mix caps at 30%, ensuring the effect enhances rather than overwhelms.

---

## Why These Filters? Every One Has a Purpose

Every filter in AudioPlus exists for a specific reason:

| Filter | Used In | Why It's Needed |
|--------|---------|-----------------|
| 90 Hz High-Pass (HPF) | Mode 0/1 | NES DAC mixing produces DC offset. Without removal, the speaker cone gets pushed to one side, reducing dynamic range and causing distortion |
| 256-Tap FIR Low-Pass | Mode 1/2 | Anti-aliasing — prevents ultrasonic frequencies above 20 kHz from folding back into the audible range during decimation. This is a fundamental requirement of digital signal processing (Nyquist theorem) |
| Console Model IIR Low-Pass | Mode 1 | Real hardware's analog circuitry is itself a low-pass filter. Different consoles have different cutoff frequencies, determining the brightness or warmth of the tone |
| 60 Hz Buzz Sine Wave | Mode 1 | Simulates the AC hum present in certain consoles (e.g., Top-Loader) due to PCB routing deficiencies |
| RF Sawtooth Crosstalk | Mode 1 | Simulates video signal bleeding into audio through RF cables, adding that authentic "era" texture |
| Low-Shelf Biquad EQ | Mode 2 | Compensates for the Triangle channel's inherent low-frequency weakness due to its 4-bit stepped waveform, filling out the bass line |
| Comb Filter + LPF Damping | Mode 2 | Creates natural room reverb. The damping simulates the physical phenomenon where high-frequency energy decays faster than low-frequency energy |
| Haas Delay Line | Mode 2 | Leverages psychoacoustic properties of human hearing to widen the soundstage without producing audible echoes |

---

## How AprNes Differs from blargg's blip_buf

In the world of NES emulator audio, blargg's (Shay Green) **blip_buf** is the most widely recognized audio processing solution. It's used by well-known emulators including Mesen, Nestopia, and Mednafen. Understanding how AprNes's approach differs from blip_buf helps explain why we chose a different path.

### blip_buf's Approach: Band-Limited Step Synthesis

blip_buf's core idea is elegantly simple: **record only changes, not absolute values**.

When a square wave jumps from 0 to 15, blip_buf doesn't write an instantaneous vertical edge into the buffer. Instead, it injects a precomputed smooth S-curve (a band-limited step) — the integral of a sinc function — that contains only frequency components below the Nyquist limit, producing zero aliasing by construction.

When a channel is silent and unchanging, blip_buf does no computation at all. Only at the instant a voltage change occurs does it inject a short impulse (~16 samples long). At the end of each frame, a single integration pass over all accumulated deltas yields the final waveform.

This is an **event-driven** architecture: zero overhead during silence, computation only when waveform transitions occur.

### AprNes's Approach: Native-Rate Oversampling + FIR Decimation

AprNes takes a different path: **record every CPU cycle's complete waveform, then decimate with a heavy FIR filter**.

Every APU cycle (1.789 MHz), we write the current voltage value into a 64K-entry ring buffer. After accumulating roughly 40.58 samples, we convolve the buffer with a 256-coefficient Blackman-windowed sinc FIR to produce one 44.1 kHz output sample.

This is a **brute-force** architecture: regardless of whether a channel is sounding or silent, every cycle writes to the buffer, and every decimation performs the full 256-tap convolution.

### Why AprNes Chose Oversampling Over blip_buf

| Aspect | blip_buf | AprNes Oversampling |
|--------|----------|-------------------|
| Anti-aliasing quality | 16-tap impulse (~40 dB sidelobe attenuation) | 256-tap Blackman sinc (>58 dB sidelobe attenuation) |
| Efficiency during silence | Excellent (zero computation) | Fixed overhead (but negligible after SIMD) |
| Per-channel independence | Requires separate blip buffer per channel | Naturally supported (one ring buffer per channel) |
| Post-processing flexibility | Low (delta domain; hard to apply frequency-domain filters) | High (standard PCM stream; freely chainable with EQ, reverb, pan) |
| Non-square-wave channels | Requires special handling (Triangle's gradual slopes don't suit delta model well) | Uniform handling (all channels treated identically) |
| SIMD friendliness | Low (scattered delta injection; hard to vectorize) | High (contiguous 256-tap convolution; Vector\<float\> accelerated) |
| Expansion audio integration | Requires rewriting blip interface for each mapper's sound chip | Directly added to native-rate buffer; unified decimation |

AprNes chose oversampling for three core reasons:

**1. Superior Filter Quality**

blip_buf uses a 16-sample impulse (half_width=8) with roughly 40 dB of sidelobe attenuation. AprNes's 256-tap Blackman FIR exceeds 58 dB — meaning residual aliasing energy is nearly 100 times lower. The audible result is cleaner highs and smoother square wave edges.

**2. Post-Processing Flexibility**

AprNes's three-mode architecture demands extensive post-processing: console model filtering, stereo panning, Triangle EQ, Haas delay, and comb reverb. These operations all require standard PCM time-domain signals. blip_buf's delta-accumulation model works in the difference domain before integration, making it awkward to apply these processes directly. Oversampling outputs a standard floating-point PCM stream that can freely chain with any DSP module.

**3. SIMD Advantage on Modern Hardware**

On 2020s CPUs, a 256-tap FIR convolution can use AVX2 instructions to process 8 floats simultaneously — 256 taps in just 32 SIMD operations. The decimation cost for one frame (735 output samples) falls well under a millisecond. blip_buf's delta injection is a scattered memory-access pattern that doesn't lend itself well to SIMD or cache locality optimization.

At the scale of NES emulation, the "zero overhead during silence" advantage has been diluted by the sheer computational power of modern CPUs. Even writing every cycle amounts to 1.79 million float writes per second plus 44,100 256-tap convolutions per second — well within reach of any contemporary processor.

### In Summary

blip_buf is an ingeniously crafted algorithm designed for the hardware constraints of the 2000s, offering an excellent efficiency-to-quality ratio in resource-limited environments. AprNes's oversampling engine is purpose-built for modern hardware — trading higher absolute computation for better filter quality, greater architectural flexibility, and friendlier SIMD parallelism. Both converge on the same fundamental goal: turning 1.789 MHz digital square waves into the clean music your ears deserve.

---

## The Complete Picture

```
              APU (1,789,773 Hz, 5 channels)
                        |
           +------------+------------+
           |            |            |
      Mode 0        Mode 1       Mode 2
    Pure Digital   Authentic     Modern
           |            |            |
    LUT Mixing     3D DAC Mix    5-Track
    DC Killer      90Hz HPF     Normalize
           |            |            |
    1-in-~41cyc   256-Tap FIR   5x 256-Tap FIR
    (aliased)     (alias-free)  (alias-free)
           |            |            |
           |      Console LPF    Triangle
           |      + Buzz + RF    Bass Boost
           |            |            |
           |            |       Stereo Pan
           |            |            |
           |            |       Haas Effect
           |            |       + Reverb
           |            |            |
      Dual Mono    Dual Mono    True Stereo
           |            |            |
           +------------+------------+
                        |
               WaveOutPlayer
            44.1 kHz / 16-bit / Stereo
```
