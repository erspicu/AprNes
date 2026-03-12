# AccuracyCoin 修復追蹤

**基線**: 133/136 PASS, 3 FAIL, 0 SKIP
**最後更新**: 2026-03-13
**分支**: master

---

## 各頁面狀態

| Page | 主題 | 狀態 | 備註 |
|------|------|------|------|
| P1-P9 | CPU Behavior / Unofficial Opcodes | 全 PASS | |
| P10 | Unofficial: SH* | 全 PASS | Per-cycle CPU rewrite + SH* fix |
| P11 | Unofficial: Misc | 全 PASS | |
| P12 | CPU Interrupts | 全 PASS | Per-cycle CPU rewrite 修復 IFlagLatency |
| P13 | DMA Tests | 3 FAIL / 10 | 7 PASS，BUGFIX53 修復 DMA+$2002 Read |
| P14 | APU Tests | 全 PASS | BUGFIX49: DMC enable delay always set |
| P15 | Power On State | DRAW only | 無自動判定 |
| P16 | PPU Rendering | 全 PASS | |
| P17 | PPU VBlank Timing | 全 PASS | |
| P18 | Sprite Evaluation | 全 PASS | BUGFIX45 修復最後一項 |
| P19 | PPU Misc | 全 PASS | BUGFIX48 修復 $2004 Stress Test |
| P20 | CPU Behavior 2 | 全 PASS | Per-cycle CPU rewrite 修復 |

---

## 已完成修復（按 Phase 分類）

### Phase 1: INDEPENDENT（全部完成）

- [x] Controller Strobing (P14) — BUGFIX33+39
- [x] Address $2004 behavior (P18) — BUGFIX34+41
- [x] Rendering Flag Behavior (P16) — BUGFIX33
- [x] Arbitrary Sprite zero (P18) — BUGFIX35
- [x] Misaligned OAM behavior (P18) — BUGFIX35
- [x] OAM Corruption (P18) — BUGFIX36
- [x] INC $4014 (P18) — BUGFIX38

### Phase 2: TIMING-CORE（部分完成）

- [x] Frame Counter IRQ (P14) — BUGFIX37
- [x] $2002 flag clear timing (P18) — **BUGFIX45**: sprite flags dot 1, VBL dot 2

### Phase 2.5: DMA BUS（全部完成）

- [x] APU Register Activation (P14) — BUGFIX46: $4017 read handler + ProcessDmaRead open bus
- [x] Delta Modulation Channel (P14) — **BUGFIX49**: DMC enable delay always set regardless of buffer state

### Phase 2.6: Per-cycle CPU + SH* + DMC DMA Cooldown（全部完成）

- [x] Per-cycle CPU rewrite — 全指令逐 cycle 執行，修復 P12 IFlagLatency + P20 Timing/Dummy Reads (+4)
- [x] SH* unofficial opcodes — SHA/SHX/SHY/SHS DMA bus conflict 正確實作 (+5)
- [x] DMC DMA cooldown — TriCNES CannotRunDMCDMARightNow，防止連續 DMC DMA (+1)

### Phase 2.7: DMA Load Countdown Timing

- [x] DMA + $2002 Read (P13) — **BUGFIX53**: DMC Load DMA countdown uses TriCNES-style GET-only decrement

### Phase 3: TIMING-DEPENDENT（全部完成）

- [x] $2007 read w/ rendering (P16) — BUGFIX34
- [x] Stale BG Shift Registers (P19) — BUGFIX40
- [x] Suddenly Resize Sprite (P18) — BUGFIX42
- [x] Sprites On Scanline 0 (P19) — **BUGFIX47**: secondary OAM + per-dot eval FSM + pre-render sprite data
- [x] $2004 Stress Test (P19) — **BUGFIX48**: per-dot $2004 read accuracy, attribute masking at read level, post-eval OAM2 read

---

## 剩餘 3 FAIL

### 根因: DMA Sub-cycle 精度（3 項，共用根因）

所有剩餘失敗都在 P13 DMA Tests，需要更精確的 DMA bus state / stolen cycle 時序。

**P13: DMA Tests (3 FAIL / 10 total)**

| 測試 | 狀態 | err | 分析 |
|------|------|-----|------|
| DMA + Open Bus | **PASS** | — | |
| DMA + $2002 Read | **PASS** | — | **BUGFIX53**: DMC Load DMA countdown timing |
| DMA + $2007 Read | **PASS** | — | |
| DMA + $2007 Write | **PASS** | — | |
| DMA + $4015 Read | **PASS** | — | Per-cycle CPU 修復 |
| DMA + $4016 Read | **PASS** | — | |
| DMC DMA Bus Conflicts | FAIL | 2 | bus conflict 合併時序 |
| DMC DMA + OAM DMA | **PASS** | — | Per-cycle CPU 修復 |
| Explicit DMA Abort | FAIL | 2 | DMA 中止 stolen cycle 數 |
| Implicit DMA Abort | FAIL | 2 | 隱式 DMA 中止 stolen cycle 數 |

