# Mapper 7: AOROM

- Source: `mappers-0.80.txt`
- Mapper number: `7`
- Purpose: implementation-oriented extraction for emulator development

## Overview

Numerous games released by Rare Ltd. use this mapper, such as Battletoads, Wizards & Warriors, and Solar Jetman.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.

## CPU Register Map Summary

- `$8000 - $FFFF`: +---------------+ | || | | | |+--| | | | | |
- `$8000`: | | | | +------- One-Screen Mirroring |
- `$2400`: +----------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- The first 32K ROM bank is swapped into $8000 when the cart is started or reset.

## CHR / VROM / VRAM Behavior

- This mapper has no provisions for VROM; therefore, all carts using it have 8K of VRAM at PPU $0000.

## Mirroring Control

- Many carts using this mapper need precise NES timing to work properly. If you're writing an emulator, be sure that you have provisions for switching screens during refresh, and be sure the one-screen mirroring is emulated properly. Also make sure that you have provisions for palette changes in midframe and for special handling of mid-HBlank writes to $2006.

## IRQ Behavior

- Not explicitly documented in the source section.

## WRAM / SaveRAM / Expansion I/O

- Not explicitly documented in the source section.

## Implementation Notes

- Not explicitly documented in the source section.

## Uncertainties In Source

- Not explicitly documented in the source section.

## Full Register And Behavior Reference

```text
 +-----------------+

 +--------------------------------------------------------------------+
 | Numerous games released by Rare Ltd. use this mapper, such as      |
 | Battletoads, Wizards & Warriors, and Solar Jetman.                 |
 +--------------------------------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $8000 - $FFFF +---| xxxSPPPP                                     |
 +---------------+   |    ||  |                                     |
                     |    |+--|                                     |
                     |    |   |                                     |
                     |    |   +---- Select 32K ROM bank at $8000    |
                     |    |                                         |
                     |    +------- One-Screen Mirroring             |
                     |              0 = Mirror pages from PPU $2000 |
                     |              1 = Mirror pages from PPU $2400 |
                     +----------------------------------------------+
```

## Raw Source Notes

- The first 32K ROM bank is swapped into $8000 when the cart is started or reset.
- This mapper has no provisions for VROM; therefore, all carts using it have 8K of VRAM at PPU $0000.
- Many carts using this mapper need precise NES timing to work properly. If you're writing an emulator, be sure that you have provisions for switching screens during refresh, and be sure the one-screen mirroring is emulated properly. Also make sure that you have provisions for palette changes in midframe and for special handling of mid-HBlank writes to $2006.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
