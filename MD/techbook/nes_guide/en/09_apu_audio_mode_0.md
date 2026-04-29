# 09 APU and AudioMode 0

## What This Chapter Solves

NES audio doesn't play WAV files — the APU generates waveforms in real time from register state. AprNes supports several audio modes; this series focuses on `AudioMode = 0`, the Pure Digital path.

This chapter introduces the APU's five channels, the frame counter, and how AprNes produces 44100 Hz audio samples.

## NES Hardware Concepts

**Everyday analogy**: think of the APU as a 5-piece band, each member with a distinct instrument:

- **Pulse 1 / Pulse 2**: two square-wave players. Adjustable pitch and "tone shape" (duty cycle: 12.5% / 25% / 50% / 75%). Mainly carry **lead melody and harmony**.
- **Triangle**: triangle-wave bassist. Softer than the squares, no volume control (either fully on or muted). Mainly carries the **bass line**.
- **Noise**: white-noise percussionist. Used for drums, explosions, hisses.
- **DMC** (Delta Modulation Channel): sample player. Plays prerecorded PCM samples (very low quality, ~5 kHz), commonly used for voice clips (the "ding-dong" doorbell in *Mike Tyson's Punch-Out!!*) or kick drums.

Each channel **runs independently**, with a 4-beat conductor (the frame counter) telling everyone "switch sections" at fixed intervals.

```
APU
├── Pulse 1   (square A)  ─→  ┐
├── Pulse 2   (square B)  ─→  │
├── Triangle  (triangle)  ─→  ├─→ Mixer (non-linear lookup) ─→ DAC ─→ Speaker
├── Noise     (noise)     ─→  │
└── DMC       (sample)    ─→  ┘
                              ▲
                       Mix is NOT linear addition!
                       pulse_out = 95.88 / (8128 / (p1 + p2) + 100)
                       tnd_out = 159.79 / (1 / (3*tri + 2*noi + dmc) / ... + 100)
```

NES APU main channels:

- Pulse 1.
- Pulse 2.
- Triangle.
- Noise.
- DMC.

### Pulse

A pulse channel has:

- duty sequence.
- timer period.
- envelope.
- sweep.
- length counter.

Pulse is commonly used for melody and effects.

### Triangle

Triangle uses a 32-step sequence. It has no envelope; it's gated by linear counter and length counter.

Triangle is commonly used for the bass line.

### Noise

Noise uses an LFSR to produce pseudo-random waveform. It has a mode bit and period table, commonly used for drum hits, explosions, and noise effects.

### DMC

DMC reads sample bytes from CPU memory and modifies a 7-bit output via delta modulation. DMC is important not only for audio: it triggers DMA that affects the CPU bus.

### Frame counter

The APU frame counter generates quarter-frame and half-frame events:

- quarter frame: updates envelope, triangle linear counter.
- half frame: updates length counter, sweep.

## Beginner-Friendly Simplification

A first version can:

1. Update APU channel timers each CPU cycle.
2. Accumulate time at a fixed sample rate.
3. When a sample is due, mix the current channel outputs into a sample.
4. Implement Pulse / Triangle / Noise first; then DMC.

Don't worry about high-quality resampling or analog filtering up front.

## AprNes / NesCore Implementation Mapping

`APU.cs` is the main module.

Important constants and state:

- `APU_SAMPLE_RATE = 44100`.
- `_sampleAccum`: sample-rate accumulator.
- `_cpuFreqInt`: CPU frequency by region.
- `AudioMode = 0` uses `ApuOutputCatchup()`.

Each APU cycle, `apu_step()`:

- processes controller shift.
- on GET cycle: update Pulse/Noise timer, DMC clock.
- on PUT cycle: handle frame-interrupt clear and DMC load-DMA countdown.
- updates DMC `$4015` deferred status.
- updates Triangle timer.
- calls `ApuFrameCounterStep()`.
- updates length-halt flags.
- calls `apuOutputFn()`.

`ApuRefreshOutputFn()`:

```csharp
apuOutputFn = AudioMode > 0 ? &ApuOutputPushPlus : &ApuOutputCatchup;
```

So `AudioMode = 0` runs through:

- `ApuOutputCatchup()`.
- accumulate `_sampleAccum += APU_SAMPLE_RATE`.
- if not yet at `_cpuFreqInt`, return.
- when a sample is due, call `generateSample(...)`.

`generateSample()`:

- pulses go through `SQUARELOOKUP`.
- triangle/noise/DMC go through `TNDLOOKUP`.
- adds mapper expansion audio.
- applies DC killer.
- applies `Volume`.
- clamps to `short`.
- invokes `AudioSampleReady?.Invoke((short)clamped, (short)clamped)`.

## Register Reference

AprNes's `IO.cs` dispatches `$4000-$4017` writes:

- `apu_4000` to `apu_4003`: Pulse 1.
- `apu_4004` to `apu_4007`: Pulse 2.
- `apu_4008` to `apu_400b`: Triangle.
- `apu_400c` to `apu_400f`: Noise.
- `apu_4010` to `apu_4013`: DMC.
- `apu_4015`: channel enable / status.
- `$4017`: frame counter mode.

## Common Mistakes

- Producing audio per frame, leading to latency and pitch inaccuracy.
- Updating Pulse sweep and envelope only when a sample is generated, not on APU timing.
- Ignoring the Triangle linear counter.
- Treating DMC as audio data only, ignoring DMA and IRQ.
- Mixing channels with linear addition instead of the NES non-linear lookup tables.

## Chapter Recap

1. The APU is five hardware channels plus a frame counter, all synchronised.
2. `AudioMode = 0` produces samples by accumulating a counter across CPU/APU cycles.
3. DMC affects CPU bus timing, so the audio module also feeds back into the rest of the emulator.

## Bridge to the Next Chapter

The next chapter covers DMA and controller I/O together, explaining how OAM DMA, DMC DMA, and JoyPad serial reads work through the CPU bus.
