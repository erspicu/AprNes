# AprNes Testing and Performance Tools Usage Guide

## Overview

AprNes provides three categories of tools:

| Tool | Purpose |
|------|---------|
| `AprNes.exe` CLI parameters | Run single ROM tests, take screenshots, obtain test results |
| `--benchmark` series parameters | Measure execution performance (FPS) across versions |
| `benchmark.ps1` | One-click four-version performance comparison |
| `run_tests.sh` | Run 174 ROM test suite (requires bash/WSL) |

---

## I. AprNes.exe — ROM Test Mode

Used to verify the Pass/Fail results of NES test ROMs (**only supported by the .NET Framework version**).

### Basic Syntax

```
AprNes.exe --rom <file.nes> [options...]
```

### Parameter Reference

| Parameter | Format | Description |
|-----------|--------|-------------|
| `--rom` | `--rom <path>` | **Required**. NES ROM path |
| `--time` | `--time <seconds>` | Run for this many seconds, then screenshot and exit (does not wait for test result) |
| `--wait-result` | (flag) | Wait for Blargg `$6000` protocol test result (0 = PASS, 1+ = FAIL) |
| `--max-wait` | `--max-wait <seconds>` | Maximum seconds to wait for result (default 30) |
| `--soft-reset` | `--soft-reset <seconds>` | Issue a soft reset at the specified time (for tests that require a reset to proceed) |
| `--input` | `--input "A:1.0,B:2.0"` | Simulate controller input; see format below |
| `--screenshot` | `--screenshot <out.png>` | Save a screenshot at the specified time |
| `--log` | `--log <results.log>` | Append results to a log file |
| `--pass-on-stable` | (flag) | Screen is stable and no "Failed" text → treated as PASS |
| `--expected-crc` | `--expected-crc "ABCD1234,EFGH5678"` | Treated as PASS when the displayed CRC matches any of the given values |
| `--debug-log` | `--debug-log <path>` | Write CPU debug trace to a file |
| `--debug-max` | `--debug-max <n>` | Maximum lines for debug trace (default 15000) |

### --input Format

```
"Button:press_time[:duration], ..."
```

- Button names: `A` `B` `Select` `Start` `Up` `Down` `Left` `Right` (case-insensitive)
- When duration is omitted, defaults to approximately 166ms (10 frames)

```bash
# Example: press Start at second 1, press A at second 3 for 0.5 seconds
AprNes.exe --rom test.nes --wait-result --input "Start:1.0,A:3.0:0.5"
```

### Result Detection Flow

1. Detect Blargg `$6000` protocol (`$6001-$6003` = `DE B0 61`)
2. If present → wait for `$6000 < $80` (0 = PASS, non-zero = FAIL code)
3. If absent → scan PPU nametable for `Passed` / `Failed` / `$01` / `0/` text
4. If still absent → read `$F0` (old blargg protocol)
5. Timeout → result `0xFF` (unknown)

### Result Output Format

```
PASS | cpu_timing_test.nes | Passed
FAIL(2) | nes_instr_test.nes | Error 02
```

### Common Examples

```bash
# Basic test (wait for $6000 protocol)
AprNes.exe --rom nes-test-roms-master/checked/cpu_timing_test6/cpu_timing_test6.nes --wait-result

# Test with time limit
AprNes.exe --rom blargg_test.nes --wait-result --max-wait 60

# Screenshot (screenshot after 5 seconds, no waiting for result)
AprNes.exe --rom mygame.nes --time 5 --screenshot out.png

# Test requiring a reset
AprNes.exe --rom reset_test.nes --wait-result --soft-reset 2.0

# CRC comparison test
AprNes.exe --rom ppu_vbl_nmi.nes --wait-result --expected-crc "A1B2C3D4"
```

---

## II. Performance Benchmark Mode (--benchmark series)

All three executable versions support the `--benchmark` parameter (headless mode, no window).

