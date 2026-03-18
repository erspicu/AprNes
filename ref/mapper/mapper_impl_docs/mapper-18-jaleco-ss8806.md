# Mapper 18: Jaleco SS8806

- Source: `mappers-0.80.txt`
- Mapper number: `18`
- Purpose: implementation-oriented extraction for emulator development

## Overview

This mapper is used on several Japanese titles by Jaleco, such as Baseball 3. As far as I know, it was not used on U.S. games.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.
- Implement IRQ state, reload/reset behavior, and timing model from the source notes.
- Keep uncertain behavior behind clear comments or validation hooks because the source flags it as incomplete.

## CPU Register Map Summary

- `$8000`: +-------+ | +--+ | | | | | | |
- `$8000`: | Low 4 bits | +-----------------------------------------+ +-------+ +-----------------------------------------+
- `$8001`: +-------+ | +--+ | | | | | | |
- `$8000`: | High 4 bits | +-----------------------------------------+ +-------+ +-----------------------------------------+
- `$8002`: +-------+ | +--+ | | | | | | |
- `$A000`: | Low 4 bits | +-----------------------------------------+ +-------+ +-----------------------------------------+
- `$8003`: +-------+ | +--+ | | | | | | |
- `$A000`: | High 4 bits | +-----------------------------------------+ +-------+ +-----------------------------------------+
- `$9000`: +-------+ | +--+ | | | | | | |
- `$C000`: | Low 4 bits | +-----------------------------------------+ +-------+ +-----------------------------------------+
- `$9001`: +-------+ | +--+ | | | | | | |
- `$C000`: | High 4 bits | +-----------------------------------------+ +-------+ +----------------------------------------------+
- `$A000`: +-------+ | +--+ | | | | | | |
- `$0000`: | Low 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$A001`: +-------+ | +--+ | | | | | | |
- `$0000`: | High 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$A002`: +-------+ | +--+ | | | | | | |
- `$0400`: | Low 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$A003`: +-------+ | +--+ | | | | | | |
- `$0400`: | High 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$B000`: +-------+ | +--+ | | | | | | |
- `$0800`: | Low 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$B001`: +-------+ | +--+ | | | | | | |
- `$0800`: | High 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$B002`: +-------+ | +--+ | | | | | | |
- `$0C00`: | Low 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$B003`: +-------+ | +--+ | | | | | | |
- `$0C00`: | High 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$C000`: +-------+ | +--+ | | | | | | |
- `$1000`: | Low 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$C001`: +-------+ | +--+ | | | | | | |
- `$1000`: | High 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$C002`: +-------+ | +--+ | | | | | | |
- `$1400`: | Low 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$C003`: +-------+ | +--+ | | | | | | |
- `$1400`: | High 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$D000`: +-------+ | +--+ | | | | | | |
- `$1800`: | Low 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$D001`: +-------+ | +--+ | | | | | | |
- `$1800`: | High 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$D002`: +-------+ | +--+ | | | | | | |
- `$1C00`: | Low 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$D003`: +-------+ | +--+ | | | | | | |
- `$1C00`: | High 4 bits | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$E000`: +-------+ | +------+ | | | | | | | | +------- Low byte of IRQ counter | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$E001`: +-------+ | +------+ | | | | | | | | +------- Low byte of IRQ counter | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$E002`: +-------+ | +------+ | | | | | | | | +------- High byte of IRQ counter | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$E003`: +-------+ | +------+ | | | | | | | | +------- High byte of IRQ counter | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$F000`: +-------+ | | | | | | | | | | +--- IRQ Control Register 0 | | 1 - Enable IRQ's | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$F001`: +-------+ | | | | | | | | | | +--- IRQ Control Register 1 | | 0 - Disable IRQ's | | 1 - Enable IRQ's | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$F002`: +-------+ | || | | || | | || | | |+--- Mirroring Control | | | 0 - Vertical mirroring | | | 1 - Horizontal mirroring | | | | | +---- One-Screen Mirroring | | 0 - Regular mirroring |
- `$2000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$F003`: +-------+ | +------+ | | | | | | | | +------- External I/O Port | | I am not sure how this works. | +----------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000.

## CHR / VROM / VRAM Behavior

- To use the ROM and VROM switching registers, first write the low 4 bits of the intended value into the first register, then the high 4 bits into the second register. For example, to swap 1K VROM bank $B8 to PPU $0400, you'd write $0B into $A003 and $08 to $A002. I think that some cartridges do it the other way around, writing the low nybble first.
- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.

