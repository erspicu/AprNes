# AprNes CRT — GPU 加速規劃（使用 Avalonia + Skia 內建能力）

- **建立日期**: 2026-04-15
- **範圍**: AprNesAvalonia 專案的 CRT 類比渲染管線 GPU 化
- **狀態**: 評估 / 規劃（尚未實作）
- **前提**: 使用 Avalonia 內建 SkiaSharp + SKRuntimeEffect，**不引入外部 GPU 庫**（Silk.NET / OpenTK / Vortice 等）

---

## 1. 為什麼用 Avalonia 自帶方案

✅ **零額外依賴**：AprNesAvalonia 已使用 Avalonia 11，內建 SkiaSharp
✅ **跨平台免費**：Skia 自動挑平台 backend（Windows DX / Linux GL / macOS Metal / Vulkan）
✅ **整合簡單**：GPU 結果可以直接給 Avalonia 的 `Image` 控制項或 `ICustomDrawOperation` 顯示，**零拷貝**
✅ **SKSL 是 GLSL 子集**：現有的 NTSC 數學可以幾乎直譯
✅ **多 pass 方便**：`SKSurface` 可作為 render target，鏈式 pass 自然支援

**取捨：**
- ❌ 無 compute shader（只能 fragment-style）
- ❌ SkiaSharp 比原生 GL/Vulkan 有薄 overhead（對我們的 pipeline 規模不重要）
- ✅ 對 AprNes 的 CRT 這種「線性 fragment shader 鏈」**正好夠用**

---

## 2. CRT pipeline → SKSL 對映

### 目前 CPU pipeline（已 parallel、已 GPU-ready）

```
CPU:  PPU emulation
       │
       └─> ntsc_rowPalettes[240][256]   (60 KB)
            ntsc_rowPhase0[240]          (960 B)
            ntsc_rowEmphasis[240]        (240 B)
       │
       ▼
      DemodulateRow (Parallel.For, CPU SIMD)
       │
       └─> linearBuffer (float R/G/B, per-row)
       │
       ▼
      ApplyHorizontalBlur (Parallel.For, 3-tap SIMD)
       │
       ▼
      Crt_Render main pass (Parallel.For)
       │
       └─> ntsc_analogScreenBuf (uint RGBA)
       │
       ▼
      ApplyFullFrameCurvatureAndConvergence (Parallel.For, gather)
       │
       ▼
      Final output → WriteableBitmap → Avalonia present
```

### 目標 GPU pipeline

```
CPU:  PPU emulation (unchanged)
       │
       └─> Upload to GPU textures:
            • paletteTex: SKImage from rowPalettes (R8, 256×240)
            • phase0Arr:  SKRuntimeEffectUniforms (float[240])
            • emphArr:    SKRuntimeEffectUniforms (float[240])
       │
       ▼
GPU:  Pass 1: demodulate.sksl
       │   Input:  paletteTex + uniforms
       │   Output: linearRGB SKSurface
       ▼
      Pass 2: hblur.sksl
       │   Input:  linearRGB
       │   Output: linearBlur SKSurface
       ▼
      Pass 3: crt_render.sksl
       │   Input:  linearBlur + scanlineWeights
       │   Output: crtScene SKSurface
       ▼
      Pass 4: curv_conv.sksl (gather UV warp + chromatic shift)
       │   Input:  crtScene
       │   Output: final SKSurface
       │
       ▼
      Avalonia ICustomDrawOperation → zero-copy present
```

**關鍵：** Pass 間都是 GPU 內部的 SKSurface 交換，**沒有 CPU readback**。

---

## 3. 各階段 SKSL 移植評估

### 3.1 Pass 1: Demodulate (最複雜)

