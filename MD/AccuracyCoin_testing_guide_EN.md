# AccuracyCoin Testing Guide

## Table of Contents

1. [Overview](#1-overview)
2. [ROM Navigation](#2-rom-navigation)
3. [Testing Tool Usage](#3-testing-tool-usage)
4. [Testing Technical Framework](#4-testing-technical-framework)
5. [Interpreting Results](#5-interpreting-results)
6. [Known Issues and Limitations](#6-known-issues-and-limitations)
7. [Page Content Overview](#7-page-content-overview)

---

## 1. Overview

**AccuracyCoin** is a NES accuracy test ROM containing **136 tests** + **5 DRAW info pages**, distributed across 20 pages. Tests cover CPU instructions, unofficial instructions, interrupt timing, DMA, APU, PPU behavior, and more.

- ROM path: `nes-test-roms-master/AccuracyCoin-main/AccuracyCoin.nes`
- Mapper: 0 (NROM)
- Reference document: `nes-test-roms-master/AccuracyCoin-main/README.md`
- Assembly source: `nes-test-roms-master/AccuracyCoin-main/AccuracyCoin.asm`

---

## 2. ROM Navigation

### 2.1 Startup

After launching, the ROM displays the Page 1/20 test item list in approximately **3 seconds**.

### 2.2 Page Navigation

When focus is on the page header (PAGE X/20):

| Button | Action |
|--------|--------|
| **Right** | Next page (Page 20 → Right returns to Page 1) |
| **Left** | Previous page (Page 1 → Left jumps to Page 20) |
| **A** | Run all tests on this page |
| **B** | Mark all tests on this page as SKIP |
| **Start** | Run all tests in the ROM sequentially (shows summary table upon completion) |
| **Down** | Move focus to the first test item on this page |

After each page switch, it is recommended to wait **0.5–1 second** (load/display time).

### 2.3 Individual Test Navigation

When focus is on an individual test item:

| Button | Action |
|--------|--------|
| **Down** | Move to next test item (pressing Down on the last item does not change page) |
| **Up** | Move to previous test item (pressing Up on the first item returns to page header) |
| **A** | Run this individual test |
| **B** | Toggle SKIP mark |
| **Select** | Show Debug Menu (memory value viewer) |

### 2.4 Two Testing Methods

#### Sequential Testing (Start Button)

1. Wait 3 seconds after launch
2. Press **Start**
3. The ROM automatically runs all 136 tests in order
4. Displays a summary table screenshot upon completion
5. **Drawback**: If a test hangs (e.g., test 82), all subsequent tests cannot run

#### Per-Page Testing (A Button)

1. Navigate to the target page
2. Press **A** to run all tests on that page
3. If a page contains a test that hangs, use **Down + B** to mark it as SKIP first, then return to the page header and press **A**
4. **Advantage**: Allows skipping hung tests and collecting all results page by page

### 2.5 Test Time Reference

| Type | Estimated Time |
|------|---------------|
| Full page test | Generally < 30 seconds |
| Single test | Generally < 10 seconds (NMI/IRQ/BRK/interrupt types may need ~10 seconds) |
| Page navigation | 0.5–1 second |

---

## 3. Testing Tool Usage

### 3.1 Automated Test Script

```bash
# Full run (compile + per-page testing + screenshots + report)
bash run_tests_AccuracyCoin_report.sh

# Skip compilation
bash run_tests_AccuracyCoin_report.sh --no-build

# Skip screenshots (collect result data only)
bash run_tests_AccuracyCoin_report.sh --no-screenshots
```

Output:
- HTML report: `report/AccuracyCoin_report.html`
- Page screenshots: `report/screenshots-ac/ac_page_XX.png`
- Intermediate results: `temp/ac_results/page_XX.hex`

### 3.2 Manual Single-Page Testing

```bash
# Example: test Page 3 (requires 2 Right navigations)
AprNes/bin/Debug/AprNes.exe \
    --rom nes-test-roms-master/AccuracyCoin-main/AccuracyCoin.nes \
    --time 40 \
    --input "Right:3.5,Right:4.0,A:5.0" \
    --screenshot result/ac_page03.png \
    --dump-ac-results
```

### 3.3 Manually Skipping Specific Items

```bash
# Example: Page 12, skip item 1 (IFlagLatency)
# Navigate to Page 12 (9 Left presses), Down to select item 1, B to mark skip, Up to return to page header, A to run
AprNes/bin/Debug/AprNes.exe \
    --rom nes-test-roms-master/AccuracyCoin-main/AccuracyCoin.nes \
    --time 45 \
    --input "Left:3.5,Left:4.0,Left:4.5,Left:5.0,Left:5.5,Left:6.0,Left:6.5,Left:7.0,Left:7.5,Down:8.5,B:9.0,Up:9.5,A:10.0" \
    --screenshot result/ac_page12.png \
    --dump-ac-results
```

### 3.4 Related CLI Parameters

| Parameter | Description |
|-----------|-------------|
| `--rom <path>` | ROM file path |
| `--time <seconds>` | Execution time (NES seconds) |
| `--input <spec>` | Simulate controller input, format: `Button:time,Button:time,...` |
| `--screenshot <path>` | Screenshot at end of run |
| `--timed-screenshots <spec>` | Timed screenshots, format: `path1:time1,path2:time2,...` |
| `--dump-ac-results` | Print `AC_RESULTS_HEX:` memory dump at end of run ($0300-$04FF) |

Button names: `A`, `B`, `Select`, `Start`, `Up`, `Down`, `Left`, `Right`

---

## 4. Testing Technical Framework

### 4.1 Per-Page Independent Testing Architecture

Because Page 12 item 1 (Interrupt Flag Latency) causes a hang, the Start sequential testing method cannot be used. Therefore a **per-page independent testing** architecture is adopted:

```
Launch emulator independently per page → Navigate to target page → Press A to run → Wait for completion → Dump results → Merge
```

Process:

1. **Per-page launch**: The emulator is launched once per page (20 times total), with no interference between pages
2. **Navigation optimization**:
   - Pages 1–10: Press Right from Page 1 (0–9 times)
   - Pages 11–20: Press Left from Page 1 (10–1 times, wrapping around is faster)
3. **Page 12 special handling**: Navigate to page → Down to select item 1 → B to mark skip → Up to return to page header → A to run the rest
4. **Page 15 special handling**: DRAW tests produce no PASS/FAIL; only capture a screenshot
5. **Result dump**: After each page, `--dump-ac-results` outputs hex data for $0300-$04FF
6. **Result merge**: Merge hex data from all 20 pages (non-zero values overwrite zero values) to produce the complete result

### 4.2 Timing Calculation

How `--input` and `--time` are calculated for each page:

```
ROM load wait:            3.0 seconds
Navigation interval:      0.5 seconds per keypress
Navigation done to A:     1.0 second
Test wait:                35 seconds (max 30 seconds + buffer)

Total time for Page N ≈ 3.0 + nav_presses × 0.5 + 1.0 + 35.0
```

### 4.3 Result Memory Addresses

AccuracyCoin stores each test's result at a fixed memory address (region $0400-$048D). Each test occupies 1 byte:

```
0x01                = PASS (bit 0 = 1)
(ErrorCode << 2) | 0x02 = FAIL (bit 1 = 1, bits 2+ = error code)
0xFF                = SKIP
0x00                = Not yet executed
```

`--dump-ac-results` dumps $0300-$04FF (512 bytes = 1024 hex chars), containing all test results.

### 4.4 Report Generation

A Python script reads the merged hex data and cross-references it against the test address table (SUITES) to produce an HTML report:
- One block per page, including screenshots and result tables
- Automatically tallies PASS/FAIL/SKIP/N/A
- DRAW pages (Page 15) are rendered as purple cards with sub-item screenshots

---

## 5. Interpreting Results

### 5.1 Result Byte Format

| Value | Meaning | Interpretation |
|-------|---------|----------------|
| `0x01` | PASS | bit 0 = 1 |
| `0x02` | FAIL, error code 0 | (0 << 2) \| 0x02 |
| `0x06` | FAIL, error code 1 | (1 << 2) \| 0x02 |
| `0x0A` | FAIL, error code 2 | (2 << 2) \| 0x02 |
| `0x1E` | FAIL, error code 7 | (7 << 2) \| 0x02 |
| `0xFF` | SKIP (marked skipped with B button) | |
| `0x00` | Not yet executed | |

General formula: `error_code = result_byte >> 2` (when bit 1 = 1)

### 5.2 Error Code Meanings

Error code meanings vary per test; see the "Error Codes" section in `nes-test-roms-master/AccuracyCoin-main/README.md`. Common error codes for unofficial instruction tests:

- 1: Target address error
- 2: Register A value error
- 3: Register X value error
- 5: CPU flags error
- 7: Target address error when RDY line is low (SHA/SHX/SHY/SHS)

### 5.3 Debug Menu

After a test completes, press **Select** to display the Debug Menu, which prints the following memory regions:
- $20-$2F: Values used by unofficial instruction tests
- $50-$6F: Values used by some tests
- $500-$5FF: Test workspace (8 rows × 32 bytes)

---

## 6. Known Issues and Limitations

### 6.1 Known Issues

**All resolved** (2026-03-14). All 136/136 tests pass.

Past issues (now fixed):
- Interrupt Flag Latency (P12) — Fixed by per-cycle CPU rewrite
- SH* instructions (P10) — Fixed by correct DMA bus conflict implementation
- DMA-related (P13) — Fixed by BUGFIX53-56

### 6.2 Per-Page Independent Testing Limitations

- Each page launches independently; Power-On State (Page 15) results may differ from sequential testing
- No summary table screenshot from the Start button (replaced by the HTML report)
- No cross-test interaction between pages (this is usually an advantage, but some timing tests may depend on prior state)

---

## 7. Page Content Overview

| Page | Name | Tests | Notes |
|------|------|-------|-------|
| 1 | CPU Behavior | 9 | Basic CPU behavior |
| 2 | Addressing mode wraparound | 6 | Address mode boundaries |
| 3 | Unofficial: SLO | 7 | |
| 4 | Unofficial: RLA | 7 | |
| 5 | Unofficial: SRE | 7 | |
| 6 | Unofficial: RRA | 7 | |
| 7 | Unofficial: *AX | 10 | SAX + LAX |
| 8 | Unofficial: DCP | 7 | |
| 9 | Unofficial: ISC | 7 | |
| 10 | Unofficial: SH* | 6 | SHA/SHS/SHY/SHX/LAE |
| 11 | Unofficial Immediates | 8 | ANC/ASR/ARR/ANE/LXA/AXS/SBC |
| 12 | CPU Interrupts | 3 | All PASS |
| 13 | APU Registers and DMA | 10 | DMA-related tests |
| 14 | APU Tests | 9 | Length counter, Frame Counter, DMC |
| 15 | Power On State | 5 | **DRAW tests, display info only** |
| 16 | PPU Behavior | 7 | CHR/Register/Palette |
| 17 | PPU VBlank Timing | 7 | VBL/NMI timing |
| 18 | Sprite Evaluation | 9 | Overflow/Hit/OAM |
| 19 | PPU Misc. | 6 | Attributes/Shift Register |
| 20 | CPU Behavior 2 | 4 | Timing/Dummy Reads/Branch |

**Total**: 136 tests + 5 DRAW = 141 items

---

*Last updated: 2026-03-14 (136/136 PASS — PERFECT SCORE)*
