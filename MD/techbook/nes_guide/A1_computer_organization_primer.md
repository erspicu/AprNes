# A1 計算機組織小複習：寫程式但不熟硬體的人需要知道的事

## 這章要解決什麼問題

很多人會寫 Python、JavaScript 甚至 C 語言，但問起「register 是什麼」「bus 是什麼」「為什麼 CPU 跟 PPU 會搶記憶體」這類問題就會卡住。這篇是給你「在開始讀模擬器章節之前」補的硬體基礎，全程用生活比喻把抽象名詞接地氣。

讀完以後，再回頭看 02–17 章會輕鬆很多。

---

## 整體比喻：把電腦想成一間廚房

接下來幾乎所有名詞都用這個廚房比喻去對應：

```
廚房 = 一台電腦

主廚 (CPU)              = 看食譜照流程做菜的人
食譜書 (ROM)            = 不能改寫的指令來源
工作檯 (RAM)            = 隨時放暫時食材的桌面
冰箱 (大型 storage)     = 慢但容量大（NES 裡幾乎沒有，但現代電腦有）
傳送帶 (Bus)            = 食材跟器具進出主廚手邊的軌道
節拍器 (Clock)          = 廚房的拍子，每一下決定誰做什麼
門鈴/警鈴 (Interrupt)   = 打斷主廚現在動作的訊號
助理 (DMA)              = 不打擾主廚，自己去搬東西的人
特殊水龍頭/瓦斯閥 (I/O) = 廚房裡某些「位置」直接連到外面的設備
甜點師 (PPU)            = 跟主廚同時工作但做不同事的人
配樂師 (APU)            = 邊做菜邊放音樂的人
卡匣 (Cartridge)        = 客人帶來的「外掛廚具盒」（含食譜跟自己的工具）
```

如果你看下面任一段名詞糊塗了，回頭對照這張表通常能想通。

---

## 1. Bit、Byte、Word：食物的單位

- **Bit**：一粒米。0 或 1。
- **Byte（8 bits）**：一袋米。最小有意義的單位，可以表達 0–255 或 -128–127。
- **Word**：一箱米。這個字在不同電腦上意義不一樣 —— 16-bit 機器叫一箱 16 bits，64-bit 機器叫一箱 64 bits。**NES 是 8-bit 機器**，所以 NES 上講「word」通常指 16-bit（由兩個 byte 組成）。

NES CPU 一次只能搬一袋米（一個 byte）。要搬大東西（例如 16-bit 位址），得分兩次搬。

### Endian（端序）：袋裡的米要從哪頭倒？

當你要把 16-bit 數字 `0x1234` 存到記憶體兩個位置時，你會：
- **Little-endian**：先放低位 → `34, 12`（先倒小米粒那頭）
- **Big-endian**：先放高位 → `12, 34`（先倒大米粒那頭）

**6502 / NES 是 Little-endian**。寫過 PowerPC（GameCube/Wii）轉到 x86 的人才會抱怨 endian。NES 開發只要記住「先低後高」就好。

---

## 2. Register、RAM、ROM：手邊、桌上、書架

模擬器初學者最容易混淆的三層儲存：

| 名字 | 廚房比喻 | 速度 | 容量 | 可寫嗎？ |
|---|---|---|---|---|
| **Register** | 主廚兩隻手抓著的食材 | 最快 | 極小（NES 6502 只有 6 個） | 可寫 |
| **RAM** | 工作檯 | 快 | 中（NES 主機 2KB + 卡匣可能還有 SRAM） | 可寫 |
| **ROM** | 食譜書 | 快但「唯讀」 | 大（NES 卡匣可達 MB 級） | **不能寫**（或要靠 mapper 切換） |

主廚每動一個食譜步驟，都要：
1. 看一眼食譜（從 ROM 讀指令）
2. 從工作檯拿/放食材（讀寫 RAM）
3. 一邊用兩隻手調整（操作 register）

NES 的 6 個 register：**A**（Accumulator，左手食材）、**X、Y**（兩個索引手指）、**SP**（堆疊指標）、**PC**（食譜的書籤）、**P**（狀態旗標 —— 等下會講）。

---

## 3. Bus（匯流排）：廚房的輸送帶

主廚不會直接跑去工作檯拿東西 —— 他把訂單寫在小紙條，「我要從 `$0042` 號格子拿一袋米」，紙條走輸送帶，工作檯那邊有人按單拿米回來。

NES 的 CPU bus 由兩條輸送帶組成：

