# Mapper 11: Color Dreams

- Source: `mappers-0.80.txt`
- Mapper number: `11`

## Overview

This mapper is used on several unlicensed Color Dreams titles, including Crystal Mines and Pesterminator. I'm not sure if their religious ("Wisdom Tree") games use the same mapper or not.

## Implementation Notes

- When the cart is first started or reset, the first 32K ROM bank in the cart is loaded into $8000, and the first 8K VROM bank is swapped into PPU $0000.
- Many games using this mapper are somewhat glitchy.

## Full Register And Behavior Reference

```text
 +-------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several unlicensed Color Dreams titles,     |
 | including Crystal Mines and Pesterminator. I'm not sure if their   |
 | religious ("Wisdom Tree") games use the same mapper or not.        |
 +--------------------------------------------------------------------+

 +---------------+    +-----------------------------------------------+
 | $8000 - $FFFF +----| CCCCPPPP                                      |
 +---------------+    | |  ||  |                                      |
                      | +--|+------- Select 32K ROM bank at $8000     |
                      |    |                                          |
                      |    +-------- Select 8K VROM bank at PPU $0000 |
                      +-----------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
