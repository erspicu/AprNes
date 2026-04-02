# 舊 Timing 模型殘留 — 待修正清單

**日期**：2026-04-03
**分支**：feature/ppu-high-precision
**目前狀態**：151→154/174（NMI >= fix）

---

## 已修正

- [x] `nmi_delay_cycle` 的 `>` 改 `>=`（配合 CPU-first 新模型）

## 待修正

### 1. NMI 模型：delay cycle → TriCNES 直接 NMILine
- **現況**：PPU 設 `nmi_delay_cycle = cpuCycleCount`，MasterClockTick 在 CPUClock==8 用 `>=` promote
- **TriCNES**：CPUClock==8 直接 `NMILine |= VBL && NMI_EN`，operationCycle==0 時 clear
- **風險**：上次嘗試直接替換造成 NMI 過度觸發（142/174），需要更仔細的 edge detection
- **影響 tests**：nmi_timing, suppression, nmi_off_timing, nmi_control, nmi_and_brk

### 2. IRQ 模型：irqLinePrev/Current → CPUClock==5 level detection
- **現況**：`irqLinePrev` 在 StartCpuCycle 保存（每 CPU cycle 開始），`irqLineCurrent` 由 `UpdateIRQLine()` 維護
- **TriCNES**：CPUClock==5 直接 `IRQLine = IRQ_LevelDetector`，level detection 在 M2 rising edge
- **問題**：DMA 的 StartCpuCycle 也會覆蓋 `irqLinePrev`，造成 DMA 期間的 IRQ 狀態不準
- **影響 tests**：irq_timing, nmi_and_irq, irq_and_dma, branch_delays_irq

### 3. DMA 引擎：blocking ProcessPendingDma → per-cycle dispatch
- **現況**：`ProcessPendingDma` 用 while 迴圈一次跑完所有 DMA cycles，每 cycle 呼叫 StartCpuCycle
- **TriCNES**：DMA 在 `_6502()` 開頭 gate check，每個 CPU gate 只做一個 DMA cycle
- **問題**：
  - DMA 的 StartCpuCycle 跑 12 次 PPU tick（但 MasterClockTick 在 DMA block 期間不跑）
  - DMA 期間的 NMI/IRQ timing 不正確（沒有 CPUClock==8/5 check）
  - 27 處 StartCpuCycle/EndCpuCycle 呼叫需要移除
- **影響 tests**：dma_2007_read, dma_4016_read, sprdma_and_dmc_dma, irq_and_dma

### 4. VBL suppression ($2002 read timing)
- **現況**：`SuppressVbl` 在 `ppu_r_2002` 中設定，條件是 `scanline == nmiTriggerLine && ppu_cycles_x == 1`
- **問題**：假設 $2002 read 和 VBL set 在同一 dot 的特定相對位置。新模型中 CPU read 和 PPU dot 的相對位置不同
- **影響 tests**：nmi_suppression

### 5. ppuRenderingEnabled 更新時機
- **現況**：在 `ppu_step_rendering()` 末尾更新（end-of-dot delay）
- **問題**：跟 CPU 的相對位置改變了（CPU 先跑，PPU 後跑）
- **影響 tests**：sprite_overflow timing

### 6. EndCpuCycle 空函式（DMA 呼叫）
- **現況**：EndCpuCycle 是空函式，但 DMA 引擎仍呼叫它（20 處）
- **修正方向**：DMA 改為 per-cycle dispatch 後，Start/EndCpuCycle 可完全移除

---

## 修正順序建議

1. **DMA per-cycle dispatch**（最大風險，最多影響）— 移除 blocking loop，改 MasterClockTick gate
2. **NMI direct line model**（需要 edge detection 正確才能避免過度觸發）
3. **IRQ level detection at CPUClock==5**
4. **VBL suppression dot 調整**
5. **ppuRenderingEnabled 更新時機**
6. **移除 Start/EndCpuCycle**（DMA 完成後）
