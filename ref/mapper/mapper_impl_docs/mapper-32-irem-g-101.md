# Mapper 32: Irem G-101

- Source: `mappers-0.80.txt`
- Mapper number: `32`
- Purpose: implementation-oriented extraction for emulator development

## Overview

This mapper is used on several Japanese titles by Irem, such as ImageFight 2. As far as I know, it was not used on U.S. games.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.

## CPU Register Map Summary

- `$8FFF`: +-------+ | +------+ | | | | | | |
- `$9FFF`: +----------------------------------------------+ +-------+ +-----------------------------------------------+
- `$9FFF`: +-------+ | || | | || | | || | | |+--- Mirroring Switch | | | 0 - Horizontal mirroring | | | 1 - Vertical mirroring | | | |
- `$C000-$DFFF`: +-----------------------------------------------+ +-------+ +----------------------------------------------+
- `$AFFF`: +-------+ | +------+ | | | | | | |
- `$A000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$BFF0`: +-------+ | +------+ | | | | | | |
- `$0000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$BFF1`: +-------+ | +------+ | | | | | | |
- `$0400`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$BFF2`: +-------+ | +------+ | | | | | | |
- `$0800`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$BFF3`: +-------+ | +------+ | | | | | | |
- `$0C00`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$BFF4`: +-------+ | +------+ | | | | | | |
- `$1000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$BFF5`: +-------+ | +------+ | | | | | | |
- `$1400`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$BFF6`: +-------+ | +------+ | | | | | | |
- `$1800`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$BFF7`: +-------+ | +------+ | | | | | | |
- `$1C00`: +----------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.

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
 +-----------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles by Irem, such as    |
 | ImageFight 2. As far as I know, it was not used on U.S. games.     |                                           |
 +--------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8FFF +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $8000      |
             |             or $C000 (based on bit 1 of      |
             |             $9FFF).                          |
             +----------------------------------------------+

 +-------+   +-----------------------------------------------+
 | $9FFF +---| xxxxxxPS                                      |
 +-------+   |       ||                                      |
             |       ||                                      |
             |       ||                                      |
             |       |+--- Mirroring Switch                  |
             |       |      0 - Horizontal mirroring         |
             |       |      1 - Vertical mirroring           |
             |       |                                       |
             |       +---- $8FFF Switching Mode              |
             |              0 - Switch $8000-$9FFF via $8FFF |
             |              1 - Switch $C000-$DFFF via $8FFF |
             +-----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $AFFF +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $A000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF0 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF1 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF2 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF3 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0C00 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF4 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF5 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF6 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF7 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1C00 |
             +----------------------------------------------+
```

## Raw Source Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.
- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
