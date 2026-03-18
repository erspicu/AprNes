# Mapper 69: Sunsoft FME-7

- Source: `mappers-0.80.txt`
- Mapper number: `69`

## Overview

This mapper is used on several Japanese titles, such as Batman Japanese, and on the U.S. title Batman: Return of the Joker. Thanks to D for hacking this mapper.

## Implementation Notes

- The last 8K ROM page is permanently "hard-wired" to the last 8K ROM page in the cart.
- This mapper is deployed in a manner similar to that of MMC3. First a register number is written to $8000 and then the register chosen can be accessed via $A000.
- Command #8 works in the following manner. The upper 2 bits select what is swapped into $6000-$7FFF. If bit 6 is 0, it will be ROM, selected from the other bits of the register. If it's 1, then the contents depend on bit 7. In this case, if bit 7 is 1, it will be WRAM. If it's 0, it will be pseudo-random numbers (this still hasn't been figured out).

## Full Register And Behavior Reference

```text
 +--------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles, such as Batman     |
 | Japanese, and on the U.S. title Batman: Return of the Joker.       |
 | Thanks to D for hacking this mapper.                               |
 +--------------------------------------------------------------------+

 +-------+   +---------------------------------------------------+
 | $8000 +---| xxxxRRRR                                          |
 +-------+   |     +--+                                          |
             |       +--- Register Number                        |
             |             0 - Select 1K VROM page at PPU $0000  |
             |             1 - Select 1K VROM page at PPU $0400  |
             |             2 - Select 1K VROM page at PPU $0800  |
             |             3 - Select 1K VROM page at PPU $0C00  |
             |             4 - Select 1K VROM page at PPU $1000  |
             |             5 - Select 1K VROM page at PPU $1400  |
             |             6 - Select 1K VROM page at PPU $1800  |
             |             7 - Select 1K VROM page at PPU $1C00  |
             |             8 - Select 8K ROM page at $6000       |
             |             9 - Select 8K ROM page at $8000       |
             |            10 - Select 8K ROM page at $A000       |
             |            11 - Select 8K ROM page at $C000       |
             |            12 - Select mirroring                  |
             |            13 - IRQ control                       |
             |            14 - Low byte of scanline counter      |
             |            15 - High byte of scanline counter     |
             |                                                   |
             | NOTE: I am not sure if the information for        |
             |        registers 8, 12, 13, 14, and 15 is correct.|
             |                                                   |
             +---------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A000 +---| VVVVVVVV                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Register Write                   |
             |              Activates the command number    |
             |              written to bits 0-3 of $8000    |
             +----------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
