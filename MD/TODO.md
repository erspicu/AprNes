# AprNes 待修復問題清單

**基線**: blargg 174/174 PASS | AccuracyCoin 117/136 PASS, 18 FAIL, 1 SKIP
**最後更新**: 2026-03-07 (BUGFIX44)
**達成率**: blargg 100% | AccuracyCoin 86%

---

## 現況總結

從 BUGFIX30 到 BUGFIX44，AccuracyCoin 從約 76 分提升到 117 分（+41）。
剩餘 18 個 FAIL 均屬於**高難度**項目，每項都已經過研究或嘗試修復：

| 難度 | 項目數 | 說明 |
|------|--------|------|
| 困難 | 5 | SH* 指令 — CPU 矽片修訂版差異，非標準行為 |
| 困難 | 6 | DMA 系列 — 共用前置條件失敗，需 sub-cycle 精度 |
| 困難 | 3 | DMC / APU Reg / Controller Strobe — 複雜 bus 互動 |
| 困難 | 1 | $2002 flag stagger — 修了會回歸 P17 (-2) |
| 困難 | 3 | PPU misc — 需 secondary OAM 持久化、per-dot shift model |

**結論**: 要再提高分數，需要投入大量架構改動或精確子周期時序，風險較高。

---

## AccuracyCoin 剩餘 18 FAIL + 1 SKIP（按頁面分組）

### P10: Unofficial Instructions: SH* (5 FAIL)

| 測試 | 地址 | err | 狀態 |
|------|------|-----|------|
| $93 SHA (indirect),Y | $0446 | 7 | 未修 |
| $9F SHA absolute,Y | $0447 | 7 | 未修 |
| $9B SHS absolute,Y | $0448 | 7 | 未修 |
| $9C SHY absolute,X | $0449 | 7 | 未修 |
| $9E SHX absolute,Y | $044A | 7 | 未修 |

**分析**: 測試檢查 CPU 矽片「Behavior 1/2/3」差異。我們實作的是標準 `REG & (H+1)` masking，
但測試還檢查 DMA 期間的退化行為和「magic number」。err=7 表示行為識別本身就失敗了。
**難度**: 高 — 需深入理解 CPU 矽片修訂版差異，可能需要選擇特定 behavior 模式實作。

### P12: IRQ Flag Latency (1 SKIP)

| 測試 | 地址 | err | 狀態 |
|------|------|-----|------|
| IRQ Flag Latency | $0461 | SKIP | Test E 掛住 |

**分析**: Test A-D PASS，Test E 因 DMC DMA 累積時序偏移 (~12 cycles) 而掛住。
**難度**: 極高 — 需 Master Clock scheduler 或 sub-cycle DMC timing。

### P13: DMA Tests (6 FAIL)

| 測試 | 地址 | err | 狀態 |
|------|------|-----|------|
| DMA + $2002 Read | $0488 | 2 | 前置條件失敗 |
| DMA + $4015 Read | $045D | 2 | 前置條件失敗 |
| DMC DMA Bus Conflicts | $046B | 2 | 前置條件失敗 |
| DMC DMA + OAM DMA | $0477 | 2 | 前置條件失敗 |
| Explicit DMA Abort | $0479 | 2 | 前置條件失敗 |
| Implicit DMA Abort | $0478 | 2 | 前置條件失敗 |

**分析**: 全部 6 項共用前置條件 `DMADMASync_PreTest` 失敗（err=2）。
前置條件測試 DMC DMA 是否正確更新 data bus，我們的時序有 1-cycle drift。
**難度**: 極高 — 需 sub-cycle M2 duty cycle 精度。修好前置條件可能一次解鎖 +6。

### P14: APU Tests (3 FAIL)

| 測試 | 地址 | err | 狀態 |
|------|------|-----|------|
| Delta Modulation Channel | $046A | 21 | 多項子測試失敗 |
| APU Register Activation | $045C | 6 | Test 4 已修，Test 5-7 極複雜 |
| Controller Strobing | $045F | 1 | Test 4 失敗（PUT/GET parity） |

**DMC (err=21)**: 高錯誤碼，表示 DMC 通道多個子測試失敗。需全面 DMC 行為修正。
**APU Reg Activation (err=6)**: BUGFIX44 修了 Test 4（OAM DMA 不觸發 APU 副作用）。
Test 5-7 需從 $3FFE 執行 STA $4014 配合 DMC DMA 把 6502 bus 放到 $4000 range，極度複雜。
**Controller Strobing (err=1)**: Test 3 PASS（DEC $4016 strobe on PUT），
Test 4 FAIL（DEC $4016 should NOT strobe on GET）。Deferred strobe 機制與 Mesen2 一致，
可能是 OAM DMA 後的 parity 不準。

