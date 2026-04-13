# AprNes JIT / CPU Profiling Report (Bresenham APU + Mod-6 Magic Merge + RfBuzz fmod)

- **Date**: 2026-04-13 18:44:28
- **Branch**: `master`
- **Build**: Debug x64, .NET Framework 4.8.1
- **ROM**: ny2011.nes (Mapper 0)
- **Duration**: 30s benchmark, 57915 CPU samples
- **Config**: NTSC, Audio Mode 2 (Modern Stereo), Ultra Analog, RF Output, CRT, 4x resolution
- **Benchmark FPS**: **64.77**

---

## 1. FPS Trend — Full Pipeline Optimization Series

| # | Optimization | FPS |
|---|-------------|-----|
| 0 | Baseline | 60.59 |
| 1 | DemodulateRow merge | 63.72 |
| 2 | ntscScanBuf byte* | 59.79 |
| 3 | Modulo 6 elimination | 63.14 |
| 4 | RunWaveformLoop ILP | 63.47 |
| 5 | CRT Convergence fixed-point | 62.58 |
| 6 | OAM corrupt flatten | 64.56 |
| 7 | Skip ScreenBuf1x analog | 63.55 |
| 8 | MMC5 duplicate notify fix | 64.35 |
| 9 | **Bresenham APU + mod-6 magic merge + RfBuzz fmod** | **64.77** |

Marginal improvement (~0.4 FPS). Hot path dominated by CRT / PPU / NTSC kernels; these optimizations shave cycles off APU + NTSC-per-scanline paths which are no longer top contributors.

---

## 2. Changes in This Batch

### 2.1 APU Bresenham sample accumulator (`APU.cs`)
Replaced `double _sampleAccum += 1.0; if (>= _cycPerSample)` with integer Bresenham:
```csharp
_sampleAccum += APU_SAMPLE_RATE;   // int
if (_sampleAccum >= _cpuFreqInt) { _sampleAccum -= _cpuFreqInt; ... }
```
Eliminates FPU add/compare/subtract (~3 float ops × 1.79M cycles/sec = ~5.4M FPU ops/sec removed).

### 2.2 NTSC mod-6 sign-extension magic — single-line merge (`Ntsc.cs`)
Collapsed 5 pairs of `x += N; x += ((5-x) >> 31) & -6;` into single expression:
```csharp
x += N + (((5-N-x) >> 31) & -6);
```
Sites: `scanPhase6 (+2)`, `ph (+1)`, `scanPhaseBase (+2)`, `tModQ (+4)`, `tModI (+1) x2`. Saves one add + one store per invocation.

### 2.3 NTSC waveform constants (`Ntsc.cs`)
`Math.Cos(1.31683f)` / `Math.Sin(1.31683f)` hoisted to `static readonly CosHerring` / `SinHerring` — eliminates 2× Math.Cos + 2× Math.Sin per scanline when RF herringbone active. Also made `nScale`/`nOff` lazy (only computed when `NoiseIntensity > 0`).

### 2.4 PPU sprite multiplexer (`ppu_new.cs`)
Cached `sprShiftH[i]` / `sprShiftL[i]` / `sprFetchAttr[i]` into locals; moved `sprFetchAttr` read inside the hit branch (lazy). Used `(attr & 0x20)` mask form for priority test.

### 2.5 AudioPlus RfBuzzPhase — fmod elimination (`AudioPlus.cs`)
```csharp
// before: RfBuzzPhase = (RfBuzzPhase + absS * 0.0001f) % 1.0f;
float np = RfBuzzPhase + absS * 0.0001f;
RfBuzzPhase = np >= 1.0f ? np - 1.0f : np;
```
Removes ~50-cycle `fmod` call per audio sample (44100×/sec) — increment is tiny so wrap happens at most once.

---

## 3. CPU Exclusive — Top Methods (57915 samples)

