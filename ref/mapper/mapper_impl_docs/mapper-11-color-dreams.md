# Mapper 11: Color Dreams

- Source: `mappers-0.80.txt`
- Mapper number: `11`
- Purpose: implementation-oriented extraction for emulator development

## Overview

This mapper is used on several unlicensed Color Dreams titles, including Crystal Mines and Pesterminator. I'm not sure if their religious ("Wisdom Tree") games use the same mapper or not.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.

## CPU Register Map Summary

- `$8000 - $FFFF`: +---------------+ | | || | |
- `$8000`: | | |
- `$0000`: +-----------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- Not explicitly documented in the source section.

## CHR / VROM / VRAM Behavior

- When the cart is first started or reset, the first 32K ROM bank in the cart is loaded into $8000, and the first 8K VROM bank is swapped into PPU $0000.

## Mirroring Control

- Not explicitly documented in the source section.

## IRQ Behavior

- Not explicitly documented in the source section.

## WRAM / SaveRAM / Expansion I/O

- Not explicitly documented in the source section.

## Implementation Notes

- Many games using this mapper are somewhat glitchy.

## Uncertainties In Source

- Not explicitly documented in the source section.

## Full Register And Behavior Reference

```text
 +-------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several unlicensed Color Dreams titles,     |
 | including Crystal Mines and Pesterminator. I'm not sure if their   |
 | religious ("Wisdom Tree") games use the same mapper or not.        |
 +--------------------------------------------------------------------+

 +---------------+    +-----------------------------------------------+
 | $8000 - $FFFF +----| CCCCPPPP                                      |
 +---------------+    | |  ||  |                                      |
                      | +--|+------- Select 32K ROM bank at $8000     |
                      |    |                                          |
                      |    +-------- Select 8K VROM bank at PPU $0000 |
                      +-----------------------------------------------+
```

## Raw Source Notes

- When the cart is first started or reset, the first 32K ROM bank in the cart is loaded into $8000, and the first 8K VROM bank is swapped into PPU $0000.
- Many games using this mapper are somewhat glitchy.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
