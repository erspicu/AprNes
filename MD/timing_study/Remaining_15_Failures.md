# 剩餘 15 個測試失敗 — 詳細分析

**日期**：2026-04-03
**分支**：feature/ppu-high-precision
**當前**：159/174 (NTSC-only)
**目標**：174/174

---

## APU Reset（3 個）
- [ ] `4017_timing` #3 — "Delay after effective $4017 write: 0"
  - 根因：power-on 的 deferred reset 機制與 pre-advance 衝突
  - 需要精確模擬 CPU reset sequence 中 APU 的行為
- [ ] `4017_written` #3 — "At reset, $4017 should be rewritten with last value"
  - 根因：soft reset 的 $4017 re-apply 時機（deferred vs immediate）
- [ ] `10.len_halt_timing` $03 — length counter halt timing
  - 根因：可能是 halt flag 從 apuRegister 讀取的時機差異

## Interrupt（2+1 umbrella）
- [ ] `3-nmi_and_irq` — "NMI BRK"（NMI during BRK handler interaction）
  - 根因：PollInterrupts 在 BRK cycle 4 的 NMI edge detection 與 instruction boundary poll 的交互
- [ ] `4-irq_and_dma` — "0 +0"（IRQ timing during DMA）
  - 根因：DMA per-cycle dispatch 期間的 IRQ sampling 時機
- [ ] `cpu_interrupts` — umbrella（以上通過就通過）

## DMA（5 個）
- [ ] `dma_2007_read` — screen CRC mismatch
- [ ] `dma_2007_write` — DMA during $2007 write
- [ ] `dma_4016_read` — DMA during $4016 read
- [ ] `sprdma_and_dmc_dma` — "T+ Clocks"（OAM+DMC DMA cycle count mismatch）
- [ ] `sprdma_and_dmc_dma_512` — 同上
- 根因：DMA per-cycle dispatch 的精密 cycle timing。可能需要對比 TriCNES 的 DMA 每個 cycle 的行為。

## MMC3（2 個）
- [ ] `mmc3_test/4-scanline_timing` — "Scanline 0 IRQ should occur later when $2000=$08"
- [ ] `mmc3_test_2/4-scanline_timing` — 同上
- 根因：$2000 pattern table 地址雖然已即時套用，但 PPU A12 notification 在 per-master-clock 模型下的 dot-level timing 與舊 catch-up 模型不同。

## Sprite Overflow（2 個）
- [ ] `sprite_overflow_tests/2.Details`
- [ ] `sprite_overflow_tests/4.Obscure`
- 根因：sprite evaluation FSM timing / ppuRenderingEnabled_EvalDelay 精度