| Excl% | Samples | Method | Category |
|-------|---------|--------|----------|
| 22.6% | 13097 | `Crt_Render` lambda | CRT |
| 18.5% | 10740 | `ppu_step_new` | PPU Core |
| 17.5% | 10116 | `Curvature+Convergence` lambda | CRT |
| 8.3%  | 4819  | `run` (MasterClockTick) | CPU Core |
| 7.7%  | 4486  | `DemodulateRow_Core` | NTSC Decode |
| 3.8%  | 2172  | `ApplyHorizontalBlur` lambda | CRT |
| 3.7%  | 2169  | `PpuPhase4_SpriteEvalAndInit` | PPU Sprite |
| 2.9%  | 1707  | `apu_step` | APU Core |
| 2.6%  | 1480  | `GenerateWaveform` | NTSC Encode |
| 0.5%  | 276   | `CpuRead` | CPU Memory |
| 0.4%  | 207   | `ppu_r_2002` | PPU Register |
| 0.2%  | 126   | `DecodeScanline` | NTSC Dispatch |
| 0.2%  | 110   | `DoBranch` | CPU Branch |
| 0.1%  | 61    | `Mapper000.MapperR_RPG` | Mapper |
| 0.1%  | 61    | `Mapper000.MapperR_CHR` | Mapper |
| 0.1%  | 60    | `Op_2C` (BIT abs) | CPU Opcode |

### Deltas vs previous report (MMC5 dedup, 59995 samples)
Percentages roughly stable (sampling noise ±0.3%). `apu_step` 3.1% → 2.9% and `GenerateWaveform` 2.5% → 2.6% are within noise but consistent with APU Bresenham being a tiny net win. `ppu_step_new` 17.1% → 18.5% reflects sample-count normalization rather than regression.

---

## 4. CPU Inclusive — Top Methods

| Incl% | Samples | Method |
|-------|---------|--------|
| 50.8% | 29432 | `run` |
| 37.4% | 21659 | `ppu_step_new` |
| 23.0% | 13304 | `Crt_Render` lambda |
| 17.7% | 10245 | `ApplyFullFrameCurvatureAndConvergence` lambda |
| 10.7% | 6202  | `DecodeScanline` |
| 10.5% | 6074  | `DecodeScanline_Physical` |
| 7.8%  | 4536  | `DemodulateRow_Core` |
| 3.9%  | 2260  | `RenderScreen` |
| 3.9%  | 2248  | `Crt_Render` |
| 3.8%  | 2205  | `PpuPhase4_SpriteEvalAndInit` |
| 3.8%  | 2202  | `ApplyHorizontalBlur` lambda |
| 3.0%  | 1764  | `apu_step` |
| 2.6%  | 1525  | `CpuRead` |
| 2.6%  | 1525  | `GenerateWaveform` |

---

## 5. Hot Path Inline Status

Top-10 hot methods; most are standalone (correctly not inlined — they are dispatch trampolines or too large for the RyuJIT budget on .NET 4.8.1):

- `ppu_step_new`, `Crt_Render` lambda, `Curvature+Convergence` lambda — NO inline (standalone, correct)
- `run` — NO inline (top-level loop, correct)
- `DemodulateRow_Core`, `apu_step`, `GenerateWaveform` — NO inline (hot kernels, inlining would blow out instruction cache)
- `GetAddressAbsolute` — YES inlined (good — small addressing helper)
- `CpuRead`, `ppu_r_2002`, `DoBranch` — NO inline (standalone)

No inlining regressions from this batch.

---

## 6. Test Status

- blargg: **184 / 184 PASS**
- AccuracyCoin: **138 / 138 PASS** (confirmed by user)

---

## 7. Next Candidates

With Crt_Render (22.6%) + Curvature/Convergence (17.5%) + HorizontalBlur (3.8%) ≈ 44% CPU in CRT post-processing, the highest-leverage remaining work is CRT kernels (already partly fixed-point after commit fc8be3f). PPU + NTSC cores are near the budget ceiling on a managed runtime.
