# Ntsc.cs / CrtScreen.cs 新舊版差異分析

**日期**: 2026-03-23
**舊檔**: `NesCore/Ntsc.cs`, `NesCore/CrtScreen.cs`
**新檔**: `NesCore/NTSC_CRT/Ntsc.cs`, `NesCore/NTSC_CRT/CrtScreen.cs`

---

## 一、Ntsc.cs 差異

### 結論：邏輯完全相同，僅格式差異

| 項目 | 舊版 | 新版 | 影響 |
|------|------|------|------|
| 註解 | 詳細優化歷程文檔 | 精簡標題 | 無 |
| `using` | 含 `System.Threading.Tasks` | 移除（未使用） | 無 |
| 程式碼排版 | 較鬆散 | 更緊湊 | 無 |
| 所有核心演算法 | — | 完全相同 | 無 |

詳細確認一致的函式列表：
- `Init()`: loLevels/hiLevels, iPhase/qPhase, cosTab6/sinTab6, hannY/I/Q, combinedI/Q, attenTab, yBase/iBase/qBase, waveTable/cTable, emphAtten, yBaseE/iBaseE/qBaseE — 全部一致
- `ApplyConfig()`, `ApplyProfile()`, `UpdateColorTemp()`, `UpdateGammaLUT()` — 一致
- `DecodeScanline()`, `DecodeScanline_Fast()`, `GenerateSignal()` — 一致
- `DecodeAV_Composite()`, `RunDecodeLoop()`, `DecodeAV_SVideo()` — 一致
- `DecodeScanline_Physical()`, `GenerateWaveform()`, `RunWaveformLoop()` — 一致
- `GenerateWaveform_SVideo()`, `RunWaveformLoop_SVideo()` — 一致
- `DemodulateRow()`, `DemodulateRow_SVideo()` — 一致
- `YiqToRgb()` — 一致
- 所有常數、SIMD 向量、公開參數 — 一致

---

## 二、CrtScreen.cs 差異

### 2.1 架構重構：後處理融合 (Fused Post-Processing)

**舊版架構**（多次全幀遍歷）：
```
Parallel.For(scanline rendering)   ← Pass 1: 生成 BGRA 像素
↓
ApplyShadowMaskAndPhosphor()       ← Pass 2: 全幀蔭罩 + 磷光衰減
↓
ApplyConvergenceAndCurvature()     ← Pass 3: 全幀色散 + 曲率
```

**新版架構**（Per-Row 融合）：
```
Parallel.For(ty => {
    1. 生成 scanline BGRA 像素
    2. ProcessRowMask/Phosphor_SWAR()   ← 同一行內蔭罩 + 磷光
    3. ProcessRowConvergence()          ← 同一行內色散（無曲率時）
})
↓
ApplyFullFrameCurvatureAndConvergence() ← 僅曲率開啟時需全幀 pass
```

**優點**：減少記憶體流量（N×全幀讀寫 → 1×全幀），對高解析度（6x/8x）效能提升顯著。
**效能實測**：8x 從 74.03 → 82.01 FPS (+10.8%)，6x 從 81.81 → 84.39 FPS (+3.2%)。

### 2.2 演算法微優化

| 函式 | 優化手法 | 說明 |
|------|----------|------|
| `PrecomputeScanlineWeights` | Loop-Invariant Code Motion | 預計算 `invDstH`, `scaleY`, `jitterOffset`, `vs4`, `maxNy`；除法→乘法 |
| `PrecomputeScanlineWeights` | FMA-friendly | `ty * scaleY + jitterOffset` 取代 `((float)ty + jitter) / dstH * SrcH` |
| `PrecomputeScanlineWeights` | Branchless clamp | `Math.Max(0, Math.Min(..., maxNy))` 取代 `if (ny >= SrcH)` |
| `PrecomputeCurvature` | Branchless 2D bounds | 位元運算 `outMask = (sx>>31) \| ((maxW-sx)>>31) \| ...` 取代 4 路 if |
| `PrecomputeCurvature` | 代數展開 | `cx * (f * maxW) + baseW` 取代 `(0.5f + cx * f) * (dstW - 1) + 0.5f` |
| `ApplyHorizontalBlur` | Loop Peeling | 主迴圈剝離最後一個像素，消除 `x+1 < SrcW` 邊界檢查 |
| `ApplyHorizontalBlur` | 代數簡化 | 尾端像素 `prev * alpha + lastCur * (center + alpha)` |
| `Render` scalar path | `Math.Max/Min` | 取代手動 `if (r < 0f) r = 0f; else if (r > 1f) r = 1f` |
| `ProcessRowConvergence` | FMA + 常數外提 | `tx * step + baseOffset - 1024` 取代 per-pixel 浮點除法 |

### 2.3 新增 Per-Row 處理函式

