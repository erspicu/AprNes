# Async Double Buffer 死鎖除錯實戰紀錄

> 2026-03-29

---

## 背景

為了在類比模式（Analog + Ultra Analog + CRT）下消除 GDI `SetDIBitsToDevice` 的同步阻塞（5-10ms/frame @ 6x），實作了 ping-pong double buffer + 獨立渲染執行緒。

原始同步模型：
```
Emu Thread: PPU模擬 → CRT渲染 → VideoOutput.Invoke() → GDI blit → _event.WaitOne() 阻塞
```

改為非同步模型：
```
Emu Thread:    PPU模擬 → CRT渲染 → wait(renderDone) → swap → signal(renderReady) → 繼續模擬
Render Thread: wait(renderReady) → GDI blit → FPS limiting → signal(renderDone)
```

效能提升顯著（6x/8x 均達 60 FPS），但 **切換解析度時程式卡死**。以下記錄三個死鎖的發現與修復過程。

---

## 死鎖 #1：AnalogSize 提前變更導致 CRT 陣列越界

### 症狀

ConfigureUI 中切換 6x ↔ 8x，程式卡住無回應。

### 根因

ConfigureUI 的 `BeforClose()` 中，`NesCore.AnalogSize` 在呼叫 `ApplyRenderSettings()` 之前就被改掉了：

```csharp
// AprNes_ConfigureUI.cs BeforClose()
NesCore.AnalogSize = 6;          // ← 立刻改！模擬端仍在跑 8x
// ...（中間一堆 config 寫入）
initUIsize();                     // ← 卡在這
ApplyRenderSettings();            // ← 從未到達
```

此時模擬端的 async path 呼叫 `SwapAnalogBuffers()` → `SyncAnalogConfig()`，把新的 6x size 同步給 CRT 模組。但 CRT 的 weight tables（`_weights[]`, `_nearestY[]`）仍是 8x 尺寸。

下一幀 `Crt_Render()` 用 6x 的 `Crt_DstW/Crt_DstH` 存取 8x 的 weight tables → **陣列越界，Parallel.For 內部死循環或 crash**。

### 教訓

> **跨執行緒共享的設定參數，改值與重建必須是原子操作。**
>
> 如果一個參數（如 AnalogSize）同時被「UI thread 寫入」和「模擬 thread 讀取」，
> 不能只改值而不重建依賴該值的資料結構。

### 修法

1. **`SwapAnalogBuffers()` 不呼叫 `SyncAnalogConfig()`**：只交換 buffer 指標，用專門的 `Crt_UpdateScreenBuf()` / `Ntsc_UpdateScreenBuf()` 只更新指標，不碰 analogSize。

2. **Sync fallback path 跳過 CRT render**：當渲染執行緒已停止，模擬端進入 sync fallback 時，完全不做 CRT render（該幀不會被顯示，也避免不一致的 CRT 狀態導致問題）。

```csharp
// SwapAnalogBuffers：只動指標，不改設定
static public void SwapAnalogBuffers()
{
    var tmp = AnalogScreenBuf;
    AnalogScreenBuf = AnalogScreenBufBack;
    AnalogScreenBufBack = tmp;
    Ntsc_UpdateScreenBuf(AnalogScreenBuf);  // 只更新指標
    Crt_UpdateScreenBuf(AnalogScreenBuf);   // 只更新指標
}
```

---

## 死鎖 #2：GDI HDC 跨執行緒競爭

### 症狀

同上 — 切換解析度時卡死。即使修了死鎖 #1，問題仍然存在。

### 根因

渲染執行緒持續呼叫 `SetDIBitsToDevice(hdcDest, ...)`，其中 `hdcDest` 是 UI panel 的 GDI device context。

當 ConfigureUI dialog 關閉時，Windows Forms 需要 repaint 主視窗（dialog 遮蔽的區域）。UI thread 的 message pump 嘗試處理 `WM_PAINT`，需要存取 panel 的 DC。但渲染執行緒正在同一個 DC 上做 `SetDIBitsToDevice`。

**GDI 的 device context 不是 thread-safe 的。** 兩個執行緒同時存取同一個 HDC，Windows GDI 內部鎖導致死鎖。

