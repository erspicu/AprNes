# Catch-up 動態同步干擾因素分析

**日期**: 2026-04-09
**目的**: 如果未來以學術研究性質嘗試在 AprNes 現有 Master Clock 模型上加入 Catch-up 機制，需要注意的所有跨子系統干擾因素完整摘要。

---

## 一、核心問題

Catch-up 架構讓 CPU 自由奔跑，只在 I/O 存取時同步 PPU/APU。但 NES 的三個主要子系統之間存在**高頻雙向依賴**，任何預測錯誤（哪怕差 1 個 master clock tick）都可能導致測試回歸。

以下是所有已識別的干擾因素，按子系統分類。

---

## 二、PPU ↔ CPU 干擾因素

### 2.1 PPU 暫存器寫入的延遲管線

CPU 寫入 PPU 暫存器時，效果不是立即生效，而是經過 **alignment-dependent 延遲**：

| 暫存器 | 延遲機制 | 依賴的同步欄位 | Catch-up 風險 |
|--------|---------|---------------|-------------|
| **$2000** | `ppu2000UpdateDelay`（1-2 PPU dot） | `mcPpuClock & 3` 決定延遲值 | NMIable 立即生效但後續被延遲修正覆蓋 |
| **$2001** | 四層旗標系統：Instant → Delayed → EvalDelay | `mcPpuClock & 3` 決定延遲值 | 渲染開關的 4 種時序版本必須各自正確 |
| **$2005** | `ppu2005UpdateDelay`（1-2 PPU dot） | `mcPpuClock & 3` | 捲動位置延遲更新 |
| **$2006** | `ppu2006UpdateDelay`（4-5 PPU dot） | `mcPpuClock & 3` | VRAM 地址延遲複製（最敏感的捲動機制） |
| **$2007** | 多狀態 SM（`ppu2007SM` 0→9） | `mcPpuClock & 3` 影響 mystery write | 讀寫管線跨越多個 dot |

**關鍵依賴**：所有延遲值都讀取 `mcPpuClock & 3`（PPU 的 master clock 相位）。如果 CPU 跑在前面，寫入時的 `mcPpuClock` 值不正確，延遲會差 1-2 dot，破壞所有 scroll 和 VBL timing 測試。

### 2.2 $2002 讀取 — EmulateUntilEndOfRead

`ppu_r_2002()` 內部呼叫 `MasterClockTick()` **7 次**（TriCNES 的 EmulateUntilEndOfRead 模型）。

這是最緊密的耦合點：
- VBL flag 在 7 次 tick **之前**取樣
- Sprite 0 Hit / Overflow 在 7 次 tick **之後**取樣
- `ppu2002ReadPending` 設定後，在下一個 PPU full step 中清除 `isVblank`（VBL 抑制）

**Catch-up 影響**：CPU 讀 $2002 時必須讓所有子系統同步到完全一致的時間點，然後再推進 7 個 master tick。這本質上退化為 Polling。

### 2.3 NMI 多階管線

VBL → NMI 信號經過 5 個階段，跨越 PPU full step 和 half step：

```
pendingVblank (sl=241, cx=0, full step)
  → ppuVSET (half step latch)
    → ppuVSET_Latch1 / Latch2 (full/half step pipeline)
      → isVblank (full step, ~1.5 dot 延遲)
        → NMILine (mcCpuClock==8, master clock phase 8)
          → nmiPinsSignal → edge detection → doNMI (CPU instruction boundary)
```

**Catch-up 影響**：NMI 的精確時間可以從 scanline/dot 預測（sl=241 cx=0 觸發），但管線延遲的 1.5 dot 必須精確計算。如果 CPU 跑過了 NMI 觸發點，已執行的指令無法回退。

### 2.4 Sprite 0 Hit 延遲管線

