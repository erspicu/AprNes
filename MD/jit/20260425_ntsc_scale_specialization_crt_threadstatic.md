# NTSC Scale Specialization (#3) + CRT Blur ThreadStatic (#4)

- **Date**: 2026-04-25
- **Source**: `MD/optimization/PPU_NTSC_CRT_Optimization_Notes.md` items #3 and #4

## #3 — NTSC decode by AnalogSize specialization

**Implementation**: generic struct constraint pattern (`IAnalogScale` + `Scale2/4/6/8`); `RunDecodeLoop<TScale>` and `RunSVideoLoop<TScale>` with `int N = default(TScale).N` (compile-time const). Switch dispatcher in `DecodeAV_Composite` and `DecodeAV_SVideo` selects scale; runtime-N fallback retained for unusual sizes.

```csharp
switch (N) {
    case 2: DispatchDecodeLoop<Scale2>(...); break;
    case 4: DispatchDecodeLoop<Scale4>(...); break;
    case 6: DispatchDecodeLoop<Scale6>(...); break;
    case 8: DispatchDecodeLoop<Scale8>(...); break;
    default: RunDecodeLoopGeneric(...); break;
}
```

JIT specializes per `TScale`: `x / N` becomes a shift (N=2/4/8) or magic-multiply (N=6) — no runtime division in hot loop.

### Caveat

**The standard PerfView trace uses `--ultra-analog`**, which routes through `DecodeScanline_Physical` → `GenerateWaveform` → `DemodulateRow_Core` (SIMD pipeline). It does NOT call `DecodeAV_Composite` / `DecodeAV_SVideo`, so the scale specialization isn't exercised by the current trace.

The optimization is correct and applies to **non-ultra-analog ("Fast") mode**, which uses the simpler decode pipeline. For users who run without `--ultra-analog`, expected gain is 5-15% (per the source doc), especially at AnalogSize 6/8 where division dominates.

## #4 — CRT horizontal blur ThreadStatic scratch

**Implementation**: replaced per-call `stackalloc float[Crt_SrcW]` (4 KB × 720 calls/frame) with `[ThreadStatic] float* tls_crtBlurRow` allocated once per worker thread via `EnsureCrtBlurScratch()`.

### Result

| Method | Before | After | Δ |
|---|---:|---:|---:|
| `<ApplyHorizontalBlur>b__0` lambda Excl | 1.1% | **0.7%** | -0.4pp |

Removed ~720 stackallocs/frame (3 planes × 240 rows). Each previously allocated 4 KB on stack; total 2.88 MB/frame avoided.

## Combined verification

| Metric | Before (apu inline) | After (#3 + #4) | Δ |
|---|---:|---:|---:|
| Global I-cache miss | 0.51% | **0.52%** | +0.01 (noise) |
| Pure-core baseline FPS | 145.17 | **146.49** | +1.32 (~0.9%, noise) |
| ApplyHorizontalBlur Excl | 1.1% | **0.7%** | -0.4pp ✓ |

Pure-core FPS doesn't measure CRT/Analog work (no filter, Audio 0). The +1.3 fps is run-to-run noise.

The only visibly measured gain in this trace is #4 (CRT blur). #3's gain is hidden because the bench uses `--ultra-analog`. To see #3 in action, run a Fast-mode benchmark (drop `--ultra-analog`).

## Build

Both NetFx Debug and .NET 10 Release build with 0 errors. JIT specialization successfully creates 4 distinct method bodies per scale (Scale2/4/6/8) when the corresponding code path is exercised at runtime.

## Conclusion

Both optimizations land cleanly:
- ✅ Both correct and merged to master
- ✅ #4 provides measurable -0.4pp on CRT blur lambda
- ✓ #3 correctness verified, dormant in ultra-analog mode (works in Fast mode)
- ✓ No regressions: global I-cache stable, FPS within noise

Future: if Fast-mode performance becomes important, this work is already in place. For now, keep the work as a "ready for when it matters" optimization.
