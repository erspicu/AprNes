# NES Mapper Tutorial Compendium

這份文件根據 `mappers-0.80.txt` 與其衍生整理稿編成，目標是給「已經知道 NES 模擬器大致架構，但還沒有很多 mapper 實作經驗」的開發者使用。

這不是硬體考古型文件，而是偏向實作導向的教學合輯。你可以把它當成一份落地指南：
- 先理解每個 mapper 到底在解決什麼問題。
- 再知道 CPU 寫入哪裡會改變 PRG / CHR / mirroring / IRQ。
- 最後依照 reset 預設狀態、固定 bank、可切換 bank 與 IRQ 規則完成實作。

## 先備觀念

在 NES 模擬器裡，mapper 通常要負責這幾件事：
- 攔截 CPU 對某些位址範圍的寫入，更新內部暫存器。
- 根據暫存器內容改變 PRG-ROM 對 CPU 位址空間的映射。
- 根據暫存器內容改變 CHR-ROM / CHR-RAM 對 PPU 位址空間的映射。
- 視需要控制 nametable mirroring。
- 視需要管理 WRAM / SaveRAM / 擴充 I/O。
- 視需要提供 scanline IRQ 或 cycle-based IRQ。

## 建議的實作骨架

如果你要實作得穩定，建議每個 mapper 至少有以下狀態：
- PRG bank registers
- CHR bank registers
- mirroring mode
- WRAM enable flag
- IRQ counter / latch / enable flag
- power-on 或 reset 後的預設映射狀態

通常也建議把 mapper 介面拆成這幾個函式：
- `cpu_read(addr)`
- `cpu_write(addr, value)`
- `ppu_read(addr)` 或 bank lookup
- `ppu_write(addr, value)`，如果有 CHR-RAM
- `reset(hard_reset)`
- `clock_cpu()` 或 `clock_scanline()`，如果 mapper 有 IRQ

## 閱讀方式

每個 mapper 條目都用同一種格式：
- `這個 mapper 在做什麼`：先用白話理解設計用途。
- `實作時最重要的事`：先抓住核心，不要一開始就陷進位元細節。
- `CPU 寫入介面`：列出要攔的位址或寄存器概念。
- `PRG / CHR / Mirroring / IRQ`：分開整理，方便直接對應程式碼。
- `實作陷阱`：列出容易寫錯的地方。
- `原始規格參考`：保留原始文字區塊，避免整理時遺漏。

## 注意

這份合輯仍然以 `mappers-0.80.txt` 為來源，因此有些 mapper 的資料本身就帶有不確定性。遇到這種情況，文件會明確標示。對這些 mapper，最安全的做法是：
- 先照文件實作可運作版本。
- 再透過測試 ROM、實際遊戲行為或其他硬體資料修正。

---

## Mapper 1: MMC1

### 這個 Mapper 在做什麼

This mapper is used on numerous U.S. and Japanese games, including Legend of Zelda, Metroid, Rad Racer, MegaMan 2, and many others.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Implement nametable mirroring control exactly as documented.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K bank into $C000. Normally, the first 16K bank is swapped via register 3 and the last bank remains "hard-wired". However, bit 2 of register 0 can change this. If it's clear, then the first 16K bank is "hard-wired" to bank zero, and the last bank is swapped via register 3. Bit 3 of register 0 will override either of these states, and allow the whole 32K to be swapped.

### CHR / VROM / VRAM

- 原始文件沒有額外的 CHR 備註，請依寄存器圖中的 PPU bank 切換規則實作。

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- MMC1 ports are only one bit. Therefore, a value will be written into these registers one bit at a time. Values aren't used until the entire 5-bit array is filled. This buffering can be reset by writing bit 7 of the register. Note that MMC1 only has one 5-bit array for this data, not a separate one for each register.

### 教學建議

- 這類 mapper 較適合作為進階題目，建議先把內部暫存器、bank 計算與 IRQ 狀態分離實作。

### 原始規格參考

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

---

## Mapper 2: UNROM

### 這個 Mapper 在做什麼

This mapper is used on many older U.S. and Japanese games, such as Castlevania, MegaMan, Ghosts & Goblins, and Amagon.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. This last 16K bank is permanently "hard-wired" to $C000, and it cannot be swapped.

### CHR / VROM / VRAM

- This mapper has no provisions for VROM; therefore, all carts using it have 8K of VRAM at PPU $0000.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- Most carts with this mapper are 128K. A few, mostly Japanese carts, such as Final Fantasy 2 and Dragon Quest 3, are 256K.
- Overall, this is one of the easiest mappers to implement in a NES emulator.

### 教學建議

- 這類 mapper 結構相對直接，很適合拿來建立你的第一版 bank switching 架構。

### 原始規格參考

```text
 +-----------------+

 +--------------------------------------------------------------------+
 | This mapper is used on many older U.S. and Japanese games, such as |
 | Castlevania, MegaMan, Ghosts & Goblins, and Amagon.                |
 +--------------------------------------------------------------------+

 +---------------+         +------------------------------------------+
 | $8000 - $FFFF +---------| PPPPPPPP                                 |
 +---------------+         | +------+                                 |
                           |    |                                     |
                           |    |                                     |
                           |    +------- Select 16K ROM bank at $8000 |
                           +------------------------------------------+
```

---

## Mapper 3: CNROM

### 這個 Mapper 在做什麼

This mapper is used on many older U.S. and Japanese games, such as Solomon's Key, Gradius, and Hudson's Adventure Island.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- The ROM size is either 16K or 32K and is not switchable. It is loaded in the same manner as a NROM game; in other words, it's loaded at $8000 if it's a 32K ROM size, and at $C000 if it's a 16K ROM size. (This is because a 6502 CPU requires several vectors to be at $FFFA - $FFFF, and therefore ROM needs to be there at all times.)

### CHR / VROM / VRAM

- The first 8K VROM bank is swapped into PPU $0000 when the cart is reset.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- This is probably the simplest memory mapper and can easily be incorporated into a NES emulator.

### 教學建議

- 這類 mapper 結構相對直接，很適合拿來建立你的第一版 bank switching 架構。

### 原始規格參考

```text
 +-----------------+

 +--------------------------------------------------------------------+
 | This mapper is used on many older U.S. and Japanese games, such as |
 | Solomon's Key, Gradius, and Hudson's Adventure Island.             |
 +--------------------------------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $8000 - $FFFF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 8K VROM bank at PPU $0000 |
                     +----------------------------------------------+
```

---

## Mapper 4: MMC3

### 這個 Mapper 在做什麼

