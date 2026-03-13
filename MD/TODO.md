# AprNes 測試總覽

**最後更新**: 2026-03-14 (BUGFIX56 — ALL TESTS PASS)

---

## 測試成績

| 測試套件 | 通過 | 總數 | 達成率 |
|----------|------|------|--------|
| blargg | 174 | 174 | 100% |
| AccuracyCoin | 136 | 136 | 100% |

- blargg 174/174 全 PASS 自 BUGFIX31 起維持至今
- AccuracyCoin 136/136 全 PASS 自 BUGFIX56 達成（PERFECT SCORE）
- AccuracyCoin 詳細修復追蹤見 [AccuracyCoin_TODO.md](AccuracyCoin_TODO.md)

---

## AccuracyCoin — 全部完成

所有 136 項測試全數通過。歷程：118/136 (BUGFIX45) → 132/136 (Per-cycle CPU) → 136/136 (BUGFIX56)。

---

## 修復歷史（BUGFIX30-45）

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
| 45 | 03-07 | $2002 flag clear timing stagger (M2 duty cycle) | 117→118 |
