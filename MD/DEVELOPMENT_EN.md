# AprNes Development Notes

## Table of Contents

1. [Project Technical Highlights](#1-project-technical-highlights)
2. [Performance Optimization Techniques](#2-performance-optimization-techniques)
3. [APU Audio Implementation](#3-apu-audio-implementation)
4. [FPS Limiter Fix](#4-fps-limiter-fix)

---

## 1. Project Technical Highlights

### 1.1 Technology Stack

| Item | Description |
|------|------|
| Language | C# (.NET Framework 4.6.1) |
| UI Framework | Windows Forms |
| Platform | Windows x64 |
| Compiler Option | `AllowUnsafeBlocks = true` |

### 1.2 Overall Architecture

```
AprNes/
├── NesCore/          ← Emulator core
│   ├── Main.cs       ← ROM loading, initialization, main loop
│   ├── CPU.cs        ← MOS 6502 CPU emulation
│   ├── PPU.cs        ← Picture Processing Unit
│   ├── APU.cs        ← Audio Processing Unit
│   ├── IO.cs         ← I/O register read/write routing
│   ├── MEM.cs        ← Memory management
│   ├── JoyPad.cs     ← Controller input
│   └── Mapper/       ← Cartridge mappers (Mapper 0/1/2/3/4/5/7/11/66/71)
├── tool/             ← Utility classes
│   ├── NativeAPIShare.cs   ← Windows API P/Invoke declarations
│   ├── NativeRendering.cs  ← GDI native rendering
│   ├── LibScanline.cs      ← Scanline filter
│   ├── libXBRz.cs          ← xBRz scaling algorithm
│   └── joystick.cs         ← Gamepad support (WinMM)
└── UI/               ← User interface
    ├── AprNesUI.cs         ← Main window
    ├── AprNes_ConfigureUI  ← Settings window
    └── AprNes_RomInfoUI    ← ROM info window
```

### 1.3 NES Hardware Emulation Accuracy

#### CPU (MOS 6502)
- Full implementation of all official instructions (approximately 5000+ lines of switch/case)
- Cycle-accurate timing: uses a `cycle_table[256]` lookup table so each instruction consumes the correct number of CPU cycles
- Supports NMI, IRQ, and RESET interrupt vectors
- Uses unmanaged memory pointers (`byte*`) to access CPU registers and memory, avoiding GC overhead

#### PPU (Picture Processing Unit)
- Scanline-accurate rendering
- Each CPU cycle drives 3 PPU cycles (NTSC clock ratio 3:1)
- Supports background rendering, sprite rendering (Sprite 0 hit), and vertical blank NMI
- Frame buffer uses `uint* ScreenBuf1x` (unmanaged pointer) for direct writes — zero GC pressure
- Supports multiple mirroring modes: horizontal, vertical, four-screen

#### Memory Management
- The entire core uses **unsafe pointers** to operate on memory, bypassing array bounds checks:
  ```csharp
  byte* NES_MEM  = AllocHGlobal(65536);  // 64KB CPU memory space
  byte* ppu_ram  = AllocHGlobal(0x4000); // 16KB PPU VRAM
  byte* spr_ram  = AllocHGlobal(256);    // OAM (Sprite RAM)
  uint* ScreenBuf1x = AllocHGlobal(256 * 240 * 4); // Frame buffer
  ```
- Read/write function table (`init_function()`): maps memory region read/write operations to function pointers, avoiding address range checks on every access

#### Mapper Support
- Supports Mapper 0, 1, 2, 3, 4, 5, 7, 11, 66, 71
- Unified operation via the `IMapper` interface, dynamically loaded with `Activator.CreateInstance`
- Covers the vast majority of NES games

### 1.4 Graphics Rendering Pipeline

```
PPU render → ScreenBuf1x (uint*) → GDI SetDIBitsToDevice → window
```

- Uses `NativeRendering` to call the GDI32 API (`SetDIBitsToDevice`) directly to draw frames, bypassing the .NET Graphics object
- Optional image filters: scanline filter (LibScanline), xBRz 2x/3x scaling (libXBRz)

---

## 2. Performance Optimization Techniques

### 2.1 Unsafe Pointers Instead of Managed Arrays

The emulator core makes heavy use of C# `unsafe` mode to operate directly on raw memory:

```csharp
// Example: PPU memory read (unsafe)
byte val = ppu_ram[addr]; // Direct pointer arithmetic, no bounds check, no GC

// Compared to managed code
byte val = ppuRamArray[addr]; // Has bounds check, may trigger GC
```

Benefits:
- Eliminates array bounds checks (saves one comparison per memory access)
- All core buffers are allocated with `Marshal.AllocHGlobal` and will never be moved or collected by the GC

### 2.2 Cycle-Accurate Main Loop

The main loop design (`Main.cs: run()`) advances the CPU and PPU at the correct cycle ratio:

```csharp
while (!exit)
{
    cpu_step();          // Execute one CPU instruction → update cpu_cycles
    do
    {
        ppu_step();      // PPU: runs 3 times per CPU cycle
        ppu_step();
        ppu_step();
        apu_step();      // APU: runs 1 time per CPU cycle
    } while (--cpu_cycles > 0);
}
```

This design avoids the overhead of synchronizing independent threads; the entire emulator advances in a fixed timing sequence on a single thread.

### 2.3 Function Pointer Table (Read/Write Routing)

Memory reads and writes are dispatched through a function pointer table, replacing large if/switch chains:

```csharp
init_function(); // Maps Mem_r/Mem_w to corresponding handler functions by address range
```

### 2.4 Native GDI Rendering

Instead of `Graphics.DrawImage()`, the native GDI32 API is used to output a 32-bit pixel buffer directly:

```csharp
// NativeRendering.cs
SetDIBitsToDevice(hdc, 0, 0, 256, 240, 0, 0, 0, 240, screenPtr, ref bmi, 0);
```

This eliminates the overhead of creating .NET Bitmap objects and the resulting GC pressure.

---

## 3. APU Audio Implementation

### 3.1 Background

The original project's `APU.cs` contained only register fields and stub functions with no audio output. This implementation fully implements all five NES APU channels and outputs audio using the Windows **WaveOut API** (`winmm.dll`), requiring no third-party packages.

### 3.2 Modified Files

#### `NesCore/APU.cs` (complete rewrite)

**Added WaveOut API P/Invoke declarations:**

```csharp
[StructLayout(LayoutKind.Sequential)]
struct WAVEFORMATEX { ... }       // PCM format descriptor

[StructLayout(LayoutKind.Sequential)]
struct WAVEHDR { ... }            // Audio buffer header

[DllImport("winmm.dll")] static extern int waveOutOpen(...);
[DllImport("winmm.dll")] static extern int waveOutWrite(...);
[DllImport("winmm.dll")] static extern int waveOutClose(IntPtr hwo);
// ... etc.
```

**Audio buffer architecture (double-buffered rotation):**

```
APU_SAMPLE_RATE = 44100 Hz
APU_BUFFER_SAMPLES = 735  (≈ samples per frame: 44100 / 60 ≈ 735)
APU_NUM_BUFFERS = 4       (4 rotating buffers to prevent playback interruption)
```

Buffers are pinned with `GCHandle.Alloc(..., Pinned)` to prevent GC movement:

```csharp
_bufPins[i] = GCHandle.Alloc(_audioBufs[i], GCHandleType.Pinned);
```

**Five channel implementation:**

| Channel | Type | Key Details |
|---------|------|-------------|
| Pulse 1 & 2 | Square wave | 8 duty cycle sequences, Envelope, Sweep frequency scanning, Length Counter |
| Triangle | Triangle wave | 32-step hardcoded waveform, Linear Counter, mute condition (period < 2) |
| Noise | Noise | 15-bit LFSR, two modes (bit1/bit6 feedback), Envelope/Length |
| DMC | Delta PCM | Reads samples from NES memory, 8-bit delta decoding, Loop/IRQ support |

**Non-linear mixing (simulates actual NES circuit):**

```csharp
// Pulse non-linear mixing table (avoids linear summation distortion)
SQUARELOOKUP[n] = 95.52 / (8128.0 / n + 100);

// Triangle + Noise + DMC mixing table
TNDLOOKUP[n] = 163.67 / (24329.0 / n + 100);

// Final output
double sample = SQUARELOOKUP[p1 + p2] + TNDLOOKUP[3*tri + 2*noise + dmc];
```

**DC-killer filter (prevents DC offset):**

```csharp
_dckiller = _dckiller * 0.999 + sample - _dcprev;
_dcprev = sample;
short out = (short)(_dckiller * 30000.0);
```

**Frame Counter (~240Hz drives envelope/length/sweep):**

```csharp
// 4-step mode: triggered at 3728.5/7457/11186/14914.5 CPU cycles
// 5-step mode: one additional step, also triggered on step 5
static void clockframecounter()
{
    setenvelope();  // Envelope decay
    setlength();    // Length Counter decrement
    setsweep();     // Sweep frequency adjustment
}
```

**Audio and emulation synchronization (natural rate limiting):**

WaveOut automatically waits for playback to finish before accepting new data once the buffer is full, causing the emulator to naturally run at around 60 FPS when audio is enabled.

#### `NesCore/Main.cs`

```csharp
// Added at the end of init():
initAPU();

// Added in run() (see Section 4 below):
timeBeginPeriod(1);
// ... main loop ...
timeEndPeriod(1);
```

#### `NesCore/CPU.cs`

`SoftReset()` now reinitializes the APU, ensuring audio works correctly after a reset:

```csharp
public static void SoftReset()
{
    softreset = true;
    closeAudio();
    initAPU();
}
```

#### `NesCore/IO.cs`

Added APU-related I/O routing:

```csharp
// IO_read()
case 0x4015: return apu_r_4015();  // Read APU status

// IO_write()
case 0x4017:
    ctrmode = ((val & 0x80) != 0) ? 5 : 4;  // Frame Counter mode
    apuintflag = (val & 0x40) != 0;
    framectr = 0;
    framectrdiv = framectrreload;
    if (ctrmode == 5) clockframecounter();
    break;
```

#### `UI/AprNesUI.cs`

- Dynamically adds a "Sound ON/OFF" menu item to the right-click context menu
- Enabling/disabling audio calls `NesCore.openAudio()` / `NesCore.closeAudio()`
- Settings read/write: `AppConfigure["Sound"]` → `NesCore.AudioEnabled`
- Calls `NesCore.closeAudio()` on window close to release WaveOut resources

---

## 4. FPS Limiter Fix

### 4.1 Problem Description

When the FPS limiter is enabled but audio is disabled, the game frame rate drops to approximately **30 FPS** (it should be 60 FPS). Enabling audio restores normal frame rate.

### 4.2 Root Cause

**FPS limiter loop in `PPU.cs:197`:**

```csharp
if (LimitFPS)
    while (StopWatch.Elapsed.TotalSeconds < 0.01666)
        Thread.Sleep(1);  // Wait until 16.66ms have elapsed
```

The actual precision of `Thread.Sleep(1)` depends on the **Windows timer resolution**:

| State | Windows Timer Resolution | `Thread.Sleep(1)` Actual Sleep Time | Result |
|-------|--------------------------|--------------------------------------|--------|
| Audio enabled (WaveOut running) | **1ms** (set internally by WaveOut) | ~1ms | 60 FPS |
| Audio disabled | **15.6ms** (Windows default) | ~15ms | Loop executes 2× × 15ms = 30 FPS |

The WaveOut API internally calls `timeBeginPeriod(1)` when opened, raising the system-wide timer resolution to 1ms. After WaveOut closes, the resolution returns to the default 15.6ms, causing `Thread.Sleep(1)` to sleep for ~15ms each call. The FPS limiter loop then only executes once before exceeding 16.66ms, resulting in ~30ms per frame (30 FPS).

### 4.3 Fix

At the beginning and end of the `run()` method in **`NesCore/Main.cs`**, explicitly set the timer resolution so it does not depend on whether WaveOut is open:

```csharp
// Before
static public void run()
{
    StopWatch.Restart();
    while (!exit) { ... }
    Console.WriteLine("exit..");
}

// After
static public void run()
{
    timeBeginPeriod(1);    // ← Added: force 1ms timer resolution
    StopWatch.Restart();
    while (!exit)
    {
        cpu_step();
        do
        {
            ppu_step(); ppu_step(); ppu_step();
            apu_step();
        } while (--cpu_cycles > 0);
    }
    timeEndPeriod(1);      // ← Added: restore default timer resolution on exit
    Console.WriteLine("exit..");
}
```

`timeBeginPeriod` / `timeEndPeriod` are also from `winmm.dll`, declared in `APU.cs`:

```csharp
[DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint uPeriod);
[DllImport("winmm.dll")] static extern uint timeEndPeriod(uint uPeriod);
```

### 4.4 Fix Result

After the fix, `Thread.Sleep(1)` maintains 1ms precision regardless of whether audio is enabled or disabled. The FPS limiter loop correctly completes its adjustment within 16.66ms, producing a stable 60 FPS output.

---

## 5. Timing Accuracy Fix History (BUGFIX8-32)

### 5.1 Summary

From BUGFIX8 to BUGFIX21, 14 fixes were completed, improving the blargg test suite from 113 PASS to **174/174 all passing**. Subsequent BUGFIX30-32 optimized for AccuracyCoin advanced accuracy tests.

### 5.2 Key Fixes

| BUGFIX | Item Fixed | Impact |
|--------|------------|--------|
| 8 | MMC3 IRQ A12 clocking | +12 PASS |
| 9 | APU frame counter timing | +6 PASS |
| 10 | DMC timer down-counter | +2 PASS |
| 11 | APU $4017 reset/power-on | +3 PASS |
| 12 | PPU VBL/NMI 1-cycle delay model | +15 PASS |
| 13-16 | MMC3 scanline + A12 phase alignment | +4 PASS |
| 17 | Sprite 0 hit per-pixel + overflow hardware bug | +4 PASS |
| 18 | CPU interrupt timing (irqLinePrev penultimate-cycle) | +4 PASS |
| 19 | DMC DMA cycle stealing (Load/Reload stolen cycles) | +2 PASS |
| 20 | PPU $2007 read cooldown (6 PPU dots) | +1 PASS |
| 21 | TestRunner --pass-on-stable | +2 PASS |
| 30 | Branch dummy reads, CPU/controller open bus | AccuracyCoin |
| 32 | Load DMA cpuCycleCount parity | AccuracyCoin |

### 5.3 Tick Model

Each `Mem_r` / `Mem_w` call invokes `tick()`, which advances 3 PPU dots + 1 APU cycle. This is the core timing mechanism.

```
Mem_r(addr) → tick() → 3× ppu_step_new() + apu_step() → read memory
```

DMC DMA uses `dmc_stolen_tick()` (PPU only, does not advance APU to avoid recursion).

### 5.4 Master Clock Infrastructure (BUGFIX32)

Added `masterClock` and `cpuCycleCount` counters that increment on each tick. The Load DMA stolen cycle calculation now uses `cpuCycleCount & 1` to determine the GET/PUT phase, replacing the original `cpuBusIsWrite` proxy.

```csharp
// Load DMA: cpuCycleCount parity determines stolen cycles
bool isPutCycle = (cpuCycleCount & 1) != 0;
haltCycles = isPutCycle ? 3 : 2;
```

### 5.5 AccuracyCoin Test Results

**Final result (2026-03-14)**: 136/136 all PASS (PERFECT SCORE)

History: initial 69/136 → Per-cycle CPU rewrite 132/136 → BUGFIX53-56 fixed P13 DMA → 136/136.
See [AccuracyCoin_TODO.md](AccuracyCoin_TODO.md) and the `bugfix/` directory.

---

## 6. Build Instructions

Run `build.bat` (requires Visual Studio 2022):

```
build.bat         ← Double-click to run
build.ps1         ← Actual build logic (called by build.bat)
```

Output locations:
- Debug: `AprNes\bin\Debug\AprNes.exe`
- Release: `AprNes\bin\Release\AprNes.exe`
