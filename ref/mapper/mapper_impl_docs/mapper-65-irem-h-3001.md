# Mapper 65: Irem H-3001

- Source: `mappers-0.80.txt`
- Mapper number: `65`
- Purpose: implementation-oriented extraction for emulator development

## Overview

This mapper is used on several Japanese titles by Irem, such as Daiku no Gensan 2. As far as I know, it was not used on U.S. games.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.
- Implement IRQ state, reload/reset behavior, and timing model from the source notes.

## CPU Register Map Summary

- `$8000`: +-------+ | +------+ | | | | | | |
- `$8000`: +-----------------------------------------+ +-------+ +--------------------------------------------+
- `$9003`: +-------+ | +------+ | | | | | | | | +------- Mirroring | | I am not sure how this works. | +--------------------------------------------+ +-------+ +--------------------------------------------+
- `$9005`: +-------+ | +------+ | | | | | | | | +------- IRQ Control | | I am not sure how this works. | +--------------------------------------------+ +-------+ +--------------------------------------------+
- `$9006`: +-------+ | +------+ | | | | | | | | +------- IRQ Control | | I am not sure how this works. | +--------------------------------------------+ +-------+ +----------------------------------------------+
- `$A000`: +-------+ | +------+ | | | | | | |
- `$A000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$B000`: +-------+ | +------+ | | | | | | |
- `$0000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$B001`: +-------+ | +------+ | | | | | | |
- `$0400`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$B002`: +-------+ | +------+ | | | | | | |
- `$0800`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$B003`: +-------+ | +------+ | | | | | | |
- `$0C00`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$B004`: +-------+ | +------+ | | | | | | |
- `$1000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$B005`: +-------+ | +------+ | | | | | | |
- `$1400`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$B006`: +-------+ | +------+ | | | | | | |
- `$1800`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$B007`: +-------+ | +------+ | | | | | | |
- `$1C00`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$C000`: +-------+ | +------+ | | | | | | |
- `$C000`: +----------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.

## CHR / VROM / VRAM Behavior

- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.

## Mirroring Control

- Not explicitly documented in the source section.

## IRQ Behavior

- Does anyone have info on mirroring or IRQ's for this mapper?

## WRAM / SaveRAM / Expansion I/O

- Not explicitly documented in the source section.

## Implementation Notes

- Not explicitly documented in the source section.

## Uncertainties In Source

- Not explicitly documented in the source section.

## Full Register And Behavior Reference

```text
 +------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles by Irem, such as    |
 | Daiku no Gensan 2. As far as I know, it was not used on U.S. games.|                                           |
 +--------------------------------------------------------------------+

 +-------+   +-----------------------------------------+
 | $8000 +---| PPPPPPPP                                |
 +-------+   | +------+                                |
             |    |                                    |
             |    |                                    |
             |    +------- Select 8K ROM bank at $8000 |
             +-----------------------------------------+

 +-------+   +--------------------------------------------+
 | $9003 +---| MMMMMMMM                                   |
 +-------+   | +------+                                   |
             |    |                                       |
             |    |                                       |
             |    +------- Mirroring                      |
             |              I am not sure how this works. |
             +--------------------------------------------+

 +-------+   +--------------------------------------------+
 | $9005 +---| IIIIIIII                                   |
 +-------+   | +------+                                   |
             |    |                                       |
             |    |                                       |
             |    +------- IRQ Control                    |
             |              I am not sure how this works. |
             +--------------------------------------------+

 +-------+   +--------------------------------------------+
 | $9006 +---| IIIIIIII                                   |
 +-------+   | +------+                                   |
             |    |                                       |
             |    |                                       |
             |    +------- IRQ Control                    |
             |              I am not sure how this works. |
             +--------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $A000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B002 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B003 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0C00 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B004 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B005 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B006 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B007 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1C00 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $C000      |
             +----------------------------------------------+
```

## Raw Source Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.
- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.
- Does anyone have info on mirroring or IRQ's for this mapper?

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
