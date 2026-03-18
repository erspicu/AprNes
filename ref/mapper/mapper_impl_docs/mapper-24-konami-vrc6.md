# Mapper 24: Konami VRC6

- Source: `mappers-0.80.txt`
- Mapper number: `24`
- Purpose: implementation-oriented extraction for emulator development

## Overview

This mapper is used on several Japanese titles by Konami, such as Akumajo Dracula [Castlevania] 3. As far as I know, it was not used on U.S. games.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.
- Implement IRQ state, reload/reset behavior, and timing model from the source notes.

## CPU Register Map Summary

- `$8000`: +-------+ | +------+ | | | | | | |
- `$8000`: +----------------------------------------------+ +-------+ +--------------------------------------------+
- `$B003`: +-------+ | | || | | | +| | | | | | | | +--- Mirroring/Page Select | | | 0 - Horizontal mirroring | | | 1 - Vertical mirroring |
- `$2400`: | | | | +------ Unknown, but usually set to 1 | +--------------------------------------------+ +-------+ +----------------------------------------------+
- `$C000`: +-------+ | +------+ | | | | | | |
- `$C000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$D000`: +-------+ | +------+ | | | | | | |
- `$0000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$D001`: +-------+ | +------+ | | | | | | |
- `$0400`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$D002`: +-------+ | +------+ | | | | | | |
- `$0800`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$D003`: +-------+ | +------+ | | | | | | |
- `$0C00`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$E000`: +-------+ | +------+ | | | | | | |
- `$1000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$E001`: +-------+ | +------+ | | | | | | |
- `$1400`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$E002`: +-------+ | +------+ | | | | | | |
- `$1800`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$E003`: +-------+ | +------+ | | | | | | |
- `$1C00`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$F000`: +-------+ | +------+ | | | | | | | | +------- IRQ Counter Register | | The IRQ countdown value is | | stored here. | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$F001`: +-------+ | | | | | | | | | | +--- IRQ Control Register 0 | | 0 - Disable IRQ's | | 1 - Enable IRQ's | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$F002`: +-------+ | +------+ | | | | | | | | +------- IRQ Control Register 1 | | Any value written here will | | reset the IRQ counter to zero. | +----------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.

## CHR / VROM / VRAM Behavior

- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.

## Mirroring Control

- Not explicitly documented in the source section.

## IRQ Behavior

- The IRQ counter is incremented each 113.75 cycles, which is equivalent to one scanline. Unlike a real scanline counter, this "scanline-emulated" counter apparently continues to run during VBlank. When the IRQ counter value reaches $FF, IRQ's will be set off, and the counter is reset.

## WRAM / SaveRAM / Expansion I/O

- Not explicitly documented in the source section.

## Implementation Notes

- There are more registers which I don't understand the usage of and which are not detailed here. There's also a custom sound chip, the operation of which is unknown to me. As always, any extra information is welcome.

## Uncertainties In Source

- Not explicitly documented in the source section.

## Full Register And Behavior Reference

```text
 +------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles by Konami, such as  |
 | Akumajo Dracula [Castlevania] 3. As far as I know, it was not used |
 | on U.S. games.                                                     |
 +--------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 16K ROM bank at $8000     |
             +----------------------------------------------+

 +-------+   +--------------------------------------------+
 | $B003 +---| xxUxMMxx                                   |
 +-------+   |   | ||                                     |
             |   | +|                                     |
             |   |  |                                     |
             |   |  +--- Mirroring/Page Select            |
             |   |        0 - Horizontal mirroring        |
             |   |        1 - Vertical mirroring          |
             |   |        2 - Mirror pages from $2000     |
             |   |        3 - Mirror pages from $2400     |
             |   |                                        |
             |   +------ Unknown, but usually set to 1    |
             +--------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $C000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D002 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D003 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0C00 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E002 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E003 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1C00 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F000 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Counter Register             |
             |              The IRQ countdown value is      |
             |              stored here.                    |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F001 +---| xxxxxxxI                                     |
 +-------+   |        |                                     |
             |        |                                     |
             |        |                                     |
             |        +--- IRQ Control Register 0           |
             |              0 - Disable IRQ's               |
             |              1 - Enable IRQ's                |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F002 +---| xxxxxxxx                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Control Register 1           |
             |              Any value written here will     |
             |              reset the IRQ counter to zero.  |
             +----------------------------------------------+
```

## Raw Source Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.
- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.
- The IRQ counter is incremented each 113.75 cycles, which is equivalent to one scanline. Unlike a real scanline counter, this "scanline-emulated" counter apparently continues to run during VBlank. When the IRQ counter value reaches $FF, IRQ's will be set off, and the counter is reset.
- There are more registers which I don't understand the usage of and which are not detailed here. There's also a custom sound chip, the operation of which is unknown to me. As always, any extra information is welcome.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
