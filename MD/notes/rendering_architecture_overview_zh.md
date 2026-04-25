# AprNes 渲染架構總覽（NetFx + Avalonia 雙專案）

撰寫日期：2026-04-25
分支：`feature/rendering-refactor`（Phase A–C 已合併、Phase D 進行中）

本文整理 AprNes NetFx（WinForms / .NET Framework 4.8.1）與 aprnesava
（Avalonia / .NET 10）兩個前端目前的 rendering 結構與運作方式，並在最後
列出整理過程中發現的結構性問題、Avalonia 側可能的 side effect 確認結果。

---

## 1. 兩個前端共用的 NesCore 渲染契約

無論哪個前端，PPU 在 emu thread 上每幀的尾端都會執行
`PpuPhase_FrameRender`（在 scanline 240 cx=1 觸發）：

```
PpuPhase_FrameRender (emu thread)
├── 若 AnalogEnabled && renderThreadRunning: renderDone.Wait + Reset   ← Phase B 同步點
├── 若 AnalogEnabled: Ntsc_FlushPendingRows()                          ← 240 條 demod
├── 若 AnalogEnabled: Crt_SetFrameCount(frame_count)                   ← snapshot
├── 若 !AnalogEnabled: Convert_PalIdxFrameToRGB(digitalFrameRgb)       ← Phase C-3
├── RenderScreen()                                                    ← 分流點
├── frame_count++
└── 若 AnalogEnabled: Ntsc_SetFrameCount(frame_count)
```

`RenderScreen()` 是雙前端的分流點：

```csharp
static void RenderScreen()
{
    if (renderThreadRunning) {
        // 非同步：emu 只負責發訊號，渲染工作交由 render thread
        renderReady.Set();
        emuWaiting = true;
        _event.WaitOne();      // 等 UI 端 Set，避免 emu 跑得比顯示快
        emuWaiting = false;
    } else {
        // 同步 fallback：Avalonia / TestRunner 走這條路
        screen_lock = true;
        if (AnalogEnabled && UltraAnalog && CrtEnabled) Crt_Render();
        VideoOutput?.Invoke(null, null);   // 同步呼叫前端 callback
        screen_lock = false;
        emuWaiting = true;
        _event.WaitOne();
        emuWaiting = false;
    }
}
```

關鍵變數：

| 名稱 | 型別 | 寫入者 | 讀取者 | 用途 |
|---|---|---|---|---|
| `ntsc_rowPalettes` | `byte*` 256×240 | emu thread（PixelZone 每像素） | render thread（分析）/ emu thread（Convert） | 每幀調色盤 index buffer，永遠分配 |
| `digitalFrameRgb` | `uint*` 256×240 | emu thread（`Convert_PalIdxFrameToRGB`） | render thread（NetFx）/ emu thread（Avalonia OnVideoOutput） | 數位 RGB 轉換結果，永遠分配 |
| `linearBuffer` | `float*` 1024×240×3 | emu thread（`Ntsc_FlushPendingRows`） | render thread（CPU CRT）或 Avalonia render thread（GPU CRT） | NTSC demod 輸出（CRT 啟用時） |
| `AnalogScreenBuf` / `AnalogScreenBufBack` | `uint*` Crt_DstW×Crt_DstH | render thread（NetFx）或 emu thread（Avalonia 同步）寫入 | 顯示層讀取 | 類比模式最終顯示 buffer |
| `_event` | `ManualResetEvent` | UI thread（Reset/Set） | emu thread（Wait） | emu 端的暫停閘 |
| `renderReady` / `renderDone` | `ManualResetEventSlim` | emu/render | render/emu | NetFx 渲染執行緒同步 |
| `emuWaiting` | `volatile bool` | emu thread | UI/render thread | 確認 emu 已停在 `_event.WaitOne()` |
| `renderThreadRunning` | `volatile bool` | NetFx UI | RenderScreen / PpuPhase_FrameRender | 區分非同步 vs 同步流程 |

---

## 2. AprNes NetFx 渲染流程（Phase C-3 之後）

### 2.1 執行緒拓撲

```
┌────────────────┐   _event          ┌────────────────┐
│  emu thread    │ ───────────────── │  UI thread     │
│ (NesCore.run)  │                   │ (WinForms loop)│
└────┬───────────┘                   └────────────────┘
     │ renderReady.Set
     ▼
┌────────────────┐   GDI blit
│ render thread  │ ───────────────── (HWND DC)
│ (always alive) │
└────────────────┘
```

