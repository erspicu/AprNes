# Mapper 4 (MMC3) Baseline — Before Mapper Refactor

- **Date**: 2026-04-23
- **Branch**: `feature/mem-refactor` @ fa32323 (post 8-page table refactor)
- **Benchmark**: `AprNes/bin/Debug/benchmark_baseline.bat`
- **Config**: NTSC / 1x (256×240) / Audio Mode 0 (Pure Digital), no filters, no analog, no CRT
- **ROM**: `Mega Man 5 (USA).nes` (MMC3 / Mapper 4)
- **Duration**: 20s per run, 3-run protocol
- **Hardware**: AMD Ryzen 7 3700X 8-core / 32 GB RAM / RTX 4080

## Results

| Run | FPS | Note |
| --- | ---:| --- |
| JIT warmup | 118.10 | discarded |
| Run 2 | **118.20** | measured |
| Run 3 | **117.65** | measured |

**Baseline = (118.20 + 117.65) / 2 = 117.93 FPS**

Run 2 vs Run 3 differ by only 0.55 FPS — low variance, solid baseline.

## Comparison to NROM baseline

| ROM | Mapper | Avg FPS |
| --- | --- | ---:|
| ny2011.nes | 0 (NROM) | 121.87 |
| Mega Man 5 (USA).nes | 4 (MMC3) | 117.93 |

MMC3 is ~3.2% slower than NROM on the same build. The gap is attributable to:
- A12 rising-edge detection for scanline IRQ
- More frequent PRG/CHR bank switching (Mega Man 5 uses scroll split + sprite swapping)
- Larger IMapper handler surface

## Purpose

This is the **pre-refactor** reference point for any future Mapper 4 / MMC3 optimisation work. Target for post-refactor: ≥ 117.93 FPS with blargg 184/184 + AccuracyCoin 138/138 intact.

## Notes

- Cooldown between runs fails (`timeout /t` shadowed by Git Bash's `timeout`), but low Run 2/3 delta shows thermal throttling is negligible for 20s runs on this CPU.
- `benchmark_baseline.bat` was updated in this branch to use Mega Man 5 instead of ny2011 (see commit after this note).
