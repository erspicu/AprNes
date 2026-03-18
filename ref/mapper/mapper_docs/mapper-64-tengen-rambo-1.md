# Mapper 64: Tengen RAMBO-1

- Source: `mappers-0.80.txt`
- Mapper number: `64`

## Overview

This mapper is used on several U.S. unlicensed titles by Tengen. They include Shinobi, Klax, and Skull & Crossbones. Thanks to D for hacking this mapper.

## Implementation Notes

- Two of the 8K ROM banks in the PRG area are switchable. The last page is "hard-wired" to the last 8K bank in the cart.
- At reset, all four 8K banks are set to the last 8K bank in the cart.
- A cart will first write the command and base select number to $8000, then the value to be used to $8001.
- On carts with VROM, the first 8K of VROM is swapped into PPU $0000 on reset. On carts without VROM, as always, there is 8K of VRAM at PPU $0000.

## Full Register And Behavior Reference

```text
 +---------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several U.S. unlicensed titles by Tengen.   |
 | They include Shinobi, Klax, and Skull & Crossbones. Thanks to D    |
 | for hacking this mapper.                                           |
 +--------------------------------------------------------------------+

 +-------+   +------------------------------------------------------+
 | $8000 +---| CPxxNNNN                                             |
 +-------+   | ||  +--+                                             |
             | ||    +--- Command Number                            |
             | ||          0 - Select 2 1K VROM pages at PPU $0000  |
             | ||          1 - Select 2 1K VROM pages at PPU $0800  |
             | ||          2 - Select 1K VROM page at PPU $1000     |
             | ||          3 - Select 1K VROM page at PPU $1400     |
             | ||          4 - Select 1K VROM page at PPU $1800     |
             | ||          5 - Select 1K VROM page at PPU $1C00     |
             | ||          6 - Select first switchable ROM page     |
             | ||          7 - Select second switchable ROM page    |
             | ||          8 - Select 1K VROM page at PPU $0400     |
             | ||          9 - Select 1K VROM page at PPU $0C00     |
             | ||          15 - Select third switchable ROM page    |
             | ||                                                   |
             | |+-------- PRG Address Select        Command Number  |
             | |                                  -#6-  -#7-  -#15- |
             | |           0 - Enable swapping at $8000/$A000/$C000 |
             | |           1 - Enable swapping at $A000/$C000/$8000 |
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
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
