# aprnesava vs. AprNes NetFx — Exclusive Features and Advantages

Date: 2026-04-26
Applies to: master @ 44ef8b9 (post HD_NTSC merge)

---

## 0. Project Positioning

| Edition | Target | UI Framework | Path | Maintenance |
|---|---|---|---|---|
| **AprNes NetFx** | .NET Framework 4.8.1, x64 | Windows Forms + GDI+ | `AprNes/` | Frozen since 2026-04-19 |
| **aprnesava** (AprNesAvalonia) | .NET 10 | Avalonia 11.3 + SkiaSharp 3.119 | `AprNesAvalonia/` | Mainline going forward |

Both editions **share the exact same NesCore source** (`<Compile Include="../AprNes/NesCore/**/*.cs" />`). CPU / PPU / APU / MEM logic is identical, so emulation accuracy is the same on both: 184/184 blargg + 138/138 AccuracyCoin v2 (perfect score). Differences are entirely in the UI layer, render layer, and .NET 10–only build symbols.

---

## 1. GPU-Accelerated CRT Post-Processing (aprnesava only)

NetFx's CRT pipeline always runs on the CPU (`CrtScreen.cs` Scalar path), relying on `Vector<T>` portable SIMD to compute luma blur, shadow mask, scanlines, convergence, and curvature. At 10× scale (2560×2100) the CPU is the bottleneck — 22 MB of pixel data must be processed every frame.

aprnesava adds two more backends:

| Backend | Implementation | Role |
|---|---|---|
| Scalar | `CrtScreen.cs` | Shared with NetFx (portable Vector<T>) |
| **SIMD** | `CrtScreen.Simd.cs` | x86 hardware intrinsics (Avx2 / Vector256 / GatherVector256 / explicit FMA / `[SkipLocalsInit]`) |
| **GPU** | `CrtScreen.Gpu.cs` + `CrtGpuRenderThread.cs` + SkSL shader | Render-thread SkRuntimeEffect on D3D11 |

The GPU backend is the Phase 3A render-thread integration: emu thread writes the post-NTSC `linearBuffer` (float RGB planes) → render thread leases a GPU-backed `SkCanvas` via Avalonia's `ISkiaSharpApiLeaseFeature` → an SkSL shader runs the full CRT post-processing on D3D11 (Catmull-Rom / Mitchell sampling, phosphor decay ping-pong, shadow mask, curvature, convergence, scanline, vignette) → blits straight to the window surface. **The pixel data never returns to the CPU.**

Measured GUI benchmark @ 10× scale:

| Strategy | Presented FPS | Emu FPS |
|---|---|---|
| Scalar (≈ NetFx CPU path) | 27.68 | 61.81 |
| SIMD | 23.45 | 70.63 |
| **GPU** | **58.67** | **107.03** |

GPU backend delivers a **2.5×** lead in presented FPS over the CPU backends, and emu thread is freed from CRT work as well (107 FPS vs. 62).

---

## 2. HD_NTSC 2× Oversampling (aprnesava only)

Merged into master 2026-04-26, the `HD_NTSC` build symbol is **only defined in the aprnesava csproj**:

| Quantity | NetFx | aprnesava |
|---|---|---|
| Samples per scanline (`kOutW`) | 1024 | **2048** |
| Samples per NES dot (`kSampDot`) | 4 | **8** |
| Fsc oversampling | 6× | **12×** |
| Phase table size (`kPhaseEntries`) | 6 | **12** |
| Filter window Y/I/Q | 6/18/54 | **12/36/108** |
| linearBuffer memory | 2.88 MB | 5.76 MB |

Benefits:
- **Higher chroma demodulation precision**: 12× oversampling reproduces RF mode artifacts (herringbone, color fringing, chroma blur) closer to true NTSC signal characteristics.
- **Filter cutoff preserved**: IIR coefficients (ChromaBlur / SlewRate / RingStrength) are auto-halved (`kSampleRateScale = 0.5`) to keep the same physical Hz cutoff at the doubled sample rate.
- **NetFx is not affected**: every HD constant and code path is gated by `#if HD_NTSC`. NetFx const-folds back to the 1024 path — IL is byte-identical to pre-2048 commits.
- **GPU backend absorbs the cost almost free**: doubled NTSC sample work on emu thread is hidden by the GPU offload of CRT.

Full design notes in `MD/Avalonia/ntsc_2048_sampling_plan.md`.

---

## 3. .NET 10 Runtime Dividends

| Aspect | NetFx (.NET Framework 4.8.1) | aprnesava (.NET 10) |
|---|---|---|
| JIT | Older RyuJIT | RyuJIT .NET 10 (significantly improved SIMD codegen, enum optimization, escape analysis) |
| Tiered Compilation | Partial | **Fully on** (`<TieredCompilation>true`) |
| Tiered PGO | Not supported | **Fully on** (`<TieredPGO>true`) — hot paths recompile with profile-guided optimization from run #2 onward |
| `Vector<T>` width | Mostly 128-bit (SSE2) | Auto 256-bit (AVX2) / 512-bit (AVX-512) |
| `Vector.MultiplyAddEstimate` (FMA) | Not available | Available (Ntsc.cs uses FMA chain under `#if NET10_0_OR_GREATER`) |
| `[SkipLocalsInit]` etc. | Not available | Available |
| `LangVersion` | 11 | Latest (implicit) |
| GC | Older server GC | Improved server GC, better large-object / pinning behaviour |

Quantified: with the same NesCore source, aprnesava's emu FPS is roughly 30-50% higher than NetFx (depending on ROM), and the NTSC SIMD pipeline runs much hotter.

