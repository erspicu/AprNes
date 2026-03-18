# Mapper 78: Irem 74HC161/32

- Source: `mappers-0.80.txt`
- Mapper number: `78`

## Overview

Several Japanese Irem titles use this mapper.

## Implementation Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. This 16K bank is permanently "hard-wired" to $C000, and it cannot be swapped.
- The first 8K VROM bank may or may not be swapped into $0000 when the cart is reset. I have no ROM images to test.

## Full Register And Behavior Reference

```text
 +----------------------------+

 +-----------------------------------------------+
 | Several Japanese Irem titles use this mapper. |
 +-----------------------------------------------+

 +---------------+    +-----------------------------------------------+
 | $8000 - $FFFF +----| CCCCPPPP                                      |
 +---------------+    | |  ||  |                                      |
                      | +--|+------ Select 16K ROM bank at $8000      |
                      |    |                                          |
                      |    +------- Select 8K VROM bank at PPU $0000  |
                      +-----------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