Render thread 在 ROM 啟動時 `StartRenderThread()` 開啟，整個 ROM session
都不會關掉；切換 digital ↔ analog 不會 `Stop/Start`，因此沒有舊版「兩條
render thread 互搶 GDI」的 deadlock 風險。

### 2.2 `RenderThreadLoop`（UI/AprNesUI.cs）

```
while (renderThreadRunning) {
    renderReady.Wait();  renderReady.Reset();
    bool analog = NesCore.AnalogEnabled;          // 在 loop 頂端 snapshot 模式
    if (analog) {
        if (UltraAnalog && CrtEnabled) Crt_Render();   // CPU CRT（Scalar/SIMD）
        SwapAnalogBuffers();
        NativeGDI.UpdateDataPtr(AnalogScreenBufBack);
        NativeGDI.DrawImageHighSpeedtoDevice();
    } else {
        RenderObj.Render();        // Render_resize.RenderFilter() + GDI blit
    }
    if (VideoRecorder.IsRecording) VideoRecorder.PushFrame(...);
    if (LimitFPS) FpsLimitSleep();
    renderDone.Set();
}
```

### 2.3 數位路徑（Render_resize）

emu thread：
1. PixelZone 每像素寫入 `ntsc_rowPalettes[scanline*256 + cx]`
2. 幀末 `Convert_PalIdxFrameToRGB(digitalFrameRgb)` 用 `NesColors[]` 把整張
   palette index frame 轉成 RGB
3. `RenderScreen` → `renderReady.Set()`，emu 進入 `_event.WaitOne()`

Render thread（`Render_resize.Render`）：
1. `RenderFilter()`：依 `_s1Filter` / `_s2Filter` 跑 xBRZ / Scalex / NN /
   Scanline pipeline；source 永遠是 `digitalFrameRgb`
2. 1× 無 filter 時：`_output = digitalFrameRgb`（aliasing），`_ownsOutput = false`
3. 其他情況：`_output` 是自己 alloc 的 buffer，`_ownsOutput = true`
4. `NativeGDI.DrawImageHighSpeedtoDevice()` 把 `_output` 內容 blit 上螢幕

### 2.4 類比路徑（Render_Analog）

emu thread：
1. PixelZone 寫入 `ntsc_rowPalettes`（與數位相同）
2. 幀末 `Ntsc_FlushPendingRows`（240 條 `Parallel.For` demod）→ `linearBuffer`
   或 `ntsc_analogScreenBuf`
3. `Crt_SetFrameCount(frame_count)` snapshot
4. `RenderScreen` → 訊號 render thread

Render thread：
1. `Crt_Render()`（如果是 Ultra+CRT）將 `linearBuffer` → `AnalogScreenBuf`
2. `SwapAnalogBuffers()`
3. `NativeGDI.UpdateDataPtr(AnalogScreenBufBack)` + `DrawImageHighSpeedtoDevice()`

### 2.5 模式切換（`PauseEmuAndRender` 模式）

`AprNesUI.cs` 提供統一的 quiesce 流程：

```csharp
public void PauseEmuAndRender() {
    if (!running) return;
    NesCore._event.Reset();
    while (!NesCore.emuWaiting) Thread.Sleep(1);   // 等 emu 停在 _event.WaitOne
    if (NesCore.renderThreadRunning)
        NesCore.renderDone.Wait();                 // 等當前 render frame 跑完
}
```

之後 UI 安全地：
- `RenderObj.freeMem()` → 切 `RenderObj` 物件
- `NesCore.AnalogEnabled = ...`
- `ConfigurePpuVisibleDispatch()` 重排 PixelZone 4 路 dispatch
- 必要時 alloc/free `AnalogScreenBuf*`
- `RenderObj.init(null, grfx)`
- `_event.Set()` 放 emu 走

PauseEmuAndRender 在 17xx-23xx 的多個 UI 路徑都被使用（Configure UI、
Analog Configure、AudioPlus Configure、Hard Reset 等）。

---

## 3. aprnesava (Avalonia) 渲染流程

### 3.1 執行緒拓撲

