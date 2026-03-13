# NesCore Integration Guide

NesCore is the NES emulator core of AprNes and supports three integration methods:

| Method | Suitable Languages | Requires .NET Runtime | Description |
|--------|-------------------|:---------------------:|-------------|
| **C# Source Reference** | C# / .NET | ✅ | Link source files directly; most flexible, best performance |
| **NesCore.dll (Managed)** | C# / .NET | ✅ | Compiled as a standard .NET DLL; suitable when distributing without source |
| **NesCoreNative.dll (AOT)** | C, C++, Python, Rust… | ❌ | Native AOT C ABI; no .NET Runtime installation required |

---

## Method 1: C# Direct Source Reference

### 1. Add Source Files

Add NesCore source links to your `.csproj` (same approach as AprNesAOT10):

```xml
<ItemGroup>
  <Compile Include="../AprNes/NesCore/Main.cs"    Link="NesCore/Main.cs" />
  <Compile Include="../AprNes/NesCore/CPU.cs"     Link="NesCore/CPU.cs" />
  <Compile Include="../AprNes/NesCore/PPU.cs"     Link="NesCore/PPU.cs" />
  <Compile Include="../AprNes/NesCore/APU.cs"     Link="NesCore/APU.cs" />
  <Compile Include="../AprNes/NesCore/MEM.cs"     Link="NesCore/MEM.cs" />
  <Compile Include="../AprNes/NesCore/IO.cs"      Link="NesCore/IO.cs" />
  <Compile Include="../AprNes/NesCore/JoyPad.cs"  Link="NesCore/JoyPad.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/IMapper.cs"       Link="NesCore/Mapper/IMapper.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper000.cs"     Link="NesCore/Mapper/Mapper000.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper001.cs"     Link="NesCore/Mapper/Mapper001.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper002.cs"     Link="NesCore/Mapper/Mapper002.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper003.cs"     Link="NesCore/Mapper/Mapper003.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper004.cs"     Link="NesCore/Mapper/Mapper004.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper004RevA.cs" Link="NesCore/Mapper/Mapper004RevA.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper004MMC6.cs" Link="NesCore/Mapper/Mapper004MMC6.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper007.cs"     Link="NesCore/Mapper/Mapper007.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper011.cs"     Link="NesCore/Mapper/Mapper011.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper066.cs"     Link="NesCore/Mapper/Mapper066.cs" />
  <Compile Include="../AprNes/NesCore/Mapper/Mapper071.cs"     Link="NesCore/Mapper/Mapper071.cs" />
</ItemGroup>
```

The project must enable `unsafe`:
```xml
<PropertyGroup>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
</PropertyGroup>
```

---

### 2. Minimal Startup Example

```csharp
using AprNes;
using System.Threading;

// 1. Enable headless mode (no window)
NesCore.HeadlessMode = true;
NesCore.AudioEnabled = false;   // Disable audio if not needed
NesCore.LimitFPS     = true;    // true = ~60 FPS; false = full speed

// 2. Error handling
NesCore.OnError = msg => Console.Error.WriteLine("[NES ERROR] " + msg);

// 3. Subscribe to frame update event
NesCore.VideoOutput += (sender, e) =>
{
    // Called once per frame (~60 FPS)
    // NesCore.ScreenBuf1x is 256×240 ARGB pixels (uint*)
    RenderFrame(NesCore.ScreenBuf1x);
};

// 4. Load ROM
byte[] romBytes = File.ReadAllBytes("game.nes");
if (!NesCore.init(romBytes))
{
    Console.Error.WriteLine("ROM load failed");
    return;
}

// 5. Run emulator on a background thread
NesCore.exit = false;
var emuThread = new Thread(NesCore.run) { IsBackground = true };
emuThread.Start();

// 6. Stop emulator
// NesCore.exit = true;
// NesCore._event.Set();   // Wake up a paused emulator (when LimitFPS=true)
// emuThread.Join(2000);
```

---

### 3. Full Public API

#### Initialization and Lifecycle

