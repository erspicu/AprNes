# NesCoreSpeed Performance Benchmark – v3

## Test Environment

| Item | Value |
|------|-------|
| Date | 2026-03-17 19:45:51 |
| ROM | Mega Man 5 (USA).nes |
| Duration | 20 seconds |
| Mode | Headless, No audio, No FPS cap |
| OS | Microsoft Windows NT 6.2.9200.0 |
| CPU | AMD Ryzen 7 3700X 8-Core Processor              |
| Runtime | .NET Framework 4.8.1 JIT |

## Results

| Frames (20s) | Average FPS |
|-------------|-------------|
| 4072 | 203.60 |

## Notes

SP-3 palette precompute + bgcolor hoist + 64b BG clear + SP-4 APU loop unroll

