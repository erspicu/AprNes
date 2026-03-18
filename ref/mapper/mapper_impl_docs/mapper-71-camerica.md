# Mapper 71: Camerica

- Source: `mappers-0.80.txt`
- Mapper number: `71`
- Purpose: implementation-oriented extraction for emulator development

## Overview

This mapper is used on Camerica's unlicensed NES carts, including Firehawk and Linus Spacehead.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.

## CPU Register Map Summary

- `$8000 - $BFFF`: +---------------+ | +------+ | | | | | | | | +------- Unknown | +------------------------------------------+ +---------------+ +------------------------------------------+
- `$C000 - $FFFF`: +---------------+ | +------+ | | | | | | |
- `$8000`: +------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. This last 16K bank is permanently "hard-wired" to $C000, and it cannot be swapped, as far as is known.

## CHR / VROM / VRAM Behavior

- This mapper has no provisions for VROM; therefore, all carts using it have 8K of VRAM at PPU $0000.

## Mirroring Control

- Not explicitly documented in the source section.

## IRQ Behavior

- Not explicitly documented in the source section.

## WRAM / SaveRAM / Expansion I/O

- Not explicitly documented in the source section.

## Implementation Notes

- Many ROMs from these games are incorrectly defined as mapper #2. Marat has still not assigned an "official" .NES mapper number for this mapper.

## Uncertainties In Source

- Not explicitly documented in the source section.

## Full Register And Behavior Reference

```text
 +---------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on Camerica's unlicensed NES carts, including  |
 | Firehawk and Linus Spacehead.                                      |
 +--------------------------------------------------------------------+

 +---------------+         +------------------------------------------+
 | $8000 - $BFFF +---------| UUUUUUUU                                 |
 +---------------+         | +------+                                 |
                           |    |                                     |
                           |    |                                     |
                           |    +------- Unknown                      |
                           +------------------------------------------+

 +---------------+         +------------------------------------------+
 | $C000 - $FFFF +---------| PPPPPPPP                                 |
 +---------------+         | +------+                                 |
                           |    |                                     |
                           |    |                                     |
                           |    +------- Select 16K ROM bank at $8000 |
                           +------------------------------------------+
```

## Raw Source Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. This last 16K bank is permanently "hard-wired" to $C000, and it cannot be swapped, as far as is known.
- This mapper has no provisions for VROM; therefore, all carts using it have 8K of VRAM at PPU $0000.
- Many ROMs from these games are incorrectly defined as mapper #2. Marat has still not assigned an "official" .NES mapper number for this mapper.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
