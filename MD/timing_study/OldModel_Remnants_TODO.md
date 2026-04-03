# 舊 Timing 模型殘留 — 修正清單

**日期**：2026-04-03
**分支**：feature/ppu-high-precision
**目前狀態**：150/174 — 第一輪 6 項 + 第二輪 3 項全部完成 ✅

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

### 7. ~~APU 執行時機：mcCpuClock==0 內 → CPUClock==12 獨立 gate~~ ✅ DONE
- APU step 移至 mcCpuClock==masterPerCpu 獨立 gate
- MasterClockTick gate 順序完全對齊 TriCNES：CPU(0) → APU(12) → NMI(8) → IRQ(5) → PPU

### 8. ~~DMA gate 缺 CPU_Read 條件~~ ✅ DONE
- 新增 cpuIsRead flag，在 CpuRead/CpuWrite/CompleteOperation 追蹤
- DMA gate 加入 `&& cpuIsRead`，write cycle 時 DMA 被 stall

### 9. ~~BRK handler cycle 4 的 PollInterrupts~~ ✅ DONE
- BRK cycle 4 加入 IRQ level check（`IRQLine && flagI == 0`）配合原有 NMI edge check
- 完整 PollInterrupts 對齊 TriCNES 模型

---

## 修正順序（全部完成）

7. ~~**DMA CPU_Read gate**~~ ✅ DONE
8. ~~**APU 獨立 gate at CPUClock==12**~~ ✅ DONE
9. ~~**BRK PollInterrupts**~~ ✅ DONE