```
┌────────────────┐   _event          ┌────────────────┐
│  emu thread    │ ───────────────── │  UI thread     │
│ (NesCore.run)  │                   │ (Avalonia)     │
└────┬───────────┘                   └────────┬───────┘
     │ VideoOutput?.Invoke (sync)             │ Dispatcher.Post
     ▼ 同個 emu thread 跑 OnVideoOutput        ▼
┌──────────────────────────────────────────────────┐
│ Avalonia Render thread                           │
│ (EmuScreenControl.Render → SkSL canvas)          │
└──────────────────────────────────────────────────┘
```

Avalonia 沒有 NesCore 級別的 render thread（`renderThreadRunning ==
false`），完全靠 RenderScreen 同步 fallback：emu thread 進到
`VideoOutput?.Invoke()`，inline 跑 `OnVideoOutput`，回來才 `_event.WaitOne()`。

### 3.2 `OnVideoOutput`（EmulatorEngine.cs:497）

```csharp
private void OnVideoOutput(object? sender, EventArgs e) {
    int copyBytes = _outputW * _outputH * 4;
    lock (_resizeLock) {
        if (_analogMode && NesCore.AnalogScreenBuf != null) {
            Buffer.MemoryCopy(NesCore.AnalogScreenBuf, _backBuffer, copyBytes, copyBytes);
        } else {
            // Phase A4b: 數位永遠走 pipeline；1× 無 filter 時 OutputPtr = digitalFrameRgb
            if (!_pipeline.IsInitialized) _pipeline.Init(null);
            _pipeline.Process();
            Buffer.MemoryCopy(_pipeline.OutputPtr, _backBuffer, copyBytes, copyBytes);
        }
        _backBuffer = Interlocked.Exchange(ref _frontBuffer, _backBuffer);  // lock-free swap
        if (VideoRecorder.IsRecording) VideoRecorder.PushFrame(...);
    }
    Interlocked.Increment(ref _frameCounter);
    if (NesCore.LimitFPS) FpsLimitSleep();
    if (Interlocked.Exchange(ref _pendingFrame, 1) == 0)
        Avalonia.Threading.Dispatcher.UIThread.Post(FireFrameReady, DispatcherPriority.Render);
}
```

整個 callback 都跑在 emu thread 上。`_bufferA` / `_bufferB` 構成
double-buffer，`_frontBuffer` 由 `Interlocked.Exchange` 提供給 Avalonia
render thread 讀。

### 3.3 Avalonia render thread（EmuScreenControl.cs）

`EmuScreenControl.Render` 在 Avalonia 自己的 render thread 上執行：

```csharp
context.Custom(new EmuDrawOperation(Bounds, FrontBufferPtr, FrameWidth, FrameHeight));
```

`EmuDrawOperation.Render` 取得 `ISkiaSharpApiLeaseFeature.Lease()`：
- 若啟用 GPU CRT（`AnalogEnabled && CrtEnabled && CrtGpuRenderThreadActive`），
  跑 SkSL shader 直接讀 `linearBuffer` →（D3D11 GPU canvas）。**這條路
  跳過 emu thread 的 `Crt_Render`，emu side 的 `CrtScreen.Gpu.Render` 會
  no-op**。
- 否則 zero-copy `SKBitmap.InstallPixels(_, _frontBuffer, _w*4)` 然後
  `DrawBitmap`。

### 3.4 模式切換（EmulatorEngine.ApplyRenderSettings）

```csharp
if (_running) {
    NesCore.VideoOutput -= OnVideoOutput;
    NesCore._event.Reset();
    while (!NesCore.emuWaiting) Thread.Sleep(1);
}

_analogMode = analogEnabled;
NesCore.ConfigurePpuVisibleDispatch();

if (analogEnabled) {
    // 分配 / 重新分配 AnalogScreenBuf*
    NesCore.SyncAnalogConfig();
    NesCore.Ntsc_Init();
    NesCore.Crt_Init();
} else {
    _pipeline.Configure(s1Filter, s1Scale, s2Filter, s2Scale, scanline);
    // 釋放 AnalogScreenBuf*
}

// 重新分配 _bufferA / _bufferB（雙緩衝大小可能變了）
_pipeline.Init(null);                          // FreeMem 內建 _ownsOutput 守護
NesCore.RenderOutputPtr = _pipeline.OutputPtr; // VideoRecorder 用

if (wasAttached) {
    NesCore.VideoOutput += OnVideoOutput;
    NesCore._event.Set();
}
```

與 NetFx 的 `PauseEmuAndRender` 對應，但少了 `renderDone.Wait`（因為沒有
NesCore render thread 可等）。

