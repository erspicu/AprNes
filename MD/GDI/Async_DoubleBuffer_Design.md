# Async Double Buffer 設計文件

> 2026-03-29

---

## 目標

將 `RenderScreen()` 中的 GDI blitting 從同步阻塞改為非同步，讓模擬執行緒不必等 GDI 完成就能開始下一幀的 PPU 模擬。預期消除 GUI 專屬的 8-15 ms/frame 開銷。

---

## 現有架構（同步模型）

```
Emu Thread                           UI Thread
─────────                            ─────────
PPU 模擬 (frame N)
  ↓ scanline 240, dot 1
Crt_Render() [8-15ms]
  ↓
VideoOutput.Invoke()  ──────→  VideoOutputDeal()
  │                              ├─ RenderObj.Render()  ← GDI SetDIBitsToDevice [5-10ms]
  │                              ├─ VideoRecorder.PushFrame()
  │                              └─ FPS limiting (Sleep/SpinWait)
  ↓                                   ↓
screen_lock = false                _event.Set()
emuWaiting = true                     │
_event.WaitOne() ◄────────────────────┘
emuWaiting = false
  ↓
PPU 模擬 (frame N+1)
```

**問題**：模擬執行緒在 `_event.WaitOne()` 阻塞，等 UI 執行緒完成 GDI blitting + FPS limiting 才繼續。GDI 傳輸時間（5-10ms @ 6x）完全浪費。

---

## 提案架構（Async Double Buffer）

### 核心概念

使用 **ping-pong buffer** — 兩份 AnalogScreenBuf（或 ScreenBuf），模擬執行緒寫入 buffer A 的同時，GDI 執行緒從 buffer B 讀取上一幀並顯示。

```
Emu Thread                           Render Thread (新)
─────────                            ──────────────────
PPU 模擬 (frame N) → 寫入 bufA
  ↓ scanline 240
Crt_Render() → bufA
  ↓
swap: bufA ↔ bufB (指標交換)
signal renderReady
  ↓                              等 renderReady
PPU 模擬 (frame N+1) → 寫入 bufB    ↓
  │ (不阻塞)                     GDI SetDIBitsToDevice(bufA)
  │                              FPS limiting
  │                              signal renderDone
  ↓                                   │
  ... (持續模擬)                      ...
```

### 時序對比

```
現有（同步）:
  |---PPU N---|--CRT--|--GDI--|--WAIT--|---PPU N+1---|
                                ^^^^^ 浪費

提案（非同步）:
  |---PPU N---|--CRT--|swap|---PPU N+1---|--CRT--|swap|
                       |--GDI--|              |--GDI--|
                       ^^^^^^^^ 重疊
```

---

## 需要修改的檔案和邏輯

### 1. PPU.cs — `RenderScreen()`

**現有**：
```csharp
static void RenderScreen()
{
    screen_lock = true;
    if (AnalogEnabled && UltraAnalog && CrtEnabled) Crt_Render();
    VideoOutput?.Invoke(null, null);  // 同步呼叫 GDI
    screen_lock = false;
    emuWaiting = true;
    _event.WaitOne();                 // 阻塞等 UI
    emuWaiting = false;
}
```

**改為**：
```csharp
static void RenderScreen()
{
    if (AnalogEnabled && UltraAnalog && CrtEnabled) Crt_Render();

    // 等上一幀 GDI 完成（如果還在跑）
    renderDoneEvent.WaitOne();

    // Swap buffers
    SwapAnalogBuffers(); // 指標交換 bufA ↔ bufB

    // 通知渲染執行緒開始 blit 上一幀（現在在 back buffer）
    renderReadyEvent.Set();

    // 不阻塞 — 直接繼續下一幀模擬
}
```

### 2. Main.cs — Buffer 分配

**新增**：
```csharp
static uint* AnalogScreenBufA;  // front buffer (模擬寫入)
static uint* AnalogScreenBufB;  // back buffer (GDI 讀取)

static void SwapAnalogBuffers()
{
    var tmp = AnalogScreenBufA;
    AnalogScreenBufA = AnalogScreenBufB;
    AnalogScreenBufB = tmp;
    AnalogScreenBuf = AnalogScreenBufA; // 模擬端永遠寫 A
}
```

### 3. AprNesUI.cs — 渲染執行緒

**新增獨立渲染執行緒**：
```csharp
Thread renderThread;
ManualResetEvent renderReadyEvent = new ManualResetEvent(false);
ManualResetEvent renderDoneEvent = new ManualResetEvent(true); // 初始已完成

void RenderThreadLoop()
{
    while (renderThreadRunning)
    {
        renderReadyEvent.WaitOne();  // 等模擬端通知
        renderReadyEvent.Reset();

        // GDI blit from back buffer
        RenderObj.Render();          // SetDIBitsToDevice(bufB)

        // FPS limiting
        if (LimitFPS) { ... }

        renderDoneEvent.Set();       // 通知模擬端完成
    }
}
```

