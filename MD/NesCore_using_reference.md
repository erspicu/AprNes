# NesCore — `using` 引用說明

整理 `NesCore/` 資料夾內所有 `.cs` 檔案的 `using` 指令，說明各命名空間在此專案中的實際用途。

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

| 命名空間 | 用途 |
|---|---|
| `System` | `EventArgs`（`VideoOut` 繼承用）、`Exception`（init 時 try/catch）、`Console`（除錯輸出）|
| `System.Windows.Forms` | `MessageBox.Show()`，在 GUI 模式下顯示錯誤訊息 |
| `System.IO` | `File.ReadAllBytes` / `File.WriteAllBytes`，讀寫 battery-backed SRAM（`.sav` 檔）|
| `System.Runtime.InteropServices` | `Marshal.AllocHGlobal`，以非託管記憶體分配 `NES_MEM`、`ppu_ram`、`ScreenBuf1x` 等原始指標；`Marshal.FreeHGlobal` 釋放 |
| `System.Threading` | `ManualResetEvent _event`，用於暫停/繼續模擬執行緒的同步控制 |
| `System.Reflection` | `Activator.CreateInstance` + `Type.GetType("AprNes.Mapper" + n)`，透過反射動態建立 Mapper 物件（依 ROM 內的 mapper 編號決定） |

---

## NesCore/CPU.cs

```csharp
#define illegal
using System.Runtime.CompilerServices;
```

| 命名空間／前置指令 | 用途 |
|---|---|
| `#define illegal` | 條件編譯旗標，啟用非官方（illegal/undocumented）6502 指令支援 |
| `System.Runtime.CompilerServices` | `[MethodImpl(MethodImplOptions.AggressiveInlining)]`，套用在 `GetFlag()`、`SetFlag()`、`NMIInterrupt()`、`IRQInterrupt()` 等高頻呼叫函式，要求 JIT 強制內聯以減少呼叫開銷 |

---

## NesCore/MEM.cs

```csharp
using System;
using System.Runtime.CompilerServices;
```

| 命名空間 | 用途 |
|---|---|
| `System` | `Console.WriteLine` 用於開機初始化除錯輸出 |
| `System.Runtime.CompilerServices` | `[MethodImpl(AggressiveInlining)]`，套用在 `tick()`、`Mem_r()`、`Mem_w()` 等每個 CPU 周期都會執行的核心函式 |

---

## NesCore/PPU.cs

```csharp
using System.Diagnostics;
using System.Threading;
using System.Runtime.CompilerServices;
```

| 命名空間 | 用途 |
|---|---|
| `System.Diagnostics` | `Stopwatch`，用來計算每幀耗時，實作 NTSC 60.0988 fps 的 FPS 限制器 |
| `System.Threading` | `Thread.Sleep(1)` 搭配 `timeBeginPeriod(1)` 做精確的 FPS 節流；`volatile` 關鍵字配合多執行緒存取 `frame_count` |
| `System.Runtime.CompilerServices` | `[MethodImpl(AggressiveInlining)]`，套用在 PPU 的逐 dot 步進函式 `ppu_step_new()` 等 |

---

## NesCore/APU.cs

```csharp
using System.Runtime.InteropServices;
using System.Threading;
using System;
```

| 命名空間 | 用途 |
|---|---|
| `System.Runtime.InteropServices` | `[DllImport("winmm.dll")]` 宣告 WaveOut API（`waveOutOpen`、`waveOutWrite`、`waveOutPrepareHeader` 等）；`[StructLayout(LayoutKind.Sequential)]` 讓 `WAVEFORMATEX`、`WAVEHDR` 結構與 C API 記憶體排列相符 |
| `System.Threading` | `Thread` 建立獨立的音訊輸出執行緒（填充 WaveOut buffer）；`Interlocked` / `volatile` 處理音訊 buffer 完成的多執行緒通知 |
| `System` | `IntPtr` 存放 WaveOut handle；`Math` 用於波形計算；`Console` 輸出初始化訊息 |

---

## NesCore/IO.cs

> 無任何 `using`。

IO 分派（$2000–$4017 讀寫路由）全部呼叫同一 `partial class NesCore` 內其他檔案已宣告的函式，不需要額外命名空間。

---

## NesCore/JoyPad.cs

```csharp
using System.Runtime.CompilerServices;
```

| 命名空間 | 用途 |
|---|---|
| `System.Runtime.CompilerServices` | `[MethodImpl(AggressiveInlining)]`，套用在手把讀取函式 `gamepad_r_4016()` 等，每個讀取週期都會觸發 |

---

## NesCore/Mapper/（所有 Mapper 檔案）

`IMapper.cs`、`Mapper000.cs` ～ `Mapper071.cs` 全部 **無任何 `using`**。

Mapper 只操作原始指標（`byte*`）與 `NesCore` 靜態成員，所有型別皆在 `AprNes` 命名空間內，不需要引用外部命名空間。

---

## 彙整表

| 命名空間 | 出現的檔案 | 核心功能摘要 |
|---|---|---|
| `System` | Main.cs, MEM.cs, APU.cs | 基礎型別、Console、Math、Exception |
| `System.IO` | Main.cs | SRAM .sav 檔讀寫 |
| `System.Windows.Forms` | Main.cs | GUI 模式錯誤對話框 |
| `System.Reflection` | Main.cs | 反射動態建立 Mapper 物件 |
| `System.Runtime.InteropServices` | Main.cs, APU.cs | 非託管記憶體分配（指標）、P/Invoke winmm.dll |
| `System.Runtime.CompilerServices` | CPU.cs, MEM.cs, PPU.cs, JoyPad.cs | `AggressiveInlining` 效能內聯 |
| `System.Threading` | Main.cs, PPU.cs, APU.cs | 執行緒控制、FPS 節流、音訊 buffer 同步 |
| `System.Diagnostics` | PPU.cs | `Stopwatch` FPS 計時 |
