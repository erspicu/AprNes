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

## #002 AggressiveInlining cleanup + Delay counter + Scanline partitioning
- **日期**: 2026-04-07
- **Branch**: feature/performance-optimization
- **變更**:
  - 移除 8 個過大/低頻 method 的 AggressiveInlining（ppu_step_new, register handlers）
  - 加入 RenderBGTile 的 AggressiveInlining（16 行 hot path）
  - Delay counter 改為 branch-prediction-friendly 模式（`!= 0 && --x == 0`）
  - **Scanline partitioning**: cache `isActiveScanline` bool，VBlank 期間跳過 sprite eval + tile fetch + pixel calc + draw 整個 rendering pipeline

| Run | Frames | Duration | FPS |
|-----|--------|----------|-----|
| JIT (discarded) | 913 | 10.01s | 91.25 |
| Run 2 | 1794 | 20.01s | **89.66** |
| Run 3 | 1844 | 20.01s | **92.18** |
| **Average** | — | — | **90.92** |

**vs #001 baseline**: 87.19 → 90.92 = **+4.3%**
**vs master**: 264.45 → 90.92 = -65.6%

---

## #003-#008 Cumulative Optimizations
- **日期**: 2026-04-07
- **Branch**: feature/performance-optimization
- **變更摘要**:
  - #003: $2007 SM 提取為獨立方法 + sprite loop unrolling with goto
  - #004: Sprite bit7 fast-check `(H|L) >= 128`
  - #005: Sprite arrays 轉 unsafe pointer（Marshal.AllocHGlobal）
  - #006: SpriteEvalTick + SpriteEvalWrite 合併（guard clauses + compressed ops）
  - #007: PrecomputePreRenderSprites bitwise 地址計算
  - #008: MasterClockTick 互斥分支 + run() batch execution + masterPerPpuHalf 預計算

| Optimization | FPS | vs Baseline |
|---|---|---|
| #001 Baseline | 87.19 | — |
| #002 Scanline partition | 90.92 | +4.3% |
| #004 bit7 fast-check | 92.21 | +5.8% |
| #005 unsafe arrays | 94.27 | +8.1% |
| #007 PreRender bitwise | 94.65 | +8.6% |
| **#008 MasterClockTick opt** | **94.53** | **+8.4%** |

**最終結果**: 87.19 → **94.53 FPS (+8.4%)**
**vs master**: 264.45 → 94.53 = -64.3%

> 注意：#008 數據波動較大（Run2=96.36, Run3=92.69），平均 94.53 與 #007 的 94.65 在誤差範圍內。
> 互斥分支 + batch 的結構改善是正確方向，效能差異需更多樣本確認。

---

*後續優化紀錄將依序添加於此。每筆包含：日期、commit、變更摘要、FPS 數據、與 baseline 比較。*
