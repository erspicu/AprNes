# 全螢幕類比模式實作計畫

> 日期：2026-03-22

---

## 一、目標

在類比模擬模式下提供全螢幕顯示，維持正確比例並最大化可視面積，根據使用者螢幕的實際解析度與比例動態計算顯示尺寸。僅在類比模式開啟時岔分全螢幕邏輯，非類比模式維持原有行為不動。

---

## 二、適用範圍

- **類比模式開啟時**：進入全螢幕走新邏輯（本文件描述）
- **類比模式關閉時**：維持原有全螢幕行為，不做任何改動

岔分點在 `fullScreeenToolStripMenuItem_Click` 內：

```csharp
if (NesCore.AnalogEnabled)
{
    // 走新的類比全螢幕邏輯（本文件）
    EnterAnalogFullScreen();
}
else
{
    // 維持原有行為不動
    // ...existing code...
}
```

---

## 三、顯示區域動態計算

### 3.1 內容寬高比（Content Aspect Ratio）

NES NTSC 的 pixel aspect ratio 為 **8:7**（NESdev 標準值），類比模式可見區域為 256×210：

```
contentDisplayW = 256 × (8/7) ≈ 292.57
contentDisplayH = 210
contentAR = 292.57 / 210 ≈ 1.3933
```

### 3.2 動態計算公式

螢幕解析度由硬體即時取得（`Screen.PrimaryScreen.Bounds`），不寫死任何數值。
此 API 回傳的是邏輯像素（已含 Windows DPI 縮放），直接使用即可，Windows 會免費 upscale 到實體像素。

```csharp
// 動態取得螢幕邏輯解析度
int screenW = Screen.PrimaryScreen.Bounds.Width;
int screenH = Screen.PrimaryScreen.Bounds.Height;

// NES 類比內容寬高比（8:7 PAR）
const double contentAR = (256.0 * 8.0 / 7.0) / 210.0;  // ≈ 1.3933

double screenAR = (double)screenW / screenH;

int displayW, displayH;
if (screenAR > contentAR)
{
    // 螢幕比內容更寬 → 以高度為基準，左右留黑邊（pillarbox）
    displayH = screenH;
    displayW = (int)(screenH * contentAR);
}
else
{
    // 螢幕比內容更窄或相同 → 以寬度為基準，上下留黑邊（letterbox）
    displayW = screenW;
    displayH = (int)(screenW / contentAR);
}

// 黑邊位置
int padX = (screenW - displayW) / 2;
int padY = (screenH - displayH) / 2;
```

### 3.3 各種螢幕預期結果

| 螢幕 | 解析度 | screenAR | 比較 contentAR | displayW × displayH | 黑邊方向 | 黑邊各 |
|------|--------|:--------:|:--------------:|---------------------|:--------:|:------:|
| 16:9 | 1920×1080 | 1.778 | > 1.393 | 1504×1080 | 左右 | 208 |
| 16:9 | 2560×1440 | 1.778 | > 1.393 | 2005×1440 | 左右 | 278 |
| 16:9 4K | 3840×2160 | 1.778 | > 1.393 | 3008×2160 | 左右 | 416 |
| 16:10 | 1920×1200 | 1.600 | > 1.393 | 1672×1200 | 左右 | 124 |
| 21:9 | 3440×1440 | 2.389 | > 1.393 | 2005×1440 | 左右 | 718 |
| 4:3 | 1600×1200 | 1.333 | < 1.393 | 1600×1148 | 上下 | 26 |

> DPI 150% 下的 16:9 4K（3840×2160 實體）→ Screen.Bounds 回報 2560×1440，計算 displayW=2005×1440，Windows 自動放大 150% 輸出。

---

## 四、進入/退出全螢幕的狀態管理

### 4.1 進入全螢幕前保存狀態

```csharp
// 保存進入全螢幕前的狀態，退出時還原
int savedPanelW, savedPanelH;
int savedPanelX, savedPanelY;
int savedFormW, savedFormH;
FormBorderStyle savedBorderStyle;

void EnterAnalogFullScreen()
{
    // 保存原始狀態
    savedPanelW = panel1.Width;
    savedPanelH = panel1.Height;
    savedPanelX = panel1.Left;
    savedPanelY = panel1.Top;
    savedFormW  = this.Width;
    savedFormH  = this.Height;
    savedBorderStyle = this.FormBorderStyle;

    // 動態計算 displayW × displayH（§3.2 公式）
    // ...

    // 進入全螢幕
    this.BackColor = Color.Black;
    this.FormBorderStyle = FormBorderStyle.None;
    this.WindowState = FormWindowState.Maximized;
    panel1.Size = new Size(displayW, displayH);
    panel1.Location = new Point(padX, padY);

    // 重建 Graphics + RenderObj
    grfx?.Dispose();
    grfx = panel1.CreateGraphics();
    unsafe { if (RenderObj != null) RenderObj.init(NesCore.ScreenBuf1x, grfx); }
}
```

