# TriCNES Timing Model 移植 — 子系統 TODO

**日期**：2026-04-03
**分支**：feature/ppu-high-precision
**當前**：163/174
**目標**：174/174（扣除 5 個 TriCNES 已知錯誤後 = 169/174）

---

## 已完成移植 ✅

- [x] MasterClockTick gate 結構與順序
- [x] NMI model（NMILine level + edge detect）
- [x] IRQ model（IRQLine latch at CPUClock==5）
- [x] DMA per-cycle dispatch 架構（OAM halt/aligned/get/put, DMC halt+get, CPU_Read gate）
- [x] APU frame counter（count-up model + deferred $4017 reset）
- [x] APU apu_step 內部順序（GET/PUT split）
- [x] APU length counter（deferred reload flag model）
- [x] APU halt flags（per-cycle update from apuRegister）
- [x] PPU register write delays（$2000/$2001/$2005/$2006）
- [x] $2000 write（immediate + delayed re-application, NMILine clearing at CPUClock==8 only）
- [x] $2002 read（deferred VBL clear, split timing for sprite flags）
- [x] VBL latch pipeline（ppuVSET → Latch1/Latch2）+ Sprite0 hit pipeline
- [x] PollInterrupts（separate function, doIRQ direct, CLI/SEI/PLP poll-before-flag）
- [x] BRK handler（PollInterrupts at cycle 4, CompleteOperation_NoPoll, no apuSoftReset inside）
- [x] Branch（PollInterrupts at cycle 1 + CantDisableIRQ at page-cross）
- [x] DMC timer（-2/GET cycle）
- [x] CPU reset sequence（through MasterClockTick, apuSoftReset moved to SoftReset()）
- [x] DmaDummyRead（full bus read, no $4016/$4017 skip）

---

## 待移植 — 11 個測試失敗對應的 4 個子系統

### J. DMA OAM/DMC Cycle Count（影響 7 個測試）
- **測試**：dma_2007_read, dma_2007_write, dma_4016_read, sprdma_and_dmc_dma ×2, irq_and_dma, cpu_interrupts(umbrella)
- **現況**：per-cycle DmaOneCycle 架構正確，但 OAM+DMC 交互時的 cycle count 比 master 多 ~7 cycles
- **根因分析**：
  - DMA dummy read 的 $2007 state machine 互動（bus read 觸發 VRAM address increment）
  - OAM/DMC 交互時的 halt/alignment 重設邏輯可能多了 extra cycles
  - irq_and_dma: DMA 期間的 IRQ sampling 時機差異
- **移植方向**：完整讀取 TriCNES 的 OAMDMA_Get/Put/Halted + DMCDMA_Get/Put/Halted，逐行對比 AprNes 的 OamDmaGet/Put/DmcDmaGet/DmaDummyRead，找出差異

### I. MMC3 A12 Notification Timing（影響 2 個測試）
- **測試**：mmc3_test/4-scanline_timing ×2
- **現況**：A12 notification 在 ppu_rendering_tick 的固定 phase 觸發
- **根因**：$2000=$08 時 scanline 0 IRQ 應該更晚觸發（sprite pattern table $1000 的 A12 edge 在 dot 260+）
- **移植方向**：讀取 TriCNES PPU tile fetch 的 A12 edge detection 邏輯，確認每個 fetch phase 的地址計算

### H. Sprite Overflow 即時評估（影響 2 個測試）
- **測試**：sprite_overflow_tests/2.Details, 4.Obscure
- **現況**：dot 0 預算整條掃描線（PrecomputeOverflow），結果在指定 cycle 套用
- **移植方向**：讀取 TriCNES 的 per-dot sprite evaluation FSM，重寫 overflow 為即時逐 dot 評估

---

## 移植順序

| 順序 | 項目 | 測試數 | 說明 |
|------|------|--------|------|
| 1 | **J. DMA cycle count** | 7 | 影響最多，需逐行對比 TriCNES DMA helpers |
| 2 | **I. MMC3 A12** | 2 | A12 edge timing 在 tile fetch |
| 3 | **H. Sprite overflow** | 2 | 預算→即時 FSM |

## TriCNES 已知錯誤（不移植）
收斂到 169/174 後，以下 5 個用 NESdev wiki 處理：
- 6-MMC3_alt, 6-MMC6, 5-MMC3_rev_A
- read_write_2007, power_up_palette
