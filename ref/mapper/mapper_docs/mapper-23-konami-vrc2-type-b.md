# Mapper 23: Konami VRC2 type B

- Source: `mappers-0.80.txt`
- Mapper number: `23`

## Overview

This mapper is used on several Japanese titles by Konami, such as Contra Japanese and Getsufuu Maden. As far as I know, it was not used on U.S. games.

## Implementation Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.
- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.
- To use the VROM switching registers, first write the low 4 bits of the intended value into the first register, then the high 4 bits into the second register. For example, to swap 1K VROM bank $B8 to PPU $0800, you'd write $0B into $C001 and $08 to $C000. I think that some cartridges do it the other way around, writing the low nybble first.

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

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
