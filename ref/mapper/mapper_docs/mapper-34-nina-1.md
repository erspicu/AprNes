# Mapper 34: Nina-1

- Source: `mappers-0.80.txt`
- Mapper number: `34`

## Overview

These two mappers were used on two U.S. games: Deadly Towers and Impossible Mission II.

## Implementation Notes

- The first 32K ROM bank is swapped into $8000 when the cart is started or reset.
- Carts without VROM (i.e. Deadly Towers) will have 8K of VRAM at PPU $0000. Carts with VROM (Impossible Mission 2) have the first 8K swapped in at reset. Apparently, this mapper is actually a combination of two actual separate mappers. Deadly Towers uses only the $8000-$FFFF switching, and Impossible Mission 2 uses only the three lower registers.
- This mapper is fairly easy to implement in a NES emulator.

## Full Register And Behavior Reference

```text
 +-------------------+

 +--------------------------------------------------------------------+
 | These two mappers were used on two U.S. games: Deadly Towers and   |
 | Impossible Mission ][.                                             |
 +--------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $7FFD +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 32K ROM bank at $8000     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $7FFE +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 4K VROM bank at PPU $0000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $7FFF +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 4K VROM bank at PPU $1000 |
             +----------------------------------------------+

 +---------------+         +------------------------------------------+
 | $8000 - $FFFF +---------| PPPPPPPP                                 |
 +---------------+         | +------+                                 |
                           |    |                                     |
                           |    |                                     |
                           |    +------- Select 32K ROM bank at $8000 |
                           +------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