### 3.5 GPU CRT 額外通道（CrtGpuRenderThread）

```
emu thread:    Ntsc_FlushPendingRows → linearBuffer
Avalonia RT:   每次 Render → 讀 linearBuffer → SkSL → GPU canvas
```

兩條 thread 對 `linearBuffer` 沒有 lock，CrtGpuRenderThread.cs 開頭明確寫：
"Minor tearing possible without sync; acceptable for Phase 3A MVP"。

---

## 4. NetFx vs Avalonia 對照表

| 面向 | NetFx | aprnesava |
|---|---|---|
| 顯示後端 | Win32 GDI（`NativeGDI`） | Skia（Avalonia compositor）+ SkSL GPU |
| Render thread | NesCore 自管，`StartRenderThread`，always-running | 借用 Avalonia render thread；NesCore 端 `renderThreadRunning=false` |
| RenderScreen 路徑 | 非同步（renderReady/renderDone） | 同步 fallback（VideoOutput?.Invoke） |
| `Convert_PalIdxFrameToRGB` 何時跑 | emu thread（在 RenderScreen 之前） | emu thread（同前；同步路徑） |
| 數位濾鏡何時跑 | render thread（`Render_resize.Render`） | emu thread（`OnVideoOutput` 內 `_pipeline.Process()`） |
| `Crt_Render`（CPU 後端）何時跑 | render thread（Phase B） | emu thread（同步 fallback 內） |
| `Crt_Render`（GPU 後端）何時跑 | 不適用 | Avalonia render thread（SkSL on D3D11） |
| Double buffer | `AnalogScreenBuf`/Back（NesCore 內部） | `_bufferA`/`_bufferB`（EmulatorEngine 內部，每次 swap 都 alloc-free 不變） |
| 模式切換 quiesce | `PauseEmuAndRender` 統一 helper（含 renderDone 等） | `ApplyRenderSettings` 內聯 spin loop |
| FPS 限制位置 | render thread loop 末端 | `OnVideoOutput` 末端（emu thread） |
| 1× 無 filter alias | `Render_resize._output = digitalFrameRgb` (`_ownsOutput=false`) | `RenderPipeline._output = digitalFrameRgb` (`_ownsOutput=false`) |
| ROM 啟動畫面初始化 | `RenderObj.init(null, grfx)` | `_pipeline.Init(null)` |

---

## 5. 結構性問題盤點

整理過程中發現的結構性問題依嚴重度排序：

### 5.1 Avalonia 沒有 Phase B 等價（**值得注意，但不是 bug**）

NetFx 的 Phase B 把 `Crt_Render` 與數位濾鏡 pipeline 移到 render thread，
讓 emu thread 不被 ~16 ms 的 CRT/濾鏡工作拖慢。

aprnesava 完全沒有這個拆分：

- 數位 1× 無 filter：`OnVideoOutput` 只有一個 `Buffer.MemoryCopy`，影響微小
- 數位 + xBRZ 4×：xBRZ 4× 在 emu thread 跑，emu→swap→送 Avalonia。emu
  幀預算被吃掉的份量等於濾鏡成本
- 類比 + Ultra + CPU CRT：`Crt_Render` 在 emu thread 跑（同步 fallback
  那一路），emu 一直被 CRT 阻塞
- 類比 + Ultra + GPU CRT：CRT 在 Avalonia render thread 上跑，emu thread
  幾乎沒做事 ✓

**建議**：若要把 Avalonia 的數位濾鏡 / CPU CRT 從 emu thread 拉出去，
最自然的作法不是抄 NetFx 的 NesCore render thread，而是讓
`OnVideoOutput` 只 swap pointer，把 `_pipeline.Process` / CPU CRT
搬到 Avalonia render thread 的 `EmuDrawOperation.Render` 裡頭。但這是
Phase E 等級的工作，目前沒急迫性。

### 5.2 Avalonia 模式切換時 pipeline 仍持有舊 digital config（**輕微記憶體浪費**）

`ApplyRenderSettings` 在 `if (analogEnabled)` 分支裡**沒有**呼叫
`_pipeline.Configure(None, 1, None, 1, false)` 把 pipeline 還原成
no-filter，僅把 `_pipelineActive = false`。然後 line 253 仍然執行
`_pipeline.Init(null)`。

