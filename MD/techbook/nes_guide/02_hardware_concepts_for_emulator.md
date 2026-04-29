# 02 寫模擬器前必懂的硬體觀念

## 這章要解決什麼問題

NES 模擬器的程式碼會大量出現遮罩、移位、鏡像、open bus、latch、DMA、IRQ、NMI、cycle 等字眼。這些不是實作細節，而是硬體本來的運作方式。

本章整理後續會反覆使用的硬體觀念。

## NES 硬體觀念

### Bit field

NES register 常常是一個 byte 裡每個 bit 都有不同意義。

**生活比喻**：想像一個 8 鍵的儀表板開關面板。每個開關代表獨立功能：1 號開關控制電燈、2 號控制風扇、3 號控制冷氣模式 1/2…。一次撥動整片面板 (一個 byte)，但每個開關 (bit) 各自獨立。

例如 PPU `$2000`（PPUCTRL）這個 byte 同時控制 7 個獨立功能：

```text
bit 7  NMI enable           ← 「VBlank 來時要不要打斷 CPU」
bit 6  master/slave         ← NES 上接地不用
bit 5  sprite size           ← 8x8 還是 8x16
bit 4  background pattern table  ← BG 用 $0000 還是 $1000 那塊 CHR
bit 3  sprite pattern table  ← Sprite 用 $0000 還是 $1000
bit 2  VRAM increment        ← $2007 後位址 +1 還是 +32
bit 1  base nametable hi bit
bit 0  base nametable lo bit
```

寫這個 byte 等同於一次調整 7 個無關的設定。emulator 程式碼會大量出現位元測試與位元組合：

```csharp
NMIable = (value & 0x80) != 0;            // 取 bit 7
VramaddrIncrement = (value & 0x04) != 0 ? 32 : 1;  // 取 bit 2
SpritePatternTable = (value & 0x08) >> 3;  // 取 bit 3 的 0/1
```

**為什麼要這樣設計？** NES 的 CPU 只能定址 64 KB，把多個布林控制壓進同一個 byte 是省 register 位址的標準做法。當年的 8-bit 主機 register 數量有限，必須這樣節省。

### Address bus 與 data bus

CPU 對外溝通時，通常是：

1. address bus 放出地址。
2. read/write pin 表示讀或寫。
3. data bus 傳送一個 byte。

如果地址對應 RAM，就讀寫 RAM。如果地址對應 PPU register，就觸發 PPU register 的行為。

**生活比喻**：想像一棟 65536 間房間的大樓，主廚要從某間房間拿東西：
1. 把房間號碼寫在小紙條上 → **位址匯流排** (16 條線指向 0–65535 任一房間)
2. 在紙條畫個 ✓ (讀) 或 ✗ (寫) → **R/W 控制線**
3. 紙條塞進輸送帶送出去，回來的會帶著一袋米 → **資料匯流排** (8 條線一次傳 1 byte)

關鍵是：**位址跟資料是兩條獨立的線路**。位址 16 條 → 可以指 0–65535；資料 8 條 → 一次只能搬 1 byte。

```
        16 條 address line
CPU ──────────────────────►  AddressDecoder
                                 │
                                 ├── 0x0000-0x1FFF  → RAM 晶片
                                 ├── 0x2000-0x3FFF  → PPU 晶片
                                 ├── 0x4000-0x401F  → APU + IO
                                 └── 0x8000-0xFFFF  → 卡匣
                                 
        8 條 data line (雙向)
CPU ◄═════════════════════►  被選中的晶片
```

**為什麼模擬器要關心這個？** 因為 NES 上很多硬體狀態（例如 open bus、DMA cycle stealing、controller serial read）的行為都取決於「**最後一次 bus 上的值是什麼**」。模擬器必須記下這個「bus 殘留值」（AprNes 的 `cpubus` 變數），不能只把 bus 當成抽象的函式呼叫。

### Memory-mapped I/O

NES 沒有獨立的 I/O 指令。CPU 用一般 memory read/write 控制硬體：

```text
$2000-$2007  PPU registers
$4000-$4013  APU channel registers
$4014        OAM DMA
$4015        APU status
$4016        Controller strobe / read
$4017        APU frame counter / controller 2 read
```

讀寫這些地址會有副作用。例如讀 `$2002` 會影響 VBlank 與 write latch，寫 `$4014` 會啟動 OAM DMA。

### Mirroring

Mirroring 是多個地址對應同一份實體硬體。

CPU 內建 RAM 只有 2KB，卻出現在 `$0000-$1FFF`：

```text
$0000-$07FF  actual RAM
$0800-$0FFF  mirror
$1000-$17FF  mirror
$1800-$1FFF  mirror
```

因此讀寫 CPU RAM 時常用 `addr & 0x7FF`。

