# Mapper 2: UNROM

- Source: `mappers-0.80.txt`
- Mapper number: `2`

## Overview

This mapper is used on many older U.S. and Japanese games, such as Castlevania, MegaMan, Ghosts & Goblins, and Amagon.

## Implementation Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. This last 16K bank is permanently "hard-wired" to $C000, and it cannot be swapped.
- This mapper has no provisions for VROM; therefore, all carts using it have 8K of VRAM at PPU $0000.
- Most carts with this mapper are 128K. A few, mostly Japanese carts, such as Final Fantasy 2 and Dragon Quest 3, are 256K.
- Overall, this is one of the easiest mappers to implement in a NES emulator.

## Full Register And Behavior Reference

```text
 +-----------------+

 +--------------------------------------------------------------------+
 | This mapper is used on many older U.S. and Japanese games, such as |
 | Castlevania, MegaMan, Ghosts & Goblins, and Amagon.                |
 +--------------------------------------------------------------------+

 +---------------+         +------------------------------------------+
 | $8000 - $FFFF +---------| PPPPPPPP                                 |
 +---------------+         | +------+                                 |
                           |    |                                     |
                           |    |                                     |
                           |    +------- Select 16K ROM bank at $8000 |
                           +------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
