# Mapper 33: Taito TC0190

- Source: `mappers-0.80.txt`
- Mapper number: `33`
- Purpose: implementation-oriented extraction for emulator development

## Overview

This mapper is used on several Japanese titles by Taito, such as Pon Poko Pon. As far as I know, it was not used on U.S. games.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.

## CPU Register Map Summary

- `$8000`: +-------+ | +------+ | | | | | | |
- `$8000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$8001`: +-------+ | +------+ | | | | | | |
- `$A000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$8002`: +-------+ | +------+ | | | | | | |
- `$0000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$8003`: +-------+ | +------+ | | | | | | |
- `$0800`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$A000`: +-------+ | +------+ | | | | | | |
- `$1000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$A001`: +-------+ | +------+ | | | | | | |
- `$1400`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$A002`: +-------+ | +------+ | | | | | | |
- `$1800`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$A003`: +-------+ | +------+ | | | | | | |
- `$1C00`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$C000`: +-------+ | +------+ | | | | | | | | +------- Unknown | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$C001`: +-------+ | +------+ | | | | | | | | +------- Unknown | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$E000`: +-------+ | +------+ | | | | | | | | +------- Reserved | +----------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 16K of ROM is permanently "hard-wired" and cannot be swapped.

## CHR / VROM / VRAM Behavior

- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.

## Mirroring Control

- Not explicitly documented in the source section.

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
 +-------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles by Taito, such as   |
 | Pon Poko Pon. As far as I know, it was not used on U.S. games.     |                                           |
 +--------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $8000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8001 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $A000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8002 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $0000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8003 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $0800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A002 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A003 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1C00 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C000 +---| UUUUUUUU                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Unknown                          |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C001 +---| UUUUUUUU                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Unknown                          |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E000 +---| RRRRRRRR                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Reserved                         |
             +----------------------------------------------+
```

## Raw Source Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 16K of ROM is permanently "hard-wired" and cannot be swapped.
- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
