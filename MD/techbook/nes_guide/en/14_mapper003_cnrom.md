# 14 Mapper003 / CNROM

## What This Chapter Solves

CNROM is the minimal CHR bank switching example. Its PRG ROM is essentially fixed, but CPU writes to the mapper register change which CHR ROM bank the PPU sees through `$0000-$1FFF`.

This chapter covers Mapper003 / CNROM, with a cross-reference to AprNes's `Mapper003.cs`.

## NES Hardware Concepts

**Everyday analogy**: CNROM is the opposite of UNROM — **the recipe stays fixed, but the picture book can be flipped page by page**. The chef (CPU) keeps reading the same recipe; the wait staff (PPU) flips to a new picture book at every level change.

PRG and CHR are two different worlds:

- The CPU executes PRG ROM.
- The PPU reads CHR ROM/RAM as the pattern table.

CNROM is typically:

```
CPU sees:                  PPU sees:
$8000 ┌──────────┐        $0000 ┌────────────────┐
      │          │              │ switchable 8 KB │
      │  fixed   │              │ CHR ROM bank    │  ← writes to CPU's $8000-$FFFF
      │  PRG ROM │              │                 │     change this
      │          │        $1FFF └────────────────┘
$FFFF └──────────┘
```

```text
CPU $8000-$FFFF     fixed PRG ROM
PPU $0000-$1FFF     switchable 8 KB CHR ROM bank
```

The CPU cannot execute CHR directly, and CHR is not visible as CPU memory. **The CPU writes to any address in `$8000-$FFFF`** (note: not literally to that address — it's a signal to the mapper), and the mapper changes which ROM bank the PPU's CHR bus sees.

```assembly
; Switch to CHR bank 2 (third 8 KB bank)
LDA  #$02
STA  $8000        ; any $8000-$FFFF write is interpreted as "change CHR bank"
                   ; mapper takes value & 0x03 (CNROM has at most 4 banks)
```

**Why does CNROM exist?** Because some games (like *Solomon's Key*, *Gradius*) have small program logic (< 32 KB) but need lots of graphics (multiple bosses, effects). CNROM is more economical than UNROM here: PRG stays at 32 KB without paging, while CHR can be 32 KB or 64 KB with each level using a different 8 KB. **Notable titles**: *Solomon's Key*, *Gradius*, *Q*bert*, *Spelunker*.

## Beginner-Friendly Simplification

CNROM state:

```csharp
int chrBank;

void WritePrg(ushort addr, byte value)
{
    chrBank = value & 3;
}

byte ReadChr(ushort addr)
{
    return chrRom[(chrBank * 0x2000) + addr];
}
```

PRG read is fixed, like NROM.

## AprNes / NesCore Implementation Mapping

Important fields in `Mapper003.cs`:

- `PRG_ROM`.
- `CHR_ROM`.
- `ppu_ram`.
- `CHR_ROM_count`.
- `CHR_Bankselect`.

`MapperW_PRG()`:

```csharp
CHR_Bankselect = value & 3;
UpdateCHRBanks();
```

`MapperR_RPG()`:

```csharp
return PRG_ROM[address - 0x8000];
```

`MapperR_CHR()`:

- if `CHR_ROM_count == 0`, read `ppu_ram[address]`.
- otherwise read `CHR_ROM[address + (CHR_Bankselect << 13)]`.

`UpdateCHRBanks()`:

- points at `CHR_ROM + (CHR_Bankselect << 13)`.
- fills `NesCore.chrBankPtrs[0..7]` for each 1 KB.

## Comparison with UNROM

```text
UNROM:
  CPU $8000-$BFFF  switch PRG
  PPU $0000-$1FFF  CHR RAM fixed

CNROM:
  CPU $8000-$FFFF  PRG fixed
  PPU $0000-$1FFF  switch CHR
```

This pairing is a nice way for beginners to see that mappers can affect either the CPU bus or the PPU bus.

## Common Mistakes

- Mapping CHR ROM directly into the CPU memory map.
- After a CPU mapper write, failing to update the CHR bank pointers used by the PPU.
- Updating `MapperR_CHR()` only, while the PPU hot path uses `chrBankPtrs`.
- Ignoring the CHR-RAM fallback when CHR ROM count is 0.

## Chapter Recap

1. CNROM is the smallest CHR bank switching example.
2. A CPU write to the mapper register changes the pattern table the PPU sees.
3. AprNes uses `UpdateCHRBanks()` so the PPU hot path can read the current CHR bank quickly.

## Bridge to the Next Chapter

The next chapter covers Mapper004 / MMC3. It supports both PRG and CHR bank switching, and generates scanline IRQ via PPU A12 edges.
