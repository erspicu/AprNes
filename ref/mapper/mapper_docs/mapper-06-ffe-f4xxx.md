# Mapper 6: FFE F4xxx

- Source: `mappers-0.80.txt`
- Mapper number: `6`

## Overview

Several hacked Japanese titles use this mapper, such as the hacked version of Wai Wai World. The unhacked versions of these games seem to use a Konami VRC mapper, and it's better to use them if possible.

## Implementation Notes

- The IRQ counter is incremented at each scanline. When it reaches $FFFF, it is reset to zero and an IRQ interrupt is executed.
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

## Implementation Reminder

- Preserve all register side effects, startup mapping defaults, mirroring behavior, IRQ rules, and any uncertainty notes exactly as documented here when implementing this mapper.
