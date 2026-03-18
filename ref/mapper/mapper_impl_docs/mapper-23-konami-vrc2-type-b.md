# Mapper 23: Konami VRC2 type B

- Source: `mappers-0.80.txt`
- Mapper number: `23`
- Purpose: implementation-oriented extraction for emulator development

## Overview

This mapper is used on several Japanese titles by Konami, such as Contra Japanese and Getsufuu Maden. As far as I know, it was not used on U.S. games.

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
- `$B000`: +-------+ | +--+ | | | | | | |
- `$0000`: | Low 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$B001`: +-------+ | +--+ | | | | | | |
- `$0000`: | High 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$B002`: +-------+ | +--+ | | | | | | |
- `$0400`: | Low 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$B003`: +-------+ | +--+ | | | | | | |
- `$0400`: | High 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$C000`: +-------+ | +--+ | | | | | | |
- `$0800`: | Low 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$C001`: +-------+ | +--+ | | | | | | |
- `$0800`: | High 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$C002`: +-------+ | +--+ | | | | | | |
- `$0C00`: | Low 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$C003`: +-------+ | +--+ | | | | | | |
- `$0C00`: | High 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$D000`: +-------+ | +--+ | | | | | | |
- `$1000`: | Low 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$D001`: +-------+ | +--+ | | | | | | |
- `$1000`: | High 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$D002`: +-------+ | +--+ | | | | | | |
- `$1400`: | Low 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$D003`: +-------+ | +--+ | | | | | | |
- `$1400`: | High 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$E000`: +-------+ | +--+ | | | | | | |
- `$1800`: | Low 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$E001`: +-------+ | +--+ | | | | | | |
- `$1800`: | High 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$E002`: +-------+ | +--+ | | | | | | |
- `$1C00`: | Low 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$E003`: +-------+ | +--+ | | | | | | |
- `$1C00`: | High 4 bits | +----------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.

## CHR / VROM / VRAM Behavior

- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.
- To use the VROM switching registers, first write the low 4 bits of the intended value into the first register, then the high 4 bits into the second register. For example, to swap 1K VROM bank $B8 to PPU $0800, you'd write $0B into $C001 and $08 to $C000. I think that some cartridges do it the other way around, writing the low nybble first.

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
 | This mapper is used on several Japanese titles by Konami, such as  |
 | Contra Japanese and Getsufuu Maden. As far as I know, it was not   |
 | used on U.S. games.                                                |
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
 | $B000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0000 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0000 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0400 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0400 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0800 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0800 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0C00 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0C00 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1000 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1000 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1400 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1400 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1800 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1800 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1C00 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1C00 |
             |              High 4 bits                     |
             +----------------------------------------------+
```

## Raw Source Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.
- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.
- To use the VROM switching registers, first write the low 4 bits of the intended value into the first register, then the high 4 bits into the second register. For example, to swap 1K VROM bank $B8 to PPU $0800, you'd write $0B into $C001 and $08 to $C000. I think that some cartridges do it the other way around, writing the low nybble first.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