### 4.2 退出全螢幕還原狀態

```csharp
void ExitAnalogFullScreen()
{
    this.BackColor = SystemColors.Menu;
    this.WindowState = FormWindowState.Normal;
    this.FormBorderStyle = savedBorderStyle;

    // 還原原始尺寸和位置
    this.Width  = savedFormW;
    this.Height = savedFormH;
    panel1.Size = new Size(savedPanelW, savedPanelH);
    panel1.Location = new Point(savedPanelX, savedPanelY);

    // 重建 Graphics + RenderObj（panel 尺寸改變後必要）
    grfx?.Dispose();
    grfx = panel1.CreateGraphics();
    unsafe { if (RenderObj != null) RenderObj.init(NesCore.ScreenBuf1x, grfx); }

    // 恢復 UI 控件可見性
    UIAbout.Visible = RomInf.Visible = UIOpenRom.Visible =
        UIReset.Visible = UIConfig.Visible = label3.Visible = true;
}
```

### 4.3 退出路徑

兩個退出全螢幕的入口（`fun8ToolStripMenuItem_Click`、`normalToolStripMenuItem_Click`）都需要判斷：

```csharp
if (NesCore.AnalogEnabled && ScreenCenterFull)
    ExitAnalogFullScreen();
else
    // 原有退出邏輯
```

---

## 五、三種類比子 Case

### Case A：Analog（非 Ultra）

```
palette index → per-dot YIQ 查表 → RGB → 輸出
```

- 全螢幕下：panel1 直接設為 displayW × displayH
- 內部渲染：需要把 AnalogSize 調整為符合 displayW/displayH 的值，或直接以 displayW/displayH 為渲染目標
- Upscale：nearest-neighbor 即可，因為本身是逐 dot 色塊
- 效能壓力最小

### Case B：Ultra Analog，無 CRT

```
1024 samples → FIR demodulate → YIQ→RGB (uint ARGB) → upscale → 顯示
```

- 信號層固定 1024 寬 × 210 行（物理正確）
- Upscale 到 displayW × displayH
- 沒有 CRT 後處理遮瑕，upscale 品質重要（Lanczos-3 或 bilinear）
- 垂直方向同樣需要品質插值（210 → displayH）

### Case C：Ultra Analog + CRT（最重要）

```
1024 samples → FIR demodulate → linearBuffer (float RGB, 1024寬)
  → float 域 upscale 到 displayW × displayH
  → CRT 後處理在 displayW × displayH 解析度
  → tone mapping / gamma → uint ARGB → 1:1 輸出到螢幕
```

**關鍵改動：CRT 處理順序反轉**

```
現在的流程：1024 → CRT(1024寬) → nearest-neighbor 放大到 dstW
正確的流程：1024 → float upscale 到 displayW → CRT(displayW × displayH)
```

---

## 六、物理正確性論述

每一步設計決策都有對應的物理依據：

### 6.1 信號層固定 1024 samples

NES 2C02 PPU 的 master clock 為 21.477272 MHz，PPU dot clock 為 master clock ÷ 4 = 5.369 MHz。每個 PPU dot 對應 4 個 master clock cycle，可見區域 256 dots × 4 = **1024 samples**。

這是硬體產生 composite 訊號的實際取樣率。超過 1024 只是 oversampling——PPU 每個 dot 只輸出一個 palette color，真正的電壓轉換發生在 4 samples/dot 的邊界，更高的取樣不會增加信號資訊量。

### 6.2 先 upscale 再 CRT，不是先 CRT 再放大

真實世界的因果順序：

1. NTSC 訊號（對應 1024 取樣）離開電視的解調電路
2. 解調後的 RGB 訊號驅動 CRT 電子槍
3. 電子束打到螢幕的 phosphor 層，產生可見光
4. CRT 螢幕有自己的物理特性（phosphor pitch、spot size、scanline 間隙）

CRT 的視覺效果——bloom 擴散、scanline gap、phosphor 顆粒——是發生在**螢幕的物理像素層級**，不是發生在信號層。因此模擬時 CRT 效果必須在「顯示解析度」下運算才物理正確。

如果在 1024 寬度做完 CRT 再放大：

| CRT 效果 | 在 1024 做再放大（錯誤） | 在顯示解析度做（正確） |
|----------|------------------------|---------------------|
| Scanline gap | 放大後變糊，間距不均勻 | 像素級精確控制 |
| Bloom | 半徑被等比放大，過度膨脹 | 正確的物理擴散半徑 |
| Phosphor | 顆粒被拉大，失真 | 對應實際顯示像素 |
| Vignette | 差異不大 | 差異不大 |

