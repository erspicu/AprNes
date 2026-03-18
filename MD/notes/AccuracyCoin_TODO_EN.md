# AccuracyCoin Fix Tracker

**Baseline**: 136/136 PASS, 0 FAIL, 0 SKIP ✓ PERFECT
**Last Updated**: 2026-03-14
**Branch**: master

---

## Page Status

| Page | Topic | Status | Notes |
|------|-------|--------|-------|
| P1-P9 | CPU Behavior / Unofficial Opcodes | All PASS | |
| P10 | Unofficial: SH* | All PASS | Per-cycle CPU rewrite + SH* fix |
| P11 | Unofficial: Misc | All PASS | |
| P12 | CPU Interrupts | All PASS | Per-cycle CPU rewrite fixed IFlagLatency |
| P13 | DMA Tests | All PASS | BUGFIX53-56 fixed all DMA tests |
| P14 | APU Tests | All PASS | BUGFIX49: DMC enable delay always set |
| P15 | Power On State | DRAW only | No automatic judgment |
| P16 | PPU Rendering | All PASS | |
| P17 | PPU VBlank Timing | All PASS | |
| P18 | Sprite Evaluation | All PASS | BUGFIX45 fixed the last item |
| P19 | PPU Misc | All PASS | BUGFIX48 fixed $2004 Stress Test |
| P20 | CPU Behavior 2 | All PASS | Fixed by per-cycle CPU rewrite |

---

## Completed Fixes (by Phase)

### Phase 1: INDEPENDENT (all done)

- [x] Controller Strobing (P14) — BUGFIX33+39
- [x] Address $2004 behavior (P18) — BUGFIX34+41
- [x] Rendering Flag Behavior (P16) — BUGFIX33
- [x] Arbitrary Sprite zero (P18) — BUGFIX35
- [x] Misaligned OAM behavior (P18) — BUGFIX35
- [x] OAM Corruption (P18) — BUGFIX36
- [x] INC $4014 (P18) — BUGFIX38

### Phase 2: TIMING-CORE (all done)

- [x] Frame Counter IRQ (P14) — BUGFIX37
- [x] $2002 flag clear timing (P18) — **BUGFIX45**: sprite flags dot 1, VBL dot 2

### Phase 2.5: DMA BUS (all done)

- [x] APU Register Activation (P14) — BUGFIX46: $4017 read handler + ProcessDmaRead open bus
- [x] Delta Modulation Channel (P14) — **BUGFIX49**: DMC enable delay always set regardless of buffer state

### Phase 2.6: Per-cycle CPU + SH* + DMC DMA Cooldown (all done)

- [x] Per-cycle CPU rewrite — all instructions execute cycle-by-cycle, fixed P12 IFlagLatency + P20 Timing/Dummy Reads (+4)
- [x] SH* unofficial opcodes — SHA/SHX/SHY/SHS DMA bus conflict correctly implemented (+5)
- [x] DMC DMA cooldown — TriCNES CannotRunDMCDMARightNow, prevents consecutive DMC DMA (+1)

### Phase 2.7: DMA Load Countdown Timing

- [x] DMA + $2002 Read (P13) — **BUGFIX53**: DMC Load DMA countdown uses TriCNES-style GET-only decrement

### Phase 2.8: DMC DMA Bus Conflicts + Deferred Status

- [x] DMC DMA Bus Conflicts (P13) — **BUGFIX54**: bus conflict rewrite + deferred $4015 status update

### Phase 2.9: Explicit DMA Abort

- [x] Explicit DMA Abort (P13) — **BUGFIX55**: 2-cycle fire window detection + parity-dependent normal delay

### Phase 3.0: Implicit DMA Abort

- [x] Implicit DMA Abort (P13) — **BUGFIX56**: 1-cycle phantom DMA + write cycle cancellation

---

## All Done

All 136 AccuracyCoin tests pass. blargg 174/174 with no regressions.

---

## Resolved Root Causes

- ~~Root cause B: DMC/APU complex interaction~~ — **Fixed BUGFIX49** (P14 all PASS)
- ~~Root cause C: PPU Per-dot accuracy~~ — **Fixed** (P19 all PASS)
- ~~Root cause D: DMC DMA accumulated offset~~ — **Fixed** Per-cycle CPU rewrite (P12 all PASS)
- ~~P10 SH* unofficial opcodes~~ — **Fixed** (P10 all PASS)
- ~~P20 CPU Behavior 2~~ — **Fixed** Per-cycle CPU rewrite (P20 all PASS)
- ~~P13 Explicit DMA Abort~~ — **Fixed BUGFIX55** (2-cycle fire window + parity delay)
- ~~P13 Implicit DMA Abort~~ — **Fixed BUGFIX56** (1-cycle phantom DMA + write cycle cancellation)