| Member | Type | Description |
|--------|------|-------------|
| `NesCore.init(byte[] romBytes)` | `bool` | Load ROM and initialize all subsystems. Returns `true` on success, `false` on failure |
| `NesCore.run()` | `void` | Main emulation loop (blocking). Should run on a dedicated Thread |
| `NesCore.exit` | `bool` | Set to `true` to terminate the `run()` loop |
| `NesCore._event` | `ManualResetEvent` | When `LimitFPS=true`, the emulator waits on this event; call `_event.Set()` to wake it |
| `NesCore.SoftReset()` | `void` | Soft reset (equivalent to pressing the Reset button) |

#### Video Output

| Member | Type | Description |
|--------|------|-------------|
| `NesCore.VideoOutput` | `event EventHandler` | Fired after each frame is complete |
| `NesCore.ScreenBuf1x` | `uint*` | 256×240 ARGB pixel buffer (61,440 `uint32` values), updated every frame |
| `NesCore.screen_lock` | `bool` | Set to `true` to pause PPU writes (prevents tearing while reading the frame) |
| `NesCore.frame_count` | `int` | Cumulative frame count (volatile) |

Frame buffer access example:
```csharp
unsafe void RenderFrame(uint* buf)
{
    for (int y = 0; y < 240; y++)
        for (int x = 0; x < 256; x++)
        {
            uint argb = buf[y * 256 + x];
            byte r = (byte)(argb >> 16);
            byte g = (byte)(argb >> 8);
            byte b = (byte)(argb);
            // Draw pixel at (x, y)
        }
}
```

#### Audio Output

| Member | Type | Description |
|--------|------|-------------|
| `NesCore.AudioSampleReady` | `Action<short>` | 44100 Hz, 16-bit mono sample callback |
| `NesCore.AudioEnabled` | `bool` | When `false`, stops generating audio samples (saves CPU) |
| `NesCore.Volume` | `int` | Volume 0–100 (default 70) |

```csharp
NesCore.AudioEnabled = true;
NesCore.AudioSampleReady += sample =>
{
    // 44100 Hz, 16-bit signed mono
    audioPlayer.Write(sample);
};
```

#### Controller Input (Player 1)

| Button Index | Corresponding Button |
|--------------|----------------------|
| 0 | A |
| 1 | B |
| 2 | Select |
| 3 | Start |
| 4 | Up |
| 5 | Down |
| 6 | Left |
| 7 | Right |

```csharp
// Press A button
NesCore.P1_ButtonPress(0);

// Release A button
NesCore.P1_ButtonUnPress(0);
```

#### SRAM Access (Battery-backed Memory)

```csharp
// Save: read out 8KB SRAM
if (NesCore.HasBattery)
{
    byte[] save = NesCore.DumpSRam();   // Returns 8192 bytes
    File.WriteAllBytes("game.sav", save);
}

// Load: restore after init(), before run()
byte[] saveData = File.ReadAllBytes("game.sav");
NesCore.LoadSRam(saveData);
```

#### ROM Information (readable after init)

```csharp
int  mapper    = NesCore.RomMapper;       // Mapper number
int  prgCount  = NesCore.RomPrgCount;     // PRG-ROM page count (16KB/page)
int  chrCount  = NesCore.RomChrCount;     // CHR-ROM page count (8KB/page)
bool horizMirr = NesCore.RomHorizMirror;  // true=horizontal mirror, false=vertical
bool hasBatt   = NesCore.HasBattery;      // Whether battery backup is present
```

#### Other Control Flags

| Member | Default | Description |
|--------|---------|-------------|
| `NesCore.LimitFPS` | `false` | `true` = limit to ~60 FPS; `false` = full speed |
| `NesCore.HeadlessMode` | `false` | `true` = headless (no UI) mode, suppresses some Console output |
| `NesCore.OnError` | `null` | `Action<string>` error handler callback |
| `NesCore.Mapper_Allow` | `{0,1,2,3,4,7,11,66}` | List of allowed mappers; can be extended |

#### Supported Mappers

