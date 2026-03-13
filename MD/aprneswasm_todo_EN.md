# AprNesWasm Performance Optimization TODO

> Current FPS is low (10~30) because: C# code runs in the WASM IL interpreter, plus JS↔WASM serialization overhead.
> The two improvement directions below can be pursued independently; recommended order: do TODO-1 first, confirm the result, then consider TODO-2.

---

## TODO-1: Enable WASM AOT Compilation ⭐ Priority

**Expected result: FPS improves from 10~30 to 60~150**

### Steps

**Step 1: Install the wasm-tools workload (requires administrator, only needs to be done once)**

```bat
:: Open CMD as Administrator
dotnet workload install wasm-tools
```

**Step 2: Open `AprNesWasm/AprNesWasm.csproj` and uncomment this line**

```xml
<!-- Find this line and remove the <!-- --> -->
<!-- <RunAOTCompilation>true</RunAOTCompilation> -->

<!-- Change to -->
<RunAOTCompilation>true</RunAOTCompilation>
```

**Step 3: Rebuild + deploy**

```bat
build_wasm.bat      ← This build will take 3~5 minutes (AOT compilation time)
deploy_wasm.bat
```

### Notes

| Item | Description |
|------|------|
| Build time | Increases by 3~5 minutes (only during publish; `dotnet run` for development is unaffected) |
| File size | `publish_wasm\wwwroot` will grow (extra `.wasm` pre-compiled files inside `_framework/`) |
| Initial load | Browser download size increases (may add 5~20 MB), but subsequent cached loads are unaffected |
| During development | `dotnet run` (local development) does not use AOT; speed is unaffected |

---

## TODO-2: Unmarshalled Interop to Reduce Serialization Overhead

**Expected result: ~2~5ms less per frame (reduces 245KB JSON serialization of JS↔WASM)**

> **Recommended to do only after TODO-1 is complete**, to first confirm whether AOT has already achieved 60 FPS.
> If performance is smooth after AOT, this item is optional.

### Root Cause

The current `drawFrame` call per frame:

```csharp
// Home.razor — OnFrame()
byte[] rgba = NesCore.GetScreenRgba();          // 245,760 bytes
await JS.InvokeVoidAsync("nesInterop.drawFrame", rgba);   // ← JSON-serializes the entire byte[]
```

`InvokeVoidAsync` converts `byte[]` to a Base64 JSON string for JS, which then decodes it.
245KB × 60 FPS = 14 MB/s of serialization per frame.

### Solution: Use Unmarshalled Direct Memory Access

**Step 1: Add an unsafe direct pointer method in `NesFrameStep.cs`**

```csharp
// Add to NesFrameStep.cs
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

**Step 2: Receive the direct pointer in `nesInterop.js`**

```javascript
// Replace the original drawFrame
Blazor.registerFunction('js_draw_frame', (argbPtr, len) => {
    const wasmMem = new Uint8Array(Module.HEAPU8.buffer, argbPtr, len * 4);
    const imgData = ctx.createImageData(256, 240);
    // ARGB → RGBA conversion
    for (let i = 0; i < len; i++) {
        imgData.data[i*4+0] = wasmMem[i*4+2]; // R
        imgData.data[i*4+1] = wasmMem[i*4+1]; // G
        imgData.data[i*4+2] = wasmMem[i*4+0]; // B
        imgData.data[i*4+3] = 255;
    }
    ctx.putImageData(imgData, 0, 0);
});
```

**Step 3: Change the call method in `Home.razor`**

```csharp
// Replace InvokeVoidAsync("drawFrame") with
NesCore.DrawFrameDirect();   // Direct call, no async overhead
```

### Notes

| Item | Description |
|------|------|
| Complexity | Moderate; requires understanding the WASM memory model |
| Dependency | Requires `AllowUnsafeBlocks=true` (already configured) |
| Effect | After AOT, the main bottleneck may no longer be here; measure first before deciding |

---

## Performance Estimate Overview

| State | Estimated FPS | Description |
|------|----------|------|
| Current (IL Interpreter) | 10~30 | IL interpreter; every opcode has overhead |
| TODO-1 complete (AOT) | 60~150 | C# pre-compiled to WASM native instructions |
| TODO-1+2 complete | 60~200 | Additionally reduces per-frame serialization overhead |
| Theoretical ceiling | ~200 | WASM is ~3~5× slower than native JIT |

NES requires 60 FPS; **TODO-1 should be sufficient to meet this requirement**.
