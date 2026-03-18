# Mapper 8: FFE F3xxx

- Source: `mappers-0.80.txt`
- Mapper number: `8`

## Overview

Several hacked Japanese titles use this mapper, such as the hacked version of Doraemon.

## Implementation Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the SECOND 16K ROM bank is loaded into $C000. This 16K bank is permanently "hard-wired" to $C000, and it cannot be swapped.
- The first 8K VROM bank is swapped into PPU $0000 when the cart is reset.
- I do not know if all 5 bits of the PRG switcher are used. Possibly only three or four are used.
- Not many games use this mapper, but it's easy to implement, so you might as well add it if you're writing a NES emulator.

## Full Register And Behavior Reference

```text
 +---------------------+

 +--------------------------------------------------------------------+
 | Several hacked Japanese titles use this mapper, such as the hacked |
 | version of Doraemon.                                               |
 +--------------------------------------------------------------------+

 +---------------+    +-----------------------------------------------+
 | $8000 - $FFFF +----| PPPPPCCC                                      |
 +---------------+    | |   || |                                      |
                      | +---|+------ Select 8K VROM bank at PPU $0000 |
                      |     |                                         |
                      |     +------- Select 16K ROM bank at $8000     |
                      +-----------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
