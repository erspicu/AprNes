# Mapper 3: CNROM

- Source: `mappers-0.80.txt`
- Mapper number: `3`
- Purpose: implementation-oriented extraction for emulator development

## Overview

This mapper is used on many older U.S. and Japanese games, such as Solomon's Key, Gradius, and Hudson's Adventure Island.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.

## CPU Register Map Summary

- `$8000 - $FFFF`: +---------------+ | +------+ | | | | | | |
- `$0000`: +----------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- The ROM size is either 16K or 32K and is not switchable. It is loaded in the same manner as a NROM game; in other words, it's loaded at $8000 if it's a 32K ROM size, and at $C000 if it's a 16K ROM size. (This is because a 6502 CPU requires several vectors to be at $FFFA - $FFFF, and therefore ROM needs to be there at all times.)

## CHR / VROM / VRAM Behavior

- The first 8K VROM bank is swapped into PPU $0000 when the cart is reset.

## Mirroring Control

- Not explicitly documented in the source section.

## IRQ Behavior

- Not explicitly documented in the source section.

## WRAM / SaveRAM / Expansion I/O

- Not explicitly documented in the source section.

## Implementation Notes

- This is probably the simplest memory mapper and can easily be incorporated into a NES emulator.

## Uncertainties In Source

- Not explicitly documented in the source section.

## Full Register And Behavior Reference

```text
 +-----------------+

 +--------------------------------------------------------------------+
 | This mapper is used on many older U.S. and Japanese games, such as |
 | Solomon's Key, Gradius, and Hudson's Adventure Island.             |
 +--------------------------------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $8000 - $FFFF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 8K VROM bank at PPU $0000 |
                     +----------------------------------------------+
```

## Raw Source Notes

- The ROM size is either 16K or 32K and is not switchable. It is loaded in the same manner as a NROM game; in other words, it's loaded at $8000 if it's a 32K ROM size, and at $C000 if it's a 16K ROM size. (This is because a 6502 CPU requires several vectors to be at $FFFA - $FFFF, and therefore ROM needs to be there at all times.)
- The first 8K VROM bank is swapped into PPU $0000 when the cart is reset.
- This is probably the simplest memory mapper and can easily be incorporated into a NES emulator.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
