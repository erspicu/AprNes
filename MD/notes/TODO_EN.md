# AprNes Test Overview

**Last Updated**: 2026-03-14 (BUGFIX56 — ALL TESTS PASS)

---

## Test Results

| Test Suite | Passed | Total | Pass Rate |
|------------|--------|-------|-----------|
| blargg | 174 | 174 | 100% |
| AccuracyCoin | 136 | 136 | 100% |

- blargg 174/174 all PASS maintained since BUGFIX31
- AccuracyCoin 136/136 all PASS achieved at BUGFIX56 (PERFECT SCORE)
- AccuracyCoin detailed fix tracker: [AccuracyCoin_TODO.md](AccuracyCoin_TODO.md)

---

## AccuracyCoin — All Done

All 136 tests pass. History: 118/136 (BUGFIX45) → 132/136 (Per-cycle CPU) → 136/136 (BUGFIX56).

---

## Fix History (BUGFIX30-45)

| BUGFIX | Date | Fix | AC Score |
|--------|------|-----|----------|
| 30 | 03-04 | Branch dummy reads, CPU open bus, controller open bus | ~76→? |
| 31 | 03-06 | Load DMA parity fix | blargg 174/174 |
| 32 | 03-06 | Load DMA cpuCycleCount parity | blargg 174/174 |
| 33 | 03-07 | AccuracyCoin page-by-page test runner | Test framework |
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
| 45 | 03-07 | $2002 flag clear timing stagger (M2 duty cycle) | 117→118 |
