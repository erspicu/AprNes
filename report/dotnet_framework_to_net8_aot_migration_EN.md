# .NET Framework → .NET 8 Native AOT Compatibility Issues

> This document catalogues all compatibility issues encountered while migrating the AprNes project
> (a NES emulator) from .NET Framework 4.x WinForms to a .NET 8 Native AOT standalone executable,
> along with their solutions.
>
> Date: 2026-02-26

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Reflection Issues](#1-reflection-issues)
3. [Application.StartupPath Issue](#2-applicationstartuppath-issue)
4. [WinForms Cannot Be Used with AOT](#3-winforms-cannot-be-used-with-aot)
5. [P/Invoke Wrong DLL Source](#4-pinvoke-wrong-dll-source)
6. [PAINTSTRUCT Layout Error](#5-paintstruct-layout-error)
7. [Marshal.SizeOf(Type) Non-Generic Version](#6-marshalsizeoftype-non-generic-version-warning)
8. [Fixed Arrays Inside Structs](#7-fixed-arrays-inside-structs)
9. [OutputType Setting](#8-outputtype-setting)
10. [Missing MSVC Linker Causes Publish Failure](#9-missing-msvc-linker-causes-publish-failure)
11. [GetModuleHandleW Wrong DLL Source](#10-getmodulehandlew-wrong-dll-source)
12. [WndProc Delegate Collected by GC](#11-wndproc-delegate-collected-by-gc)
13. [Shared Source Strategy](#shared-source-strategy)
14. [Summary Comparison Table](#summary-comparison-table)

---

## Architecture Overview

```
AprNes/                      ← Original .NET Framework project (WinForms)
├── NesCore/                 ← Emulator core (shared)
├── tool/                    ← Utility classes (partially shared, partially modified)
└── UI/                      ← WinForms UI (cannot be shared)

AprNesAOT/                   ← New .NET 8 Native AOT project
├── AprNesAOT.csproj         ← Links shared source files via Compile Include
├── AprNesAOT.xml            ← Trimmer Root Descriptor
└── Program.cs               ← Pure Win32 P/Invoke UI (replaces WinForms)
```

---

## 1. Reflection Issues

### Problem Description

.NET Framework code makes heavy use of reflection to dynamically create objects at runtime:

```csharp
// ❌ AOT-incompatible — runtime Type.GetType() cannot be used in AOT
IMapper mapperObj = (IMapper)Activator.CreateInstance(
    Type.GetType("AprNes.Mapper_" + mapper_id));

// ❌ AOT-incompatible — reflective field access
FieldInfo fi = typeof(SomeClass).GetField("fieldName");
```

### Cause

Native AOT performs static analysis of all code at compile time and **does not support runtime dynamic type resolution**. `Type.GetType()` requires full type metadata, which may be absent after trimming in AOT builds.

### Solution

Replace with a switch-case factory pattern so the compiler can determine all possible types at compile time:

```csharp
// ✅ AOT-compatible — static factory
static IMapper CreateMapper(int id) => id switch
{
    0  => new Mapper000(),
    1  => new Mapper001(),
    2  => new Mapper002(),
    3  => new Mapper003(),
    4  => new Mapper004(),
    7  => new Mapper007(),
    11 => new Mapper011(),
    66 => new Mapper066(),
    _  => throw new NotSupportedException($"Mapper {id} not supported")
};
```

### Related Configuration

If reflection cannot be fully removed (e.g., third-party packages), preserve types in the Trimmer Root Descriptor XML:

```xml
<!-- AprNesAOT.xml -->
<linker>
  <assembly fullname="AprNesAOT">
    <type fullname="AprNes.Mapper000" preserve="all"/>
    <type fullname="AprNes.Mapper001" preserve="all"/>
  </assembly>
</linker>
```

---

## 2. Application.StartupPath Issue

### Problem Description

```csharp
// ❌ .NET Framework — depends on System.Windows.Forms
using System.Windows.Forms;
string path = Application.StartupPath + @"\AprNesLang.ini";
```

`Application.StartupPath` belongs to `System.Windows.Forms.Application` and is unavailable in AOT or non-WinForms projects.

### Solution

```csharp
// ✅ Compatible with .NET 5+, also supports .NET Framework 4.6.1+
string path = Path.Combine(AppContext.BaseDirectory, "AprNesLang.ini");
```

> ⚠️ Note: `AppContext.BaseDirectory` returns a path **with a trailing backslash** on Windows.
> Always use `Path.Combine()` rather than direct string concatenation.

---

## 3. WinForms Cannot Be Used with AOT

### Problem Description

`System.Windows.Forms` (WinForms) **has no support for Native AOT** because:

- WinForms makes heavy use of reflection (Designer, resource loading, control creation)
- WinForms `Form`, `Control`, etc. depend on dynamic code generation
- `Application.Run()` internally depends on assemblies that cannot be trimmed

### Solution

The AOT version requires a complete **UI reimplementation using pure Win32 P/Invoke**:

```csharp
// ✅ Create windows manually via Win32 API
[DllImport("user32.dll")] static extern nint CreateWindowExW(...);
[DllImport("user32.dll")] static extern nint CreateMenu();
[DllImport("user32.dll")] static extern bool AppendMenuW(...);
[DllImport("comdlg32.dll")] static extern bool GetOpenFileNameW(ref OPENFILENAME ofn);
```

#### Key Replacement Mappings

| WinForms | Win32 P/Invoke Equivalent |
|----------|--------------------------|
| `Form` | `RegisterClassExW` + `CreateWindowExW` |
| `MenuStrip` | `CreateMenu` + `AppendMenuW` + `SetMenu` |
| `OpenFileDialog` | `GetOpenFileNameW` (comdlg32.dll) |
| `MessageBox.Show()` | `MessageBoxW` (user32.dll) |
| `label.Text = ...` | `DrawTextW` (user32.dll) |
| `this.Text = ...` | `SetWindowTextW` (user32.dll) |
| `Timer` | `SetTimer` / `KillTimer` (user32.dll) |
| `Application.Exit()` | `PostQuitMessage(0)` |
| `Invalidate()` | `InvalidateRect` (user32.dll) |
| `OnPaint` | `WM_PAINT` message in WndProc |

---

## 4. P/Invoke Wrong DLL Source

### Problem Description (actual occurrence)

```
Unhandled exception. System.EntryPointNotFoundException:
Unable to find an entry point named 'SetBkMode' in DLL 'user32.dll'.
```

When writing Win32 P/Invoke by hand it is easy to place GDI functions under user32.dll by mistake:

```csharp
// ❌ Wrong — GDI functions placed under user32.dll
[DllImport("user32.dll")] static extern nint SetBkMode(nint hdc, int mode);
[DllImport("user32.dll")] static extern uint SetTextColor(nint hdc, uint color);
[DllImport("user32.dll")] static extern nint GetModuleHandleW(nint lpModuleName);
```

### Solution

Verify the correct DLL for each Win32 function:

```csharp
// ✅ Correctly categorized
// user32.dll — windows / messages / menus / input
[DllImport("user32.dll")] static extern nint CreateWindowExW(...);
[DllImport("user32.dll")] static extern bool AppendMenuW(...);

// gdi32.dll — drawing / fonts / colors
[DllImport("gdi32.dll")] static extern nint SetBkMode(nint hdc, int mode);
[DllImport("gdi32.dll")] static extern uint SetTextColor(nint hdc, uint color);
[DllImport("gdi32.dll")] static extern int  SetDIBitsToDevice(...);

// kernel32.dll — processes / modules / memory
[DllImport("kernel32.dll")] static extern nint GetModuleHandleW(nint name);

// shell32.dll — Shell integration
[DllImport("shell32.dll")] static extern void DragAcceptFiles(...);

// comdlg32.dll — common dialogs
[DllImport("comdlg32.dll")] static extern bool GetOpenFileNameW(ref OPENFILENAME ofn);
```

#### Quick Reference: Common Win32 Functions by DLL

| DLL | Common Functions |
|-----|-----------------|
| `user32.dll` | CreateWindowExW, RegisterClassExW, DefWindowProcW, PostQuitMessage, MessageBoxW, DrawTextW, SetWindowTextW, GetClientRect, SetMenu, AppendMenuW, CheckMenuItem, SetTimer, BeginPaint, EndPaint, InvalidateRect |
| `gdi32.dll` | SetBkMode, SetTextColor, SetDIBitsToDevice, StretchDIBits, CreateCompatibleDC, DeleteDC, SelectObject, DeleteObject |
| `kernel32.dll` | GetModuleHandleW, AllocConsole, GetLastError |
| `shell32.dll` | DragAcceptFiles, DragQueryFileW, DragFinish |
| `comdlg32.dll` | GetOpenFileNameW, GetSaveFileNameW |
| `winmm.dll` | waveOutOpen, waveOutWrite, joyGetPos |

---

## 5. PAINTSTRUCT Layout Error

### Problem Description

The complete Win32 `PAINTSTRUCT` structure definition:

```c
typedef struct tagPAINTSTRUCT {
    HDC  hdc;
    BOOL fErase;
    RECT rcPaint;           // left, top, right, bottom (4 × int)
    BOOL fRestore;          // ← often omitted
    BOOL fIncUpdate;        // ← often omitted
    BYTE rgbReserved[32];   // ← must be an inline fixed array
} PAINTSTRUCT;
```

```csharp
// ❌ Incomplete — missing fRestore/fIncUpdate, and rgbReserved uses byte[]
[StructLayout(LayoutKind.Sequential)]
struct PAINTSTRUCT {
    public nint hdc;
    public int fErase;
    public int rcLeft, rcTop, rcRight, rcBottom;
    public byte[] rgbReserved; // ← managed array, completely wrong layout
}
```

### Solution

```csharp
// ✅ Complete and correct definition
[StructLayout(LayoutKind.Sequential)]
unsafe struct PAINTSTRUCT {
    public nint hdc;
    public int  fErase;
    public int  rcLeft, rcTop, rcRight, rcBottom; // RECT expanded inline
    public int  fRestore;
    public int  fIncUpdate;
    public fixed byte rgbReserved[32]; // ← must use fixed inline array
}
```

> ⚠️ Struct layout errors do not cause compile-time errors; they only manifest as memory corruption
> or rendering anomalies that are **extremely difficult to track down**.
> Always verify each field against the MSDN documentation.

---

## 6. Marshal.SizeOf(Type) Non-Generic Version (Warning)

### Problem Description

```
warning IL3050: Using member 'Marshal.SizeOf(Type)' which has
'RequiresDynamicCodeAttribute' can break functionality when AOT compiling.
```

```csharp
// ⚠️ Produces IL3050 warning (may fail under AOT)
int size = Marshal.SizeOf(typeof(WAVEHDR));
```

### Solution

Use the generic version (fully AOT-compatible):

```csharp
// ✅ AOT-compatible
int size = Marshal.SizeOf<WAVEHDR>();
```

> `WaveOutPlayer.cs` and `joystick.cs` in this project still use the non-generic version (inherited
> from the .NET Framework codebase). Currently this only produces a warning and does not affect
> execution, but it should be corrected in a production environment.

---

## 7. Fixed Arrays Inside Structs

### Problem Description

Using `fixed` arrays inside a `struct` in AOT requires `unsafe`:

```csharp
// ❌ Compile error: fixed requires unsafe struct
[StructLayout(LayoutKind.Sequential)]
struct PAINTSTRUCT {
    public fixed byte rgbReserved[32]; // CS0214 error
}
```

### Solution

Add the `unsafe` keyword:

```csharp
// ✅ Correct
[StructLayout(LayoutKind.Sequential)]
unsafe struct PAINTSTRUCT {
    public fixed byte rgbReserved[32];
}
```

Also enable unsafe blocks in `.csproj`:

```xml
<PropertyGroup>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
</PropertyGroup>
```

---

## 8. OutputType Setting

### Problem Description

```csharp
// .csproj configured as Exe (Console subsystem)
<OutputType>Exe</OutputType>
```

This produces an exe with the **Console subsystem**, which causes an extra black console window to appear when run on Windows.

### Solution

```xml
<!-- ✅ Windows GUI subsystem, no console window -->
<OutputType>WinExe</OutputType>
```

---

## 9. Missing MSVC Linker Causes Publish Failure

### Problem Description

`dotnet publish -r win-x64` (Native AOT) requires MSVC's `link.exe` (x64 version):

```
error : Platform linker not found. Ensure you have the required
components to build native code for win-x64.
```

### Solution

Install the **C++ x64 build tools for Visual Studio**:

1. Open Visual Studio Installer
2. Click **Modify**
3. Check **Desktop development with C++** → **MSVC v143 - VS 2022 C++ x64/x86 build tools**
4. Install (**requires administrator privileges**)

Or install via command line (requires elevation):
```powershell
vs_installer.exe modify `
  --installPath "C:\Program Files\Microsoft Visual Studio\2022\Community" `
  --add Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
  --quiet --norestart
```

> ⚠️ Note: This installation requires **UAC elevation (run as administrator)**.
> Running as a regular user will return exit code 5007 (elevation required).

---

## 10. GetModuleHandleW Wrong DLL Source

### Problem Description (actual occurrence)

```
Unhandled exception. System.EntryPointNotFoundException:
Unable to find an entry point named 'GetModuleHandleW' in DLL 'user32.dll'.
```

```csharp
// ❌ Wrong — GetModuleHandleW is not in user32.dll
[DllImport("user32.dll")]
static extern nint GetModuleHandleW(nint lpModuleName);
```

### Solution

```csharp
// ✅ Correct — GetModuleHandleW is in kernel32.dll
[DllImport("kernel32.dll")]
static extern nint GetModuleHandleW(nint lpModuleName);
```

---

## 11. WndProc Delegate Collected by GC

### Problem Description

In AOT, after converting a delegate to a function pointer, if no strong reference is kept, the GC may collect the delegate and invalidate the window procedure pointer, causing an Access Violation:

```csharp
// ❌ Dangerous — local variable may be collected by GC
static void Main() {
    WndProcDelegate proc = WndProc; // local variable
    nint ptr = Marshal.GetFunctionPointerForDelegate(proc);
    // ... proc may be collected here
}
```

### Solution

Declare it as a `static` field to maintain a strong reference (preventing GC collection):

```csharp
// ✅ static field keeps it alive
delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);
static WndProcDelegate _wndProcDelegate; // static field

static void Main() {
    _wndProcDelegate = WndProc; // assign to static
    nint ptr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
    // ...
}
```

---

## Shared Source Strategy

`AprNesAOT.csproj` uses glob patterns to link source files, avoiding file duplication:

```xml
<ItemGroup>
  <!-- Shared NesCore -->
  <Compile Include="..\AprNes\NesCore\*.cs" />
  <Compile Include="..\AprNes\NesCore\Mapper\*.cs" />
  <!-- Shared utilities (AOT-compatible parts) -->
  <Compile Include="..\AprNes\tool\LangINI.cs" />
  <Compile Include="..\AprNes\tool\WaveOutPlayer.cs" />
  <Compile Include="..\AprNes\tool\joystick.cs" />
  <Compile Include="..\AprNes\tool\NativeAPIShare.cs" />
</ItemGroup>
```

#### Files That Cannot Be Shared (System.Drawing dependency)

| File | Reason |
|------|--------|
| `UI/AprNesUI.cs` | Inherits `Form`, uses WinForms |
| `UI/AprNes_ConfigureUI.cs` | Same as above |
| `tool/InterfaceGraphic.cs` | Uses `System.Drawing.Graphics` |
| `tool/NativeRendering.cs` | Uses `System.Drawing.Bitmap` |
| `tool/Scalex.cs` | Uses `System.Drawing` |
| `tool/libXBRz.cs` | Uses `System.Drawing` |

> `System.Drawing` has limited AOT support on .NET 8 (some GDI+ features require the runtime).
> It is recommended to switch to direct GDI rendering (`SetDIBitsToDevice` / `StretchDIBits`).

---

## Summary Comparison Table

| Issue Category | .NET Framework Approach | .NET 8 AOT Alternative | Severity |
|----------------|------------------------|------------------------|----------|
| Dynamic type creation | `Activator.CreateInstance(Type.GetType(...))` | Switch-case factory | 🔴 Compile error |
| Reflective field access | `typeof(T).GetField(...)` | Direct access or static method | 🔴 Runtime exception |
| Application path | `Application.StartupPath` | `AppContext.BaseDirectory` | 🔴 Compile error |
| UI framework | `System.Windows.Forms` | Win32 P/Invoke hand-written | 🔴 Completely incompatible |
| GDI function DLL | Mistakenly placed in user32.dll | Move to gdi32.dll | 🔴 Runtime crash |
| Kernel function DLL | Mistakenly placed in user32.dll | Move to kernel32.dll | 🔴 Runtime crash |
| PAINTSTRUCT layout | Incomplete fields / byte[] | Full fields / fixed byte[32] | 🟠 Memory corruption |
| Marshal.SizeOf | `Marshal.SizeOf(typeof(T))` | `Marshal.SizeOf<T>()` | 🟡 IL3050 Warning |
| WndProc delegate | Local variable | Static field | 🟠 Random crash |
| Output type | `<OutputType>Exe</OutputType>` | `<OutputType>WinExe</OutputType>` | 🟡 Spurious console window |
| Linker toolchain | Not required | Must install MSVC x64 Build Tools | 🔴 Publish failure |

---

*Documentation: AprNes project  
Reference versions: .NET Framework 4.8 → .NET 8.0 Native AOT (win-x64)*
