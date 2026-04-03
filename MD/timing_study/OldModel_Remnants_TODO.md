# 舊 Timing 模型殘留 — 修正清單

**日期**：2026-04-03
**分支**：feature/ppu-high-precision
**最終狀態**：150/174 — 全 6 項架構修正完成 ✅

---

## 已修正（全部完成）

- [x] `nmi_delay_cycle` 的 `>` 改 `>=`（配合 CPU-first 新模型）
- [x] DMA per-cycle dispatch（TODO #3）— ProcessPendingDma blocking loop → per-cycle DmaOneCycle
- [x] 移除 Start/EndCpuCycle（TODO #6）— 已從 MEM.cs 完全移除
- [x] NMI direct line model（TODO #1）— NMILine + edge detection
- [x] IRQ CPUClock==5 level detection（TODO #2）— IRQLine latched at M2 rising edge
- [x] VBL suppression（TODO #4）— pendingVblank = false at VBL dot
- [x] ppuRenderingEnabled（TODO #5）— 已驗證無需修改

---

### 1. ~~NMI 模型：delay cycle → TriCNES 直接 NMILine~~ ✅ DONE
- nmi_delay_cycle/nmi_output_prev/nmi_pending 替換為 NMILine + edge detection
- TriCNES 模型：CPUClock==8 `NMILine |= VBL && NMI_EN`，PollInterrupts edge detect
- 移除：nmi_just_deferred（edge detection 自然處理）
- 殘留 timing 回歸（suppression/disable）屬參數微調，非架構問題

### 2. ~~IRQ 模型：irqLinePrev/Current → CPUClock==5 level detection~~ ✅ DONE
- irqLinePrev 移除，IRQLine 在 CPUClock==5 從 irqLineCurrent (level detector) latch
- APU frame counter re-assertion at CPUClock==5
- BRK completion clears IRQLine（TriCNES acknowledge model）

### 3. ~~DMA 引擎：blocking ProcessPendingDma → per-cycle dispatch~~ ✅ DONE
- ProcessPendingDma 替換為 per-cycle DmaOneCycle()
- TriCNES 模型：OAM halt/aligned/put/get 狀態機，DMC halt + single-cycle get
- PPU 透過 MasterClockTick 自然推進
- APU 每個 DMA cycle 也正常執行（修正了舊模型 APU 在 DMA 期間暫停的 bug）

### 4. ~~VBL suppression ($2002 read timing)~~ ✅ DONE
- ppu_r_2002 在 VBL dot (sl=nmiTriggerLine, cx=1) 清除 pendingVblank
- 移除 SuppressVbl dead code（永遠為 false）

### 5. ~~ppuRenderingEnabled 更新時機~~ ✅ 無需修改
- end-of-dot delay 是 per-PPU-dot 機制，在 per-master-clock 架構下本身正確
- sprite_overflow 失敗在重構前就存在，非此項造成

### 6. ~~EndCpuCycle 空函式（DMA 呼叫）~~ ✅ DONE
- StartCpuCycle/EndCpuCycle/tick/Mem_r/Mem_w/ZP_r/ZP_w 全部移除
- absorbDmaFlags/ProcessPendingDma 一併移除

---

## 額外清理

- [x] 移除 dead code：ppu_2007_temp, sprLineReady, SuppressVbl, m2PhaseIsWrite, cpu_step()
- [x] 合併 ppu_step_ntsc/pal/dendy → 統一 ppu_step()（region 參數化）

---

## 修正順序（全部完成）

1. ~~**DMA per-cycle dispatch**~~ ✅ DONE
2. ~~**NMI direct line model**~~ ✅ DONE
3. ~~**IRQ level detection at CPUClock==5**~~ ✅ DONE
4. ~~**VBL suppression dot 調整**~~ ✅ DONE
5. ~~**ppuRenderingEnabled 更新時機**~~ ✅ 無需修改
6. ~~**移除 Start/EndCpuCycle**~~ ✅ DONE
