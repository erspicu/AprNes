# 剩餘 21 個測試失敗 — 分析

**日期**：2026-04-03
**分支**：feature/ppu-high-precision  
**當前**：153/174 (NTSC-only)

---

## APU（4 個）
- [ ] `4017_timing` — "Frame IRQ should be set sooner after power/reset"（frame counter init offset）
- [ ] `4017_written` — $4017 write timing
- [ ] `09.reset_timing` — reset timing
- [ ] `10.len_halt_timing` — length counter halt timing

**根因推測**：frame counter 開機初始值缺少 power-on advance offset（舊模型 cpuCycleCount=7 提供，新模型移除了）

## NMI/VBL（4+1 umbrella）
- [ ] `07-nmi_on_timing` — NMI enable timing（$2000 delay 精度）
- [ ] `08-nmi_off_timing` — NMI disable timing
- [ ] `6.nmi_disable` — NMI disable
- [ ] `7.nmi_timing` — NMI timing
- [ ] `ppu_vbl_nmi` — umbrella（以上通過就通過）

**根因推測**：$2000 write delay + NMILine 清除時機 + VBL latch pipeline 的 1-dot offset

## DMA（5 個）
- [ ] `dma_2007_read` — DMA during $2007 read
- [ ] `dma_2007_write` — DMA during $2007 write
- [ ] `dma_4016_read` — DMA during $4016 read
- [ ] `sprdma_and_dmc_dma` — OAM+DMC DMA interleave
- [ ] `sprdma_and_dmc_dma_512` — OAM+DMC DMA 512-byte

**根因推測**：DMA per-cycle dispatch 的精密 timing + $2007 state machine 在 DMA 期間的行為

## IRQ/Interrupt（2+1 umbrella）
- [ ] `3-nmi_and_irq` — NMI + IRQ interaction
- [ ] `4-irq_and_dma` — IRQ + DMA interaction
- [ ] `cpu_interrupts` — umbrella

## MMC3（2 個）
- [ ] `mmc3_test/4-scanline_timing` — MMC3 scanline counter timing
- [ ] `mmc3_test_2/4-scanline_timing` — 同上

**根因推測**：A12 notification timing 在 per-master-clock 模型下不精確

## Sprite Overflow（2 個）
- [ ] `sprite_overflow_tests/2.Details` — overflow evaluation 細節
- [ ] `sprite_overflow_tests/4.Obscure` — overflow obscure case

**根因推測**：ppuRenderingEnabled_EvalDelay 或 sprite evaluation FSM timing
