# NesCore — `using` Reference Guide

A summary of all `using` directives in every `.cs` file under `NesCore/`, explaining the actual purpose of each namespace in this project.

---

## NesCore/Main.cs

```csharp
using System;
using System.Windows.Forms;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Reflection;
```

| Namespace | Purpose |
|---|---|
| `System` | `EventArgs` (used by `VideoOut` inheritance), `Exception` (try/catch during init), `Console` (debug output) |
| `System.Windows.Forms` | `MessageBox.Show()`, displays error messages in GUI mode |
| `System.IO` | `File.ReadAllBytes` / `File.WriteAllBytes`, reads and writes battery-backed SRAM (`.sav` files) |
| `System.Runtime.InteropServices` | `Marshal.AllocHGlobal`, allocates unmanaged memory for raw pointers such as `NES_MEM`, `ppu_ram`, `ScreenBuf1x`; `Marshal.FreeHGlobal` for deallocation |
| `System.Threading` | `ManualResetEvent _event`, synchronization control for pausing/resuming the emulation thread |
| `System.Reflection` | `Activator.CreateInstance` + `Type.GetType("AprNes.Mapper" + n)`, dynamically creates Mapper objects via reflection based on the mapper number in the ROM |

---

## NesCore/CPU.cs

```csharp
#define illegal
using System.Runtime.CompilerServices;
```

| Namespace / Directive | Purpose |
|---|---|
| `#define illegal` | Conditional compilation flag that enables unofficial (illegal/undocumented) 6502 instruction support |
| `System.Runtime.CompilerServices` | `[MethodImpl(MethodImplOptions.AggressiveInlining)]`, applied to high-frequency functions such as `GetFlag()`, `SetFlag()`, `NMIInterrupt()`, `IRQInterrupt()` to request forced JIT inlining and reduce call overhead |

---

## NesCore/MEM.cs

```csharp
using System;
using System.Runtime.CompilerServices;
```

| Namespace | Purpose |
|---|---|
| `System` | `Console.WriteLine` for boot initialization debug output |
| `System.Runtime.CompilerServices` | `[MethodImpl(AggressiveInlining)]`, applied to core functions executed every CPU cycle such as `tick()`, `Mem_r()`, `Mem_w()` |

---

## NesCore/PPU.cs

```csharp
using System.Diagnostics;
using System.Threading;
using System.Runtime.CompilerServices;
```

| Namespace | Purpose |
|---|---|
| `System.Diagnostics` | `Stopwatch`, measures per-frame elapsed time to implement the NTSC 60.0988 fps limiter |
| `System.Threading` | `Thread.Sleep(1)` combined with `timeBeginPeriod(1)` for precise FPS throttling; `volatile` keyword for multi-threaded access to `frame_count` |
| `System.Runtime.CompilerServices` | `[MethodImpl(AggressiveInlining)]`, applied to PPU per-dot stepping functions such as `ppu_step_new()` |

---

## NesCore/APU.cs

```csharp
using System.Runtime.InteropServices;
using System.Threading;
using System;
```

| Namespace | Purpose |
|---|---|
| `System.Runtime.InteropServices` | `[DllImport("winmm.dll")]` declares WaveOut API functions (`waveOutOpen`, `waveOutWrite`, `waveOutPrepareHeader`, etc.); `[StructLayout(LayoutKind.Sequential)]` aligns `WAVEFORMATEX` and `WAVEHDR` structs with C API memory layout |
| `System.Threading` | `Thread` creates a dedicated audio output thread (fills WaveOut buffer); `Interlocked` / `volatile` for multi-threaded audio buffer completion notifications |
| `System` | `IntPtr` stores the WaveOut handle; `Math` for waveform calculations; `Console` for initialization messages |

---

## NesCore/IO.cs

> No `using` directives.

IO dispatch ($2000–$4017 read/write routing) calls only functions already declared in other files within the same `partial class NesCore`, requiring no additional namespaces.

---

## NesCore/JoyPad.cs

```csharp
using System.Runtime.CompilerServices;
```

| Namespace | Purpose |
|---|---|
| `System.Runtime.CompilerServices` | `[MethodImpl(AggressiveInlining)]`, applied to gamepad read functions such as `gamepad_r_4016()`, which are triggered on every read cycle |

---

## NesCore/Mapper/ (all Mapper files)

`IMapper.cs`, `Mapper000.cs` through `Mapper071.cs` all have **no `using` directives**.

Mappers only operate on raw pointers (`byte*`) and `NesCore` static members; all types are within the `AprNes` namespace, requiring no external namespace references.

---

## Summary Table

| Namespace | Files | Core Function Summary |
|---|---|---|
| `System` | Main.cs, MEM.cs, APU.cs | Basic types, Console, Math, Exception |
| `System.IO` | Main.cs | SRAM .sav file read/write |
| `System.Windows.Forms` | Main.cs | GUI mode error dialog |
| `System.Reflection` | Main.cs | Dynamic Mapper object creation via reflection |
| `System.Runtime.InteropServices` | Main.cs, APU.cs | Unmanaged memory allocation (pointers), P/Invoke winmm.dll |
| `System.Runtime.CompilerServices` | CPU.cs, MEM.cs, PPU.cs, JoyPad.cs | `AggressiveInlining` performance inlining |
| `System.Threading` | Main.cs, PPU.cs, APU.cs | Thread control, FPS throttling, audio buffer synchronization |
| `System.Diagnostics` | PPU.cs | `Stopwatch` FPS timing |
