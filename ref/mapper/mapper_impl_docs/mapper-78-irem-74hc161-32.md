# Mapper 78: Irem 74HC161/32

- Source: `mappers-0.80.txt`
- Mapper number: `78`
- Purpose: implementation-oriented extraction for emulator development

## Overview

Several Japanese Irem titles use this mapper.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Keep uncertain behavior behind clear comments or validation hooks because the source flags it as incomplete.

## CPU Register Map Summary

- `$8000 - $FFFF`: +---------------+ | | || | |
- `$8000`: | | |
- `$0000`: +-----------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. This 16K bank is permanently "hard-wired" to $C000, and it cannot be swapped.

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

- The first 8K VROM bank may or may not be swapped into $0000 when the cart is reset. I have no ROM images to test.

## Full Register And Behavior Reference

```text
 +----------------------------+

 +-----------------------------------------------+
 | Several Japanese Irem titles use this mapper. |
 +-----------------------------------------------+

 +---------------+    +-----------------------------------------------+
 | $8000 - $FFFF +----| CCCCPPPP                                      |
 +---------------+    | |  ||  |                                      |
                      | +--|+------ Select 16K ROM bank at $8000      |
                      |    |                                          |
                      |    +------- Select 8K VROM bank at PPU $0000  |
                      +-----------------------------------------------+
```

## Raw Source Notes

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. This 16K bank is permanently "hard-wired" to $C000, and it cannot be swapped.
- The first 8K VROM bank may or may not be swapped into $0000 when the cart is reset. I have no ROM images to test.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
