# CrtScreen.Simd.cs — Baseline FPS Before .NET 10 SIMD Optimizations

- **Date**: 2026-04-16
- **Build**: AprNesAvalonia **Release** (.NET 10, TieredPGO)
- **Commit**: `0c0495b` (post-rename to CrtScreen.Simd.cs, byte-identical to CrtScreen.cs)
- **ROM**: ny2011 (NROM, NTSC, 30s benchmark)
- **Config**: `--audio-mode 2 --ultra-analog --analog-output RF --crt`

---

## Baseline FPS

| Analog Size | Run 1 | Run 2 | Avg |
|-------------|-------|-------|-----|
| **4x**      | 113.08 | 105.63 | **~109** |
| **6x**      | 89.53 | 85.65 | **~87.5** |
| **8x**      | 74.53 | 70.04 | **~72.3** |

---

## Profile Context (from previous 4x analysis)

主要 CPU 瓶頸（排序）：
- `ApplyFullFrameCurvatureAndConvergence`: **45.8% at 8x** / 19.7% at 4x
- `Crt_Render`: 23.5% at 4x / 30.6% at 8x
- `DemodulateRow_Core`: 11% at 4x (parallel total) / 3.7% at 8x
- `ApplyHorizontalBlur`: 1.0% at 4x / 0.4% at 8x
- `ppu_step_new`: 14.2% at 4x / 5.1% at 8x

---

## Planned .NET 10 SIMD Optimizations (one method at a time)

1. **ApplyFullFrameCurvatureAndConvergence** — `Avx2.GatherVector256` 取代 scalar gather
   預期收益：4x +5-10%, 8x +20-40%（最大單點 ROI）
2. **ProcessRowConvergence** / per-row gather helper
3. **Crt_Render** main pass — Vector256<T> explicit + Fma.MultiplyAdd
4. **ApplyHorizontalBlur** — 確認 Vector256 展開
5. **[SkipLocalsInit]** 逐 method 加上，消除 stackalloc zero-init

每個 method 優化後會重跑 4x / 6x / 8x 並記錄進 this file 的延伸。

---

## Session Policy

- 每次只改 **一個 method**
- 改完 → build → 184/184 blargg sanity (WinForms path 確保 shared Ntsc.cs 沒壞)
- → Avalonia Release benchmark 4x / 6x / 8x
- → 記錄進 MD 比較
- 如果回歸立即還原；如果持平保留討論後決定
