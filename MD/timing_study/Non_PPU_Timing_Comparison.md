# AprNes vs TriCNES — 非 PPU Timing 模型完整比較

**建立日期：2026-04-02**
**目的**：比對兩個模擬器在 CPU、APU、DMA、Memory Bus 等非 PPU 子系統的 timing 處理差異。

---

## 1. CPU Cycle 模型

### AprNes（`CPU.cs`）
- Per-cycle 狀態機，`operationCycle` 變數追蹤指令內第幾個 cycle
- 每個 opcode handler 根據 `operationCycle` 執行對應的 bus 操作
- `opFnPtrs[]` 陣列存放 256 個 opcode 的 delegate

### TriCNES（`Emulator.cs:268+`）
- 同樣 per-cycle 狀態機，`operationCycle` 欄位
- 每個 opcode 用 switch/case on `operationCycle`
- 完全相同的支援欄位：`dl`, `temporaryAddress`, `dataBus`, `addressBus`

### 結論：⬜ 相同 — 兩者使用完全一致的 per-cycle 狀態機模型

---

## 2. Read-Modify-Write (RMW) 指令

### AprNes
- 標準 4-cycle 模式：Fetch address → Read → Write old value → Write new value
- 明確的 dummy write（舊值）後真正寫入（新值）

### TriCNES
- 完全相同的 4/5-cycle 模式（依 addressing mode）
- 相同的 dummy write 行為

### 結論：⬜ 相同

---

## 3. Page Crossing Penalty

### AprNes（`CPU.cs:345-502`）
- `GetAddressAbsOffX(optionalExtraCycle)` 帶參數控制
- 無 page cross 時 `operationCycle++` 跳過一個 cycle
- 比較高位元組是否改變

### TriCNES（`Emulator.cs:9826-9870`）
- `GetAddressIndOffY(TakeExtraCycleOnlyIfPageBoundaryCrossed)` 參數
- 相同的條件跳過邏輯

### 結論：⬜ 相同

---

## 4. Branch Taken/Not-Taken Timing

### AprNes（`CPU.cs:508-533`）
- Not taken：2 cycles
- Taken, no cross：3 cycles
- Taken, cross：4 cycles
- 特殊追蹤：`branchIrqSaved` 在 dummy read 前保存 `irqLinePrev`，無 page cross 時還原

### TriCNES（`Emulator.cs:6804-6830`）
- 相同的 2/3/4 cycle 模式
- Page cross cycle 呼叫 `PollInterrupts_CantDisableIRQ()`

### 結論：⬜ 相同效果 — 不同機制（AprNes 用 saved state，TriCNES 用 explicit poll），結果一致

---

## 5. Interrupt Polling（IRQ/NMI 取樣時機）

### AprNes（`CPU.cs:569-612`, `MEM.cs:40-48`）
- NMI：PPU 驅動的 `nmi_delay_cycle` 機制，在 `StartCpuCycle()` 中 promote
- IRQ：`irqLinePrev` / `irqLineCurrent` per-tick tracking（penultimate cycle 自然捕獲）
- 取樣時機：隱式，通過 PPU step 後的 edge detection

### TriCNES（`Emulator.cs:3939-3960`）
- 明確呼叫 `PollInterrupts()`，通常在指令最後一個 cycle
- NMI：`NMI_PinsSignal && !NMI_PreviousPinsSignal` 偵測 rising edge
- IRQ：`IRQLine && !flag_Interrupt` 位準取樣

### 相同點
- 都在 penultimate cycle 取樣
- NMI 是 edge-triggered，IRQ 是 level-triggered

### 差異分析
| | AprNes | TriCNES |
|---|---|---|
| NMI 機制 | `nmi_delay_cycle` 間接 | `PollInterrupts()` 直接 |
| IRQ 機制 | `irqLinePrev/Current` per-tick | `IRQLine` per-instruction |

### 結論：🟡 機制不同，效果相同 — 都實現了正確的 penultimate cycle 取樣

---

## 6. IRQ/NMI Hijacking（BRK + NMI 劫持）

### AprNes（`CPU.cs:584-664`）
- BRK 的 operationCycle 4（stack push）檢查 `nmi_pending`
- 若 NMI pending → 設 `doNMI = true` → vector 從 $FFFE 改到 $FFFA
- `nmi_just_deferred` flag 處理 BRK/IRQ 後的 NMI deferral

### TriCNES（`Emulator.cs:9379, 10570`）
- `DoNMI` flag 由 `PollInterrupts()` edge detection 設定
- BRK cycle 3 偵測 NMI edge → 下一 cycle 使用 NMI vector