| Number | Common Name | Representative Games |
|--------|-------------|----------------------|
| 0 | NROM | Super Mario Bros., Tetris |
| 1 | MMC1 (SxROM) | The Legend of Zelda, Final Fantasy |
| 2 | UxROM | Mega Man, Castlevania |
| 3 | CNROM | Teenage Mutant Ninja Turtles |
| 4 | MMC3 (TxROM) | Super Mario Bros. 3, Mega Man 3-6 |
| 7 | AxROM | Bases Loaded |
| 11 | Color Dreams | Some early games |
| 66 | GxROM | Super Mario Bros. + Donkey Kong |

---

## Method 1B: NesCore.dll (Managed .NET DLL)

Suitable when you have another .NET project that wants to use NesCore but **does not want to include the source code** (e.g., distributing binaries, or sharing a single compiled DLL across multiple projects).

### 1. Create a NesCore Class Library Project

Create `NesCore.csproj` (in any directory, e.g., `NesCore/`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net8.0</TargetFramework>   <!-- or net10.0 / net6.0 -->
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <AssemblyName>NesCore</AssemblyName>
    <RootNamespace>AprNes</RootNamespace>
  </PropertyGroup>

  <!-- Reference AprNes NesCore source files -->
  <ItemGroup>
    <Compile Include="../AprNes/NesCore/Main.cs"    Link="NesCore/Main.cs" />
    <Compile Include="../AprNes/NesCore/CPU.cs"     Link="NesCore/CPU.cs" />
    <Compile Include="../AprNes/NesCore/PPU.cs"     Link="NesCore/PPU.cs" />
    <Compile Include="../AprNes/NesCore/APU.cs"     Link="NesCore/APU.cs" />
    <Compile Include="../AprNes/NesCore/MEM.cs"     Link="NesCore/MEM.cs" />
    <Compile Include="../AprNes/NesCore/IO.cs"      Link="NesCore/IO.cs" />
    <Compile Include="../AprNes/NesCore/JoyPad.cs"  Link="NesCore/JoyPad.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/IMapper.cs"       Link="NesCore/Mapper/IMapper.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper000.cs"     Link="NesCore/Mapper/Mapper000.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper001.cs"     Link="NesCore/Mapper/Mapper001.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper002.cs"     Link="NesCore/Mapper/Mapper002.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper003.cs"     Link="NesCore/Mapper/Mapper003.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper004.cs"     Link="NesCore/Mapper/Mapper004.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper004RevA.cs" Link="NesCore/Mapper/Mapper004RevA.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper004MMC6.cs" Link="NesCore/Mapper/Mapper004MMC6.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper007.cs"     Link="NesCore/Mapper/Mapper007.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper011.cs"     Link="NesCore/Mapper/Mapper011.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper066.cs"     Link="NesCore/Mapper/Mapper066.cs" />
    <Compile Include="../AprNes/NesCore/Mapper/Mapper071.cs"     Link="NesCore/Mapper/Mapper071.cs" />
  </ItemGroup>
</Project>
```

Build:
```powershell
dotnet build NesCore.csproj -c Release
# Output: NesCore/bin/Release/net8.0/NesCore.dll
```

---

### 2. Reference in Your Project

**Option A: ProjectReference (recommended — auto-rebuilds during development)**

```xml
<ItemGroup>
  <ProjectReference Include="../NesCore/NesCore.csproj" />
</ItemGroup>
```

**Option B: Direct reference to the compiled DLL**

```xml
<ItemGroup>
  <Reference Include="NesCore">
    <HintPath>../NesCore/bin/Release/net8.0/NesCore.dll</HintPath>
  </Reference>
</ItemGroup>
```

Your project still needs to enable unsafe:
```xml
<AllowUnsafeBlocks>true</AllowUnsafeBlocks>
```

---

### 3. Usage

The Managed DLL API is **identical** to Method 1 (C# source reference) — both use the `AprNes.NesCore` static class.

```csharp
using AprNes;
using System.Threading;

// Configuration
NesCore.HeadlessMode = true;
NesCore.AudioEnabled = false;
NesCore.LimitFPS     = true;
NesCore.OnError      = msg => Console.Error.WriteLine("[NES] " + msg);

// Subscribe to video event
unsafe
{
    NesCore.VideoOutput += (_, _) =>
    {
        uint* screen = NesCore.ScreenBuf1x;  // 256×240 ARGB
        // Render screen...
    };
}

// Load ROM
byte[] rom = File.ReadAllBytes("game.nes");
if (!NesCore.init(rom)) return;

// Run emulator
NesCore.exit = false;
var t = new Thread(NesCore.run) { IsBackground = true };
t.Start();
```

For the complete API reference, see the [Full Public API](#3-full-public-api) section under Method 1 — both are identical.

---

### Method 1 vs Method 1B Comparison

| | C# Source Reference | NesCore.dll (Managed) |
|---|---|---|
| Compile speed | Slower (recompiles NesCore each time) | Faster (references precompiled DLL) |
| Debugging | ✅ Can step directly into NesCore code | ⚠️ Requires PDB file to step in |
| Distribution | Source code included | Only the DLL is needed |
| Version management | NesCore changes take effect immediately | Requires rebuilding and updating the DLL |
| Best for | Development, debugging NesCore | Distribution, sharing across multiple projects |

---

## Method 2: NesCoreNative.dll (C ABI)

Suitable for C, C++, Python, Rust, and any other language that can call a native DLL. **Does not require .NET Runtime installation.**

### Build the DLL

```powershell
cd NesCoreNative
dotnet publish -r win-x64 -c Release
# Output: NesCoreNative\bin\Release\net8.0\win-x64\publish\NesCoreNative.dll
```

### C API Overview

```c
// Callback setup (call before nescore_init)
void nescore_set_video_callback(void (*cb)());
void nescore_set_audio_callback(void (*cb)(short sample));
void nescore_set_error_callback(void (*cb)(const char* msg, int len));

// Core control
int      nescore_init(uint8_t* romData, int len);  // 1=success, 0=failure
void     nescore_run();                             // Run on background thread (non-blocking)
void     nescore_stop();                            // Stop emulator

// Video
uint32_t* nescore_get_screen();                     // 256×240 ARGB pixels

// Controller (btn: 0=A 1=B 2=SEL 3=START 4=UP 5=DOWN 6=LEFT 7=RIGHT)
void nescore_joypad_press(uint8_t btn);
void nescore_joypad_release(uint8_t btn);

// Settings
void nescore_set_volume(int vol);      // 0–100
void nescore_set_limitfps(int enable); // 0=full speed, 1=~60fps

// Benchmark (blocking)
int  nescore_benchmark(int seconds);   // Returns total frame count
```

---

### C Example

```c
#include <windows.h>
#include <stdio.h>
#include <stdint.h>

typedef void     (*fn_set_video_cb)(void (*cb)());
typedef void     (*fn_set_error_cb)(void (*cb)(const char*, int));
typedef int      (*fn_init)(uint8_t*, int);
typedef void     (*fn_run)();
typedef void     (*fn_stop)();
typedef uint32_t*(*fn_get_screen)();
typedef void     (*fn_joypad_press)(uint8_t);
typedef void     (*fn_joypad_release)(uint8_t);
typedef void     (*fn_set_limitfps)(int);

// Global frame counter
volatile int g_frames = 0;

void on_video() { g_frames++; }
void on_error(const char* msg, int len) { fprintf(stderr, "[NES] %.*s\n", len, msg); }

int main()
{
    HMODULE dll = LoadLibraryA("NesCoreNative.dll");
    if (!dll) { fprintf(stderr, "DLL not found\n"); return 1; }

#define LOAD(T, name) T name = (T)GetProcAddress(dll, #name)
    LOAD(fn_set_video_cb,  nescore_set_video_callback);
    LOAD(fn_set_error_cb,  nescore_set_error_callback);
    LOAD(fn_init,          nescore_init);
    LOAD(fn_run,           nescore_run);
    LOAD(fn_stop,          nescore_stop);
    LOAD(fn_get_screen,    nescore_get_screen);
    LOAD(fn_joypad_press,  nescore_joypad_press);
    LOAD(fn_joypad_release,nescore_joypad_release);
    LOAD(fn_set_limitfps,  nescore_set_limitfps);
#undef LOAD

    // Set callbacks
    nescore_set_video_callback(on_video);
    nescore_set_error_callback(on_error);

    // Load ROM
    FILE* f = fopen("game.nes", "rb");
    fseek(f, 0, SEEK_END); int len = ftell(f); rewind(f);
    uint8_t* rom = (uint8_t*)malloc(len);
    fread(rom, 1, len, f); fclose(f);

    if (!nescore_init(rom, len)) { fprintf(stderr, "Init failed\n"); return 1; }
    free(rom);

    // Run at ~60 FPS
    nescore_set_limitfps(1);
    nescore_run();

    // Press Start button (index=3) then release
    Sleep(1000);
    nescore_joypad_press(3);
    Sleep(100);
    nescore_joypad_release(3);

    // Main loop (grab frame each iteration)
    while (1) {
        uint32_t* screen = nescore_get_screen();
        // Use screen[y*256+x] to get pixels and render
        Sleep(16);
    }

    nescore_stop();
    FreeLibrary(dll);
    return 0;
}
```

---

### Python Example

```python
import ctypes, pathlib, time

dll = ctypes.CDLL(str(pathlib.Path("NesCoreNative.dll").resolve()))

# Define function signatures
dll.nescore_set_video_callback.argtypes = [ctypes.c_void_p]
dll.nescore_init.argtypes  = [ctypes.c_char_p, ctypes.c_int]
dll.nescore_init.restype   = ctypes.c_int
dll.nescore_run.argtypes   = []
dll.nescore_stop.argtypes  = []
dll.nescore_get_screen.restype = ctypes.POINTER(ctypes.c_uint32)
dll.nescore_joypad_press.argtypes   = [ctypes.c_uint8]
dll.nescore_joypad_release.argtypes = [ctypes.c_uint8]
dll.nescore_set_limitfps.argtypes   = [ctypes.c_int]
dll.nescore_benchmark.argtypes = [ctypes.c_int]
dll.nescore_benchmark.restype  = ctypes.c_int

# Set video callback
frames = 0
@ctypes.CFUNCTYPE(None)
def on_video():
    global frames
    frames += 1

dll.nescore_set_video_callback(on_video)

# Load ROM
with open("game.nes", "rb") as f:
    rom = f.read()
rom_buf = (ctypes.c_uint8 * len(rom))(*rom)

if not dll.nescore_init(rom_buf, len(rom)):
    raise RuntimeError("ROM init failed")

# Benchmark (10 seconds)
count = dll.nescore_benchmark(10)
print(f"Benchmark: {count} frames in 10s = {count/10:.1f} FPS")

# Normal execution
dll.nescore_set_limitfps(1)
dll.nescore_run()

time.sleep(2)

# Get frame buffer (256×240 uint32 ARGB)
screen = dll.nescore_get_screen()
pixel_0_0 = screen[0]   # Top-left pixel

dll.nescore_stop()
```

---

## Important Limitations and Notes

### 1. All State is Static (Singleton)

All NesCore state is `static` — **only one NES instance can run per process**. If you need to run two ROMs simultaneously (e.g., head-to-head, multiple instances), you must use separate processes.

### 2. run() Must Be on a Dedicated Thread

`NesCore.run()` is a blocking loop. It must not be called directly on the main thread (it will block the UI).

```csharp
// Correct
var t = new Thread(NesCore.run) { IsBackground = true };
t.Start();

// Wrong: calling directly will hang
NesCore.run();
```

### 3. Thread Safety of ScreenBuf1x

`ScreenBuf1x` is written by the emulator thread and read by your render thread. To strictly prevent screen tearing:

```csharp
// Lock before reading
NesCore.screen_lock = true;
// ... read ScreenBuf1x ...
NesCore.screen_lock = false;
```

> Note: While `screen_lock = true`, the PPU continues running but stops writing to the buffer; a brief lock (<1ms) is generally safe.

### 4. Do Not Call run() Before init()

`init()` allocates all unmanaged memory (ROM, VRAM, SRAM, screen buffer). Calling `run()` before `init()` will cause a null pointer access crash.

### 5. Behavior When a Mapper Is Not Supported

`init()` calls `ShowError()` and returns `false`. Make sure your ROM uses a supported mapper (see the list above). Unsupported mappers do not throw an Exception; they notify via the `OnError` callback.

---

*Documentation updated: 2026-03-03*