- **Address bus（位址匯流排）**：16 條線，所以可以指定 `$0000`–`$FFFF`（65536 個格子）的任何一個。
- **Data bus（資料匯流排）**：8 條線，一次傳一個 byte。

每一個 CPU cycle，主廚會做下面其中一件事之一：
- 「**讀**：請把 `$1234` 號格子的內容給我」（位址 bus 送出 1234，資料 bus 回來一個 byte）
- 「**寫**：請把這個 byte 放到 `$5678` 號格子」（兩條 bus 都送）

每個 cycle 只能做一個動作。**這就是為什麼 cycle accuracy 很重要** —— 主廚發出訂單的順序、資料抵達的時機，整個系統都得對齊。

---

## 4. Clock（時脈）跟 Cycle（週期）：節拍器

廚房裡有個節拍器，每秒滴答 1,789,773 次（NES 北美版的 CPU 時脈是 ~1.79 MHz）。

每一次滴答叫一個 **clock cycle**。主廚每個動作都至少要一個 cycle：
- 從食譜讀一個 byte → 1 cycle
- 把資料寫到工作檯 → 1 cycle
- 一條 6502 指令通常要 2–7 cycles

但 NES 不只一個節拍器 —— 還有更快的 **master clock**（北美版 21.477 MHz），CPU 跑這個的 1/12，PPU 跑這個的 1/4。也就是說：

- 主廚每滴答 1 下，PPU 已經滴答 3 下了
- 三個人（CPU、PPU、APU）共用同一個節拍器，各自照自己的拍子做事

模擬器的中心問題就是：**怎麼讓三個拍子在軟體裡正確對齊**。AprNes 的 master clock loop 就是在做這件事。

---

## 5. Memory Map（記憶體映射）：大樓的樓層配置圖

CPU 看到的 64KB 位址空間（`$0000`–`$FFFF`）**不是一塊真的 64KB 記憶體**，而是一棟大樓的平面圖：

```
$0000-$07FF  RAM           (2KB 真實記憶體)
$0800-$1FFF  RAM mirror    (上面那塊的鏡像，講四遍)
$2000-$2007  PPU registers (共 8 個暫存器)
$2008-$3FFF  PPU mirror    (那 8 個暫存器一直重複)
$4000-$4017  APU + I/O     (聲音、手把、OAM DMA)
$4018-$401F  測試模式      (NES 用不到)
$4020-$FFFF  卡匣          (PRG ROM、SRAM、Mapper register)
```

把這想成一棟 65536 房間的大樓：

- 0–2047 號房：真的有 2KB RAM
- 2048–8191 號房：**鏡像**（按下這幾個房間的門鈴，會通到 RAM 那 2048 個房間之一）
- 8192–8199 號房：**特殊房間**，門鈴連到 PPU 晶片
- 16384–16407 號房：APU 跟手把
- 16415 號之後：卡匣的 PRG ROM 跟其他硬體

這就是 **memory-mapped I/O** —— 主廚以為自己在「往工作檯放東西」，但其實那個位置直接連到「PPU 晶片的某個訊號」。寫 `$2000` 不是寫到 RAM，是設定 PPU 控制暫存器。

### Mirroring（鏡像）：同房間多個門牌號

NES 為什麼把 2KB RAM 鏡像成 8KB？因為**省晶片**。當年解碼晶片只接 11 根 address line（足以解 2048 個房間），剩下的 line 直接忽略。所以你按 `$0042` 跟按 `$0842` 進的是同一間。

實作模擬器時，記憶體 dispatch 處理鏡像最簡單的方法是 `addr & 0x07FF`（把高 bit 砍掉）。

---

## 6. Memory-Mapped I/O 是什麼？特殊房間有外線

普通 RAM 房間：你寫 byte 進去，下次讀就拿到那個 byte。

**Memory-Mapped I/O 房間**：你寫 byte 進去，**會觸發某個硬體動作**（例如改變 PPU 的捲軸位置、觸發 DMA、改變音量）。讀也類似 —— 讀某個位址會回傳硬體當下狀態（例如 PPU 的 VBlank 旗標、手把的當前按鈕）。

例子：

| 寫入位址 | 不是寫到 RAM —— 而是觸發 |
|---|---|
| `$2000` | 設定 PPU 控制暫存器（NMI 開關、sprite 大小） |
| `$2005` | 設定 PPU 捲軸（要分兩次寫，第一次 X 第二次 Y） |
| `$2007` | 把 byte 送進 PPU 的 VRAM |
| `$4014` | **觸發 OAM DMA**：把 256 byte 從 RAM 搬到 PPU 的精靈表（會 stall CPU 513 cycles） |
| `$4017` | 設定 APU frame counter 模式 |