```
Render Thread: SetDIBitsToDevice(hdcDest, ...)  ← 持有 GDI 內部鎖
UI Thread:     WM_PAINT → BeginPaint(panel)     ← 等 GDI 鎖 → 死鎖
```

### 教訓

> **GDI HDC 不是 thread-safe 的。**
>
> 即使 `SetDIBitsToDevice` 可以從任意執行緒呼叫（技術上不會 crash），
> 當另一個執行緒（通常是 UI thread）需要存取同一個 HDC 時，
> Windows GDI 的內部序列化機制可能導致死鎖。
>
> 特別危險的情境：
> - Modal dialog 關閉 → WM_PAINT
> - 控件 resize（`panel.Width = ...`）→ WM_SIZE → WM_PAINT
> - `Graphics.Dispose()` / `CreateGraphics()` → DC 操作
>
> 如果必須從非 UI 執行緒做 GDI blit，必須在任何 UI 操作前先停止該執行緒。

### 修法

在 `initUIsize()` 開頭加入 `StopAnalogRenderThread()`，確保渲染執行緒已停止後才操作 panel 和 Graphics。

```csharp
public void initUIsize()
{
    StopAnalogRenderThread();  // ← 先停！
    // ... panel resize, grfx dispose/recreate ...
}
```

但這還不夠 — 見死鎖 #3。

---

## 死鎖 #3：Async 模式下 `emuWaiting` 永遠不會被設定

### 症狀

加了上述兩個修法後，仍然卡死。加入 debug log 追蹤：

```
[UI] StartAnalogRenderThread        ← 唯一一條 UI log
[EMU] async path: CRT render start
[RENDER] GDI blit done
[EMU] async path: CRT render start  ← 持續正常運行
...                                  ← 永遠沒有 StopAnalogRenderThread log
```

**`StopAnalogRenderThread` 從未被呼叫！** 程式卡在更早的地方。

### 根因

`BeforClose()` 最開頭就有暫停模擬端的邏輯：

```csharp
void BeforClose()
{
    // ...
    NesCore._event.Reset();
    while (!NesCore.emuWaiting) Thread.Sleep(1);  // ← 永遠等不到！
    // ...（後面的 initUIsize / ApplyRenderSettings 從未到達）
}
```

在原始同步模型中，模擬端每幀都會：
```csharp
emuWaiting = true;
_event.WaitOne();    // 阻塞
emuWaiting = false;
```

但在 async 模式下，模擬端走的是 async path：
```csharp
analogRenderDone.Wait();
SwapAnalogBuffers();
analogRenderReady.Set();
// 不阻塞，不設 emuWaiting，直接繼續下一幀
```

`_event.Reset()` 對 async path 毫無影響。`emuWaiting` 永遠是 `false`。UI thread 的 `while (!emuWaiting)` 無限迴圈。

### 教訓

> **引入新的同步機制時，必須盤點所有依賴舊機制的程式碼。**
>
> 原始設計中 `_event` + `emuWaiting` 是唯一的 frame sync 機制。
> 新增 async path 後，這套機制被繞過了，但散布在各處的
> `_event.Reset()` + `while (!emuWaiting)` 暫停邏輯並不知道。
>
> 每一個「暫停模擬端」的呼叫點都需要先回到同步模式：
> 1. 停止渲染執行緒（`StopAnalogRenderThread`）
> 2. 模擬端下一幀自動走 sync fallback → 設 `emuWaiting` → 阻塞
> 3. UI 才能安全等待 `emuWaiting`

### 修法

在 `BeforClose()` 最前面呼叫 `StopAnalogRenderThread()`：

```csharp
void BeforClose()
{
    AprNesUI.GetInstance().StopAnalogRenderThread();  // ← 先停渲染執行緒！
    // ...
    NesCore._event.Reset();                           // 現在有效：模擬端會走 sync fallback
    while (!NesCore.emuWaiting) Thread.Sleep(1);      // 現在能等到
}
```

---

## 完整修復清單

### 需要 `StopAnalogRenderThread()` 的所有位置