### 6.3 float 域（linear RGB）做 upscale

光的混合在物理上是線性的。人眼感知的亮度混合、CRT phosphor 的光學疊加都發生在 linear 空間。

如果在 gamma-compressed（sRGB）空間做插值，等於在非線性空間執行線性運算：
- 暗部會偏暗（gamma 壓縮後暗部值被壓縮，插值結果偏低）
- 亮部過渡不自然

正確做法：在 linear float RGB 空間完成插值，CRT 處理完後再做 gamma compression。這是 physically-based rendering (PBR) 的標準做法。

### 6.4 垂直方向品質插值（非行複製）

真實 CRT 的電子束具有 Gaussian spot profile——scanline 之間的亮度是連續衰減的，不是硬切。目前的 `Buffer.MemoryCopy`（行複製）等於假設電子束寬度為零的理想線，不符合物理現實。

品質插值（bilinear / Lanczos）能模擬 spot profile 的自然過渡，配合 CRT 後處理的 scanline gap 效果，產生更真實的視覺結果。

### 6.5 內容寬高比 8:7 PAR

NTSC NES 的 pixel aspect ratio 為 8:7（NESdev 標準值），非正方形像素。256 個 NES 像素在螢幕上的等效寬度為 256 × 8/7 ≈ 292.57 顯示單位。類比模式可見 210 行，因此 contentAR ≈ 1.3933。

此比例介於 4:3（1.333）和 16:10（1.6）之間：
- 16:9 / 16:10 / 21:9 螢幕：左右留黑邊（pillarbox）
- 4:3 螢幕：上下留黑邊（letterbox），因為 NES 8:7 PAR 內容略寬於 4:3

### 6.6 DPI 縮放

AprNes 為 non-DPI-aware 應用程式。`Screen.PrimaryScreen.Bounds` 回報邏輯像素（已除以 Windows 縮放比例）。例如 150% 下的 4K 螢幕（3840×2160 實體）回報 2560×1440 邏輯像素。

公式直接使用邏輯像素即可。Windows 會自動將應用程式輸出放大到實體像素。這意味著：
- 計算量以邏輯像素為準（150% 下計算量約為實體像素的 44%）
- 最終輸出有 Windows 級 upscale（略模糊，可接受）
- 不需要額外處理 DPI

---

## 七、流程總整理

```
                信號產生       解調/色彩          Upscale                CRT            顯示
Case A (Analog)   -           256×210 per-dot   scale→displayW×H       -              panel1(displayW×H)
Case B (Ultra)    1024×210    1024×210 (uint)   Lanczos→displayW×H     -              panel1(displayW×H)
Case C (Ultra+CRT)1024×210   1024×210 (float)  Lanczos→displayW×H(f)  displayW×H     panel1(displayW×H) 1:1
```

**一句話總結**：信號在信號的解析度處理，螢幕效果在螢幕的解析度處理，色彩混合在線性空間處理。每一步都對應真實世界中事情發生的位置和方式。

---

## 八、實作優先順序

1. **Case C（Ultra+CRT）** — 視覺品質最高、最多人會用的組合，也是改動最核心的部分（upscale/CRT 順序反轉 + float 域插值）
2. **Case B（Ultra）** — Case C 的 float upscale 寫好後，Case B 只是跳過 CRT 步驟，共用同一個 upscale 函數
3. **Case A（Analog）** — 只是自動計算整數倍或直接以 displayW/H 為目標，改動最小

---

## 九、關鍵技術改動點

1. **全螢幕進入/退出狀態管理**：進入前保存 panel1 尺寸/位置 + Form 尺寸，退出時完整還原（§4）
2. **螢幕解析度動態取得**：`Screen.PrimaryScreen.Bounds` 即時取得，配合 contentAR 通用公式計算 displayW/displayH，適應任何螢幕比例（§3.2）
3. **新增 float 域 Lanczos/bilinear upscale 函數**：輸入 linearBuffer (1024×210 float)，輸出 displayW×displayH float buffer
4. **CRT 後處理改為在 displayW×displayH 運算**：目前 CrtScreen 以 1024 寬為基準，需改為接受動態解析度
5. **垂直插值取代行複製**：`Buffer.MemoryCopy` 重複行 → 品質插值或由 CRT 的 scanline 模擬處理
6. **grfx + RenderObj 重建**：進入/退出全螢幕時 panel1 尺寸改變，必須 Dispose + 重建 Graphics context 並重新 init RenderObj
7. **nearest-neighbor 升級**：Case B 的 `demodTmpBuf → row0` 定點 resample 改為品質插值