### 4. NativeRendering.cs — 指標更新

`Render_Analog.Render()` 需要讀取 **back buffer** 的指標而非 front buffer。每次 swap 後更新 GDI 的 `data_ptr`。

### 5. CrtScreen.cs — 寫入目標

`Crt_Render()` 寫入 `AnalogScreenBuf`（= bufA = front buffer），不需改動。Swap 後 GDI 讀 bufB（上一幀的 front buffer）。

---

## 非 Analog 模式的處理

數位模式（AnalogEnabled=false）也可以受益：

- `ScreenBuf1x` 同樣可以做 ping-pong
- 但 256×240 = 245 KB，GDI 傳輸只要 ~0.5 ms，收益很小
- **建議先只對 Analog 模式實作**，數位模式保持現有同步

---

## 風險與注意事項

### 1. Buffer 一致性

| 風險 | 說明 | 對策 |
|------|------|------|
| 撕裂 | 模擬端寫 front buffer 時 GDI 還在讀 back buffer | swap 前必須等 `renderDoneEvent` |
| 指標失效 | `AnalogScreenBuf` 被 realloc（解析度切換） | realloc 時暫停渲染執行緒 |
| 競態 | `Crt_Render` 讀 `linearBuffer` + 寫 `AnalogScreenBuf` | linearBuffer 不需 double buffer（CRT 在 swap 前完成） |

### 2. 生命週期管理

- `VideoOutput +=/-=` 在多處使用（ROM 載入、reset、config 切換）
- 改為渲染執行緒後，這些地方需要改為控制 `renderThreadRunning` flag
- TestRunner（headless 模式）不啟動渲染執行緒

### 3. FPS Limiting

- 目前 FPS limiting 在 `VideoOutputDeal` 內，會在 UI 執行緒 Sleep
- 改為渲染執行緒後，FPS limiting 移到渲染執行緒
- 或改為在模擬端做（`RenderScreen` 內 swap 後 Sleep）

### 4. VideoRecorder

- `VideoRecorder.PushFrame()` 需要讀取完成的 frame buffer
- 改為在 swap 前 push（從 front buffer 讀取最新完成的幀）
- 或在渲染執行緒內 push（從 back buffer 讀取）

### 5. screen_lock 語意變更

- 現有：`screen_lock = true` 表示正在做 CRT + GDI
- 改為：可能需要兩個 lock（CRT lock + GDI lock）
- 或簡化為只用 event 同步，移除 screen_lock

---

## 預期收益

| 場景 | 現有 FPS | 預期 FPS | 提升 |
|------|---------|---------|------|
| 6x Analog+CRT+Audio (GUI) | ~55 | ~62-66 | +10-15% |
| 8x Analog+CRT+Audio (GUI) | ~45 | ~50-55 | +10-15% |
| 4x Analog+CRT+Audio (GUI) | ~70+ | ~80+ | 小幅 |
| 2x Analog+CRT+Audio (GUI) | ~85+ | ~88+ | 極小 |

最大受益者是 6x/8x — GDI 傳輸時間佔比最大的場景。

**注意**：如果 CRT 渲染本身就超過 16.67 ms（如 8x），async buffer 只能消除 GDI 開銷但不能讓 CRT 變快。

---

## 實作步驟

1. **建立分支** `feature/async-double-buffer`
2. **分配 ping-pong buffer** — Main.cs 新增 bufA/bufB，初始化時分配
3. **實作 SwapAnalogBuffers()** — 指標交換 + 更新 GDI data_ptr
4. **建立渲染執行緒** — AprNesUI.cs，用 ManualResetEvent 同步
5. **改寫 RenderScreen()** — 移除 `_event.WaitOne()`，改用 swap + signal
6. **處理 VideoRecorder** — 確保從正確的 buffer 讀取
7. **處理生命週期** — ROM 載入/reset/config 時正確暫停/恢復渲染執行緒
8. **TestRunner 相容** — headless 模式不啟動渲染執行緒
9. **測試** — 174/174 blargg + 136/136 AC + 目視確認無撕裂
10. **Benchmark** — 比較 async vs sync 在 2x/4x/6x/8x 的 FPS 差異

---

## 替代方案比較

| 方案 | 效果 | 複雜度 | 風險 |
|------|------|--------|------|
| **A: Async double buffer（本方案）** | 消除 GDI 阻塞 | 中 | buffer 同步、生命週期 |
| B: GDI → Direct2D | 消除 GDI + 硬體加速 blit | 高 | 需引入 DirectX 依賴 |
| C: GDI → OpenGL texture | 類似 B | 高 | 需引入 OpenGL 依賴 |
| D: BeginInvoke 非同步 | 最小改動 | 低 | 無法保證 blit 完成時機 |

本方案（A）在效果和複雜度之間取得平衡，不引入外部依賴，保持純 Win32 API。
