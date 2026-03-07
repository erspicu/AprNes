# AprNes 測試總覽

**最後更新**: 2026-03-07 (BUGFIX45)

---

## 測試成績

| 測試套件 | 通過 | 總數 | 達成率 |
|----------|------|------|--------|
| blargg | 174 | 174 | 100% |
| AccuracyCoin | 118 | 136 | 87% |

- blargg 174/174 全 PASS 自 BUGFIX31 起維持至今
- AccuracyCoin 詳細修復追蹤見 [AccuracyCoin_TODO.md](AccuracyCoin_TODO.md)

---

## AccuracyCoin 剩餘問題摘要

17 FAIL + 1 SKIP，全部屬於高難度：

| 分類 | 項目數 | 根因 |
|------|--------|------|
| DMA sub-cycle 精度 | 6+1+5 | P13 前置條件 + P20 + P10 SH*，共用同一根因 |
| DMC/APU 複雜互動 | 3 | P14 DMC/APU Reg/Controller Strobe |
| PPU per-dot 精度 | 3 | P19 BG Serial In / Sprites SL0 / $2004 Stress |
| DMC DMA 累積偏移 | 1 | P12 IRQ Flag Latency (Test E hang) |

**最高 ROI**: 修好 P13 `DMADMASync_PreTest` 可能一次解鎖 +6~+12 項。

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
