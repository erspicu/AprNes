# .NET Framework → .NET 8 Native AOT 相容性問題整理

> 本文件整理 AprNes 專案（NES 模擬器）從 .NET Framework 4.x WinForms 遷移至
> .NET 8 Native AOT 獨立執行檔過程中遇到的所有相容性問題，以及解決方案。
>
> 日期：2026-02-26

---

## 目錄

1. [架構概覽](#架構概覽)
2. [反射（Reflection）問題](#1-反射-reflection-問題)
3. [Application.StartupPath 問題](#2-applicationstartuppath-問題)
4. [WinForms 無法在 AOT 使用](#3-winforms-無法在-aot-使用)
5. [P/Invoke DLL 來源錯誤](#4-pinvoke-dll-來源錯誤)
6. [PAINTSTRUCT 結構佈局錯誤](#5-paintstruct-結構佈局錯誤)
7. [Marshal.SizeOf(Type) 非泛型版本](#6-marshalsizeoftype-非泛型版本-warning)
8. [固定陣列（fixed array）在結構中的宣告](#7-固定陣列-fixed-array-在結構中的宣告)
9. [OutputType 設定](#8-outputtype-設定)
10. [MSVC Linker 缺少導致 Publish 失敗](#9-msvc-linker-缺少導致-publish-失敗)
11. [GetModuleHandleW 來源 DLL 錯誤](#10-getmodulehandlew-來源-dll-錯誤)
12. [WndProc Delegate 被 GC 回收](#11-wndproc-delegate-被-gc-回收)
13. [共用原始碼策略](#共用原始碼策略)
14. [總結對照表](#總結對照表)

---

## 架構概覽

```
AprNes/                      ← 原始 .NET Framework 專案 (WinForms)
├── NesCore/                 ← 模擬器核心（共用）
├── tool/                    ← 工具類別（部分共用，部分修改）
└── UI/                      ← WinForms UI（無法共用）

AprNesAOT/                   ← 新 .NET 8 Native AOT 專案
├── AprNesAOT.csproj         ← 用 Compile Include 連結共用原始碼
├── AprNesAOT.xml            ← Trimmer Root Descriptor
└── Program.cs               ← 純 Win32 P/Invoke UI（取代 WinForms）
```

---

## 1. 反射（Reflection）問題

### 問題描述

.NET Framework 程式大量使用反射在執行期動態建立物件：

```csharp
// ❌ AOT 不相容 — 執行期 Type.GetType() 無法在 AOT 中使用
IMapper mapperObj = (IMapper)Activator.CreateInstance(
    Type.GetType("AprNes.Mapper_" + mapper_id));

// ❌ AOT 不相容 — 反射欄位存取
FieldInfo fi = typeof(SomeClass).GetField("fieldName");
```

### 原因

Native AOT 在編譯期對所有程式碼做靜態分析，**不支援執行期動態型別解析**。`Type.GetType()` 需要完整的型別中繼資料，AOT 在 Trim 後這些資訊可能不存在。

### 解決方案

改用 switch-case 工廠模式，讓編譯器在編譯期就能確定所有可能的型別：

```csharp
// ✅ AOT 相容 — 靜態工廠
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

### 相關設定

若無法完全移除反射（例如第三方套件），可在 Trimmer Root Descriptor XML 中保留型別：

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

## 2. Application.StartupPath 問題

### 問題描述

```csharp
// ❌ .NET Framework — 依賴 System.Windows.Forms
using System.Windows.Forms;
string path = Application.StartupPath + @"\AprNesLang.ini";
```

`Application.StartupPath` 屬於 `System.Windows.Forms.Application`，在 AOT 或非 WinForms 專案中無法使用。

### 解決方案

```csharp
// ✅ .NET 5+ 相容，同時支援 .NET Framework 4.6.1+
string path = Path.Combine(AppContext.BaseDirectory, "AprNesLang.ini");
```

> ⚠️ 注意：`AppContext.BaseDirectory` 在 Windows 上返回的路徑**含有尾部反斜線**，
> 請務必使用 `Path.Combine()` 而非字串直接拼接。

---

## 3. WinForms 無法在 AOT 使用

### 問題描述

`System.Windows.Forms`（WinForms）**完全不支援 Native AOT**，原因：

- WinForms 大量使用反射（Designer、資源載入、控制項建立）
- WinForms 的 `Form`、`Control` 等依賴動態程式碼生成
- `Application.Run()` 內部依賴無法 Trim 的組件

### 解決方案

AOT 版本需要完全**以純 Win32 P/Invoke 重新實作 UI**：

```csharp
// ✅ 用 Win32 API 手動建立視窗
[DllImport("user32.dll")] static extern nint CreateWindowExW(...);
[DllImport("user32.dll")] static extern nint CreateMenu();
[DllImport("user32.dll")] static extern bool AppendMenuW(...);
[DllImport("comdlg32.dll")] static extern bool GetOpenFileNameW(ref OPENFILENAME ofn);
```

#### 主要替換對照

| WinForms | Win32 P/Invoke 替代 |
|----------|-------------------|
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

## 4. P/Invoke DLL 來源錯誤

### 問題描述（實際發生）

```
Unhandled exception. System.EntryPointNotFoundException:
Unable to find an entry point named 'SetBkMode' in DLL 'user32.dll'.
```

在手寫 Win32 P/Invoke 時，容易將 GDI 函式誤放到 user32.dll：

```csharp
// ❌ 錯誤 — GDI 函式放在 user32.dll
[DllImport("user32.dll")] static extern nint SetBkMode(nint hdc, int mode);
[DllImport("user32.dll")] static extern uint SetTextColor(nint hdc, uint color);
[DllImport("user32.dll")] static extern nint GetModuleHandleW(nint lpModuleName);
```

### 解決方案

需確認每個 Win32 函式的正確 DLL：

```csharp
// ✅ 正確分類
// user32.dll — 視窗/訊息/選單/輸入
[DllImport("user32.dll")] static extern nint CreateWindowExW(...);
[DllImport("user32.dll")] static extern bool AppendMenuW(...);

// gdi32.dll — 繪圖/字型/顏色
[DllImport("gdi32.dll")] static extern nint SetBkMode(nint hdc, int mode);
[DllImport("gdi32.dll")] static extern uint SetTextColor(nint hdc, uint color);
[DllImport("gdi32.dll")] static extern int  SetDIBitsToDevice(...);

// kernel32.dll — 程序/模組/記憶體
[DllImport("kernel32.dll")] static extern nint GetModuleHandleW(nint name);

// shell32.dll — Shell 整合
[DllImport("shell32.dll")] static extern void DragAcceptFiles(...);

// comdlg32.dll — 通用對話框
[DllImport("comdlg32.dll")] static extern bool GetOpenFileNameW(ref OPENFILENAME ofn);
```

#### 常用 Win32 函式 DLL 速查

| DLL | 常用函式 |
|-----|---------|
| `user32.dll` | CreateWindowExW, RegisterClassExW, DefWindowProcW, PostQuitMessage, MessageBoxW, DrawTextW, SetWindowTextW, GetClientRect, SetMenu, AppendMenuW, CheckMenuItem, SetTimer, BeginPaint, EndPaint, InvalidateRect |
| `gdi32.dll` | SetBkMode, SetTextColor, SetDIBitsToDevice, StretchDIBits, CreateCompatibleDC, DeleteDC, SelectObject, DeleteObject |
| `kernel32.dll` | GetModuleHandleW, AllocConsole, GetLastError |
| `shell32.dll` | DragAcceptFiles, DragQueryFileW, DragFinish |
| `comdlg32.dll` | GetOpenFileNameW, GetSaveFileNameW |
| `winmm.dll` | waveOutOpen, waveOutWrite, joyGetPos |

---

## 5. PAINTSTRUCT 結構佈局錯誤

### 問題描述

Win32 `PAINTSTRUCT` 結構的完整定義：

```c
typedef struct tagPAINTSTRUCT {
    HDC  hdc;
    BOOL fErase;
    RECT rcPaint;           // left, top, right, bottom (4 × int)
    BOOL fRestore;          // ← 常被漏掉
    BOOL fIncUpdate;        // ← 常被漏掉
    BYTE rgbReserved[32];   // ← 必須是 inline fixed array
} PAINTSTRUCT;
```

```csharp
// ❌ 不完整 — 缺少 fRestore/fIncUpdate，且 rgbReserved 用 byte[]
[StructLayout(LayoutKind.Sequential)]
struct PAINTSTRUCT {
    public nint hdc;
    public int fErase;
    public int rcLeft, rcTop, rcRight, rcBottom;
    public byte[] rgbReserved; // ← 管理陣列，佈局完全錯誤
}
```

### 解決方案

```csharp
// ✅ 完整正確定義
[StructLayout(LayoutKind.Sequential)]
unsafe struct PAINTSTRUCT {
    public nint hdc;
    public int  fErase;
    public int  rcLeft, rcTop, rcRight, rcBottom; // RECT 展開
    public int  fRestore;
    public int  fIncUpdate;
    public fixed byte rgbReserved[32]; // ← 必須用 fixed inline array
}
```

> ⚠️ 結構佈局錯誤不會在編譯期報錯，只會導致記憶體損毀或畫面異常，**非常難以追蹤**。
> 建議對照 MSDN 文件逐欄確認。

---

## 6. Marshal.SizeOf(Type) 非泛型版本（Warning）

### 問題描述

```
warning IL3050: Using member 'Marshal.SizeOf(Type)' which has
'RequiresDynamicCodeAttribute' can break functionality when AOT compiling.
```

```csharp
// ⚠️ 會產生 IL3050 警告（AOT 可能失效）
int size = Marshal.SizeOf(typeof(WAVEHDR));
```

### 解決方案

改用泛型版本（完全 AOT 相容）：

```csharp
// ✅ AOT 相容
int size = Marshal.SizeOf<WAVEHDR>();
```

> 本專案中 `WaveOutPlayer.cs` 和 `joystick.cs` 仍使用非泛型版本（從 .NET Framework 繼承），
> 目前僅產生 warning 不影響執行，但正式產品環境建議修正。

---

## 7. 固定陣列（fixed array）在結構中的宣告

### 問題描述

AOT 中在 `struct` 內使用 `fixed` 陣列需要 `unsafe`：

```csharp
// ❌ 編譯錯誤：fixed 需要 unsafe struct
[StructLayout(LayoutKind.Sequential)]
struct PAINTSTRUCT {
    public fixed byte rgbReserved[32]; // CS0214 error
}
```

### 解決方案

加上 `unsafe` 關鍵字：

```csharp
// ✅ 正確
[StructLayout(LayoutKind.Sequential)]
unsafe struct PAINTSTRUCT {
    public fixed byte rgbReserved[32];
}
```

並且在 `.csproj` 中啟用 unsafe：

```xml
<PropertyGroup>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
</PropertyGroup>
```

---

## 8. OutputType 設定

### 問題描述

```csharp
// .csproj 設定為 Exe（Console 子系統）
<OutputType>Exe</OutputType>
```

這會建立 **Console 子系統** 的 exe，在 Windows 上執行時會額外跳出黑色命令列視窗。

### 解決方案

```xml
<!-- ✅ Windows GUI 子系統，不產生 Console 視窗 -->
<OutputType>WinExe</OutputType>
```

---

## 9. MSVC Linker 缺少導致 Publish 失敗

### 問題描述

`dotnet publish -r win-x64`（Native AOT）需要 MSVC 的 `link.exe`（x64 版本）：

```
error : Platform linker not found. Ensure you have the required
components to build native code for win-x64.
```

### 解決方案

需要安裝 **Visual Studio 的 C++ x64 建置工具**：

1. 開啟 Visual Studio Installer
2. 點選「修改（Modify）」
3. 勾選「使用 C++ 的桌面開發」→「MSVC v143 - VS 2022 C++ x64/x86 建置工具」
4. 安裝（**需要系統管理員權限**）

或使用命令列安裝（需提升權限）：
```powershell
vs_installer.exe modify `
  --installPath "C:\Program Files\Microsoft Visual Studio\2022\Community" `
  --add Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
  --quiet --norestart
```

> ⚠️ 注意：此安裝需要 **UAC 提升（以系統管理員執行）**，
> 一般使用者帳戶執行會回傳 exit code 5007（需要提升）。

---

## 10. GetModuleHandleW 來源 DLL 錯誤

### 問題描述（實際發生）

```
Unhandled exception. System.EntryPointNotFoundException:
Unable to find an entry point named 'GetModuleHandleW' in DLL 'user32.dll'.
```

```csharp
// ❌ 錯誤 — GetModuleHandleW 不在 user32.dll
[DllImport("user32.dll")]
static extern nint GetModuleHandleW(nint lpModuleName);
```

### 解決方案

```csharp
// ✅ 正確 — GetModuleHandleW 在 kernel32.dll
[DllImport("kernel32.dll")]
static extern nint GetModuleHandleW(nint lpModuleName);
```

---

## 11. WndProc Delegate 被 GC 回收

### 問題描述

AOT 中，將 delegate 轉成函式指標後，若沒有維持強參考，GC 可能回收 delegate 導致視窗程序指標失效，出現 Access Violation：

```csharp
// ❌ 危險 — 區域變數可能被 GC 回收
static void Main() {
    WndProcDelegate proc = WndProc; // 區域變數
    nint ptr = Marshal.GetFunctionPointerForDelegate(proc);
    // ... proc 可能在此被回收
}
```

### 解決方案

宣告為 `static` 欄位以維持強參考（不會被 GC 回收）：

```csharp
// ✅ static 欄位保持存活
delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);
static WndProcDelegate _wndProcDelegate; // static 欄位

static void Main() {
    _wndProcDelegate = WndProc; // 指派到 static
    nint ptr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
    // ...
}
```

---

## 共用原始碼策略

AprNesAOT 的 `.csproj` 使用 Glob 模式連結原始碼，避免複製檔案：

```xml
<ItemGroup>
  <!-- 共用 NesCore -->
  <Compile Include="..\AprNes\NesCore\*.cs" />
  <Compile Include="..\AprNes\NesCore\Mapper\*.cs" />
  <!-- 共用工具（AOT 相容部分） -->
  <Compile Include="..\AprNes\tool\LangINI.cs" />
  <Compile Include="..\AprNes\tool\WaveOutPlayer.cs" />
  <Compile Include="..\AprNes\tool\joystick.cs" />
  <Compile Include="..\AprNes\tool\NativeAPIShare.cs" />
</ItemGroup>
```

#### 無法共用的檔案（System.Drawing 依賴）

| 檔案 | 原因 |
|------|------|
| `UI/AprNesUI.cs` | 繼承 `Form`，使用 WinForms |
| `UI/AprNes_ConfigureUI.cs` | 同上 |
| `tool/InterfaceGraphic.cs` | 使用 `System.Drawing.Graphics` |
| `tool/NativeRendering.cs` | 使用 `System.Drawing.Bitmap` |
| `tool/Scalex.cs` | 使用 `System.Drawing` |
| `tool/libXBRz.cs` | 使用 `System.Drawing` |

> `System.Drawing` 在 .NET 8 上 AOT 支援有限（部分 GDI+ 功能需要 runtime），
> 建議改用 GDI 直接繪製（`SetDIBitsToDevice` / `StretchDIBits`）。

---

## 總結對照表

| 問題類別 | .NET Framework 寫法 | .NET 8 AOT 替代方案 | 嚴重度 |
|---------|-------------------|-------------------|--------|
| 動態型別建立 | `Activator.CreateInstance(Type.GetType(...))` | switch-case 工廠 | 🔴 編譯錯誤 |
| 反射欄位存取 | `typeof(T).GetField(...)` | 直接存取或靜態方法 | 🔴 執行期異常 |
| 應用程式路徑 | `Application.StartupPath` | `AppContext.BaseDirectory` | 🔴 編譯錯誤 |
| UI 框架 | `System.Windows.Forms` | Win32 P/Invoke 手寫 | 🔴 完全不相容 |
| GDI 函式 DLL | 誤放 user32.dll | 改 gdi32.dll | 🔴 執行期崩潰 |
| Kernel 函式 DLL | 誤放 user32.dll | 改 kernel32.dll | 🔴 執行期崩潰 |
| PAINTSTRUCT 結構 | 欄位不完整 / byte[] | 完整欄位 / fixed byte[32] | 🟠 記憶體損毀 |
| Marshal.SizeOf | `Marshal.SizeOf(typeof(T))` | `Marshal.SizeOf<T>()` | 🟡 IL3050 Warning |
| WndProc Delegate | 區域變數 | static 欄位 | 🟠 隨機崩潰 |
| 輸出類型 | `<OutputType>Exe</OutputType>` | `<OutputType>WinExe</OutputType>` | 🟡 多餘 Console 視窗 |
| Linker 工具 | 不需要 | 需安裝 MSVC x64 Build Tools | 🔴 Publish 失敗 |

---

*文件整理：AprNes 專案  
參考版本：.NET Framework 4.8 → .NET 8.0 Native AOT (win-x64)*
