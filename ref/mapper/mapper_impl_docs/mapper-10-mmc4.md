# Mapper 10: MMC4

- Source: `mappers-0.80.txt`
- Mapper number: `10`
- Purpose: implementation-oriented extraction for emulator development

## Overview

This mapper is used on several Japanese carts such as Fire Emblem and Family War. Thanks to FanWen and Jim Geffre for the mapper information.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.

## CPU Register Map Summary

- `$A000 - $AFFF`: +---------------+ | +------+ | | | | | | |
- `$8000`: +------------------------------------------+ +---------------+ +------------------------------------------------+
- `$B000 - $BFFF`: +---------------+ | +------+ | | | | | | |
- `$0000`: | for use when latch #1 is $FD | +------------------------------------------------+ +---------------+ +------------------------------------------------+
- `$C000 - $CFFF`: +---------------+ | +------+ | | | | | | |
- `$0000`: | for use when latch #1 is $FE | +------------------------------------------------+ +---------------+ +------------------------------------------------+
- `$D000 - $DFFF`: +---------------+ | +------+ | | | | | | |
- `$1000`: | for use when latch #2 is $FD | +------------------------------------------------+ +---------------+ +------------------------------------------------+
- `$E000 - $EFFF`: +---------------+ | +------+ | | | | | | |
- `$1000`: | for use when latch #2 is $FE | +------------------------------------------------+ +---------------+ +----------------------------------------------+
- `$F000 - $FFFF`: +---------------+ | | | | | | | | | | +--- Mirroring Select | | 0 - Vertical mirroring | | 1 - Horizontal mirroring | +----------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. This last 16K bank is permanently "hard-wired" to $C000, and cannot be swapped.

## CHR / VROM / VRAM Behavior

- The "latches" can be swapped by access to PPU memory. If PPU $0FD0-$0FDF is accessed, latch #1 becomes $FD. If $0FE0-$0FEF is accessed, it becomes $FE. Latch #2 works in the same manner, except the addresses are $1FD0-$1FDF and $1FE0-$1FEF for $FD and $FE respectively. These bank switch settings take effect immediately. Latches contain $FE on reset.

## Mirroring Control

- Not explicitly documented in the source section.

## IRQ Behavior

- Not explicitly documented in the source section.

## WRAM / SaveRAM / Expansion I/O

- Not explicitly documented in the source section.

## Implementation Notes

- Not explicitly documented in the source section.

## Uncertainties In Source

- Not explicitly documented in the source section.

## Full Register And Behavior Reference

```text
 +-----------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese carts such as Fire Emblem  |
 | and Family War. Thanks to FanWen and Jim Geffre for the mapper     |
 | information.                                                       |
 +--------------------------------------------------------------------+

 +---------------+         +------------------------------------------+
 | $A000 - $AFFF +---------| PPPPPPPP                                 |
 +---------------+         | +------+                                 |
                           |    |                                     |
                           |    |                                     |
                           |    +------- Select 16K ROM bank at $8000 |
                           +------------------------------------------+

 +---------------+         +------------------------------------------------+
 | $B000 - $BFFF +---------| CCCCCCCC                                       |
 +---------------+         | +------+                                       |
                           |    |                                           |
                           |    |                                           |
                           |    +------- Select 4K VROM bank at PPU $0000   |
                           |             for use when latch #1 is $FD       |
                           +------------------------------------------------+

 +---------------+         +------------------------------------------------+
 | $C000 - $CFFF +---------| CCCCCCCC                                       |
 +---------------+         | +------+                                       |
                           |    |                                           |
                           |    |                                           |
                           |    +------- Select 4K VROM bank at PPU $0000   |
                           |             for use when latch #1 is $FE       |
                           +------------------------------------------------+

 +---------------+         +------------------------------------------------+
 | $D000 - $DFFF +---------| CCCCCCCC                                       |
 +---------------+         | +------+                                       |
                           |    |                                           |
                           |    |                                           |
                           |    +------- Select 4K VROM bank at PPU $1000   |
                           |             for use when latch #2 is $FD       |
                           +------------------------------------------------+

 +---------------+         +------------------------------------------------+
 | $E000 - $EFFF +---------| CCCCCCCC                                       |
 +---------------+         | +------+                                       |
                           |    |                                           |
                           |    |                                           |
                           |    +------- Select 4K VROM bank at PPU $1000   |
                           |             for use when latch #2 is $FE       |
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

## Raw Source Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. This last 16K bank is permanently "hard-wired" to $C000, and cannot be swapped.
- The "latches" can be swapped by access to PPU memory. If PPU $0FD0-$0FDF is accessed, latch #1 becomes $FD. If $0FE0-$0FEF is accessed, it becomes $FE. Latch #2 works in the same manner, except the addresses are $1FD0-$1FDF and $1FE0-$1FEF for $FD and $FE respectively. These bank switch settings take effect immediately. Latches contain $FE on reset.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
