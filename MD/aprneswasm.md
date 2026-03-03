# AprNesWasm — Blazor WebAssembly 版本說明

## 概述

AprNesWasm 是 AprNes NES 模擬器的瀏覽器移植版本，使用 **Blazor WebAssembly (.NET 10)** 技術，讓使用者無需安裝任何程式即可直接在瀏覽器中遊玩 NES ROM。

---

## 線上網站

🌐 **https://erspicu.github.io/AprNes/**

- 部署平台：GitHub Pages
- 分支：`gh-pages`（獨立於主程式碼的 `master` 分支）
- 自動部署：執行 `deploy_wasm.bat` 即可更新

---

## 使用方式

1. 開啟 https://erspicu.github.io/AprNes/
2. 點選「選擇檔案」，載入 `.nes` ROM 檔案
3. 畫面出現後，點一下 Canvas 讓鍵盤取得焦點
4. 開始遊玩

### 鍵盤對應

| 鍵盤按鍵 | NES 按鈕 |
|---------|---------|
| Z | A |
| X | B |
| Enter | Start |
| Shift | Select |
| ↑ ↓ ← → | 方向鍵 |

### 支援 Mapper

| Mapper | 代表遊戲 |
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

## 本地開發執行

```powershell
cd AprNesWasm
dotnet run
# 瀏覽器開啟 http://localhost:5000
```

或熱重載模式：
```powershell
dotnet watch
```

---

## 建置與部署

### 建置

```bat
build_wasm.bat
```

輸出：`publish_wasm\wwwroot\`

### 部署到 GitHub Pages

```bat
deploy_wasm.bat
```

deploy_wasm.bat 自動執行：
1. 複製 `publish_wasm\wwwroot\` 到暫存目錄
2. Patch `index.html` 的 `base href` 為 `/AprNes/`（GitHub Pages 子路徑）
3. 新增 `.nojekyll`（防止 Jekyll 忽略 `_framework/` 資料夾）
4. 新增 `404.html`（SPA fallback）
5. `git push -f origin gh-pages`（強推，保持 gh-pages 只有最新部署）

> **注意**：第一次使用需在 GitHub repo Settings → Pages → Branch 選 `gh-pages`

---

## 技術架構

### 整體架構圖

```
JS requestAnimationFrame
        ↓
[JSInvokable] OnFrame()        ← Blazor C#
        ↓
NesCore.StepOneFrame()         ← 跑一幀 CPU+PPU+APU（同步，不 block）
        ↓
NesCore.GetScreenRgba()        ← ARGB uint* → RGBA byte[]
        ↓
nesInterop.drawFrame()         ← Canvas putImageData
        ↓
nesInterop.playAudio()         ← Web Audio API 排程播放
```

### 核心技巧

| 項目 | 說明 |
|------|------|
| **不需要 WASM 多執行緒** | `LimitFPS=false` 讓 `ManualResetEvent.WaitOne()` 立即返回，改為 step-based |
| **Partial class 擴充** | `NesFrameStep.cs` 用 partial class 直接存取 NesCore 私有方法 `cpu_step()` |
| **像素格式轉換** | NesCore 輸出 ARGB `uint` → `GetScreenRgba()` 轉成 Canvas 需要的 RGBA `byte[]` |
| **音效收集** | `AudioSampleReady` 事件在 `StepOneFrame()` 期間同步觸發，收集到 List\<short\> |
| **Web Audio 排程** | 使用 `AudioBufferSourceNode` 排程播放，避免音效中斷 |

### 重要設定

```xml
<!-- AprNesWasm.csproj -->
<BlazorEnableCompression>false</BlazorEnableCompression>
```

GitHub Pages 不支援 Brotli pre-compressed 檔案的 `Content-Encoding` header，
停用壓縮讓所有 `.wasm` 和 `.js` 以原始格式傳輸。

---

## 已知限制

| 限制 | 說明 |
|------|------|
| **單一 ROM 實例** | NesCore 所有狀態為 `static`，同一時間只能跑一個 ROM |
| **無 SRAM 存檔** | 目前不支援 localStorage 存取 SRAM |
| **1x 解析度** | 畫面固定 256×240，無縮放選項 |
| **僅 P1 鍵盤** | 不支援 Gamepad API / P2 |
| **iOS Safari** | Web Audio 需要使用者手勢才能啟動，部分版本可能有相容問題 |

---

## 相關檔案

| 檔案 | 說明 |
|------|------|
| `AprNesWasm/AprNesWasm.csproj` | 專案設定，含 NesCore source link |
| `AprNesWasm/NesFrameStep.cs` | NesCore WASM 擴充（WasmInit / StepOneFrame / GetScreenRgba） |
| `AprNesWasm/Pages/Home.razor` | 主頁面（ROM 載入、Canvas、鍵盤、FPS） |
| `AprNesWasm/wwwroot/js/nesInterop.js` | JS 端 Canvas 渲染 + Web Audio |
| `build_wasm.bat` | 建置腳本 |
| `deploy_wasm.bat` | GitHub Pages 部署腳本 |
