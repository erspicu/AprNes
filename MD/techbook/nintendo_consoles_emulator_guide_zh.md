# 任天堂主機模擬器開發導論

> 從 1983 年的紅白機到 2017 年的 Switch（至今仍在販售），任天堂在 40 多年間推出 12 款主流家用與掌機。本文按**發售日期順序**逐一介紹各主機的硬體架構、設計哲學，以及如果有人想為這台機器寫模擬器，會碰到哪些核心難點與技術挑戰。每台主機附上代表性的開源模擬器專案作為延伸閱讀。

---

## 目錄

1. [前言：為什麼任天堂主機這麼適合學模擬器？](#前言為什麼任天堂主機這麼適合學模擬器)
2. [NES / Famicom — 1983.07.15](#nes--famicom--19830715)
3. [Game Boy — 1989.04.21](#game-boy--19890421)
4. [SNES / Super Famicom — 1990.11.21](#snes--super-famicom--19901121)
5. [Nintendo 64 — 1996.06.23](#nintendo-64--19960623)
6. [Game Boy Color — 1998.10.21](#game-boy-color--19981021)
7. [Game Boy Advance — 2001.03.21](#game-boy-advance--20010321)
8. [GameCube — 2001.09.14](#gamecube--20010914)
9. [Nintendo DS — 2004.11.21](#nintendo-ds--20041121)
10. [Wii — 2006.12.02](#wii--20061202)
11. [Nintendo 3DS — 2011.02.26](#nintendo-3ds--20110226)
12. [Wii U — 2012.12.08](#wii-u--20121208)
13. [Nintendo Switch — 2017.03.03](#nintendo-switch--20170303)
14. [整體難度排名與選擇建議](#整體難度排名與選擇建議)
15. [跨主機共通的開發主題](#跨主機共通的開發主題)

---

## 前言：為什麼任天堂主機這麼適合學模擬器？

任天堂從 1983 年第一台 Famicom 至今，推出的主機**精準涵蓋了整個半導體與電腦圖形學的發展史**：

- 早期（NES、GB、SNES）展示了 8/16 位元時代「自訂晶片組」的設計思維
- 中期（N64、NGC、Wii）跨入 RISC 多處理器與 3D 加速時代
- 近代（NDS、3DS、Wii U、Switch）進入 ARM 主流、現代 GPU、作業系統 HLE 的範式

**對於想自己動手寫模擬器的人**，從 NES 開始一路往上做，相當於走過了一遍計算機架構演化史的縮影。相較於 Sony / Sega 等廠的硬體，任天堂主機有兩個特色讓它特別適合學習：

1. **硬體文件公開度高**：NESdev、GBDev、Pret 等社群維護了極詳細的硬體規格、test ROM、bug 行為紀錄。
2. **測試工具齊全**：blargg test ROMs、Mooneye GB、AccuracyCoin、CGB-Acid2 等開源驗證套件，讓「我的模擬器寫得對不對」成為可量化問題。

接下來按照發售順序介紹每台主機，重點放在**硬體架構特色**跟**模擬實作上的核心難點**。

---

## NES / Famicom — 1983.07.15

> Family Computer（俗稱「紅白機」），北美地區 1985.10.18 以 Nintendo Entertainment System (NES) 名義上市。

### 硬體架構

- **CPU**：Ricoh 2A03（基於 6502 核心，去除 BCD 模式，內建 APU），1.79 MHz
- **PPU**：Ricoh 2C02 圖像處理器
- **記憶體**：CPU 2 KB RAM、PPU 2 KB VRAM、64 byte OAM
- **音訊**：5 通道 APU（兩路方波、一路三角波、一路雜訊、一路 DMC 採樣）
- **圖像**：256×240 解析度、25 色同時顯示（從 64 色調色盤中選）

### 模擬實作的核心難點

NES 看似是「寫個 6502 解碼器」就完事，但要做到 cycle-accurate 精度，每一塊都有暗坑：

**1. PPU 的極致時序**

PPU 跟 CPU 同步運作（每個 CPU cycle 對應 3 個 PPU dot），且各種狀態存取（OAM、VRAM、暫存器 `$2000-$2007`）必須精確發生在對的 dot 上。錯一個 cycle 就會出現畫面抖動、scrolling 偏移、sprite-0-hit 失準。

**2. Loopy's Scrolling 內部狀態機**

`$2005`（PPUSCROLL）跟 `$2006`（PPUADDR）共享 PPU 內部 16-bit 暫存器 `v` 跟 `t`，加上 `w`（write toggle）。要正確還原「先寫一次 `$2006`，再讀 `$2002` 重置 toggle，再寫第二次 `$2006`」這類序列，必須完整實作 PPU 內部的 latch state。經典神文章 [The skinny on NES scrolling](https://www.nesdev.org/wiki/PPU_scrolling) 是必讀。

**3. Mapper 碎片化（The Mapper Hell）**

NES 卡匣有 256 種以上不同的 mapper（記憶體控制器），從最簡單的 NROM (#0) 到帶有 IRQ counter 的 MMC3 (#4)，再到內建擴充音效的 MMC5 (#5)、VRC6、N163、FME-7。實作一個能跑 NROM/UxROM 的 emulator 只要幾天，但要支援 90% 商業遊戲得實作數十個 mapper。**MMC3 的 A12 rising-edge IRQ** 跟 **MMC5 的 split-screen + ExGrafix 模式**是兩個著名的「畢業考」級難關。

**4. APU 的非線性混音 + DMC DMA cycle stealing**

5 個音訊通道的最終輸出**不是線性相加**，而是兩條非線性查表（[NESdev mixer formula](https://www.nesdev.org/wiki/APU_Mixer)）。DMC 通道讀採樣資料時還會偷走 CPU 3-4 個 cycle（dummy reads），如果這部分模擬不準，許多遊戲會發生明顯的音訊跑掉或時序錯位。

**5. 6502 非法指令 + JMP 邊界 bug**

許多老遊戲依賴 `LAX`、`SAX`、`DCP` 等未公開指令。`JMP ($xxFF)` 還有著名的「page boundary bug」（不會跨頁讀取 high byte）。要 100% 相容必須完整實作這些怪癖。

### 代表性開源模擬器

- **Mesen2** — 跨平台多系統模擬器，cycle-accurate，是 NES 模擬器精度的金標準之一
- **fceux** — 老牌 NES emulator + debugger
- **Nestopia UE** — 高精度 NES 模擬器
- **TriCNES** — 來自 AccuracyCoin 作者，per-master-clock timing model，是學「電路級 NES 行為」的最佳參考

### 難度評級：★★ 初階 → ★★★★（精度滿分）

入門做能跑遊戲的版本門檻低，但要過 184/184 blargg + 138/138 AccuracyCoin 的雙滿分難度跳一個量級。

---

## Game Boy — 1989.04.21

> 攜帶式遊戲機的奠基之作。北美 1989.07.31 上市。

### 硬體架構

- **CPU**：Sharp LR35902 — 介於 Intel 8080 跟 Z80 之間的 8-bit 處理器，4.19 MHz
- **記憶體**：8 KB WRAM、8 KB VRAM、$A0 byte OAM
- **音訊**：4 通道 APU（兩路方波、一路 wavetable、一路雜訊）
- **圖像**：160×144 解析度、4 階灰階

### 模擬實作的核心難點

GB 常被視為「模擬器入門磚」—— 能跑遊戲的版本一週可以做出來。但要做到 cycle-accurate，難度藏在細節裡：

**1. LR35902 不是真正的 Z80**

它去掉了 Z80 的部分指令（IX/IY 索引、shadow registers）但加了 8080 的特性（`LD (HL),n`、`LD A,(BC)` 等），還新增了 GB 專用指令（`SWAP`、`STOP`、`HALT`）。當作 Z80 模擬會錯，當作 8080 也會錯，只能當作獨立的指令集學。

**2. Halt Bug**

如果 `HALT` 指令執行時 IME（中斷主開關）為 0 但有待處理中斷，下一條指令會被**重複讀取一次**（PC 不前進）。這是真機硬體 bug，但《Megaman V》等遊戲意外依賴這行為。要 100% 相容必須模擬出來。

**3. PPU 的 STAT 模式切換**

PPU 在 Mode 0（H-Blank）、Mode 1（V-Blank）、Mode 2（OAM Search）、Mode 3（Pixel Transfer）之間循環。許多遊戲（如《Prehistorik Man》）讀 STAT 暫存器來達成「在掃描線中途修改 LCDC」這類超越硬體的特效。模式切換時間錯一個 cycle 就會破圖。

**4. MBC（Memory Bank Controller）變體**

MBC1（最基本）、MBC2（內建 4-bit RAM）、MBC3（含 RTC，《寶可夢金/銀》就靠它）、MBC5（最大、含震動）、MBC7（陀螺儀，《Kirby Tilt 'n' Tumble》）。MBC3 的 RTC 模擬還要把現代系統時間轉成 Game Boy 內部的計時器格式。

**5. APU 的 DAC 行為**

每個聲道有獨立的 DAC（Digital-to-Analog Converter）開關。某些遊戲透過快速開關 DAC 來產生 PCM 音訊（《Pokémon Yellow》皮卡丘語音就是這樣做出來的）。要還原這效果必須處理 DAC 邊緣的 click noise。

### 代表性開源模擬器

- **SameBoy** — 全世界最精確的 GB / GBC 模擬器之一，過了所有 Mooneye 跟 Acid2 測試
- **mGBA** — 跨平台、含 GB/GBC/GBA 支援
- **BGB** — 老牌精度典範（Windows-only）

### 難度評級：★ 入門 → ★★★（精度滿分）

寫一個能跑《Tetris》的版本一週搞定；要過 Mooneye GB acceptance + CGB-Acid2 全套，得花幾個月。

---

## SNES / Super Famicom — 1990.11.21

> 16-bit 王者。北美 1991.08.23 以 Super Nintendo Entertainment System 上市。

### 硬體架構

- **CPU**：Ricoh 5A22（基於 WDC 65C816），可動態切換 8/16-bit 累積器與索引暫存器
- **時脈**：CPU 3.58 MHz（FastROM）/ 2.68 MHz（SlowROM），動態切換
- **PPU**：兩顆 PPU 晶片組（PPU1 / PPU2），8 種背景模式
- **音訊**：Sony SPC700 獨立音效處理器，自有 64 KB SRAM
- **圖像**：256×224 ~ 512×448、最多 256 色（Mode 7 模式可全螢幕旋轉縮放）

### 模擬實作的核心難點

SNES 在模擬器圈被公認為**最難寫得精確的 8/16-bit 主機**：

**1. 65C816 的動態暫存器寬度**

`P` 暫存器中的 `M`（accumulator size）跟 `X`（index size）位元決定當前 A、X、Y 是 8-bit 還是 16-bit。**同一個 opcode 在不同模式下指令長度跟行為都不一樣**，這讓靜態反組譯極度困難 —— 你不知道下一個 byte 是運算元還是新的 opcode，直到追完所有 `REP`/`SEP` 的執行歷史。

**2. FastROM / SlowROM 動態時脈**

CPU 存取不同記憶體區域的速度不一樣（6 / 8 / 12 master cycles）。某些 mapper 還允許 ROM 跑 FastROM 模式（3.58 MHz）。指令週期計算極瑣碎，每個 read/write 都得查當前位址的 wait state。

**3. SPC700 獨立王國**

聲音子系統（SPC700 + DSP + 64 KB RAM）是**完全獨立的電腦**，跟主 CPU 透過 4 個 I/O 暫存器通訊。兩邊時序同步如果差了幾百 cycle，遊戲會破音、卡死、甚至無法啟動。SPC700 自己還有獨立的 test ROM 套件。

**4. PPU 的 Mode 7 + 視窗 + 半透明混色**

Mode 7 是 SNES 的招牌：背景層支援即時矩陣運算（旋轉、縮放、透視）。實作時要處理 fixed-point 數學跟 HDMA 配合的「mode 7 with H-DMA」（《F-Zero》、《Super Mario Kart》的賽道效果就是這樣畫的）。半透明混色（Color Math，Add/Subtract）跟硬體窗口（Window Mask）每個 pixel 要做一連串布林邏輯運算。

**5. 擴充協處理器（Enhancement Chips）**

寫一個「核心」SNES 模擬器只能跑 70% 遊戲，剩下 30% 要每顆協處理器個別實作：
- **DSP-1**（《Mario Kart》《Pilotwings》）— 16-bit 數學協處理器
- **Super FX**（《Star Fox》《Yoshi's Island》）— RISC 處理器跑 3D 多邊形
- **SA-1**（《Super Mario RPG》《Kirby Super Star》）— 比主 CPU 還快的 65C816
- **Cx4**（《Mega Man X2》）— 浮點協處理器
- **SPC7110**（《Far East of Eden Zero》）— 含解壓縮硬體

### 代表性開源模擬器

- **bsnes / higan / ares** — 由 byuu/Near 創立的精度典範系列；ares 是現役分支
- **Snes9x** — 老牌、相容度高、效能好
- **bsnes-jg** — bsnes 的維護分支

### 難度評級：★★★★ 中階

從 NES 跨到 SNES，難度約**兩個量級**的躍升 —— 主要來自 65C816 動態狀態追蹤、SPC700 獨立同步、跟協處理器數量。

---

## Nintendo 64 — 1996.06.23

> 任天堂第一台 64-bit 主機，也是世界上第一台 64-bit 家用機。北美 1996.09.29 上市。

### 硬體架構

- **CPU**：NEC VR4300（基於 MIPS R4300i），93.75 MHz
- **協處理器**：RCP（Reality Co-Processor）—— 含 RSP（向量處理器）+ RDP（光柵化器）
- **記憶體**：4 MB RDRAM（可擴充至 8 MB），UMA 架構
- **儲存**：卡匣（最大 64 MB）+ Controller Pak / Rumble Pak
- **圖像**：320×240 / 640×480、1670 萬色 + 抗鋸齒

### 模擬實作的核心難點

如果說 SNES 是「2D 模擬精度的地獄」，N64 就是「3D 模擬架構的迷宮」：

**1. RSP 的可程式化微代碼（Microcode）**

RSP 是基於 MIPS 的向量處理器，**支援開發者自訂微代碼**。任天堂提供 SDK 內建幾種（Fast3D、F3DEX、F3DEX2），但 Rare、Factor 5 等大廠寫了自己的版本。要支援所有遊戲，模擬器得針對**每種微代碼個別逆向工程或 HLE** —— 這就是為什麼 N64 模擬器歷史上有 HLE/LLE 的長期分裂。

**2. RDP 的低階渲染**

RDP 處理 z-buffering、抗鋸齒、紋理過濾。要精確重現 RDP 行為（LLE 模式，例如 Angrylion 插件）即使在現代 CPU 上也吃緊。HLE 模式（例如 GLideN64）速度快但相容性差。

**3. UMA（Unified Memory Architecture）**

CPU、RSP、RDP 共享同一塊 4 MB RDRAM。三方之間的 cache 一致性、bus arbitration 都要精確模擬，否則會出現紋理錯誤、Z-fighting 等典型「N64 模擬器破圖」。

**4. 浮點數行為**

R4300i 的浮點運算結果跟 IEEE 754 有微小差異（特別在 denormal 處理上）。物理引擎跟過場動畫高度依賴這些細節，差一個 ULP 就可能讓主角飛出地圖。

**5. 異常處理 + TLB**

R4300i 有完整的 MMU 跟 TLB（Translation Lookaside Buffer）。某些遊戲（《Body Harvest》、《Indiana Jones》）使用虛擬記憶體 paging，模擬器必須完整實作 TLB miss → exception handler → page table walk 的流程。

### 代表性開源模擬器

- **Project64** — 最老牌的 N64 emulator
- **Mupen64Plus** — 跨平台插件式架構
- **Ares** — 高精度多系統，N64 模組基於 LLE
- **simple64** — 較新的 fork，注重精度

### 難度評級：★★★★★★ 專家級

N64 排這麼高不是因為性能要求高，而是 RSP 微代碼這個「黑盒子」需要案例式逆向。寫一個能跑《Mario 64》《Zelda OoT》的版本可達成，要支援《Conker's Bad Fur Day》《Banjo-Tooie》等帶自訂微代碼的遊戲就成倍難。

---

## Game Boy Color — 1998.10.21

> Game Boy 的彩色升級版，向下相容 GB 卡匣。

### 硬體架構

- **CPU**：升級版 LR35902，可切換 4.19 MHz / 8.38 MHz（雙倍速）
- **記憶體**：32 KB WRAM（8 banks）、16 KB VRAM（2 banks）
- **音訊**：跟 GB 完全相同
- **圖像**：160×144、最多同時 56 色（從 32,768 色挑選）

### 模擬實作的核心難點

GBC 對 GB 模擬器來說不是「打掉重練」，而是處理**效能加倍**跟**色彩管理**兩件事：

**1. 雙倍速模式**

寫 `0x01` 到 `KEY1` 後執行 `STOP`，CPU 會切到 8.38 MHz，但 PPU、APU、Timer 維持原速 —— 換言之 CPU 跟周邊的時鐘比例改變了。如果同步邏輯寫得太死（hardcoded ratio），切速度時音調會變、畫面會跑掉。

**2. GBC 色彩校正（Color Correction）**

GBC 螢幕色彩飽和度低 + 特殊 gamma 曲線，直接把 15-bit RGB 線性映射到現代 24-bit 螢幕會過度鮮豔。專業模擬器會內建 color correction 矩陣（SameBoy 的演算法是事實標準）。

**3. HDMA / GDMA**

GBC 新增的 DMA 功能，可以在每條掃描線的 H-Blank 期間搬資料（H-Blank DMA）。用來在 raster line 中途換背景或精靈資料。對 PPU 時序精度要求極高 —— H-Blank 才幾十個 cycle，要精確算對 transfer 在哪一個 dot 觸發。

**4. VRAM / WRAM 多 bank 切換**

新的 `VBK` 跟 `SVBK` 暫存器控制當前可見的 bank。Bank 切換邏輯如果寫錯，遊戲會存取到錯的記憶體段，畫面破圖、邏輯錯亂。

### 代表性開源模擬器

跟 Game Boy 共用：**SameBoy**、**mGBA**、**BGB**。SameBoy 是 GBC 模擬精度的金標準，過了 CGB-Acid2 全部測試。

### 難度評級：★★★ 進階

如果已經有 GB 模擬器底子，GBC 是「擴充」而非「重做」。但雙倍速模式跟 HDMA 是新挑戰。

---

## Game Boy Advance — 2001.03.21

> 第一台採用 ARM 架構的任天堂掌機。北美 2001.06.11 上市。

### 硬體架構

- **CPU**：ARM7TDMI（含 Thumb 16-bit 子集），16.78 MHz
- **記憶體**：32 KB IWRAM（內部）、256 KB EWRAM（外部）、96 KB VRAM
- **音訊**：4 通道 GB 相容 + 2 路 8-bit PCM 直接音訊（Direct Sound）
- **圖像**：240×160、32,768 色 + 4 層背景 + 縮放/旋轉
- **儲存**：卡匣含 SRAM/Flash/EEPROM 三種存檔機制

### 模擬實作的核心難點

GBA 是任天堂掌機**從自訂處理器跨到標準 ARM 的分水嶺**。文件公開度比早期主機高很多：

**1. ARM / Thumb 雙指令集**

ARM7TDMI 同時支援 32-bit ARM 跟 16-bit Thumb 指令，可動態切換（透過 `BX` 指令）。Thumb 指令集是壓縮版本，犧牲表達能力換取程式碼密度。模擬器解碼器要能無縫切換兩種模式。

**2. Wait States**

GBA 對記憶體存取速度敏感：IWRAM（0 wait state）、EWRAM（2 wait states，可設定）、Game Pak ROM（依 `WAITCNT` 設定 1-8 wait states）、Game Pak SRAM（不同設定）。指令時序計算要逐筆查當前位址的 wait state。**沒處理好的後果**：很多遊戲畫面撕裂，《GoldenEye Rogue Agent》之類的遊戲直接死當。

**3. Direct Sound + DMA**

兩個新增的 PCM 通道使用循環 buffer + DMA 自動補資料。DMA 觸發時機（時序對到 sample rate）要跟 timer 完美配合，否則會有爆音。

**4. PPU 的多模式背景**

6 種背景模式（Mode 0-5），含旋轉縮放（類似 SNES Mode 7 但更通用）、bitmap 直接 framebuffer 模式（Mode 3-5）、混合模式。窗口、混色、優先順序的計算每像素都得做。

**5. 卡匣存檔機制不一**

不同遊戲用 SRAM、Flash（64K/128K）或 EEPROM（512 byte/8K）三種完全不同的存檔機制，且**沒有標準的偵測方法** —— 模擬器得用啟發式（搜尋 ROM 內的特徵字串）或維護 game database 來判斷。

### 代表性開源模擬器

- **mGBA** — 跨平台、現役主力 GBA emulator，精度跟相容性都頂尖
- **VBA-M** — VisualBoyAdvance 的維護分支
- **NanoBoyAdvance** — 較新的高精度 GBA 模擬器

### 難度評級：★★★★ 進階+

ARM 架構文件齊全降低門檻，但 wait states + direct sound + 旋轉背景同步加起來是不小工作量。

---

## GameCube — 2001.09.14

> 任天堂第一台採用光碟（mini DVD）的家用機。北美 2001.11.18 上市。

### 硬體架構

- **CPU**：IBM PowerPC 750CXe「Gekko」，485 MHz，含獨家 Paired-Singles 指令
- **GPU**：ATI/ArtX「Flipper」，含 TEV（Texture Environment Unit）固定功能流水線
- **記憶體**：24 MB 1T-SRAM 主記憶體 + 16 MB ARAM（音訊）
- **儲存**：1.5 GB miniDVD
- **圖像**：480i/480p、最大 1920×1080 framebuffer

### 模擬實作的核心難點

從 N64 的「不對稱怪異硬體」跨到 GameCube 的「精密高效率 PowerPC 小鋼炮」：

**1. Paired-Singles（PowerPC 750CXe 的客製化 SIMD）**

Gekko 的浮點暫存器可以在 64-bit 內塞兩個 32-bit float，配合 `ps_madd`、`ps_sum0` 等專用指令做物理運算。**捨入行為跟 IEEE 754 微妙不同** —— 物理引擎依賴這些細節，捨入錯一位水會流錯方向。

**2. TEV（Texture Environment Unit）**

GameCube 的 GPU 用一個**最多 16 階段的固定功能 TEV chain** 處理紋理混合，每階段可任意設定混色公式。現代 GPU（Vulkan/D3D12）只認 shader，模擬器必須把 TEV 設定**動態翻譯成 fragment shader** —— 而且因為 TEV 有 1670 萬種設定組合，shader 編譯次數爆炸。

**3. FIFO（CP / GP 同步）**

CPU 把繪圖指令寫進 FIFO，GPU 從中讀取。兩邊處理速度的時序如果掌握不準，會出現 GPU 讀到空資料或 CPU overrun，畫面閃爍或當機。Bus Timing 是 GameCube/Wii 模擬器歷史上反覆 debug 的主題。

**4. Endian 差異**

PowerPC 是 Big-Endian，現代 PC（x86/ARM）是 Little-Endian。每次記憶體存取都要 byte swap。.NET 的 `BinaryPrimitives.ReverseEndianness` 或硬體 `bswap` 指令是性能關鍵。

**5. 浮點數異常**

PowerPC 處理 denormalized number 跟 IEEE 754 不完全一致，部分遊戲依賴此差異。

### 代表性開源模擬器

- **Dolphin** — GameCube + Wii 通用模擬器，數十年的工程積累，跨平台、高度成熟
- **Ishiiruka** — Dolphin 的優化分支

### 難度評級：★★★★★★ 高階

Dolphin 的 codebase 龐大複雜，但成熟度高、文件齊全。Paired-Singles 跟 TEV→shader 翻譯是兩大主題挑戰。

---

## Nintendo DS — 2004.11.21

> 任天堂主流掌機從 GBA 的後繼者。雙螢幕、觸控、麥克風。

### 硬體架構

- **CPU**：兩顆 ARM 處理器
  - ARM946E-S（67 MHz）負責主邏輯與 3D
  - ARM7TDMI（33 MHz）負責音訊、Wi-Fi、卡匣
- **記憶體**：4 MB Main RAM、64 KB ARM7 WRAM、Shared 32 KB WRAM、656 KB VRAM
- **音訊**：16 PCM 通道（ARM7 處理）
- **圖像**：兩個 256×192 螢幕、2D 引擎 A/B、3D 硬體（最多 ~2048 多邊形/frame）
- **特色**：觸控、麥克風、Wi-Fi、向下相容 GBA 卡匣

### 模擬實作的核心難點

NDS 是任天堂掌機**從 8/16-bit 思維跨到雙核多媒體**的轉折：

**1. 雙核同步（ARM9 + ARM7）**

兩顆 CPU **共享記憶體跟 IPC FIFO**。同步精度不夠的話，遊戲會頻繁當機 —— 例如《Jump Ultimate Stars》《罪惡裝備》對 IPC 時序非常挑剔。ARM9 還有 cache + write buffer，加上跟 ARM7 cache 不一致時的 invalidation 邏輯。

**2. ARM946E-S 的進階特性**

ARM9 比 ARM7 多了：
- DSP 指令（`SMUL`、`SMLA` 等飽和運算）
- MPU（不是完整 MMU，但有區段保護）
- 指令/資料 cache + write buffer
- TCM（Tightly-Coupled Memory，類似 cache 但程式可控制）

對 JIT 模擬器來說，cache + write buffer 是 SMC（self-modifying code）偵測的惡夢。

**3. 3D 硬體的定點數運算**

NDS 沒有浮點 GPU。所有 3D 矩陣運算用 fixed-point。模擬器要精確還原 fixed-point 的捨入行為跟溢位處理 —— 否則貼圖會輕微錯位、Z-fighting 處處可見。

**4. 2D 引擎 A/B + Capture 模式**

兩套獨立的 2D 引擎（Engine A 上螢幕、Engine B 下螢幕），各 4 層背景、accelerated effects、Master Brightness。還有「3D capture」模式可把 3D 渲染到 2D 圖層當背景，混合邏輯複雜。

**5. 加密卡匣協議**

NDS ROM 含加密 secure area。卡匣命令協議 + KEY1/KEY2 加密如果模擬不對，遊戲開機就會卡住。

### 代表性開源模擬器

- **melonDS** — 跨平台、精度高、現役主流
- **DeSmuME** — 老牌、相容度好
- **DraStic** — Android 端最強，閉源但商業上很成功

### 難度評級：★★★★★ 中高階

從單核 8-bit 跳到雙核 ARM9+ARM7+IPC 是質變。3D 硬體精度也是新主題。

---

## Wii — 2006.12.02

> 動作感應+網路功能的家用機。北美 2006.11.19 上市。

### 硬體架構

- **CPU**：PowerPC「Broadway」729 MHz（Gekko 的超頻版）
- **GPU**：「Hollywood」（Flipper 增強版）
- **協處理器**：「Starlet」（ARM9 核心，內嵌於 Hollywood，跑 IOS）
- **記憶體**：24 MB 1T-SRAM + 64 MB GDDR3
- **儲存**：12 cm DVD 光碟、512 MB 內建 NAND
- **特色**：藍牙 Wii Remote（紅外線 + 加速規）、Wi-Fi、向下相容 GameCube

### 模擬實作的核心難點

Wii 在硬體上是「兩倍速 GameCube」，但加上**異質協處理器**跟**現代化 I/O**：

**1. Starlet（ARM 協處理器）+ IOS**

Hollywood 內藏 ARM9 核心叫 Starlet，跑作業系統 IOS。Starlet 接管所有 I/O：SD 卡、Wi-Fi、光碟、USB。所有硬體解密跟安全檢查也在 Starlet。**模擬器要同時跑 PowerPC JIT（Broadway）+ ARM 模擬器（Starlet）並讓兩邊正確通訊**。

**2. 藍牙 Wii Remote 映射**

Wii Remote 是真正的藍牙 HID 裝置，有加速規 + 紅外線定位。模擬器要：
- 模擬整個藍牙 stack（讓遊戲看到「藍牙裝置連上了」）
- 將 PC mouse / 手把映射成 Wii Remote 的 IR + accel 資料流
- 處理 Nunchuk、Classic Controller、Balance Board 等擴充配件不同的回報格式

**3. NAND 檔案系統**

Wii 內建 512 MB NAND 存系統選單、頻道（Channels）、存檔。模擬器要實作 NAND 虛擬檔案系統 + WAD 格式解析（頻道安裝包）。

**4. AES 解密**

Disc + Title key + Disc key 三層加密，雖然比 3DS/Switch 簡單，仍需要正確的 common key 才能解開光碟內容。

### 代表性開源模擬器

- **Dolphin** — GameCube + Wii 同套程式碼，是這兩台機器的事實標準
- **Wii 專屬模擬器幾乎沒有獨立分支**，因為 Dolphin 已涵蓋

### 難度評級：★★★★★★★ 挑戰級

Wii 不是「Dolphin GameCube + 一點藍牙」這麼單純 —— Starlet/IOS 模擬是整個額外子系統。

---

## Nintendo 3DS — 2011.02.26

> 裸視 3D 掌機。北美 2011.03.27 上市。

### 硬體架構

- **CPU**：ARM11 MPCore（雙核，New 3DS 升為四核），268 MHz（New 3DS 804 MHz）
- **GPU**：DMP「PICA200」，使用客製化 Maestro 著色器
- **協處理器**：ARM9（系統服務）、DSP（音訊）
- **記憶體**：128 MB FCRAM（New 3DS 256 MB）、6 MB VRAM
- **特色**：裸視 3D（視差障壁）、陀螺儀、雙螢幕、StreetPass / SpotPass

### 模擬實作的核心難點

3DS 是任天堂掌機**從「電子玩具」跨到「現代行動運算裝置」**的分水嶺：

**1. ARM11 MPCore 對稱多處理（SMP）**

跟 NDS 的雙核異步不同，3DS 是真正的 SMP。模擬器要處理 race conditions、cache coherency、跨核 IPC。

**2. PICA200 GPU + Maestro shader**

PICA200 不是標準的 OpenGL/D3D 流水線，使用客製化的「Maestro 指令集」 + 一堆固定功能單元（特殊光照模型、霧化、過濾）。模擬器要把 2011 年的客製硬體特性映射到現代 GLSL/HLSL —— 這比 GameCube TEV 更難，因為 PICA200 既有部分 shader 可程式化、又有固定功能單元。

**3. Horizon OS**

3DS 跑完整微核心作業系統。模擬器通常採 HLE（高階模擬）—— 用 C++ 重寫數百個系統服務（檔案系統、好友清單、相機、音訊渲染）。

**4. AES 加密 + bootrom secrets**

NCCH/NCSD 格式經 AES-128-CTR 加密，需要正確的 KeyX/KeyY 才能解開。還有 bootrom 的硬體 secret 需要從真機提取。3DS 的解密體系比 Wii 嚴格很多。

**5. 雙螢幕 + 3D 視差渲染**

上螢幕含視差障壁需要同時渲染左右眼兩路圖像，下螢幕觸控解析度不同。資源分配跟視窗管理變複雜。

### 代表性開源模擬器

- **Citra** — 跨平台、HLE 路線，現役主力（注意：原版 Citra 已停止維護，社群分叉持續）
- **Lime3DS / Mandarine / Azahar** — Citra 的活躍 fork

### 難度評級：★★★★★★★ 挑戰級

從 NDS 跳到 3DS 是「跨量級」躍升 —— SMP + 現代 GPU + 完整作業系統 HLE。

---

## Wii U — 2012.12.08

> 任天堂第一台 HD 主機。北美 2012.11.18 上市。商業表現不佳但技術獨特。

### 硬體架構

- **CPU**：PowerPC「Espresso」三核心，1.24 GHz
- **GPU**：AMD Radeon R700 系列「Latte」，550 MHz
- **記憶體**：2 GB DDR3（系統 1 GB + 遊戲 1 GB）
- **儲存**：25 GB 雙層藍光衍生光碟、8/32 GB 內建快閃 + USB 擴充
- **特色**：GamePad（內建螢幕 + 觸控 + 陀螺儀 + NFC + 攝影機）

### 模擬實作的核心難點

Wii U 是任天堂主機演進中**異常獨特的轉折點**，既保留 PowerPC 又加入現代多核 + 著色器：

**1. Espresso 三核 SMP**

三核共享 L2 cache，遊戲廣泛使用多執行緒。模擬器在 x86 host 上要保證 memory consistency model 正確（PowerPC 是 weak ordering，x86 是 stronger），這在多執行緒下會產生極難 debug 的同步錯誤。

**2. GX2（現代著色器架構）**

跟 GameCube/Wii 的 TEV 完全不同 —— Wii U 用 R700 GPU 跑完整 fragment/vertex shader。**Shader cache 問題嚴重**：Wii U 遊戲在執行時動態生成成千上萬個 shader 組合，每次進入新場景就觸發編譯卡頓。Cemu 的 Shader Cache + Pipeline Cache 機制是著名的解決方案。

**3. GamePad 雙串流**

主機要**同時渲染兩路畫面**（電視 1080p + GamePad 480p），且 GamePad 透過專用 5 GHz 無線視訊串流。模擬器要處理同時兩路渲染管線（很多遊戲兩個畫面內容完全不同）。

**4. Cafe OS + RPL 動態鏈結**

Wii U 不再像 NGC/Wii 那樣直接操作硬體 —— 跑的是 Cafe OS，有完整的系統呼叫表。`.rpx` / `.rpl` 動態鏈結庫格式需要專屬 loader 處理符號重定向。模擬器要 HLE 數千個系統函式。

**5. NFC + Amiibo**

GamePad 內建 NFC 讀卡機，遊戲讀寫 Amiibo 資料。模擬器要實作虛擬 NFC tag。

### 代表性開源模擬器

- **Cemu** — 唯一成熟的 Wii U 模擬器，2022 年開源，2023 年加入 Linux 支援
- **Decaf** — 較小型的研究型 Wii U emulator

### 難度評級：★★★★★★★★ 魔王級

Cemu 在 Wii U 模擬幾乎是壟斷地位 —— 沒第二個成熟選擇是因為這個級別的模擬器需要的工程量太大。

---

## Nintendo Switch — 2017.03.03

> 家用 + 掌機混合模式主機。

### 硬體架構

- **CPU/GPU SoC**：Nvidia Tegra X1
  - CPU：4 核 ARM Cortex-A57（1.02 GHz 攜帶 / 1.78 GHz 連接電視）
  - GPU：Nvidia Maxwell（256 CUDA cores），307~768 MHz 動態調整
- **記憶體**：4 GB LPDDR4
- **儲存**：32 GB 內建 + microSD 擴充、卡匣
- **特色**：Joy-Con（HD 振動 + IR 攝影機 + 加速規 + 陀螺儀 + NFC）

### 模擬實作的核心難點

Switch 本質上是「搭載 Tegra X1 的現代行動電腦」。模擬器開發呈現**「初期進展極快，後期優化極難」**的曲線：

**1. ARMv8 (AArch64) JIT**

跟 Switch 一樣都是 ARMv8，但你不能在 x86-64 host 上直接跑 ARM 指令。模擬器要寫 AArch64 → x86-64 的 JIT。**Memory consistency 是大坑** —— ARMv8 是 weak ordering，x86-64 是 stronger，需要在所有跨執行緒記憶體存取上加 memory fence。

**2. Maxwell GPU 模擬**

Switch 遊戲透過 Nvidia 自家 NVN API 或 Vulkan 繪圖。模擬器要：
- 攔截 GPU 命令並翻譯成 host 端 Vulkan / D3D12
- 處理 Maxwell 特殊的 tile-swizzled 紋理格式
- 即時編譯 Switch shader → SPIR-V

Shader stutter（編譯卡頓）是 Switch 模擬器最常見的抱怨。

**3. Horizon OS（微核心）+ 系統服務**

Switch 跑微核心作業系統 Horizon。遊戲依賴大量系統服務（帳號、Bluetooth、音訊渲染引擎、檔案系統）。模擬器要 HLE 這些服務。

**4. 強大加密**

NCA (Nintendo Content Archive) 格式經 AES-128-XTS 加密，需要數十組 prod.keys / title.keys。RomFS / Save Data 也都加密。

**5. 多核同步**

Tegra X1 四核中遊戲通常用 3 核（核 0-2）。三核 → host 執行緒映射、確保同步精度，是維持穩定 FPS 的關鍵。

### 代表性開源模擬器

- **Yuzu** — 用 C++ 寫的 Switch 模擬器，2024 年因法律問題停止官方維護，社群有 fork 持續
- **Ryujinx** — 用 C# / .NET 寫的 Switch 模擬器，2024 年原作者停手後社群接管 fork
- **Suyu / Sudachi** — Yuzu 的活躍 fork

### 難度評級：★★★★★★★★★ 終極

技術門檻最高，加上法律風險（任天堂積極打擊 Switch 模擬器專案）讓這領域氣氛緊張。從正面意義上講，Switch 模擬器涵蓋了現代電腦科學幾乎所有重要主題：JIT、shader compiler、現代 GPU API、微核心、密碼學、多執行緒同步。

---

## 整體難度排名與選擇建議

按「從零實作可運行核心」所需工程量排名：

| 排名 | 主機 | 發售年份 | 難度 | 核心挑戰關鍵字 |
|---|---|---|---|---|
| 1 | Game Boy | 1989 | ★ 入門 | 8-bit 時序、MBC 分頁 |
| 2 | NES / Famicom | 1983 | ★★ 初階 | PPU 掃描線、Mapper、APU 同步 |
| 3 | Game Boy Color | 1998 | ★★★ 進階 | 雙倍速、HDMA、色彩管理 |
| 4 | Game Boy Advance | 2001 | ★★★★ 進階+ | ARM/Thumb、Wait States、Direct Sound |
| 5 | SNES / Super Famicom | 1990 | ★★★★ 中階 | 65C816、SPC700、協處理器 |
| 6 | Nintendo DS | 2004 | ★★★★★ 中高階 | 雙核 ARM9+ARM7、3D fixed-point |
| 7 | Nintendo 3DS | 2011 | ★★★★★★ 挑戰級 | ARM11 SMP、PICA200、Horizon OS HLE |
| 8 | GameCube | 2001 | ★★★★★★ 高階 | TEV、Gekko JIT、FIFO 同步 |
| 9 | Wii | 2006 | ★★★★★★★ 挑戰級 | Starlet/IOS、藍牙映射 |
| 10 | Nintendo 64 | 1996 | ★★★★★★★ 專家級 | RCP 微代碼、UMA |
| 11 | Wii U | 2012 | ★★★★★★★★ 魔王級 | 三核 SMP、GX2、Cafe OS |
| 12 | Switch | 2017 | ★★★★★★★★★ 終極 | Maxwell GPU、HLE、shader compiler |

### 學習路徑建議

- **第一步**：NES 或 Game Boy。8-bit 時序是所有模擬器的基本功。建議從文件最齊全的 NES 入手，經過 blargg 184/184 + AccuracyCoin 138/138 兩套測試。
- **第二步**：Game Boy Color → SNES。學會「擴充已有 emulator」跟「協處理器架構」。
- **第三步**：GBA → NDS。轉到 ARM 標準架構，開始接觸 JIT 跟 3D。
- **第四步**：N64 或 GameCube。RISC 64-bit + 3D 加速 + UMA / FIFO 同步。
- **第五步起**：Wii / 3DS / Wii U / Switch。完整作業系統、現代 GPU、HLE — 這個層級的 emulator 通常需要團隊規模。

---

## 跨主機共通的開發主題

不論做哪一台機器，下面這些主題會反覆出現：

### 1. 時序模型（Timing Model）

「每執行一條 Guest 指令，要花掉多少 host 時間？周邊（PPU / GPU / DMA）什麼時候推進？」幾乎所有 8/16-bit 主機都會碰到這個 cycle-accurate 議題。詳細討論可參考 [NES 模擬器 Timing 模型對照指南](nes_emulator_timing_models_guide_zh.md)。

### 2. JIT vs. Interpreter

8-bit 主機純 interpreter 就夠快；GBA 開始 interpreter 跑滿速但 JIT 省電；NDS/3DS/Switch 沒有 JIT 跑不動。JIT 跟 .NET / Java JIT 的差別、實作技術選擇，可參考 [模擬器技術問答集](emulator_techniques_qa_zh.md)。

### 3. 密碼學

從 NDS 卡匣的 KEY1/KEY2，到 Wii 的 Title Key 三層加密、3DS 的 AES-CTR、Switch 的 NCA AES-XTS —— 主機越新加密越複雜，模擬器要嘛實作完整的密碼學引擎，要嘛要求使用者提供從真機 dump 出的解密 key。

### 4. HLE vs. LLE

從 N64 的 RSP 微代碼開始，每台主機都有「直接模擬硬體（LLE）」 vs「攔截系統呼叫用 host API 重寫（HLE）」的選擇。LLE 精度高但慢，HLE 快但相容性差。

### 5. 測試驅動開發

任天堂主機因為社群活躍，每台都有完整的 test ROM 套件：
- NES：blargg、AccuracyCoin、scanline-a1 等
- GB/GBC：Mooneye GB、Blargg's GB tests、CGB-Acid2
- GBA：mGBA suite、jsmolka tests
- N64：N64-tests
- 各家測試套件能讓模擬器精度進度可量化、可追蹤。

### 6. 開源生態

任天堂主機模擬器幾乎都有成熟的開源實作可參考：Mesen2、SameBoy、bsnes/ares、mGBA、melonDS、Dolphin、Cemu、Citra fork、Yuzu/Ryujinx fork。讀別人的程式碼是少走彎路最有效的方法。

---

## 結語

寫模擬器不是練「快速跑老遊戲」這件事，而是用一個具體目標逼自己學完計算機架構的整套核心議題：CPU 微架構、記憶體階層、同步、JIT 編譯、GPU 渲染管線、作業系統服務、密碼學、形式驗證。任天堂這 12 台主機剛好提供了從 1983 到 2017 的完整漸進階梯 —— 每往前一台機器，就多解鎖一塊計算機科學的拼圖。

從 NES 開始，逐台往前做。每一台都是一個獨立的小宇宙，每一個小宇宙都連著更大的領域。
