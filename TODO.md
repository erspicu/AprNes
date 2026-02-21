# AprNes 待修復問題清單

**基線**: 158 PASS / 16 FAIL / 174 TOTAL (2026-02-22, run_tests_report.sh)
> 注: 16 FAIL 含 2 個重複計算（merged test + mmc3_test_2），獨立 bug 數為 14

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
- ~~Bug D 剩餘: APU IRQ timing~~ → **+3 PASS** (BUGFIX14)
  - framectrdiv tick-before-write compensation (-1)
  - 移除 pre-fire，irqAssertCycles = 2（step 3 + 2 post-fire = 3 total）
  - lengthctr_snapshot for $4015 reads（pre-step values）
  - **IRQ line sampling at opcode fetch**: irqLineAtFetch 模擬 penultimate-cycle polling
- ~~Bug E 部分: CPU interrupt timing~~ → **+1 PASS** (BUGFIX14 bonus)
  - Branch IRQ suppression: taken branch 無 page cross 抑制 IRQ polling
  - OAM DMA IRQ deferral: DMA 期間抑制 IRQ polling
  - irqLineAtFetch 修正 NMI+IRQ 交互 → 3-nmi_and_irq PASS

---

## P3 — 較難修復（共 12 個測試 FAIL）

### Bug E: CPU interrupt timing 剩餘（NMI/BRK/DMA 交互）
- **影響**: 4 個測試 FAIL（原 5 個，已修 2 個；含 1 merged test）
- **難度**: 高
- **失敗測試**:
  - `cpu_interrupts_v2/cpu_interrupts` — merged test（因 sub-test 2,4,5 失敗）
  - `cpu_interrupts_v2/2-nmi_and_brk` — NMI hijack BRK vector（NMI 早 ~2 cycles）
  - `cpu_interrupts_v2/4-irq_and_dma` — IRQ + DMA cycle 交互
  - `cpu_interrupts_v2/5-branch_delays_irq` — Branch 跨頁延遲 IRQ（test_jmp 基線失敗）

### Bug F: DMC DMA cycle stealing — ⚠️ 需架構重構
- **影響**: 5 個測試 FAIL
- **難度**: 極高（架構限制）
- **問題**: tick() 同時推進 CPU 和 PPU，但真實 NES 的 DMC DMA 只偷 CPU cycles（PPU 獨立運行）
- **需要**: 解耦 CPU/PPU 時鐘（major refactor）
- **失敗測試**:
  - `dmc_dma_during_read4/dma_2007_read` — Timeout
  - `dmc_dma_during_read4/dma_4016_read` — DMC DMA 干擾 $4016 讀取
  - `dmc_dma_during_read4/double_2007_read` — Timeout
  - `sprdma_and_dmc_dma/sprdma_and_dmc_dma` — OAM DMA cycle 全為 399
  - `sprdma_and_dmc_dma/sprdma_and_dmc_dma_512` — 同上

### MMC3 scanline timing (mmc3_test 新版)
- **影響**: 2 個測試 FAIL（同一 bug，mmc3_test + mmc3_test_2）
- **失敗測試**:
  - `mmc3_test/4-scanline_timing` — Scanline 0 IRQ should occur sooner when $2000=$08
  - `mmc3_test_2/4-scanline_timing` — 同上（mmc3_test_2 版本）

---

## P4 — 暫緩（共 5 個測試 FAIL）

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

已完成: Bug D 剩餘 + Bug E 部分 → 158 PASS / 16 FAIL ★★
  → APU IRQ timing 修正 (+3: 08.irq_timing, 10.len_halt, 11.len_reload)
  → IRQ line sampling at opcode fetch (+1: 3-nmi_and_irq)
  → Branch IRQ suppression + OAM DMA IRQ deferral
  → (16 FAIL 含 merged test + mmc3_test_2 重複，獨立 bug 14 個)

下一步: Bug E 剩餘 — NMI/BRK/DMA 交互
  → 2-nmi_and_brk, 4-irq_and_dma, 5-branch_delays_irq
  → Bug F (DMC DMA) 需架構重構，暫緩

Phase 8: Bug G + H — sprite timing + misc
  → 預期 +5，目標 ~163+ PASS (16 FAIL → ~11 FAIL)
```

---

*最後更新: 2026-02-22 (BUGFIX14 — 158 PASS / 16 FAIL / 174 TOTAL, APU IRQ timing + IRQ fetch sampling)*
