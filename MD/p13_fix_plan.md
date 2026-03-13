# P13 DMA Tests 修復計畫 (132→136/136)

**建立日期**: 2026-03-10
**基線**: 174/174 blargg + 132/136 AC (master branch)
**目標**: 136/136 AC (P13 全 PASS)
**狀態**: **已完成** — BUGFIX53-56 修復全部 P13 測試 (136/136 AC)

---

## 剩餘 4 FAIL 與錯誤描述

| # | 測試 | err | 具體錯誤 (err=2) | 需要的修復 |
|---|------|-----|-----------------|-----------|
| 1 | DMA + $2002 Read | 2 | halt/alignment cycles 沒正確讀取 $2002 | DMA cycle-accurate |
| 2 | DMC DMA Bus Conflicts | 2 | DMC DMA 與 APU 暫存器的 bus conflict 不正確 | DMA cycle-accurate |
| 3 | Explicit DMA Abort | 2 | 被中止的 DMA stolen cycle 數量錯 | DMA cycle-accurate + Abort |
| 4 | Implicit DMA Abort | 2 | 被中止的 DMA stolen cycle 數量錯 | DMA cycle-accurate + Abort |

---

## 優先方案：方案 B — DMA 狀態機重寫（TriCNES 風格）

### 設計思路

將 AprNes 的一次性批量 DMA (`dmcfillbuffer()`) 改為逐 cycle 的 Get/Put 狀態機，
類似 TriCNES 的做法。在每個 tick() 中推進 DMA 狀態，而非一次 steal 3-4 cycles。

### 核心概念

```
目前 AprNes:
  dmcfillbuffer() → 一次呼叫 dmc_stolen_tick() 3-4 次 → 批量完成

方案 B:
  tick() 每次呼叫時檢查 DMA 狀態 → 逐 cycle 推進 Get/Put FSM
  用 get/put cycle parity (類似 TriCNES 的 APU_PutCycle) 控制讀寫交替
```

### 改動範圍

| 檔案 | 改動 | 風險 |
|------|------|------|
| `APU.cs` | `dmcfillbuffer()` → DMA FSM (狀態、parity 追蹤) | 主要 |
| `MEM.cs` | tick() 加 DMA 狀態機推進，移除/重構 `dmc_stolen_tick()` | 中等 |
| `IO.cs` | $4014/$4015 寫入的 DMA 觸發邏輯調整 | 小 |
| `CPU.cs` | 幾乎不動（Mem_r/Mem_w 介面不變） | 無 |
| `PPU.cs` | 不動 | 無 |

### 需要實作的 DMA 狀態

```
OAM DMA:
  - Halt cycle (等待正確 parity)
  - Alignment cycle (若 halt 在 put cycle，額外等一個 get cycle)
  - 256 byte transfer: 交替 Get (讀源地址) / Put (寫 $2004)
  - 共 513-514 cycles

DMC DMA:
  - Halt cycle (1 cycle)
  - Alignment cycle (0-1 cycle，依 parity)
  - Dummy cycle (1 cycle)
  - Read cycle (1 cycle，讀 DMC sample address)
  - 共 3-4 cycles

優先級（同時 DMA 時）:
  - GET cycle: DMC 優先
  - PUT cycle: OAM 優先
```

### 與方案 A 的比較

| | 方案 A：MCU Split | **方案 B：DMA 狀態機** |
|--|--|--|
| 改動檔案 | 5-6 個核心檔案 | 2-3 個 |
| 影響測試 | 全部 310 個 | ~15 個 DMA 相關 |
| 回歸風險 | 極高 | 中等 |
| 目標收益 | +4 AC | +4 AC |
| 額外收益 | 未來所有 sub-cycle 問題 | 僅 DMA |
| 已有失敗經驗 | 有（2+1 model） | 無 |

### TriCNES 參考

TriCNES 用對稱 3+3 模型（跟 AprNes 一樣）但 DMA 是逐 cycle 狀態機，
結果 AC 136/136 全過。證明不需要非對稱 MCU split 也能解決 P13。

關鍵參考位置：
- `ref/TriCNES-main/Emulator.cs`
  - DMA 狀態機: lines 3836-4095
  - APU_PutCycle 交替: line 705
  - OAM DMA Get/Put: OAMDMA_Get(), OAMDMA_Put()
  - DMC DMA Get/Put: DMCDMA_Get(), DMCDMA_Put()
  - CPU_Read 檢查: line 3974 (DMA 只在 read cycle 執行)