### 2.1 Standard benchmark

Common to all versions:

```
<exe> --benchmark <rom_path> [seconds] [output_file]
```

| Position | Description | Default |
|----------|-------------|---------|
| `<rom_path>` | **Required**. ROM file path | — |
| `[seconds]` | Test duration | `10` |
| `[output_file]` | Text file to append results to | No file, output to console only |

```bash
# .NET Framework 10-second test
AprNes\bin\Release\AprNes.exe --benchmark "game.nes" 10

# .NET 8 test, save results to result.txt
AprNesAOT\bin\Release\net8.0-windows\AprNesAOT.exe --benchmark "game.nes" 10 result.txt

# .NET 10 test
AprNesAOT10\bin\Release\net10.0-windows\AprNesAOT10.exe --benchmark "game.nes" 10 result.txt
```

Output format:
```
JIT [.NET 10 RyuJIT] :    7640 frames      764.0 FPS
```

> **Note**: `AprNesAOT.exe`'s `--benchmark` also simultaneously runs an AOT DLL (`NesCoreNative.dll`) benchmark, outputting two lines:
> ```
> JIT [.NET 8 RyuJIT]     :    7018 frames      701.8 FPS
> AOT [NesCoreNative]     :    5500 frames      550.0 FPS
> ```

---

### 2.2 SIMD Comparison Mode (AprNesAOT10 only)

Runs SIMD ON → SIMD OFF consecutively **within the same process** (good for a quick feel of the difference, but affected by JIT warm-up):

```
AprNesAOT10.exe --benchmark-simd <rom_path> [seconds] [output_file]
```

```bash
AprNesAOT10\bin\Release\net10.0-windows\AprNesAOT10.exe --benchmark-simd "spritecans.nes" 10
```

Output format:
```
[SIMD  ON ] running ...    5582 frames  558.2 FPS
[SIMD  OFF] running ...    6038 frames  603.8 FPS
[SIMD gain] -45.6 FPS  (-7.6%)
```

> ⚠️ **Limitation**: Within the same process, the JIT PGO state from the first test segment affects the second, making the data not entirely fair.

---

### 2.3 Force SIMD OFF Mode (AprNesAOT10 only)

Tests in **separate processes**; pair with a normal `--benchmark` run to form a fair comparison:

```
AprNesAOT10.exe --benchmark-nosimd <rom_path> [seconds] [output_file]
```

```powershell
# Fair SIMD comparison (two independent processes, swap order and average)
$exe = "AprNesAOT10\bin\Release\net10.0-windows\AprNesAOT10.exe"
$rom = "spritecans.nes"

# Round 1: SIMD ON first
Start-Process -FilePath $exe -ArgumentList "--benchmark", $rom, 10, "on.txt"  -Wait -NoNewWindow
Start-Sleep -Seconds 5   # CPU cool-down
Start-Process -FilePath $exe -ArgumentList "--benchmark-nosimd", $rom, 10, "off.txt" -Wait -NoNewWindow

# Round 2: SIMD OFF first (swap order to cancel bias)
Start-Process -FilePath $exe -ArgumentList "--benchmark-nosimd", $rom, 10, "off2.txt" -Wait -NoNewWindow
Start-Sleep -Seconds 5
Start-Process -FilePath $exe -ArgumentList "--benchmark", $rom, 10, "on2.txt"  -Wait -NoNewWindow
```

---

## III. benchmark.ps1 — One-Click Four-Version Comparison

### Usage

```powershell
# Run from the repo root
powershell -NoProfile -ExecutionPolicy Bypass -File benchmark.ps1
```

Or directly in PowerShell:
```powershell
.\benchmark.ps1
```

### Test Flow

```
[1/4] .NET Framework 4.8.1 JIT  (AprNes.exe)         ← writes header + line 1
[2/4] .NET 8 RyuJIT              (AprNesAOT.exe)       ← appends line 2 (JIT)
[3/4] Native AOT                 (AprNesAOT.exe)       ← appends line 3 (AOT DLL)
[4/4] .NET 10 RyuJIT             (AprNesAOT10.exe)     ← appends line 4
```

