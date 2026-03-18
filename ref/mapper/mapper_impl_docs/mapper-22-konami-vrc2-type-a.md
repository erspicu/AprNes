# Mapper 22: Konami VRC2 type A

- Source: `mappers-0.80.txt`
- Mapper number: `22`
- Purpose: implementation-oriented extraction for emulator development

## Overview

This mapper is used on the Japanese title TwinBee 3 by Konami.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.

## CPU Register Map Summary

- `$8000`: +-------+ | +------+ | | | | | | |
- `$8000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$9000`: +-------+ | || | | +| | | | | | +--- Mirroring/Page Select | | 0 - Vertical mirroring | | 1 - Horizontal mirroring |
- `$2000`: | NOTE: I don't have any confidence in the | | accuracy of this information. | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$A000`: +-------+ | +------+ | | | | | | |
- `$A000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$B000`: +-------+ | +------+ | | | | | | |
- `$0000`: | Shift this value right one bit | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$B001`: +-------+ | +------+ | | | | | | |
- `$0400`: | Shift this value right one bit | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$C000`: +-------+ | +------+ | | | | | | |
- `$0800`: | Shift this value right one bit | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$C001`: +-------+ | +------+ | | | | | | |
- `$0C00`: | Shift this value right one bit | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$D000`: +-------+ | +------+ | | | | | | |
- `$1000`: | Shift this value right one bit | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$D001`: +-------+ | +------+ | | | | | | |
- `$1400`: | Shift this value right one bit | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$E000`: +-------+ | +------+ | | | | | | |
- `$1800`: | Shift this value right one bit | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$E001`: +-------+ | +------+ | | | | | | |
- `$1C00`: | Shift this value right one bit | +----------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 16K of ROM is permanently "hard-wired" and cannot be swapped.

## CHR / VROM / VRAM Behavior

- On reset, the first 8K of VROM is swapped into PPU $0000.

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
 +-------------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on the Japanese title TwinBee 3 by Konami.     |
 +--------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $8000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $9000 +---| xxxxxxMM                                     |
 +-------+   |       ||                                     |
             |       +|                                     |
             |        |                                     |
             |        +--- Mirroring/Page Select            |
             |              0 - Vertical mirroring          |
             |              1 - Horizontal mirroring        |
             |              2 - Mirror pages from $2400     |
             |              3 - Mirror pages from $2000     |
             | NOTE: I don't have any confidence in the     |
             |       accuracy of this information.          |
             +----------------------------------------------+

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
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0400 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0800 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0C00 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1000 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1400 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1800 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1C00 |
             |              Shift this value right one bit  |
             +----------------------------------------------+
```

## Raw Source Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 16K of ROM is permanently "hard-wired" and cannot be swapped.
- On reset, the first 8K of VROM is swapped into PPU $0000.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
