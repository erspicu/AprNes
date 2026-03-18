# Mapper 32: Irem G-101

- Source: `mappers-0.80.txt`
- Mapper number: `32`

## Overview

This mapper is used on several Japanese titles by Irem, such as ImageFight 2. As far as I know, it was not used on U.S. games.

## Implementation Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.
- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.

## Full Register And Behavior Reference

```text
 +-----------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles by Irem, such as    |
 | ImageFight 2. As far as I know, it was not used on U.S. games.     |                                           |
 +--------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8FFF +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $8000      |
             |             or $C000 (based on bit 1 of      |
             |             $9FFF).                          |
             +----------------------------------------------+

 +-------+   +-----------------------------------------------+
 | $9FFF +---| xxxxxxPS                                      |
 +-------+   |       ||                                      |
             |       ||                                      |
             |       ||                                      |
             |       |+--- Mirroring Switch                  |
             |       |      0 - Horizontal mirroring         |
             |       |      1 - Vertical mirroring           |
             |       |                                       |
             |       +---- $8FFF Switching Mode              |
             |              0 - Switch $8000-$9FFF via $8FFF |
             |              1 - Switch $C000-$DFFF via $8FFF |
             +-----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $AFFF +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $A000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF0 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF1 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF2 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF3 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0C00 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF4 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF5 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF6 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF7 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1C00 |
             +----------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