這就是為什麼模擬器的「memory write」函式不能只是 `mem[addr] = value;` —— 必須先看 addr 落在哪個區段，分派給對應的硬體 handler。AprNes 的 `MEM.cs` / `IO.cs` 就是在做這件事。

---

## 7. Open Bus：沒人接電話時聽到的回音

如果主廚對著輸送帶說「給我 `$401F` 號房間的東西」，但 `$401F` 那邊沒接電話（沒任何硬體處理那個位址），會發生什麼？

**真實硬體不是 0、不是隨機數，而是「資料 bus 上殘留的最後一筆值」。** 這叫 **open bus**。

這在很多老遊戲是合法行為 —— 它們會故意讀一個無效位址，**期待拿到「上次主廚剛剛碰過的 byte」**。模擬器要正確還原這個行為，否則某些測試 ROM 會出錯。

實作上，每次正常讀寫都記下這個「最後一筆 data bus 值」，當有人讀到無效位址就回它。

---

## 8. Interrupt：門鈴打斷做菜

主廚做菜做到一半，有時候會被打斷。打斷分兩種：

### NMI（Non-Maskable Interrupt）— 火警鈴

- **無法忽略**。一響起來主廚必須立刻放下手邊的活去處理。
- NES 上 NMI 來自 PPU —— **每幀畫面結束（VBlank）就響一次**。遊戲利用這個時機更新畫面（因為這時 PPU 暫時不在畫東西，可以安心改 VRAM）。
- 響起來時 CPU 會：
  1. 把當前的「書籤位址 PC」跟「狀態旗標 P」存到工作檯（堆疊）
  2. 跳去 `$FFFA-$FFFB` 指定的地方執行（NMI handler）
  3. 處理完用 `RTI` 指令回原處繼續

### IRQ（Interrupt Request）— 普通電話

- **可以忽略**（如果狀態旗標的 `I` flag 設了就不接）。
- NES 上 IRQ 來自 APU frame counter、DMC sample 結束、**或卡匣 mapper**（例如 MMC3 的 scanline IRQ counter）。
- 處理流程跟 NMI 類似，但走 `$FFFE-$FFFF` 那邊。

### Reset — 大樓拉警報重新開機

按下 NES 的 reset 按鈕，CPU 會把書籤跳到 `$FFFC-$FFFD` 指定的位址，從那邊重新開始（但 RAM 不清空，所以遊戲開機畫面有時會看到上次玩剩下的內容）。

模擬器要實作這三個 vector：`$FFFA`（NMI）、`$FFFC`（Reset）、`$FFFE`（IRQ/BRK）。

---

## 9. DMA：助理直接搬東西，不打擾主廚？其實會

**DMA**（Direct Memory Access）是「主廚不動手，叫助理直接從 A 搬到 B」的機制。

NES 上有兩種 DMA：

### OAM DMA（精靈表 DMA）

寫 `$4014` 觸發，把指定的 256 byte 一次搬到 PPU 的 OAM（精靈屬性表）。

「不打擾主廚」這句**在 NES 上不完全正確** —— 助理跟主廚共用同一條輸送帶，所以助理在搬的時候，主廚必須**停下來等**（513–514 cycles）。

但即便如此，比起主廚自己用 256 條 `STA` 指令搬要快很多。

### DMC DMA（音訊取樣 DMA）

DMC 聲道播放長段音訊取樣時，每隔幾百 cycle 會自動去 PRG ROM 拿下一筆資料。這個 DMA **會偷走 1–4 個 CPU cycle**（cycle stealing）—— 主廚進行到一半被插隊讓 DMC 用一下 bus。

很多遊戲的時序敏感邏輯會被 DMC DMA 影響，這也是 NES 模擬器精度測試的重點之一。

---

## 10. CPU vs PPU 同時跑：主廚跟甜點師

NES 厲害的地方是 CPU 跟 PPU **真的同時在動**。不是「CPU 跑完一條再讓 PPU 補三步」，而是兩個人各自照自己的節拍器拍子前進。

```
master clock 拍   1   2   3   4   5   6   7   8   9   10  11  12
CPU                                       *cycle*               *cycle*
PPU              *dot*  *dot*  *dot*  *dot*  *dot*  *dot*  ...
```

PPU 跑 master clock 的 1/4，CPU 跑 1/12 —— 比例剛好 3:1（PPU 每跑 3 dot CPU 才跑 1 cycle）。

這就是為什麼模擬器的「**tick model**」會這樣寫：