## Mirroring Control

- Not explicitly documented in the source section.

## IRQ Behavior

- The IRQ counter is decremented at each scanline. When it reaches zero, an IRQ interrupt is executed.

## WRAM / SaveRAM / Expansion I/O

- Not explicitly documented in the source section.

## Implementation Notes

- Not explicitly documented in the source section.

## Uncertainties In Source

- This information is untested! I do not have any mapper 18 ROM images, unfortunately.

## Full Register And Behavior Reference

```text
 +--------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles by Jaleco, such as  |
 | Baseball 3. As far as I know, it was not used on U.S. games.       |            |
 +--------------------------------------------------------------------+

 +-------+   +-----------------------------------------+
 | $8000 +---| xxxxPPPP                                |
 +-------+   |     +--+                                |
             |       |                                 |
             |       |                                 |
             |       +---- Select 8K ROM bank at $8000 |
             |              Low 4 bits                 |
             +-----------------------------------------+

 +-------+   +-----------------------------------------+
 | $8001 +---| xxxxPPPP                                |
 +-------+   |     +--+                                |
             |       |                                 |
             |       |                                 |
             |       +---- Select 8K ROM bank at $8000 |
             |              High 4 bits                |
             +-----------------------------------------+

 +-------+   +-----------------------------------------+
 | $8002 +---| xxxxPPPP                                |
 +-------+   |     +--+                                |
             |       |                                 |
             |       |                                 |
             |       +---- Select 8K ROM bank at $A000 |
             |              Low 4 bits                 |
             +-----------------------------------------+

 +-------+   +-----------------------------------------+
 | $8003 +---| xxxxPPPP                                |
 +-------+   |     +--+                                |
             |       |                                 |
             |       |                                 |
             |       +---- Select 8K ROM bank at $A000 |
             |              High 4 bits                |
             +-----------------------------------------+

 +-------+   +-----------------------------------------+
 | $9000 +---| xxxxPPPP                                |
 +-------+   |     +--+                                |
             |       |                                 |
             |       |                                 |
             |       +---- Select 8K ROM bank at $C000 |
             |              Low 4 bits                 |
             +-----------------------------------------+

 +-------+   +-----------------------------------------+
 | $9001 +---| xxxxPPPP                                |
 +-------+   |     +--+                                |
             |       |                                 |
             |       |                                 |
             |       +---- Select 8K ROM bank at $C000 |
             |              High 4 bits                |
             +-----------------------------------------+

 +-------+   +----------------------------------------------+
 | $A000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0000 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0000 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0400 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0400 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0800 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0800 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0C00 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0C00 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1000 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1000 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1400 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1400 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1800 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1800 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1C00 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1C00 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E000 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Low byte of IRQ counter          |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E001 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Low byte of IRQ counter          |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E002 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- High byte of IRQ counter         |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E003 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- High byte of IRQ counter         |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F000 +---| xxxxxxxI                                     |
 +-------+   |        |                                     |
             |        |                                     |
             |        |                                     |
             |        +--- IRQ Control Register 0           |
             |              1 - Enable IRQ's                |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F001 +---| xxxxxxxI                                     |
 +-------+   |        |                                     |
             |        |                                     |
             |        |                                     |
             |        +--- IRQ Control Register 1           |
             |              0 - Disable IRQ's               |
             |              1 - Enable IRQ's                |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F002 +---| xxxxxxPM                                     |
 +-------+   |       ||                                     |
             |       ||                                     |
             |       ||                                     |
             |       |+--- Mirroring Control                |
             |       |      0 - Vertical mirroring          |
             |       |      1 - Horizontal mirroring        |
             |       |                                      |
             |       +---- One-Screen Mirroring             |
             |              0 - Regular mirroring           |
             |              1 - Mirror pages from PPU $2000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F003 +---| EEEEEEEE                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- External I/O Port                |
             |              I am not sure how this works.   |
             +----------------------------------------------+
```

## Raw Source Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000.
- To use the ROM and VROM switching registers, first write the low 4 bits of the intended value into the first register, then the high 4 bits into the second register. For example, to swap 1K VROM bank $B8 to PPU $0400, you'd write $0B into $A003 and $08 to $A002. I think that some cartridges do it the other way around, writing the low nybble first.
- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.
- The IRQ counter is decremented at each scanline. When it reaches zero, an IRQ interrupt is executed.
- This information is untested! I do not have any mapper 18 ROM images, unfortunately.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
