# Mapper 16: Bandai

- Source: `mappers-0.80.txt`
- Mapper number: `16`

## Overview

This mapper is used on several Japanese titles by Bandai, such as the DragonBall Z series and the SD Gundam Knight series. As far as I know, it was not used on U.S. games.

## Implementation Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. This last 16K bank is permanently "hard-wired" to $C000, and it cannot be swapped.
- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.
- The IRQ counter is decremented at each scanline if active and set off when it reaches zero. An IRQ interrupt is executed at that point.

## Full Register And Behavior Reference

```text
 +-------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles by Bandai, such as  |
 | the DragonBall Z series and the SD Gundam Knight series.           |
 | As far as I know, it was not used on U.S. games.                   |
 +--------------------------------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6000, $7FF0, $8000 +---| CCCCCCCC                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Select 1K VROM bank at PPU $0000 |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6001, $7FF1, $8001 +---| CCCCCCCC                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Select 1K VROM bank at PPU $0400 |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6002, $7FF2, $8002 +---| CCCCCCCC                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Select 1K VROM bank at PPU $0800 |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6003, $7FF3, $8003 +---| CCCCCCCC                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Select 1K VROM bank at PPU $0C00 |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6004, $7FF4, $8004 +---| CCCCCCCC                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Select 1K VROM bank at PPU $1000 |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6005, $7FF5, $8005 +---| CCCCCCCC                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Select 1K VROM bank at PPU $1400 |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6006, $7FF6, $8006 +---| CCCCCCCC                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Select 1K VROM bank at PPU $1800 |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6007, $7FF7, $8007 +---| CCCCCCCC                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Select 1K VROM bank at PPU $1C00 |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6008, $7FF8, $8008 +---| PPPPPPPP                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Select 16K ROM bank at $8000     |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6009, $7FF9, $8009 +---| xxxxxxMM                                     |
 +---------------------+   |       ||                                     |
                           |       +|                                     |
                           |        |                                     |
                           |        +--- Mirroring/Page Select            |
                           |              0 - Horizontal mirroring        |
                           |              1 - Vertical mirroring          |
                           |              2 - Mirror pages from $2000     |
                           |              3 - Mirror pages from $2400     |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $600A, $7FFA, $800A +---| xxxxxxxI                                     |
 +---------------------+   |        |                                     |
                           |        |                                     |
                           |        |                                     |
                           |        +--- IRQ Control Register             |
                           |              0 - Disable IRQ's               |
                           |              1 - Enable IRQ's                |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $600B, $7FFB, $800B +---| IIIIIIII                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Low byte of IRQ counter          |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $600C, $7FFC, $800C +---| IIIIIIII                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- High byte of IRQ counter         |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $600D, $7FFD, $800D +---| EEEEEEEE                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- EPROM I/O Port                   |
                           |              I am not sure how this works.   |
                           +----------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