結果：
- 若使用者在 digital 模式時設 xBRZ 4×（pipeline 內部分配 ~4 MB
  `_output`），切到 analog 時 pipeline 還是用上次的 4× 配置重新 alloc
  4 MB，但這 4 MB 在 analog 模式整個都不會被讀（`_analogMode = true` 時
  `OnVideoOutput` 走 AnalogScreenBuf 那一路）
- 不是 leak（下次再切回 digital，Configure 改成 None 後 Init 會 free），
  但是**短暫的 ~4 MB 浪費 + 一次無謂的 alloc/free**

**建議修法**：在 analog 分支裡呼叫 `_pipeline.Configure(ResizeFilter.None, 1, ResizeFilter.None, 1, false)`，讓 pipeline 重新 Init 時走 1× alias 路徑（`_output = digitalFrameRgb`、`_ownsOutput = false`、零分配）。

### 5.3 `_analogMode` (Avalonia) 與 `NesCore.AnalogEnabled` 必須同步（**潛在不變式風險**）

- `NesCore.AnalogEnabled` 控制 emu thread 寫哪個 buffer
- `EmulatorEngine._analogMode` 控制 `OnVideoOutput` 讀哪個 buffer

兩者由 `ApplyRenderSettings` 同時設定（caller 在呼叫前先設
`NesCore.AnalogEnabled`，方法內第一行設 `_analogMode = analogEnabled`）。
但這個契約只在 `ApplyRenderSettings` 一個地方維護，沒有測試或 assert
保護。若有人改其他路徑（例如 hot-toggle UI 直接改
`NesCore.AnalogEnabled` 而沒呼叫 ApplyRenderSettings），`_analogMode`
就會 desync，OnVideoOutput 會抓錯 buffer。

**建議**：把 `_analogMode` 改成 `private bool _analogMode => NesCore.AnalogEnabled;`，去掉一份狀態。

### 5.4 重複的 quiesce spin loop（**程式碼重複**）

NetFx 的 `PauseEmuAndRender` 在 `AprNesUI.cs` 與 Avalonia 的 spin
loop 在 `EmulatorEngine.ApplyRenderSettings`、`AnalogConfigWindow`、
`AudioPlusConfigWindow` 都重複了一次。

每處的邏輯都是「Reset event → spin until emuWaiting」。差異只在 NetFx
還要等 `renderDone`。

**建議**：把這個 helper 放到 NesCore 內，讓兩個前端共享：
```csharp
public static void PauseEmuToWait() {
    _event.Reset();
    while (!emuWaiting && !exit) Thread.Sleep(1);
    // renderDone.Wait() 只在 renderThreadRunning 時有意義
    if (renderThreadRunning) renderDone.Wait();
}
```

### 5.5 `_outputW/_outputH` 與 `Crt_DstW/H` 在 Avalonia 必須一致（**契約風險**）

`ApplyRenderSettings` line 180-181：
```csharp
newW = 256 * analogSize;
newH = 210 * analogSize;
```

但 NesCore 內部 `Crt_DstW`/`Crt_DstH` 是由 `SyncAnalogConfig` 與
`Crt_Init` 計算的，可能與 `256 * analogSize / 210 * analogSize` 不完全
一致（特別是 CRT 有非 4:3 的 aspect 邏輯時）。

`OnVideoOutput` 用 `_outputW * _outputH * 4` 算 `copyBytes`，但 source
是 `NesCore.AnalogScreenBuf`（大小 = `Crt_DstW * Crt_DstH`）。若兩者
不符，可能讀過頭或顯示錯位。

**建議**：analog 分支直接用 `NesCore.Crt_DstW` / `NesCore.Crt_DstH`
做為 `newW/newH`，不要再自己算一次。

### 5.6 `Render_resize` 與 `RenderPipeline` 仍是兩份近似程式碼（**已知重複**）

兩者邏輯幾乎相同（同樣 `_stage1Buf`、`_output`、`_ownsOutput`、同樣
4 種 filter、同樣 1× alias），差別只在 NetFx 版多一個 GDI init
(`NativeGDI.initHighSpeed`) 與 `GetOutput() => Bitmap`。

Phase D 只做了 dead field 清理，沒嘗試合併。

**建議**：把 filter pipeline 抽到共用檔（例如 `AprNes/tool/FilterPipeline.cs`），兩邊各自 wrap 一個薄的 platform-specific shell。延後到 Phase E。

### 5.7 GPU CRT 與 emu thread 對 `linearBuffer` 沒有同步（**已記錄的設計取捨**）