A great majority of newer NES games (early 90's) use this mapper, both U.S. and Japanese. Among the better-known MMC3 titles are Super Mario Bros. 2 and 3, MegaMan 3, 4, 5, and 6, and Crystalis.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- Two of the 8K ROM banks in the PRG area are switchable. The other two are "hard-wired" to the last two banks in the cart. The default setting is switchable banks at $8000 and $A000, with banks 0 and 1 being swapped in at reset. Through bit 6 of $8000, the hard-wiring can be made to affect $8000 and $E000 instead of $C000 and $E000. The switchable banks, whatever their addresses, can be swapped through commands 6 and 7.
- A cart will first write the command and base select number to $8000, then the value to be used to $8001.

### CHR / VROM / VRAM

- On carts with VROM, the first 8K of VROM is swapped into PPU $0000 on reset. On carts without VROM, as always, there is 8K of VRAM at PPU $0000.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- 這個 mapper 沒有額外列出特殊陷阱；優先確保 bank 對映、固定 bank 與 reset 狀態正確。

### 教學建議

- 這類 mapper 較適合作為進階題目，建議先把內部暫存器、bank 計算與 IRQ 狀態分離實作。

### 原始規格參考

```text
 +----------------+

 +--------------------------------------------------------------------+
 | A great majority of newer NES games (early 90's) use this mapper,  |
 | both U.S. and Japanese. Among the better-known MMC3 titles are     |
 | Super Mario Bros. 2 and 3, MegaMan 3, 4, 5, and 6, and Crystalis.  |
 +--------------------------------------------------------------------+

 +-------+   +------------------------------------------------------+
 | $8000 +---| CPxxxNNN                                             |
 +-------+   | ||   +-+                                             |
             | ||    +--- Command Number                            |
             | ||          0 - Select 2 1K VROM pages at PPU $0000  |
             | ||          1 - Select 2 1K VROM pages at PPU $0800  |
             | ||          2 - Select 1K VROM page at PPU $1000     |
             | ||          3 - Select 1K VROM page at PPU $1400     |
             | ||          4 - Select 1K VROM page at PPU $1800     |
             | ||          5 - Select 1K VROM page at PPU $1C00     |
             | ||          6 - Select first switchable ROM page     |
             | ||          7 - Select second switchable ROM page    |
             | ||                                                   |
             | |+-------- PRG Address Select                        |
             | |           0 - Enable swapping for $8000 and $A000  |
             | |           1 - Enable swapping for $A000 and $C000  |
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

 +-------+   +----------------------------------------------+
 | $A001 +---| Sxxxxxxx                                     |
 +-------+   | |                                            |
             | |                                            |
             | |                                            |
             | +---------- SaveRAM Toggle                   |
             |              0 - Disable $6000-$7FFF         |
             |              1 - Enable $6000-$7FFF          |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C000 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Counter Register             |
             |              The IRQ countdown value is      |
             |              stored here.                    |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C001 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Latch Register               |
             |              A temporary value is stored     |
             |              here.                           |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E000 +---| xxxxxxxx                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Control Register 0           |
             |              Any value written here will     |
             |              disable IRQ's and copy the      |
             |              latch register to the actual    |
             |              IRQ counter register.           |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E001 +---| xxxxxxxx                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Control Register 1           |
             |              Any value written here will     |
             |              enable IRQ's.                   |
             +----------------------------------------------+
```

---

## Mapper 5: MMC5

### 這個 Mapper 在做什麼

This mapper appears in a few newer NES titles, most notably Castlevania 3. Some other games such as Uncharted Waters and several Koei titles also use this mapper. Thanks to D and Jim Geffre for this information.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- On reset, all ROM banks are set to the LAST 8K bank in the cartridge. The last 8K of this is "hard-wired" and cannot be swapped. (As far as I know.)

### CHR / VROM / VRAM

- 原始文件沒有額外的 CHR 備註，請依寄存器圖中的 PPU bank 切換規則實作。

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- Much of this information is incomplete and possibly inaccurate.
- To learn about MMC5's EXRAM system, read Y0SHi's NESTECH document. Note that Castlevania 3 doesn't use EXRAM but the Koei games (Bandit Kings of Ancient China, Gemfire, etc.) do use it.
- MMC5 has its own sound chip, which is only used in Japanese games. I do not know how it works.

### 教學建議

- 這類 mapper 較適合作為進階題目，建議先把內部暫存器、bank 計算與 IRQ 狀態分離實作。

### 原始規格參考

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

---

## Mapper 6: FFE F4xxx

### 這個 Mapper 在做什麼

Several hacked Japanese titles use this mapper, such as the hacked version of Wai Wai World. The unhacked versions of these games seem to use a Konami VRC mapper, and it's better to use them if possible.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.
- Implement IRQ state, reload/reset behavior, and timing model from the source notes.
- Keep uncertain behavior behind clear comments or validation hooks because the source flags it as incomplete.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- 原始文件沒有額外的 PRG 備註，但寄存器圖仍然是主要依據。

### CHR / VROM / VRAM

- 原始文件沒有額外的 CHR 備註，請依寄存器圖中的 PPU bank 切換規則實作。

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- The IRQ counter is incremented at each scanline. When it reaches $FFFF, it is reset to zero and an IRQ interrupt is executed.

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- I am not sure if all my information about this mapper is accurate.

### 教學建議

- 這類多半出現在特殊或盜版卡帶，文件有時不完整，請接受「先求可跑、再逐步修正」的策略。

### 原始規格參考

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

---

## Mapper 7: AOROM

### 這個 Mapper 在做什麼

Numerous games released by Rare Ltd. use this mapper, such as Battletoads, Wizards & Warriors, and Solar Jetman.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- The first 32K ROM bank is swapped into $8000 when the cart is started or reset.

### CHR / VROM / VRAM

- This mapper has no provisions for VROM; therefore, all carts using it have 8K of VRAM at PPU $0000.

### Mirroring

- Many carts using this mapper need precise NES timing to work properly. If you're writing an emulator, be sure that you have provisions for switching screens during refresh, and be sure the one-screen mirroring is emulated properly. Also make sure that you have provisions for palette changes in midframe and for special handling of mid-HBlank writes to $2006.

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- 這個 mapper 沒有額外列出特殊陷阱；優先確保 bank 對映、固定 bank 與 reset 狀態正確。

### 教學建議

- 這類 mapper 結構相對直接，很適合拿來建立你的第一版 bank switching 架構。

### 原始規格參考

```text
 +-----------------+

 +--------------------------------------------------------------------+
 | Numerous games released by Rare Ltd. use this mapper, such as      |
 | Battletoads, Wizards & Warriors, and Solar Jetman.                 |
 +--------------------------------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $8000 - $FFFF +---| xxxSPPPP                                     |
 +---------------+   |    ||  |                                     |
                     |    |+--|                                     |
                     |    |   |                                     |
                     |    |   +---- Select 32K ROM bank at $8000    |
                     |    |                                         |
                     |    +------- One-Screen Mirroring             |
                     |              0 = Mirror pages from PPU $2000 |
                     |              1 = Mirror pages from PPU $2400 |
                     +----------------------------------------------+
```

---

## Mapper 8: FFE F3xxx

### 這個 Mapper 在做什麼

Several hacked Japanese titles use this mapper, such as the hacked version of Doraemon.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the SECOND 16K ROM bank is loaded into $C000. This 16K bank is permanently "hard-wired" to $C000, and it cannot be swapped.
- I do not know if all 5 bits of the PRG switcher are used. Possibly only three or four are used.

### CHR / VROM / VRAM

- The first 8K VROM bank is swapped into PPU $0000 when the cart is reset.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- Not many games use this mapper, but it's easy to implement, so you might as well add it if you're writing a NES emulator.

### 教學建議

- 這類多半出現在特殊或盜版卡帶，文件有時不完整，請接受「先求可跑、再逐步修正」的策略。

### 原始規格參考

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

---

## Mapper 9: MMC2

### 這個 Mapper 在做什麼

This mapper is used only on the U.S. versions of Punch-Out (both standard and "Mike Tyson" versions.) Thanks to Paul Robson and Jim Geffre for the mapper information.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 8K ROM bank in the cart is loaded into $8000, and the LAST 3 8K ROM banks are loaded into $A000. These last 8K banks are permanently "hard-wired" to $A000, and cannot be swapped.

### CHR / VROM / VRAM

- The "latch selector" in question can be swapped by access to PPU memory. If PPU $0FD0-$0FDF or $1FD0-$1FDF is accessed, the latch selector is $FD. If $0FE0-$0FEF or $1FE0-$1FEF is accessed, the latch selector is changed to $FE. These settings take effect immediately. The latch contains $FE on reset.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- 這個 mapper 沒有額外列出特殊陷阱；優先確保 bank 對映、固定 bank 與 reset 狀態正確。

### 教學建議

- 這類 mapper 的重點在 CHR bank 切換條件，實作時要特別留意 PPU 相關狀態。

### 原始規格參考

```text
 +----------------+

 +--------------------------------------------------------------------+
 | This mapper is used only on the U.S. versions of Punch-Out (both   |
 | standard and "Mike Tyson" versions.) Thanks to Paul Robson and     |
 | Jim Geffre for the mapper information.                             |
 +--------------------------------------------------------------------+

 +---------------+         +------------------------------------------+
 | $A000 - $AFFF +---------| PPPPPPPP                                 |
 +---------------+         | +------+                                 |
                           |    |                                     |
                           |    |                                     |
                           |    +------- Select 8K ROM bank at $8000  |
                           +------------------------------------------+

 +---------------+         +-----------------------------------------------+
 | $B000 - $CFFF +---------| CCCCCCCC                                      |
 +---------------+         | +------+                                      |
                           |    |                                          |
                           |    |                                          |
                           |    +------- Select 4K VROM bank at PPU $0000  |
                           +-----------------------------------------------+

 +---------------+         +------------------------------------------------+
 | $D000 - $DFFF +---------| CCCCCCCC                                       |
 +---------------+         | +------+                                       |
                           |    |                                           |
                           |    |                                           |
                           |    +------- Select 4K VROM bank at PPU $1000   |
                           |             for use when latch selector is $FD |
                           +------------------------------------------------+

 +---------------+         +------------------------------------------------+
 | $E000 - $EFFF +---------| CCCCCCCC                                       |
 +---------------+         | +------+                                       |
                           |    |                                           |
                           |    |                                           |
                           |    +------- Select 4K VROM bank at PPU $1000   |
                           |             for use when latch selector is $FE |
                           +------------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $F000 - $FFFF +---| xxxxxxxM                                     |
 +---------------+   |        |                                     |
                     |        |                                     |
                     |        |                                     |
                     |        +--- Mirroring Select                 |
                     |              0 - Vertical mirroring          |
                     |              1 - Horizontal mirroring        |
                     +----------------------------------------------+
```

---

## Mapper 10: MMC4

### 這個 Mapper 在做什麼

This mapper is used on several Japanese carts such as Fire Emblem and Family War. Thanks to FanWen and Jim Geffre for the mapper information.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. This last 16K bank is permanently "hard-wired" to $C000, and cannot be swapped.

### CHR / VROM / VRAM

- The "latches" can be swapped by access to PPU memory. If PPU $0FD0-$0FDF is accessed, latch #1 becomes $FD. If $0FE0-$0FEF is accessed, it becomes $FE. Latch #2 works in the same manner, except the addresses are $1FD0-$1FDF and $1FE0-$1FEF for $FD and $FE respectively. These bank switch settings take effect immediately. Latches contain $FE on reset.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- 這個 mapper 沒有額外列出特殊陷阱；優先確保 bank 對映、固定 bank 與 reset 狀態正確。

### 教學建議

- 這類 mapper 的重點在 CHR bank 切換條件，實作時要特別留意 PPU 相關狀態。

### 原始規格參考

```text
 +-----------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese carts such as Fire Emblem  |
 | and Family War. Thanks to FanWen and Jim Geffre for the mapper     |
 | information.                                                       |
 +--------------------------------------------------------------------+

 +---------------+         +------------------------------------------+
 | $A000 - $AFFF +---------| PPPPPPPP                                 |
 +---------------+         | +------+                                 |
                           |    |                                     |
                           |    |                                     |
                           |    +------- Select 16K ROM bank at $8000 |
                           +------------------------------------------+

 +---------------+         +------------------------------------------------+
 | $B000 - $BFFF +---------| CCCCCCCC                                       |
 +---------------+         | +------+                                       |
                           |    |                                           |
                           |    |                                           |
                           |    +------- Select 4K VROM bank at PPU $0000   |
                           |             for use when latch #1 is $FD       |
                           +------------------------------------------------+

 +---------------+         +------------------------------------------------+
 | $C000 - $CFFF +---------| CCCCCCCC                                       |
 +---------------+         | +------+                                       |
                           |    |                                           |
                           |    |                                           |
                           |    +------- Select 4K VROM bank at PPU $0000   |
                           |             for use when latch #1 is $FE       |
                           +------------------------------------------------+

 +---------------+         +------------------------------------------------+
 | $D000 - $DFFF +---------| CCCCCCCC                                       |
 +---------------+         | +------+                                       |
                           |    |                                           |
                           |    |                                           |
                           |    +------- Select 4K VROM bank at PPU $1000   |
                           |             for use when latch #2 is $FD       |
                           +------------------------------------------------+

 +---------------+         +------------------------------------------------+
 | $E000 - $EFFF +---------| CCCCCCCC                                       |
 +---------------+         | +------+                                       |
                           |    |                                           |
                           |    |                                           |
                           |    +------- Select 4K VROM bank at PPU $1000   |
                           |             for use when latch #2 is $FE       |
                           +------------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $F000 - $FFFF +---| xxxxxxxM                                     |
 +---------------+   |        |                                     |
                     |        |                                     |
                     |        |                                     |
                     |        +--- Mirroring Select                 |
                     |              0 - Vertical mirroring          |
                     |              1 - Horizontal mirroring        |
                     +----------------------------------------------+
```

---

## Mapper 11: Color Dreams

### 這個 Mapper 在做什麼

This mapper is used on several unlicensed Color Dreams titles, including Crystal Mines and Pesterminator. I'm not sure if their religious ("Wisdom Tree") games use the same mapper or not.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- 原始文件沒有額外的 PRG 備註，但寄存器圖仍然是主要依據。

### CHR / VROM / VRAM

- When the cart is first started or reset, the first 32K ROM bank in the cart is loaded into $8000, and the first 8K VROM bank is swapped into PPU $0000.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- Many games using this mapper are somewhat glitchy.

### 教學建議

- 這類 mapper 結構相對直接，很適合拿來建立你的第一版 bank switching 架構。

### 原始規格參考

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

---

## Mapper 15: 100-in-1

### 這個 Mapper 在做什麼

Several hacked Japanese titles use this mapper, such as the 100-in-1 pirate cart.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- 原始文件沒有額外的 PRG 備註，但寄存器圖仍然是主要依據。

### CHR / VROM / VRAM

- The first 32K of ROM is loaded into $8000 on reset. There is 8K of VRAM at PPU $0000.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- 這個 mapper 沒有額外列出特殊陷阱；優先確保 bank 對映、固定 bank 與 reset 狀態正確。

### 教學建議

- 這類多半出現在特殊或盜版卡帶，文件有時不完整，請接受「先求可跑、再逐步修正」的策略。

### 原始規格參考

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

---

## Mapper 16: Bandai

### 這個 Mapper 在做什麼

This mapper is used on several Japanese titles by Bandai, such as the DragonBall Z series and the SD Gundam Knight series. As far as I know, it was not used on U.S. games.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.
- Implement IRQ state, reload/reset behavior, and timing model from the source notes.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. This last 16K bank is permanently "hard-wired" to $C000, and it cannot be swapped.

### CHR / VROM / VRAM

- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- The IRQ counter is decremented at each scanline if active and set off when it reaches zero. An IRQ interrupt is executed at that point.

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- 這個 mapper 沒有額外列出特殊陷阱；優先確保 bank 對映、固定 bank 與 reset 狀態正確。

### 教學建議

- 建議先用這個 mapper 建立單元測試：reset 後映射、一次寫入後的映射，以及 mirroring / IRQ 是否改變。

### 原始規格參考

```text
 +-------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles by Bandai, such as  |
 | the DragonBall Z series and the SD Gundam Knight series.           |
 | As far as I know, it was not used on U.S. games.                   |
 +--------------------------------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6000, $7FF0, $8000 +---| CCCCCCCC                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Select 1K VROM bank at PPU $0000 |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6001, $7FF1, $8001 +---| CCCCCCCC                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Select 1K VROM bank at PPU $0400 |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6002, $7FF2, $8002 +---| CCCCCCCC                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Select 1K VROM bank at PPU $0800 |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6003, $7FF3, $8003 +---| CCCCCCCC                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Select 1K VROM bank at PPU $0C00 |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6004, $7FF4, $8004 +---| CCCCCCCC                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Select 1K VROM bank at PPU $1000 |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6005, $7FF5, $8005 +---| CCCCCCCC                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Select 1K VROM bank at PPU $1400 |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6006, $7FF6, $8006 +---| CCCCCCCC                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Select 1K VROM bank at PPU $1800 |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6007, $7FF7, $8007 +---| CCCCCCCC                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Select 1K VROM bank at PPU $1C00 |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6008, $7FF8, $8008 +---| PPPPPPPP                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Select 16K ROM bank at $8000     |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $6009, $7FF9, $8009 +---| xxxxxxMM                                     |
 +---------------------+   |       ||                                     |
                           |       +|                                     |
                           |        |                                     |
                           |        +--- Mirroring/Page Select            |
                           |              0 - Horizontal mirroring        |
                           |              1 - Vertical mirroring          |
                           |              2 - Mirror pages from $2000     |
                           |              3 - Mirror pages from $2400     |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $600A, $7FFA, $800A +---| xxxxxxxI                                     |
 +---------------------+   |        |                                     |
                           |        |                                     |
                           |        |                                     |
                           |        +--- IRQ Control Register             |
                           |              0 - Disable IRQ's               |
                           |              1 - Enable IRQ's                |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $600B, $7FFB, $800B +---| IIIIIIII                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- Low byte of IRQ counter          |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $600C, $7FFC, $800C +---| IIIIIIII                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- High byte of IRQ counter         |
                           +----------------------------------------------+

 +---------------------+   +----------------------------------------------+
 | $600D, $7FFD, $800D +---| EEEEEEEE                                     |
 +---------------------+   | +------+                                     |
                           |    |                                         |
                           |    |                                         |
                           |    +------- EPROM I/O Port                   |
                           |              I am not sure how this works.   |
                           +----------------------------------------------+
```

---

## Mapper 17: FFE F8xxx

### 這個 Mapper 在做什麼

Several hacked Japanese titles use this mapper, such as the hacked versions of Parodius and DragonBall Z 3.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.
- Implement IRQ state, reload/reset behavior, and timing model from the source notes.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000.

### CHR / VROM / VRAM

- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- The IRQ counter is incremented at each scanline. When it reaches $FFFF, it is reset to zero and an IRQ interrupt is executed.

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- 這個 mapper 沒有額外列出特殊陷阱；優先確保 bank 對映、固定 bank 與 reset 狀態正確。

### 教學建議

- 這類多半出現在特殊或盜版卡帶，文件有時不完整，請接受「先求可跑、再逐步修正」的策略。

### 原始規格參考

```text
 +----------------------+

 +--------------------------------------------------------------------+
 | Several hacked Japanese titles use this mapper, such as the hacked |
 | versions of Parodius and DragonBall Z 3.                           |
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

 +-------+   +-----------------------------------------+
 | $4504 +---| PPPPPPPP                                |
 +-------+   | +------+                                |
             |    |                                    |
             |    |                                    |
             |    +------- Select 8K ROM bank at $8000 |
             +-----------------------------------------+

 +-------+   +-----------------------------------------+
 | $4505 +---| PPPPPPPP                                |
 +-------+   | +------+                                |
             |    |                                    |
             |    |                                    |
             |    +------- Select 8K ROM bank at $A000 |
             +-----------------------------------------+

 +-------+   +-----------------------------------------+
 | $4506 +---| PPPPPPPP                                |
 +-------+   | +------+                                |
             |    |                                    |
             |    |                                    |
             |    +------- Select 8K ROM bank at $C000 |
             +-----------------------------------------+

 +-------+   +-----------------------------------------+
 | $4507 +---| PPPPPPPP                                |
 +-------+   | +------+                                |
             |    |                                    |
             |    |                                    |
             |    +------- Select 8K ROM bank at $E000 |
             +-----------------------------------------+

 +-------+   +----------------------------------------------+
 | $4510 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4511 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4512 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4513 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0C00 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4514 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4515 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4516 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $4517 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1C00 |
             +----------------------------------------------+
```

---

## Mapper 18: Jaleco SS8806

### 這個 Mapper 在做什麼

This mapper is used on several Japanese titles by Jaleco, such as Baseball 3. As far as I know, it was not used on U.S. games.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.
- Implement IRQ state, reload/reset behavior, and timing model from the source notes.
- Keep uncertain behavior behind clear comments or validation hooks because the source flags it as incomplete.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000.

### CHR / VROM / VRAM

- To use the ROM and VROM switching registers, first write the low 4 bits of the intended value into the first register, then the high 4 bits into the second register. For example, to swap 1K VROM bank $B8 to PPU $0400, you'd write $0B into $A003 and $08 to $A002. I think that some cartridges do it the other way around, writing the low nybble first.
- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- The IRQ counter is decremented at each scanline. When it reaches zero, an IRQ interrupt is executed.

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- This information is untested! I do not have any mapper 18 ROM images, unfortunately.

### 教學建議

- 建議先用這個 mapper 建立單元測試：reset 後映射、一次寫入後的映射，以及 mirroring / IRQ 是否改變。

### 原始規格參考

```text
 +--------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles by Jaleco, such as  |
 | Baseball 3. As far as I know, it was not used on U.S. games.       |            |
 +--------------------------------------------------------------------+

 +-------+   +-----------------------------------------+
 | $8000 +---| xxxxPPPP                                |
 +-------+   |     +--+                                |
             |       |                                 |
             |       |                                 |
             |       +---- Select 8K ROM bank at $8000 |
             |              Low 4 bits                 |
             +-----------------------------------------+

 +-------+   +-----------------------------------------+
 | $8001 +---| xxxxPPPP                                |
 +-------+   |     +--+                                |
             |       |                                 |
             |       |                                 |
             |       +---- Select 8K ROM bank at $8000 |
             |              High 4 bits                |
             +-----------------------------------------+

 +-------+   +-----------------------------------------+
 | $8002 +---| xxxxPPPP                                |
 +-------+   |     +--+                                |
             |       |                                 |
             |       |                                 |
             |       +---- Select 8K ROM bank at $A000 |
             |              Low 4 bits                 |
             +-----------------------------------------+

 +-------+   +-----------------------------------------+
 | $8003 +---| xxxxPPPP                                |
 +-------+   |     +--+                                |
             |       |                                 |
             |       |                                 |
             |       +---- Select 8K ROM bank at $A000 |
             |              High 4 bits                |
             +-----------------------------------------+

 +-------+   +-----------------------------------------+
 | $9000 +---| xxxxPPPP                                |
 +-------+   |     +--+                                |
             |       |                                 |
             |       |                                 |
             |       +---- Select 8K ROM bank at $C000 |
             |              Low 4 bits                 |
             +-----------------------------------------+

 +-------+   +-----------------------------------------+
 | $9001 +---| xxxxPPPP                                |
 +-------+   |     +--+                                |
             |       |                                 |
             |       |                                 |
             |       +---- Select 8K ROM bank at $C000 |
             |              High 4 bits                |
             +-----------------------------------------+

 +-------+   +----------------------------------------------+
 | $A000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0000 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0000 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0400 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0400 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0800 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0800 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0C00 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0C00 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1000 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1000 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1400 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1400 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1800 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1800 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1C00 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1C00 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E000 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Low byte of IRQ counter          |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E001 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Low byte of IRQ counter          |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E002 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- High byte of IRQ counter         |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E003 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- High byte of IRQ counter         |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F000 +---| xxxxxxxI                                     |
 +-------+   |        |                                     |
             |        |                                     |
             |        |                                     |
             |        +--- IRQ Control Register 0           |
             |              1 - Enable IRQ's                |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F001 +---| xxxxxxxI                                     |
 +-------+   |        |                                     |
             |        |                                     |
             |        |                                     |
             |        +--- IRQ Control Register 1           |
             |              0 - Disable IRQ's               |
             |              1 - Enable IRQ's                |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F002 +---| xxxxxxPM                                     |
 +-------+   |       ||                                     |
             |       ||                                     |
             |       ||                                     |
             |       |+--- Mirroring Control                |
             |       |      0 - Vertical mirroring          |
             |       |      1 - Horizontal mirroring        |
             |       |                                      |
             |       +---- One-Screen Mirroring             |
             |              0 - Regular mirroring           |
             |              1 - Mirror pages from PPU $2000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F003 +---| EEEEEEEE                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- External I/O Port                |
             |              I am not sure how this works.   |
             +----------------------------------------------+
```

---

## Mapper 19: Namcot 106

### 這個 Mapper 在做什麼

This mapper is used on several Japanese titles by Namcot, such as Splatterhouse and Family Stadium '90. As far as I know, it was not used on U.S. games.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Keep uncertain behavior behind clear comments or validation hooks because the source flags it as incomplete.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.

### CHR / VROM / VRAM

- The LAST 8K of VROM is swapped into PPU $0000 on reset, if it is present.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- Thanks to Mark Knibbs for correcting several misconceptions about this mapper that were included in 0.70.
- The IRQ counter is incremented at each scanline. When it reaches $7FFF, an IRQ interrupt is executed, but there is no reset. This is still preliminary and untested, and I may be wrong on this point. Splatterhouse and several other games run fine without it.
- The Namcot 106 mapper supports one or more additional sound channels. BioNES supports these. I have no clue how they work.

### 教學建議

- 建議先用這個 mapper 建立單元測試：reset 後映射、一次寫入後的映射，以及 mirroring / IRQ 是否改變。

### 原始規格參考

```text
 +-----------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles by Namcot, such as  |
 | Splatterhouse and Family Stadium '90. As far as I know, it was not |
 | used on U.S. games.                                                |
 +--------------------------------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $5000 - $57FF +---| IIIIIIII                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Low byte of IRQ counter          |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $5800 - $5FFF +---| CIIIIIII                                     |
 +---------------+   | |+-----+                                     |
                     | |   |                                        |
                     | |   |                                        |
                     | |   +------- High bits of IRQ counter        |
                     | |                                            |
                     | +----------- IRQ Control Register            |
                     |               0 - Disable IRQ's              |
                     |               1 - Enable IRQ's               |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $8000 - $87FF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $0000 |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $8800 - $8FFF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $0400 |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $9000 - $97FF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $0800 |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $9800 - $9FFF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $0C00 |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $A000 - $A7FF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $1000 |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $A800 - $AFFF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $1400 |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $B000 - $B7FF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $1800 |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $B800 - $BFFF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $1C00 |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $C000 - $C7FF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $2000 |
                     |              A value of $E0 or above will    |
                     |              use VRAM instead                |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $C800 - $CFFF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $2400 |
                     |              A value of $E0 or above will    |
                     |              use VRAM instead                |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $D000 - $D7FF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $2800 |
                     |              A value of $E0 or above will    |
                     |              use VRAM instead                |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $D800 - $DFFF +---| CCCCCCCC                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 1K VROM bank at PPU $2C00 |
                     |              A value of $E0 or above will    |
                     |              use VRAM instead                |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $E000 - $E7FF +---| PPPPPPPP                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 8K ROM bank at $8000      |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $E800 - $EFFF +---| PPPPPPPP                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 8K ROM bank at $A000      |
                     +----------------------------------------------+

 +---------------+   +----------------------------------------------+
 | $F000 - $F7FF +---| PPPPPPPP                                     |
 +---------------+   | +------+                                     |
                     |    |                                         |
                     |    |                                         |
                     |    +------- Select 8K ROM bank at $C000      |
                     +----------------------------------------------+
```

---

## Mapper 21: Konami VRC4

### 這個 Mapper 在做什麼

This mapper is used on several Japanese titles by Konami, such as Wai Wai World 2 and Gradius 2. As far as I know, it was not used on U.S. games.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.
- Implement IRQ state, reload/reset behavior, and timing model from the source notes.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.

### CHR / VROM / VRAM

- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- The IRQ counter is incremented each 113.75 cycles, which is equivalent to one scanline. Unlike a real scanline counter, this "scanline-emulated" counter apparently continues to run during VBlank. When the IRQ counter value reaches $FF, IRQ's will be set off, and the counter is reset.

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- To use the VROM switching registers, first write the low 4 bits of the intended value into the first register, then the high 4 bits into the second register. For example, to swap 1K VROM bank $B8 to PPU $0800, you'd write $0B into $C002 and $08 to $C000. I think that some cartridges do it the other way around, writing the low nybble first. Note that this is actually two different varieties of mapper combined into one. Gradius 2 uses the pairs 0-2 and 1-3. Other games (i.e. Wai Wai World 2) use the pairs 0-2 and 4-6. In the .NES format these two are "shoe-horned" together. fwNES refers to the Gradius 2 style as mapper #25 and the Wai Wai World 2 style as mapper #21. Marat's standard lists both as #21.

### 教學建議

- 這類 mapper 較適合作為進階題目，建議先把內部暫存器、bank 計算與 IRQ 狀態分離實作。

### 原始規格參考

```text
 +------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles by Konami, such as  |
 | Wai Wai World 2 and Gradius 2. As far as I know, it was not used   |
 | on U.S. games.                                                     |
 +--------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $8000      |
             |             or $C000 (based on bit 1 of      |
             |             $9002).                          |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $9000 +---| xxxxxxMM                                     |
 +-------+   |       ||                                     |
             |       +|                                     |
             |        |                                     |
             |        +--- Mirroring/Page Select            |
             |              0 - Vertical mirroring          |
             |              1 - Horizontal mirroring        |
             |              2 - Mirror pages from $2400     |
             |              3 - Mirror pages from $2000     |
             +----------------------------------------------+

 +-------+   +-----------------------------------------------+
 | $9002 +---| xxxxxxPS                                      |
 +-------+   |       ||                                      |
             |       ||                                      |
             |       ||                                      |
             |       |+--- SaveRAM Toggle                    |
             |       |      0 - Disable $6000-$7FFF          |
             |       |      1 - Enable $6000-$7FFF           |
             |       |                                       |
             |       +---- $8000 Switching Mode              |
             |              0 - Switch $8000-$9FFF via $8000 |
             |              1 - Switch $C000-$DFFF via $8000 |
             +-----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $9003 +---| EEEEEEEE                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- External I/O Port                |
             |              I am not sure how this works.   |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $A000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0000 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0000 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0400 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0400 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B004 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0400 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B006 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0400 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0800 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0800 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0C00 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0C00 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C004 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0C00 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C006 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0C00 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1000 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1000 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1400 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1400 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D004 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1400 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D006 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1400 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1800 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1800 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1C00 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1C00 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E004 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1C00 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E006 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1C00 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F000 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Counter Register             |
             |              The IRQ countdown value is      |
             |              stored here.                    |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F001 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Counter Register             |
             |              The IRQ countdown value is      |
             |              stored here. (Apparently is     |
             |              the same register as $F000.)    |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F002 +---| xxxxxxII                                     |
 +-------+   |       ||                                     |
             |       +|                                     |
             |        |                                     |
             |        +--- IRQ Control Register 0           |
             |              0 - Disable IRQ's               |
             |              2 - Enable IRQ's                |
             |              3 - Enable IRQ's                |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F003 +---| xxxxxxxx                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Control Register 1           |
             |              Any value written here will     |
             |              reset the IRQ counter to zero.  |
             +----------------------------------------------+
```

---

## Mapper 22: Konami VRC2 type A

### 這個 Mapper 在做什麼

This mapper is used on the Japanese title TwinBee 3 by Konami.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 16K of ROM is permanently "hard-wired" and cannot be swapped.

### CHR / VROM / VRAM

- On reset, the first 8K of VROM is swapped into PPU $0000.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- 這個 mapper 沒有額外列出特殊陷阱；優先確保 bank 對映、固定 bank 與 reset 狀態正確。

### 教學建議

- 建議先用這個 mapper 建立單元測試：reset 後映射、一次寫入後的映射，以及 mirroring / IRQ 是否改變。

### 原始規格參考

```text
 +-------------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on the Japanese title TwinBee 3 by Konami.     |
 +--------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $8000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $9000 +---| xxxxxxMM                                     |
 +-------+   |       ||                                     |
             |       +|                                     |
             |        |                                     |
             |        +--- Mirroring/Page Select            |
             |              0 - Vertical mirroring          |
             |              1 - Horizontal mirroring        |
             |              2 - Mirror pages from $2400     |
             |              3 - Mirror pages from $2000     |
             | NOTE: I don't have any confidence in the     |
             |       accuracy of this information.          |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $A000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0000 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0400 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0800 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0C00 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1000 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1400 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1800 |
             |              Shift this value right one bit  |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1C00 |
             |              Shift this value right one bit  |
             +----------------------------------------------+
```

---

## Mapper 23: Konami VRC2 type B

### 這個 Mapper 在做什麼

This mapper is used on several Japanese titles by Konami, such as Contra Japanese and Getsufuu Maden. As far as I know, it was not used on U.S. games.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.

### CHR / VROM / VRAM

- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.
- To use the VROM switching registers, first write the low 4 bits of the intended value into the first register, then the high 4 bits into the second register. For example, to swap 1K VROM bank $B8 to PPU $0800, you'd write $0B into $C001 and $08 to $C000. I think that some cartridges do it the other way around, writing the low nybble first.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- 這個 mapper 沒有額外列出特殊陷阱；優先確保 bank 對映、固定 bank 與 reset 狀態正確。

### 教學建議

- 建議先用這個 mapper 建立單元測試：reset 後映射、一次寫入後的映射，以及 mirroring / IRQ 是否改變。

### 原始規格參考

```text
 +-------------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles by Konami, such as  |
 | Contra Japanese and Getsufuu Maden. As far as I know, it was not   |
 | used on U.S. games.                                                |
 +--------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $8000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $9000 +---| xxxxxxMM                                     |
 +-------+   |       ||                                     |
             |       +|                                     |
             |        |                                     |
             |        +--- Mirroring/Page Select            |
             |              0 - Vertical mirroring          |
             |              1 - Horizontal mirroring        |
             |              2 - Mirror pages from $2400     |
             |              3 - Mirror pages from $2000     |
             | NOTE: I don't have any confidence in the     |
             |       accuracy of this information.          |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $A000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0000 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0000 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0400 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0400 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0800 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0800 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0C00 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $0C00 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1000 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1000 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1400 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1400 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E000 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1800 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E001 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1800 |
             |              High 4 bits                     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E002 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1C00 |
             |              Low 4 bits                      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E003 +---| xxxxCCCC                                     |
 +-------+   |     +--+                                     |
             |       |                                      |
             |       |                                      |
             |       +---- Select 1K VROM bank at PPU $1C00 |
             |              High 4 bits                     |
             +----------------------------------------------+
```

---

## Mapper 24: Konami VRC6

### 這個 Mapper 在做什麼

This mapper is used on several Japanese titles by Konami, such as Akumajo Dracula [Castlevania] 3. As far as I know, it was not used on U.S. games.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.
- Implement IRQ state, reload/reset behavior, and timing model from the source notes.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.

### CHR / VROM / VRAM

- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- The IRQ counter is incremented each 113.75 cycles, which is equivalent to one scanline. Unlike a real scanline counter, this "scanline-emulated" counter apparently continues to run during VBlank. When the IRQ counter value reaches $FF, IRQ's will be set off, and the counter is reset.

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- There are more registers which I don't understand the usage of and which are not detailed here. There's also a custom sound chip, the operation of which is unknown to me. As always, any extra information is welcome.

### 教學建議

- 這類 mapper 較適合作為進階題目，建議先把內部暫存器、bank 計算與 IRQ 狀態分離實作。

### 原始規格參考

```text
 +------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles by Konami, such as  |
 | Akumajo Dracula [Castlevania] 3. As far as I know, it was not used |
 | on U.S. games.                                                     |
 +--------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 16K ROM bank at $8000     |
             +----------------------------------------------+

 +-------+   +--------------------------------------------+
 | $B003 +---| xxUxMMxx                                   |
 +-------+   |   | ||                                     |
             |   | +|                                     |
             |   |  |                                     |
             |   |  +--- Mirroring/Page Select            |
             |   |        0 - Horizontal mirroring        |
             |   |        1 - Vertical mirroring          |
             |   |        2 - Mirror pages from $2000     |
             |   |        3 - Mirror pages from $2400     |
             |   |                                        |
             |   +------ Unknown, but usually set to 1    |
             +--------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $C000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D002 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $D003 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0C00 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E002 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E003 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1C00 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F000 +---| IIIIIIII                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Counter Register             |
             |              The IRQ countdown value is      |
             |              stored here.                    |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F001 +---| xxxxxxxI                                     |
 +-------+   |        |                                     |
             |        |                                     |
             |        |                                     |
             |        +--- IRQ Control Register 0           |
             |              0 - Disable IRQ's               |
             |              1 - Enable IRQ's                |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $F002 +---| xxxxxxxx                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- IRQ Control Register 1           |
             |              Any value written here will     |
             |              reset the IRQ counter to zero.  |
             +----------------------------------------------+
```

---

## Mapper 32: Irem G-101

### 這個 Mapper 在做什麼

This mapper is used on several Japanese titles by Irem, such as ImageFight 2. As far as I know, it was not used on U.S. games.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.

### CHR / VROM / VRAM

- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- 這個 mapper 沒有額外列出特殊陷阱；優先確保 bank 對映、固定 bank 與 reset 狀態正確。

### 教學建議

- 建議先用這個 mapper 建立單元測試：reset 後映射、一次寫入後的映射，以及 mirroring / IRQ 是否改變。

### 原始規格參考

```text
 +-----------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles by Irem, such as    |
 | ImageFight 2. As far as I know, it was not used on U.S. games.     |                                           |
 +--------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8FFF +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $8000      |
             |             or $C000 (based on bit 1 of      |
             |             $9FFF).                          |
             +----------------------------------------------+

 +-------+   +-----------------------------------------------+
 | $9FFF +---| xxxxxxPS                                      |
 +-------+   |       ||                                      |
             |       ||                                      |
             |       ||                                      |
             |       |+--- Mirroring Switch                  |
             |       |      0 - Horizontal mirroring         |
             |       |      1 - Vertical mirroring           |
             |       |                                       |
             |       +---- $8FFF Switching Mode              |
             |              0 - Switch $8000-$9FFF via $8FFF |
             |              1 - Switch $C000-$DFFF via $8FFF |
             +-----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $AFFF +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $A000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF0 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF1 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF2 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF3 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0C00 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF4 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF5 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF6 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $BFF7 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1C00 |
             +----------------------------------------------+
```

---

## Mapper 33: Taito TC0190

### 這個 Mapper 在做什麼

This mapper is used on several Japanese titles by Taito, such as Pon Poko Pon. As far as I know, it was not used on U.S. games.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 16K of ROM is permanently "hard-wired" and cannot be swapped.

### CHR / VROM / VRAM

- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- 這個 mapper 沒有額外列出特殊陷阱；優先確保 bank 對映、固定 bank 與 reset 狀態正確。

### 教學建議

- 建議先用這個 mapper 建立單元測試：reset 後映射、一次寫入後的映射，以及 mirroring / IRQ 是否改變。

### 原始規格參考

```text
 +-------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles by Taito, such as   |
 | Pon Poko Pon. As far as I know, it was not used on U.S. games.     |                                           |
 +--------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $8000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8001 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $A000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8002 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $0000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $8003 +---| CCCCCCCC                                     |
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
             |    +------- Select 1K VROM bank at PPU $1000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A002 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A003 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1C00 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C000 +---| UUUUUUUU                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Unknown                          |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C001 +---| UUUUUUUU                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Unknown                          |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $E000 +---| RRRRRRRR                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Reserved                         |
             +----------------------------------------------+
```

---

## Mapper 34: Nina-1

### 這個 Mapper 在做什麼

These two mappers were used on two U.S. games: Deadly Towers and Impossible Mission II.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- The first 32K ROM bank is swapped into $8000 when the cart is started or reset.

### CHR / VROM / VRAM

- Carts without VROM (i.e. Deadly Towers) will have 8K of VRAM at PPU $0000. Carts with VROM (Impossible Mission 2) have the first 8K swapped in at reset. Apparently, this mapper is actually a combination of two actual separate mappers. Deadly Towers uses only the $8000-$FFFF switching, and Impossible Mission 2 uses only the three lower registers.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- This mapper is fairly easy to implement in a NES emulator.

### 教學建議

- 建議先用這個 mapper 建立單元測試：reset 後映射、一次寫入後的映射，以及 mirroring / IRQ 是否改變。

### 原始規格參考

```text
 +-------------------+

 +--------------------------------------------------------------------+
 | These two mappers were used on two U.S. games: Deadly Towers and   |
 | Impossible Mission ][.                                             |
 +--------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $7FFD +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 32K ROM bank at $8000     |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $7FFE +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 4K VROM bank at PPU $0000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $7FFF +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 4K VROM bank at PPU $1000 |
             +----------------------------------------------+

 +---------------+         +------------------------------------------+
 | $8000 - $FFFF +---------| PPPPPPPP                                 |
 +---------------+         | +------+                                 |
                           |    |                                     |
                           |    |                                     |
                           |    +------- Select 32K ROM bank at $8000 |
                           +------------------------------------------+
```

---

## Mapper 64: Tengen RAMBO-1

### 這個 Mapper 在做什麼

This mapper is used on several U.S. unlicensed titles by Tengen. They include Shinobi, Klax, and Skull & Crossbones. Thanks to D for hacking this mapper.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- Two of the 8K ROM banks in the PRG area are switchable. The last page is "hard-wired" to the last 8K bank in the cart.
- A cart will first write the command and base select number to $8000, then the value to be used to $8001.

### CHR / VROM / VRAM

- On carts with VROM, the first 8K of VROM is swapped into PPU $0000 on reset. On carts without VROM, as always, there is 8K of VRAM at PPU $0000.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- 這個 mapper 沒有額外列出特殊陷阱；優先確保 bank 對映、固定 bank 與 reset 狀態正確。

### 教學建議

- 這類 mapper 較適合作為進階題目，建議先把內部暫存器、bank 計算與 IRQ 狀態分離實作。

### 原始規格參考

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

---

## Mapper 65: Irem H-3001

### 這個 Mapper 在做什麼

This mapper is used on several Japanese titles by Irem, such as Daiku no Gensan 2. As far as I know, it was not used on U.S. games.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.
- Implement nametable mirroring control exactly as documented.
- Implement IRQ state, reload/reset behavior, and timing model from the source notes.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 8K of ROM is permanently "hard-wired" and cannot be swapped.

### CHR / VROM / VRAM

- VROM should NOT be swapped into PPU $0000 when the cartridge is started or reset, in order to avoid graphics corruption.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- Does anyone have info on mirroring or IRQ's for this mapper?

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- 這個 mapper 沒有額外列出特殊陷阱；優先確保 bank 對映、固定 bank 與 reset 狀態正確。

### 教學建議

- 建議先用這個 mapper 建立單元測試：reset 後映射、一次寫入後的映射，以及 mirroring / IRQ 是否改變。

### 原始規格參考

```text
 +------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles by Irem, such as    |
 | Daiku no Gensan 2. As far as I know, it was not used on U.S. games.|                                           |
 +--------------------------------------------------------------------+

 +-------+   +-----------------------------------------+
 | $8000 +---| PPPPPPPP                                |
 +-------+   | +------+                                |
             |    |                                    |
             |    |                                    |
             |    +------- Select 8K ROM bank at $8000 |
             +-----------------------------------------+

 +-------+   +--------------------------------------------+
 | $9003 +---| MMMMMMMM                                   |
 +-------+   | +------+                                   |
             |    |                                       |
             |    |                                       |
             |    +------- Mirroring                      |
             |              I am not sure how this works. |
             +--------------------------------------------+

 +-------+   +--------------------------------------------+
 | $9005 +---| IIIIIIII                                   |
 +-------+   | +------+                                   |
             |    |                                       |
             |    |                                       |
             |    +------- IRQ Control                    |
             |              I am not sure how this works. |
             +--------------------------------------------+

 +-------+   +--------------------------------------------+
 | $9006 +---| IIIIIIII                                   |
 +-------+   | +------+                                   |
             |    |                                       |
             |    |                                       |
             |    +------- IRQ Control                    |
             |              I am not sure how this works. |
             +--------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $A000      |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B002 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B003 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $0C00 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B004 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B005 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1400 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B006 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $B007 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 1K VROM bank at PPU $1C00 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $C000 +---| PPPPPPPP                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 8K ROM bank at $C000      |
             +----------------------------------------------+
```

---

## Mapper 66: GNROM

### 這個 Mapper 在做什麼

This mapper is used on several Japanese titles, such as DragonBall, and on U.S. titles such as Gumshoe and Dragon Power.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- 原始文件沒有額外的 PRG 備註，但寄存器圖仍然是主要依據。

### CHR / VROM / VRAM

- When the cart is first started or reset, the first 32K ROM bank in the cart is loaded into $8000, and the first 8K VROM bank is swapped into PPU $0000.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- This mapper is used on the DragonBall (NOT DragonBallZ) NES game. Contrary to popular belief, this mapper is NOT mapper 16!

### 教學建議

- 這類 mapper 結構相對直接，很適合拿來建立你的第一版 bank switching 架構。

### 原始規格參考

```text
 +------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles, such as            |
 | DragonBall, and on U.S. titles such as Gumshoe and Dragon Power.   |                                           |
 +--------------------------------------------------------------------+

 +---------------+    +-----------------------------------------------+
 | $8000 - $FFFF +----| xxPPxxCC                                      |
 +---------------+    |   ||  ||                                      |
                      |   +|  +----- Select 8K VROM bank at PPU $0000 |
                      |    |                                          |
                      |    +-------- Select 32K ROM bank at $8000     |
                      +-----------------------------------------------+
```

---

## Mapper 68: Sunsoft Mapper #4

### 這個 Mapper 在做什麼

This mapper is used on the Japanese title AfterBurner II by Sunsoft.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Implement nametable mirroring control exactly as documented.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. The last 16K of ROM is permanently "hard-wired" and cannot be swapped.

### CHR / VROM / VRAM

- 原始文件沒有額外的 CHR 備註，請依寄存器圖中的 PPU bank 切換規則實作。

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- 這個 mapper 沒有額外列出特殊陷阱；優先確保 bank 對映、固定 bank 與 reset 狀態正確。

### 教學建議

- 建議先用這個 mapper 建立單元測試：reset 後映射、一次寫入後的映射，以及 mirroring / IRQ 是否改變。

### 原始規格參考

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

---

## Mapper 69: Sunsoft FME-7

### 這個 Mapper 在做什麼

This mapper is used on several Japanese titles, such as Batman Japanese, and on the U.S. title Batman: Return of the Joker. Thanks to D for hacking this mapper.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Implement nametable mirroring control exactly as documented.
- Handle WRAM/SaveRAM/expansion I/O behavior for the documented address ranges.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- The last 8K ROM page is permanently "hard-wired" to the last 8K ROM page in the cart.

### CHR / VROM / VRAM

- 原始文件沒有額外的 CHR 備註，請依寄存器圖中的 PPU bank 切換規則實作。

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- Command #8 works in the following manner. The upper 2 bits select what is swapped into $6000-$7FFF. If bit 6 is 0, it will be ROM, selected from the other bits of the register. If it's 1, then the contents depend on bit 7. In this case, if bit 7 is 1, it will be WRAM. If it's 0, it will be pseudo-random numbers (this still hasn't been figured out).

### 實作陷阱

- This mapper is deployed in a manner similar to that of MMC3. First a register number is written to $8000 and then the register chosen can be accessed via $A000.

### 教學建議

- 這類 mapper 較適合作為進階題目，建議先把內部暫存器、bank 計算與 IRQ 狀態分離實作。

### 原始規格參考

```text
 +--------------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on several Japanese titles, such as Batman     |
 | Japanese, and on the U.S. title Batman: Return of the Joker.       |
 | Thanks to D for hacking this mapper.                               |
 +--------------------------------------------------------------------+

 +-------+   +---------------------------------------------------+
 | $8000 +---| xxxxRRRR                                          |
 +-------+   |     +--+                                          |
             |       +--- Register Number                        |
             |             0 - Select 1K VROM page at PPU $0000  |
             |             1 - Select 1K VROM page at PPU $0400  |
             |             2 - Select 1K VROM page at PPU $0800  |
             |             3 - Select 1K VROM page at PPU $0C00  |
             |             4 - Select 1K VROM page at PPU $1000  |
             |             5 - Select 1K VROM page at PPU $1400  |
             |             6 - Select 1K VROM page at PPU $1800  |
             |             7 - Select 1K VROM page at PPU $1C00  |
             |             8 - Select 8K ROM page at $6000       |
             |             9 - Select 8K ROM page at $8000       |
             |            10 - Select 8K ROM page at $A000       |
             |            11 - Select 8K ROM page at $C000       |
             |            12 - Select mirroring                  |
             |            13 - IRQ control                       |
             |            14 - Low byte of scanline counter      |
             |            15 - High byte of scanline counter     |
             |                                                   |
             | NOTE: I am not sure if the information for        |
             |        registers 8, 12, 13, 14, and 15 is correct.|
             |                                                   |
             +---------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $A000 +---| VVVVVVVV                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Register Write                   |
             |              Activates the command number    |
             |              written to bits 0-3 of $8000    |
             +----------------------------------------------+
```

---

## Mapper 71: Camerica

### 這個 Mapper 在做什麼

This mapper is used on Camerica's unlicensed NES carts, including Firehawk and Linus Spacehead.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Update CHR/VROM/VRAM banking exactly as documented, including bank size and reset behavior.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. This last 16K bank is permanently "hard-wired" to $C000, and it cannot be swapped, as far as is known.

### CHR / VROM / VRAM

- This mapper has no provisions for VROM; therefore, all carts using it have 8K of VRAM at PPU $0000.

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- Many ROMs from these games are incorrectly defined as mapper #2. Marat has still not assigned an "official" .NES mapper number for this mapper.

### 教學建議

- 這類 mapper 結構相對直接，很適合拿來建立你的第一版 bank switching 架構。

### 原始規格參考

```text
 +---------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on Camerica's unlicensed NES carts, including  |
 | Firehawk and Linus Spacehead.                                      |
 +--------------------------------------------------------------------+

 +---------------+         +------------------------------------------+
 | $8000 - $BFFF +---------| UUUUUUUU                                 |
 +---------------+         | +------+                                 |
                           |    |                                     |
                           |    |                                     |
                           |    +------- Unknown                      |
                           +------------------------------------------+

 +---------------+         +------------------------------------------+
 | $C000 - $FFFF +---------| PPPPPPPP                                 |
 +---------------+         | +------+                                 |
                           |    |                                     |
                           |    |                                     |
                           |    +------- Select 16K ROM bank at $8000 |
                           +------------------------------------------+
```

---

## Mapper 78: Irem 74HC161/32

### 這個 Mapper 在做什麼

Several Japanese Irem titles use this mapper.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Keep uncertain behavior behind clear comments or validation hooks because the source flags it as incomplete.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the first 16K ROM bank in the cart is loaded into $8000, and the LAST 16K ROM bank is loaded into $C000. This 16K bank is permanently "hard-wired" to $C000, and it cannot be swapped.

### CHR / VROM / VRAM

- 原始文件沒有額外的 CHR 備註，請依寄存器圖中的 PPU bank 切換規則實作。

### Mirroring

- 原始文件沒有額外備註時，請以寄存器圖中 mirroring 位元為準。

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- 原始文件沒有額外 WRAM / SaveRAM / I/O 備註。

### 實作陷阱

- The first 8K VROM bank may or may not be swapped into $0000 when the cart is reset. I have no ROM images to test.

### 教學建議

- 這類 mapper 結構相對直接，很適合拿來建立你的第一版 bank switching 架構。

### 原始規格參考

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

---

## Mapper 91: HK-SF3

### 這個 Mapper 在做什麼

This mapper is used on the pirate cart with a title screen reading "Street Fighter 3". It may or may not have been used in other bootleg games. Thanks to Mark Knibbs for information regarding this mapper.

### 實作時最重要的事

- Implement CPU write decoding for every documented address/register range.
- Update PRG banking exactly as documented, including fixed banks and special bank-size modes.
- Implement nametable mirroring control exactly as documented.
- Handle WRAM/SaveRAM/expansion I/O behavior for the documented address ranges.
- Keep uncertain behavior behind clear comments or validation hooks because the source flags it as incomplete.

### CPU 寫入介面

這個 mapper 的完整寫入介面請直接看本節後面的「原始規格參考」。如果你正在寫程式，建議先把對應位址範圍的 `cpu_write()` 分派完成，再逐一補上 bank / mirroring / IRQ 的副作用。

### PRG Banking

- When the cart is first started, the LAST 16K ROM bank in the cart is loaded into both $8000 and $C000. The 16K at $C000 is permanently "hard-wired" to $C000 and cannot be swapped.

### CHR / VROM / VRAM

- 原始文件沒有額外的 CHR 備註，請依寄存器圖中的 PPU bank 切換規則實作。

### Mirroring

- Vertical mirroring is always active.

### IRQ

- 這個 mapper 在來源文件中沒有額外 IRQ 備註，若寄存器圖也沒有 IRQ 欄位，通常代表不需要 IRQ。

### WRAM / SaveRAM / 擴充 I/O

- Some of the registers can be accessed from other addresses than those listed above. For example, $7000 can also be accessed from $7002, $7004, and so on through $7FFA. $7001 can be accessed at $7003, $7005, and so on through $7FFB. Similar rules apparently are in force for the registers at $6000-$6FFF.

### 實作陷阱

- This mapper supports IRQ interrupts. I have no clue how.

### 教學建議

- 這類多半出現在特殊或盜版卡帶，文件有時不完整，請接受「先求可跑、再逐步修正」的策略。

### 原始規格參考

```text
 +-------------------+

 +--------------------------------------------------------------------+
 | This mapper is used on the pirate cart with a title screen reading |
 | "Street Fighter 3". It may or may not have been used in other      |
 | bootleg games. Thanks to Mark Knibbs for information regarding     |
 | this mapper.                                                       |
 +--------------------------------------------------------------------+

 +-------+   +----------------------------------------------+
 | $6000 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $0000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $6001 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $0800 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $6002 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $1000 |
             +----------------------------------------------+

 +-------+   +----------------------------------------------+
 | $6003 +---| CCCCCCCC                                     |
 +-------+   | +------+                                     |
             |    |                                         |
             |    |                                         |
             |    +------- Select 2K VROM bank at PPU $1800 |
             +----------------------------------------------+

 +-------+   +-----------------------------------------+
 | $7000 +---| PPPPPPPP                                |
 +-------+   | +------+                                |
             |    |                                    |
             |    |                                    |
             |    +------- Select 8K ROM bank at $8000 |
             +-----------------------------------------+

 +-------+   +-----------------------------------------+
 | $7001 +---| PPPPPPPP                                |
 +-------+   | +------+                                |
             |    |                                    |
             |    |                                    |
             |    +------- Select 8K ROM bank at $A000 |
             +-----------------------------------------+
```

---