類似 NMI，經過 3 階段延遲（pendingSprite0Hit → pendingSprite0Hit2 → isSprite0hit → isSprite0hit_Delayed），總計 ~1.5 dot。遊戲透過輪詢 $2002 bit 6 偵測碰撞。

**Catch-up 影響**：Sprite 0 的碰撞位置取決於 BG 和 Sprite 的像素資料，這只能在逐 dot 渲染時計算。**無法數學預測**，必須實際跑 PPU rendering。

### 2.5 OAM Corruption Model

`oamCorruptDelay` + `oamCorruptPending` 依賴 rendering enable/disable 的精確時序。`$2001` 寫入時記錄 `oamCorruptWasRendering` 和 `oamCorrupt2001Value`，延遲後在特定條件下觸發 OAM 損壞。

**Catch-up 影響**：需要精確追蹤 $2001 寫入時刻的渲染狀態。

---

## 三、APU ↔ CPU 干擾因素

### 3.1 mcApuPutCycle — 最關鍵的單一同步欄位

`mcApuPutCycle` 是一個 bool，在每次 APU step 結束時翻轉（GET ↔ PUT）。**至少 6 個子系統讀取它**：

| 讀取者 | 用途 | 錯誤後果 |
|--------|------|---------|
| DmaOneCycle() | PUT/GET cycle 決定 DMA 優先權和操作類型 | OAM/DMC DMA 操作順序錯亂 |
| OAM DMA 首 cycle | parity 決定 513 vs 514 cycle 長度 | DMA 時長差 1 cycle |
| $4015 write handler | 決定 dmcStatusDelay = 3 or 4 | DMC 啟用/停用延遲差 1 cycle |
| $4017 write handler | 決定 apuFrameCounterReset = 3 or 4 | Frame counter 重置延遲差 1 cycle |
| $4015 implicit abort | 決定 abort 條件 | DMC phantom DMA 行為錯誤 |
| soft reset | 決定 $4017 deferred write 延遲 | 重置後 APU 時序錯誤 |

**Catch-up 影響**：如果 CPU 跑在 APU 前面，`mcApuPutCycle` 的狀態是過時的。所有讀取此欄位的路徑都會得到錯誤值。**無法透過數學預測補償**，因為 APU step 的 PUT/GET 交替取決於 APU 已經推進了多少步。

### 3.2 DMC DMA 偷取 CPU Cycle

DMC 的 `clockdmc()` 在計時器歸零時設定 `dmcDmaRunning = true`。下一次 CPU gate 時，DMA 取代 CPU 執行。

偷取的 cycle 數取決於：
- `cpuIsRead`：DMA 只在 CPU 讀取 cycle 觸發（寫入 cycle 不可中斷）
- `dmcDmaHalt`：halt phase 產生 1 個 dummy read
- `mcApuPutCycle`：GET vs PUT 決定是否執行實際 fetch

**Catch-up 影響**：DMC rate 是固定的（可預測觸發時間），但 `$4015` 寫入可以在任意 CPU cycle 重啟 DMC，且 `dmcLoadDmaCountdown` 的延遲依賴 `mcApuPutCycle` parity。**DMC 觸發時機是 DYNAMIC 的**。

### 3.3 Frame Counter IRQ

APU frame counter 在固定閾值觸發 IRQ（4-step mode: cycle 29828/29829/29830）。IRQ 信號經過：

```
statusframeint (APU 設定)
  → irqLineCurrent (UpdateIRQLine 合成)
    → IRQLine (mcCpuClock==5 鎖存)
      → PollInterrupts() (CPU 指令邊界取樣)
```

**Catch-up 影響**：閾值是固定的（可預測），但 `$4017` 寫入會重置 counter，延遲值依賴 `mcApuPutCycle`。`$4015` 讀取會設定 `clearingFrameInterrupt`（deferred clear，下一個 PUT cycle 處理）。

### 3.4 Controller Shift Register

