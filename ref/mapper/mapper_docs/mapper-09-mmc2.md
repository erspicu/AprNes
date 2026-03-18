# Mapper 9: MMC2

- Source: `mappers-0.80.txt`
- Mapper number: `9`

## Overview

This mapper is used only on the U.S. versions of Punch-Out (both standard and "Mike Tyson" versions.) Thanks to Paul Robson and Jim Geffre for the mapper information.

## Implementation Notes

- When the cart is first started, the first 8K ROM bank in the cart is loaded into $8000, and the LAST 3 8K ROM banks are loaded into $A000. These last 8K banks are permanently "hard-wired" to $A000, and cannot be swapped.
- The "latch selector" in question can be swapped by access to PPU memory. If PPU $0FD0-$0FDF or $1FD0-$1FDF is accessed, the latch selector is $FD. If $0FE0-$0FEF or $1FE0-$1FEF is accessed, the latch selector is changed to $FE. These settings take effect immediately. The latch contains $FE on reset.

## Full Register And Behavior Reference

```text
 +----------------+

 +--------------------------------------------------------------------+
 | This mapper is used only on the U.S. versions of Punch-Out (both   |
 | standard and "Mike Tyson" versions.) Thanks to Paul Robson and     |
 | Jim Geffre for the mapper information.                             |
 +--------------------------------------------------------------------+

 +---------------+         +------------------------------------------+
 | $A000 - $AFFF +---------| PPPPPPPP                                 |
 +---------------+         | +------+                                 |
                           |    |                                     |
                           |    |                                     |
                           |    +------- Select 8K ROM bank at $8000  |
                           +------------------------------------------+

 +---------------+         +-----------------------------------------------+
 | $B000 - $CFFF +---------| CCCCCCCC                                      |
 +---------------+         | +------+                                      |
                           |    |                                          |
                           |    |                                          |
                           |    +------- Select 4K VROM bank at PPU $0000  |
                           +-----------------------------------------------+

 +---------------+         +------------------------------------------------+
 | $D000 - $DFFF +---------| CCCCCCCC                                       |
 +---------------+         | +------+                                       |
                           |    |                                           |
                           |    |                                           |
                           |    +------- Select 4K VROM bank at PPU $1000   |
                           |             for use when latch selector is $FD |
                           +------------------------------------------------+

 +---------------+         +------------------------------------------------+
 | $E000 - $EFFF +---------| CCCCCCCC                                       |
 +---------------+         | +------+                                       |
                           |    |                                           |
                           |    |                                           |
                           |    +------- Select 4K VROM bank at PPU $1000   |
                           |             for use when latch selector is $FE |
                           +------------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $F000 - $FFFF +---| xxxxxxxM                                     |
 +---------------+   |        |                                     |
                     |        |                                     |
                     |        |                                     |
                     |        +--- Mirroring Select                 |
                     |              0 - Vertical mirroring          |
                     |              1 - Horizontal mirroring        |
                     +----------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