PPU register 也每 8 bytes 鏡像一次，因此 `$2008` 等同 `$2000`，`$3FFF` 以前都會反覆映射到 `$2000-$2007`。

### Latch

Latch 是硬體裡暫存狀態的概念。CPU 寫入 register 後，結果不一定是普通變數立即被完整更新。

**生活比喻**：想像 PPU 的 `$2005` 暫存器是門口的「**訪客留言板**」。它有兩面 —— 第一個訪客寫下時翻到正面（X scroll），第二個訪客寫下時翻到反面（Y scroll）。誰來寫不重要，**「現在面朝哪一面」才重要**。這個「現在朝哪面」由 PPU 內部的 1-bit `w` toggle 決定。

```text
[w = 0]  CPU 寫 $2005 ──→ 寫到 X scroll；w 翻到 1
[w = 1]  CPU 寫 $2005 ──→ 寫到 Y scroll；w 翻到 0
```

PPU `$2005` 跟 `$2006` 都共用這個 toggle。所以順序很重要：

- 第一次寫 `$2005` → horizontal scroll (X)
- 第二次寫 `$2005` → vertical scroll (Y)
- 第一次寫 `$2006` → VRAM address high byte
- 第二次寫 `$2006` → low byte 並排程更新 address

**最容易踩雷的地方**：讀 `$2002` 會把 `w` toggle **重置成 0**！如果遊戲在寫 `$2005` 之間意外讀了 `$2002`，下次寫入會被當成「第一次寫」處理，造成 scroll 錯亂。

實作時，模擬器要把這個 toggle 表示成一個 boolean，並在所有相關 register 的讀寫都正確更新：

```csharp
bool w;        // PPU 內部 toggle
ushort t, v;   // temporary / current VRAM address
byte fineX;

void Write_2005(byte value) {
    if (!w) { t = (t & 0x7FE0) | (value >> 3);  fineX = value & 7;  w = true; }
    else    { t = (t & 0x0C1F) | ((value & 7) << 12) | ((value & 0xF8) << 2);  w = false; }
}

void Read_2002() {
    // ... 讀 status ...
    w = false;  // ★ 重置 toggle
}
```

### Open bus

Open bus 指 data bus 沒有被新的硬體值主動驅動時，讀到殘留值。這會影響一些測試 ROM 與特殊遊戲行為。

**生活比喻**：你對著電話講「請給我 X 號房間的東西」，但 X 號根本沒接電話。電話線不會自動回 0 或回錯誤碼 —— 你會聽到上一通電話最後一句話的回音 (因為電容效應，bus 上的電壓會殘留一陣)。下次有人主動講話前，回音就是 bus 的當前值。

具體例子：

```
CPU 讀 $1234 (RAM)        → bus 取值 $42
CPU 讀 $401F (沒接硬體)   → 仍會回 $42 (剛剛的殘留)
CPU 讀 $2002 (PPU status) → bus 上 5 個低位 bit 是 open bus，
                              只有高 3 bit (VBlank/Sprite0/Overflow) 會被覆寫
```

**為什麼某些 register 只「部分覆寫」？** 因為硬體上 PPU `$2002` 只把 3 個 bit 接到 data bus；其他 5 個 bit 的線路是浮接的，所以那 5 bit **保留之前的 bus 殘留值**。模擬器要做：

```csharp
byte Read_2002() {
    byte status = (vblankFlag ? 0x80 : 0) | (spr0Hit ? 0x40 : 0) | (sprOverflow ? 0x20 : 0);
    return (byte)((status & 0xE0) | (cpubus & 0x1F));  // 高 3 bit 是真值，低 5 bit 用殘留
}
```

AprNes 中可以看到 `openbus` 與 `cpubus`：

- `openbus`：PPU 相關的 bus 殘留。
- `cpubus`：CPU data bus 最近值。

**會考的測試 ROM**：`ppu_open_bus.nes`（blargg）會檢查每個 PPU register 的 open bus 行為。寫對了你會在那條測試 ROM 看到 PASS。

### Clock 與 cycle

不要把所有「cycle」混在一起。

- master clock：整台機器的基準時脈。
- CPU cycle：CPU 一次 bus cycle 或內部步進。
- PPU dot：PPU 畫面管線的一個像素時序。
- APU step：音訊硬體更新一次。

NTSC NES 中，PPU 大約每 CPU cycle 前進 3 dots。AprNes 更進一步用 master clock gate 描述各硬體在哪個 phase 動作。

### IRQ 與 NMI

Interrupt 是硬體請 CPU 暫停目前流程，跳去執行 interrupt handler。

**生活比喻**：

