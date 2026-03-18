# Mapper 3: CNROM

- Source: `mappers-0.80.txt`
- Mapper number: `3`

## Overview

This mapper is used on many older U.S. and Japanese games, such as Solomon's Key, Gradius, and Hudson's Adventure Island.

## Implementation Notes

- The ROM size is either 16K or 32K and is not switchable. It is loaded in the same manner as a NROM game; in other words, it's loaded at $8000 if it's a 32K ROM size, and at $C000 if it's a 16K ROM size. (This is because a 6502 CPU requires several vectors to be at $FFFA - $FFFF, and therefore ROM needs to be there at all times.)
- The first 8K VROM bank is swapped into PPU $0000 when the cart is reset.
- This is probably the simplest memory mapper and can easily be incorporated into a NES emulator.

## Full Register And Behavior Reference

```text
 +-----------------+

 +--------------------------------------------------------------------+
 | This mapper is used on many older U.S. and Japanese games, such as |
 | Solomon's Key, Gradius, and Hudson's Adventure Island.             |
 +--------------------------------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $8000 - $FFFF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 8K VROM bank at PPU $0000 |
                     +----------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
