# 17 AprNes / NesCore Code Reading Map

## What This Chapter Solves

The `NesCore` codebase is sizeable, and many details exist for timing accuracy and hot-path performance. Beginners who jump straight into the middle of `CPU.cs` or `ppu_new.cs` will get lost.

This chapter provides a recommended reading order and a summary of each file's role.

## Recommended Reading Order

### 1. `Main.cs`

First, read the initialisation flow:

- `init(byte[] rom_bytes)`.
- ROM header parsing.
- PRG/CHR allocation.
- Mapper construction.
- PPU/APU/CPU initialisation.
- `HardResetState()`.
- region timing.

Then read the main loop:

- `Run_NTSC()`.
- `Run_PAL()`.
- `Run_Dendy()`.
- master-clock unrolled kernel.

Reading goal: understand how AprNes turns a ROM into a running NES.

### 2. `MEM.cs`

Focus:

- CPU bus dispatch table.
- the memory handlers `CpuRead()` / `CpuWrite()` call.
- DMA state.
- `DmaOneCycle()`.
- `DmaFetch()`.
- `UpdateIRQLine()`.

Reading goal: understand how a CPU address dispatches to RAM, PPU, APU, JoyPad, mapper.

### 3. `IO.cs`

Focus:

- `$2000-$3FFF` mirror to PPU registers.
- `$4000-$4017` dispatch to APU, OAM DMA, JoyPad.

Reading goal: build the mapping between CPU register reads/writes and hardware handlers.

### 4. `CPU.cs`

Read first:

- CPU registers and flags.
- how `CpuRead()` / `CpuWrite()` are used.
- addressing-mode helpers.
- `CompleteOperation()`.
- `PollInterrupts()`.
- `InitOpHandlers()`.

Don't try to read all 256 opcodes line-by-line up front. Pick representative instructions like `LDA`, `STA`, `ADC`, `BNE`, `BRK` to learn the patterns.

Reading goal: understand AprNes's per-cycle CPU state machine.

### 5. `PPU.cs`

Read registers and state first:

- `ppu_r_2002()`.
- `ppu_w_2000()`.
- `ppu_w_2001()`.
- `ppu_w_2005()`.
- `ppu_w_2006()`.
- `ppu_r_2007()` / `ppu_w_2007()`.
- palette.
- OAM.

Reading goal: understand how the CPU controls the PPU through `$2000-$2007`.

### 6. `ppu_new.cs` and `ppu_dispatch.cs`

Harder material. Start at the high level:

- `ppu_step_new()`.
- `ppu_half_step_new()`.
- deferred updates.
- sprite evaluation.
- sprite fetch.
- frame render.

`ppu_dispatch.cs` is for hot-path dispatch — you don't need to read every handler upfront.

Reading goal: understand that AprNes's PPU is a dot pipeline, not a frame renderer.

### 7. `APU.cs`

Read first:

- channel state.
- `initAPU()`.
- `apu_step()`.
- `ApuFrameCounterStep()`.
- `ApuOutputCatchup()`.
- `generateSample()`.

Stick to the `AudioMode = 0` main path. Skip `AudioPlus` advanced audio for now.

Reading goal: understand how the APU updates channels every cycle and emits 44100 Hz samples periodically.

### 8. `JoyPad.cs`

Focus:

- `P1_Port` / `P2_Port`.
- shift register.
- strobe.
- `gamepad_r_4016()` / `gamepad_w_4016()`.
- delayed shift inside APU step.

Reading goal: understand that the controller is a serial device.

### 9. Mappers

Recommended order:

1. `IMapper.cs`: read the mapper interface first.
2. `Mapper000.cs`: fixed mapping.
3. `Mapper002.cs`: PRG bank switching.
4. `Mapper003.cs`: CHR bank switching.
5. `Mapper001.cs`: MMC1 serial register.
6. `Mapper004.cs`: MMC3 banks and A12 IRQ.
7. `Mapper004RevA.cs` / `Mapper004MMC6.cs`: revision and MMC6 add-ons.

Reading goal: understand how cartridge hardware plugs into the CPU/PPU buses.

## AprNes Architecture in One Sentence

AprNes can be summarised as:

> AprNes treats the NES as a hardware system in which CPU, PPU, APU, DMA, and mapper share a clock and a bus, schedules these components via the master clock, and rebuilds chip / cartridge interactions through memory-mapped registers and the mapper interface.

## Lookup Table

```text
ROM loader        Main.cs
Master clock      Main.cs
CPU bus           MEM.cs, IO.cs
CPU core          CPU.cs
PPU registers     PPU.cs
PPU pipeline      ppu_new.cs, ppu_dispatch.cs
APU               APU.cs
Controller        JoyPad.cs
Mapper interface  IMapper.cs
Mapper 0-4        Mapper000.cs ... Mapper004.cs
```

## Common Mistakes

- Starting from optimised hot paths, missing the overall data flow.
- Treating partial-class files as unrelated; in fact they jointly form `NesCore`.
- Reading PPU before understanding `$2000-$2007`.
- Reading Mapper004 before Mapper000, 002, 003.
- Reading AudioPlus before `AudioMode = 0`.

## Chapter Recap

1. Read initialisation and bus first; CPU/PPU/APU details next.
2. Mappers in the order 0, 2, 3, 1, 4 follows conceptual difficulty.
3. AprNes's complexity is mostly about hardware timing and hot-path performance.

## Next Steps

To continue expanding the series, each chapter starting from chapter 1 can grow with diagrams, code snippets, suggested test ROMs, and implementation exercises.
