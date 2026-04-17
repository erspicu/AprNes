# AprNes Avalonia — CRT GPU 加速設計

日期：2026-04-17
適用專案：`AprNesAvalonia/`（Avalonia 11.3.13 + SkiaSharp 2.88.9 + .NET 10）
狀態：**設計階段，尚未實作**

---

## 1. 目的與定位

### 為什麼做
目前 CRT 模擬（scanline / shadow mask / horizontal blur / phosphor decay / convergence / barrel distortion）在 CPU SIMD（`CrtScreen.Simd.cs`，`Vector256<T>` / AVX2）上執行，4x 解析度時仍佔用顯著 CPU 時間（參考 MEMORY.md：DSP Mode 2 4x 僅 82.65 FPS vs 無 DSP 109.59 FPS，CRT 佔 ~25% 幀時間）。用 GPU fragment shader 執行可：

- 釋放 CPU（騰出給 emulator core / APU / mapper）
- 隨解析度擴張近乎免費（fragment 平行度 >> AVX2 lane 數）
- 未來可做 NTSC 合成（per-dot 1D composite 訊號）在 CPU 上代價高，GPU 更適合

### 保留既有實作
**不動** `CrtScreen.cs`（純量 baseline）與 `CrtScreen.Simd.cs`（SIMD 版）。GPU 是**第三條路**，實驗性質，風險隔離。Runtime config 切換：
- `CrtImpl = Cpu` — 純量（net48 相容）
- `CrtImpl = Simd` — .NET 10 SIMD（目前預設）
- `CrtImpl = Gpu` — **新增**，Avalonia only

---

## 2. 技術堆疊

| 層 | 技術 | 說明 |
|----|------|------|
| Host | Avalonia 11.3.13 | 已整合 |
| 繪圖 | SkiaSharp 2.88.9 | 已作為 Avalonia backend |
| Shader | **SkSL**（SkiaSharp Shading Language）| `SKRuntimeEffect.CreateShader` |
| 橋接 | `ISkiaSharpApiLeaseFeature` | 已用於 `EmuScreenControl.EmuDrawOperation` |
| 資料 | `SKBitmap.InstallPixels` + `SKSurface` ping-pong | NES 輸入仍 zero-copy |

### SkSL 能力與限制
- ✅ Fragment shader（`half4 main(float2 coord)`）
- ✅ 多個 sampler（`uniform shader childTex`）
- ✅ 純量 / vec2-4 uniform
- ✅ 靜態分支、靜態展開迴圈
- ❌ 無 compute shader、無 structured buffer
- ❌ 動態長度迴圈要小心（用常數邊界）
- ❌ 沒有 `textureLod` 細部控制 — 用 `child.eval(coord)` 搭配 `SKShaderTileMode`

---

## 3. 現有 CRT Pipeline 重點（CPU 版）

流程摘要（`CrtScreen.Simd.cs`）：
```
NES 256x240 RGB
  ↓ (optional) NTSC Ntsc_FlushPendingRows → 1024x240 YIQ float
  ↓ Crt_Render (Parallel.For scanline)
    - scanline 權重（垂直 sin/triangular）
    - horizontal blur (RF/SVideo 模式)
    - gamma + brightness boost（per AnalogOutput）
    - mask（aperture grille / shadow mask LUT）
    - phosphor decay（frame blend with _prevFrame）
    - convergence（RGB sub-pixel 位移）
    - curvature（barrel/pincushion，precomputed LUT）
  ↓ uint* ARGB (up to 1024x840)
送 Avalonia zero-copy path
```

**關鍵參數**（需作為 SkSL uniform）：
- `AnalogSize`（2/4/6/8x）
- `AnalogOutput`（AV/SVideo/RF）
- `MaskType`（None / ApertureGrille / ShadowMask）
- `ScanlineStrength`、`Gamma`、`Brightness`、`Curvature`、`Convergence`
- NTSC：`UltraAnalog`、`HueOffset`、`SaturationBoost`

---

## 4. GPU 版 Pipeline 設計

### 總體策略：**分階段推進**
v1 先做單一 fragment pass 覆蓋大部分 CRT 效果；NTSC 與 blur 分別為 v2、v3。

