# NesCoreSpeed Performance Benchmark – v5

## Test Environment

| Item | Value |
|------|-------|
| Date | 2026-03-17 19:52:45 |
| ROM | Mega Man 5 (USA).nes |
| Duration | 20 seconds |
| Mode | Headless, No audio, No FPS cap |
| OS | Microsoft Windows NT 6.2.9200.0 |
| CPU | AMD Ryzen 7 3700X 8-Core Processor              |
| Runtime | .NET Framework 4.8.1 JIT |

## Results

| Frames (20s) | Average FPS |
|-------------|-------------|
| 4017 | 200.85 |

## Notes

SP-1+SP-2+SP-3 stackalloc tilePal+SP-5 byte BG array (no SP-4 APU unroll)

