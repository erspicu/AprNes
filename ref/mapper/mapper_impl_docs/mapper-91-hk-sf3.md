# Mapper 91: HK-SF3

- Source: `mappers-0.80.txt`
- Mapper number: `91`
- Purpose: implementation-oriented extraction for emulator development

## Overview

This mapper is used on the pirate cart with a title screen reading "Street Fighter 3". It may or may not have been used in other bootleg games. Thanks to Mark Knibbs for information regarding this mapper.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Implement nametable mirroring control exactly as documented.
- Handle WRAM/SaveRAM/expansion I/O behavior for the documented address ranges.
- Keep uncertain behavior behind clear comments or validation hooks because the source flags it as incomplete.

## CPU Register Map Summary

- `$6000`: +-------+ | +------+ | | | | | | |
- `$0000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$6001`: +-------+ | +------+ | | | | | | |
- `$0800`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$6002`: +-------+ | +------+ | | | | | | |
- `$1000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$6003`: +-------+ | +------+ | | | | | | |
- `$1800`: +----------------------------------------------+ +-------+ +-----------------------------------------+
- `$7000`: +-------+ | +------+ | | | | | | |
- `$8000`: +-----------------------------------------+ +-------+ +-----------------------------------------+
- `$7001`: +-------+ | +------+ | | | | | | |
- `$A000`: +-----------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- When the cart is first started, the LAST 16K ROM bank in the cart is loaded into both $8000 and $C000. The 16K at $C000 is permanently "hard-wired" to $C000 and cannot be swapped.

## CHR / VROM / VRAM Behavior

- Not explicitly documented in the source section.

## Mirroring Control

- Vertical mirroring is always active.

## IRQ Behavior

- Not explicitly documented in the source section.

## WRAM / SaveRAM / Expansion I/O

- Some of the registers can be accessed from other addresses than those listed above. For example, $7000 can also be accessed from $7002, $7004, and so on through $7FFA. $7001 can be accessed at $7003, $7005, and so on through $7FFB. Similar rules apparently are in force for the registers at $6000-$6FFF.

## Implementation Notes

- Not explicitly documented in the source section.

## Uncertainties In Source

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

## Raw Source Notes

- When the cart is first started, the LAST 16K ROM bank in the cart is loaded into both $8000 and $C000. The 16K at $C000 is permanently "hard-wired" to $C000 and cannot be swapped.
- Vertical mirroring is always active.
- Some of the registers can be accessed from other addresses than those listed above. For example, $7000 can also be accessed from $7002, $7004, and so on through $7FFA. $7001 can be accessed at $7003, $7005, and so on through $7FFB. Similar rules apparently are in force for the registers at $6000-$6FFF.
- This mapper supports IRQ interrupts. I have no clue how.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
