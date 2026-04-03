# 舊 Timing 模型殘留 — 修正清單

**日期**：2026-04-03
**分支**：feature/ppu-high-precision
**目前狀態**：150/174 — 第一輪 6 項完成，第二輪 3 項進行中

---

## 第一輪：已修正（全部完成）

- [x] `nmi_delay_cycle` 的 `>` 改 `>=`（配合 CPU-first 新模型）
- [x] DMA per-cycle dispatch（#3）— ProcessPendingDma blocking loop → per-cycle DmaOneCycle
- [x] 移除 Start/EndCpuCycle（#6）— 已從 MEM.cs 完全移除
- [x] NMI direct line model（#1）— NMILine + edge detection
- [x] IRQ CPUClock==5 level detection（#2）— IRQLine latched at M2 rising edge
- [x] VBL suppression（#4）— pendingVblank = false at VBL dot
- [x] ppuRenderingEnabled（#5）— 已驗證無需修改
- [x] Dead code 清理 + region 合併 ppu_step

---

## 第二輪：TriCNES 差異比對（架構層級）

### 7. APU 執行時機：mcCpuClock==0 內 → CPUClock==12 獨立 gate
- **現況**：APU step 在 mcCpuClock==0 gate 內，CPU step 之後執行
- **TriCNES**：APU step 在 CPUClock==12（獨立 gate），是下一個 CPU cycle 的開頭
- **差異**：AprNes APU 在 CPU cycle 結尾跑，TriCNES 在開頭跑（1 cycle offset）
- **影響 tests**：irq_timing, irq_flag_timing（APU frame counter IRQ timing）

### 8. DMA gate 缺 CPU_Read 條件
- **現況**：DMA gate 只檢查 `dmcDmaRunning || spriteDmaTransfer`
- **TriCNES**：DMA gate 額外要求 `CPU_Read == true`，write cycle 時 DMA 被 stall
- **差異**：AprNes DMA 無論 read/write cycle 都搶 cycle；TriCNES 只在 read cycle 搶
- **影響 tests**：dma_2007_write, dma_2007_read, dma_4016_read, irq_and_dma, sprdma_and_dmc_dma

### 9. BRK handler cycle 4 的 PollInterrupts
- **現況**：BRK cycle 4 只檢查 NMI edge（`NMILine && !nmiPinsSignal`）
- **TriCNES**：BRK cycle 4 呼叫完整 PollInterrupts()（NMI edge + IRQ level）
- **差異**：AprNes 不會偵測 BRK 執行期間新到達的 IRQ
- **影響 tests**：nmi_and_brk, nmi_and_irq

---

## 修正順序建議

7. **DMA CPU_Read gate**（影響最多測試，5+ 個 DMA/IRQ 測試）
8. **APU 獨立 gate at CPUClock==12**（影響 IRQ timing）
9. **BRK PollInterrupts**（影響 nmi_and_brk/irq 測試）
