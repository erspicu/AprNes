# AccuracyCoin 修復追蹤

**基線**: 121/136 PASS, 14 FAIL, 1 SKIP (BUGFIX48)
**最後更新**: 2026-03-08

---

## 各頁面狀態

| Page | 主題 | 狀態 | 備註 |
|------|------|------|------|
| P1-P9 | CPU Behavior / Unofficial Opcodes | 全 PASS | |
| P10 | Unofficial: SH* | 5 FAIL / 6 | LAE PASS，SHA/SHX/SHY/SHS FAIL |
| P11 | Unofficial: Misc | 全 PASS | |
| P12 | CPU Interrupts | 1 SKIP / 3 | IFlagLatency Test E hang |
| P13 | DMA Tests | 6 FAIL / 6 | 共用前置條件失敗 |
| P14 | APU Tests | 2 FAIL / 7 | DMC/Controller Strobe |
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

### Phase 3: TIMING-DEPENDENT（部分完成）

- [x] $2007 read w/ rendering (P16) — BUGFIX34
- [x] Stale BG Shift Registers (P19) — BUGFIX40
- [x] Suddenly Resize Sprite (P18) — BUGFIX42
- [x] Sprites On Scanline 0 (P19) — **BUGFIX47**: secondary OAM + per-dot eval FSM + pre-render sprite data
- [x] $2004 Stress Test (P19) — **BUGFIX48**: per-dot $2004 read accuracy, attribute masking at read level, post-eval OAM2 read

---

## 剩餘 14 FAIL + 1 SKIP

### 根因 A: DMA Sub-cycle 精度（12 項，共用根因）

所有這些測試都需要 sub-cycle DMA bus state 精度。P13 的 `DMADMASync_PreTest` 是關鍵前置條件。

**P13: DMA Tests (6 FAIL)** — 全部 err=2，前置條件失敗

| 測試 | 地址 | 分析 |
|------|------|------|
| DMA + $2002 Read | $0488 | phantom reads 碰 $2002 應有 side effects |
| DMA + $4015 Read | $045D | phantom reads 碰 $4015 應清 IRQ flag |
| DMC DMA Bus Conflicts | $046B | DMA 讀 $4000-$401F 應 bus conflict |
| DMC DMA + OAM DMA | $0477 | DMC + OAM DMA 重疊 cycle count |
| Explicit DMA Abort | $0479 | DMA 中止時 stolen cycle 數 |
| Implicit DMA Abort | $0478 | 隱式 DMA 中止 stolen cycle 數 |

**P10: SH* Instructions (5 FAIL)** — 全部 err=7

| 測試 | 地址 | 分析 |
|------|------|------|
| $93 SHA (indirect),Y | $0546 | Behavior 1 基本正確，DMA bus conflict 測試失敗 |
| $9F SHA absolute,Y | $0547 | 同上 |
| $9B SHS absolute,Y | $0548 | 同上 |
| $9C SHY absolute,X | $0549 | 同上 |
| $9E SHX absolute,Y | $054A | 同上 |

測試期望 DMA 發生在 SH* write cycle 前會消除 H 的 AND masking（value = A & X，不再 & H）。

**P20: CPU Behavior 2 (2 FAIL)** — 前置條件依賴 DMA timing

| 測試 | 地址 | err | 分析 |
|------|------|-----|------|
| Instruction Timing | $0460 | 2 | DMA timing 前置條件失敗 |
| Implied Dummy Reads | $046D | 3 | 指令本身已正確，被 DMA 前置條件擋住 |

### 根因 B: DMC/APU 複雜互動（2 項）

**P14: APU Tests (2 FAIL)**

| 測試 | 地址 | err | 分析 |
|------|------|-----|------|
| Delta Modulation Channel | $046A | 21 | 多項 DMC 子測試失敗，需全面修正 |
| APU Register Activation | $045C | — | **PASS** (BUGFIX46: $4017 read handler + ProcessDmaRead open bus fix) |
| Controller Strobing | $045F | 1 | Test 4 PUT/GET parity，OAM DMA 後 parity 不準 |

### 根因 C: PPU Per-dot 精度（已全部完成）

**P19: PPU Misc (全 PASS)**

| 測試 | 地址 | err | 分析 |
|------|------|-----|------|
| BG Serial In | $0487 | — | **PASS** (BUGFIX BGSerialIn) |
| Sprites On Scanline 0 | $0484 | — | **PASS** (BUGFIX47: secondary OAM + per-dot eval FSM) |
| $2004 Stress Test | $048C | — | **PASS** (BUGFIX48: per-dot $2004 read, attr masking, post-eval OAM2 read) |

### 根因 D: DMC DMA 累積偏移（1 項）

**P12: IRQ Flag Latency (1 SKIP)**

| 測試 | 地址 | 分析 |
|------|------|------|
| IRQ Flag Latency | $0461 | Test A-D PASS，Test E hang（DMC DMA ~12 cycle drift） |

需要 Master Clock scheduler 或 sub-cycle DMC timing。

---

## 突破口分析

| 方向 | 潛在收益 | 難度 | 說明 |
|------|----------|------|------|
| P13 DMA 前置條件 | +6~+13 | 極高 | 修好 DMADMASync_PreTest 可解鎖 P13(6) + P20(2) + P10(5) |
| P14 Controller Strobe | +1 | 高 | PUT/GET parity 問題，嘗試過翻轉但回歸 |
| P19 $2004 Stress | ~~+1~~ | ~~高~~ | **已完成 BUGFIX48** |
| Master Clock 重構 | +16 | 極高 | 理論上解決所有問題，但工程量巨大 |

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
