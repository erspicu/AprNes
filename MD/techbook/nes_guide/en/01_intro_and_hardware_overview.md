# 01 Introduction and NES Hardware Overview

## What This Chapter Solves

When people first try to write an emulator, they tend to frame the problem as "load ROM, decode 6502 instructions, run them." That's only a small part of the picture. NES games don't run on a CPU alone — they run on a complete hardware system made of CPU, PPU, APU, controllers, and the cartridge mapper.

This chapter establishes the global map. Each subsequent chapter will pull one block apart and cross-reference its AprNes / `NesCore` implementation.

> **For readers unfamiliar with hardware terminology**: if terms like register, bus, interrupt, or memory-mapped I/O slow you down, start with [A1 Computer Organization Primer](A1_computer_organization_primer.md) — that one walks through every abstract concept using kitchen / chef / countertop analogies. You'll find this series much easier afterward.
>
> When you later need exact 6502 instruction rules, see [A2 6502 Complete 256-Opcode Implementation Reference](A2_6502_opcode_reference.md).

## NES Hardware Concepts

The NES splits roughly into a handful of major components:

```text
              +------------------+
              |    Cartridge     |
              | PRG / CHR / SRAM |
              |      Mapper      |
              +---------+--------+
                        |
+---------+      +------+-------+      +---------+
| JoyPad  | <--> |  CPU 2A03   | <--> |   APU   |
+---------+      +------+-------+      +---------+
                        |
                  memory-mapped I/O
                        |
                 +------+-------+
                 |     PPU      |
                 | background   |
                 | sprites      |
                 | palette      |
                 +------+-------+
                        |
                    video output
```

The CPU is the main program executor. Game logic, level flow, collision detection, writing PPU registers, reading the controllers — most of this happens on the CPU.

The PPU is the graphics chip. It doesn't wait for the CPU to draw a complete frame and then output it. Instead, it generates pixels in real time as it follows the scanline / dot rhythm, reading pattern tables, name tables, attribute tables, and sprite OAM as it goes.

The APU is the audio chip. It contains Pulse, Triangle, Noise, and DMC channels, plus its own frame counter. The DMC channel even kicks off DMA against CPU memory, which feeds back into CPU bus timing.

The cartridge is not just ROM. Many cartridges contain a mapper used to switch PRG/CHR banks, control mirroring, generate IRQs, or even add expansion audio.

## Beginner-Friendly Simplification

A first-pass emulator can imagine the hardware as four data flows:

```text
ROM loader -> CPU memory map -> CPU executes instructions
                         |
                         +-> PPU registers -> frame buffer
                         |
                         +-> APU registers -> audio samples
                         |
                         +-> Mapper -> PRG/CHR bank mapping
```

This model is enough to run a few early or simple test ROMs, but it isn't precise. Real NES behaviour depends on which clock phase each hardware event happens in.

## AprNes / NesCore Implementation Mapping

AprNes keeps the core in a `NesCore` partial class:

- `Main.cs` — initialisation, ROM loading, mapper creation, master-clock loop.
- `MEM.cs` — CPU bus dispatch, DMA, IRQ line.
- `CPU.cs` — 6502 registers, flags, addressing modes, opcode handlers.
- `PPU.cs` / `ppu_new.cs` — PPU registers, scroll, OAM, pixel pipeline.
- `APU.cs` — channel state, frame counter, `AudioMode 0` sample output.
- `IO.cs` / `JoyPad.cs` — memory-mapped I/O and controller serial read.
- `Mapper000.cs` through `Mapper004.cs` — progressive examples of how cartridge hardware extends the system.

AprNes doesn't aim merely to produce correct results — it tries to get the hardware timing right. Its main loop is therefore not "CPU runs one instruction, PPU catches up by 3× cycles," but rather an interleaving of CPU, PPU, APU, DMA, and mapper work driven by master-clock gates.

## Common Mistakes

- Treating CPU memory as a flat 64 KB byte array. Many addresses are hardware registers.
- Treating the PPU as a graphics API the CPU calls. The PPU advances per dot under its own clock.
- Treating the mapper as a simple ROM-offset function. Mappers are state machines on the cartridge.
- Treating audio as "mix samples and output." DMC DMA actually feeds back into CPU timing.

## Chapter Recap

1. An NES emulator simulates an entire hardware system, not just the CPU.
2. CPU, PPU, APU, DMA, and mapper interact mostly through clock, bus, and register signals.
3. AprNes's design centres on placing all those interactions back on a single master-clock timeline.

## Bridge to the Next Chapter

The next chapter fills in the foundational hardware concepts: bit fields, buses, memory-mapped I/O, mirroring, latches, open bus, clocks, IRQ/NMI, and DMA.
