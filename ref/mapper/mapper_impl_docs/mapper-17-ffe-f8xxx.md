# Mapper 17: FFE F8xxx

- Source: `mappers-0.80.txt`
- Mapper number: `17`
- Purpose: implementation-oriented extraction for emulator development

## Overview

Several hacked Japanese titles use this mapper, such as the hacked versions of Parodius and DragonBall Z 3.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.
- Implement IRQ state, reload/reset behavior, and timing model from the source notes.

## CPU Register Map Summary

- `$42FC`: +-------+ | | | | | | | | | | +--------- Unknown | +--------------------------------------------------+ +-------+ +--------------------------------------------------+
- `$42FD`: +-------+ | | | | | | | | | | +--------- Unknown | +--------------------------------------------------+ +-------+ +--------------------------------------------------+
- `$42FE`: +-------+ | | | | | | | | | | +--------- Page Select |
- `$2000`: +--------------------------------------------------+ +-------+ +--------------------------------------------------+
- `$42FF`: +-------+ | | | | | | | | | | +--------- Mirroring Select | | 0 - Horizontal mirroring | | 1 - Vertical mirroring | +--------------------------------------------------+ +-------+ +----------------------------------------------+
- `$4501`: +-------+ | +------+ | | | | | | | | +------- IRQ Control Register 0 | | Any value written here will | | disable IRQ's. | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$4502`: +-------+ | +------+ | | | | | | | | +------- Low byte of IRQ counter | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$4503`: +-------+ | +------+ | | | | | | | | +------- High byte of IRQ counter and | | IRQ Control Register 1 | | Any value written here will | | enable IRQ's. | +----------------------------------------------+ +-------+ +-----------------------------------------+
- `$4504`: +-------+ | +------+ | | | | | | |
- `$8000`: +-----------------------------------------+ +-------+ +-----------------------------------------+
- `$4505`: +-------+ | +------+ | | | | | | |
- `$A000`: +-----------------------------------------+ +-------+ +-----------------------------------------+
- `$4506`: +-------+ | +------+ | | | | | | |
- `$C000`: +-----------------------------------------+ +-------+ +-----------------------------------------+
- `$4507`: +-------+ | +------+ | | | | | | |
- `$E000`: +-----------------------------------------+ +-------+ +----------------------------------------------+
- `$4510`: +-------+ | +------+ | | | | | | |
- `$0000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$4511`: +-------+ | +------+ | | | | | | |
- `$0400`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$4512`: +-------+ | +------+ | | | | | | |
- `$0800`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$4513`: +-------+ | +------+ | | | | | | |
- `$0C00`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$4514`: +-------+ | +------+ | | | | | | |
- `$1000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$4515`: +-------+ | +------+ | | | | | | |
- `$1400`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$4516`: +-------+ | +------+ | | | | | | |
- `$1800`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$4517`: +-------+ | +------+ | | | | | | |
- `$1C00`: +----------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000.

## CHR / VROM / VRAM Behavior

- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.

## Mirroring Control

- Not explicitly documented in the source section.

## IRQ Behavior

- The IRQ counter is incremented at each scanline. When it reaches $FFFF, it is reset to zero and an IRQ interrupt is executed.

## WRAM / SaveRAM / Expansion I/O

- Not explicitly documented in the source section.

## Implementation Notes

- Not explicitly documented in the source section.

## Uncertainties In Source

- Not explicitly documented in the source section.

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

## Raw Source Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000.
- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.
- The IRQ counter is incremented at each scanline. When it reaches $FFFF, it is reset to zero and an IRQ interrupt is executed.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
