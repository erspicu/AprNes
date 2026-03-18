# Mapper 22: Konami VRC2 type A

- Source: `mappers-0.80.txt`
- Mapper number: `22`

## Overview

This mapper is used on the Japanese title TwinBee 3 by Konami.

## Implementation Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 16K of ROM is permanently "hard-wired" and cannot be swapped.
- On reset, the first 8K of VROM is swapped into PPU $0000.

## Full Register And Behavior Reference

```text
 +-------------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on the Japanese title TwinBee 3 by Konami.     |
 +--------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $8000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $9000 +---| xxxxxxMM                                     |
 +-------+   |       ||                                     |
             |       +|                                     |
             |        |                                     |
             |        +--- Mirroring/Page Select            |
             |              0 - Vertical mirroring          |
             |              1 - Horizontal mirroring        |
             |              2 - Mirror pages from $2400     |
             |              3 - Mirror pages from $2000     |
             | NOTE: I don't have any confidence in the     |
             |       accuracy of this information.          |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $A000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0000 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0400 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0800 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0C00 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1000 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1400 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1800 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1C00 |
             |              Shift this value right one bit  |
             +----------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
