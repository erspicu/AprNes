# Analog + Ultra Analog + CRT 6x 效能瓶頸分析

> 2026-03-29

---

## 問題

Analog + Ultra Analog + CRT 全開、音效 Modern 模式下，6x 解析度實際 GUI 執行低於 60 FPS。
Headless benchmark 與實際 GUI FPS 有明顯差距。

---

## 完整 Frame 渲染管線

```
NES PPU (256×240)
  ↓ per-scanline
ntscScanBuf[256] (palette indices)
  ↓ DecodeScanline_Physical() × 240 scanlines
linearBuffer (1024×240×3 float planes = 2.88 MB)
  ↓ Crt_Render() (scanline 240, dot 1)
AnalogScreenBuf (1536×1260 ARGB32 = 7.74 MB @ 6x)
  ↓ VideoOutput event
SetDIBitsToDevice() → Windows Forms Panel
  ↓
_event.WaitOne() 阻塞等 UI 完成
```

---

## 各階段 Buffer 大小 (6x)

| Buffer | 解析度 | 大小 | 生命週期 |
|--------|--------|------|----------|
| ScreenBuf1x | 256×240 | 245 KB | 常駐 |
| ntscScanBuf | 256 bytes | 256 B | per-scanline 重用 |
| linearBuffer | 1024×240×3 planes | 2.88 MB | 常駐 |
| AnalogScreenBuf | 1536×1260 | 7.74 MB | AnalogSize 變更時重配 |
| _prevFrame (phosphor) | 1536×1260 | 7.74 MB | CRT 啟用時常駐 |
| _curvTemp (curvature) | 1536×1260 | 7.74 MB | Curvature 啟用時常駐 |

---

## 每幀時間預算分析

60 FPS 目標 = **16.67 ms/frame**

| 階段 | 估計耗時 | 頻率 | 說明 |
|------|---------|------|------|
| PPU cycle-accurate 模擬 | ~2-3 ms | per-dot (89K/frame) | CPU/PPU/APU tick |
| **NTSC Physical 解碼** | **15-25 ms** | per-scanline (×240) | `DecodeScanline_Physical` 波形生成 + IQ 解調 |
| **CRT 光學渲染** | **8-15 ms** | per-frame | `Crt_Render` 194 萬像素處理 |
| **GDI SetDIBitsToDevice** | **5-10 ms** | per-frame | 7.74 MB 同步記憶體傳輸 |
| Audio DSP (Mode 2) | ~1-2 ms | per-sample batch | Oversampler + mixer + FX |
| **合計** | **~31-55 ms** | | **遠超 16.67 ms 預算** |

---

## 三大瓶頸排序

```
1. NTSC Physical 解碼    ████████████████████  15-25 ms  ← 最大瓶頸
2. CRT 光學渲染          ████████████          8-15 ms
3. GDI 記憶體傳輸        ████████              5-10 ms   ← GUI 專屬
                                              ────────
                                              28-50 ms > 16.67 ms
```

### 瓶頸 1: NTSC Physical 解碼（最大）

- **檔案**: `NesCore/NTSC_CRT/Ntsc.cs` — `DecodeScanline_Physical()`
- 每 scanline stack alloc `float[1584]` × 2 = 12.7 KB
- 完整 NTSC composite 波形生成（sin/cos 載波調變）
- RF/AV/S-Video 各有不同編碼路徑
- IQ 解調用 windowed quadrature filter
- 240 scanlines × ~80-100 μs/scanline = 19-24 ms

### 瓶頸 2: CRT 光學渲染

- **檔案**: `NesCore/NTSC_CRT/CrtScreen.cs` — `Crt_Render()`
- 輸出 1536×1260 = 194 萬像素（6x）
- 每像素操作：Gaussian beam spread、bilinear interpolation、bloom、vignette
- 可選效果：shadow mask、phosphor decay、convergence、curvature
- 使用 `Parallel.For` 但仍受 CPU 核心數限制
- 像素數隨解析度平方成長：4x=107萬, 6x=194萬, 8x=344萬

