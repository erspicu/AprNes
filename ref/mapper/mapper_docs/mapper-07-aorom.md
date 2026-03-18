# Mapper 7: AOROM

- Source: `mappers-0.80.txt`
- Mapper number: `7`

## Overview

Numerous games released by Rare Ltd. use this mapper, such as Battletoads, Wizards & Warriors, and Solar Jetman.

## Implementation Notes

- The first 32K ROM bank is swapped into $8000 when the cart is started or reset.
- This mapper has no provisions for VROM; therefore, all carts using it have 8K of VRAM at PPU $0000.
- Many carts using this mapper need precise NES timing to work properly. If you're writing an emulator, be sure that you have provisions for switching screens during refresh, and be sure the one-screen mirroring is emulated properly. Also make sure that you have provisions for palette changes in midframe and for special handling of mid-HBlank writes to $2006.

## Full Register And Behavior Reference

```text
 +-----------------+

 +--------------------------------------------------------------------+
 | Numerous games released by Rare Ltd. use this mapper, such as      |
 | Battletoads, Wizards & Warriors, and Solar Jetman.                 |
 +--------------------------------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $8000 - $FFFF +---| xxxSPPPP                                     |
 +---------------+   |    ||  |                                     |
                     |    |+--|                                     |
                     |    |   |                                     |
                     |    |   +---- Select 32K ROM bank at $8000    |
                     |    |                                         |
                     |    +------- One-Screen Mirroring             |
                     |              0 = Mirror pages from PPU $2000 |
                     |              1 = Mirror pages from PPU $2400 |
                     +----------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