**CPU 版**：`DecodeScanline_Physical_Worker` (600+ 行 C#)，包含：
- `GenerateWaveform`：NES 調色盤 → waveform (per-dot 4 samples)
- `DemodulateRow_Core`：54-tap Q, 18-tap I, 6-tap Y Hann-window 卷積
- `YiqToRgb`：YIQ → RGB 色彩矩陣

**GPU 策略**：
- **`GenerateWaveform` → 1D texture lookup**
  - 預先算好 `waveTable[64][6][4]` = 1536 floats，上傳成 SKImage（R32F, 1536×1）
  - Shader 根據 palette index + phase 查表
- **`DemodulateRow_Core` → 卷積 fragment shader**
  - 將 `combinedQ[6][54]` 預先算好並上傳成 SKImage（R32F, 54×6）
  - Shader 用 `for` 迴圈做 54 tap 卷積（GPU 上 for 是 unrolled）
  - 或改用 mipmap-based blur 近似（若精度可放寬）
- **YIQ→RGB**：3×3 matrix，SKSL 直接用 `mat3` 型別

**行數估計**：~250-300 SKSL

**難點**：SKRuntimeEffect 的 uniform 上限（一般 16KB），`combinedQ` 是 324 floats = 1.3KB 沒問題，但若 Emphasis 需要更大 LUT（64×8 per emphasis）可能要用多張 texture。

---

### 3.2 Pass 2: Horizontal Blur（極易）

**CPU 版**：3-tap 對稱濾波，`α·prev + c·cur + α·next`

**SKSL 版**：
```glsl
uniform shader src;
uniform float alpha;
uniform float center;
uniform float2 texelSize;
half4 main(float2 uv) {
    half3 prev = sample(src, uv - float2(texelSize.x, 0)).rgb;
    half3 cur  = sample(src, uv).rgb;
    half3 next = sample(src, uv + float2(texelSize.x, 0)).rgb;
    return half4(prev * alpha + cur * center + next * alpha, 1);
}
```
~8 行。完成。

---

### 3.3 Pass 3: Crt_Render main pass（中等）

**CPU 版**：per-pixel 計算 scanline weight、boost、source sampling

**SKSL 版**：
- Scanline weight / boost 可以用 1D uniform 陣列（240 floats）
- Source sampling 就是 `sample(linearBufferTex, uv)` 自動 bilinear
- Bloom：多 sample + 加權（或用另一個 downsampled texture）

**行數估計**：~40-60 SKSL

---

### 3.4 Pass 4: Curvature + Convergence（GPU 最擅長）

**CPU 版**：gather 操作，目前 `_curvMap` 是預先算的 source index lookup

**SKSL 版**：
```glsl
uniform shader scene;
uniform float curvature;
uniform float convShift;

half4 main(float2 uv) {
    // Curvature UV warp (barrel distortion)
    float2 cc = uv * 2 - 1;
    float r2 = dot(cc, cc);
    float2 cuv = uv + cc * (r2 * curvature);

    // Bounds check → black if off-screen
    if (any(greaterThan(abs(cc + cc * r2 * curvature), float2(1)))) {
        return half4(0, 0, 0, 1);
    }

    // Per-channel chromatic shift
    float r = sample(scene, cuv + float2(convShift, 0)).r;
    float g = sample(scene, cuv).g;
    float b = sample(scene, cuv - float2(convShift, 0)).b;
    return half4(r, g, b, 1);
}
```

~20 行。**GPU 就是為此而生**——texture sampling + UV 扭曲是 fragment shader 最原始的用途。

Performance：CPU 上目前 50% CPU（8x 時），GPU 上只是幾個 texture fetch。預期 **>50x 加速**。

---

## 4. 資料流 / 上傳方案

### 4.1 CPU → GPU（每 frame 上傳）

| 資料 | 大小 | 上傳方式 |
|---|---|---|
| paletteTex | 60 KB (R8, 256×240) | `SKBitmap.InstallPixels` → `SKImage.FromBitmap` |
| phase0 array | 960 B | SKRuntimeEffect uniform `float[240]` |
| emphasis | 240 B | uniform `float[240]` |
| frameCount, CRT 參數 | < 1KB | scalar uniforms |

**總 upload：~61 KB/frame × 60 fps = 3.7 MB/s** — 無感。

### 4.2 GPU 間 pass 交換

- 每個 pass 輸出到 `SKSurface`
- 下一 pass 把該 surface `Snapshot()` 成 SKImage 作為 sampler
- 全部在 GPU memory，**零 CPU readback**

### 4.3 GPU → Display

- Final SKSurface 透過 `ICustomDrawOperation.Render(drawingContext)` 直接繪到 Avalonia window
- Avalonia 11 支援直接取得 `SKCanvas`，可以 `canvas.DrawImage(finalImage, rect)`
- **零拷貝**：GPU 圖像直接成為 window 合成的一部分

---

## 5. 多 pass 的 SKSurface 鏈

```csharp
// 建立 3 個 render target（對齊 dstW × dstH）
using var surf1 = SKSurface.Create(context, dstInfo);  // linearRGB
using var surf2 = SKSurface.Create(context, dstInfo);  // linearBlur
using var surf3 = SKSurface.Create(context, dstInfo);  // crtScene

// Pass 1: Demodulate → surf1
surf1.Canvas.DrawRect(fullRect, demodulatePaint);

// Pass 2: HBlur → surf2 (read surf1)
hblurPaint.Shader = SKShader.CreateRuntimeEffect(
    hblurEffect,
    uniforms,
    new[] { surf1.Snapshot().ToShader() });
surf2.Canvas.DrawRect(fullRect, hblurPaint);

// Pass 3 + 4 同理...

// Final: surf3 drawn to Avalonia canvas
```

---

## 6. 階段式移植計畫（可逐步驗證）

### Phase A: GPU Pipeline 骨架（先不換演算法）

**目標**：把 CPU 計算結果每 frame 上傳成 texture，用 SKSL 單純做 `DrawImage` 顯示。驗證資料路徑通順。

- CPU 算完 `ntsc_analogScreenBuf`（現狀）
- 上傳成 SKImage
- 透過 `ICustomDrawOperation` 顯示
- **預期 FPS**：目前 GDI/Avalonia bitmap upload 的差別（可能略快也可能略慢）
- **目的**：驗證 SkiaSharp interop + 上傳路徑

---

### Phase B: Curvature + Convergence 移 GPU

**目標**：最大 ROI 的單 stage 移植。CPU 算到 `crtScene`，GPU 做 curvature + chromatic shift。

- 新增 `curv_conv.sksl`（~20 行）
- 修改 CrtScreen：`Crt_Render` 結束時把 `_curvTemp` 上傳 GPU（replace `ApplyFullFrameCurvatureAndConvergence`）
- GPU shader 輸出到 final surface
- **預期 FPS**：4x +10%、8x +30-50%（CPU 從 50% CPU 掉到接近 0）

**風險**：需要上傳 `_curvTemp` 每 frame（4x 下 ~4 MB，不小），整體 ROI 要看 upload + shader vs 原本 CPU gather 的差異。

---

### Phase C: Crt_Render + HBlur 移 GPU

**目標**：只剩 Demodulate 在 CPU，其後全 GPU。

- 新增 `hblur.sksl`（8 行）+ `crt_render.sksl`（~60 行）
- CPU 上傳 `linearBuffer`（3 planes × 1024 × 224 × float = 2.6 MB/frame）
- 3 個 SKSurface pass 到 final
- **預期 FPS**：4x +15-20%、8x +40-60%

**注意**：linearBuffer 是 float，R32F texture 上傳較重，可考慮量化到 R8 (256 levels) 看視覺差異。

---

### Phase D: Demodulate 也移 GPU（最終形態）

**目標**：CPU 只做 PPU emulation + palette upload，剩下全 GPU。

- 新增 `demodulate.sksl`（300 行）含 NTSC 卷積 + YIQ→RGB
- 上傳 `paletteTex` + phase0 + emphasis uniforms
- 4 stage GPU pipeline
- **預期 FPS**：4x +30-50%、8x +80-100%

**挑戰**：Physical path 的 Hann window 卷積是最大移植工作量。可以先移 Fast path，Physical 留在 CPU 分開模式。

---

## 7. 技術細節提醒

### SKRuntimeEffect uniform 限制
- 各平台不同，保守估 **16 KB per runtime effect**
- 我們最大的 `combinedQ` = 324 floats = 1.3 KB，**綽綽有餘**
- 若超過：拆成多 pass 或用 SKImage texture 當 LUT

### SKSL 語法差異（vs GLSL）
- 型別：`half4`（low precision RGBA）、`float2/3/4`（vec2/3/4）
- 入口：`half4 main(float2 uv)`
- Texture sample：`sample(shader, uv)` 取代 `texture2D`
- 無 loop 限制（.NET Framework 時期 SKSL 已支援 unbounded `for`）

### Avalonia + SkiaSharp 實作關鍵點
1. 取得 Skia `GRContext`：`AvaloniaLocator.Current.GetService<ISkiaSharpGpu>().TryGetGrContext()`
2. 建 `SKSurface` 時傳入 `GRContext` 得 GPU surface
3. `ICustomDrawOperation.Render` 在 UI thread 呼叫，可直接 `drawingContext.SkiaSharp` 取得 `SKCanvas`

### Phosphor Decay (跨幀 texture)
- 需要 ping-pong：`prevFrameSurface` 和 `currentFrameSurface` 輪換
- 每 frame render 時讀 prev、寫 current、swap references

---

## 8. 建議排程（與 .NET 10 遷移合併考慮）

```
Phase 0 (現在) .NET Framework 4.8.1 CRT 100% CPU + 並行化完整 ← 你在這
   │
   ▼
Phase 1 .NET 10 遷移
   │   • TieredPGO + Vector256
   │   • 繼續優化 AprNesAvalonia（已有）
   │
   ▼
Phase 2 SIMD 深度優化 (CPU)
   │   • Avx2.GatherVector256 攻 Curvature
   │   • Vector256/512 vectorize DemodulateRow
   │   • 4x 目標 150+, 8x 80+
   │
   ▼
Phase 3 GPU Phase A + B (Avalonia/Skia)
   │   • Pipeline 骨架
   │   • Curvature 先上 GPU
   │   • 4x 180+, 8x 120+
   │
   ▼
Phase 4 GPU Phase C + D
   │   • CRT + HBlur + Demodulate 全 GPU
   │   • 4x 300+, 8x 200+
   │   • 唯一 bottleneck 剩 CPU emulation
```

---

## 9. 本文檔要點回顧

✅ **Avalonia + SkiaSharp + SKRuntimeEffect 是最合適的 AprNes GPU 方案**
  - 零額外依賴、跨平台、整合乾淨

✅ **CRT pipeline 架構已 GPU-ready**
  - Phase0 capture per-scanline（race-free）
  - Worker functions 無 static mutation
  - 所有 stage parallel-friendly

✅ **建議先完成 .NET 10 + SIMD 後再上 GPU**
  - 兩個大變動分開做降低風險
  - SIMD 紅利可能讓 GPU 不是必需（但 6x/8x 以上 GPU 仍有巨大優勢）

✅ **Phase B（Curvature 上 GPU）是單點突破最大 ROI**
  - 20 行 SKSL
  - CPU 50% → GPU 近乎零
  - 尤其 8x 下收益驚人

---

## 附錄：現有已做好的 GPU 準備工作（f3aa7f3, afe29cc）

- `ntsc_rowPalettes` + `ntsc_rowEmphasis` + `ntsc_rowPhase0` 三個 snapshot buffer
- 這些 buffer 的**格式和 GPU 上傳需求一致**：R8 texture + uniform arrays
- `DecodeScanline_Physical_Worker` / `DecodeScanline_Fast_Worker` 無 side-effect，參數化純函數
- **只需要把 worker 函式的 C# 數學 port 成 SKSL，資料結構不用再改**

這一波 parallel demod 的架構重構，剛好把 GPU migration 的第一步鋪好了。
