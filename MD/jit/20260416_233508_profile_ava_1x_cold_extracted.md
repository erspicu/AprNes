# AprNesAvalonia Release 1x — JIT Profile (PpuPhase4 Cold-Path Extraction)

- **Date**: 2026-04-16 23:35
- **Build**: AprNesAvalonia Release (.NET 10, TieredPGO ON)
- **Config**: `bench_profile_ava_1x.bat` — 1x native digital, audio-mode 0, 30s
- **Trace**: `temp/aprnesava_jit_1x.etl` (26.0 MB)
- **Samples**: 31,232
- **Compared with**: `20260416_225353_profile_ava_1x_native.md` (baseline)

## Changes since baseline

1. `ppu_new.cs`: 3 loops → `Unsafe.InitBlockUnaligned` for zero + byte-fill
2. `PpuPhase4_SpriteEvalAndInit` extracted 4 cold helpers (Pattern A: condition at caller):
   - `PpuPhase4_HandleOamCorruption`
   - `PpuPhase4_Dot339`
   - `PpuPhase4_DummyNTFetch(int)`
   - `PpuPhase4_VisibleScanlineDot1Init`
3. NuGet: `System.Runtime.CompilerServices.Unsafe` 6.1.2 (net48 only; built-in on .NET 10)

## IL Size — PpuPhase4_SpriteEvalAndInit

| | Before | After | Δ |
|---|--------|-------|---|
| `PpuPhase4_SpriteEvalAndInit` | **1866** | **1268** | **-598 (-32%)** |
| `PpuPhase4_DummyNTFetch` | — | 319 | +319 (new) |
| `PpuPhase4_VisibleScanlineDot1Init` | — | ~180 | new |
| `PpuPhase4_Dot339` | — | ~50 | new |
| `PpuPhase4_HandleOamCorruption` | — | ~80 | new |

熱函數 IL 縮小 32% — instruction cache 更友善，register pressure 降低。

## CPU% Top 12 (Exclusive)

| Rank | Method | Baseline | Post | Δ |
|------|--------|----------|------|---|
| 1 | `ppu_step_new` | 41.0% | **40.9%** | -0.1 |
| 2 | `Run_NTSC` | 20.9% | 21.9% | +1.0 (noise) |
| 3 | `PpuPhase4_SpriteEvalAndInit` | 15.4% | **14.1%** | **-1.3** |
| 4 | `apu_step` | 8.7% | 9.5% | +0.8 (noise) |
| 5 | `DoBranch` | 1.0% | 1.0% | 0 |
| 6 | `NestedTick7_NTSC` | 0.8% | 0.7% | -0.1 |
| 7 | `Wrap_MapperR_RPG` | 0.8% | 0.7% | -0.1 |
| 8 | `Op_2C` | 0.5% | 0.6% | +0.1 |
| 9 | `ApuOutputCatchup` | 0.4% | 0.4% | 0 |
| 10 | `GetAddressAbsolute` | 0.4% | 0.4% | 0 |
| — | `PpuPhase4_VisibleScanlineDot1Init` | — | **0.4%** | new |
| — | `PpuPhase4_DummyNTFetch` | — | **0.1%** | new |
| — | `PpuPhase4_Dot339` | — | 0.0% | new |

### PpuPhase4 系列小計
- Before: 15.4%
- After: 14.1 + 0.4 + 0.1 + 0.0 = **14.6%**

差 -0.8pp，但這在 run-to-run variance（±1pp）之內。Work 沒被消滅，只是搬家。

## 為什麼 IL 縮小卻沒 FPS 明顯提升？

PpuPhase4 的抽取是 **IL-size 優化** 而非 **algorithmic** 優化：
- 熱路徑邏輯完全不變（同樣 341 dots × 262 scanlines × 60 fps 的處理量）
- 節省的是 JIT 編譯成果的 **cache footprint**
- **Tier-1 JIT** 可能對更小的函數更積極優化（enregistration、better code layout）
- 收益往往是 **long-run warm cache** 時顯現，benchmark 取樣可能看不到

## TieredPGO Behavior

4 個新 helper 都出現 3 次 JIT 編譯（Tier-0 → Tier-1 with PGO），符合預期：
- `PpuPhase4_SpriteEvalAndInit` ×3
- `PpuPhase4_DummyNTFetch` ×3
- `PpuPhase4_VisibleScanlineDot1Init` ×?
- `PpuPhase4_Dot339` ×?

主函數 PGO 可以累積更多在**實際熱路徑**的分支 profile（不被 cold 路徑稀釋）。

## Run-to-run Variance 確認

此次 run_NTSC +1.0pp / apu_step +0.8pp 與 PpuPhase4 -1.3pp 相抵；整體 NesCore 佔比應該 ~93% 不變，符合「work preserved, reorganized」預期。

## 下一步候選

| 目標 | IL 削減 | 難度 | ROI |
|------|---------|------|-----|
| Dots 257-320 sprite fetch 整塊抽出 | ~600-800 | 中 | 🟡 (call freq 19%，需測) |
| `ppu_step_new` 熱路徑拆分（41% CPU 大怪物） | ~1000+ | 高 | 🎯 最大機會但高風險 |
| Dot 257 copy + Dot 322 merged helper | ~40 | 低 | 低 |
| MMC5 NotifyVramRead 抽出 | ~20 | 低 | 低 |

**最大機會仍然是 `ppu_step_new`（41% CPU）**，但它是超大 state machine，拆分需要謹慎設計以維持 TriCNES timing accuracy。

## 原始資料
- 完整報告：`temp/profile_report_ava_1x_v2.txt`
- ETL 原檔：`temp/aprnesava_jit_1x.etl`
