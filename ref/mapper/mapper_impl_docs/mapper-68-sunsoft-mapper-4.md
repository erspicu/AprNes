# Mapper 68: Sunsoft Mapper #4

- Source: `mappers-0.80.txt`
- Mapper number: `68`
- Purpose: implementation-oriented extraction for emulator development

## Overview

This mapper is used on the Japanese title AfterBurner II by Sunsoft.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Implement nametable mirroring control exactly as documented.

## CPU Register Map Summary

- `$8000`: +-------+ | +------+ | | | | | | |
- `$0000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$9000`: +-------+ | +------+ | | | | | | |
- `$0800`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$A000`: +-------+ | +------+ | | | | | | |
- `$1000`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$B000`: +-------+ | +------+ | | | | | | |
- `$1800`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$E000`: +-------+ | || | | +| | | | | | +--- Mirroring/Page Select | | 0 - Horizontal mirroring | | 1 - Vertical mirroring |
- `$2400`: +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$F000`: +-------+ | +------+ | | | | | | |
- `$8000`: +----------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 16K of ROM is permanently "hard-wired" and cannot be swapped.

## CHR / VROM / VRAM Behavior

- Not explicitly documented in the source section.

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
 +------------------------------+

 +---------------------------------------------------------------------+
 | This mapper is used on the Japanese title AfterBurner ][ by Sunsoft.|
 +---------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $0000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $9000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $0800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $1000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $1800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E000 +---| xxxxxxMM                                     |
 +-------+   |       ||                                     |
             |       +|                                     |
             |        |                                     |
             |        +--- Mirroring/Page Select            |
             |              0 - Horizontal mirroring        |
             |              1 - Vertical mirroring          |
             |              2 - Mirror pages from $2000     |
             |              3 - Mirror pages from $2400     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 16K ROM bank at $8000     |
             +----------------------------------------------+
```

## Raw Source Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 16K of ROM is permanently "hard-wired" and cannot be swapped.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
