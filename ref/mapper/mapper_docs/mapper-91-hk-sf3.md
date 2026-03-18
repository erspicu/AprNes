# Mapper 91: HK-SF3

- Source: `mappers-0.80.txt`
- Mapper number: `91`

## Overview

This mapper is used on the pirate cart with a title screen reading "Street Fighter 3". It may or may not have been used in other bootleg games. Thanks to Mark Knibbs for information regarding this mapper.

## Implementation Notes

- When the cart is first started, the LAST 16K ROM bank in the cart is loaded into both $8000 and $C000. The 16K at $C000 is permanently "hard-wired" to $C000 and cannot be swapped.
- Vertical mirroring is always active.
- Some of the registers can be accessed from other addresses than those listed above. For example, $7000 can also be accessed from $7002, $7004, and so on through $7FFA. $7001 can be accessed at $7003, $7005, and so on through $7FFB. Similar rules apparently are in force for the registers at $6000-$6FFF.
- This mapper supports IRQ interrupts. I have no clue how.

## Full Register And Behavior Reference

```text
 +-------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on the pirate cart with a title screen reading |
 | "Street Fighter 3". It may or may not have been used in other      |
 | bootleg games. Thanks to Mark Knibbs for information regarding     |
 | this mapper.                                                       |
 +--------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $6000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $0000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $6001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $0800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $6002 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $1000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $6003 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $1800 |
             +----------------------------------------------+

 +-------+   +-----------------------------------------+
 | $7000 +---| PPPPPPPP                                |
 +-------+   | +------+                                |
             |    |                                    |
             |    |                                    |
             |    +------- Select 8K ROM bank at $8000 |
             +-----------------------------------------+

 +-------+   +-----------------------------------------+
 | $7001 +---| PPPPPPPP                                |
 +-------+   | +------+                                |
             |    |                                    |
             |    |                                    |
             |    +------- Select 8K ROM bank at $A000 |
             +-----------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
