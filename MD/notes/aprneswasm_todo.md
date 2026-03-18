# AprNesWasm 效能優化 TODO

> 目前 FPS 偏低（10~30）原因：C# 程式碼跑在 WASM IL 直譯器，加上 JS↔WASM 序列化開銷。
> 以下兩個改善方向可獨立進行，建議順序：先做 TODO-1，確認效果後再考慮 TODO-2。

---

## TODO-1：啟用 WASM AOT 編譯 ⭐ 優先

**預期效果：FPS 從 10~30 提升到 60~150**

### 步驟

**Step 1：安裝 wasm-tools workload（需管理員，只需做一次）**

```bat
:: 以系統管理員身份開啟 CMD
dotnet workload install wasm-tools
```

**Step 2：開啟 `AprNesWasm/AprNesWasm.csproj`，取消這行的註解**

```xml
<!-- 找到這行，把 <!-- --> 移除 -->
<!-- <RunAOTCompilation>true</RunAOTCompilation> -->

<!-- 改成 -->
<RunAOTCompilation>true</RunAOTCompilation>
```

**Step 3：重新 build + deploy**

```bat
build_wasm.bat      ← 這次 build 會花 3~5 分鐘（AOT 編譯時間）
deploy_wasm.bat
```

### 注意事項

| 項目 | 說明 |
|------|------|
| Build 時間 | 增加 3~5 分鐘（只在 publish 時，開發 `dotnet run` 不受影響） |
| 檔案大小 | `publish_wasm\wwwroot` 會增大（`_framework/` 內多出 `.wasm` 預編譯檔） |
| 首次載入 | 瀏覽器下載量增加（可能增加 5~20 MB），但後續 Cache 後不影響 |
| 開發時 | `dotnet run`（本地開發）不用 AOT，速度不受影響 |

---

## TODO-2：Unmarshalled Interop 減少序列化開銷

**預期效果：每幀少花 ~2~5ms（減少 JS↔WASM 的 245KB JSON 序列化）**

> **建議在 TODO-1 完成後才做**，先確認 AOT 是否已達到 60 FPS。
> 若 AOT 後已順暢，此項可選擇性進行。

### 問題根源

目前每幀的 `drawFrame` 呼叫：

```csharp
// Home.razor — OnFrame()
byte[] rgba = NesCore.GetScreenRgba();          // 245,760 bytes
await JS.InvokeVoidAsync("nesInterop.drawFrame", rgba);   // ← JSON 序列化整個 byte[]
```

`InvokeVoidAsync` 會把 `byte[]` 轉成 Base64 JSON 字串傳給 JS，再由 JS 解碼。
每幀 245KB × 60 FPS = 14 MB/s 的序列化。

### 解法：改用 Unmarshalled 直接記憶體存取

**Step 1：`NesFrameStep.cs` 新增 unsafe 直接指標方法**

```csharp
// 在 NesFrameStep.cs 加入
[System.Runtime.InteropServices.DllImport("*")]
static extern unsafe void js_draw_frame(uint* argb, int len);

public static void DrawFrameDirect()
{
    unsafe
    {
        fixed (uint* p = ScreenBuf1x)
            js_draw_frame(p, ScreenBuf1x.Length);
    }
}
```

**Step 2：`nesInterop.js` 接收直接指標**

```javascript
// 取代原本的 drawFrame
Blazor.registerFunction('js_draw_frame', (argbPtr, len) => {
    const wasmMem = new Uint8Array(Module.HEAPU8.buffer, argbPtr, len * 4);
    const imgData = ctx.createImageData(256, 240);
    // ARGB → RGBA 轉換
    for (let i = 0; i < len; i++) {
        imgData.data[i*4+0] = wasmMem[i*4+2]; // R
        imgData.data[i*4+1] = wasmMem[i*4+1]; // G
        imgData.data[i*4+2] = wasmMem[i*4+0]; // B
        imgData.data[i*4+3] = 255;
    }
    ctx.putImageData(imgData, 0, 0);
});
```

**Step 3：`Home.razor` 改呼叫方式**

```csharp
// 把 InvokeVoidAsync("drawFrame") 改成
NesCore.DrawFrameDirect();   // 直接呼叫，無 async 開銷
```

### 注意事項

| 項目 | 說明 |
|------|------|
| 複雜度 | 中等，需要了解 WASM 記憶體模型 |
| 相依性 | 需要 `AllowUnsafeBlocks=true`（已設定） |
| 效果 | AOT 後主要瓶頸可能已不在這裡，先測量再決定 |

---

## 效能預估總覽

| 狀態 | 預估 FPS | 說明 |
|------|----------|------|
| 現在（IL Interpreter） | 10~30 | IL 直譯器，每個 opcode 都有 overhead |
| TODO-1 完成（AOT） | 60~150 | C# 預編譯為 WASM native 指令 |
| TODO-1+2 完成 | 60~200 | 額外減少每幀序列化開銷 |
| 理論上限 | ~200 | WASM 比 native JIT 慢約 3~5 倍 |

NES 要求 60 FPS，**TODO-1 完成後應可滿足需求**。