### 瓶頸 3: GDI 記憶體傳輸（GUI 專屬）

- **檔案**: `tool/NativeRendering.cs` — `SetDIBitsToDevice()`
- **同步呼叫** — CPU 等 driver 完成 7.74 MB 複製才返回
- 沒有 double buffering，沒有 async DMA
- `_event.WaitOne()` 阻塞模擬執行緒等 UI 完成
- Windows DWM compositor 額外一次合成（非全螢幕時）

---

## Headless vs GUI 差距分析

Headless 模式跳過的步驟：

| 步驟 | Headless | GUI | 差距 |
|------|----------|-----|------|
| `SetDIBitsToDevice` (7.74 MB) | 跳過 | 同步等待 | +5-10 ms |
| `_event.WaitOne()` UI 同步 | 跳過 | 阻塞 | +0-5 ms |
| DWM compositor 合成 | 無 | 有 | +1-3 ms |
| **合計 GUI 專屬開銷** | | | **+8-15 ms/frame** |

這解釋了 headless benchmark ~66-82 FPS vs GUI 實際 < 60 FPS 的差距。

---

## 各解析度 GDI 傳輸負擔

| 倍率 | 輸出解析度 | Buffer 大小 | 60 FPS 頻寬 |
|------|-----------|------------|------------|
| 2x | 512×420 | 0.86 MB | 52 MB/s |
| 4x | 1024×840 | 3.44 MB | 207 MB/s |
| 6x | 1536×1260 | 7.74 MB | 464 MB/s |
| 8x | 2048×1680 | 13.76 MB | 826 MB/s |

---

## Frame 同步機制

```csharp
static void RenderScreen()
{
    screen_lock = true;
    if (AnalogEnabled && UltraAnalog && CrtEnabled)
        Crt_Render();                    // ← CPU 密集 (8-15 ms @ 6x)
    VideoOutput?.Invoke(null, null);     // ← GDI blitting (5-10 ms @ 6x)
    screen_lock = false;
    emuWaiting = true;
    _event.WaitOne();                    // ← 阻塞等 UI 完成
    emuWaiting = false;
}
```

- 模擬執行緒在 `RenderScreen` 內**依序**執行 CRT 渲染 + GDI 輸出 + 等待
- 無法重疊計算：下一幀的 PPU 模擬被阻塞直到當前幀顯示完成
- 瓶頸串聯而非並聯

---

## 優化方向

| 方向 | 預期效果 | 複雜度 | 說明 |
|------|---------|--------|------|
| **NTSC/CRT → GPU compute shader** | 最大 (10x+) | 高 | DirectX/OpenGL compute，管線移至 GPU |
| **GDI → D3D11 SwapChain** | 消除 GDI 同步 | 中 | 替換顯示後端，triple buffering |
| **Async double buffer** | 解耦 GDI 阻塞 | 中 | 獨立渲染執行緒，ping-pong buffer |
| **CRT SIMD 進一步優化** | 30-50% CRT 提速 | 中 | AVX2 向量化 beam spread / mask |
| **NTSC 降採樣 (2x 取代 4x)** | 減半解碼量 | 低 | 水平解析度取捨 |
| **動態解析度切換** | 維持 60 FPS | 低 | FPS < 60 時自動降倍率 |

### 根本結論

GDI `SetDIBitsToDevice` 確實是 headless/GUI 差距的主因（~8-15 ms），但 6x 跑不到 60 FPS 的**根本原因是 NTSC Physical 解碼 + CRT 渲染的 CPU 運算量本身就超過 16.67 ms 預算**。即使在 headless 模式，6x 也只有 ~66 FPS（剛好及格線），GUI 額外的 GDI 開銷讓它掉到 60 以下。

要在 6x 穩定 60 FPS，需要**同時**處理 CPU 運算瓶頸（NTSC+CRT）和 GDI 傳輸瓶頸。最有效的方案是將整條 NTSC+CRT 管線移至 GPU。
