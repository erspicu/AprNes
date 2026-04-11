# TriCNES Emulator Core / Mapper Difference Analysis

## Scope

This document only analyzes the emulator core and mappers:

- `old-TriCNES-main/Emulator.cs`
- `new-TriCNES-main/Emulator.cs`
- `old-TriCNES-main/mappers/*.cs`
- `new-TriCNES-main/mappers/*.cs`

It does not cover GUI, forms, `Program`, or other peripheral files.

## Quick Conclusion

The `new` version is not a minor cleanup. It is a structural shift from a high-level behavior-oriented emulator core toward a more hardware-oriented model built around:

- PPU bus behavior
- latches
- phase/timing relationships
- mapper-owned memory behavior
- device timing for FDS

In one sentence:

`old` is closer to a high-level behavioral approximation, while `new` is clearly moving toward a bus/latch/phase-driven hardware model.

## File-Level Overview

### `Emulator.cs`

This is the main source of change.

- Approximate diff size: `649 insertions / 680 deletions`

### `mappers/`

Only the following mapper files have meaningful changes:

- `Mapper_FDS.cs`
- `Mapper_MMC3.cs`
- `Mapper_MMC2.cs`
- `Mapper_AOROM.cs`
- `Mapper_CNROM.cs`
- `Mapper_UxROM.cs`

Unchanged mapper files:

- `Mapper_FME7.cs`
- `Mapper_MMC1.cs`
- `Mapper_NROM.cs`
- `Mapper_NULL.cs`

This indicates the change is not a wholesale rewrite of all mappers. It is focused on the areas most tightly coupled to the new PPU/FDS/PRGRAM design.

## High-Level Design Differences

### 1. Base `Mapper` responsibilities were redefined

In `old`, the base `Mapper` implicitly handled:

- PRG RAM reads in `$6000-$7FFF`
- PRG RAM writes in `$6000-$7FFF`

In `new`:

- default PRGRAM handling is removed from the base mapper
- `StorePRG()` in the base mapper becomes effectively empty
- `FetchPPU()` is added as a virtual mapper hook
- `FDS_ByteTransferFlag()` is added as a virtual mapper hook

#### Meaning

Responsibility shifts from the core back to the mapper:

- whether PRG RAM exists
- which ranges are readable/writable
- whether protection applies
- whether a mapper-specific memory path is needed

This is architecturally cleaner and more correct.

### 2. `DiskDrive` changed from a data holder into a timed device

In `old`, `DiskDrive` is mostly:

- `Disk`
- `ShiftRegister`
- `IRQ`
- `InsertDisk()`

In `new`, it gains:

- `Cart`
- `clock`
- `Status_ByteTransferFlag`
- `Clock()`

It now advances device state and raises a byte-transfer event every `1792` master clocks.

#### Meaning

FDS is no longer just “loadable disk-related data”. It starts behaving like an actual timed device inside the emulator event flow.

### 3. CPU/PPU/APU phase model changed

`old` uses a countdown-style timing model.

`new` uses an incrementing phase model, which makes it easier to express:

- when CPU work happens
- when M2-related mapper logic happens
- when PPU half-steps happen
- when APU steps happen

This is especially important for:

- `$2007`
- MMC3 IRQ timing
- FDS transfer timing

## PPU Design Differences

### 4. PPU internal register naming now matches standard `v/t` semantics

`old` uses:

- `PPU_ReadWriteAddress`
- `PPU_TempVRAMAddress`
- `PPU_VRAMAddressBuffer`

`new` uses:

- `PPU_v`
- `PPU_t`
- `PPU_ReadBuffer`

This is not just a rename. It reflects a clearer adoption of the conventional NES PPU model.

### 5. `$2007` was fundamentally redesigned

`old` models `$2007` through a high-level speculative state machine with many special flags such as:

- delayed reads
- early address increment
- mystery write
- interrupted read-to-write behavior

`new` replaces this with a more hardware-like signal model:

- `PPU_2007_Read`
- `PPU_2007_Write`
- SR latches
- read/write latch chains
- ALE behavior
- half-step timing
- explicit read/write bus phases

#### Meaning

`old` tries to reproduce observed behavior.

`new` tries to model the internal mechanism that produces the behavior.

That is the single biggest architectural change in the diff.

### 6. `FetchPPU()` now behaves like a real bus path

In `old`, PPU fetches usually look like:

- compute address
- call `FetchPPU(address)`

In `new`, `FetchPPU()` uses:

- the high part of `PPU_AddressBus`
- the low byte from `PPU_OctalLatch`

and routes the fetch through:

- CHR ROM / CHR RAM
- mirrored CIRAM / nametable memory
- mapper-specific memory paths where applicable

