# Mapper 8: FFE F3xxx

- Source: `mappers-0.80.txt`
- Mapper number: `8`
- Purpose: implementation-oriented extraction for emulator development

## Overview

Several hacked Japanese titles use this mapper, such as the hacked version of Doraemon.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.

## CPU Register Map Summary

- `$8000 - $FFFF`: +---------------+ | | || | |
- `$0000`: | | |
- `$8000`: +-----------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the SECOND 16K ROM bank is loaded into $C000. This 16K bank is permanently "hard-wired" to $C000, and it cannot be swapped.
- I do not know if all 5 bits of the PRG switcher are used. Possibly only three or four are used.

## CHR / VROM / VRAM Behavior

- The first 8K VROM bank is swapped into PPU $0000 when the cart is reset.

## Mirroring Control

- Not explicitly documented in the source section.

## IRQ Behavior

- Not explicitly documented in the source section.

## WRAM / SaveRAM / Expansion I/O

- Not explicitly documented in the source section.

## Implementation Notes

- Not many games use this mapper, but it's easy to implement, so you might as well add it if you're writing a NES emulator.

## Uncertainties In Source

- Not explicitly documented in the source section.

## Full Register And Behavior Reference

```text
 +---------------------+

 +--------------------------------------------------------------------+
 | Several hacked Japanese titles use this mapper, such as the hacked |
 | version of Doraemon.                                               |
 +--------------------------------------------------------------------+

 +---------------+    +-----------------------------------------------+
 | $8000 - $FFFF +----| PPPPPCCC                                      |
 +---------------+    | |   || |                                      |
                      | +---|+------ Select 8K VROM bank at PPU $0000 |
                      |     |                                         |
                      |     +------- Select 16K ROM bank at $8000     |
                      +-----------------------------------------------+
```

## Raw Source Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the SECOND 16K ROM bank is loaded into $C000. This 16K bank is permanently "hard-wired" to $C000, and it cannot be swapped.
- The first 8K VROM bank is swapped into PPU $0000 when the cart is reset.
- I do not know if all 5 bits of the PRG switcher are used. Possibly only three or four are used.
- Not many games use this mapper, but it's easy to implement, so you might as well add it if you're writing a NES emulator.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
