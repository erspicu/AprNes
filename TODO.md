# AprNes 待修復問題清單

**基線**: 125 PASS / 49 FAIL / 174 TOTAL (2026-02-21)

優先權排序原則：**影響大 + 好修** 排最前面

---

## 已修復 — Bug A: MMC3 IRQ A12 Clocking ✓

> **BUGFIX8** (2026-02-21): 從 hardcoded cycle 260 改為 PPU address bus A12 rising edge 驅動。
> 結果: 113→125 PASS (+12), 61→49 FAIL, 0 退化。
> 仍 FAIL: rev_A (預期)、MMC6 (不同晶片)、scanline_timing ×2、MMC3_alt。
> 詳見 `bugfix/2026-02-21_BUGFIX8.md`。

---

## P2 — 高影響、中等難度（共 13 個測試）

### Bug B: PPU VBL/NMI timing 差 1-2 個 cycle
- **影響**: 13 個 timing 測試 FAIL，影響遊戲中 VBL/NMI 交互的精確度
- **難度**: 中等偏難（需要精確到 PPU cycle 級）
- **失敗測試**:
  - `ppu_vbl_nmi/ppu_vbl_nmi` — 合併 ROM 失敗
  - `ppu_vbl_nmi/02-vbl_set_time` — VBL flag 設定時間偏差
  - `ppu_vbl_nmi/04-nmi_control` — "Immediate occurrence should be after NEXT instruction" (#11)
  - `ppu_vbl_nmi/05-nmi_timing` — NMI timing 偏差
  - `ppu_vbl_nmi/06-suppression` — VBL suppression 窗口不正確
  - `ppu_vbl_nmi/07-nmi_on_timing` — NMI enable timing
  - `ppu_vbl_nmi/08-nmi_off_timing` — NMI disable timing
  - `ppu_vbl_nmi/10-even_odd_timing` — "Clock is skipped too late, relative to enabling BG"
  - `vbl_nmi_timing/2.vbl_timing` — Failed #5
  - `vbl_nmi_timing/3.even_odd_frames` — Failed #3: even/odd frame toggle
  - `vbl_nmi_timing/4.vbl_clear_timing` — VBL clear timing
  - `vbl_nmi_timing/5.nmi_suppression` — NMI suppression
  - `vbl_nmi_timing/6.nmi_disable` — NMI disable
  - `vbl_nmi_timing/7.nmi_timing` — NMI timing
- **根因**: PPU 的 VBL flag 設定/清除時機、NMI 觸發延遲、even/odd frame skip 的
  精確 PPU cycle 有偏差。可能差 1-2 個 PPU cycle。
- **修復方向**:
  1. 精確對齊 VBL flag 在 scanline 241 dot 1 設定
  2. NMI 觸發應該是 VBL flag 設定後的下一個 CPU cycle
  3. Even/odd frame: odd frame 的 pre-render scanline 少 1 PPU cycle（dot 339→340 skip）
  4. $2002 讀取在 VBL set 同一 cycle 時的 suppression 行為

---

## P3 — 中影響、中等難度（共 8 個測試）

### Bug C: APU frame counter timing 偏差
- **影響**: 8 個 APU timing 測試 FAIL
- **難度**: 中等
- **失敗測試**:
  - `apu_test/4-jitter` — "Frame IRQ is set too late" (#3)
  - `apu_test/5-len_timing` — "First length of mode 0 is too late" (#3)
  - `apu_test/6-irq_flag_timing` — "Flag first set too late" (#3)
  - `apu_test/8-dmc_rates` — "Rate 0's period is too long" (#3)
  - `apu_test/apu_test` — 合併 ROM（因為 4/5/6/8 失敗）
  - `blargg_apu_2005.07.30/04.clock_jitter` — clock jitter
  - `blargg_apu_2005.07.30/05.len_timing_mode0` — length timing mode 0
  - `blargg_apu_2005.07.30/06.len_timing_mode1` — length timing mode 1
  - `blargg_apu_2005.07.30/07.irq_flag_timing` — IRQ flag timing
  - `blargg_apu_2005.07.30/08.irq_timing` — $03 IRQ timing
- **根因**: APU frame counter 的 divider 步進值（frameReload4/5）或 APU cycle
  計數與 CPU cycle 的對齊有 1-2 cycle 偏差。DMC rate period 也有微小偏差。
- **修復方向**:
  1. 驗證 frameReload4/5 數值是否完全匹配 nesdev wiki 的精確值
  2. 確認 APU step 與 CPU cycle 的對齊（APU 每 2 CPU cycle clock 一次）
  3. DMC rate table 值驗證

### Bug D: APU $4017 reset 行為 + reset timing
- **影響**: 3 個測試 FAIL
- **難度**: 中等偏難
- **失敗測試**:
  - `apu_reset/4017_written` — "At reset, $4017 should be rewritten with last value written" (#3)
  - `blargg_apu_2005.07.30/09.reset_timing` — $04 reset timing
  - `blargg_apu_2005.07.30/10.len_halt_timing` — length halt timing
  - `blargg_apu_2005.07.30/11.len_reload_timing` — length reload timing
- **根因**: soft reset 時 $4017 的重新套用時機、frame counter 的延遲啟動行為。
- **修復方向**: 精確實作 reset 後的 frame counter 初始化延遲

---

## P4 — 中影響、較難修（共 9 個測試）

### Bug E: CPU interrupt timing（NMI/IRQ/BRK 交互）
- **影響**: 5 個 interrupt 測試 FAIL，可能影響少數使用精確 interrupt timing 的遊戲
- **難度**: 難
- **失敗測試**:
  - `cpu_interrupts_v2/cpu_interrupts` — 合併 ROM
  - `cpu_interrupts_v2/2-nmi_and_brk` — NMI 與 BRK 交互：NMI 值都是 02，應有變化
  - `cpu_interrupts_v2/3-nmi_and_irq` — NMI 與 IRQ 交互
  - `cpu_interrupts_v2/4-irq_and_dma` — IRQ 與 OAM DMA 交互：DMA cycle 計數偏差
  - `cpu_interrupts_v2/5-branch_delays_irq` — branch 指令延遲 IRQ 的行為
- **根因**: NMI/IRQ 在指令執行的精確 polling 時機、BRK 指令與 NMI 的 hijacking
  行為、branch taken/page-cross 對 IRQ 延遲的影響、OAM DMA 期間的 IRQ timing。
- **修復方向**:
  1. NMI polling 需要在指令倒數第二個 cycle 進行（而非 last cycle）
  2. BRK 的 vector 在 push 完 P 後如果此時有 NMI pending，應 hijack 到 NMI vector
  3. Branch taken+page cross 期間的 IRQ 延遲一條指令

### Bug F: DMC DMA cycle stealing 不精確
- **影響**: 5 個測試 FAIL（3 個 dmc_dma + 2 個 sprdma_and_dmc_dma）
- **難度**: 難
- **失敗測試**:
  - `dmc_dma_during_read4/dma_2007_read` — DMC DMA 與 $2007 讀取交互
  - `dmc_dma_during_read4/dma_4016_read` — DMC DMA 與 $4016 讀取交互
  - `dmc_dma_during_read4/double_2007_read` — 雙重 $2007 讀取
  - `sprdma_and_dmc_dma/sprdma_and_dmc_dma` — OAM DMA clocks 全部 399（應有 +1/+2 變化）
  - `sprdma_and_dmc_dma/sprdma_and_dmc_dma_512` — 同上 512 byte 版本
- **根因**: DMC DMA 的 cycle stealing 機制：DMC 需要在 CPU read cycle 時插入
  halt+dummy+read（1~4 extra cycles），取決於 CPU 當前是 read 還是 write cycle。
  目前實作為簡化版（in_tick guard 擋掉巢狀 tick）。
- **修復方向**: 需要實作 DMC DMA 的精確 cycle stealing，包括：
  1. DMC sample fetch 在 CPU read cycle 時 halt CPU
  2. 根據當前是 read/write cycle 決定額外 1~4 cycles
  3. OAM DMA + DMC DMA 同時進行時的交互

---

## P5 — 低影響、不修或暫緩（共 4 個測試）

### Bug G: PPU sprite timing 精確度
- **影響**: 4 個測試 FAIL，對大多數遊戲無明顯影響
- **難度**: 難
- **失敗測試**:
  - `sprite_hit_tests_2005.10.05/09.timing_basics` — sprite hit timing (#3)
  - `sprite_hit_tests_2005.10.05/10.timing_order` — sprite hit timing order
  - `sprite_overflow_tests/3.Timing` — sprite overflow timing (#5)
  - `sprite_overflow_tests/4.Obscure` — sprite overflow obscure behavior (#2)
- **根因**: Sprite 0 hit 和 sprite overflow 的精確觸發 cycle 有偏差。
- **備註**: 大多數遊戲不依賴精確的 sprite timing，暫緩修復。

### Bug H: read_joy3 thorough test
- **影響**: 1 個測試 FAIL，對一般遊戲無影響
- **難度**: 不明
- **失敗測試**:
  - `read_joy3/thorough_test` — 截圖全黑只顯示 Failed
- **備註**: 可能是搖桿讀取的 timing 問題或 DPCM 干擾。暫緩。

### Bug I: instr_misc 合併 ROM
- **影響**: 1 個測試 FAIL，但 4 個 singles 全部 PASS
- **難度**: 不需修（非模擬器 bug）
- **失敗測試**:
  - `instr_misc/instr_misc` — 合併 ROM 使用 Mapper 1 (MMC1)，在 test 3 of 4 的
    前置檢查 "$2002 mirroring every 8 bytes to $3FFA" 失敗，但 IO_read 正確處理了
    mirror。可能是 MMC1 合併 ROM 的 PPU timing sensitivity 問題。
- **備註**: 所有 4 個 individual ROM 都通過，不影響功能正確性。

### Bug J: blargg_ppu vbl_clear_time
- **影響**: 1 個測試 FAIL ($01)，與 Bug B (PPU timing) 相關
- **難度**: 隨 Bug B 一起修
- **失敗測試**:
  - `blargg_ppu_tests_2005.09.15b/vbl_clear_time` — $01

---

## 修復路線圖建議

```
Phase 1 (影響最大): Bug A — MMC3 A12 clocking
  → 預期修復 ~16 個 FAIL，直接提升至 ~129 PASS

Phase 2 (核心 timing): Bug B — PPU VBL/NMI timing
  → 預期修復 ~13 個 FAIL，提升至 ~142 PASS

Phase 3 (APU 精度): Bug C + D — APU frame counter timing
  → 預期修復 ~8 個 FAIL，提升至 ~150 PASS

Phase 4 (進階): Bug E + F — interrupt timing + DMC DMA
  → 預期修復 ~10 個 FAIL，提升至 ~160 PASS

Phase 5 (完善): Bug G/H/I/J — sprite timing + misc
  → 預期修復 ~4 個 FAIL，目標 ~164 PASS
```

---

*最後更新: 2026-02-21*
