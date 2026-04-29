# 11 Mapper000 / NROM

## What This Chapter Solves

Mapper000 (NROM) is the simplest NES cartridge format. It has no bank switching, making it the right first target for learning the mapper interface.

This chapter covers how NROM connects PRG ROM and CHR ROM/RAM to the CPU/PPU buses, with a cross-reference to AprNes's `Mapper000.cs`.

## NES Hardware Concepts

**Everyday analogy**: NROM is the simplest cartridge — **a printed book with no gimmicks**. The chef just reads it, no page-flipping, no hidden compartments, no passwords.

NROM commonly comes in two flavours:

- **NROM-128**: 16 KB PRG ROM (corresponds to a 16 KB ROM chip, e.g. *Donkey Kong*, *Super Mario Bros.*)
- **NROM-256**: 32 KB PRG ROM (corresponds to a 32 KB ROM chip, e.g. *Excitebike*)

The CPU cartridge window is `$8000-$FFFF`, 32 KB total.

```
NROM-128 (16 KB):
   $8000 ┌─────────────────┐
         │  PRG ROM        │  ← contents of the 16 KB
   $BFFF ├─────────────────┤
   $C000 │  PRG ROM (mirror)│  ← same content placed again
   $FFFF └─────────────────┘     so reset/IRQ vectors at $FFFC-$FFFF resolve

NROM-256 (32 KB):
   $8000 ┌─────────────────┐
         │  PRG ROM lo     │
   $BFFF ├─────────────────┤
   $C000 │  PRG ROM hi     │
   $FFFF └─────────────────┘
```

**Why start with NROM?** Because NROM has no mapper logic (no register, no bank switching, no IRQ). Get the IMapper interface working with a runnable game first; tackle stateful mappers afterward.

NROM-256:

```text
$8000-$FFFF  32 KB PRG ROM
```

NROM-128:

```text
$8000-$BFFF  16 KB PRG ROM
$C000-$FFFF  mirror of same 16 KB PRG ROM
```

PPU pattern table:

```text
$0000-$1FFF  8 KB CHR ROM or CHR RAM
```

NROM mirroring is decided by the iNES header — the mapper itself has no register to change.

## Beginner-Friendly Simplification

The NROM mapper can be very simple:

```csharp
byte ReadPrg(ushort addr)
{
    return prgRom[addr - 0x8000];
}

byte ReadChr(ushort addr)
{
    return chrRomOrRam[addr];
}
```

If the ROM loader has already mirrored 16 KB PRG into 32 KB, the mapper needs no special-case logic.

## AprNes / NesCore Implementation Mapping

`Mapper000.cs` implements `IMapper`.

Initialisation:

- save `PRG_ROM`.
- save `CHR_ROM`.
- save `ppu_ram`.
- save `CHR_ROM_count`.

CPU PRG read:

```csharp
return PRG_ROM[address - 0x8000];
```

CHR read:

- if `CHR_ROM_count == 0`, read `ppu_ram[address]`.
- otherwise read `CHR_ROM[address]`.

CHR write:

- allowed only for CHR RAM.
- with CHR ROM, writes are ignored.

`UpdateCHRBanks()`:

- points `NesCore.chrBankPtrs[0..7]` at 8 consecutive 1 KB CHR banks.
- lets the PPU hot path quickly read CHR via bank pointers.

## Common Mistakes

- Forgetting to mirror 16 KB PRG, so reset-vector reads return garbage.
- When CHR ROM count is 0, not allocating CHR RAM.
- Discarding CHR RAM writes, so CHR-RAM-based games show no graphics.
- Believing NROM has no mapper class. Even without bank switching, the mapper interface is needed to connect CPU/PPU buses.

## Chapter Recap

1. Mapper000 is a fixed mapping with no mapper registers.
2. PRG 16 KB mirroring can happen in the ROM loader to simplify the mapper.
3. CHR ROM and CHR RAM must be handled separately.

## Bridge to the Next Chapter

The next chapter covers Mapper001 / MMC1. It introduces real mapper registers — controlled by 5-bit serial writes that drive PRG/CHR bank selection and mirroring.