```
v1 (MVP)              v2 (多 Pass)            v3 (NTSC)
============          ==============          ==============
[NES 256x240]         [NES 256x240]           [NES 256x240]
    ↓                     ↓                        ↓
 CRT.sksl            ntsc_composite.sksl     ntsc_modulate.sksl
(單 pass)             (256→1024 YIQ→RGB)     (YIQ 1D signal)
    ↓                     ↓                        ↓
 [畫面]              hblur.sksl              ntsc_demod.sksl
                     (separable gauss)       (phase-aware decode)
                         ↓                        ↓
                     crt_core.sksl            hblur.sksl
                     (mask/scan/etc)              ↓
                         ↓                    crt_core.sksl
                     [畫面]                       ↓
                                               [畫面]
```

### v1 MVP — 單 Pass CRT Core

**Pass**：一個 SkSL fragment shader
**輸入**：
- `uniform shader uScreen`：NES 256×240 RGB（`SKBitmap.InstallPixels` zero-copy）
- `uniform shader uPrevFrame`：上一幀完整畫面（for phosphor decay；首幀為全黑）
- `uniform float2 uResolution`：輸出像素尺寸
- `uniform float2 uInputSize`：256, 240
- `uniform float uScanlineStrength, uGamma, uBrightness, uCurvature`
- `uniform float3 uConvergenceOffset`：RGB 偏移（pixel 單位）
- `uniform int uMaskType`：0/1/2
- `uniform float uPhosphorDecay`：0..1

**輸出**：fragment ARGB，直接交給 Avalonia 的 `canvas`

**演算法大綱**（偽 SkSL）：
```glsl
half4 main(float2 fragCoord) {
    // 1. UV 轉換 + 曲面變形
    float2 uv = fragCoord / uResolution;
    uv = barrel(uv, uCurvature);                 // 1-2 乘加
    if (any(uv < 0) || any(uv > 1)) return 0;    // 螢幕外黑邊

    // 2. 取樣 NES 原始像素（含 convergence 分色）
    float2 nesUV = uv * uInputSize;
    half3 rgb;
    rgb.r = uScreen.eval(nesUV + uConvergenceOffset.xy * vec2(1, 0)).r;
    rgb.g = uScreen.eval(nesUV).g;
    rgb.b = uScreen.eval(nesUV - uConvergenceOffset.zz * vec2(1, 0)).b;

    // 3. Scanline 垂直強度調變
    float scanY = fract(uv.y * uInputSize.y);
    float scan = mix(1.0, 0.5 + 0.5 * sin(scanY * 3.14159),
                     uScanlineStrength);
    rgb *= scan;

    // 4. Mask（aperture grille / shadow mask）
    int mx = int(fragCoord.x) % 3;
    half3 maskRGB;
    if (uMaskType == 1) {
        // aperture grille: column-based RGB stripe
        maskRGB = (mx == 0) ? half3(1, 0.3, 0.3)
                : (mx == 1) ? half3(0.3, 1, 0.3)
                :             half3(0.3, 0.3, 1);
    } else if (uMaskType == 2) {
        // shadow mask: 2D pattern
        int my = int(fragCoord.y) % 2;
        maskRGB = shadowMaskLut(mx, my);
    } else {
        maskRGB = half3(1);
    }
    rgb *= maskRGB;

    // 5. Gamma + brightness
    rgb = pow(rgb, half3(uGamma));
    rgb *= uBrightness;

    // 6. Phosphor decay（與上一幀 blend）
    half3 prev = uPrevFrame.eval(fragCoord).rgb;
    rgb = mix(rgb, prev, uPhosphorDecay);

    return half4(rgb, 1);
}
```

### v2 — 多 Pass（加上 horizontal blur）
- 產生中繼 `SKSurface`（同輸出尺寸）
- Pass 1：blur（分離式 Gaussian，3-5 tap）→ mid surface
- Pass 2：讀 mid surface，執行 v1 核心 → 畫面

### v3 — NTSC 合成模擬
- Per-scanline 256 → 1024 複合訊號重建
- Composite phase modulation（`sin/cos(2πf·x + phase)`）
- Low-pass filter demodulation
- 最重階段；僅在 `UltraAnalog=true` 時啟用
- 可選用 raster scanline 逐列呼叫 shader，或單 pass 2D 處理

