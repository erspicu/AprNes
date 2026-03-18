# Mapper 6: FFE F4xxx

- Source: `mappers-0.80.txt`
- Mapper number: `6`
- Purpose: implementation-oriented extraction for emulator development

## Overview

Several hacked Japanese titles use this mapper, such as the hacked version of Wai Wai World. The unhacked versions of these games seem to use a Konami VRC mapper, and it's better to use them if possible.

## Implementation Checklist

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.
- Implement IRQ state, reload/reset behavior, and timing model from the source notes.
- Keep uncertain behavior behind clear comments or validation hooks because the source flags it as incomplete.

## CPU Register Map Summary

- `$42FC`: +-------+ | | | | | | | | | | +--------- Unknown | +--------------------------------------------------+ +-------+ +--------------------------------------------------+
- `$42FD`: +-------+ | | | | | | | | | | +--------- Unknown | +--------------------------------------------------+ +-------+ +--------------------------------------------------+
- `$42FE`: +-------+ | | | | | | | | | | +--------- Page Select |
- `$2000`: +--------------------------------------------------+ +-------+ +--------------------------------------------------+
- `$42FF`: +-------+ | | | | | | | | | | +--------- Mirroring Select | | 0 - Horizontal mirroring | | 1 - Vertical mirroring | +--------------------------------------------------+ +-------+ +-----------------------------------------------+
- `$43FE`: +-------+ | | ||| | | +----|+----- 512K PRG Select | | | | | +------ 512K CHR Select | | NOTE: I don't have any confidence in the | | accuracy of this information. | +-----------------------------------------------+ +-------+ +-----------------------------------------------+
- `$4500`: +-------+ | |||||| | | | ||+||+------ PPU Mode Select | | || || 1 - 32K | | || || 5 - 256K plus EXRAM | | || || 7 - 256K | | || || | | || |+------- SW Pin | | || | I have no idea what this does. | | || | | | || +-------- SaveRAM Toggle | | || 0 - No SaveRAM | | || 1 - SaveRAM | | || | | |+---------- Execution Mode | | | 0 - Do nothing | | | 1 - Execute game | | | | | +----------- Medium | | 0 - Famicom Disk System | | 1 - Cartridge | +-----------------------------------------------+ +-------+ +----------------------------------------------+
- `$4501`: +-------+ | +------+ | | | | | | | | +------- IRQ Control Register 0 | | Any value written here will | | disable IRQ's. | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$4502`: +-------+ | +------+ | | | | | | | | +------- Low byte of IRQ counter | +----------------------------------------------+ +-------+ +----------------------------------------------+
- `$4503`: +-------+ | +------+ | | | | | | | | +------- High byte of IRQ counter and | | IRQ Control Register 1 | | Any value written here will | | enable IRQ's. | +----------------------------------------------+ +---------------+ +-----------------------------------------------+
- `$8000 - $FFFF`: +---------------+ | | ||| | | +--|+----- Pattern Table Select | | | |
- `$8000`: +-----------------------------------------------+

## Power-On / Reset Behavior

- Not explicitly documented in the source section.

## PRG Banking Behavior

- Not explicitly documented in the source section.

## CHR / VROM / VRAM Behavior

- Not explicitly documented in the source section.

## Mirroring Control

- Not explicitly documented in the source section.

## IRQ Behavior

- The IRQ counter is incremented at each scanline. When it reaches $FFFF, it is reset to zero and an IRQ interrupt is executed.

## WRAM / SaveRAM / Expansion I/O

- Not explicitly documented in the source section.

## Implementation Notes

- Not explicitly documented in the source section.

## Uncertainties In Source

- I am not sure if all my information about this mapper is accurate.

## Full Register And Behavior Reference

```text
 +---------------------+

 +--------------------------------------------------------------------+
 | Several hacked Japanese titles use this mapper, such as the hacked |
 | version of Wai Wai World. The unhacked versions of these games     |
 | seem to use a Konami VRC mapper, and it's better to use them if    |
 | possible.                                                          |
 +--------------------------------------------------------------------+

 +-------+   +--------------------------------------------------+
 | $42FC +---| xxxPxxxx                                         |
 +-------+   |    |                                             |
             |    |                                             |
             |    |                                             |
             |    +--------- Unknown                            |
             +--------------------------------------------------+

 +-------+   +--------------------------------------------------+
 | $42FD +---| xxxMxxxx                                         |
 +-------+   |    |                                             |
             |    |                                             |
             |    |                                             |
             |    +--------- Unknown                            |
             +--------------------------------------------------+

 +-------+   +--------------------------------------------------+
 | $42FE +---| xxxPxxxx                                         |
 +-------+   |    |                                             |
             |    |                                             |
             |    |                                             |
             |    +--------- Page Select                        |
             |                0 - Mirror pages from PPU $2400   |
             |                1 - Mirror pages from PPU $2000   |
             +--------------------------------------------------+

 +-------+   +--------------------------------------------------+
 | $42FF +---| xxxMxxxx                                         |
 +-------+   |    |                                             |
             |    |                                             |
             |    |                                             |
             |    +--------- Mirroring Select                   |
             |                0 - Horizontal mirroring          |
             |                1 - Vertical mirroring            |
             +--------------------------------------------------+

 +-------+   +-----------------------------------------------+
 | $43FE +---| CCCCCCPP                                      |
 +-------+   | |    |||                                      |
             | +----|+----- 512K PRG Select                  |
             |      |                                        |
             |      +------ 512K CHR Select                  |
             | NOTE: I don't have any confidence in the      |
             |       accuracy of this information.           |
             +-----------------------------------------------+

 +-------+   +-----------------------------------------------+
 | $4500 +---| DESSWPPP                                      |
 +-------+   | |||||| |                                      |
             | ||+||+------ PPU Mode Select                  |
             | || ||         1 - 32K                         |
             | || ||         5 - 256K plus EXRAM             |
             | || ||         7 - 256K                        |
             | || ||                                         |
             | || |+------- SW Pin                           |
             | || |          I have no idea what this does.  |
             | || |                                          |
             | || +-------- SaveRAM Toggle                   |
             | ||            0 - No SaveRAM                  |
             | ||            1 - SaveRAM                     |
             | ||                                            |
             | |+---------- Execution Mode                   |
             | |             0 - Do nothing                  |
             | |             1 - Execute game                |
             | |                                             |
             | +----------- Medium                           |
             |               0 - Famicom Disk System         |
             |               1 - Cartridge                   |
             +-----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4501 +---| xxxxxxxx                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Control Register 0           |
             |              Any value written here will     |
             |              disable IRQ's.                  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4502 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Low byte of IRQ counter          |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4503 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- High byte of IRQ counter and     |
             |             IRQ Control Register 1           |
             |              Any value written here will     |
             |              enable IRQ's.                   |
             +----------------------------------------------+

 +---------------+    +-----------------------------------------------+
 | $8000 - $FFFF +----| xxPPPPCC                                      |
 +---------------+    |   |  |||                                      |
                      |   +--|+----- Pattern Table Select             |
                      |      |                                        |
                      |      +------- Select 16K ROM bank at $8000    |
                      +-----------------------------------------------+
```

## Raw Source Notes

- The IRQ counter is incremented at each scanline. When it reaches $FFFF, it is reset to zero and an IRQ interrupt is executed.
- I am not sure if all my information about this mapper is accurate.

## Implementation Reminder

- Treat this file as a structured extraction of the original document, not as independently verified hardware truth. Where the source marks behavior as uncertain, implementation should remain configurable or be validated against test ROMs and hardware behavior later.
