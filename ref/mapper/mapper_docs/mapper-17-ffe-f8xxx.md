# Mapper 17: FFE F8xxx

- Source: `mappers-0.80.txt`
- Mapper number: `17`

## Overview

Several hacked Japanese titles use this mapper, such as the hacked versions of Parodius and DragonBall Z 3.

## Implementation Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000.
- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.
- The IRQ counter is incremented at each scanline. When it reaches $FFFF, it is reset to zero and an IRQ interrupt is executed.

## Full Register And Behavior Reference

```text
 +----------------------+

 +--------------------------------------------------------------------+
 | Several hacked Japanese titles use this mapper, such as the hacked |
 | versions of Parodius and DragonBall Z 3.                           |
 +--------------------------------------------------------------------+

 +-------+   +--------------------------------------------------+
 | $42FC +---| xxxPxxxx                                         |
 +-------+   |    |                                             |
             |    |                                             |
             |    |                                             |
             |    +--------- Unknown                            |
             +--------------------------------------------------+

 +-------+   +--------------------------------------------------+
 | $42FD +---| xxxMxxxx                                         |
 +-------+   |    |                                             |
             |    |                                             |
             |    |                                             |
             |    +--------- Unknown                            |
             +--------------------------------------------------+

 +-------+   +--------------------------------------------------+
 | $42FE +---| xxxPxxxx                                         |
 +-------+   |    |                                             |
             |    |                                             |
             |    |                                             |
             |    +--------- Page Select                        |
             |                0 - Mirror pages from PPU $2400   |
             |                1 - Mirror pages from PPU $2000   |
             +--------------------------------------------------+

 +-------+   +--------------------------------------------------+
 | $42FF +---| xxxMxxxx                                         |
 +-------+   |    |                                             |
             |    |                                             |
             |    |                                             |
             |    +--------- Mirroring Select                   |
             |                0 - Horizontal mirroring          |
             |                1 - Vertical mirroring            |
             +--------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4501 +---| xxxxxxxx                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Control Register 0           |
             |              Any value written here will     |
             |              disable IRQ's.                  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4502 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Low byte of IRQ counter          |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4503 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- High byte of IRQ counter and     |
             |             IRQ Control Register 1           |
             |              Any value written here will     |
             |              enable IRQ's.                   |
             +----------------------------------------------+

 +-------+   +-----------------------------------------+
 | $4504 +---| PPPPPPPP                                |
 +-------+   | +------+                                |
             |    |                                    |
             |    |                                    |
             |    +------- Select 8K ROM bank at $8000 |
             +-----------------------------------------+

 +-------+   +-----------------------------------------+
 | $4505 +---| PPPPPPPP                                |
 +-------+   | +------+                                |
             |    |                                    |
             |    |                                    |
             |    +------- Select 8K ROM bank at $A000 |
             +-----------------------------------------+

 +-------+   +-----------------------------------------+
 | $4506 +---| PPPPPPPP                                |
 +-------+   | +------+                                |
             |    |                                    |
             |    |                                    |
             |    +------- Select 8K ROM bank at $C000 |
             +-----------------------------------------+

 +-------+   +-----------------------------------------+
 | $4507 +---| PPPPPPPP                                |
 +-------+   | +------+                                |
             |    |                                    |
             |    |                                    |
             |    +------- Select 8K ROM bank at $E000 |
             +-----------------------------------------+

 +-------+   +----------------------------------------------+
 | $4510 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4511 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4512 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4513 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0C00 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4514 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4515 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4516 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4517 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1C00 |
             +----------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
