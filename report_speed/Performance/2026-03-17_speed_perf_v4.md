# NesCoreSpeed Performance Benchmark – v4

## Test Environment

| Item | Value |
|------|-------|
| Date | 2026-03-17 19:49:52 |
| ROM | Mega Man 5 (USA).nes |
| Duration | 20 seconds |
| Mode | Headless, No audio, No FPS cap |
| OS | Microsoft Windows NT 6.2.9200.0 |
| CPU | AMD Ryzen 7 3700X 8-Core Processor              |
| Runtime | .NET Framework 4.8.1 JIT |

## Results

| Frames (20s) | Average FPS |
|-------------|-------------|
| 4097 | 204.85 |

## Notes

SP-5 byte BG array (4x smaller, 8x faster clear) + SP-3 stackalloc tilePal + SP-4 APU unroll

