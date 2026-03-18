# Mapper 4: MMC3

- Source: `mappers-0.80.txt`
- Mapper number: `4`
- Purpose: implementation-oriented extraction for emulator development

## Overview

A great majority of newer NES games (early 90's) use this mapper, both U.S. and Japanese. Among the better-known MMC3 titles are Super Mario Bros. 2 and 3, MegaMan 3, 4, 5, and 6, and Crystalis.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.

## CPU Register Map Summary

- `$8000`: +-------+ | || +-+ | | || +--- Command Number |
- `$1C00`: | || 6 - Select first switchable ROM page | | || 7 - Select second switchable ROM page | | || | | |+-------- PRG Address Select |
- `$A000`: | | | | +--------- CHR Address Select | | 0 - Use normal address for commands 0-5 |
- `$1000`: +------------------------------------------------------+ +-------+ +----------------------------------------------+
- `$8001`: +-------+ | +------+ | | | | | | | | +------- Page Number for Command | | Activates the command number |
- `$8000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$A000`: +-------+ | | | | | | | | | | +--- Mirroring Select | | 0 - Horizontal mirroring | | 1 - Vertical mirroring | | NOTE: I don't have any confidence in the | | accuracy of this information. | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$A001`: +-------+ | | | | | | | | | | +---------- SaveRAM Toggle |
- `$6000-$7FFF`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$C000`: +-------+ | +------+ | | | | | | | | +------- IRQ Counter Register | | The IRQ countdown value is | | stored here. | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$C001`: +-------+ | +------+ | | | | | | | | +------- IRQ Latch Register | | A temporary value is stored | | here. | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$E000`: +-------+ | +------+ | | | | | | | | +------- IRQ Control Register 0 | | Any value written here will | | disable IRQ's and copy the | | latch register to the actual | | IRQ counter register. | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$E001`: +-------+ | +------+ | | | | | | | | +------- IRQ Control Register 1 | | Any value written here will | | enable IRQ's. | +----------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- Two of the 8K ROM banks in the PRG area are switchable. The other two are "hard-wired" to the last two banks in the cart. The default setting is switchable banks at $8000 and $A000, with banks 0 and 1 being swapped in at reset. Through bit 6 of $8000, the hard-wiring can be made to affect $8000 and $E000 instead of $C000 and $E000. The switchable banks, whatever their addresses, can be swapped through commands 6 and 7.
- A cart will first write the command and base select number to $8000, then the value to be used to $8001.

## CHR / VROM / VRAM Behavior

- On carts with VROM, the first 8K of VROM is swapped into PPU $0000 on reset. On carts without VROM, as always, there is 8K of VRAM at PPU $0000.

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
 +----------------+

 +--------------------------------------------------------------------+
 | A great majority of newer NES games (early 90's) use this mapper,  |
 | both U.S. and Japanese. Among the better-known MMC3 titles are     |
 | Super Mario Bros. 2 and 3, MegaMan 3, 4, 5, and 6, and Crystalis.  |
 +--------------------------------------------------------------------+

 +-------+   +------------------------------------------------------+
 | $8000 +---| CPxxxNNN                                             |
 +-------+   | ||   +-+                                             |
             | ||    +--- Command Number                            |
             | ||          0 - Select 2 1K VROM pages at PPU $0000  |
             | ||          1 - Select 2 1K VROM pages at PPU $0800  |
             | ||          2 - Select 1K VROM page at PPU $1000     |
             | ||          3 - Select 1K VROM page at PPU $1400     |
             | ||          4 - Select 1K VROM page at PPU $1800     |
             | ||          5 - Select 1K VROM page at PPU $1C00     |
             | ||          6 - Select first switchable ROM page     |
             | ||          7 - Select second switchable ROM page    |
             | ||                                                   |
             | |+-------- PRG Address Select                        |
             | |           0 - Enable swapping for $8000 and $A000  |
             | |           1 - Enable swapping for $A000 and $C000  |
             | |                                                    |
             | +--------- CHR Address Select                        |
             |             0 - Use normal address for commands 0-5  |
             |             1 - XOR command 0-5 address with $1000   |
             +------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8001 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Page Number for Command          |
             |              Activates the command number    |
             |              written to bits 0-2 of $8000    |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A000 +---| xxxxxxxM                                     |
 +-------+   |        |                                     |
             |        |                                     |
             |        |                                     |
             |        +--- Mirroring Select                 |
             |              0 - Horizontal mirroring        |
             |              1 - Vertical mirroring          |
             | NOTE: I don't have any confidence in the     |
             |       accuracy of this information.          |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A001 +---| Sxxxxxxx                                     |
 +-------+   | |                                            |
             | |                                            |
             | |                                            |
             | +---------- SaveRAM Toggle                   |
             |              0 - Disable $6000-$7FFF         |
             |              1 - Enable $6000-$7FFF          |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C000 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Counter Register             |
             |              The IRQ countdown value is      |
             |              stored here.                    |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C001 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Latch Register               |
             |              A temporary value is stored     |
             |              here.                           |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E000 +---| xxxxxxxx                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Control Register 0           |
             |              Any value written here will     |
             |              disable IRQ's and copy the      |
             |              latch register to the actual    |
             |              IRQ counter register.           |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E001 +---| xxxxxxxx                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Control Register 1           |
             |              Any value written here will     |
             |              enable IRQ's.                   |
             +----------------------------------------------+
```

## Raw Source Notes

- Two of the 8K ROM banks in the PRG area are switchable. The other two are "hard-wired" to the last two banks in the cart. The default setting is switchable banks at $8000 and $A000, with banks 0 and 1 being swapped in at reset. Through bit 6 of $8000, the hard-wiring can be made to affect $8000 and $E000 instead of $C000 and $E000. The switchable banks, whatever their addresses, can be swapped through commands 6 and 7.
- A cart will first write the command and base select number to $8000, then the value to be used to $8001.
- On carts with VROM, the first 8K of VROM is swapped into PPU $0000 on reset. On carts without VROM, as always, there is 8K of VRAM at PPU $0000.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
