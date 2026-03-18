# AprNesWasm — Blazor WebAssembly Version Overview

## Overview

AprNesWasm is a browser port of the AprNes NES emulator, built with **Blazor WebAssembly (.NET 10)**, allowing users to play NES ROMs directly in the browser without installing any software.

---

## Live Website

🌐 **https://erspicu.github.io/AprNes/**

- Hosting platform: GitHub Pages
- Branch: `gh-pages` (independent from the main code's `master` branch)
- Automatic deployment: run `deploy_wasm.bat` to update

---

## How to Use

1. Open https://erspicu.github.io/AprNes/
2. Click "Choose File" to load a `.nes` ROM file
3. Once the screen appears, click on the Canvas to give it keyboard focus
4. Start playing

### Keyboard Mapping

| Keyboard Key | NES Button |
|---------|---------|
| Z | A |
| X | B |
| Enter | Start |
| Shift | Select |
| ↑ ↓ ← → | D-Pad |

### Supported Mappers

| Mapper | Representative Games |
|--------|---------|
| 0 (NROM) | Donkey Kong, Mario Bros |
| 1 (MMC1) | Mega Man 2, Zelda |
| 2 (UxROM) | Mega Man, Castlevania |
| 3 (CNROM) | Arkanoid |
| 4 (MMC3) | Super Mario Bros 3, Contra |
| 7 (AxROM) | Battletoads |
| 11 | Color Dreams |
| 66 | Super Mario Bros + Duck Hunt |

---

## Running Locally for Development

```powershell
cd AprNesWasm
dotnet run
# Open http://localhost:5000 in the browser
```

Or with hot reload:
```powershell
dotnet watch
```

---

## Building and Deploying

### Build

```bat
build_wasm.bat
```

Output: `publish_wasm\wwwroot\`

### Deploy to GitHub Pages

```bat
deploy_wasm.bat
```

deploy_wasm.bat automatically:
1. Copies `publish_wasm\wwwroot\` to a temporary directory
2. Patches the `base href` in `index.html` to `/AprNes/` (GitHub Pages sub-path)
3. Adds `.nojekyll` (prevents Jekyll from ignoring the `_framework/` folder)
4. Adds `404.html` (SPA fallback)
5. `git push -f origin gh-pages` (force push, keeping gh-pages at the latest deployment only)

> **Note**: On first use, go to GitHub repo Settings → Pages → Branch and select `gh-pages`

---

## Technical Architecture

### Overall Architecture Diagram

```
JS requestAnimationFrame
        ↓
[JSInvokable] OnFrame()        ← Blazor C#
        ↓
NesCore.StepOneFrame()         ← Runs one frame of CPU+PPU+APU (synchronous, non-blocking)
        ↓
NesCore.GetScreenRgba()        ← ARGB uint* → RGBA byte[]
        ↓
nesInterop.drawFrame()         ← Canvas putImageData
        ↓
nesInterop.playAudio()         ← Web Audio API scheduled playback
```

### Key Techniques

| Item | Description |
|------|------|
| **No WASM multi-threading needed** | `LimitFPS=false` makes `ManualResetEvent.WaitOne()` return immediately, switching to step-based execution |
| **Partial class extension** | `NesFrameStep.cs` uses a partial class to directly access NesCore's private method `cpu_step()` |
| **Pixel format conversion** | NesCore outputs ARGB `uint` → `GetScreenRgba()` converts to the RGBA `byte[]` required by Canvas |
| **Audio collection** | `AudioSampleReady` events fire synchronously during `StepOneFrame()`, collected into a `List<short>` |
| **Web Audio scheduling** | Uses `AudioBufferSourceNode` for scheduled playback to prevent audio glitches |

### Important Settings

```xml
<!-- AprNesWasm.csproj -->
<BlazorEnableCompression>false</BlazorEnableCompression>
```

GitHub Pages does not support the `Content-Encoding` header for Brotli pre-compressed files,
so compression is disabled to serve all `.wasm` and `.js` files in their raw format.

---

## Known Limitations

| Limitation | Description |
|------|------|
| **Single ROM instance** | All NesCore state is `static`; only one ROM can run at a time |
| **No SRAM save** | localStorage access for SRAM is not currently supported |
| **1x resolution** | Display is fixed at 256×240 with no scaling option |
| **Player 1 keyboard only** | Gamepad API / Player 2 not supported |
| **iOS Safari** | Web Audio requires a user gesture to start; some versions may have compatibility issues |

---

## Related Files

| File | Description |
|------|------|
| `AprNesWasm/AprNesWasm.csproj` | Project settings, including NesCore source link |
| `AprNesWasm/NesFrameStep.cs` | NesCore WASM extensions (WasmInit / StepOneFrame / GetScreenRgba) |
| `AprNesWasm/Pages/Home.razor` | Main page (ROM loading, Canvas, keyboard, FPS) |
| `AprNesWasm/wwwroot/js/nesInterop.js` | JS-side Canvas rendering + Web Audio |
| `build_wasm.bat` | Build script |
| `deploy_wasm.bat` | GitHub Pages deployment script |