### Configuration (modify variables at the top of benchmark.ps1)

| Variable | Default | Description |
|----------|---------|-------------|
| `$rom` | `Controller Test (USA).nes` | Test ROM path |
| `$seconds` | `10` | Seconds per test |
| `$output` | `benchmark.txt` | Results output file |

### Output Format (benchmark.txt)

```
=== AprNes Benchmark ===
ROM  : Controller Test (USA).nes
Time : 10 sec each
Date : 2026-03-03 15:00:00
OS   : Microsoft Windows NT 10.0.19045.0
CPU  : 13th Gen Intel(R) Core(TM) i7-1370P

JIT [.NET Framework 4.8.1 JIT] :    4220 frames      422.0 FPS
JIT [.NET 8 RyuJIT]            :    7018 frames      701.8 FPS
AOT [NesCoreNative]            :    5500 frames      550.0 FPS
JIT [.NET 10 RyuJIT]           :    7640 frames      764.0 FPS
```

### Auto Build

If an exe does not exist, the script will automatically attempt to build it:
- `AprNes.exe` / `AprNesAOT.exe` → calls `build.ps1` and `buildAot.bat`
- `AprNesAOT10.exe` → calls `dotnet build AprNesAOT10\AprNesAOT10.csproj -c Release`

---

## IV. run_tests.sh — ROM Test Suite (bash/WSL)

### Usage

```bash
# Must be run from the repo root; EXE must be built first (Debug version)
bash run_tests.sh

# Version that generates a report
bash run_tests_report.sh
```

### Prerequisites

```bash
# Build the .NET Framework Debug version first
powershell -NoProfile -File build.ps1
# Verify EXE exists
ls AprNes/bin/Debug/AprNes.exe
```

### Test ROM Path

All test ROMs are located at:
```
nes-test-roms-master/checked/<suite>/<rom.nes>
```

### Result Summary

```
=== Starting test run ===
PASS: cpu_timing_test6/cpu_timing_test6.nes
FAIL(2): nes_instr_test/nes_instr_test.nes -- Error 02
...
=== Results: 170 passed, 4 failed / 174 total ===
```

---

## V. ROM Selection Recommendations

| Use Case | Recommended ROM |
|----------|----------------|
| Pure CPU performance test | `nes-test-roms-master/Controller Test (USA)/Controller Test (USA).nes` |
| Maximum sprite load | `nes-test-roms-master/spritecans-2011/spritecans.nes` |
| PPU correctness | `nes-test-roms-master/checked/ppu_vbl_nmi/` series |
| CPU instruction correctness | `nes-test-roms-master/checked/cpu_timing_test6/` |

---

## VI. Frequently Asked Questions

### Q: benchmark.ps1 flashes and disappears immediately?
`AprNesAOT.exe` is a WinExe (GUI application); PowerShell's `&` operator does not wait for GUI exe to finish. The script uses `Start-Process -Wait -NoNewWindow` to resolve this. If calling it manually, use the same approach.

### Q: The two SIMD test results differ wildly?
Laptop CPU Turbo Boost thermal throttling can cause ±10% fluctuation, far exceeding the SIMD benefit of Sprite Pass 3. Recommended:
1. Run with swapped test order once each and take the average
2. Test in an environment with a fixed CPU frequency (Turbo Boost disabled)

### Q: Test result is FAIL(255)?
Result `0xFF` means timeout with no determinable result (no `$6000` protocol, `Passed`/`Failed` text, `$F0`, or any other result indicator found). This usually means:
- The ROM needs a longer `--max-wait`
- The ROM uses an unsupported result protocol
- The ROM has a Mapper issue (emulation not supported)

*Documentation updated: 2026-03-03*
