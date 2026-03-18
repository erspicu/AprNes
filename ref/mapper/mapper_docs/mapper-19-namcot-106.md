# Mapper 19: Namcot 106

- Source: `mappers-0.80.txt`
- Mapper number: `19`

## Overview

This mapper is used on several Japanese titles by Namcot, such as Splatterhouse and Family Stadium '90. As far as I know, it was not used on U.S. games.

## Implementation Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.
- The LAST 8K of VROM is swapped into PPU $0000 on reset, if it is present.
- The IRQ counter is incremented at each scanline. When it reaches $7FFF, an IRQ interrupt is executed, but there is no reset. This is still preliminary and untested, and I may be wrong on this point. Splatterhouse and several other games run fine without it.
- The Namcot 106 mapper supports one or more additional sound channels. BioNES supports these. I have no clue how they work.
- Thanks to Mark Knibbs for correcting several misconceptions about this mapper that were included in 0.70.

## Full Register And Behavior Reference

```text
 +-----------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles by Namcot, such as  |
 | Splatterhouse and Family Stadium '90. As far as I know, it was not |
 | used on U.S. games.                                                |
 +--------------------------------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $5000 - $57FF +---| IIIIIIII                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Low byte of IRQ counter          |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $5800 - $5FFF +---| CIIIIIII                                     |
 +---------------+   | |+-----+                                     |
                     | |   |                                        |
                     | |   |                                        |
                     | |   +------- High bits of IRQ counter        |
                     | |                                            |
                     | +----------- IRQ Control Register            |
                     |               0 - Disable IRQ's              |
                     |               1 - Enable IRQ's               |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $8000 - $87FF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $0000 |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $8800 - $8FFF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $0400 |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $9000 - $97FF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $0800 |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $9800 - $9FFF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $0C00 |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $A000 - $A7FF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $1000 |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $A800 - $AFFF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $1400 |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $B000 - $B7FF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $1800 |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $B800 - $BFFF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $1C00 |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $C000 - $C7FF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $2000 |
                     |              A value of $E0 or above will    |
                     |              use VRAM instead                |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $C800 - $CFFF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $2400 |
                     |              A value of $E0 or above will    |
                     |              use VRAM instead                |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $D000 - $D7FF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $2800 |
                     |              A value of $E0 or above will    |
                     |              use VRAM instead                |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $D800 - $DFFF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $2C00 |
                     |              A value of $E0 or above will    |
                     |              use VRAM instead                |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $E000 - $E7FF +---| PPPPPPPP                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 8K ROM bank at $8000      |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $E800 - $EFFF +---| PPPPPPPP                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 8K ROM bank at $A000      |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $F000 - $F7FF +---| PPPPPPPP                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 8K ROM bank at $C000      |
                     +----------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
