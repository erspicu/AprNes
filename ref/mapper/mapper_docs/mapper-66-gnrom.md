# Mapper 66: GNROM

- Source: `mappers-0.80.txt`
- Mapper number: `66`

## Overview

This mapper is used on several Japanese titles, such as DragonBall, and on U.S. titles such as Gumshoe and Dragon Power.

## Implementation Notes

- When the cart is first started or reset, the first 32K ROM bank in the cart is loaded into $8000, and the first 8K VROM bank is swapped into PPU $0000.
- This mapper is used on the DragonBall (NOT DragonBallZ) NES game. Contrary to popular belief, this mapper is NOT mapper 16!

## Full Register And Behavior Reference

```text
 +------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles, such as            |
 | DragonBall, and on U.S. titles such as Gumshoe and Dragon Power.   |                                           |
 +--------------------------------------------------------------------+

 +---------------+    +-----------------------------------------------+
 | $8000 - $FFFF +----| xxPPxxCC                                      |
 +---------------+    |   ||  ||                                      |
                      |   +|  +----- Select 8K VROM bank at PPU $0000 |
                      |    |                                          |
                      |    +-------- Select 32K ROM bank at $8000     |
                      +-----------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
