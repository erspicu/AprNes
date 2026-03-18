# Mapper 21: Konami VRC4

- Source: `mappers-0.80.txt`
- Mapper number: `21`

## Overview

This mapper is used on several Japanese titles by Konami, such as Wai Wai World 2 and Gradius 2. As far as I know, it was not used on U.S. games.

## Implementation Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.
- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.
- To use the VROM switching registers, first write the low 4 bits of the intended value into the first register, then the high 4 bits into the second register. For example, to swap 1K VROM bank $B8 to PPU $0800, you'd write $0B into $C002 and $08 to $C000. I think that some cartridges do it the other way around, writing the low nybble first. Note that this is actually two different varieties of mapper combined into one. Gradius 2 uses the pairs 0-2 and 1-3. Other games (i.e. Wai Wai World 2) use the pairs 0-2 and 4-6. In the .NES format these two are "shoe-horned" together. fwNES refers to the Gradius 2 style as mapper #25 and the Wai Wai World 2 style as mapper #21. Marat's standard lists both as #21.
- The IRQ counter is incremented each 113.75 cycles, which is equivalent to one scanline. Unlike a real scanline counter, this "scanline-emulated" counter apparently continues to run during VBlank. When the IRQ counter value reaches $FF, IRQ's will be set off, and the counter is reset.

## Full Register And Behavior Reference

```text
 +------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles by Konami, such as  |
 | Wai Wai World 2 and Gradius 2. As far as I know, it was not used   |
 | on U.S. games.                                                     |
 +--------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $8000      |
             |             or $C000 (based on bit 1 of      |
             |             $9002).                          |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $9000 +---| xxxxxxMM                                     |
 +-------+   |       ||                                     |
             |       +|                                     |
             |        |                                     |
             |        +--- Mirroring/Page Select            |
             |              0 - Vertical mirroring          |
             |              1 - Horizontal mirroring        |
             |              2 - Mirror pages from $2400     |
             |              3 - Mirror pages from $2000     |
             +----------------------------------------------+

 +-------+   +-----------------------------------------------+
 | $9002 +---| xxxxxxPS                                      |
 +-------+   |       ||                                      |
             |       ||                                      |
             |       ||                                      |
             |       |+--- SaveRAM Toggle                    |
             |       |      0 - Disable $6000-$7FFF          |
             |       |      1 - Enable $6000-$7FFF           |
             |       |                                       |
             |       +---- $8000 Switching Mode              |
             |              0 - Switch $8000-$9FFF via $8000 |
             |              1 - Switch $C000-$DFFF via $8000 |
             +-----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $9003 +---| EEEEEEEE                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- External I/O Port                |
             |              I am not sure how this works.   |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $A000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0000 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0000 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0400 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0400 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B004 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0400 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B006 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0400 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0800 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0800 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0C00 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0C00 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C004 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0C00 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C006 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0C00 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1000 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1000 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1400 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1400 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D004 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1400 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D006 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1400 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1800 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1800 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1C00 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1C00 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E004 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1C00 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E006 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1C00 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F000 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Counter Register             |
             |              The IRQ countdown value is      |
             |              stored here.                    |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F001 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Counter Register             |
             |              The IRQ countdown value is      |
             |              stored here. (Apparently is     |
             |              the same register as $F000.)    |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F002 +---| xxxxxxII                                     |
 +-------+   |       ||                                     |
             |       +|                                     |
             |        |                                     |
             |        +--- IRQ Control Register 0           |
             |              0 - Disable IRQ's               |
             |              2 - Enable IRQ's                |
             |              3 - Enable IRQ's                |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F003 +---| xxxxxxxx                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Control Register 1           |
             |              Any value written here will     |
             |              reset the IRQ counter to zero.  |
             +----------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
