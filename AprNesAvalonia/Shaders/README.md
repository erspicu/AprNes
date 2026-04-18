# AprNesAvalonia Shaders

這個目錄放 SkSL（SkiaSharp Shading Language）runtime fragment shader 檔案。
執行時會被 `ShaderLoader.cs` 從 **exe 同目錄的 `Shaders/`** 子資料夾讀取。

## 執行期路徑

| 角色 | 路徑 |
|------|------|
| 原始檔（git 追蹤、IDE 編輯） | `AprNesAvalonia/Shaders/*.sksl` |
| 執行時載入（可即時編輯）| `AprNesAvalonia/bin/{Debug,Release}/net10.0/Shaders/*.sksl` |

csproj 的 `<None CopyToOutputDirectory="PreserveNewest">` 會在 build 時把原始檔同步到輸出目錄。

## 自行修改流程（不需重 build）

1. 用文字編輯器打開 `bin/Release/net10.0/Shaders/crt_core_v1.sksl`
2. 存檔
3. 重啟 AprNesAvalonia.exe — shader 重新編譯載入

> **注意**：`ShaderLoader` 會快取已編過的 shader，**同一個 process 內改檔不會即時生效**。需要重啟 exe。未來若要 hot reload 可改 `ShaderLoader.Reset()` + `FileSystemWatcher`。

## 現有 shader

### `crt_core_v1.sksl`
Phase 3A CRT 主 shader。功能對齊 `CrtScreen.Simd.cs`：
- Barrel curvature (UV 彎曲)
- 3-tap horizontal blur (HBeamSpread)
- RGB convergence（位置相依，邊緣偏移最大）
- Gaussian beam scanline + interlace jitter
- Scanline × bloom 耦合
- Vignette × brightness 融合
- Aperture grille / shadow mask
- Gamma V*(1-GC + GC*V)
- Phosphor decay（ping-pong prev surface）

## SkSL 版本與限制

目前用 SkiaSharp **3.119.3-preview.1.1**（搭配 Skia m116+ Raster Pipeline 或真 GPU GRContext）。寫 shader 時注意：

| 限制 | 建議 |
|------|------|
| `int` 整數取模 `%` 不允許 | 用 `int(mod(x, N))` |
| `uniform int` 不允許 | 用 `uniform float` 再 `int(v + 0.5)` 轉 |
| 子 shader 宣告 | `uniform shader uName;`，呼叫 `uName.eval(coord)` |
| 無 loop 動態邊界 | 常數邊界，或改用 branch |
| 無 structured buffer | 用 uniform / child shader 傳資料 |

## 新增自己的 shader

1. 在 `AprNesAvalonia/Shaders/` 建 `your_effect.sksl`
2. 重 build（copy-to-output 會帶進 `bin/.../Shaders/`）
3. C# 端用 `ShaderLoader.Load("your_effect.sksl")` 取得 `SKRuntimeEffect`
4. 配上 uniforms / child shaders → `effect.ToShader(uniforms, children)`
5. `canvas.DrawRect(rect, paint)` 套用

範例參考 `AprNesAvalonia/CrtGpuRenderThread.cs`。

## 除錯

- Shader 編譯錯誤會 log 到 console + `gui_benchmark.trace.log`（benchmark 模式）
- 若 shader 失敗，`CrtScreen.Shared.cs` dispatcher 會 fallback 到 Simd backend（不會崩）

## 參考資源

- [SkSL 官方規格](https://skia.org/docs/user/sksl/)
- [SkiaSharp SKRuntimeEffect API](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skruntimeeffect)
- 本專案設計文件：`MD/gpu/CRT_GPU_Design.md`
