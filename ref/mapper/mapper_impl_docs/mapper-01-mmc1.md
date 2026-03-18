# Mapper 1: MMC1

- Source: `mappers-0.80.txt`
- Mapper number: `1`
- Purpose: implementation-oriented extraction for emulator development

## Overview

This mapper is used on numerous U.S. and Japanese games, including Legend of Zelda, Metroid, Rad Racer, MegaMan 2, and many others.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Implement nametable mirroring control exactly as documented.

## CPU Register Map Summary

- `$8000 - $9FFF`: | (Register 0) | | | ||||| | +---------------+ | | ||||+--- Mirroring Flag | | | |||| 0 = Horizontal | | | |||| 1 = Vertical | | | |||| | | | |||+---- One-Screen Mirroring |
- `$2000`: | | ||| 1 = Regular mirroring | | | ||| | | | ||+----- PRG Switching Area |
- `$8000`: | | || | | | |+------ PRG Switching Size |
- `$8000`: | | | 1 = Swap 16K of ROM based on bit 2 | | | | | | | +------- <Carts with VROM> | | | VROM Switching Size |
- `$0000`: | | <1024K carts> | | | 0 = Ignore 256K selection register 0 | | | 1 = Acknowledge 256K selection register 1 | | | | | +---------- Reset Port | | 0 = Do nothing | | 1 = Reset register 0 | +--------------------------------------------------------+ +---------------+ +--------------------------------------------------------+
- `$A000 - $BFFF`: | (Register 1) | | | || | |
- `$0000`: | | | If bit 4 of register 0 is off, then switch| | | | a full 8K bank. Otherwise, switch 4K only.| | | | | | | +-------- 256K ROM Selection Register 0 | | | <512K carts> | | | 0 = Swap banks from first 256K of PRG | | | 1 = Swap banks from second 256K of PRG | | | <1024K carts with bit 4 of register 0 off>| | | 0 = Swap banks from first 256K of PRG | | | 1 = Swap banks from third 256K of PRG | | | <1024K carts with bit 4 of register 0 on> | | | Low bit of 256K PRG bank selection | | | | | +----------- Reset Port | | 0 = Do nothing | | 1 = Reset register 1 | +--------------------------------------------------------+ +---------------+ +--------------------------------------------------------+
- `$C000 - $DFFF`: | (Register 2) | | | || | |
- `$1000`: | | | If bit 4 of register 0 is on, then switch |
- `$1000`: | | | | | | +------ 256K ROM Selection Register 1 | | | <1024K carts with bit 4 of register 0 off> | | | Store but ignore this bit (base 256K | | | selection on 256K selection register 0) | | | <1024K carts with bit 4 of register 0 on> | | | High bit of 256K PRG bank selection | | | | | +--------- Reset Port | | 0 = Do nothing | | 1 = Reset register 2 | +--------------------------------------------------------+ +---------------+ +--------------------------------------------------------+
- `$E000 - $FFFF`: | (Register 3) | | | | | | +---------------+ | | +------ Select ROM bank | | | Size is determined by bit 3 of register 0 | | | If it's a 32K bank, it will be swapped at |
- `$8000`: | | written should be shifted right 1 bit to | | | get the actual value.) If it's a 16K bank, |
- `$8000`: | | on the value in bit 2 of register 0. | | | Don't forget to also account for the 256K | | | block swapping if the PRG size is 512K or | | | more. | | | | | +---------- Reset Port | | 0 = Do nothing | | 1 = Reset register 3 | +--------------------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K bank into $C000. Normally, the first 16K bank is swapped via register 3 and the last bank remains "hard-wired". However, bit 2 of register 0 can change this. If it's clear, then the first 16K bank is "hard-wired" to bank zero, and the last bank is swapped via register 3. Bit 3 of register 0 will override either of these states, and allow the whole 32K to be swapped.

## CHR / VROM / VRAM Behavior

- Not explicitly documented in the source section.

## Mirroring Control

- Not explicitly documented in the source section.

## IRQ Behavior

- Not explicitly documented in the source section.

## WRAM / SaveRAM / Expansion I/O

- Not explicitly documented in the source section.

## Implementation Notes

- MMC1 ports are only one bit. Therefore, a value will be written into these registers one bit at a time. Values aren't used until the entire 5-bit array is filled. This buffering can be reset by writing bit 7 of the register. Note that MMC1 only has one 5-bit array for this data, not a separate one for each register.

## Uncertainties In Source

- Not explicitly documented in the source section.

## Full Register And Behavior Reference