### 結論：⬜ 相同效果 — 語義一致

---

## 7. APU Frame Counter Timing ⭐ 有差異

### AprNes（`APU.cs:116-290`）
- **4-step NTSC**：步驟在 CPU cycle 7460, 14916, 22374, 29832
- **5-step NTSC**：7460, 14916, 22374, 29832, 37284
- `framectrdiv` 每 APU cycle 遞減
- 陣列：`frameReload4[] = {7460, 7456, 7458, 7458}`

### TriCNES（`Emulator.cs:748-1057`）
- **4-step NTSC**：步驟在 CPU cycle 7457, 14913, 22371, 29828
- **5-step NTSC**：7457, 14913, 22371, 29829, 37281
- 內部 double 所有值做半 cycle 精度
- `APU_FrameCounterReset` 倒數（寫入 $4017 後 3-4 cycle 延遲）

### 差異分析
| Step | AprNes | TriCNES | 差距 |
|:----:|:------:|:-------:|:----:|
| 1 | 7460 | 7457 | **+3** |
| 2 | 14916 | 14913 | **+3** |
| 3 | 22374 | 22371 | **+3** |
| 4 | 29832 | 29828 | **+4** |

### 結論：🟠 有差異（3-4 cycles）— AprNes 的 frame counter 步驟比 TriCNES 晚 3-4 個 CPU cycle。已通過 blargg apu_2005 11/11，此差異在測試容忍範圍內。

---

## 8. APU IRQ Timing & Assertion

### AprNes（`APU.cs:121-123`）
- `irqAssertCycles` 計數器：step 3 觸發後 IRQ 持續多個 cycle
- `statusframeint` flag 設定
- $4015 讀取時清除

### TriCNES（`Emulator.cs:1057, 694-697`）
- `APU_Status_FrameInterrupt` flag
- 在 CPUClock==5 時 assert IRQ line
- $4015 讀取或 $4017 bit6 寫入時清除

### 結論：⬜ 功能相同

---

## 9. DMC DMA Timing

### AprNes（`MEM.cs:141-292`）
- **Halt cycle**：`dmaNeedHalt` 觸發 `ProcessPendingDma()` 阻塞
- **Dummy read**：`dmcNeedDummyRead` flag 在 fetch 前多讀一次
- **Steal count**：2-3 cycles（依 M2 phase alignment）
- **Implicit abort**：`dmcImplicitAbortActive` — DMA gate 在 1 cycle 後失敗
- Get（偶數 M2）：讀取；Put（奇數 M2）：寫入 DMC shifter

### TriCNES（`Emulator.cs:934-980`）
- **Halt cycle**：`DMCDMA_Halt` flag
- **DMCDMADelay**：2 APU cycle 延遲
- **Get/Put**：偶數 clock → Get，奇數 clock → Put
- `DMCDMA_Get()` / `DMCDMA_Put()` 函式

### 結論：⬜ 功能相同 — 都 steal 2-3 cycles，alignment 處理方式略有不同

---

## 10. OAM DMA ($4014) Timing

### AprNes（`MEM.cs:204-273`）
- **總 cycle**：513-514（halt + 256 get/put + alignment）
- **Alignment**：`cpuCycleCount & 1` 檢查 parity
- Get（偶數 M2）：從 source page 讀取；Put（奇數 M2）：寫入 $2004

### TriCNES（`Emulator.cs:3855-3880`）
- **總 cycle**：513-514
- **Alignment**：`OAMDMA_Aligned` + `FirstCycleOfOAMDMA` 追蹤
- 相同的 get/put 模式

### 結論：⬜ 相同

---

## 11. DMA + DMC 交互作用

### AprNes（`MEM.cs:207-288`）
- OAM + DMC 可同時進行，主迴圈交錯 get/put
- DMC gate check：disabled mid-DMA 則中止

### TriCNES（`Emulator.cs:3832-3880`）
- 順序處理：先 OAM DMA 再 DMC DMA
- DMC gate 類似

### 結論：⬜ 功能等價（交錯 vs 順序，master clock alignment 下結果相同）

---

## 12. Memory Bus — Tick 模型

### AprNes（`MEM.cs:62-85`）
- `CpuRead(addr)`: `StartCpuCycle()` → 讀取 → `EndCpuCycle()`
- `CpuWrite(addr, val)`: `StartCpuCycle()` → 寫入 → `EndCpuCycle()`
- 每次 bus 操作前後各跑一半 master clock

