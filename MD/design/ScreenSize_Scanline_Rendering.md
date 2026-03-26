# 非類比模式 — 畫面大小 & 掃描線渲染流程

## 概述

非類比模式下，畫面渲染由兩組設定控制：
- **Screen（畫面大小）**：`filter="xbrz"`，x1 ~ x9
- **Scanline（掃描線）**：`filter="scanline"`，僅 x2 / x4 / x6

兩者互斥，由 `AprNes_ConfigureUI` 的 radio button 決定，寫入 `AppConfigure["ScreenSize"]` 和 `AppConfigure["filter"]`。

渲染器透過反射動態載入：
```csharp
// AprNesUI.cs L1150
RenderObj = (InterfaceGraphic)Activator.CreateInstance(
    Type.GetType("AprNes.Render_" + AppConfigure["filter"] + "_" + ScreenSize + "x"));
```

所有渲染器實作 `InterfaceGraphic` 介面（`tool/InterfaceGraphic.cs`）：
```csharp
unsafe interface InterfaceGraphic
{
    void init(uint* input, Graphics _device);
    void Render();
    void freeMem();
    Bitmap GetOutput();
}
```

---

## 畫面大小（Screen，filter=xbrz）

UI 上 x1~x9 的 radio button（`radioButtonX1` ~ `radioButtonX9`），對應 `Render_xbrz_{N}x` 類別。

### 倍率與渲染方式

| 倍率 | 輸出尺寸 | 渲染方式 | 緩衝區 |
|------|----------|----------|--------|
| x1 | 256×240 | 直接輸出 `ScreenBuf1x`，無縮放 | 無額外分配 |
| x2 | 512×480 | `HS_XBRz.ScaleImage2X` | `_output` 245760 uint |
| x3 | 768×720 | `HS_XBRz.ScaleImage3X` | `_output` 768×720 |
| x4 | 1024×960 | `HS_XBRz.ScaleImage4X` | `_output` 1024×960 |
| x5 | 1280×1200 | `HS_XBRz.ScaleImage5X` | `_output` 1280×1200 |
| x6 | 1536×1440 | `HS_XBRz.ScaleImage6X` | `_output` 1536×1440 |
| x8 | 2048×1920 | xBRZ 4x → Scale2x（兩段放大） | `_output_tmp` 1024×960 + `_output` 2048×1920 |
| x9 | 2304×2160 | xBRZ 3x → Scale3x（兩段放大） | `_output_tmp` 768×720 + `_output` 2304×2160 |

### 視窗大小計算

```csharp
// AprNesUI.cs initUIsize()
int renderWidth  = 256 * ScreenSize;
int renderHeight = 240 * ScreenSize;
this.Width  = renderWidth  + 26;   // panel + 左右邊框
this.Height = renderHeight + 92;   // panel + 上工具列(35) + 下按鈕列(57)
```

### 渲染流程（每幀）

```
ScreenBuf1x (256×240, NesCore PPU 輸出)
    ↓
HS_XBRz.ScaleImage{N}X(_input, _output)    ← xBRZ 像素藝術縮放演算法
    ↓
NativeGDI.DrawImageHighSpeedtoDevice()       ← 直接 blit 到 panel Graphics
```

x8/x9 多一步中間縮放：
```
x8: ScreenBuf1x → xBRZ 4x (1024×960) → Scale2x (2048×1920) → GDI blit
x9: ScreenBuf1x → xBRZ 3x (768×720)  → Scale3x (2304×2160) → GDI blit
```

---

## 掃描線（Scanline，filter=scanline）

UI 上 x2s / x4s / x6s 的 radio button（`radioButtonX2s` ~ `radioButtonX6s`），對應 `Render_scanline_{N}x` 類別。

### 倍率與渲染方式

| 倍率 | 來源 | 輸出尺寸 | 渲染流程 |
|------|------|----------|----------|
| x2 | 256×240（原始） | **600×480** | indexTable 查表 → YIQ 掃描線 → 水平模糊 |
| x4 | 512×480（xBRZ 2x） | **1196×960** | xBRZ 2x → indexTable 查表 → YIQ 掃描線 → 水平模糊 |
| x6 | 768×720（xBRZ 3x） | **1792×1440** | xBRZ 3x → indexTable 查表 → YIQ 掃描線 → 水平模糊 |