### 已解決的根因

- ~~根因 B: DMC/APU 複雜互動~~ — **已修復 BUGFIX49** (P14 全 PASS)
- ~~根因 C: PPU Per-dot 精度~~ — **已修復** (P19 全 PASS)
- ~~根因 D: DMC DMA 累積偏移~~ — **已修復** Per-cycle CPU rewrite (P12 全 PASS)
- ~~P10 SH* unofficial opcodes~~ — **已修復** (P10 全 PASS)
- ~~P20 CPU Behavior 2~~ — **已修復** Per-cycle CPU rewrite (P20 全 PASS)

---

## 突破口分析

| 方向 | 潛在收益 | 難度 | 說明 |
|------|----------|------|------|
| P13 DMA timing | +4 | 極高 | 需要更精確的 DMA stolen cycle / phantom read 時序 |
| 136/136 完美分數 | +4 | 極高 | 剩餘 4 項全在 P13，共用 DMA sub-cycle 根因 |

---

## 已嘗試但未成功的修復

| 目標 | 嘗試 | 結果 | 原因 |
|------|------|------|------|
| P18 $2002 flag clear | Dot 2→3 VBL stagger | P17 -2 回歸 | VBL end timing 被延後 1 dot |
| P18 $2002 flag clear | Dot 1 統一清除 | 無改善 | sprite flags 需比 VBL 更早清除 |
| P18 $2002 flag clear | **Dot 1 sprite / Dot 2 VBL** | **PASS** | **BUGFIX45** |
| P14 APU Reg Test 6 | $4020-$40FF mirror | P14 -1 回歸 | Controller Strobing 受影響 |
| P14 Controller Strobing | 翻轉 strobe parity | -1 回歸 | 其他測試依賴當前 parity |
| P14 APU Reg Activation | cpuLastReadAddr tracking | err 4→6 惡化 | 已 revert |
| P19 $2004 Stress | oamCopyBuffer + secondaryOam | **已修復 BUGFIX48** | 需 attr mask at read + post-eval OAM2 read |
| DMA timing | Reorder ProcessPendingDma before StartCpuCycle | 172/174, AC 114/136 | irq_and_dma 失敗 (OAM parity shift)，AC 反而變差 -7 |
| DMA timing | Reorder + OAM parity compensation tick | 170/174 | 多出 1 CC，破壞 sprite overflow timing |
| DMA timing | dmcDmaPending deferred flag | 168/174 | DMC halt CC shift +1 → getCycle parity flip |
| DMA timing | dmcDmaTriggerCycle same-cycle skip | 168/174 | 同上：halt CC = X+2 (應為 X+1) |
| DMA timing | Load DMA countdown +1 (3/4) | 170/174 | 全域偏移，非目標性修正 |
| DMA timing | DMC defer + DMA before Start | 172/174, AC 121/136 | sprdma ±1 cycle，AC 無改善 |
| DMA timing | DMC defer + getCycle flip | 170/174 | irq_and_dma 失敗 |
| DMA timing | DMC defer + pre-increment CC | 168/174 | APU 在 DMA 中位置偏移 |
| DMA timing | DMC defer + OAM-aware | 172/174, AC 121/136 | DMC-only DMA 仍差 ±1 |

### 2026-03-10 P13 深入調查結論

| 嘗試 | 結果 | 原因 |
|------|------|------|
| 延遲 $4015 disable (TriCNES APU_DelayedDMC4015) | 174/174, AC 132/136 (無改善) | 延遲正確但不是根因 |
| 簡單 DMA reorder (ProcessPendingDma→StartCpuCycle) | 172/174, AC P13 不變 | irq_and_dma 回歸 |
| DMA reorder + getCycle parity flip | 168/174, P13 10/10 全 FAIL | 補償不正確 |

**P13 err=2 的具體含義** (從 AccuracyCoin ROM 錯誤碼確認):
- DMA + $2002 Read err=2: halt/alignment cycles 沒正確讀取 $2002
- DMC DMA Bus Conflicts err=2: 與 APU 暫存器的 bus conflict 不正確
- Explicit DMA Abort err=2: 被中止的 DMA stolen cycle 數量錯
- Implicit DMA Abort err=2: 被中止的 DMA stolen cycle 數量錯

**根本結論: 需要三個修復同時到位**

1. **Asymmetric DMA split** — Read cycle: DMA before StartCpuCycle; Write cycle: 不變
   - 修正 halt cycle phantom read 晚 1 CC 的問題
   - 最大結構改動，影響全部 174+136 測試
2. **Abort mechanism** — 延遲 $4015 disable + explicit/implicit abort stolen cycle 計數
   - TriCNES parity 映射已確認: post-toggle `APU_PutCycle` = 我們的 `!putCycle`
3. **Bus conflict 邏輯** — DMC DMA 讀 APU 暫存器時的 bus-OR 行為
   - 最獨立、最低風險

詳細修復計畫見 Claude memory: `p13_fix_plan.md`