`ProcessControllerShift()`（apu_step 內）和 `ProcessControllerStrobe()`（GET cycle）處理手把的 shift register。DMA 期間的 $4016/$4017 讀取有 bus masking（`dataPinsNotFloating`）。

**Catch-up 影響**：遊戲讀取 $4016 的時序不影響模擬精確度（polling 夠快），但 DMA 期間的 masking 依賴即時的 bus 狀態。

---

## 四、Mapper 干擾因素

### 4.1 Mapper 分類 — 可否批次處理

**Category A — 純 CPU cycle 計數器（可批次）**：

| Mapper | 名稱 | IRQ 機制 |
|--------|------|---------|
| 016 | Bandai FCG | 16-bit 下計數 |
| 018 | Jaleco SS88006 | 可變寬度計數 |
| 019 | Namco 163 | 15-bit 上計數 |
| 065 | Irem H3001 | 16-bit 下計數 |
| 067 | Sunsoft #3 | 16-bit 下計數 |
| 069 | FME-7 | 16-bit 下計數 |
| VRC4/6/7 cycle mode | Konami | 8-bit 上計數 |

這些理論上可以用 `counter -= batchedCycles` 數學快進。但 register write 之間的 cycle 必須正確分段。

**Category B — Per-dot PPU 依賴（不可批次）**：

| Mapper | 名稱 | 依賴原因 |
|--------|------|---------|
| **004** | **MMC3** | A12 邊緣偵測 + M2 filter（跨 CPU/PPU 域） |
| 064 | RAMBO-1 | A12 模式同 MMC3 |
| **005** | **MMC5** | VRAM read pattern 掃描線偵測 + CHR A/B 切換 |
| 009/010 | MMC2/MMC4 | CHR latch 依賴逐 fetch 地址 |

**Category C — 混合**：
| Mapper | 名稱 | 說明 |
|--------|------|------|
| 090 | JY Company | IRQ 來源可設定（CPU/A12/PPU read） |

### 4.2 MMC3 — 最困難的 Mapper

MMC3 的 A12 邊緣偵測需要兩個子系統同時配合：

1. **PPU 端**（`PpuClock()`）：每 dot 偵測 `ppuAddressBus` 的 A12 bit（bit 12）上升沿
2. **CPU 端**（`CpuClockRise()`，mcCpuClock==5）：M2 filter 在每個 CPU cycle 計算 A12 低電平持續時間（需連續 3 cycle A12=0 才認定為有效低電平）

**關鍵問題**：M2 filter 在 master clock phase 5 讀取 `ppuAddressBus`，這落在 PPU dots 之間。PPU 在 phase 0 或 4 更新 `ppuAddressBus`，M2 在 phase 5 取樣。**CPU 和 PPU 的 master clock 相位交織不可拆分。**

遺漏任何一次 `PpuClock()` 或 `CpuClockRise()` 都會破壞 M2 filter 計數，導致 scanline counter 在錯誤的掃描線觸發 IRQ。

**影響遊戲**：SMB2、SMB3、Mega Man 3-6、Kirby's Adventure、Gradius II — 所有使用 MMC3 split-screen 的遊戲。

### 4.3 MMC5 — VRAM Read Pattern 偵測

MMC5 不依賴 A12，而是偵測 PPU 的 VRAM 讀取模式：連續 3 次相同 nametable 地址讀取 = 新掃描線開始。

每一次 PPU fetch 都必須呼叫 `NotifyVramRead(addr)`。遺漏任何一次都會破壞 3-consecutive 偵測器，導致：
- 掃描線計數器不前進（IRQ 不觸發）
- CHR A/B set 切換錯誤（sprite tiles 使用 BG banks）
- 幀結束偵測失敗（`ppuIdleCounter` 不重置）

---

## 五、其他干擾因素

### 5.1 Open Bus 衰減

`open_bus_decay_timer = 77777`（dot 計數）。如果 CPU 跑在前面而 PPU 沒推進，衰減計時器的 dot 計數不正確。部分測試 ROM 檢測 open bus 衰減時間。

