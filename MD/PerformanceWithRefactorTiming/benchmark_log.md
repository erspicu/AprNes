# AprNes Performance Log — TriCNES Refactor Timing Model

追蹤 feature/fetch-port 分支（TriCNES PPU/DMA/Fetch 移植後）的效能優化歷程。

## 測試條件
- **Config**: NTSC / 1x (256x240) / Audio Mode 0 (Pure Digital) / No filters
- **ROM**: ny2011.nes (Mapper 0)
- **Protocol**: JIT warmup 10s (discarded) → 30s cooldown → Run2 20s → 30s cooldown → Run3 20s → average
- **Platform**: Windows 11, .NET Framework 4.8.1, Debug build

## 歷史基準（master 分支）
| 日期 | 版本 | FPS | 備註 |
|------|------|-----|------|
| 2026-03-18 | master (pre-refactor) | **264.45** | AccuracyOptA=ON, Release build |

---

## #001 Baseline — TriCNES Refactor 完成後
- **日期**: 2026-04-06 23:13
- **Branch**: feature/fetch-port @ e82711b
- **狀態**: 174/174 NTSC + 10/10 PAL + 136/136 AC (全滿分)
- **變更**: TriCNES PPU port + DmaFetch bus conflict + OAM corruption delay + $2007 SM fix + PAL region

| Run | Frames | Duration | FPS |
|-----|--------|----------|-----|
| JIT (discarded) | 885 | 10.00s | 88.46 |
| Run 2 | 1739 | 20.00s | **86.93** |
| Run 3 | 1750 | 20.01s | **87.45** |
| **Average** | — | — | **87.19** |

**vs master baseline**: 264.45 → 87.19 = **-67.0%**

TriCNES 移植（per-dot PPU step + full bus conflict + OAM corruption delay model）帶來顯著的效能成本。後續需要針對 hot path 持續優化。

---

*後續優化紀錄將依序添加於此。每筆包含：日期、commit、變更摘要、FPS 數據、與 baseline 比較。*
