# Mapper 18: Jaleco SS8806

- Source: `mappers-0.80.txt`
- Mapper number: `18`

## Overview

This mapper is used on several Japanese titles by Jaleco, such as Baseball 3. As far as I know, it was not used on U.S. games.

## Implementation Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000.
- To use the ROM and VROM switching registers, first write the low 4 bits of the intended value into the first register, then the high 4 bits into the second register. For example, to swap 1K VROM bank $B8 to PPU $0400, you'd write $0B into $A003 and $08 to $A002. I think that some cartridges do it the other way around, writing the low nybble first.
- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.
- The IRQ counter is decremented at each scanline. When it reaches zero, an IRQ interrupt is executed.
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

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
