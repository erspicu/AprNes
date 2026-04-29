# 13 Mapper002 / UNROM

## What This Chapter Solves

UNROM is a great example for learning PRG bank switching. It has neither MMC1's serial register nor MMC3's IRQ; it just demonstrates how CPU program space can be paged.

This chapter covers Mapper002 / UNROM's PRG bank switching and CHR RAM, with a cross-reference to AprNes's `Mapper002.cs`.

## NES Hardware Concepts

**Everyday analogy**: UNROM is like **a desk that holds two books — an upper one and a lower one**. The lower one is never swapped (because the recipe's table of contents is on the last page); the upper one can be replaced with any other volume from the bookshelf.

The PRG ROM window the CPU sees directly is 32 KB:

```text
$8000-$FFFF
```

But a game's PRG ROM may exceed 32 KB. UNROM's approach:

```
$8000 ┌──────────────────────────┐
      │ switchable 16 KB PRG bank │  ← writes to $8000-$FFFF change this
      │ (up to 8 or 16 banks)     │     game's main logic lives here
$BFFF ├──────────────────────────┤
$C000 │ fixed 16 KB PRG bank      │  ← always the last bank
      │ (last bank)               │     vectors + shared subroutines live here
$FFFF └──────────────────────────┘     e.g. reset/NMI/IRQ handlers
```

Pinning the last bank is essential, because the interrupt vectors are at:

```text
$FFFA-$FFFB  NMI vector
$FFFC-$FFFD  Reset vector
$FFFE-$FFFF  IRQ/BRK vector
```

If the last bank could be swapped out, the CPU might fail to find a valid entry on reset or interrupt.

**Why is this layout so common?** Because "**shared code (NMI handler, input processing, common subroutines) in the fixed bank; per-level data / different screens in the swappable bank**" is the most natural architecture for NES games. Notable titles: *Mega Man* (each level uses one bank), *Castlevania*, *Contra*, *DuckTales*.

UNROM typically uses **CHR RAM** (not CHR ROM). Why? UNROM cartridge hardware doesn't provide CHR bank switching, yet games still want dynamic graphics (different sprites per level). Solution: **install 8 KB SRAM on the cartridge for the PPU**, and the CPU writes the active patterns through PPU `$2007`. Graphics data is loaded into PPU pattern table by the CPU at runtime.

## Beginner-Friendly Simplification

UNROM mapper state is just one PRG bank number:

```csharp
int bank;

void WritePrg(ushort addr, byte value)
{
    bank = value & 7;
}

byte ReadPrg(ushort addr)
{
    if (addr < 0xC000)
        return prg[(bank * 0x4000) + (addr - 0x8000)];
    return prg[lastBankOffset + (addr - 0xC000)];
}
```

CHR is direct read/write to 8 KB RAM.

## AprNes / NesCore Implementation Mapping

Important fields in `Mapper002.cs`:

- `PRG_ROM`.
- `ppu_ram`.
- `PRG_ROM_count`.
- `PRG_Bankselect`.
- `Rom_offset`.

Initialisation:

```text
Rom_offset = (PRG_ROM_count - 1) * 0x4000
```

This is the start offset of the last 16 KB PRG bank.

`MapperW_PRG()`:

```csharp
PRG_Bankselect = value & 7;
```

`MapperR_RPG()`:

- `< $C000`: read the switchable bank.
- `>= $C000`: read the fixed last bank.

CHR:

- `MapperR_CHR()` reads `ppu_ram[address]` directly.
- `MapperW_CHR()` writes `ppu_ram[addr]` directly.
- `UpdateCHRBanks()` points all 8 × 1 KB pointers at `ppu_ram`.

## Common Mistakes

- Making `$C000-$FFFF` switchable too, destabilising vectors.
- Forgetting that UNROM normally uses CHR RAM.
- Not masking or validating the PRG bank number against the actual ROM size.
- Treating mapper register writes as writes to PRG ROM.

## Chapter Recap

1. UNROM demonstrates the smallest PRG bank switching.
2. The fixed last bank at `$C000-$FFFF` exists for vectors and resident code.
3. CHR RAM lets the CPU update graphics patterns at runtime.

## Bridge to the Next Chapter

The next chapter covers Mapper003 / CNROM, switching topic to CHR bank switching — where CPU writes change the graphics the PPU sees.
