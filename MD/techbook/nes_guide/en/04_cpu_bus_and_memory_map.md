# 04 CPU Bus and Memory Map

## What This Chapter Solves

The CPU has a 16-bit address bus, so it can emit any address in `$0000-$FFFF` (64 KB). But these 64 KB are not one contiguous block of RAM — they're shared between several hardware blocks.

This chapter covers the NES CPU memory map and how AprNes uses a dispatch table to route reads and writes to the right hardware.

## NES Hardware Concepts

CPU address space:

```text
$0000-$07FF  2 KB internal RAM         ┐
$0800-$0FFF  RAM mirror                 ├ same 2 KB repeats 4×
$1000-$17FF  RAM mirror                 │
$1800-$1FFF  RAM mirror                 ┘
$2000-$2007  PPU registers (8 of them)  ┐
$2008-$3FFF  PPU register mirror        ┘ those 8 registers repeat 1024×
$4000-$4017  APU / controller / DMA registers
$4018-$401F  CPU test mode (NES doesn't use this)
$4020-$5FFF  cartridge expansion area (most mappers leave this empty)
$6000-$7FFF  cartridge PRG RAM / SRAM (battery-backed = save data)
$8000-$FFFF  cartridge PRG ROM / mapper banks
```

This table is the centre of writing an NES emulator.

**Everyday analogy**: imagine the 64 KB as a 65,536-room building floor plan:
- **Rooms 0–8191**: the system's small storage closet (2 KB RAM with four duplicate door-numbers). Why duplicates? To save chips! 1980s address decoders only wired up 11 lines and ignored the rest, so "`$0042` and `$0842` lead to the same room" was just a side effect.
- **Rooms 8192–16383**: 8 PPU control consoles, but each door-number was reprinted 1023 times.
- **Rooms 16384–16407**: APU, controllers, DMA triggers.
- **Rooms 16415–24575**: the cartridge's expansion area; for most cartridges these are "empty rooms" (reads return open bus).
- **Rooms 24576–32767**: cartridge PRG RAM (where battery-backed cartridges store saves).
- **Rooms 32768–65535**: cartridge PRG ROM. **The chef spends 90% of the time reading recipes here**, so read speed in this region directly governs game performance.

**Why can't an emulator just use a 64 KB byte array?** Because **writing the same address can have completely different behaviour**:
- Write `$0042` → really stored in RAM; readable next time.
- Write `$2000` → triggers a PPU control setting; nothing stored.
- Write `$4014` → triggers OAM DMA, stalls CPU 513 cycles.
- Write `$8000` → on NROM, no effect; on MMC1, "shifts one bit into the mapper register".

The emulator's `Mem_w(addr, val)` function must inspect which segment `addr` falls into and dispatch to the correct handler — this is **bus dispatch**.

A CPU read at some address could be:

- read from RAM.
- read PPU status.
- read APU status.
- read a controller bit.
- read mapper-provided PRG ROM.
- read open bus.

A CPU write at some address could be:

- write to RAM.
- modify PPU scroll.
- start OAM DMA.
- change an APU channel parameter.
- change a mapper bank register.

## Beginner-Friendly Simplification

A first version can look like:

```csharp
byte CpuRead(ushort addr)
{
    if (addr < 0x2000) return ram[addr & 0x7FF];
    if (addr < 0x4000) return PpuReadRegister(0x2000 | (addr & 7));
    if (addr < 0x4020) return ApuIoRead(addr);
    if (addr < 0x6000) return openBus;
    if (addr < 0x8000) return sram[addr - 0x6000];
    return mapper.ReadPrg(addr);
}
```

This is enough to build the right mental model. Open bus, DMA, DMC bus conflicts, and register delays come later.

## AprNes / NesCore Implementation Mapping

AprNes uses 8 × 8 KB page handlers in `MEM.cs`:

```text
addr >> 13 = 0  $0000-$1FFF
addr >> 13 = 1  $2000-$3FFF
addr >> 13 = 2  $4000-$5FFF
addr >> 13 = 3  $6000-$7FFF
addr >> 13 = 4  $8000-$9FFF
addr >> 13 = 5  $A000-$BFFF
addr >> 13 = 6  $C000-$DFFF
addr >> 13 = 7  $E000-$FFFF
```

For reads:

- page 0: `Read_NesRam`.
- page 1: `IO_read`, handling PPU register mirror.
- page 2: `Read_Page2`, dispatching to APU/IO or mapper expansion.
- page 3: `MapperObj.MapperR_RAM`.
- page 4-7: `MapperObj.MapperR_RPG`.

For writes:

- page 0: `Write_NesRam`.
- page 1: `IO_write`.
- page 2: `Write_Page2`.
- page 3: `MapperObj.MapperW_RAM`.
- page 4-7: `MapperObj.MapperW_PRG`.

This design is smaller than a 65536-handler table and faster than long if-chains on the hot path.

## Bus Side Effects

AprNes's `CpuRead()` and `CpuWrite()` do more than fetch / store a byte:

- set `cpuBusAddr`.
- set `cpuIsRead`.
- update `cpubus`.
- on write cycles, handle DMC implicit abort.
- call the matching hardware handler.

This state is consumed by DMA, open bus, controller read, and other logic.

## Common Mistakes

- Treating `$2000-$3FFF` as PPU VRAM. It is the CPU-side entry point to PPU registers.
- Forgetting PPU register mirror, breaking behaviour beyond `$2008`.
- Discarding writes to `$8000-$FFFF`. The ROM itself isn't writable, but mapper registers may be.
- Reading CHR ROM through the CPU bus map. CHR lives in the PPU address space and isn't directly visible to the CPU.

## Chapter Recap

1. The CPU's 64 KB address space maps several hardware blocks.
2. Memory reads/writes can have side effects — don't treat them as raw byte-array access.
3. AprNes uses an 8-page dispatch table to keep the CPU bus hot path short.

## Bridge to the Next Chapter

The next chapter covers the CPU core: 6502 registers, flags, addressing modes, opcode dispatch, and AprNes's per-cycle instruction model.