---

## 備選方案：方案 A — 非對稱 MCU Split（Mesen2 風格）

若方案 B 無法解決所有 4 個 P13 測試，再考慮方案 A。

### Mesen2 M2 Phase Split

```
NTSC: _startClockCount = 6, _endClockCount = 6
Read:  Start +5 MCU, End +7 MCU (total 12)
Write: Start +7 MCU, End +5 MCU (total 12)
```

### 具體改動

1. `StartCpuCycle(bool forRead)`: masterClock += forRead ? 5 : 7
2. `EndCpuCycle(bool forRead)`: masterClock += forRead ? 7 : 5
3. CpuRead: ProcessPendingDma → StartCpuCycle(true) → read → EndCpuCycle(true)
4. CpuWrite: StartCpuCycle(false) → write → EndCpuCycle(false)
5. 需要重新校準 boot CC、PPU offset、APU offset

---

## 2026-03-10 實驗結果（舊方案 A 嘗試）

| 方法 | blargg | AC | 失敗原因 |
|------|--------|-----|---------|
| 簡單 reorder (DMA before Start) | 172/174 | 124/136 | getCycle parity 翻轉 |
| Reorder + getCycle flip | 169/174 | — | OAM DMA read/write 在錯誤 cycle |
| Reorder + boot CC=8 | 172/174 | 124/136 | 系統自我補償，無效 |
| Steps 2+3 only (無 reorder) | 174/174 | 132/136 | 無改善也無回歸 |
| All 3 steps together | 172/174 | 124/136 | 同 reorder 問題 + P14 回歸 |

---

## Step 2: Abort Mechanism (待 DMA 狀態機完成後整合)

**已有基礎設施**: APU.cs 的 dmcDisableDelay, dmcImplicitAbortPending, dmcImplicitAbortActive。
clockdmc() 已有 delay countdown。

**延遲 $4015 disable**: putCycle ? 4 : 3 (normal), putCycle ? 6 : 5 (explicit abort)
**Implicit abort**: (dmctimer == 5 && putCycle) || (dmctimer == 4 && !putCycle)
**寫 cycle 取消**: EndCpuCycle 中 dmcImplicitAbortActive && cpuBusIsWrite → 取消 DMA

**已驗證**: 單獨實作不影響 blargg (174/174) 也不改善 AC (132/136)。

---

## TriCNES Parity 映射備忘

```
TriCNES APU_PutCycle (post-toggle) = 我們的 !putCycle
putCycle = (apucycle & 1) != 0   (odd = PUT in our model)

TriCNES DMC timer 每 CPU cycle 遞減 2 (我們遞減 1)
TriCNES timer 值 / 2 = 我們的 timer 值

TriCNES 公式 → 我們的等效:
  APU_PutCycle ? 3 : 4        →  putCycle ? 4 : 3
  APU_PutCycle ? 5 : 6        →  putCycle ? 6 : 5
  timer==2 && !APU_PutCycle   →  dmctimer==1 && putCycle
  timer==Rate && APU_PutCycle →  dmctimer==dmcrate && !putCycle
  timer==10 && !APU_PutCycle  →  dmctimer==5 && putCycle
  timer==8 && APU_PutCycle    →  dmctimer==4 && !putCycle
```

---

## 相關檔案

| 檔案 | 修改內容 |
|------|---------|
| `AprNes/NesCore/APU.cs` | dmcfillbuffer → DMA FSM; apu_4015: delayed disable + abort |
| `AprNes/NesCore/MEM.cs` | tick(): DMA 狀態機推進; dmc_stolen_tick() 重構/移除 |
| `AprNes/NesCore/IO.cs` | $4014/$4015: DMA 觸發邏輯調整 |

## 參考資料

- TriCNES: `ref/TriCNES-main/Emulator.cs` (DMA: lines 3836-4095, $4015: lines 9358-9402)
- Mesen2: `ref/Mesen2-master/Core/NES/NesCpu.cpp` (ProcessPendingDma: 325-448, M2 split: 317-321/294-297)
- TriCNES DMC timer decrement: lines 897-898