```
每次 CPU 做一個 read/write：
    1. 推進 PPU 3 dot
    2. 推進 APU 1 cycle
    3. 真正讀/寫
```

AprNes 在 `MEM.cs` 的 `tick()` 函式就是在做這件事。

---

## 11. State Machine（狀態機）：硬體就是會自己滴答的機器

**State machine** 是「給定當下狀態 + 輸入訊號 → 跳到下一個狀態」的數學模型。**幾乎所有硬體都是 state machine**。

PPU 是個明顯例子：

```
Mode 0 (HBlank)   → Mode 2 (OAM Search)
Mode 2 (OAM Search) → Mode 3 (Pixel Transfer)
Mode 3 (Pixel Transfer) → Mode 0 (HBlank) → ...
```

每次 master clock 滴答一下，PPU 就照自己的邏輯前進到下一個狀態。模擬器要做的就是把這個 state machine 在軟體裡精準復刻。

CPU 也是 state machine，只是比較複雜 —— 每條指令在內部其實由多個「micro-step」組成，每個 step 一個 cycle。例如 `LDA $1234`（從 `$1234` 讀到 A）內部其實是：

```
cycle 1: 讀 opcode (PC=PC+1)
cycle 2: 讀低 byte 1234 中的 0x34 (PC=PC+1)
cycle 3: 讀高 byte 1234 中的 0x12 (PC=PC+1)
cycle 4: 從 $1234 讀到 A
```

cycle-accurate 模擬器就是把每條指令拆到這個粒度。

---

## 12. Latch / Flip-Flop：留言板

**Latch** 是只能存 1 bit 的最小儲存單元。物理上是兩個邏輯閘接成迴圈，能「記得」最後一次寫進去的值，直到下次寫蓋掉。

廚房比喻：留言板上的便利貼。寫上去 → 一直看得到，直到誰把它撕掉換新的。

NES 內部有大量 latch：
- PPU 的 `w` toggle latch（決定 `$2005`/`$2006` 是寫第一次還是第二次）
- 手把的 strobe latch
- Sprite 0 hit、sprite overflow 的狀態 latch
- Mapper 內部的 bank-select latch

模擬器要把每個 latch 表示成一個 boolean 或 byte 變數，並在正確時機更新。

---

## 13. PRG / CHR：兩種卡匣 ROM

NES 卡匣裡有兩種 ROM：

- **PRG ROM**（Program ROM）：給 CPU 用的指令跟資料。「食譜書」。
- **CHR ROM**（Character ROM）：給 PPU 用的圖像 pattern。「貼紙的版型」。

**為什麼分開？** 因為 NES 的 CPU bus 跟 PPU bus 是**物理分開的兩條輸送帶**！CPU 從一條 bus 讀 PRG，PPU 從另一條 bus 讀 CHR，**真正能同時進行**。這是 NES 能在 8-bit 時代就跑得動 60fps 動作遊戲的關鍵設計。

少數遊戲沒有 CHR ROM，而是用 **CHR RAM**（卡匣含 RAM 而非 ROM）—— CPU 透過 PPU register `$2007` 把圖像資料寫進 CHR RAM，這樣同一塊圖片區可以在遊戲中改變內容（例如《薩爾達》的 HUD 字型）。

---

## 14. Mapper：卡匣裡的「擴充硬體」

NES CPU 只能定址 64KB（其中只有 32KB 給卡匣），但《超級瑪利歐 3》是 384KB ROM。怎麼放進去？

**Mapper** 是卡匣上的小型晶片，負責「讓 CPU 看到一個 32KB 的窗口，但這個窗口可以**滑到 ROM 的不同位置**」。換句話說，mapper 是個 **bank switcher**。

Mapper 比喻：書架上有一本 384 頁的食譜，但主廚的書桌只能放 32 頁。Mapper 是個書僮，可以隨時把書桌上的 32 頁換成另外的 32 頁。

主廚怎麼指揮書僮？**寫某個特定的記憶體位址**（例如 `$8000`–`$FFFF` 範圍內）。寫值不是寫到那個位址 —— 是給 mapper 一個**換頁命令**。

不同 mapper 命令格式不同：
- **NROM (Mapper 0)**：沒有 mapper，沒得換頁。32KB 直接擺在那。
- **UNROM (Mapper 2)**：寫 `$8000`–`$FFFF` 任何地方都換頁，寫的值是頁碼。
- **CNROM (Mapper 3)**：類似 UNROM，但換的是 CHR 頁不是 PRG 頁。
- **MMC1 (Mapper 1)**：要用 5 次寫入慢慢餵一個 5-bit 命令進去（serial protocol）。
- **MMC3 (Mapper 4)**：含 IRQ counter，可以「每條 scanline 提醒一次主廚」。

