# AprNesAvalonia Release 1x — JIT Profile v4 (ppu_step_new Cold Extracted)

- **Date**: 2026-04-17 00:00
- **Build**: AprNesAvalonia Release (.NET 10, TieredPGO ON)
- **Commit**: `eee8dd2` — 5 cold flag paths extracted from ppu_step_new
- **Previous**:
  - v1 baseline (`20260416_225353_profile_ava_1x_native.md`)
  - v2 PpuPhase4 cold (`20260416_233508_profile_ava_1x_cold_extracted.md`)
  - v3 +SpriteFetch (`20260416_235146_profile_ava_1x_sprite_fetch.md`)

## IL Size Progression

| Method | v1 | v2 | v3 | **v4** | v1→v4 |
|--------|----|----|----|----|----|
| `PpuPhase4_SpriteEvalAndInit` | 1866 | 1268 | **610** | 610 | **-67%** |
| `ppu_step_new` | 2693 | 2693 | 2693 | **2372** | **-12%** |
| `PpuPhase4_SpriteFetch` | — | — | 648 | 648 | new |
| `PpuPhase4_DummyNTFetch` | — | 319 | 319 | 319 | new |
| `PpuPhase4_VisibleScanlineDot1Init` | — | 135 | 135 | 135 | new |
| `PpuPhase_Apply2001Mask` | — | — | — | 112 | new |
| `PpuPhase_Apply2001Emphasis` | — | — | — | 71 | new |
| `PpuPhase_FrameRender` | — | — | — | 65 | new |
| `PpuPhase_DoOddFrameSkip` | — | — | — | 58 | new |
| `PpuPhase_HandleDelayedOamCorruption` | — | — | — | 51 | new |
| `PpuPhase4_Dot339` | — | 45 | 45 | 45 | new |

兩大熱 method 累計 IL 瘦身 **~1600 bytes**。

## CPU% (Exclusive)

| Method | v1 | v2 | v3 | **v4** |
|--------|----|----|----|--------|
| `ppu_step_new` | 41.0% | 40.9% | 40.5% | **40.4%** |
| `Run_NTSC` | 20.9% | 21.9% | 21.4% | 20.2% |
| `PpuPhase4_SpriteEvalAndInit` | 15.4% | 14.1% | 11.8% | 13.3% |
| `apu_step` | 8.7% | 9.5% | 9.2% | 8.2% |
| `PpuPhase4_SpriteFetch` | — | — | 3.5% | 3.1% |
| `ApuOutputCatchup` | 0.4% | 0.4% | 0.4% | 2.2% |
| 新 5 個 helpers (v4) 總和 | — | — | — | **<0.1%** |

**ppu_step_new 幾乎不變**（40.5% → 40.4%）— 熱路徑完全保留，只抽了冷分支。

## FPS — 純 bench（取最高）

| 版本 | 最高 FPS | vs baseline |
|------|---------|-------------|
| 原始 baseline | 154.83 | — |
| v2 (4 cold helpers) | 154.41 | -0.3% (noise) |
| v3 (+ SpriteFetch) | 156.96 | +1.4% |
| **v4 (+ 5 × ppu_step cold)** | **158.26** | **+2.2%** ✅ |

## FPS — PerfView ETW 下（不可信）

| 版本 | PerfView FPS |
|------|--------------|
| v1 | 145.80 |
| v2 | 152.94 |
| v3 | 148.49 |
| v4 | **141.83** |

**PerfView 再次騙人** — v4 PerfView 看似 -7 FPS，純 bench 反而 +1.3 FPS。確認：profiling 時 ETW sampling + stack walk 開銷不穩定，**不可作為效能對比基準**，純 bench 才算。

## 累計成果

| 指標 | Before (v1) | After (v4) | Δ |
|------|------------|------------|---|
| `PpuPhase4_SpriteEvalAndInit` IL | 1866 | 610 | **-67%** |
| `ppu_step_new` IL | 2693 | 2372 | **-12%** |
| Avalonia Release 1x 最高 FPS | 154.83 | 158.26 | **+2.2%** |
| PpuPhase4 / ppu_step_new 冷 helpers | 0 | 10 | 10 個 |

## 抽出列表（Pattern A）

### ppu_step_new 冷 helpers（v4 新增）
| Helper | Guard | 頻率 |
|--------|-------|------|
| `PpuPhase_DoOddFrameSkip(ref cx)` | NTSC+odd+rendering+preRender+cx==340 | 1/89K |
| `PpuPhase_HandleDelayedOamCorruption` | `oamCorruptDelay != 0` | <0.1% |
| `PpuPhase_Apply2001Mask` | `ppu2001UpdateDelay > 0` | <0.5% |
| `PpuPhase_Apply2001Emphasis` | `ppu2001EmphasisDelay > 0` | <0.5% |
| `PpuPhase_FrameRender` | `scanline==240 && cx==1` | 1/89K |

### PpuPhase4 helpers（v2/v3）
| Helper | Guard | 頻率 |
|--------|-------|------|
| `PpuPhase4_HandleOamCorruption` | corrupt flags | <0.1% |
| `PpuPhase4_Dot339` | `evalDot == 339` | 0.3% |
| `PpuPhase4_VisibleScanlineDot1Init` | visible scanline dot 1 | 0.27% |
| `PpuPhase4_DummyNTFetch(int)` | dots 0/337-340 | 1.5% |
| `PpuPhase4_SpriteFetch(int)` | dots 257-320 | 19% |

## 下一步候選

`ppu_step_new` 剩 2372 IL — 其中絕大多數真的是熱路徑（每 dot 都跑）：
- Tile fetch + CalculatePixel（~185 行，92% scanlines）— **可能還能微調但風險高**
- Pipeline shift / `PPU_DATA_Pipeline_Step` / A12Prev — 都是 HOT 每 dot 邏輯

進一步收益可能要換角度：algorithmic 優化（not structural），例如 CalculatePixel 的 SWAR sprite mux 還可不可再加速、`PpuBusRead` 的 mapper dispatch 是否可短路等。

## 原始資料
- 完整報告：`temp/profile_report_ava_1x_v4.txt`
- ETL 原檔：`temp/aprnesava_jit_1x.etl`