### P18: Sprite Evaluation (1 FAIL)

| 測試 | 地址 | err | 狀態 |
|------|------|-----|------|
| $2002 flag clear timing | $048D | 1 | 嘗試失敗 |

**分析**: 需要把 VBL 和 sprite flags 的清除時序分開（M2 duty cycle effect）。
**已嘗試**: 把 sprite flags 清除放 dot 2、VBL 清除放 dot 3 → **回歸 P17 VBL timing (-2 FAIL)**。
**難度**: 高 — 需要不影響 P17 的方式實現 sub-dot flag stagger。

### P19: PPU Misc (3 FAIL)

| 測試 | 地址 | err | 狀態 |
|------|------|-----|------|
| BG Serial In | $0487 | 2 | 未修 |
| Sprites On Scanline 0 | $0484 | 2 | 已研究 |
| $2004 Stress Test | $048C | 1 | 未修 |

**BG Serial In**: 需 per-dot BG shift register reload 時序模型，目前 shadow-only。
**Sprites On SL 0**: Pre-render line (261) 的 sprite tile fetch 用 `261 & 255 = 5` 做 Y check。
需要 secondary OAM 跨 scanline 持久化 + pre-render line sprite fetch 邏輯。架構改動大。
**$2004 Stress Test**: 需 OAM evaluation latch — rendering 期間 $2004 返回 evaluation 暫存器值而非直接 OAM。

### P20: CPU Behavior 2 (1 FAIL)

| 測試 | 地址 | err | 狀態 |
|------|------|-----|------|
| Implied Dummy Reads | $046D | 3 | 前置條件失敗 |

**分析**: err=3 的前置條件依賴 DMA timing（同 P13 DMADMASync_PreTest）。
Implied dummy reads 本身已正確實作，修好 P13 前置條件可能連帶解決。

---

## 已嘗試但未成功的修復

| 目標 | 嘗試 | 結果 | 原因 |
|------|------|------|------|
| P18 $2002 flag clear | Dot 2→3 VBL stagger | P17 -2 回歸 | VBL end timing 被延後 1 dot |
| P18 $2002 flag clear | Dot 1 統一清除 | 無改善 | 原本就是 dot 2 清除 |
| P14 APU Reg Test 6 | $4020-$40FF mirror to $4000-$401F | P14 -1 回歸 | Controller Strobing 受影響 |
| P14 Controller Strobing | 翻轉 strobe parity | -1 回歸 | 其他測試依賴當前 parity |

---

## 可能的突破口

1. **P13 DMA 前置條件 (+6~+8)**: 如果能修好 `DMADMASync_PreTest`，可能一次解鎖 P13 全部 6 項 + P20 的 1 項 + P14 的部分測試。這是**投資報酬率最高**的方向，但需要精確到 sub-cycle 的 DMC DMA open bus timing。

2. **P10 SH* Behavior (+5)**: 需確認目標 behavior 類型。如果 AccuracyCoin 接受 Behavior 1（`A & X & (H+1)`），我們的實作可能只需微調。如果需要 Behavior 2（需 magic number），則複雜度大增。

---

## 已完成的 AccuracyCoin 修復（BUGFIX30-44）

| BUGFIX | 日期 | 修復內容 | AC 分數 |
|--------|------|---------|---------|
| 30 | 03-04 | Branch dummy reads, CPU open bus, controller open bus | ~76→? |
| 31 | 03-06 | Load DMA parity fix | blargg 174/174 |
| 32 | 03-06 | Load DMA cpuCycleCount parity | blargg 174/174 |
| 33 | 03-07 | AccuracyCoin page-by-page test runner | 測試框架 |
| 34 | 03-07 | Unofficial opcodes batch fix | 103→108 |
| 35 | 03-07 | Arbitrary sprite zero + misaligned OAM | 108→110 |
| 36 | 03-07 | OAM corruption | 110→111 |
| 37 | 03-07 | PPU register open bus + $2004 during rendering | 111→112 |
| 38 | 03-07 | INC $4014 + palette RAM quirks | 112→113 |
| 39 | 03-07 | Attributes as tiles + t register quirks | 113→114 |
| 40 | 03-07 | Stale BG shift registers + deferred Load DMA | 114→115 |
| 41 | 03-07 | $2007 read during rendering | 115→116 |
| 42 | 03-07 | Suddenly resize sprite (sprite size latch at dot 261) | 116→117 |
| 43 | 03-07 | Rendering flag behavior (freeze BG shift regs when off) | 116→117 |
| 44 | 03-07 | OAM DMA APU activation bypass + debug cleanup | 117→117 |

---

*blargg 174/174 全 PASS 自 BUGFIX31 起維持至今*
