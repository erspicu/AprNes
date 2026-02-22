# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build

```bash
powershell -NoProfile -Command "& 'C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe' 'C:\ai_project\AprNes\AprNes.sln' /p:Configuration=Debug /t:Rebuild /nologo"
```

Output: `AprNes/bin/Debug/AprNes.exe`

## Run Tests

**Full test suite** (174 ROMs, bash):
```bash
bash run_tests.sh
```

**Single test ROM** (headless mode):
```bash
AprNes/bin/Debug/AprNes.exe --rom nes-test-roms-master/checked/<suite>/<rom>.nes --wait-result --max-wait 30
```

Exit code 0 = PASS, non-zero = FAIL. `--wait-result` blocks until the test ROM signals pass/fail or `--max-wait` timeout. Common extra flags: `--time <sec>`, `--screenshot <path.png>`, `--input <spec>` (for joypad tests, e.g. `A:2.0,B:4.0`).

**重建測試結果網頁**（截圖 + JSON，兩者缺一不可）：
```bash
bash run_tests_report.sh --json --screenshots
```
**必須同時帶 `--json` 和 `--screenshots`**，否則報告不完整。此指令會：自動編譯 → 跑全部 174 個測試 → 截圖（PNG→WebP）→ 產生 `report/results.json` + `report/index.html`。開啟 `report/index.html` 即可瀏覽含截圖的互動式測試報告（可篩選 pass/fail、按 suite 分組、搜尋 ROM 名稱、點圖放大）。其他用法：
- `bash run_tests_report.sh --no-build --json --screenshots` — 跳過編譯，直接跑測試
- 不建議省略 `--json` 或 `--screenshots`，會導致報告缺少資料

**Run GUI**: `AprNes/bin/Debug/AprNes.exe` (no args)

## Architecture

This is a cycle-accurate NES emulator in C# (.NET Framework 4.6.1, Windows Forms, unsafe code).

### Single partial class design

The entire emulator core is one `partial class NesCore` split across `AprNes/NesCore/`:

| File | Subsystem | Key responsibilities |
|------|-----------|---------------------|
| `Main.cs` | Init/loop | ROM loading, `init()`, `run()` main loop, `cleanup()` |
| `CPU.cs` | 6502 CPU | All opcodes in a giant switch, registers, interrupt vectors (~5000 lines) |
| `PPU.cs` | Graphics | Scanline rendering, VBL/NMI timing, OAM DMA (`ppu_w_4014`), sprite-0-hit |
| `APU.cs` | Audio | 5 channels (Pulse×2, Triangle, Noise, DMC), frame counter, WaveOut output |
| `MEM.cs` | Memory | `Mem_r()`/`Mem_w()`, `tick()`, `dmc_stolen_tick()`, function pointer tables |
| `IO.cs` | I/O routing | Register dispatch for $2000-$2007, $4000-$4017 |
| `JoyPad.cs` | Input | Controller strobe/read |

All fields are `static` — there is no instance state. Memory is `byte*` via `Marshal.AllocHGlobal` (no GC).

### Tick model (MEM.cs)

Every `Mem_r`/`Mem_w` call invokes `tick()` which advances 3 PPU dots + 1 APU cycle. This is the core timing mechanism — there is no separate clock scheduler.

```
Mem_r(addr) → set cpuBusAddr/cpuBusIsWrite → tick() → read via function table
tick() → promote nmi_delay → 3× ppu_step_new() with NMI edge detection → apu_step() → IRQ tracking
```

`dmc_stolen_tick()` is the same as `tick()` but bypasses the `in_tick` reentrancy guard. Used during DMC DMA cycle stealing.

### Memory dispatch

65536-entry function pointer arrays (`mem_read_fun[]`, `mem_write_fun[]`) initialized in `init_function()`. Mappers register their handlers into these arrays. No address-range if/switch at runtime.

### VBL/NMI timing (PPU.cs)

1-cycle delay model: rising edge sets `nmi_delay` → next tick promotes to `nmi_pending` → CPU checks `nmi_pending`. `$2002` read clears `nmi_delay` (cancellable) but not `nmi_pending` (irreversible).

### APU frame counter + IRQ (APU.cs)

`framectrdiv` counts down each `apu_step()`. At zero, triggers envelope/length/sweep clocks. IRQ fires on step 3 in 4-step mode. `irqLineAtFetch` captures IRQ state during CPU opcode fetch for penultimate-cycle sampling.

### DMC DMA (APU.cs + MEM.cs)

DMC sample fetches steal 3-4 CPU cycles. `dmcfillbuffer()` calls `dmc_stolen_tick()` for each stolen cycle, then reads via `mem_read_fun[]` directly (not `Mem_r`, to avoid `in_tick` guard). `dmcDmaInProgress` prevents recursive DMA triggers. `cpuBusAddr`/`cpuBusIsWrite` track the CPU's last bus state for phantom read emulation.

### Mappers (NesCore/Mapper/)

Interface `IMapper` with methods like `MapperR_RPG`, `MapperR_CHR`, `MapperW_PRG`. Loaded dynamically via `Activator.CreateInstance`. MMC3 (Mapper004) is the most complex, with scanline IRQ via A12 rising edge detection.

### Test infrastructure

`TestRunner.cs` implements headless mode. Test ROMs write pass/fail to a known memory location; the TestRunner polls it each frame. The `run_tests.sh` script iterates all 174 ROMs under `nes-test-roms-master/checked/` and reports PASS/FAIL counts.

## Key conventions

- **Language**: Code comments and git messages may be in Chinese (Traditional) or English
- **All core state is static**: No object instances for the emulator — everything is static fields on `NesCore`
- **unsafe everywhere**: Core code uses raw pointers (`byte*`, `uint*`) for all memory buffers
- **ConfigureUI quirk**: `AprNes_ConfigureUI` has no Designer.cs; controls are defined in `designer.cs` (lowercase)
- **Web downloads**: **永遠不要使用 WebFetch 工具**。所有網頁下載必須使用 `page_getter/downloader.py`（Playwright 無頭瀏覽器），可繞過 Cloudflare/JS 防護，避免 403 錯誤。用法：
  ```bash
  python page_getter/downloader.py <URL>                    # 自動命名
  python page_getter/downloader.py <URL> -o output.html     # 指定檔名
  ```
  下載後用 Read 工具讀取 HTML 內容。輸出預設存在 `page_getter/` 目錄（依 URL 末段命名，加 `.html` 後綴）。
- **Reference material**: `ref/` 目錄內有已下載的參考資料，研究 NES 硬體行為時應**優先查閱 ref/ 內的現有資料**，不夠再透過 `page_getter/downloader.py` 抓取新網頁。內容包含：
  - NESdev Wiki 頁面（DMA、APU DMC、PPU rendering/timing、CPU cycle reference 等 HTML）
  - Mesen2 模擬器完整原始碼（`ref/Mesen2-master/`）及單獨擷取的關鍵檔案（`Mesen_NesCpu.cpp`/`.h`）
  - 注意：Mesen2 的實作可作為參考但不保證 100% 正確
- **Bugfix notes**: Historical fix documentation in `bugfix/` directory (timestamped markdown files)
