# AccuracyCoin 修復追蹤

**基線**: 122/136 PASS, 13 FAIL, 1 SKIP (BUGFIX49)
**最後更新**: 2026-03-08

---

## 各頁面狀態

| Page | 主題 | 狀態 | 備註 |
|------|------|------|------|
| P1-P9 | CPU Behavior / Unofficial Opcodes | 全 PASS | |
| P10 | Unofficial: SH* | 5 FAIL / 6 | LAE PASS，SHA/SHX/SHY/SHS FAIL (err=7) |
| P11 | Unofficial: Misc | 全 PASS | |
| P12 | CPU Interrupts | 1 SKIP / 3 | IFlagLatency Test E hang |
| P13 | DMA Tests | 6 FAIL / 10 | 4 PASS（Open Bus, $2007 R/W, $4016 R）|
| P14 | APU Tests | 全 PASS | BUGFIX49: DMC enable delay always set |
| P15 | Power On State | DRAW only | 無自動判定 |
| P16 | PPU Rendering | 全 PASS | |
| P17 | PPU VBlank Timing | 全 PASS | |
| P18 | Sprite Evaluation | 全 PASS | BUGFIX45 修復最後一項 |
| P19 | PPU Misc | 全 PASS | BUGFIX48 修復 $2004 Stress Test |
| P20 | CPU Behavior 2 | 2 FAIL / 4 | Instruction Timing / Implied Dummy Reads |

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

### Phase 2.5: DMA BUS（部分完成）

- [x] APU Register Activation (P14) — BUGFIX46: $4017 read handler + ProcessDmaRead open bus
- [x] Delta Modulation Channel (P14) — **BUGFIX49**: DMC enable delay always set regardless of buffer state

### Phase 3: TIMING-DEPENDENT（部分完成）

- [x] $2007 read w/ rendering (P16) — BUGFIX34
- [x] Stale BG Shift Registers (P19) — BUGFIX40
- [x] Suddenly Resize Sprite (P18) — BUGFIX42
- [x] Sprites On Scanline 0 (P19) — **BUGFIX47**: secondary OAM + per-dot eval FSM + pre-render sprite data
- [x] $2004 Stress Test (P19) — **BUGFIX48**: per-dot $2004 read accuracy, attribute masking at read level, post-eval OAM2 read

---

## 剩餘 13 FAIL + 1 SKIP

### 根因 A: DMA Sub-cycle 精度（12 項，共用根因）

所有這些測試都需要 sub-cycle DMA bus state 精度和正確的 DMA → CPU cycle 對齊。

**P13: DMA Tests (6 FAIL / 10 total)**

前置條件 (DMADMASync_PreTest) 已通過。4 項已 PASS，剩餘 6 項需 DMA timing 修正：

| 測試 | 狀態 | err | 分析 |
|------|------|-----|------|
| DMA + Open Bus | **PASS** | — | 前置條件通過 |
| DMA + $2002 Read | FAIL | 2 | phantom read 碰 $2002 時序不對（DMC 在同 Mem_r 觸發，phantom read 命中錯誤地址）|
| DMA + $2007 Read | **PASS** | — | |
| DMA + $2007 Write | **PASS** | — | |
| DMA + $4015 Read | FAIL | 2 | 同 $2002：phantom read 碰 $4015 時序不對 |
| DMA + $4016 Read | **PASS** | — | |
| DMC DMA Bus Conflicts | FAIL | — | bus conflict 合併時序 |
| DMC DMA + OAM DMA | FAIL | 1 | DMC+OAM 重疊 stolen cycle 數 |
| Explicit DMA Abort | FAIL | 1 | DMA 中止 stolen cycle 數 |
| Implicit DMA Abort | FAIL | 1 | 隱式 DMA 中止 stolen cycle 數 |

**P10: SH* Instructions (5 FAIL)** — 全部 err=7

| 測試 | 地址 | 分析 |
|------|------|------|
| $93 SHA (indirect),Y | $0546 | Behavior 1 基本正確，DMA bus conflict 測試失敗 |
| $9F SHA absolute,Y | $0547 | 同上 |
| $9B SHS absolute,Y | $0548 | 同上 |
| $9C SHY absolute,X | $0549 | 同上 |
| $9E SHX absolute,Y | $054A | 同上 |

測試期望 DMA 發生在 SH* write cycle 前會消除 H 的 AND masking（value = A & X，不再 & H）。

**P20: CPU Behavior 2 (2 FAIL)**

| 測試 | err | 分析 |
|------|-----|------|
| Instruction Timing | 2 | DMA timing 前置條件 |
| Implied Dummy Reads | 3 | 指令本身已正確，被 DMA 前置條件擋住 |

### ~~根因 B: DMC/APU 複雜互動~~ （已修復 BUGFIX49）

**P14: APU Tests — 全 PASS**

DMC enable delay 修正: 移除 `if (dmcBufferEmpty)` 條件，改為 Mesen2 風格的
always-set `_transferStartDelay`。

### 根因 C: PPU Per-dot 精度（已全部完成）

**P19: PPU Misc (全 PASS)**

### 根因 D: DMC DMA 累積偏移（1 項）

**P12: IRQ Flag Latency (1 SKIP)**

| 測試 | 分析 |
|------|------|
| IRQ Flag Latency | Test A-D PASS，Test E hang（DMC DMA ~12 cycle drift） |

需要 Master Clock scheduler 或 sub-cycle DMC timing。

---

## 突破口分析

| 方向 | 潛在收益 | 難度 | 說明 |
|------|----------|------|------|
| P13 DMA timing | +6 | 極高 | 需要 asymmetric MCU split 修正 phantom read 地址 |
| ~~P14 DMC~~ | ~~+1~~ | — | **已修復 BUGFIX49** |
| P10 SH* | +5 | 極高 | 需要 DMA RDY line during write cycle |
| P20 timing | +2 | 極高 | 被 DMA 前置條件擋住 |
| Asymmetric MCU Split | +13~+14 | 極高 | 理論上解決所有 DMA timing 問題 |

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

### 根本結論

12+0 的單相模型無法同時滿足 OAM DMA 和 DMC DMA 的 parity 需求。
OAM DMA 需要 StartCpuCycle 在 ProcessPendingDma 之前（halt CC = N+2），
DMC DMA 需要 ProcessPendingDma 在 StartCpuCycle 之前（延遲 1 cycle 觸發）。
Mesen2 用 5+7/7+5 不對稱分配 + 不同的 boot parity 解決此矛盾。
完整的 asymmetric MCU split 是唯一的解決方案，但風險極高（影響全部 174 tests）。
