# 舊 Timing 模型殘留 — 修正清單

**日期**：2026-04-03
**分支**：feature/ppu-high-precision
**目前狀態**：151/174 — 全 10 項完成 ✅

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

## 第二輪：TriCNES 差異比對（架構層級）

- [x] (#7) APU 獨立 gate at CPUClock==12
- [x] (#8) DMA CPU_Read gate
- [x] (#9) BRK handler cycle 4 full PollInterrupts
- [x] Gate 順序對齊：CPU(0) → NMI(8) → PPU(0) → PPU_half(2) → IRQ(5) → APU(12)
- [x] Counter init 對齊：mcCpuClock=0, mcPpuClock=0（TriCNES default）

## 第三輪：最終深度比對

### 10. ~~PollInterrupts 呼叫時機：指令開頭 → 指令結尾~~ ✅ DONE
- PollInterrupts（NMI edge + IRQ level）從 MasterClockTick operationCycle==0 移入 CompleteOperation()
- BRK (opcode 0x00) 結束時不 poll（TriCNES line 4229 確認）
- prevFlagI 改為 static field，在 CPU gate 開始時 capture
- 151/174（+2：04-nmi_control 和 vbl_clear_time 恢復）
