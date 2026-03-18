# Mapper 68: Sunsoft Mapper #4

- Source: `mappers-0.80.txt`
- Mapper number: `68`

## Overview

This mapper is used on the Japanese title AfterBurner II by Sunsoft.

## Implementation Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 16K of ROM is permanently "hard-wired" and cannot be swapped.

## Full Register And Behavior Reference

```text
 +------------------------------+

 +---------------------------------------------------------------------+
 | This mapper is used on the Japanese title AfterBurner ][ by Sunsoft.|
 +---------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $0000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $9000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $0800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $1000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $1800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E000 +---| xxxxxxMM                                     |
 +-------+   |       ||                                     |
             |       +|                                     |
             |        |                                     |
             |        +--- Mirroring/Page Select            |
             |              0 - Horizontal mirroring        |
             |              1 - Vertical mirroring          |
             |              2 - Mirror pages from $2000     |
             |              3 - Mirror pages from $2400     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 16K ROM bank at $8000     |
             +----------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
