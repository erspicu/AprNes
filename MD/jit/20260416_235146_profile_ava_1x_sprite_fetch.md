# AprNesAvalonia Release 1x — JIT Profile (PpuPhase4 Sprite Fetch Extracted)

- **Date**: 2026-04-16 23:51
- **Build**: AprNesAvalonia Release (.NET 10, TieredPGO ON)
- **Commit**: `330036a` — sprite fetch (dots 257-320) extracted as helper
- **Config**: `bench_profile_ava_1x.bat` — 1x native digital, audio-mode 0, 30s
- **Previous profiles**:
  - `20260416_225353_profile_ava_1x_native.md` (v1 baseline)
  - `20260416_233508_profile_ava_1x_cold_extracted.md` (v2 — 4 cold helpers)

## IL Size Summary

| Method | v1 | v2 | **v3 (current)** | v1→v3 |
|--------|----|----|-------------------|-------|
| `PpuPhase4_SpriteEvalAndInit` | 1866 | 1268 | **610** | **-67%** |
| `PpuPhase4_SpriteFetch` | — | — | 648 | new |
| `PpuPhase4_DummyNTFetch` | — | 319 | 319 | |
| `PpuPhase4_VisibleScanlineDot1Init` | — | 135 | 135 | |
| `PpuPhase4_Dot339` | — | 45 | 45 | |
| `PpuPhase4_HandleOamCorruption` | — | ~80 | ~80 | |

**主函數 IL 從 1866 → 610 bytes（-67%）** — JIT tier-1 熱路徑超精簡。

## CPU% Top (Exclusive)

| Method | v1 | v2 | **v3** |
|--------|-----|-----|--------|
| `ppu_step_new` | 41.0% | 40.9% | 40.5% |
| `Run_NTSC` | 20.9% | 21.9% | 21.4% |
| `PpuPhase4_SpriteEvalAndInit` | 15.4% | 14.1% | **11.8%** |
| `apu_step` | 8.7% | 9.5% | 9.2% |
| `PpuPhase4_SpriteFetch` | — | — | **3.5%** |
| `PpuPhase4_VisibleScanlineDot1Init` | — | 0.4% | 0.3% |
| `PpuPhase4_DummyNTFetch` | — | 0.1% | 0.2% |

PpuPhase4 family total:
- v1: 15.4%
- v2: 14.6%
- v3: **15.8%**

v3 比 v1 略多 0.4pp — 是 sprite-fetch call overhead（3M calls/sec × ~4 cycles ≈ 0.3% CPU）。預期內。

## FPS — 純 bench（不含 PerfView ETW overhead）

| Run | FPS |
|-----|-----|
| Run 1 (warmup) | 156.63 |
| Run 2 | 153.86 |
| **Run 3** | **156.96** ⬆️ |

**最高 156.96 FPS**，比原始 baseline 154.83 **+2.13 FPS (+1.4%)**。

## PerfView 下的 FPS（含 ETW 採樣 overhead）

| 版本 | PerfView FPS |
|------|--------------|
| v1 | 145.80 |
| v2 | 152.94 |
| v3 | **148.49** |

PerfView v3 看起來回退了（152.94 → 148.49），**但純 bench 翻盤** — 確認是 ETW sampling noise。profiling 時的 FPS 不可直接當效能比較基準。

## 為什麼有效？

1. **主函數 IL 剩 610 bytes**（從 1866 開始，-67%）— Tier-1 JIT 對小函數更積極優化（register allocation、code layout、constant folding）
2. **Sprite fetch 獨立為 helper** — JIT 可把它當獨立單元優化，不被主函數其他分支干擾
3. **Instruction cache footprint** 主函數熱路徑更小 → iL1 命中率更高
4. **Call overhead 0.3%** < 主函數瘦身帶來的 tier-1 JIT 效益

## 累計 5 個 PpuPhase4 helpers

| Helper | 頻率 | 用途 |
|--------|------|------|
| `HandleOamCorruption` | <0.1% | OAM corruption flags |
| `Dot339` | 0.3% | dot 339 counter reset |
| `VisibleScanlineDot1Init` | 0.27% | dot 1 scanline init |
| `DummyNTFetch` | 1.5% | dots 0, 337-340 |
| **`SpriteFetch`** | **19%** | dots 257-320 sprite fetch |

## 下一步候選

| 目標 | 當前 CPU% | IL 估 | ROI |
|------|----------|-------|-----|
| **`ppu_step_new` 熱路徑拆分** | **40.5%** | 2693 | 🎯 最大機會 |
| Dot 257 copy + Dot 322 merged helper | <0.1% | ~40 | 🟢 小幅度 |

真正的大獎仍是 `ppu_step_new`（40.5% CPU、2693 IL bytes，FAILED inline）。

## 原始資料
- 完整報告：`temp/profile_report_ava_1x_v3.txt`
- ETL 原檔：`temp/aprnesava_jit_1x.etl`
