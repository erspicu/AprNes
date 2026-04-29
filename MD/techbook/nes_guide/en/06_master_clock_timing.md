# 06 Master Clock and System-Wide Synchronisation

## What This Chapter Solves

If CPU, PPU, and APU each run their own loop, timing errors creep in fast. NES video, audio, interrupts, DMA, and mapper IRQs all depend on the *relative* timing of hardware events.

This chapter covers how AprNes uses the master clock to place the entire system on one timeline.

## NES Hardware Concepts

NTSC NES shares one high-frequency master clock. Each chip divides that base:

- CPU runs at master / 12.
- PPU runs at master / 4.
- PPU dot rate is roughly 3× CPU cycle rate.
- APU is synced with CPU cycles, but with separate GET / PUT phases.

**Everyday analogy**: imagine a high-frequency metronome (the master clock) ticking 21,477,272 times per second.
- The **CPU** moves once every 12 ticks (1,789,773/s = 1.79 MHz).
- The **PPU** moves once every 4 ticks (5,369,318/s = 5.37 MHz).
- The **APU** runs in step with the CPU but internally distinguishes GET (odd cycle) and PUT (even cycle) events.

```
master clock tick   1   2   3   4   5   6   7   8   9   10  11  12  13  14  15...
CPU                [─────────────── 1 cycle ──────────────][...
PPU                [─ 1 dot ─][─ 1 dot ─][─ 1 dot ─][─ 1 dot ─][─...
                       ▲          ▲          ▲          ▲
                    PPU advances                          
```

For every 3 PPU dots, the CPU advances 1 cycle. So the PPU **isn't a sub-routine of the CPU** — it's an independent processor running 3× faster.

A simple way to picture it:

```text
CPU:  C . . C . . C . .       (one move every 12 master ticks)
PPU:  P P P P P P P P P       (one move every 4 master ticks)
```

**Why is this 1:3 CPU:PPU ratio so convenient?** Because the NES screen is 256 pixels wide, each scanline is 341 dots, and the CPU runs exactly 113.667 cycles per scanline. This ratio lets **game programmers estimate "what dot is the PPU on?" by counting CPU instructions executed** — the foundation for tricks like scanline IRQs and split-scroll on the NES.

AprNes's model goes finer: at specific master-clock phases it executes:

- CPU step or DMA step.
- APU step.
- PPU full step.
- PPU half step.
- NMI line sampling.
- IRQ line sampling.
- Mapper clock rise.

## Beginner-Friendly Simplification

A common first version:

```text
cycles = ExecuteOneCpuInstruction()
RunPpu(cycles * 3)
RunApu(cycles)
```

Workable to start, but with issues:

- Mid-instruction PPU register writes have inaccurate timing.
- OAM DMA insertion isn't precise.
- DMC DMA can miss specific CPU read cycles.
- MMC3 scanline IRQ may shift.
- PPU split-timing tests tend to fail.

A more advanced version:

```text
for each CPU cycle:
    CPU step one cycle
    PPU step three dots
    APU step one cycle
```

AprNes is closer to per-master-clock gating.

## AprNes / NesCore Implementation Mapping

`Main.cs` carries region timing:

- `RegionType.NTSC`
- `RegionType.PAL`
- `RegionType.Dendy`

`ApplyRegionProfile()` sets:

- `preRenderLine`
- `nmiTriggerLine`
- `masterPerCpu`
- `masterPerPpu`
- `cpuFreq`
- `FrameSeconds`

Master-clock state:

- `mcCpuClock`
- `mcPpuClock`
- `mcApuPutCycle`

The NTSC fast path unrolls fixed phases to avoid running many `if`s per master tick. PAL and Dendy have different dividers and unrolled kernels.

Key logic inside the CPU gate:

```text
if CPU bus is a read and DMA is active:
    DmaOneCycle()
else:
    cpu_step_one_cycle()

MapperObj.CpuCycle()
```

Other phases call:

- `apu_step()`
- `ppu_step_new()`
- `ppu_half_step_new()`
- NMI / IRQ line update
- `MapperObj.CpuClockRise()`

## PPU Full Step and Half Step

AprNes splits PPU work into:

- `ppu_step_new()`: dot start and major phases.
- `ppu_half_step_new()`: background shifter, fetch commit, VBlank latch, sprite-0 pipeline, second stage of `$2007`.

This lets the model express half-dot and latch-style timing inside the PPU.

## Common Mistakes

- Synchronising CPU and PPU at frame granularity.
- Handling PPU register side effects only after the CPU instruction completes.
- Triggering the CPU NMI handler the moment VBlank starts.
- Implementing DMA as instantaneous.
- Applying NTSC timing to PAL / Dendy.

## Chapter Recap

1. The hard part of NES emulation is multi-hardware synchronisation, not raw CPU speed.
2. AprNes uses master-clock gates to schedule CPU, PPU, APU, DMA, and mapper.
3. Timing precision directly affects PPU register, DMA, DMC, MMC3 IRQ, and NMI behaviour.

## Bridge to the Next Chapter

The next chapter dives into PPU memory and registers, covering how the CPU controls the graphics chip via `$2000-$2007`.
