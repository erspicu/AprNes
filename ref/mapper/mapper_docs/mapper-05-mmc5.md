# Mapper 5: MMC5

- Source: `mappers-0.80.txt`
- Mapper number: `5`

## Overview

This mapper appears in a few newer NES titles, most notably Castlevania 3. Some other games such as Uncharted Waters and several Koei titles also use this mapper. Thanks to D and Jim Geffre for this information.

## Implementation Notes

- Much of this information is incomplete and possibly inaccurate.
- To learn about MMC5's EXRAM system, read Y0SHi's NESTECH document. Note that Castlevania 3 doesn't use EXRAM but the Koei games (Bandit Kings of Ancient China, Gemfire, etc.) do use it.
- On reset, all ROM banks are set to the LAST 8K bank in the cartridge. The last 8K of this is "hard-wired" and cannot be swapped. (As far as I know.)
- MMC5 has its own sound chip, which is only used in Japanese games. I do not know how it works.

## Full Register And Behavior Reference

```text
 +----------------+

 +--------------------------------------------------------------------+
 | This mapper appears in a few newer NES titles, most notably        |
 | Castlevania 3. Some other games such as Uncharted Waters and       |
 | several Koei titles also use this mapper. Thanks to D and          |
 | Jim Geffre for this information.                                   |
 +--------------------------------------------------------------------+

 +-------+   +--------------------------------------------+
 | $5103 +---| xxxxxxSS                                   |
 +-------+   |       ||                                   |
             |       ++                                   |
             |       |                                    |
             |       +-- Sprite CHR bank size             |
             |            0 - One 8K bank                 |
             |            1 - Two 4K banks                |
             |            2 - Three 2K banks              |
             |            3 - Four 1K banks               |
             +--------------------------------------------+

 +-------+   +-----------------------------------------------+
 | $5104 +---| xxxxxxCT                                      |
 +-------+   |       ||                                      |
             |       ||                                      |
             |       ||                                      |
             |       |+--- EXRAM background tile select      |
             |       |      0 - Normal tile support          |
             |       |      1 - Enable EXRAM for tiles       |
             |       |                                       |
             |       +---- EXRAM color select                |
             |              0 - EXRAM color off              |
             |              1 - Enable EXRAM color expansion |
             +-----------------------------------------------+

 +-------+   +--------------------------------------------+
 | $5105 +---| MMMMMMMM                                   |
 +-------+   | ||||||||                                   |
             | ++++++++                                   |
             | | | | |                                    |
             | | | | +-- $2000 nametable select           |
             | | | |      Select nametable for $2000      |
             | | | |                                      |
             | | | +---- $2400 nametable select           |
             | | |        Select nametable for $2400      |
             | | |                                        |
             | | +------ $2800 nametable select           |
             | |          Select nametable for $2800      |
             | |                                          |
             | +-------- $2C00 nametable select           |
             |             Select nametable for $2C00     |
             +--------------------------------------------+

 +-------+   +----------------------------------------------+
 | $5114 +---| UPPPPPPP                                     |
 +-------+   | |+-----+                                     |
             | |  |                                         |
             | |  |                                         |
             | |  +------- Select 8K ROM bank at $8000      |
             | |                                            |
             | +---------- PRG Bank Activation              |
             |              0 = Bank contains all $FFs      |
             |              1 = Bank contains 8K of ROM     |
             |                   selected from bits 0-7     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $5115 +---| UPPPPPPP                                     |
 +-------+   | |+-----+                                     |
             | |  |                                         |
             | |  |                                         |
             | |  +------- Select 8K ROM bank at $A000      |
             | |                                            |
             | +---------- PRG Bank Activation              |
             |              0 = Bank contains all $FFs      |
             |              1 = Bank contains 8K of ROM     |
             |                   selected from bits 0-7     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $5116 +---| UPPPPPPP                                     |
 +-------+   | |+-----+                                     |
             | |  |                                         |
             | |  |                                         |
             | |  +------- Select 8K ROM bank at $C000      |
             | |                                            |
             | +---------- PRG Bank Activation              |
             |              0 = Bank contains all $FFs      |
             |              1 = Bank contains 8K of ROM     |
             |                   selected from bits 0-7     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $5120 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0000 |
             |              Only active if 1K switching is  |
             |              active via $5103                |
             |              This CHR selection is used for  |
             |              drawing sprites only.           |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $5121 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- (If 1K switching is active       |
             |              via $5103)                      |
             |             Select 1K VROM bank at PPU $0400 |
             |             (If 2K switching is active       |
             |              via $5103)                      |
             |             Select 2K VROM bank at PPU $0000 |
             |              This CHR selection is used for  |
             |              drawing sprites only.           |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $5122 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0800 |
             |              Only active if 1K switching is  |
             |              active via $5103                |
             |              This CHR selection is used for  |
             |              drawing sprites only.           |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $5123 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- (If 1K switching is active       |
             |              via $5103)                      |
             |             Select 1K VROM bank at PPU $0C00 |
             |             (If 2K switching is active       |
             |              via $5103)                      |
             |             Select 2K VROM bank at PPU $0800 |
             |             (If 4K switching is active       |
             |              via $5103)                      |
             |             Select 4K VROM bank at PPU $0000 |
             |              This CHR selection is used for  |
             |              drawing sprites only.           |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $5124 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1000 |
             |              Only active if 1K switching is  |
             |              active via $5103                |
             |              This CHR selection is used for  |
             |              drawing sprites only.           |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $5125 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- (If 1K switching is active       |
             |              via $5103)                      |
             |             Select 1K VROM bank at PPU $1400 |
             |             (If 2K switching is active       |
             |              via $5103)                      |
             |             Select 2K VROM bank at PPU $1000 |
             |              This CHR selection is used for  |
             |              drawing sprites only.           |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $5126 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1800 |
             |              Only active if 1K switching is  |
             |              active via $5103                |
             |              This CHR selection is used for  |
             |              drawing sprites only.           |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $5127 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- (If 1K switching is active       |
             |              via $5103)                      |
             |             Select 1K VROM bank at PPU $1C00 |
             |             (If 2K switching is active       |
             |              via $5103)                      |
             |             Select 2K VROM bank at PPU $1800 |
             |             (If 4K switching is active       |
             |              via $5103)                      |
             |             Select 4K VROM bank at PPU $1000 |
             |             (If 8K switching is active       |
             |              via $5103)                      |
             |             Select 8K VROM bank at PPU $0000 |
             |              This CHR selection is used for  |
             |              drawing sprites only.           |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $5128 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $0000 |
             |              This CHR selection is used only |
             |              for drawing the nametables if   |
             |              EXRAM is not activated.         |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $5129 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $0800 |
             |              This CHR selection is used only |
             |              for drawing the nametables if   |
             |              EXRAM is not activated.         |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $512A +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $1000 |
             |              This CHR selection is used only |
             |              for drawing the nametables if   |
             |              EXRAM is not activated.         |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $512B +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $1800 |
             |              This CHR selection is used only |
             |              for drawing the nametables if   |
             |              EXRAM is not activated.         |
             +----------------------------------------------+
```

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
