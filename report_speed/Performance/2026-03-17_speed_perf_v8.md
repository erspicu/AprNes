# NesCoreSpeed Performance Benchmark – v8

## Test Environment

| Item | Value |
|------|-------|
| Date | 2026-03-17 20:09:50 |
| ROM | Mega Man 5 (U).nes |
| Duration | 20 seconds |
| Mode | Headless, No audio, No FPS cap |
| OS | Microsoft Windows NT 6.2.9200.0 |
| CPU | AMD Ryzen 7 3700X 8-Core Processor              |
| Runtime | .NET Framework 4.8.1 JIT |

## Results

| Frames (20s) | Average FPS |
|-------------|-------------|
| 4596 | 229.80 |

## Notes

SP-10 inline Mem_r/Mem_w fast paths (avoid 512KB table)