---

## 5. 與 Avalonia 整合的具體作法

### 現況
`AprNesAvalonia/Views/EmuScreenControl.cs`：
- 已用 `ISkiaSharpApiLeaseFeature` → `SKCanvas`
- 用 `SKBitmap.InstallPixels` 零拷貝 NES 畫面
- 用 `canvas.DrawBitmap` 配 `SKFilterQuality.Low` 做放大

### 新增 GPU path
1. **抽象層**：新增 `IEmuRenderer` 介面
   ```csharp
   interface IEmuRenderer {
       void Render(SKCanvas canvas, IntPtr framePtr, int w, int h, SKRect dst);
       void Dispose();
   }
   ```
2. **實作兩個版本**：
   - `BitmapBlitRenderer` — 包 `canvas.DrawBitmap`（現狀）
   - `CrtGpuRenderer` — 新增，持有 `SKRuntimeEffect`、`_prevSurface`
3. **EmuScreenControl** 持有 `IEmuRenderer`，依 config 切換
4. `CrtGpuRenderer.Render()` 內部：
   ```csharp
   // 建 SKBitmap 指向 NES 原始像素（zero-copy）
   // 建 child shader: nesShader = bmp.ToShader()
   // 建 uniforms dictionary
   // effect.ToShader(uniforms, children) → SKShader
   // paint.Shader = shader; canvas.DrawRect(dst, paint)
   // 將本幀結果拷到 _prevSurface（snapshot）供下幀 phosphor
   ```

### Shader 檔案組織
`AprNesAvalonia/Shaders/` 新增目錄：
- `crt_core_v1.sksl`（v1 單 pass）
- `hblur.sksl`（v2）
- `ntsc_composite.sksl`、`ntsc_demod.sksl`（v3）
- 以 `EmbeddedResource` 方式打包進 DLL，或純字串常數（看 SKRuntimeEffect API 哪個順手）

### Fallback
- `SKRuntimeEffect.CreateShader` 失敗（older GPU / software Skia）→ log warning、切回 SIMD path
- 測試 exe 截圖模式（TestRunner）強制用 CPU path 以保證 deterministic

---

## 6. 實作步驟（里程碑）

### M0 — 基礎骨架（0.5 天）
- [ ] 新增 `MD/gpu/` 目錄（本文件）
- [ ] 建立 `AprNesAvalonia/Shaders/` 目錄
- [ ] 新增 `IEmuRenderer` 介面與現況 `BitmapBlitRenderer` 重構
- [ ] Config 新增 `CrtImpl` 列舉 + UI toggle（`AnalogConfigWindow`）

### M1 — 最小 GPU Pass（1 天）
- [ ] 寫最簡 SkSL：僅放大（等於現況但走 runtime effect）
- [ ] `CrtGpuRenderer` 骨架：建 effect、uniforms、child shader、Render
- [ ] 畫面正確顯示（與 CPU path 視覺一致）
- [ ] 手動切換測試（config → GPU 跟 CPU 互切不當機）

### M2 — CRT 核心（1-2 天）
- [ ] SkSL 加入：scanline、mask（aperture grille）、gamma、brightness、convergence、barrel curvature
- [ ] Uniform 全數暴露並連到 config
- [ ] 與 SIMD path 視覺比對（diff 像素 < 2% 容忍）
- [ ] Benchmark vs SIMD：目標 ≥ 2x FPS 提升在 4x/6x

### M3 — Phosphor Decay（0.5 天）
- [ ] Ping-pong `SKSurface`（上一幀 snapshot）
- [ ] Child shader `uPrevFrame` 連線
- [ ] 首幀預設黑
- [ ] 視覺驗證拖影感與 CPU path 一致

### M4 — Horizontal Blur（多 Pass；1 天）
- [ ] 中繼 `SKSurface` 建立
- [ ] `hblur.sksl`（3-5 tap separable Gaussian）
- [ ] AV/SVideo/RF 依 blur 強度 uniform 差異
- [ ] Benchmark 維持優勢

### M5 — Shadow Mask LUT（0.5 天）
- [ ] 另一組 mask pattern（非 aperture grille）
- [ ] 預先產生 8x8 SKBitmap 當 texture LUT 或直接在 shader 寫死