### 特殊寬度說明

掃描線模式使用非整數倍寬度（600、1196、1792），而非標準的 512、1024、1536。這是為了模擬 NES 原生 8:7 像素寬高比的水平拉伸效果。`initUIsize()` 中有對這三種寬度的特殊 switch-case 處理。

### 掃描線渲染步驟（LibScanline.cs）

以 `ScanlineFor1x()`（256×240 → 600×480）為例：

```
步驟 1: indexTable 查表
    ─ 每個輸出像素 (x, y) 查 indexTable1x[] 取得來源 ScreenBuf1x 座標
    ─ indexTable 在 init() 時預計算：
      indexTable1x[x + y*600] = (int)(x * (1/600.0 * 256.0)) + ((y >> 1) << 8)
      水平：600→256 的縮放映射
      垂直：y/2 → 240 的映射（每行輸出 2 行）

步驟 2: RGB → YIQ 查表轉換
    ─ 16M 項的預計算查表（yiq_y[], yiq_i[], yiq_q[]）
    ─ 直接用 RGB 24-bit 值當 index

步驟 3: 掃描線暗化
    ─ 奇數行（y & 1 == 1）的 Y 通道乘以 0.85
    ─ rates[] 預計算查表：rates[i] = (byte)(i * 0.85f)
    ─ 效果：每隔一行壓暗 15%，產生明暗交替條紋

步驟 4: 4:1:1 色度降取樣
    ─ 每 4 個像素才更新一次 I 和 Q 值
    ─ 模擬 NTSC 的色度頻寬限制

步驟 5: YIQ → RGB 查表轉回
    ─ 16M 項的預計算查表（toRGB[]）

步驟 6: 3-tap 水平模糊
    ─ 公式：output = (左+右)×2 + 中×4) / 8
    ─ 加權平均：中心像素 50%，左右各 25%
    ─ 模擬類比訊號的水平頻寬限制
```

### 掃描線渲染流程圖

```
Render_scanline_2x:
  ScreenBuf1x (256×240)
      ↓ indexTable1x 查表 + YIQ 掃描線 + 水平模糊
  output (600×480) → GDI blit

Render_scanline_4x:
  ScreenBuf1x (256×240)
      ↓ HS_XBRz.ScaleImage2X
  _output_tmp (512×480)
      ↓ indexTable2x 查表 + YIQ 掃描線 + 水平模糊
  _output (1196×960) → GDI blit

Render_scanline_6x:
  ScreenBuf1x (256×240)
      ↓ HS_XBRz.ScaleImage3X
  _output_tmp (768×720)
      ↓ indexTable3x 查表 + YIQ 掃描線 + 水平模糊
  _output (1792×1440) → GDI blit
```

---

## 相關檔案

| 檔案 | 說明 |
|------|------|
| `AprNes/UI/AprNesUI.cs` | `initUIsize()` — 視窗/panel 大小計算，RenderObj 載入 |
| `AprNes/UI/AprNes_ConfigureUI.cs` | UI radio button → `ScreenSize` / `filter` 設定 |
| `AprNes/tool/InterfaceGraphic.cs` | 所有 `Render_*` 類別定義 |
| `AprNes/tool/LibScanline.cs` | `LibScanline` — indexTable 預計算 + 掃描線渲染 |
| `AprNes/tool/xBRZ_speed.cs` | `HS_XBRz` — xBRZ 像素藝術縮放 |
| `AprNes/tool/Scalex.cs` | `ScalexTool` — Scale2x/3x（x8、x9 第二段用） |

---

## 類比模式（Analog）的差異

類比模式開啟時，上述 Screen/Scanline 設定完全不使用：
- 渲染器固定為 `Render_Analog`
- 畫面大小由 `NesCore.AnalogSize`（2x/4x/6x/8x）控制
- 視窗大小：`256 * AnalogSize × 210 * AnalogSize`
- 掃描線由 CrtScreen Stage 2 的高斯光斑模型處理（非 LibScanline）
