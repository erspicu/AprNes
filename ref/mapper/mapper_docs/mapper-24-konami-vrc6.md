# Mapper 24: Konami VRC6

- Source: `mappers-0.80.txt`
- Mapper number: `24`

## Overview

This mapper is used on several Japanese titles by Konami, such as Akumajo Dracula [Castlevania] 3. As far as I know, it was not used on U.S. games.

## Implementation Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.
- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.
- The IRQ counter is incremented each 113.75 cycles, which is equivalent to one scanline. Unlike a real scanline counter, this "scanline-emulated" counter apparently continues to run during VBlank. When the IRQ counter value reaches $FF, IRQ's will be set off, and the counter is reset.
- There are more registers which I don't understand the usage of and which are not detailed here. There's also a custom sound chip, the operation of which is unknown to me. As always, any extra information is welcome.

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

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