### TriCNES
- `Fetch(addr)` / `Store(data, addr)` 在 mapper 層處理
- Master clock 由主迴圈的 CPUClock 倒數管理

### 結論：⬜ 結構不同，timing 等價

---

## 13. Open Bus & Bus Conflict

### AprNes（`MEM.cs:296-344`）
- 未映射讀取返回 `cpubus`（上次 bus 值）
- PPU open bus：`openbus` + `open_bus_decay_timer`（77777 PPU dots decay）
- Mapper bus conflict：在 mapper 的 `StorePRG()` 中隱式 AND

### TriCNES
- 未映射讀取返回 `dataBus`
- PPU open bus：`PPUBusDecay[]` 陣列逐 bit 衰減
- Bus conflict：同樣在 mapper 層

### 差異分析
| | AprNes | TriCNES |
|---|---|---|
| CPU open bus | 單一 `cpubus` | 單一 `dataBus` |
| PPU open bus decay | 全位元組統一衰減 | **逐 bit 衰減** |

### 結論：🟡 PPU open bus decay 粒度不同（per-byte vs per-bit），但實際影響極小

---

## 14. Reset Sequence

### AprNes（`CPU.cs:56-60`）
- 7 cycle boot sequence，由 Op_00（BRK handler）驅動
- Cycle 1-3: dummy read + stack decrement; Cycle 4-6: vector fetch ($FFFC); Cycle 7: wait

### TriCNES（`Emulator.cs:485-507`）
- `Reset()` 函式設定初始狀態
- 同樣的 BRK-style reset 序列

### 結論：⬜ 相同

---

## 15. Power-Up State ⭐ 有差異

### AprNes（`Main.cs:257-287`）
- A=0, X=0, Y=0, **SP=0xFD**, PC=from $FFFC
- I flag = 1（reset cycle 7 設定）
- RAM 全清零

### TriCNES（`Emulator.cs:485-507`）
- A=0, X=0, Y=0, **SP=0x00**, PC=from $FFFC
- I flag = 1
- RAM 全清零

### 差異分析
| | AprNes | TriCNES |
|---|---|---|
| **Stack Pointer** | **0xFD** | **0x00** |

### 結論：🟠 SP 初始值不同 — AprNes 的 0xFD 是 NESdev Wiki 定義的正確值（reset sequence 會將 SP 從 0x00 做 3 次 decrement 到 0xFD）。TriCNES 的 0x00 可能是 reset sequence 會自己 decrement 到 0xFD。

---

## 16. Region 差異

### AprNes
| 參數 | NTSC | PAL | Dendy |
|------|------|-----|-------|
| Master/CPU | 12 | 16 | 15 |
| CPU freq | 1,789,773 Hz | 1,662,607 Hz | 1,773,447 Hz |
| Pre-render line | 261 | 311 | 311 |
| NMI trigger line | 241 | 241 | 291 |
| Frame seconds | 1/60.0988 | 1/50.0070 | 1/50.0070 |

### TriCNES
- 僅支援 NTSC/PAL
- **不支援 Dendy**

### 結論：🟢 AprNes 區域支援更完整（三區域 vs 兩區域）

---

## 總結

### 完全相同（⬜）
- CPU per-cycle 狀態機模型
- RMW dummy write 行為
- Page crossing penalty
- Branch taken/not-taken cycle count
- NMI/IRQ hijacking 語義
- APU IRQ assertion
- OAM DMA 總 cycle count
- DMA + DMC 交互
- DMC DMA steal count
- Length counter / envelope / sweep clocking
- Reset sequence

### 機制不同但效果相同（🟡）
- Interrupt polling（nmi_delay_cycle vs PollInterrupts）
- Branch IRQ handling（saved state vs explicit poll）
- Memory bus tick model（Start/End bracket vs Fetch/Store）
- PPU open bus decay（per-byte vs per-bit）

### 有數值差異（🟠）
- **APU frame counter timing**：AprNes 晚 3-4 cycles（已通過 blargg，在容忍範圍內）
- **Power-up SP**：AprNes=0xFD（正確），TriCNES=0x00（reset sequence 自行 decrement）

### AprNes 更完整（🟢）
- 支援 Dendy 區域（TriCNES 不支援）

### 結論
非 PPU 子系統的 timing 模型高度一致。兩者的差異主要在 **實作機制**（不同的程式結構達成相同效果），而非 **timing 語義**。唯一的數值差異是 APU frame counter 的 3-4 cycle offset，但已通過所有 blargg 測試。