詳細的 Mapper 解析在 11–15 章。

---

## 15. 為什麼 Cycle Accuracy 重要？

對 8-bit 主機來說，「一條指令到底花幾個 cycle」會直接影響行為，因為：

1. **賽跑現象**：CPU 寫 PPU register 時，PPU 已經跑到某個 dot。差一個 cycle，可能寫到不同的 PPU 內部狀態。例如《Battletoads》的 sprite 0 hit 偵測對時序極敏感。
2. **DMC 偷 cycle**：如前所述，DMC 會在不確定時機偷走 CPU cycle。模擬器如果沒精確處理，遊戲音樂播放時其他邏輯會錯亂。
3. **MMC3 IRQ**：MMC3 mapper 在 PPU 讀某個 pattern table 時觸發 IRQ。這個觸發時機如果差了 1 dot，整條 scanline IRQ 都跑掉。

近似的 frame-based 或 scanline-based 模擬器可以跑大部分遊戲，但要過 **blargg test ROMs** 或 **AccuracyCoin** 這類精度測試，必須做到 cycle-accurate（甚至 dot-accurate）。

更詳細的討論可見另一篇 [NES 模擬器 Timing 模型對照指南](../nes_emulator_timing_models_guide_zh.md)。

---

## 16. 為什麼 NES 模擬器不能「只寫一個 6502 直譯器」？

如果只寫 6502 直譯器，你能讀懂 ROM 內所有 CPU 指令、能正確跑出 register 跟 RAM 結果。但是：

- 沒人畫畫面（沒 PPU）
- 沒人放音樂（沒 APU）
- 寫到 `$2007` 沒反應，CHR ROM 進不到 PPU
- 卡匣換頁沒處理，遊戲跑到第 32KB 後崩潰
- VBlank NMI 沒觸發，遊戲卡在「等待 VBlank」的迴圈裡

**結論**：模擬器是模擬「整台主機」，CPU 只是其中一個元件。你必須讓 CPU、PPU、APU、DMA、Mapper 在同一條時間線上正確互動。

---

## 17. 簡單對照給已會寫程式的人

| 你熟悉的概念 | NES 上對應 |
|---|---|
| 函式指標表 | CPU bus dispatch（mem read/write 函式表）|
| Class instance variable | CPU register / PPU register |
| Function call | JSR + RTS |
| OS interrupt handler | NMI / IRQ vector |
| Memory-mapped file | PRG ROM 從卡匣 「對映」到 CPU 位址 |
| Hash table 的 chaining | Mirroring |
| 多執行緒共享記憶體 | CPU + PPU 共享 OAM DMA bus |
| Mutex | 沒有（NES 是單執行緒，但有 cycle stealing 互鎖）|
| Stack（程式語言層的）| 6502 的 SP 指向 `$0100`–`$01FF` 那塊 |
| Bit field | Status register P 的 7 bits |

---

## 18. 接下來讀什麼

讀完這篇後，回頭從 02 章繼續看會比較順：

- [02 模擬器要懂的硬體概念](02_hardware_concepts_for_emulator.md) — 把上面的概念套用到具體的模擬器架構
- [04 CPU bus 跟 memory map](04_cpu_bus_and_memory_map.md) — 怎麼把 dispatch 寫成程式
- [05 6502 CPU 核心](05_6502_cpu_core.md) — 暫存器、旗標、addressing mode、per-cycle opcode

如果你之後寫 6502 解碼遇到 opcode 不知道怎麼處理，去翻 [A2 6502 完整 256 opcode 實作參考](A2_6502_opcode_reference.md)。

---

## 重點整理

1. 把電腦想成廚房，每個元件都有對應的角色。
2. CPU 透過 bus 跟外面打交道；bus 由位址 + 資料兩部分組成。
3. NES 的 64KB 位址空間不是 64KB 真記憶體 —— 是大樓平面圖，含 RAM、I/O、卡匣三大區塊。
4. Memory-mapped I/O 讓主廚以為在寫記憶體，其實是在按硬體開關。
5. Interrupt（NMI/IRQ）是廚房的門鈴跟火警鈴 —— 打斷主廚的流程。
6. CPU 跟 PPU 真的同時在跑，模擬器最大的工作是讓兩條時間線對齊。
7. Mapper 是卡匣的 bank switcher，讓有限的 CPU 視窗能看到大容量 ROM。
8. Cycle accuracy 重要，因為 NES 上很多硬體互動的結果取決於發生在哪個 cycle。