| 位置 | 原因 |
|------|------|
| `AprNes_ConfigureUI.BeforClose()` 最開頭 | 最早的暫停點，在任何設定變更之前 |
| `AprNesUI.initUIsize()` 開頭 | panel resize / Graphics recreate 前 |
| `AprNesUI.ApplyRenderSettings()` 開頭 | buffer 重新分配前 |
| `AprNesUI.Reset()` | SoftReset 前 |
| `AprNesUI.HardReset()` | 完整重初始化前 |
| `AprNesUI.NESCaptureScreen()` | 截圖讀 buffer 前 |
| `AprNesUI._ultraAnalogMenuItem_Click()` | Ultra Analog 切換前 |
| `AprNesUI.EnterAnalogFullScreen()` | 全螢幕 buffer 重配前 |
| `AprNesUI.ExitAnalogFullScreen()` | 退出全螢幕 buffer 重配前 |
| `AprNesUI.AprNesUI_FormClosing()` | 程式關閉前 |
| 載入新 ROM 時（`LoadRomFromPath` 內停舊遊戲） | 停止舊的渲染執行緒 |

### 需要 `StartAnalogRenderThread()` 的所有位置

| 位置 | 條件 |
|------|------|
| `ApplyRenderSettings()` 尾部 | `if (AnalogEnabled)` |
| `Reset()` 尾部 | `if (AnalogEnabled)` |
| `HardReset()` 尾部 | `if (AnalogEnabled)` |
| `LoadRomFromPath()` emu start 前 | `if (AnalogEnabled)` |
| `EnterAnalogFullScreen()` 恢復模擬時 | `if (AnalogEnabled)` |
| `ExitAnalogFullScreen()` 恢復模擬時 | `if (AnalogEnabled)` |

---

## 除錯方法論

### 1. Log 比猜測有效 100 倍

三個死鎖中，前兩個靠推理判斷（但判斷錯誤 — 以為問題在 ApplyRenderSettings），
第三個靠 log 一秒定位（log 顯示 `[UI] StopAnalogRenderThread` 從未出現 → 問題在更早的地方）。

**建議**：在所有同步點（Wait/Set/Reset）加 log，包含：
- 時間戳（毫秒）
- 執行緒名稱/ID
- 事件名稱

```csharp
DbgLog($"[{threadName}] waiting {eventName}...");
event.Wait();
DbgLog($"[{threadName}] {eventName} received");
```

### 2. 「最後一條 log」就是死鎖點

如果 log 停在：
```
[RENDER] GDI blit done
```
而沒有後續的 `[RENDER] analogRenderDone.Set()`，問題就在這兩行之間。

但本案例更微妙：**log 完全正常，沒有異常**。async 循環持續運行，但 `[UI]` 的 log 從未出現。這說明問題在 UI thread，而且在我們的 log 點之前。

### 3. 盤點所有「暫停模擬端」的呼叫點

在引入新的 frame sync 機制後，搜尋所有使用舊機制的地方：

```
grep -n "emuWaiting\|_event\.Reset\|_event\.Set" *.cs
```

每一個 `while (!emuWaiting)` 都是潛在的死鎖點。

---

## 設計原則總結

1. **跨執行緒共享的 GDI HDC**：在任何 UI 操作（resize、dispose、recreate）前，必須先停止使用該 HDC 的所有非 UI 執行緒。

2. **參數變更與資料結構重建必須原子化**：`SwapBuffers` 只動指標，不同步可能已變更的設定參數（如 AnalogSize）。完整的設定同步（`SyncAnalogConfig` + `Crt_Init`）只在模擬端安全暫停後由 UI thread 執行。

3. **新增同步路徑時，必須提供從新路徑回退到舊路徑的機制**：async path 的模擬端透過檢查 `analogRenderThreadRunning` flag 自動 fallback 到 sync path。`StopAnalogRenderThread` 設定 flag 後，模擬端下次 `RenderScreen()` 就會走 sync fallback 並正確設定 `emuWaiting`。

4. **`StopAnalogRenderThread` 是唯一安全的過渡入口**：它內部依序處理 `_event.Reset()` → `analogRenderThreadRunning=false` → 喚醒渲染執行緒 → 等待退出 → 喚醒模擬端。所有需要暫停模擬端的地方，都必須先呼叫此方法。
