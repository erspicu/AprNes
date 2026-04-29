# 16 Building an NES Emulator Step by Step

## What This Chapter Solves

The earlier chapters covered hardware and AprNes implementation. This chapter assembles a pragmatic development path so a beginner knows what to do first and what to defer, avoiding being crushed at day one by cycle-accurate PPU, DMA edge cases, and MMC3 IRQ.

## Mindset Before You Start

Writing an emulator is a **long-term project**, not a weekend toy. Set realistic expectations:

| Stage | Approx. time | What you can run |
|---|---|---|
| ROM loader + CPU stub | 1-3 days | nestest CPU test (no graphics) |
| First screen | 1 week | *Donkey Kong* boot screen (sprite/BG may be misaligned) |
| Full NROM games | 2-3 weeks | *Super Mario Bros.* playable |
| MMC1 + MMC3 | 1 month | most 1985-1990 mainstream games |
| Cycle accurate (passing blargg) | 2-3 months | all 184 blargg tests |
| AccuracyCoin perfect score | 3-6 months | 138/138 |

**Iron rules**:
1. **Pass CPU test ROMs first** (nestest); leave graphics aside even if ugly.
2. **Get games running before chasing accuracy** — don't aim for cycle-accurate from day one.
3. **Every stage needs ROM-test acceptance** — don't use "does the game look OK?" as your correctness criterion.
4. **Keep a reference emulator handy** — when stuck, open Mesen2 / fceux with a debugger and compare.

---

## Stage 1: ROM Loader and Mapper000

Goals:

- Load `.nes`.
- Parse iNES header.
- Build PRG ROM / CHR ROM or CHR RAM.
- Support only Mapper000.

Acceptance:

- Reset vector readable.
- CPU can fetch opcodes from `$8000-$FFFF`.
- PPU can read CHR pattern data.

**How to test**: print the reset vector:

```csharp
ushort resetVec = (ushort)(prg[0x7FFC - 0x8000] | (prg[0x7FFD - 0x8000] << 8));
Console.WriteLine($"Reset = ${resetVec:X4}");   // should land in $8000-$FFFF
```

Use NROM ROMs like *Donkey Kong* or *Super Mario Bros.*; reset vectors are usually around `$C000`.

## Stage 2: CPU Memory Map

Goals:

- `$0000-$1FFF` RAM mirror.
- `$2000-$3FFF` PPU register stub.
- `$4000-$401F` APU/IO stub.
- `$6000-$7FFF` SRAM.
- `$8000-$FFFF` mapper PRG.

Acceptance:

- CPU test ROM correctly reads/writes RAM.
- Mapper PRG access doesn't go out of bounds.

## Stage 3: 6502 CPU Core

Goals:

- Registers and flags.
- Addressing modes.
- Official opcodes.
- Stack.
- Branch.
- Interrupt.

Recommendation:

- Start instruction-level.
- Each instruction returns a cycle count.
- Pass nestest-class CPU tests first.

Convert to per-cycle later.

**Critical test ROMs**:
- **nestest.nes** (must pass) — load at `$C000` in automated mode; success leaves `$0002-$0003` showing error code (`$00 $00` = PASS).
- **blargg `instr_test-v5/all_instrs.nes`** — tests official + common illegal opcodes.
- **blargg `cpu_timing_test6/cpu_timing_test.nes`** — tests cycle counting and page-cross penalties.

Advanced (for cycle-accurate):
- **cpu_dummy_reads** / **cpu_dummy_writes_oam** / **cpu_dummy_writes_ppumem**
- **cpu_interrupts_v2** — NMI hijacking and other edge cases.

Detailed opcode rules: [A2 6502 Complete 256-Opcode Implementation Reference](A2_6502_opcode_reference.md).

## Stage 4: Minimal PPU Picture

Goals:

- PPU memory map.
- `$2000-$2007` basic behaviour.
- background rendering.
- palette.
- VBlank/NMI.

Recommendation:

- A scanline renderer is fine for the first version.
- Get the picture out first.
- Don't rush every PPU timing bug.

## Stage 5: Controller

Goals:

- `$4016` strobe.
- `$4016/$4017` serial read.
- correct button order.

Acceptance:

- Game title screen accepts Start.
- D-pad and A/B work correctly.

## Stage 6: Sprites and OAM

Goals:

- OAM.
- `$2003/$2004`.
- First-pass `$4014` OAM DMA.
- Sprite rendering.
- Sprite 0 hit.

Recommendation:

- Get sprites functional first.
- Add overflow bug and per-dot evaluation later.

## Stage 7: APU AudioMode 0

Goals:

- Pulse.
- Triangle.
- Noise.
- DMC.
- frame counter.
- sample accumulator.
- lookup-table mixing.

Recommendation:

- Output 44100 Hz mono first.
- Skip advanced analog filtering or stereo effects initially.

## Stage 8: Mappers 001-004

Recommended order:

1. Mapper002 / UNROM:
   - smallest PRG bank switching.
2. Mapper003 / CNROM:
   - smallest CHR bank switching.
3. Mapper001 / MMC1:
   - serial register.
   - PRG/CHR mode.
   - mirroring.
4. Mapper004 / MMC3:
   - PRG/CHR bank.
   - A12 IRQ.
   - revision differences.

This order is more stable than starting at MMC3 — each mapper only adds one or two new concepts.

## Stage 9: Tighten Timing Accuracy

Once the functional emulator runs many games, start filling in:

- CPU per-cycle.
- PPU dot-level rendering.
- `$2005/$2006/$2007` delays and buffers.
- DMA per-cycle.
- DMC DMA.
- open bus.
- sprite evaluation bugs.
- MMC3 A12 edges.

AprNes is essentially the form after this stage.

## AprNes / NesCore Implementation Mapping

Using AprNes as the final reference, files map to development stages:

- ROM loader: `Main.cs`.
- CPU bus: `MEM.cs`, `IO.cs`.
- CPU core: `CPU.cs`.
- PPU register: `PPU.cs`.
- PPU dot pipeline: `ppu_new.cs`, `ppu_dispatch.cs`.
- APU: `APU.cs`.
- Controller: `JoyPad.cs`.
- Mapper: `Mapper000.cs` through `Mapper004.cs`.

## Common Mistakes

- Chasing AprNes-level timing on day one and never running any game.
- Writing only the CPU with no minimal PPU output, leaving you with nothing to observe.
- Skipping mappers, so you can only run a handful of ROMs.
- Optimising the hot path before the functional version is stable.
- Lacking test ROMs and guessing at correctness from game visuals alone.

## Chapter Recap

1. Develop NES emulators in stages — functional first, then timing-accurate.
2. Mappers 002, 003, 001, 004 form a good easy-to-hard path.
3. AprNes is a high-accuracy finish line, not necessarily the first-version shape.

## Bridge to the Next Chapter

The next chapter provides the AprNes / `NesCore` code reading map, so when you return to the codebase you know how to navigate each file.
