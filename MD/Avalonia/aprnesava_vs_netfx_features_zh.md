# aprnesava 相比 AprNes NetFx 的獨有特色與優勢

撰寫日期：2026-04-26
適用版本：master @ 44ef8b9（HD_NTSC merge 後）

---

## 0. 兩版定位

| 版本 | Target | UI 框架 | 路徑 | 維護狀態 |
|---|---|---|---|---|
| **AprNes NetFx** | .NET Framework 4.8.1, x64 | Windows Forms + GDI+ | `AprNes/` | 2026-04-19 起基本停止維護 |
| **aprnesava** (AprNesAvalonia) | .NET 10 | Avalonia 11.3 + SkiaSharp 3.119 | `AprNesAvalonia/` | 未來主線 |

兩版**共用同一套 NesCore 程式碼**（`<Compile Include="../AprNes/NesCore/**/*.cs" />`），CPU/PPU/APU/MEM 邏輯完全一致，所以 emulation 精度兩邊都是 184/184 blargg + 138/138 AccuracyCoin v2 滿分。差異全部在 UI 層、渲染層、跟 .NET 10 才開的 build symbol。

---

## 1. GPU 加速 CRT 後處理（aprnesava 獨有）

NetFx 的 CRT pipeline 永遠在 CPU 跑（`CrtScreen.cs` Scalar 路徑），靠 `Vector<T>` portable SIMD 把 luma blur、shadow mask、scanline、convergence、curvature 全部算完。10× scale (2560×2100) 下 CPU 是瓶頸，整片 22 MB 像素資料每 frame 都要在 CPU 上跑一遍。

aprnesava 額外有兩套後端可選：

| Backend | 實作 | 角色 |
|---|---|---|
| Scalar | `CrtScreen.cs` | 跟 NetFx 共用，portable Vector<T> |
| **SIMD** | `CrtScreen.Simd.cs` | x86 hardware intrinsics（Avx2 / Vector256 / GatherVector256 / 顯式 FMA / `[SkipLocalsInit]`） |
| **GPU** | `CrtScreen.Gpu.cs` + `CrtGpuRenderThread.cs` + SkSL shader | render thread 直接在 D3D11 GPU 上跑 SkRuntimeEffect |

GPU backend 是 Phase 3A 的 render-thread 整合：emu thread 把 NTSC 完的 `linearBuffer`（float RGB planes）寫入 → render thread 透過 Avalonia 的 `ISkiaSharpApiLeaseFeature` 拿到 GPU-backed `SkCanvas` → SkSL shader 在 D3D11 上跑完整 CRT 後處理（Catmull-Rom / Mitchell 採樣、phosphor decay ping-pong、shadow mask、curvature、convergence、scanline、vignette）→ 直接 blit 到視窗 surface，**全程不回 CPU**。

實測 10× scale benchmark：

| Strategy | Presented FPS | Emu FPS |
|---|---|---|
| Scalar (≈ NetFx 的 CPU 路徑) | 27.68 | 61.81 |
| SIMD | 23.45 | 70.63 |
| **GPU** | **58.67** | **107.03** |

GPU 對 CPU 後端 **2.5×** 的 presented FPS 領先，emu thread 也因為不再扛 CRT 工作一起釋放出來（107 FPS vs 62）。

---

## 2. HD_NTSC 2× 過採樣（aprnesava 獨有）

2026-04-26 合併入 master 的 `HD_NTSC` build symbol，**只在 aprnesava csproj 定義**：

| 量 | NetFx | aprnesava |
|---|---|---|
| samples per scanline (`kOutW`) | 1024 | **2048** |
| samples per NES dot (`kSampDot`) | 4 | **8** |
| 相對 Fsc oversampling | 6× | **12×** |
| Phase table 大小 (`kPhaseEntries`) | 6 | **12** |
| Filter window Y/I/Q | 6/18/54 | **12/36/108** |
| linearBuffer 記憶體 | 2.88 MB | 5.76 MB |

