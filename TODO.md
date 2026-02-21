# AprNes 待修復問題清單

**基線**: 156 PASS / 18 FAIL / 174 TOTAL (2026-02-22)

優先權排序原則：**影響大 + 好修** 排最前面

---

## 已完成

- ~~Bug A: MMC3 IRQ A12 clocking~~ → **+12 PASS** (BUGFIX8)
- ~~Bug C: APU frame counter timing~~ → **+6 PASS** (BUGFIX9)
  - frameReload 修正、$4017 offset +7→+2、even/odd jitter
  - IRQ 三連 assert (pre-fire / step 3 / post-fire)
- ~~Bug L: DMC timer 計時器行為~~ → **+2 PASS** (BUGFIX10)
  - DMC timer 從 up-counter 改為 down-counter
  - $4010 更改速率時不重置倒數，僅在 reload 時生效
- ~~Bug D: APU $4017 reset/power-on~~ → **+3 PASS** (BUGFIX11)
  - Power-on/reset advance: framectrdiv = 7450 (模擬 9-cycle 提前寫入)
  - Length counter reload suppression: 在 length clock 同 cycle 寫入時抑制 reload
- ~~Bug B: PPU VBL/NMI timing~~ → **+15 PASS** (BUGFIX12)
  - 1-cycle NMI delay model: nmi_delay → promote → nmi_pending
  - Per-dot edge detection in tick()
  - VBL clear cx=1→2, VBL flag 抑制 at (sl=241,cx=1)
  - $2002 read 不清 nmi_pending, $2000 falling edge 不清 nmi_pending
- ~~Bug M: MMC3 scanline timing~~ → **+2 PASS** (BUGFIX13)
  - BG A12 notification 移至 phase 3,7（模擬真實 NES bus timing）
  - Sprite A12 notification 移至 phase 2,6，sprite fetch 從 cx=257 開始
  - 新增 garbage NT A12 通知 at cx=337
  - VBL gap detection: 跨 VBL 時不重置 a12LowSince

---

## P2 — 中等難度（共 6 個測試）

### Bug D: APU timing 剩餘
- **已修復**: 3/6 測試 — 4017_timing, 4017_written, 09.reset_timing
- **剩餘 3 個測試 FAIL**:
  - `blargg_apu_2005/08.irq_timing` — IRQ 觸發 timing ($02)，需 IRQ line/flag 分離
  - `blargg_apu_2005/10.len_halt_timing` — Length counter halt timing ($03)
  - `blargg_apu_2005/11.len_reload_timing` — Length counter reload timing ($03，test 4 已修但 test 3 新露出)

### ~~Bug M: MMC3 掃描線時序微調~~ ✅ 已修復 (BUGFIX13)

---

## P3 — 較難修復（共 12 個測試）

### Bug E: CPU interrupt timing（NMI/IRQ/BRK 交互）
- **影響**: 5 個測試 FAIL
- **難度**: 高
- **失敗測試**:
  - `cpu_interrupts_v2/cpu_interrupts` — 合併 ROM
  - `cpu_interrupts_v2/2-nmi_and_brk` — NMI hijack BRK vector
  - `cpu_interrupts_v2/3-nmi_and_irq` — NMI + IRQ 優先權
  - `cpu_interrupts_v2/4-irq_and_dma` — IRQ + DMA cycle 交互
  - `cpu_interrupts_v2/5-branch_delays_irq` — Branch 跨頁延遲 IRQ
- **根因**: NMI polling 時機（指令倒數第二 cycle）、BRK vector hijacking、branch taken+page cross 的 IRQ 延遲。

### Bug F: DMC DMA cycle stealing
- **影響**: 5 個測試 FAIL
- **難度**: 高
- **失敗測試**:
  - `dmc_dma_during_read4/dma_2007_read` — Timeout
  - `dmc_dma_during_read4/dma_4016_read` — DMC DMA 干擾 $4016 讀取
  - `dmc_dma_during_read4/double_2007_read` — Timeout
  - `sprdma_and_dmc_dma/sprdma_and_dmc_dma` — OAM DMA cycle 全為 399
  - `sprdma_and_dmc_dma/sprdma_and_dmc_dma_512` — 同上
- **根因**: DMC DMA halt+dummy+read cycle stealing 未實作，OAM DMA + DMC DMA 衝突未處理。

---

## P4 — 暫緩（共 5 個測試）

### Bug G: Sprite timing 精確度
- **影響**: 4 個測試 FAIL，大多數遊戲不受影響
- **失敗測試**:
  - `sprite_hit_tests/09.timing_basics` — Sprite 0 hit timing
  - `sprite_hit_tests/10.timing_order` — Sprite 0 hit 檢測順序
  - `sprite_overflow_tests/3.Timing` — Sprite overflow timing
  - `sprite_overflow_tests/4.Obscure` — Sprite overflow 硬體 bug 模擬

### Bug H: 手把讀取
- **影響**: 1 個測試 FAIL
- **失敗測試**:
  - `read_joy3/thorough_test` — 可能與 DMA 衝突相關

---

## 修復路線圖

```
已完成: Bug A (MMC3 A12) + Bug C (APU frame counter)
  113 → 125 → 128 → 134 PASS

已完成: Bug L (DMC timer) → 136 PASS

已完成: Bug D (APU reset/power-on) → 139 PASS

已完成: Bug B (PPU VBL/NMI timing) → 154 PASS ★

已完成: Bug M (MMC3 scanline timing) → 156 PASS ★

Phase 7: Bug E + F — CPU interrupt + DMC DMA（最難）
  → 預期 +10，達到 ~166 PASS

Phase 8: Bug D 剩餘 + Bug G + H — APU timing + sprite + misc
  → 預期 +8，目標 ~174 PASS
```

---

*最後更新: 2026-02-22 (Bug M complete — 156 PASS)*
