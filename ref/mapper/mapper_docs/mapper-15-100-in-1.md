# Mapper 15: 100-in-1

- Source: `mappers-0.80.txt`
- Mapper number: `15`

## Overview

Several hacked Japanese titles use this mapper, such as the 100-in-1 pirate cart.

## Implementation Notes

- The first 32K of ROM is loaded into $8000 on reset. There is 8K of VRAM at PPU $0000.

## Full Register And Behavior Reference

```text
 +---------------------+

 +--------------------------------------------------------------------+
 | Several hacked Japanese titles use this mapper, such as the        |
 | 100-in-1 pirate cart.                                              |
 +--------------------------------------------------------------------+

 +-------+    +------------------------------------------------+
 | $8000 +----| SMPPPPPP                                       |
 +-------+    | |||    |                                       |
              | ||+--------- Select 16K ROM bank at $8000      |
              | ||           Select next 16K ROM bank at $C000 |
              | ||                                             |
              | |+---------- Mirroring Control                 |
              | |             0 - Vertical Mirroring           |
              | |             1 - Horizontal Mirroring         |
              | |                                              |
              | +----------- Page Swap                         |
              |               0 - Swap 8K pages at $8000/$A000 |
              |               1 - Swap 8K pages at $C000/$E000 |
              +------------------------------------------------+

 +-------+    +------------------------------------------------+
 | $8001 +----| SxPPPPPP                                       |
 +-------+    | | |    |                                       |
              | | +--------- Select 16K ROM bank at $C000      |
              | |                                              |
              | +----------- Swap Register                     |
              |               Swap 8K at $C000 and $E000       |
              +------------------------------------------------+

 +-------+    +------------------------------------------------+
 | $8002 +----| SxPPPPPP                                       |
 +-------+    | | |    |                                       |
              | | +--------- Select 8K of a 16K segment at     |
              | |            $8000, $A000, $C000, and $E000.   |
              | |                                              |
              | +----------- Segment Selector                  |
              |               0 - Select lower 8K of segment   |
              |               1 - Select upper 8K of segment   |
              +------------------------------------------------+

 +-------+    +------------------------------------------------+
 | $8003 +----| SMPPPPPP                                       |
 +-------+    | |||    |                                       |
              | ||+--------- Select 16K ROM bank at $C000      |
              | ||                                             |
              | |+---------- Mirroring Control                 |
              | |             0 - Vertical Mirroring           |
              | |             1 - Horizontal Mirroring         |
              | |                                              |
              | +----------- Swap Register                     |
              |               Swap 8K at $C000 and $E000       |
              +------------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