### M6 — NTSC Stretch Goal（2-3 天，可延後）
- [ ] 1D composite modulation shader
- [ ] 相位解調 shader
- [ ] 3 pass 串聯（modulate → demod → crt_core）
- [ ] `UltraAnalog` 開關走 GPU path

### M7 — 整合驗證與文件（1 天）
- [ ] GPU 與 SIMD 在 10 款不同遊戲的視覺 screenshot 對照
- [ ] Benchmark 表：2x/4x/6x/8x × {無 DSP, DSP mode 2} × {CPU SIMD, GPU}
- [ ] 更新 `README.md` 說明 CRT 三種實作
- [ ] `MD/PerformanceWithAV/CRT_GPU_Benchmark_YYYY-MM-DD.md`

---

## 7. 風險與對策

| 風險 | 機率 | 對策 |
|------|:----:|------|
| SkSL 某些版本 Skia 不支援 | 低 | `SKRuntimeEffect.CreateShader` 失敗偵測 + fallback |
| Zero-copy `SKBitmap.InstallPixels` 做 shader child 失敗 | 中 | 備案：每幀 copy 到 `SKImage`（小幅效能損失，但 256×240 可接受）|
| Avalonia Render Thread GPU context 不穩 | 中 | 用 `try/catch` 包所有 shader 呼叫；失敗切 CPU |
| Phosphor decay 的 ping-pong `SKSurface` 在 resize 時破裂 | 中 | 監聽 `Bounds` 變更，重建 `SKSurface` |
| NTSC 在 GPU 實作複雜度超估 | 高 | v3 為 stretch goal；做不完不影響 v1/v2 |
| 不同 GPU 驅動程式對 SkSL 精度差異造成畫面微差 | 低 | 接受容差 ±2/255；不保證 bit-exact |

---

## 8. 效能目標

**基準**（現有 SIMD，摘自 MEMORY.md）：
- Analog 2x no DSP：117.91 FPS
- Analog 4x no DSP：109.59 FPS
- Analog 6x no DSP：82.38 FPS
- Analog 8x no DSP：79.03 FPS
- Analog 4x DSP Mode 2：82.65 FPS
- Analog 8x DSP Mode 2：64.49 FPS

**GPU 目標**（保守 2x，挑戰 3x）：
| 模式 | SIMD | GPU 目標 | 挑戰目標 |
|------|:----:|:--------:|:--------:|
| 4x no DSP | 109.59 | ≥ 220 | ≥ 330 |
| 6x no DSP | 82.38 | ≥ 165 | ≥ 250 |
| 8x no DSP | 79.03 | ≥ 160 | ≥ 240 |
| 4x DSP Mode 2 | 82.65 | ≥ 165 | ≥ 250 |
| 8x DSP Mode 2 | 64.49 | ≥ 130 | ≥ 195 |

（若 GPU path 不過基準 CPU 的 60%，則視為失敗並暫緩）

---

## 9. 參考資料

- [Avalonia Custom Skia Rendering](https://docs.avaloniaui.net/docs/guides/graphics-and-animation/custom-drawing-operation)
- [SkiaSharp SKRuntimeEffect](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skruntimeeffect)
- [SkSL Specification](https://skia.org/docs/user/sksl/)
- 同家族前例：RetroArch CRT-Royale GLSL、Mesen2 CRT shader、ShaderToy Mattias Gustavsson CRT 範例
- 本專案：`CrtScreen.cs`、`CrtScreen.Simd.cs`、`Views/EmuScreenControl.cs`

---

## 10. 決策點（開工前需確認）

1. **v1 是否跳過 NTSC 合成？** → 建議：是（簡化問題，v3 再處理）
2. **GPU fallback 是 SIMD 或純量？** → 建議：SIMD（config `CrtImpl = Simd`）
3. **Shader 打包方式**：EmbeddedResource vs 字串常數？ → 建議：字串常數（簡單，易 debug；後期再改 EmbeddedResource）
4. **Phosphor decay 需要嗎？** → 建議：v1 選配；CPU 版預設關，GPU 比照
5. **Testing/headless 模式是否支援 GPU path？** → 建議：**否**，強制走 CPU SIMD 以保 deterministic