```text
 +----------------+

 +--------------------------------------------------------------------+
 | This mapper is used on numerous U.S. and Japanese games, including |
 | Legend of Zelda, Metroid, Rad Racer, MegaMan 2, and many others.   |
 +--------------------------------------------------------------------+

 +---------------+ +--------------------------------------------------------+
 | $8000 - $9FFF +-| RxxCFHPM                                               |
 | (Register 0)  | | |  |||||                                               |
 +---------------+ | |  ||||+--- Mirroring Flag                             |
                   | |  ||||      0 = Horizontal                            |
                   | |  ||||      1 = Vertical                              |
                   | |  ||||                                                |
                   | |  |||+---- One-Screen Mirroring                       |
                   | |  |||       0 = All pages mirrored from PPU $2000     |
                   | |  |||       1 = Regular mirroring                     |
                   | |  |||                                                 |
                   | |  ||+----- PRG Switching Area                         |
                   | |  ||        0 = Swap ROM bank at $C000                |
                   | |  ||        1 = Swap ROM bank at $8000                |
                   | |  ||                                                  |
                   | |  |+------ PRG Switching Size                         |
                   | |  |         0 = Swap 32K of ROM at $8000              |
                   | |  |         1 = Swap 16K of ROM based on bit 2        |
                   | |  |                                                   |
                   | |  +------- <Carts with VROM>                          |
                   | |           VROM Switching Size                        |
                   | |            0 = Swap 8K of VROM at PPU $0000          |
                   | |            1 = Swap 4K of VROM at PPU $0000 and $1000|
                   | |           <1024K carts>                              |
                   | |            0 = Ignore 256K selection register 0      |
                   | |            1 = Acknowledge 256K selection register 1 |
                   | |                                                      |
                   | +---------- Reset Port                                 |
                   |              0 = Do nothing                            |
                   |              1 = Reset register 0                      |
                   +--------------------------------------------------------+

 +---------------+ +--------------------------------------------------------+
 | $A000 - $BFFF +-| RxxPCCCC                                               |
 | (Register 1)  | | |  ||  |                                               |
 +---------------+ | |  |+------- Select VROM bank at $0000                 |
                   | |  |         If bit 4 of register 0 is off, then switch|
                   | |  |         a full 8K bank. Otherwise, switch 4K only.|
                   | |  |                                                   |
                   | |  +-------- 256K ROM Selection Register 0             |
                   | |            <512K carts>                              |
                   | |            0 = Swap banks from first 256K of PRG     |
                   | |            1 = Swap banks from second 256K of PRG    |
                   | |            <1024K carts with bit 4 of register 0 off>|
                   | |            0 = Swap banks from first 256K of PRG     |
                   | |            1 = Swap banks from third 256K of PRG     |
                   | |            <1024K carts with bit 4 of register 0 on> |
                   | |            Low bit of 256K PRG bank selection        |
                   | |                                                      |
                   | +----------- Reset Port                                |
                   |              0 = Do nothing                            |
                   |              1 = Reset register 1                      |
                   +--------------------------------------------------------+

 +---------------+ +--------------------------------------------------------+
 | $C000 - $DFFF +-| RxxPCCCC                                               |
 | (Register 2)  | | |  ||  |                                               |
 +---------------+ | |  |+----- Select VROM bank at $1000                   |
                   | |  |        If bit 4 of register 0 is on, then switch  |
                   | |  |        a 4K bank at $1000. Otherwise ignore it.   |
                   | |  |                                                   |
                   | |  +------ 256K ROM Selection Register 1               |
                   | |           <1024K carts with bit 4 of register 0 off> |
                   | |            Store but ignore this bit (base 256K      |
                   | |            selection on 256K selection register 0)   |
                   | |           <1024K carts with bit 4 of register 0 on>  |
                   | |            High bit of 256K PRG bank selection       |
                   | |                                                      |
                   | +--------- Reset Port                                  |
                   |             0 = Do nothing                             |
                   |             1 = Reset register 2                       |
                   +--------------------------------------------------------+

 +---------------+ +--------------------------------------------------------+
 | $E000 - $FFFF +-| RxxxCCCC                                               |
 | (Register 3)  | | |   |  |                                               |
 +---------------+ | |   +------ Select ROM bank                            |
                   | |           Size is determined by bit 3 of register 0  |
                   | |           If it's a 32K bank, it will be swapped at  |
                   | |           $8000. (NOTE: In this case, the value      |
                   | |           written should be shifted right 1 bit to   |
                   | |           get the actual value.) If it's a 16K bank, |
                   | |           it will be selected at $8000 or $C000 based|
                   | |           on the value in bit 2 of register 0.       |
                   | |           Don't forget to also account for the 256K  |
                   | |           block swapping if the PRG size is 512K or  |
                   | |           more.                                      |
                   | |                                                      |
                   | +---------- Reset Port                                 |
                   |             0 = Do nothing                             |
                   |             1 = Reset register 3                       |
                   +--------------------------------------------------------+
```

## Raw Source Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K bank into $C000. Normally, the first 16K bank is swapped via register 3 and the last bank remains "hard-wired". However, bit 2 of register 0 can change this. If it's clear, then the first 16K bank is "hard-wired" to bank zero, and the last bank is swapped via register 3. Bit 3 of register 0 will override either of these states, and allow the whole 32K to be swapped.
- MMC1 ports are only one bit. Therefore, a value will be written into these registers one bit at a time. Values aren't used until the entire 5-bit array is filled. This buffering can be reset by writing bit 7 of the register. Note that MMC1 only has one 5-bit array for this data, not a separate one for each register.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
