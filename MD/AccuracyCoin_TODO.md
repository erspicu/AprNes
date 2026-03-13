# AccuracyCoin 修復追蹤

**基線**: 136/136 PASS, 0 FAIL, 0 SKIP ✓ PERFECT
**最後更新**: 2026-03-14
**分支**: master

---

## 各頁面狀態

| Page | 主題 | 狀態 | 備註 |
|------|------|------|------|
| P1-P9 | CPU Behavior / Unofficial Opcodes | 全 PASS | |
| P10 | Unofficial: SH* | 全 PASS | Per-cycle CPU rewrite + SH* fix |
| P11 | Unofficial: Misc | 全 PASS | |
| P12 | CPU Interrupts | 全 PASS | Per-cycle CPU rewrite 修復 IFlagLatency |
| P13 | DMA Tests | 全 PASS | BUGFIX53-56 修復全部 DMA 測試 |
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

### Phase 2: TIMING-CORE（全部完成）

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

### Phase 2.8: DMC DMA Bus Conflicts + Deferred Status

- [x] DMC DMA Bus Conflicts (P13) — **BUGFIX54**: bus conflict rewrite + deferred $4015 status update

### Phase 2.9: Explicit DMA Abort

- [x] Explicit DMA Abort (P13) — **BUGFIX55**: 2-cycle fire window detection + parity-dependent normal delay

### Phase 3.0: Implicit DMA Abort

- [x] Implicit DMA Abort (P13) — **BUGFIX56**: 1-cycle phantom DMA + write cycle cancellation

---

## 全部完成

所有 136 項 AccuracyCoin 測試全數通過。blargg 174/174 無回歸。

---

## 已解決的根因

- ~~根因 B: DMC/APU 複雜互動~~ — **已修復 BUGFIX49** (P14 全 PASS)
- ~~根因 C: PPU Per-dot 精度~~ — **已修復** (P19 全 PASS)
- ~~根因 D: DMC DMA 累積偏移~~ — **已修復** Per-cycle CPU rewrite (P12 全 PASS)
- ~~P10 SH* unofficial opcodes~~ — **已修復** (P10 全 PASS)
- ~~P20 CPU Behavior 2~~ — **已修復** Per-cycle CPU rewrite (P20 全 PASS)
- ~~P13 Explicit DMA Abort~~ — **已修復 BUGFIX55** (2-cycle fire window + parity delay)
- ~~P13 Implicit DMA Abort~~ — **已修復 BUGFIX56** (1-cycle phantom DMA + write cycle cancellation)