效益：
- **Chroma 解調精度更高**：12× 過採樣讓 RF 模式的 herringbone、color fringing、chroma blur 還原更接近真實 NTSC 訊號特性
- **Filter cutoff 不變**：IIR 係數（ChromaBlur / SlewRate / RingStrength）自動 ÷2 維持同樣的物理 Hz cutoff
- **NetFx 完全不受影響**：所有 HD 相關常數跟程式碼都在 `#if HD_NTSC` 後面，NetFx 編譯時 const-fold 回 1024 path，IL byte-identical
- **GPU 幾乎免費吃下**：emu thread 多算 2× sample 的成本被 GPU 後端吸收，整體 FPS 沒明顯掉

完整設計細節見 `MD/Avalonia/ntsc_2048_sampling_plan.md`。

---

## 3. .NET 10 Runtime 紅利

| 面向 | NetFx (.NET Framework 4.8.1) | aprnesava (.NET 10) |
|---|---|---|
| JIT | RyuJIT 舊版 | RyuJIT .NET 10（明顯改善的 SIMD codegen、enum optimization、escape analysis） |
| Tiered Compilation | 部分 | **全開**（`<TieredCompilation>true`） |
| Tiered PGO | 不支援 | **全開**（`<TieredPGO>true`），第 2 次以後熱路徑用 profile-guided 最佳化 |
| `Vector<T>` 寬度 | 多半 128-bit (SSE2) | 自動 256-bit (AVX2) / 512-bit (AVX-512) |
| `Vector.MultiplyAddEstimate` (FMA) | 無 | 有（Ntsc.cs 內 `#if NET10_0_OR_GREATER` 走 FMA chain） |
| `[SkipLocalsInit]` 等屬性 | 無 | 有 |
| `LangVersion` | 11 | 最新（隱式） |
| GC | 舊 server GC | 改良過的 server GC + 大物件 / pinning 表現更好 |

直接量化效益：同一份 NesCore 程式碼，aprnesava 版的 emu FPS 對 NetFx 高約 30-50%（因 ROM 而異），SIMD 跑 NTSC pipeline 更密。

---

## 4. 零拷貝渲染管線（aprnesava 獨有）

NetFx 走 GDI+ 的 `Graphics.DrawImage`，每 frame 至少一次 byte[]→Bitmap→Graphics 的 CPU copy + format convert。

aprnesava 的 `EmuScreenControl.EmuDrawOperation`：
- 接外部 `IntPtr FrontBufferPtr` 直接指模擬器寫的 unmanaged buffer
- 在 Avalonia render thread 用 `SKBitmap.InstallPixels(info, ptr, stride)` — **O(1)，沒有像素拷貝**
- 經由 `ICustomDrawOperation` 直接在 GPU surface 上 `DrawBitmap`
- UI thread 不參與像素搬運，render 在獨立 thread

這跟 GPU CRT backend 是同一條 ISkiaSharpApiLeaseFeature 拿 GR Context 的架構，整個渲染管線「emu unmanaged buffer → GPU texture → 螢幕」全程零拷貝。

---

## 5. 平台抽象層（aprnesava 獨有）

NetFx 直接呼叫 Win32 `waveOutOpen` / DirectInput8 / XInput，跨平台無解。

aprnesava 加了 `Platform/` 介面層：
- `IAudioBackend` — Win32WaveOutBackend 是預設實作，未來可換 OpenAL / NAudio / Linux ALSA
- `IGamepadBackend` — Win32GamepadBackend (DirectInput8 + XInput) / NullGamepadBackend
- `PlatformFactory` — 根據 OS 選擇實作

雖然目前**只實際支援 Windows**（waveOut / DirectInput 都是 Win32 only），但介面已經切乾淨，要擴 Linux / macOS 不用動 emulator 邏輯，只要寫平台後端。

---

## 6. UI 架構升級（aprnesava 獨有）

