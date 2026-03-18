# Mapper 2: UNROM

- Source: `mappers-0.80.txt`
- Mapper number: `2`
- Purpose: implementation-oriented extraction for emulator development

## Overview

This mapper is used on many older U.S. and Japanese games, such as Castlevania, MegaMan, Ghosts & Goblins, and Amagon.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.

## CPU Register Map Summary

- `$8000 - $FFFF`: +---------------+ | +------+ | | | | | | |
- `$8000`: +------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. This last 16K bank is permanently "hard-wired" to $C000, and it cannot be swapped.

## CHR / VROM / VRAM Behavior

- This mapper has no provisions for VROM; therefore, all carts using it have 8K of VRAM at PPU $0000.

## Mirroring Control

- Not explicitly documented in the source section.

## IRQ Behavior

- Not explicitly documented in the source section.

## WRAM / SaveRAM / Expansion I/O

- Not explicitly documented in the source section.

## Implementation Notes

- Most carts with this mapper are 128K. A few, mostly Japanese carts, such as Final Fantasy 2 and Dragon Quest 3, are 256K.
- Overall, this is one of the easiest mappers to implement in a NES emulator.

## Uncertainties In Source

- Not explicitly documented in the source section.

## Full Register And Behavior Reference

```text
 +-----------------+

 +--------------------------------------------------------------------+
 | This mapper is used on many older U.S. and Japanese games, such as |
 | Castlevania, MegaMan, Ghosts & Goblins, and Amagon.                |
 +--------------------------------------------------------------------+

 +---------------+         +------------------------------------------+
 | $8000 - $FFFF +---------| PPPPPPPP                                 |
 +---------------+         | +------+                                 |
                           |    |                                     |
                           |    |                                     |
                           |    +------- Select 16K ROM bank at $8000 |
                           +------------------------------------------+
```

## Raw Source Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. This last 16K bank is permanently "hard-wired" to $C000, and it cannot be swapped.
- This mapper has no provisions for VROM; therefore, all carts using it have 8K of VRAM at PPU $0000.
- Most carts with this mapper are 128K. A few, mostly Japanese carts, such as Final Fantasy 2 and Dragon Quest 3, are 256K.
- Overall, this is one of the easiest mappers to implement in a NES emulator.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