---

## 4. Zero-Copy Render Pipeline (aprnesava only)

NetFx uses GDI+ `Graphics.DrawImage`, which forces at least one byte[]→Bitmap→Graphics CPU copy + format conversion per frame.

aprnesava's `EmuScreenControl.EmuDrawOperation`:
- Accepts an external `IntPtr FrontBufferPtr` pointing directly at the emulator's unmanaged buffer
- On Avalonia's render thread it calls `SKBitmap.InstallPixels(info, ptr, stride)` — **O(1), no pixel copy**
- Draws to GPU surface directly via `ICustomDrawOperation`
- UI thread is not in the pixel-handling path; rendering happens on a dedicated render thread

This shares the same `ISkiaSharpApiLeaseFeature` / GR Context with the GPU CRT backend. The whole rendering chain "emu unmanaged buffer → GPU texture → screen" is fully zero-copy.

---

## 5. Platform Abstraction Layer (aprnesava only)

NetFx calls Win32 `waveOutOpen` / DirectInput8 / XInput directly — no path to any other OS.

aprnesava introduces a `Platform/` interface layer:
- `IAudioBackend` — `Win32WaveOutBackend` is the default; future implementations can be OpenAL / NAudio / Linux ALSA
- `IGamepadBackend` — `Win32GamepadBackend` (DirectInput8 + XInput) / `NullGamepadBackend`
- `PlatformFactory` — chooses backend by host OS

In practice **only Windows is currently supported** (waveOut / DirectInput are Win32-only), but the interfaces are clean. Adding Linux / macOS would only require platform backends — emulator logic stays untouched.

---

## 6. UI Architecture Upgrade (aprnesava only)

| Aspect | NetFx | aprnesava |
|---|---|---|
| UI declaration | Hand-written `designer.cs` | XAML (compiled bindings) |
| Theme | Win32 default | Avalonia Fluent + Inter font |
| Animation / transparency | None | Yes |
| HiDPI | Partial (per-monitor flaky) | Native per-monitor v2 |
| Drag-and-drop ROM | None | Yes (`MainWindow.axaml.cs:678`) |

ConfigWindow refactor (2026-03-31): 5-tab layout (P1/P2 Input / Graphics / Audio / General) + AnalogConfigWindow (NTSC + CRT tweaks) + AudioPlusConfigWindow (NES channels + expansion chips + post-processing). NetFx packs everything into a single `AprNes_ConfigureUI` window.

---

## 7. SkSL Runtime Shader System (aprnesava only)

`AprNesAvalonia/Shaders/` contains:
- `crt_core_v1.sksl` — baseline
- `crt_core_20260426193000_catmullrom.sksl` — Catmull-Rom 4-tap cubic sampling
- `crt_core_20260426193627_mitchell.sksl` — Mitchell-Netravali 4-tap

`ShaderLoader.LoadLatest("crt_core_", ...)` automatically picks the newest timestamped version with older versions kept as fallback. Shaders can be hot-swapped without recompiling the emulator. NetFx has nothing comparable — CRT is hard-coded in C#.

---

## 8. Build and Toolchain

| Aspect | NetFx | aprnesava |
|---|---|---|
| Build command | VS2022 MSBuild (langversion 11 required) | `dotnet build` or `build_avalonia.bat` |
| Build time (Debug) | ~10s | ~4s |
| Build time (Release) | ~15s | ~5s |
| Output | `AprNes/bin/Debug/AprNes.exe` | `AprNesAvalonia/bin/Debug/net10.0/AprNesAvalonia.exe` |
| Embedded build timestamp | None | Yes (`SourceRevisionId` carries date+time, survives `copy/rename`) |
| Conditional compilation | Few | `CRT_SIMD_AVAILABLE`, `CRT_GPU_AVAILABLE`, `HD_NTSC` etc. |

---

## 9. When Should I Still Use NetFx?

aprnesava is better in nearly every measurable way. Two scenarios still favour NetFx:

1. **Target machine only has .NET Framework** — e.g. Windows 7 / 8 with 4.8.1 but no .NET 10. aprnesava cannot run there.
2. **CPU lacks AVX2 and there is no GPU** (very old Atom/Bobcat) — the GPU backend is unusable, and aprnesava can't claim its .NET 10 SIMD dividend, so NetFx and aprnesava-Scalar perform similarly.

For any modern PC (Windows 10+, AVX2 CPU, any GPU), aprnesava is the better choice.

---

## 10. What Is Identical Across the Two Editions

To avoid confusion — these are **the same on both** and never differ:

- CPU / PPU / APU / MEM / Mapper emulation accuracy
- Audio channel count + AudioPlus expansion chips (VRC6 / MMC5 / N163 / FME-7 / VRC7 / Sunsoft)
- 184/184 blargg + 138/138 AccuracyCoin v2 test results
- ROM loading, save RAM, save state, cheat logic
- Most input handling (both reuse `joystick.cs` / `DirectInputHelper.cs`)

---

## 11. Summary

aprnesava's advantages stack across four layers:

1. **Algorithmic precision** — HD_NTSC 12× Fsc oversampling (aprnesava only)
2. **Performance** — GPU CRT pipeline + .NET 10 JIT + zero-copy render (FPS doubled)
3. **User experience** — Avalonia Fluent UI + drag-and-drop + per-monitor HiDPI
4. **Extensibility** — platform abstraction + SkSL hot-swap + multi-backend dispatch

NetFx still runs and still passes every test, but new features will land on aprnesava only — NetFx is in maintenance freeze.