| 函式 | 功能 | 對應舊版 |
|------|------|----------|
| `ProcessRowMask_SWAR` | 單行蔭罩 (SWAR) | `ApplyShadowMask()` |
| `ProcessRowMaskPhosphor_SWAR` | 單行蔭罩+磷光 (SWAR) | `ApplyShadowMaskAndPhosphor()` |
| `ProcessRowPhosphor_SWAR` | 單行磷光 (SWAR) | `ApplyPhosphorPersistence()` |
| `ProcessRowConvergence` | 單行色散 | `ApplyBeamConvergence()` |
| `ApplyFullFrameCurvatureAndConvergence` | 曲率+色散全幀 | `ApplyConvergenceAndCurvature()` |

### 2.4 移除的舊函式

以下函式在新版中被 per-row 融合取代，不再存在：
- `ApplyShadowMask()`
- `ApplyShadowMaskAndPhosphor()`
- `ApplyPhosphorPersistence()`
- `ApplyBeamConvergence()`
- `ApplyCurvature()`
- `ApplyConvergenceAndCurvature()`
- `MaskPhosphorPixel()`（inline helper）

---

## 三、顏色 Bug 分析

### 3.1 問題定位

**根因**：`ProcessRowMask_SWAR` 和 `ProcessRowMaskPhosphor_SWAR` 中的 SWAR 蔭罩乘法使用了 **異質乘數打包**（heterogeneous packed multiplier），導致跨通道交叉乘積污染。

### 3.2 Bug 機制

新版預計算蔭罩乘數，將 R/B 通道的不同縮放因子打包成一個 `uint`：

```csharp
// Phase 0: R 保持(×256), B 衰減(×udim)
maskRB[0] = (256u << 16) | udim;   // = 0x010000XX
```

然後用 SWAR 乘法一次處理 R+B：

```csharp
((px & 0x00FF00FFu) * mRB0) >> 8) & 0x00FF00FFu
```

#### 為什麼這是錯的

SWAR (SIMD Within A Register) 乘法只在 **均勻乘數** 時正確：

```
0x00RR00BB × scale = 0x00(RR×scale)00(BB×scale)   ← ✓ 無溢出
```

用 **異質乘數** `0x0100_00C0` 時，產生交叉項：

```
0x00RR00BB × 0x010000C0
= RR × 0x01000000    ← 溢出 32 位元，丟失！
+ RR × 0x000000C0    ← 汙染 R 結果通道
+ BB × 0x01000000    ← 汙染 R 結果通道
+ BB × 0x000000C0    ← B 結果正確
```

#### 數值驗證

R=0x80, B=0x40, udim=0xC0 時：

| | 期望值 | 實際值 | 誤差 |
|---|--------|--------|------|
| R（保持） | 128 (0x80) | **160 (0xA0)** | +32 (+25%) |
| B（衰減） | 48 (0x30) | 48 (0x30) | 0 |

R 通道被 B×scaleR 的交叉項汙染，導致 **R 值偏高、顏色偏紅/偏亮**。

### 3.3 影響範圍

- 預設 `ShadowMaskMode = ApertureGrille`、`ShadowMaskStrength = 0.3f` → **預設開啟**
- 影響所有三種端子模式 (RF/AV/SVideo)
- 影響 `ProcessRowMask_SWAR`（純蔭罩）和 `ProcessRowMaskPhosphor_SWAR`（蔭罩+磷光）
- `ProcessRowPhosphor_SWAR` 不受影響（使用均勻乘數 udec）

### 3.4 修復方案

將蔭罩衰減改回 **逐通道純量運算**（與舊版相同），保留 per-row 融合架構：

```csharp
// Phase 0: keep R, dim G+B — 正確的逐通道寫法
uint px = row[tx];
uint r = (px >> 16) & 0xFFu;                    // R 不變
uint g = ((px >> 8 & 0xFFu) * udim) >> 8;       // G 衰減
uint b = ((px & 0xFFu) * udim) >> 8;            // B 衰減
row[tx] = 0xFF000000u | (r << 16) | (g << 8) | b;
```

磷光 SWAR 衰減（均勻 udec）維持不變，因其數學正確。

---

## 四、效能影響總結

| 解析度 | 舊版 FPS | 新版 FPS | 變化 | 原因 |
|--------|----------|----------|------|------|
| 2x (512×420) | 127.80 | 120.42 | -5.8% | 低解析度下融合架構的 overhead 反而較大 |
| 4x (1024×840) | 113.91 | 111.17 | -2.4% | 接近持平 |
| 6x (1536×1260) | 81.81 | 84.39 | **+3.2%** | 記憶體流量減少開始顯現效益 |
| 8x (2048×1680) | 74.03 | 82.01 | **+10.8%** | 大幅受益於融合架構 |

> 修復蔭罩 Bug 不影響效能（逐通道純量 vs SWAR 在總管線中佔比極小）。