**風險**：低。衰減時間本身是固定近似值，差幾個 dot 不影響。

### 5.2 FDS 專屬

`fds_CpuCycle()` 每 CPU cycle 執行，包含 IRQ timer、磁碟 I/O 狀態機、wavetable 音效。IRQ timer 是純 CPU cycle 計數（可批次），但 disk I/O 狀態機依賴即時讀寫。

**風險**：中。僅 FDS 遊戲受影響。

### 5.3 擴展音效晶片

VRC6/VRC7/Namco163/Sunsoft 5B/MMC5 音效各有獨立計時器。混音容忍度高（音效品質差異不易察覺），但精確的計時器 reload 時機依賴 register write 的 CPU cycle。

**風險**：低。音效混音對微小時序偏差不敏感。

### 5.4 DMA 期間的 CPU Bus 狀態

`cpuBusAddr` 和 `dataPinsNotFloating` 在 DMA 期間影響 phantom read 和 controller masking。如果 CPU 跑在前面，bus 狀態可能不正確。

**風險**：高。影響 DMC DMA 讀取值和 controller shift register 行為。

---

## 六、Catch-up 可行性分級

### 6.1 安全區域（可數學快進）

| 場景 | 條件 | 預估節省 |
|------|------|---------|
| **VBlank 期間** | scanline 241-260，無 I/O | ~7-8% PPU 時間 |
| **Category A mapper** | 純 CPU cycle counter | counter -= N 替代逐 tick |
| **無 DMC 的靜默期** | dmcDmaRunning=false | CPU 可多跑幾個 cycle |

### 6.2 危險區域（必須逐 tick 同步）

| 場景 | 原因 |
|------|------|
| **Visible scanlines (0-239)** | Sprite 0 Hit 無法預測；PPU 渲染必須逐 dot |
| **MMC3/MMC5 遊戲的任何時刻** | A12/VRAM pattern 依賴逐 dot PPU bus state |
| **DMC DMA 活躍期間** | 偷取 cycle 的時機依賴 cpuIsRead + mcApuPutCycle |
| **$2002 讀取** | 7 次 MasterClockTick 推進所有子系統 |
| **所有 PPU register 寫入** | 延遲值依賴 mcPpuClock & 3 |

### 6.3 結論：動態 Catch-up 的最小可行策略

如果要在不破壞精確度的前提下實施 Catch-up：

1. **僅在 VBlank 且無 pending I/O 時快進 PPU** — 最安全的切入點
2. **Category A mapper 的 IRQ counter 可批次計算** — 但必須在 register write 處分段
3. **mcApuPutCycle 必須始終同步** — 這是不可妥協的，6+ 個子系統依賴它
4. **MMC3/MMC5 遊戲不適用 Catch-up** — M2 filter 的跨域依賴無法解耦
5. **所有 PPU register read/write 必須觸發完整同步** — 包括 7-tick EmulateUntilEndOfRead
6. **NMI 預測必須精確到 master clock level** — 包括管線延遲的 1.5 dot

**實際預估效能提升**：在最保守的策略下（僅 VBlank 快進 + Category A mapper 批次），約 **5-10%**。要達到 25-40% 的提升需要在 visible scanlines 也做部分 Catch-up，但這會觸碰 Sprite 0 Hit 和 MMC3 等危險區域。

---

## 七、必須通過的驗證標準

任何 Catch-up 實作都必須：

- [x] blargg 184/184 PASS（含 PAL）
- [x] AccuracyCoin 136/136 PASS
- [x] MMC3 IRQ 18/18 PASS（mmc3_irq_tests + mmc3_test + mmc3_test_2）
- [x] VBL/NMI timing 17/17 PASS
- [x] CPU interrupts 5/5 PASS
- [x] DMA tests 5/5 PASS
- [x] Sprite tests 16/16 PASS

**任何一項回歸即 revert，不做妥協。**