| 面向 | NetFx | aprnesava |
|---|---|---|
| UI 描述 | 手寫 designer.cs | XAML（編譯時 binding） |
| Theme | Win32 預設 | Avalonia Fluent + Inter font |
| 視覺效果 | 無動畫、無透明 | 有 |
| HiDPI | 部分支援（per-monitor 有問題） | 原生 per-monitor v2 |
| Drag & drop ROM | 沒有 | `MainWindow.axaml.cs:678` 有 |

ConfigWindow 重構（2026-03-31）：5 分頁式（P1/P2 Input / Graphics / Audio / General）+ AnalogConfigWindow（NTSC + CRT 微調）+ AudioPlusConfigWindow（NES 聲道 + 擴展 + 後處理）。NetFx 是單一 `AprNes_ConfigureUI` 把所有設定塞在一個視窗。

---

## 7. SkSL Runtime Shader 系統（aprnesava 獨有）

`AprNesAvalonia/Shaders/` 內有：
- `crt_core_v1.sksl` — baseline
- `crt_core_20260426193000_catmullrom.sksl` — Catmull-Rom 4-tap 立方採樣
- `crt_core_20260426193627_mitchell.sksl` — Mitchell-Netravali 4-tap

`ShaderLoader.LoadLatest("crt_core_", ...)` 會自動挑時間戳最新的版本，舊版 fallback 還在。可以 hot-swap shader 不用重新編譯整個 emu。NetFx 完全沒這層 — CRT 全部 hard-coded 在 C# 裡。

---

## 8. Build 與工具鏈

| 面向 | NetFx | aprnesava |
|---|---|---|
| 編譯指令 | VS2022 MSBuild（langversion 11 需求） | `dotnet build` 或 `build_avalonia.bat` |
| 編譯時間（Debug） | ~10s | ~4s |
| 編譯時間（Release） | ~15s | ~5s |
| Output | `AprNes/bin/Debug/AprNes.exe` | `AprNesAvalonia/bin/Debug/net10.0/AprNesAvalonia.exe` |
| 嵌入 build timestamp | 沒有 | 有（`SourceRevisionId` 自動帶日期時間，`copy/rename` 後仍可查） |
| Conditional compilation | 較少 | `CRT_SIMD_AVAILABLE`、`CRT_GPU_AVAILABLE`、`HD_NTSC` 等多個 build symbol |

---

## 9. 何時還是該選 NetFx？

aprnesava 全面領先，但兩種情境 NetFx 還有用：

1. **目標機只有 .NET Framework**（例如 Windows 7 / 8 仍裝 4.8.1，不能裝 .NET 10）— aprnesava 不可能跑
2. **CPU 沒有 AVX2 也沒 GPU**（極舊的 Atom/Bobcat）— GPU backend 無效，又吃不到 .NET 10 SIMD 紅利時，NetFx 跟 aprnesava-Scalar 差不多

絕大多數現代 PC（Windows 10+, AVX2 CPU, 任何 GPU）aprnesava 是更好的選擇。

---

## 10. 共用且兩邊一致的部分

避免誤會 — 以下這些**兩版完全相同**，沒有差異：

- CPU/PPU/APU/MEM/Mapper 模擬精度
- 聲音 channel 數 + AudioPlus 擴展（VRC6/MMC5/N163/FME-7/VRC7/Sunsoft）
- 184/184 blargg + 138/138 AccuracyCoin v2 測試結果
- ROM 載入、saveram、savestate、cheat 相關邏輯
- 大部分輸入處理（兩版都用同一份 `joystick.cs` / `DirectInputHelper.cs`）

---

## 11. 結論

aprnesava 的優勢可以歸納成四個層級：

1. **演算法精度層** — HD_NTSC 12× Fsc 過採樣（aprnesava only）
2. **效能層** — GPU CRT pipeline + .NET 10 JIT + zero-copy render（FPS 翻倍）
3. **使用者體驗層** — Avalonia Fluent UI + drag-drop + per-monitor HiDPI
4. **可擴展性層** — 平台抽象 + SkSL hot-swap + 多 backend dispatch

NetFx 版仍然能跑、測試也都過，但所有新功能都會在 aprnesava 上做，NetFx 等同凍結維護模式。