`CrtGpuRenderThread.cs` 自己的註解寫了 "Minor tearing possible
without sync; acceptable for Phase 3A MVP"。Avalonia render thread 在
任意時間點讀 `linearBuffer`，emu thread 可能正在覆寫它（下一幀的
`Ntsc_FlushPendingRows`）。

肉眼幾乎看不出來；要根治需要為 `linearBuffer` 也做雙緩衝。**目前是
有意識的取捨，不算結構問題。**

---

## 6. Avalonia side-effect 確認（針對 Phase D 的清理）

Phase D 清理包含：

1. `Render_resize.cs`：刪 `_input` / `_rgbInput`，簡化 `GetOutput()`
2. `RenderPipeline.cs`（Avalonia）：刪 `_input` / `_rgbInput`，簡化 `OutputPtr`

逐項確認 Avalonia side-effect：

### 6.1 `RenderPipeline._input` 刪除
- 唯一讀取點是 `OutputPtr => _output != null ? _output : _input`，已改成
  `OutputPtr => _output`
- `_output` 是否一定非 null？`Init` 結束時，either 自己 alloc 一塊，
  either 指向 `digitalFrameRgb`（NesCore 在 `init()`/`initFDS()` 永遠
  分配）。`FreeMem` 把 `_output` 設回 null，但 `OutputPtr` 在 ROM 載入
  前不會被讀（`EmulatorEngine` line 258 的呼叫在 `_pipeline.Init(null)`
  之後）
- 結論：✓ 不會踩到 null

### 6.2 `RenderPipeline._rgbInput` 刪除
- 從未被寫入（搜尋整個 Avalonia 專案 0 次寫入），只有 `FreeMem` 試圖
  free 它，等於 dead code
- 結論：✓ 純無害

### 6.3 `Init(uint* input)` 簽名保留但 `input` 不被儲存
- 所有呼叫者（`EmulatorEngine.cs:253, 418, 515`）都已經傳 `null`
  （Phase A4b 之後 pipeline 的 source 改為 `NesCore.digitalFrameRgb`）
- 結論：✓ 沒有任何呼叫者依賴 `_input` 被填入

### 6.4 `_ownsOutput` 守護未被破壞
- `FreeMem` 仍然只在 `_output != null && _ownsOutput` 時 free
- `Init` 仍然在 1× 無 filter 時 set `_ownsOutput = false`
- 結論：✓ Phase C-3 修復原意保留

### 6.5 編譯驗證
- `dotnet build AprNesAvalonia.csproj -c Debug` ✓ 0 errors，warning 數
  與清理前相同（既有 8 個無關 warning）
- `MSBuild AprNes.csproj` ✓ 0 errors

### 6.6 Avalonia 模式切換實機路徑檢查
- digital → analog：`_pipeline.Init(null)` → `FreeMem`（`_output =
  digitalFrameRgb`、`_ownsOutput = false`，正確不 free）→ analog 分支
  不再 alloc `_output`（因為 stale Configure 仍然有 filter，所以實際
  上會 alloc，但這是上面 5.2 的問題，不是這次清理導致）
- analog → digital：`_pipeline.Configure(...)` 設新 filter →
  `_pipeline.Init(null)` → `FreeMem`（這次 `_ownsOutput` 是上次數位
  時設的值，可能 true 或 false，都正確處理）
- 結論：✓ 與 NetFx 同樣安全

**整體結論**：本次 Phase D 的 dead code 清理沒有對 Avalonia 引入 side
effect。真正影響 Avalonia 使用者體驗的結構問題列在第 5 節，獨立於本次
清理之外。

---

## 7. 後續建議優先級

| 項目 | 嚴重度 | 工作量 | 何時做 |
|---|---|---|---|
| 5.2 模式切換時 Configure 還原 | 低 | 1 行 | 隨手修 |
| 5.3 `_analogMode` 改 property | 低 | ~10 行 | 隨手修 |
| 5.5 `_outputW/H` 用 `Crt_DstW/H` | 中 | ~10 行 | 下次碰到 analog scale issue 時 |
| 5.4 共用 quiesce helper | 中 | ~30 行 | 需要碰到第三個前端時 |
| 5.1 Avalonia render thread 拆分 | 中（效能） | 多日 | 若 Avalonia 數位 + 重 filter 顯示卡頓再說 |
| 5.6 合併 `Render_resize` / `RenderPipeline` | 低 | 多日 | 需求出現時 |
