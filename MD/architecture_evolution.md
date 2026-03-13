# AprNes 架構發展紀錄：從初版到 Cycle-Accurate 的演進之路

本文記錄 AprNes 模擬器從 2016 年初版到 2026 年 cycle-accurate 精度的完整架構演進歷程，
包含每個階段的時序模型變化、設計決策、以及相關的 NES 硬體知識教學。

---

## 目錄

1. [NES 硬體時序基礎知識](#1-nes-硬體時序基礎知識)
2. [第一階段：初版 (2016)](#2-第一階段初版-2016)
3. [第二階段：PPU Cycle-Accurate 改寫 (2026-02-19)](#3-第二階段ppu-cycle-accurate-改寫-2026-02-19)
4. [第三階段：VBL/NMI 時序模型 (2026-02-21)](#4-第三階段vblnmi-時序模型-2026-02-21)
5. [第四階段：APU IRQ 與 CPU 中斷時序 (2026-02-22)](#5-第四階段apu-irq-與-cpu-中斷時序-2026-02-22)
6. [第五階段：Sprite 逐 Dot 精度 (2026-02-22)](#6-第五階段sprite-逐-dot-精度-2026-02-22)
7. [第六階段：DMC DMA Cycle Stealing (2026-02-22)](#7-第六階段dmc-dma-cycle-stealing-2026-02-22)
8. [第七階段：AccuracyCoin 挑戰 (2026-03-06~03-10)](#8-第七階段accuracycoin-挑戰-2026-0306-0310)
9. [第八階段：Per-Cycle CPU 重寫 (2026-03-10)](#9-第八階段per-cycle-cpu-重寫-2026-03-10)
10. [時序模型總覽](#10-時序模型總覽)
11. [測試成績進展](#11-測試成績進展)

---

## 1. NES 硬體時序基礎知識

理解 AprNes 的演進需要先了解 NES 的基本時序架構。

### 1.1 Master Clock 與子系統時脈

NES 的核心是一個 **21.477272 MHz** 的 master clock，各子系統以不同除頻倍率運作：

```
Master Clock: 21.477272 MHz
  ├── CPU (RP2A03): ÷12 = 1.789773 MHz  → 每 12 master clocks 執行 1 CPU cycle
  ├── PPU (RP2C02): ÷4  = 5.369318 MHz  → 每 4 master clocks 執行 1 PPU dot
  └── APU:          ÷24 = 894886.5 Hz   → 每 2 CPU cycles 執行 1 APU cycle
```

**重要比例**：1 CPU cycle = 3 PPU dots。這個比例是模擬器時序的基石。

### 1.2 PPU 畫面結構

PPU 產生 NTSC 訊號的掃描線結構：

```
Scanline  0-239:  可見畫面（256 dots 繪圖 + 85 dots HBlank = 341 dots/line）
Scanline  240:    Post-render（空閒）
Scanline  241:    VBlank 開始（dot 1 設置 VBL flag，可觸發 NMI）
Scanline 242-260: VBlank 期間（CPU 可自由存取 VRAM）
Scanline  261:    Pre-render（dot 2 清除 VBL flag；準備下一幀）
```

每幀 = 262 scanlines × 341 dots = **89,342 PPU dots** = **29,780.67 CPU cycles**。

### 1.3 CPU 指令與 Tick

6502 CPU 每條指令消耗 2-7 CPU cycles。每個 cycle 進行一次記憶體存取（read 或 write）。
在模擬器中，如何在這些存取之間推進 PPU 和 APU，就是「時序模型」的核心問題。

### 1.4 中斷機制

NES 有兩種中斷：

- **NMI (Non-Maskable Interrupt)**：PPU 在 VBlank 開始時發出，通知 CPU 可以更新畫面
- **IRQ (Interrupt Request)**：APU frame counter 或 mapper（如 MMC3）發出，可被 CPU 遮蔽

中斷的精確觸發時機是許多測試 ROM 驗證的重點。

---

## 2. 第一階段：初版 (2016)

**Git**: `e19d1b6` (2016-10-26)
**架構**: 最簡單的「逐幀」模型

### 2.1 初版時序模型

```
┌─────────────────────────────────────┐
│          初版執行模型                 │
│                                     │
│  while (running) {                  │
│      CPU.ExecuteOneInstruction();   │
│      PPU.CatchUp(cpu_cycles * 3);  │  ← 整條指令跑完才推 PPU
│      APU.Step();                    │
│  }                                  │
└─────────────────────────────────────┘
```

特點：
- CPU 一次跑完一整條指令（2-7 cycles），然後批量推進 PPU
- PPU 以逐 scanline 批量渲染（不是逐 dot）
- 沒有中斷時序模擬（NMI 直接在 scanline 241 觸發）
- 沒有 DMC DMA 模擬
- 支援 Mapper 0/1/2/3/4

### 2.2 侷限

這個模型對大多數遊戲已夠用，但無法通過任何 timing-sensitive 的測試 ROM。
主要問題：
- PPU 寄存器的讀寫無法在正確的 dot 位置生效
- NMI/IRQ 觸發時機不精確
- Mapper IRQ（如 MMC3 scanline counter）無法正確計數

---

## 3. 第二階段：PPU Cycle-Accurate 改寫 (2026-02-19)

**Git**: `24687f0` ~ `be3f979`
**BUGFIX**: BUGFIX2 (Bug 10-14)
**成就**: 建立逐 dot PPU 渲染管線

### 3.1 動機

2026 年 2 月，專案從「能跑遊戲」轉向「通過測試 ROM」。
第一步就是讓 PPU 從批量渲染改為逐 dot 渲染。

### 3.2 Tick-on-Access 模型

引入了 AprNes 的核心時序機制——**每次記憶體存取推進 3 PPU dots**：

```
┌─────────────────────────────────────────────┐
│          Tick-on-Access 模型                  │
│                                             │
│  Mem_r(addr) {                              │
│      tick();              // 推進 3 PPU dots │
│      return read(addr);   // 執行讀取        │
│  }                                          │
│                                             │
│  tick() {                                   │
│      ppu_step_new();      // PPU dot 1      │
│      ppu_step_new();      // PPU dot 2      │
│      ppu_step_new();      // PPU dot 3      │
│      apu_step();          // APU 半 cycle    │
│  }                                          │
└─────────────────────────────────────────────┘
```

每條 CPU 指令的每個 cycle（Mem_r 或 Mem_w）都會呼叫 `tick()`，
確保 PPU 在 CPU 的每個記憶體存取之間精確推進 3 dots。

### 3.3 PPU 渲染管線

PPU 的 tile fetch 改為 8-cycle pipeline（每 tile 8 dots）：

```
dot 0: Nametable byte fetch
dot 2: Attribute table byte fetch
dot 4: Pattern table low byte fetch
dot 6: Pattern table high byte fetch
dot 7: Load shift registers + render pixel
```

引入 16-bit shift register (highshift/lowshift) 與 3-stage attribute pipeline，
取代原本的批量渲染。

### 3.4 教學：為什麼 Tick-on-Access？

在真實 NES 中，CPU、PPU、APU 是同時並行運作的。但在軟體模擬器中，
我們只有一個執行緒。Tick-on-Access 是一種「懶惰同步」策略：

> **不主動推進 PPU 時間，而是在 CPU 每次存取記憶體時順便推進。**

因為 6502 每個 cycle 必定有一次記憶體存取（read 或 write），
所以 Mem_r/Mem_w 就是最自然的同步點。

優點：實作簡單、效率高（不需要事件排程器）。
缺點：PPU 的觀察粒度被限制在「每 3 dots」，無法模擬 sub-cycle 行為。

---

## 4. 第三階段：VBL/NMI 時序模型 (2026-02-21)

**Git**: `7671455`
**BUGFIX**: BUGFIX5, BUGFIX9-11
**成就**: 154 PASS / 20 FAIL (+15)

### 4.1 1-Cycle NMI Delay Model

真實 NES 的 NMI 不是瞬間觸發的。PPU 在 scanline 241, dot 1 設置 VBL flag，
但 NMI 要到**下一個 CPU cycle** 才會被 CPU 偵測到。

```
┌──────────────────────────────────────────────────┐
│                NMI 時序流程                        │
│                                                  │
│ PPU dot: ... → sl=241,cx=1 → ...                 │
│                    │                              │
│                    ▼                              │
│              設置 VBL flag                         │
│              設置 nmi_delay = true                 │
│                                                  │
│ 下一次 tick() 開頭:                                │
│              nmi_delay → nmi_pending              │
│              (promote: 延遲 1 cycle 後生效)        │
│                                                  │
│ CPU 檢查: nmi_pending == true → 觸發 NMI          │
└──────────────────────────────────────────────────┘
```

### 4.2 $2002 讀取的取消機制

如果 CPU 在 VBL flag 剛設置的同一 cycle 讀取 $2002：
- 讀到 VBL flag = 1（已設置）
- **清除 nmi_delay**（NMI 被取消，因為還沒 promote 到 nmi_pending）

但如果 nmi_delay 已經 promote 為 nmi_pending，就無法取消了。
這個「1-cycle 窗口」是 `ppu_vbl_nmi` 測試套件的核心測試點。

### 4.3 教學：為什麼需要延遲？

在真實硬體中，PPU 和 CPU 是異步運行的。PPU 拉低 NMI 線後，
CPU 要等到下一個 phi2（CPU 時脈上升沿）才能偵測到。這大約就是 1 CPU cycle 的延遲。

不實作這個延遲的話，10 個 `ppu_vbl_nmi` 測試全部 FAIL。
實作後，一次性通過 15 個測試。

---

## 5. 第四階段：APU IRQ 與 CPU 中斷時序 (2026-02-22)

**Git**: `dd044d1` (APU IRQ), `1dd9024` (CPU 中斷)
**BUGFIX**: BUGFIX12-13 (APU), BUGFIX18 (CPU 中斷)
**成就**: 158 → 169 PASS

### 5.1 APU Frame Counter IRQ

APU 的 frame counter 在 4-step 模式下，第 4 步會產生 IRQ。
關鍵是 IRQ 的「assert 持續時間」和「清除時機」：

```
Frame Counter 4-step Mode:
  Step 0: Envelope + Triangle linear counter
  Step 1: Envelope + Length counter + Sweep
  Step 2: Envelope + Triangle linear counter
  Step 3: Envelope + Length counter + Sweep + IRQ assert ← 這裡！

IRQ assert 持續 ~3 APU cycles（step 3 本身 + 2 post cycles）
$4017 寫入或 $4015 讀取可清除 IRQ flag
```

### 5.2 CPU 中斷取樣：Penultimate Cycle

6502 CPU 在**倒數第二個 cycle**（penultimate cycle）取樣 IRQ/NMI 線的狀態。

```
┌──────────────────────────────────────────────┐
│  6502 指令中斷取樣時機                          │
│                                              │
│  假設指令有 N 個 cycles:                       │
│                                              │
│  Cycle 1: opcode fetch                       │
│  Cycle 2: operand fetch                      │
│  ...                                         │
│  Cycle N-1: ← IRQ/NMI 在此取樣 ←             │
│  Cycle N: 最後一個 cycle                       │
│                                              │
│  如果 Cycle N-1 時 IRQ line 為 low:           │
│  → 下一條指令被替換為 IRQ handler              │
└──────────────────────────────────────────────┘
```

**實作方式** (BUGFIX18)：
- 在每次 `tick()` 結尾記錄 `irqLinePrev = irqLineCurrent`
- CPU 在指令結尾檢查 `irqLinePrev`（自然捕獲倒數第二個 cycle 的狀態）

### 5.3 NMI Deferral（中斷延遲觸發）

BRK/IRQ/NMI 的 handler 執行期間如果又偵測到 NMI 上升沿，
NMI 不會立即巢狀觸發，而是**延遲到下一條指令後**才觸發：

```
正常: NMI detected → 立即進入 NMI handler
延遲: BRK/IRQ 進行中 + NMI detected → 設 nmi_just_deferred
      → 當前 handler 完成 → 下一條指令完成 → 才觸發 NMI
```

### 5.4 OAM DMA 與 IRQ 隔離

OAM DMA（$4014 寫入）會 steal 513-514 CPU cycles。
在 DMA 期間，IRQ line 的狀態變化不應影響 DMA 結束後的中斷判斷：

```
DMA 開始前: save irqLinePrev
DMA 執行: 513-514 個 tick()，irqLinePrev 被改來改去
DMA 結束後: restore irqLinePrev（回到 DMA 前的狀態）
```

---

## 6. 第五階段：Sprite 逐 Dot 精度 (2026-02-22)

**Git**: `5461fe7`
**BUGFIX**: BUGFIX17 (Sprite 0 Hit + Overflow)
**成就**: 165 PASS / 9 FAIL (+4)

### 6.1 Sprite 0 Hit 逐 Pixel 偵測

Sprite 0 Hit 是 PPU 的重要功能——當 sprite 0 的不透明像素與 BG 的不透明像素重疊時，
$2002 bit 6 被設置。遊戲用它做 split-screen 效果。

```
原本: 在 dot 257 批量檢查整條 scanline（不精確）
改後: 在 ppu_step_new() 逐 dot 檢查（dot 2-255）

bool CheckSprite0Hit(int dot) {
    if (dot < 2 || dot > 255) return false;
    if (sprite0_pixel[dot] != transparent && bg_pixel[dot] != transparent)
        return true;  // hit!
}
```

### 6.2 Sprite Overflow 硬體 Bug

NES 的 sprite overflow 偵測有一個著名的硬體 bug：
當找到 8 個 sprite 後，評估第 9 個 sprite 時，
byte offset `m` 不會歸零，而是繼續遞增 (0→1→2→3)。

```
正常評估: 比較 OAM[n*4 + 0] (Y 座標)
Bug 觸發後: 比較 OAM[n*4 + m]，m 每個 sprite 遞增
  sprite 9:  比較 Y     (m=0，碰巧正確)
  sprite 10: 比較 tile  (m=1，錯誤！)
  sprite 11: 比較 attr  (m=2，錯誤！)
  sprite 12: 比較 X     (m=3，錯誤！)
  sprite 13: 比較 Y     (m=0 wrap，又正確)
  ...
```

### 6.3 後續: Secondary OAM FSM (BUGFIX47, 2026-03-08)

AccuracyCoin 測試要求更精確的 sprite evaluation：

```
dots  1-64:  清除 secondary OAM (每 2 dots 寫入 $FF)
dots 65-256: 逐 dot 評估 primary OAM
  odd dot:   讀取 primary OAM[oamAddr] → oamCopyBuffer
  even dot:  寫入 oamCopyBuffer → secondary OAM（若 in-range）
dot  256:    finalize（設定 sprite 0 flag）
dot  257:    PrecomputePreRenderSprites()（為下一 scanline 準備）
```

$2004 讀取在 rendering 期間回傳 `oamCopyBuffer`（而非 primary OAM），
這個細節是 AccuracyCoin 的 SprSL0 和 $2004 Stress Test 的測試重點。

---

## 7. 第六階段：DMC DMA Cycle Stealing (2026-02-22)

**Git**: `f3188b9`
**BUGFIX**: BUGFIX19
**成就**: 171 PASS / 3 FAIL (+2)

### 7.1 DMC DMA 機制

APU 的 DMC 聲道需要從記憶體讀取 sample data。
這個讀取不是由 CPU 指令執行的，而是由 DMA 單元「偷取」CPU cycles：

```
DMC sample fetch:
  Cycle 1: Halt（暫停 CPU）
  Cycle 2: Alignment（等待正確的 read/write phase）
  Cycle 3: Dummy read（phantom read，用 CPU 當前 bus 地址）
  Cycle 4: Sample read（從 DMC sample address 讀取 byte）

共 3-4 cycles，取決於 DMA 觸發時 CPU 正在做 read 還是 write
```

### 7.2 dmc_stolen_tick()

AprNes 實作了 `dmc_stolen_tick()`，它與 `tick()` 相同但繞過 `in_tick` 重入保護。
這是因為 DMC DMA 發生在 `tick()` 內部（由 APU step 觸發），
需要在已經進入 tick 的情況下再推進時間。

### 7.3 Phantom Read

DMC DMA 的 dummy cycle 會在 CPU 的 bus 上產生一次「幽靈讀取」。
如果 CPU 當時的 bus 地址指向 PPU 寄存器（如 $2007），
這個 phantom read 會產生實際的副作用！

```
例: CPU 正在執行 LDA $2007,X (page cross case)
  Cycle 3: Mem_r($2007) → PPU read buffer swap, vram_addr++
  此時 DMC DMA 觸發:
  Stolen cycle 3: phantom read $2007 → 又一次 buffer swap!
  Cycle 4: Mem_r($2107) → mapped to $2007 → 第三次 read
```

這就是 `dmc_dma_during_read4` 測試套件驗證的行為。

---

## 8. 第七階段：AccuracyCoin 挑戰 (2026-03-06~03-10)

**Git**: `7c1a20b` ~ `5af6fdb`
**BUGFIX**: BUGFIX31-52
**成就**: 174/174 blargg + 132/136 AccuracyCoin

AccuracyCoin 是一個極其嚴格的模擬器精確度測試，包含 136 個子項目，
涵蓋 CPU、PPU、APU 各種邊界行為。

### 8.1 Load DMA Parity (BUGFIX31)

DMC DMA 觸發時的延遲取決於 APU 的 get/put cycle parity：

```
原本: 固定延遲 3 cycles
修正: putCycle ? 2 : 3
      (APU cycle 為奇數時延遲 2，偶數時延遲 3)
```

這一個修正讓 blargg 從 171 → 174 全過。

### 8.2 PPU Rendering Enable 延遲 (BUGFIX46)

PPU 的 rendering enable ($2001 bit 3/4) 不是立即生效的，
而是在下一個 PPU dot 才生效（1-dot delay）：

```
ppuRenderingEnabled: 在每個 ppu_step_new() 結尾更新
tile fetch / shift register clocking: 使用 ppuRenderingEnabled（延遲值）
sprite 0 hit: 使用即時的 ShowBackGround/ShowSprites（無延遲）
```

### 8.3 $2002 Flag Clear Timing Stagger (BUGFIX45)

Pre-render line (scanline 261) 清除 PPU flags 的時機不完全相同：

```
dot 1: 清除 Sprite 0 Hit + Sprite Overflow
dot 2: 清除 VBlank flag
```

這 1-dot 的差距反映了真實硬體中 M2 duty cycle 造成的時序錯位。

### 8.4 NMI Delay 改為 Cycle-Based (BUGFIX35)

原本的 `nmi_delay` 是布林值，改為 cycle count：

```
原本: nmi_delay = true → 下次 tick promote
改後: nmi_delay_cycle = cpuCycleCount → 當 cpuCycleCount > nmi_delay_cycle 時 promote
```

更精確地模擬「恰好 1 CPU cycle」的延遲。

---

## 9. 第八階段：Per-Cycle CPU 重寫 (2026-03-10)

**Git**: `533d1d4`
**BUGFIX**: BUGFIX50
**成就**: 174/174 blargg + 126/136 AC (+4)

### 9.1 動機

在 Tick-on-Access 模型中，CPU 一次跑完整條指令，
DMA 只能在指令邊界插入。但真實硬體中，DMA 可以在**任何 read cycle** 插入。

```
原本 (Per-Instruction):                  改後 (Per-Cycle):
┌─────────────────────┐                 ┌─────────────────────┐
│ Execute full instr   │                 │ cpu_step_one_cycle() │
│ Check DMA at end     │                 │   ├── CpuRead()     │
│ Execute full instr   │                 │   │   ├── tick()     │
│ ...                  │                 │   │   ├── CheckDMA() │ ← 每次 read 都能插入 DMA
│                      │                 │   │   └── read data  │
│                      │                 │   └── operationCycle++│
│                      │                 │ cpu_step_one_cycle() │
│                      │                 │   ├── CpuWrite()    │
│                      │                 │   ...               │
└─────────────────────┘                 └─────────────────────┘
```

### 9.2 operationCycle 狀態機

每條 CPU 指令被拆解為多個 cycle，用 `operationCycle` 追蹤進度：

```csharp
// 以 LDA abs,X 為例 (opcode 0xBD, 4-5 cycles)
case 0xBD:
    switch (operationCycle) {
        case 0: CpuRead(PC++); break;           // fetch opcode
        case 1: lo = CpuRead(PC++); break;      // fetch low byte
        case 2: hi = CpuRead(PC++); break;      // fetch high byte
        case 3:                                   // read from addr+X
            addr = (hi << 8) | lo;
            crossed = ((addr & 0xFF) + X) > 0xFF;
            CpuRead((addr & 0xFF00) | ((addr + X) & 0xFF)); // possible wrong page
            if (!crossed) { A = lastRead; SetNZ(A); done = true; }
            break;
        case 4:                                   // page cross: re-read correct addr
            A = CpuRead(addr + X);
            SetNZ(A);
            done = true;
            break;
    }
```

### 9.3 ProcessPendingDma

DMA 檢查現在在每次 `CpuRead()` 內部進行：

```
CpuRead(addr):
    tick()                          // 推進 PPU/APU
    ProcessPendingDma()             // 檢查是否有 pending DMA
    return mem_read_fun[addr]()     // 執行讀取
```

這讓 DMC DMA 能在**指令中途**的任何 read cycle 精確插入，
大幅提升了 DMA 相關測試的通過率。

### 9.4 後續最佳化 (BUGFIX51-52)

- **SH\* Opcodes** (BUGFIX51): 非官方 opcode (SHX/SHY/SHA) 在 DMA 發生時需要特殊處理
- **DMC Cooldown** (BUGFIX52): DMC fetch 完成後 2 cycle 冷卻期，防止立即重新觸發

---

## 10. 時序模型總覽

### 架構演進對照表

| 面向 | 初版 (2016) | Tick-on-Access (02-19) | VBL/NMI (02-21) | Per-Cycle (03-10) |
|------|------------|----------------------|----------------|------------------|
| **CPU 執行** | 逐指令 | 逐指令 | 逐指令 | 逐 cycle |
| **PPU 推進** | 批量 | 每 Mem_r/Mem_w 推 3 dots | 同左 | 同左 |
| **DMA 插入** | 無 | 指令邊界 | 指令邊界 | 任意 read cycle |
| **NMI 時序** | 即時 | 即時 | 1-cycle delay | cycle-based delay |
| **IRQ 取樣** | 無 | 指令結尾 | penultimate cycle | 同左 |
| **Sprite 0 Hit** | 批量 | 批量 | 批量 | 逐 dot |
| **Sprite Eval** | 批量 | 批量 | 批量 | 逐 dot FSM |

### Tick Model 示意圖

```
當前模型 (Per-Cycle + Tick-on-Access):

CPU instruction: LDA $2007,X (page cross, 5 cycles)
                 ┌────────────────────────────────────────────────┐
Cycle 1 (read):  │ tick() → 3 PPU dots → CheckDMA → fetch opcode │
Cycle 2 (read):  │ tick() → 3 PPU dots → CheckDMA → fetch low    │
Cycle 3 (read):  │ tick() → 3 PPU dots → CheckDMA → fetch high   │
Cycle 4 (read):  │ tick() → 3 PPU dots → CheckDMA → dummy read   │
                 │                        ↑ DMC DMA 可在此插入!    │
Cycle 5 (read):  │ tick() → 3 PPU dots → CheckDMA → real read    │
                 └────────────────────────────────────────────────┘
                  PPU dots: |•••|•••|•••|•••|•••| = 15 dots total
```

---

## 11. 測試成績進展

```
日期         blargg    AccuracyCoin    關鍵修正
─────────────────────────────────────────────────────────────────
2016-10-26   ~20/124   —              初版，基本能跑遊戲
2026-02-19   105/154   —              PPU cycle-accurate, APU 音效
2026-02-20   130/154   —              APU init, DMC IRQ, test runner
2026-02-21   139/174   —              APU frame counter timing
2026-02-21   154/174   —              VBL/NMI 1-cycle delay model (+15!)
2026-02-22   156/174   —              MMC3 A12 phase alignment
2026-02-22   158/174   —              APU IRQ + CPU penultimate cycle
2026-02-22   165/174   —              Sprite per-pixel hit + overflow hw bug
2026-02-22   169/174   —              CPU interrupt timing + NMI deferral
2026-02-22   171/174   —              DMC DMA cycle stealing
2026-02-22   172/174   —              PPU $2007 read cooldown
2026-02-22   174/174   —              --pass-on-stable (ALL BLARGG PASS!)
2026-03-06   174/174   118/136        AccuracyCoin Phase 1
2026-03-07   174/174   120/136        PPU rendering enable delay
2026-03-08   174/174   122/136        Secondary OAM FSM
2026-03-10   174/174   126/136        Per-Cycle CPU rewrite
2026-03-10   174/174   131/136        SH* opcodes fix
2026-03-10   174/174   132/136        DMC DMA cooldown
2026-03-13   174/174   133/136        DMC Load DMA countdown timing (BUGFIX53)
2026-03-13   174/174   134/136        DMC DMA bus conflicts + deferred status (BUGFIX54)
2026-03-13   174/174   135/136        Explicit DMA abort (BUGFIX55)
2026-03-14   174/174   136/136        Implicit DMA abort (BUGFIX56) — PERFECT!
```

### 全部完成

所有 174 blargg 測試 + 136 AccuracyCoin 測試全數通過。
P13 DMA 測試的 4 個失敗透過 BUGFIX53-56 逐一修復，
採用 TriCNES 風格的 DMA timing（deferred status、bus conflicts、
explicit/implicit abort 機制），在不改變核心 tick model 的前提下達成。

---

## 附錄：相關文件索引

| 文件 | 說明 |
|------|------|
| `bugfix/` | 所有 Bug 修復的詳細紀錄（根因分析、修改內容、驗證結果） |
| `MD/p13_fix_plan.md` | P13 DMA 修復計畫（方案 A/B 比較） |
| `report/methodology.html` | 測試方法論文件 |
| `report/index.html` | Blargg 測試報告（含截圖） |
| `report/AccuracyCoin_report.html` | AccuracyCoin 測試報告 |
| `report/TriCNES_report.html` | TriCNES 對照測試報告 |
| `CLAUDE.md` | 開發者指南（編譯、測試、架構說明） |
