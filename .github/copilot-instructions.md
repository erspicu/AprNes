# AprNes – Copilot Instructions

## Build

```powershell
# Preferred (finds MSBuild via vswhere, builds Debug + Release x64)
powershell -NoProfile -File build.ps1

# Or directly with MSBuild
& 'C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe' AprNes\AprNes.csproj /p:Configuration=Debug /p:Platform=x64 /nologo
```

Output: `AprNes\bin\Debug\AprNes.exe` and `AprNes\bin\Release\AprNes.exe`

## Tests

**Full suite** (174 ROMs, requires bash/WSL):
```bash
bash run_tests.sh
```

**Single ROM** (headless mode):
```bash
AprNes/bin/Debug/AprNes.exe --rom nes-test-roms-master/checked/<suite>/<rom>.nes --wait-result --max-wait 30
```
Exit code 0 = PASS, non-zero = FAIL. Additional flags: `--time <sec>`, `--screenshot <path.png>`, `--input "A:2.0,B:4.0"` (button:second pairs for joypad tests).

**Generate HTML report** (screenshots + JSON, both flags required):
```bash
bash run_tests_report.sh --json --screenshots
```
Opens at `report/index.html`.

**Run GUI**:
```bash
AprNes/bin/Debug/AprNes.exe
```

## Architecture

C# NES emulator targeting .NET Framework 4.6.1, Windows Forms, x64 only. `AllowUnsafeBlocks = true` is required.

### Single partial class design

The entire emulator core is **one `partial class NesCore`** split across `AprNes/NesCore/`:

| File | Subsystem |
|------|-----------|
| `Main.cs` | ROM loading, `init()`, `run()` main loop |
| `CPU.cs` | MOS 6502 — all opcodes in a ~5000-line switch |
| `PPU.cs` | Scanline rendering, VBL/NMI timing, sprite-0-hit |
| `APU.cs` | 5 audio channels, WaveOut output, frame counter |
| `MEM.cs` | `Mem_r()`/`Mem_w()`, `tick()`, DMC cycle steal |
| `IO.cs` | Register dispatch for $2000–$2007, $4000–$4017 |
| `JoyPad.cs` | Controller strobe/read |

**All fields are `static`** — there is no instance state. Memory buffers are allocated with `Marshal.AllocHGlobal` (never GC'd or moved).

### Tick model

Every `Mem_r`/`Mem_w` call invokes `tick()`, which advances 3 PPU dots + 1 APU cycle. This is the sole timing mechanism — no separate clock scheduler.

```
Mem_r(addr) → set cpuBusAddr/cpuBusIsWrite → tick() → read via function pointer table
tick() → promote nmi_delay → 3× ppu_step() with NMI edge detection → apu_step() → IRQ tracking
```

`dmc_stolen_tick()` is identical to `tick()` but bypasses the `in_tick` reentrancy guard (used during DMC DMA cycle stealing).

### Memory dispatch

`mem_read_fun[65536]` / `mem_write_fun[65536]` — function pointer arrays initialized in `init_function()`. Mappers register their handlers into these arrays. No address-range if/switch at runtime.

### VBL/NMI timing

1-cycle delay model: rising edge sets `nmi_delay` → next tick promotes to `nmi_pending` → CPU checks `nmi_pending`. Reading `$2002` clears `nmi_delay` (cancellable) but not `nmi_pending` (irreversible).

### Mappers

Interface `IMapper` with `MapperR_PRG`, `MapperR_CHR`, `MapperW_PRG`. Loaded via `Activator.CreateInstance`. Supported: 0, 1, 2, 3, 4 (MMC3 with A12 IRQ), 4-MMC6, 4-RevA, 5, 7, 11, 66, 71.

### APU audio

WaveOut API (winmm.dll, no third-party audio libraries). Double-buffer scheme: 4 × 735 samples at 44100 Hz (~1 frame each). Non-linear mixing tables (`SQUARELOOKUP`, `TNDLOOKUP`) match real NES hardware. DC-kill filter applied before output.

### Rendering pipeline

```
PPU → ScreenBuf1x (uint*) → GDI SetDIBitsToDevice → window
```
Directly calls GDI32; no .NET `Graphics`/`Bitmap` objects. Optional filters: scanline (LibScanline), xBRz 2×/3× (libXBRz).

## Communication

所有回應請使用**繁體中文**。程式碼、指令、檔案路徑維持原樣，僅說明文字使用中文。

## Key Conventions

- **All core state is static**: `NesCore` has no instances — all fields on `partial class NesCore` are `static`.
- **unsafe everywhere**: Core code uses raw pointers (`byte*`, `uint*`) for all memory and screen buffers.
- **Language**: Code comments and commit messages may be in Traditional Chinese or English.
- **`ConfigureUI` quirk**: `AprNes_ConfigureUI` has no `Designer.cs`; controls are defined in `designer.cs` (lowercase filename).
- **Web downloads**: Never use the WebFetch tool. All web downloads must use `page_getter/downloader.py` (Playwright headless browser, bypasses Cloudflare/JS protection):
  ```bash
  python page_getter/downloader.py <URL>               # auto-named output
  python page_getter/downloader.py <URL> -o output.html
  ```
  Output is saved in `page_getter/` by default; read the file afterward with the Read tool.
- **Reference material**: Check `ref/` before downloading anything. It contains NESdev Wiki pages (DMA, APU DMC, PPU/CPU timing), and the full Mesen2 source (`ref/Mesen2-master/`). Mesen2 is useful as a reference but is not guaranteed 100% correct.
- **Bugfix history**: `bugfix/` holds timestamped markdown files documenting past bugs and fixes — consult these when investigating a regression.
- **Test ROMs**: All 174 test ROMs live under `nes-test-roms-master/checked/<suite>/`. Only ROMs in `checked/` are used by `run_tests.sh`.
- **FPS limiting**: `Thread.Sleep(1)` accuracy depends on the Windows timer resolution. `timeBeginPeriod(1)` is called at `run()` entry and `timeEndPeriod(1)` at exit to ensure stable 60 FPS regardless of audio state.
