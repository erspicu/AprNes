# 舊 Timing 模型殘留 — 修正清單

**日期**：2026-04-03
**分支**：feature/ppu-high-precision
**目前狀態**：139/174 — 架構 + 參數全部對齊 TriCNES ✅（APU length counter timing 待調）

---

## 第一輪：架構修正（全部完成）

- [x] `nmi_delay_cycle` 的 `>` 改 `>=`
- [x] DMA per-cycle dispatch（#3）
- [x] 移除 Start/EndCpuCycle（#6）
- [x] NMI direct line model（#1）
- [x] IRQ CPUClock==5 level detection（#2）
- [x] VBL suppression（#4）
- [x] ppuRenderingEnabled（#5）— 無需修改
- [x] Dead code 清理 + region 合併 ppu_step

## 第二輪：架構對齊（全部完成）

- [x] (#7) APU 獨立 gate at CPUClock==12
- [x] (#8) DMA CPU_Read gate
- [x] (#9) BRK handler cycle 4 full PollInterrupts
- [x] Gate 順序對齊
- [x] Counter init 對齊

## 第三輪：最終架構比對（全部完成）

- [x] (#10) PollInterrupts 移入 CompleteOperation

---

## 第四輪：Timing 參數校正（TriCNES 對齊）

### 11. ~~PPU Register Write Delays（相位依賴）~~ ✅ DONE
- $2000：改為全延遲模型（TriCNES: ALL fields delayed 1-2 PPU cycles）
- $2001 phase 2：3→2
- $2005 phase 1：2→1
- $2006 phase 1：5→4
- 150/174（+1 nmi_on_timing 回歸，$2000 延遲 NMI enable 正確行為）

### 12. ~~cpuCycleCount 初始值~~ ✅ DONE
- 7→0（TriCNES default）

### 13. ~~DMC Rate Table~~ ✅ 已正確
- AprNes NTSC [428,380,...] 與 TriCNES 一致（之前誤判為 PAL 值）

### 14. ~~APU Frame Counter~~ ✅ DONE（架構對齊）
- countdown/reload 模型替換為 TriCNES count-up/switch 模型
- framectrdiv → apuFrameCounter (count-up ushort)
- frameReload4/5 → hardcoded switch (7457/14913/22371/29828-30/37281-82)
- $4017 write: deferred reset (apuFrameCounterReset = 3 or 4 cycles)
- IRQ: per-cycle 29828/29829/29830 三階段（取代 irqAssertCycles 機制）
- 移除: framectrdiv, framectr, frameReload4/5, irqAssertCycles, frameIrqClearPending, clockframecounter()
- 139/174（+3 recovered: irq_timing, branch_delays_irq, nmi_control。-11 APU length counter timing 待調）