This is a shift from “memory helper” to “bus transaction model”.

### 7. New PPU address-path state was introduced

`new` adds:

- `PPU_OctalLatch`
- `PPU_PatternAddressRegister_CHR`
- `PPU_PatternAddressRegister_NT`
- `PPU_PatternAddressRegister_AT`
- `PPU_PAR_MUX`

These are strong signs that the new core is modeling:

- address-source selection
- address muxing
- staged address generation
- the fact that different PPU sub-operations share a common address path

### 8. `$2000`, `$2001`, `$2005`, `$2006`, `$2007` writes are modeled with finer timing

`new` often inserts master-clock advancement to represent:

- when PPUSEL goes high/low
- when the CPU data bus becomes stable
- when internal PPU state actually observes the final value

This is especially visible in:

- `$2000`
- `$2007`

Compared to `old`, which more often expressed the same effects as delayed state changes or exception flags.

### 9. Background and sprite fetches now share the same address-path logic

In `new`, background fetches and sprite fetches both rely on:

- PAR preparation
- muxing
- `PPU_AddressBus`
- `PPU_OctalLatch`
- mapper-level `FetchPPU()`

This is a major structural improvement because it gives the PPU one consistent language for:

- background fetch
- sprite fetch
- `$2007` access

That consistency matters a lot for MMC3 A12 behavior.

## Mapper Differences

### 10. `Mapper_FDS` is a major functional upgrade

Compared to `old`, `new` adds:

- `$4025` control handling
- byte-transfer IRQ generation
- `$4031` disk data input behavior
- IRQ acknowledge on `$4031` read
- FDS timing state in savestate

This is a meaningful step from “partial FDS support” toward actual FDS device behavior.

### 11. `Mapper_MMC3` benefits from the new bus model

MMC3’s IRQ logic is not fundamentally rewritten.

The improvement is that MMC3 now observes:

- a more realistic `PPU_AddressBus`
- a more meaningful A12 transition
- a better-integrated M2 filter timing relationship

Also, `Mapper_MMC3` now implements its own `FetchPPU()`, which allows it to control:

- CHR accesses
- nametable-related memory source selection
- alternative nametable arrangement behavior

This is architecturally important.

### 12. Smaller mapper changes still matter

`Mapper_MMC2`, `Mapper_AOROM`, `Mapper_CNROM`, and `Mapper_UxROM` mainly remove fallback calls to `base.StorePRG()`.

Even though the code changes are small, the design meaning is important:

- the core no longer quietly gives them generic PRGRAM semantics
- each mapper becomes explicitly responsible for its own write behavior

That reduces accidental permissiveness.

## Timing and Event-Model Summary

### 13. `old` timeline style

`old` tends to:

- trigger `$2007` behavior in the CPU handler
- set high-level flags
- let a small number of later PPU-cycle checkpoints perform follow-up work

That is practical, but the checkpoints are chosen to reproduce observed outcomes.

### 14. `new` timeline style

`new` separates the problem into:

- what the CPU sees now
- when latches are asserted
- when ALE is active
- when read buffer refill happens
- when `v` advances
- when writes actually land

This is much closer to a signal/timing diagram than to a pure behavioral rule set.

## Address Path / MMC3 Summary

### 15. Why `PPU_OctalLatch + PAR + AddressBus` matters

These additions make the PPU memory path more coherent:

- address sources are prepared separately
- a mux selects which source is currently active
- the bus is driven in phases
- low bits are latched separately

This gives MMC3 a better observation environment for A12 transitions and ties multiple PPU subsystems into one shared model.

## Incomplete / Transitional Areas

### 16. The new version is not fully finished

The code itself clearly marks several areas as temporary, inaccurate, or still under investigation.

The biggest remaining transitional areas are:

- `$2001` timing
- rendering on/off boundaries
- palette corruption details
- OAM corruption edge cases
- `CopyV`-related scroll behavior
- `PPU_EXT_Enable` being present but not really implemented

### 17. Most mature areas vs. least finished areas

More mature in `new`:

- `$2007`
- PPU address path
- mapper/core responsibility split
- FDS device timing
- MMC3 bus observation environment

Still transitional in `new`:

- `$2001`
- rendering enable/disable edge behavior
- corruption edge cases

## Final Assessment

The most accurate summary is:

- `old` is a functionality-oriented emulator core that uses targeted rules to reproduce difficult cases.
- `new` is an architectural upgrade that introduces a stronger low-level model and real device/bus structure.

So `new` is not simply “more complete”.
It is better described as:

- a substantially improved underlying architecture
- plus meaningful feature completion in some subsystems
- while still retaining unfinished edge-case work in the hardest PPU boundary areas
