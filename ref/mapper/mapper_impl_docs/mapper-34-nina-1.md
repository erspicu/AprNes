# Mapper 34: Nina-1

- Source: `mappers-0.80.txt`
- Mapper number: `34`
- Purpose: implementation-oriented extraction for emulator development

## Overview

These two mappers were used on two U.S. games: Deadly Towers and Impossible Mission II.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.

## CPU Register Map Summary

- `$7FFD`: +-------+ | +------+ | | | | | | |
- `$8000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$7FFE`: +-------+ | +------+ | | | | | | |
- `$0000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$7FFF`: +-------+ | +------+ | | | | | | |
- `$1000`: +----------------------------------------------+ +---------------+ +------------------------------------------+
- `$8000 - $FFFF`: +---------------+ | +------+ | | | | | | |
- `$8000`: +------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- The first 32K ROM bank is swapped into $8000 when the cart is started or reset.

## CHR / VROM / VRAM Behavior

- Carts without VROM (i.e. Deadly Towers) will have 8K of VRAM at PPU $0000. Carts with VROM (Impossible Mission 2) have the first 8K swapped in at reset. Apparently, this mapper is actually a combination of two actual separate mappers. Deadly Towers uses only the $8000-$FFFF switching, and Impossible Mission 2 uses only the three lower registers.

## Mirroring Control

- Not explicitly documented in the source section.

## IRQ Behavior

- Not explicitly documented in the source section.

## WRAM / SaveRAM / Expansion I/O

- Not explicitly documented in the source section.

## Implementation Notes

- This mapper is fairly easy to implement in a NES emulator.

## Uncertainties In Source

- Not explicitly documented in the source section.

## Full Register And Behavior Reference

```text
 +-------------------+

 +--------------------------------------------------------------------+
 | These two mappers were used on two U.S. games: Deadly Towers and   |
 | Impossible Mission ][.                                             |
 +--------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $7FFD +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 32K ROM bank at $8000     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $7FFE +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 4K VROM bank at PPU $0000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $7FFF +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 4K VROM bank at PPU $1000 |
             +----------------------------------------------+

 +---------------+         +------------------------------------------+
 | $8000 - $FFFF +---------| PPPPPPPP                                 |
 +---------------+         | +------+                                 |
                           |    |                                     |
                           |    |                                     |
                           |    +------- Select 32K ROM bank at $8000 |
                           +------------------------------------------+
```

## Raw Source Notes

- The first 32K ROM bank is swapped into $8000 when the cart is started or reset.
- Carts without VROM (i.e. Deadly Towers) will have 8K of VRAM at PPU $0000. Carts with VROM (Impossible Mission 2) have the first 8K swapped in at reset. Apparently, this mapper is actually a combination of two actual separate mappers. Deadly Towers uses only the $8000-$FFFF switching, and Impossible Mission 2 uses only the three lower registers.
- This mapper is fairly easy to implement in a NES emulator.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