- **NMI = 火警鈴**。響起來主廚必須立刻放下鍋鏟，跑樓梯出去。沒有「我現在很忙等等再說」的選項。
- **IRQ = 電話響**。如果你掛著「請勿打擾」牌（CPU 的 `I` flag = 1），電話響了你也不接。等你拿掉牌子（`CLI` 指令），下次響才接。

NES 上：

| 訊號 | 來源 | 可不可以遮罩 | NES 上的用途 |
|---|---|---|---|
| **NMI** | PPU 每幀 VBlank 一次 | ❌ 不能 | 通知遊戲「現在可以安全更新畫面了」 |
| **IRQ** | APU frame counter / DMC / mapper IRQ counter | ✅ 看 `I` flag | 計時、scanline 同步、自訂事件 |
| **Reset** | 玩家按 reset 鈕、開機 | — | 跳到 `$FFFC-$FFFD` 指定的開機位址 |

當中斷發生，CPU 不會在指令中途瞬間跳走 —— 它會：
1. 把當前的 `PC` (回家位置) 跟 `P` (狀態旗標) 推到 stack
2. 設 `I = 1`（避免 IRQ handler 自己又被打斷）
3. 從對應的 vector 讀位址，跳過去
4. handler 結束後用 `RTI` 把 `PC` 跟 `P` 還原

```text
NMI vector  : $FFFA-$FFFB
Reset vector: $FFFC-$FFFD
IRQ vector  : $FFFE-$FFFF
```

CPU 不是任意瞬間都跳中斷，而是在特定 instruction boundary 輪詢中斷狀態。**精確時機**：6502 在每條指令的「倒數第二個 cycle」採樣中斷線（這叫 **edge sampling**）。模擬器寫到 cycle-accurate 程度時，必須在這個正確的 cycle 採樣，否則會錯過或重複觸發。

**NMI 的常見用法**：遊戲在 main loop 裡迴圈等 NMI，每次 NMI 觸發就更新畫面：
```assembly
main_loop:
    JSR  game_logic     ; 跑遊戲邏輯
    LDA  vblank_flag    ; main loop 等 NMI 把這個 flag 設成 1
    BEQ  main_loop      ;
    LDA  #0
    STA  vblank_flag
    JSR  update_screen  ; 在 VBlank 期間安全寫 PPU
    JMP  main_loop

nmi_handler:
    LDA  #1
    STA  vblank_flag    ; 通知 main loop
    RTI
```

### DMA

DMA 是硬體接管 bus 搬資料。OAM DMA 會把 CPU memory 的 256 bytes 搬到 PPU OAM。DMC DMA 會讀取 sample byte。

DMA 不是普通 `Array.Copy`，因為它會消耗 CPU bus cycle，並與 CPU read/write phase 互動。

## 初學者簡化模型

第一版可以這樣處理：

- RAM mirroring 用 `addr & mask`。
- PPU/APU/IO 先做最常用 register。
- open bus 先回傳上一個 bus value。
- DMA 先用 cycle count 阻塞 CPU。
- IRQ/NMI 先在 instruction 結束時檢查。

等遊戲能跑，再逐步逼近 AprNes 的 per-cycle 行為。

## AprNes / NesCore 實作對照

- `CPU.cs`
  - `CpuRead()` / `CpuWrite()` 設定 `cpuBusAddr`, `cpuIsRead`, `cpubus`。
  - `PollInterrupts()` 在 instruction 完成前輪詢 NMI/IRQ。
- `MEM.cs`
  - `Read_NesRam()` 用 `addr & 0x7FF` 處理 RAM mirror。
  - `DmaOneCycle()` 每次只執行一個 DMA cycle。
  - `DmaFetch()` 處理 DMA 讀取、open bus 與 APU/joypad bus conflict。
- `PPU.cs`
  - `vram_latch`, `ppu_2007_buffer`, `openbus`。
  - `$2005/$2006/$2007` 都有延遲或 pipeline 行為。
- `IO.cs`
  - 把 CPU 對 `$2000-$4017` 的讀寫導向 PPU/APU/JoyPad。

## 常見錯誤

- 把 PPU register 當普通陣列。
- 忽略 mirror，導致遊戲讀寫錯位址。
- 在 CPU 寫 `$2006` 後立即更新所有 PPU 內部狀態，忽略延遲。
- 用簡單布林值代表 IRQ，卻沒有區分 IRQ line current 與 CPU 已取樣狀態。
- 把 DMA 寫成瞬間複製，完全不影響 CPU timing。

## 本章重點整理

1. NES 透過 memory-mapped I/O 控制硬體。
2. Bus、latch、open bus、DMA 都會產生可觀察行為。
3. AprNes 的許多複雜度都是為了讓這些硬體細節出現在正確時序。

## 下一章銜接

下一章進入 ROM 載入，介紹 `.nes` 檔案、iNES header、PRG ROM、CHR ROM、Mapper 編號與 AprNes 的初始化流程。
