# Mapper 15: 100-in-1

- Source: `mappers-0.80.txt`
- Mapper number: `15`
- Purpose: implementation-oriented extraction for emulator development

## Overview

Several hacked Japanese titles use this mapper, such as the 100-in-1 pirate cart.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.

## CPU Register Map Summary

- `$8000`: +-------+ | ||| | |
- `$C000`: | || | | |+---------- Mirroring Control | | | 0 - Vertical Mirroring | | | 1 - Horizontal Mirroring | | | | | +----------- Page Swap |
- `$C000`: +------------------------------------------------+ +-------+ +------------------------------------------------+
- `$8001`: +-------+ | | | | |
- `$C000`: | | | | +----------- Swap Register |
- `$8002`: +-------+ | | | | | | | +--------- Select 8K of a 16K segment at |
- `$8000`: | | | | +----------- Segment Selector | | 0 - Select lower 8K of segment | | 1 - Select upper 8K of segment | +------------------------------------------------+ +-------+ +------------------------------------------------+
- `$8003`: +-------+ | ||| | |
- `$C000`: | || | | |+---------- Mirroring Control | | | 0 - Vertical Mirroring | | | 1 - Horizontal Mirroring | | | | | +----------- Swap Register |
- `$C000`: +------------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- Not explicitly documented in the source section.

## CHR / VROM / VRAM Behavior

- The first 32K of ROM is loaded into $8000 on reset. There is 8K of VRAM at PPU $0000.

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
 +---------------------+

 +--------------------------------------------------------------------+
 | Several hacked Japanese titles use this mapper, such as the        |
 | 100-in-1 pirate cart.                                              |
 +--------------------------------------------------------------------+

 +-------+    +------------------------------------------------+
 | $8000 +----| SMPPPPPP                                       |
 +-------+    | |||    |                                       |
              | ||+--------- Select 16K ROM bank at $8000      |
              | ||           Select next 16K ROM bank at $C000 |
              | ||                                             |
              | |+---------- Mirroring Control                 |
              | |             0 - Vertical Mirroring           |
              | |             1 - Horizontal Mirroring         |
              | |                                              |
              | +----------- Page Swap                         |
              |               0 - Swap 8K pages at $8000/$A000 |
              |               1 - Swap 8K pages at $C000/$E000 |
              +------------------------------------------------+

 +-------+    +------------------------------------------------+
 | $8001 +----| SxPPPPPP                                       |
 +-------+    | | |    |                                       |
              | | +--------- Select 16K ROM bank at $C000      |
              | |                                              |
              | +----------- Swap Register                     |
              |               Swap 8K at $C000 and $E000       |
              +------------------------------------------------+

 +-------+    +------------------------------------------------+
 | $8002 +----| SxPPPPPP                                       |
 +-------+    | | |    |                                       |
              | | +--------- Select 8K of a 16K segment at     |
              | |            $8000, $A000, $C000, and $E000.   |
              | |                                              |
              | +----------- Segment Selector                  |
              |               0 - Select lower 8K of segment   |
              |               1 - Select upper 8K of segment   |
              +------------------------------------------------+

 +-------+    +------------------------------------------------+
 | $8003 +----| SMPPPPPP                                       |
 +-------+    | |||    |                                       |
              | ||+--------- Select 16K ROM bank at $C000      |
              | ||                                             |
              | |+---------- Mirroring Control                 |
              | |             0 - Vertical Mirroring           |
              | |             1 - Horizontal Mirroring         |
              | |                                              |
              | +----------- Swap Register                     |
              |               Swap 8K at $C000 and $E000       |
              +------------------------------------------------+
```

## Raw Source Notes

- The first 32K of ROM is loaded into $8000 on reset. There is 8K of VRAM at PPU $0000.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
