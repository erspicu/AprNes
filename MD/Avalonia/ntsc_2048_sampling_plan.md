# NTSC 2048-sample 升級計畫（A 策略 — Avalonia 專用）

撰寫日期：2026-04-26
分支：`feature/ntsc-2048-sampling`
狀態：**規劃中（尚未動工）**

---

## 1. 背景與動機

### 1.1 目前 NTSC pipeline 的取樣率

NES NTSC 訊號模擬目前**固定在 1024 sample/scanline**：

```
kOutW    = 1024   (visible-line 樣本數)
kSampDot = 4      (samples per NES dot)
kWaveLen = 256 × 4 = 1024
```

換算物理意義：**每個 NES master clock tick 採一個 sample**（NES 一條 visible line = 1024 master cycles）。等於 6× Fsc（color subcarrier）oversampling，這是 NES NTSC 訊號的 native rate。

### 1.2 為什麼想升 2048

| 面向 | 1024（目前）| 2048（計畫）|
|---|---|---|
| Fsc oversampling | 6× | **12×** |
| Per-dot subsamples | 4 | **8** |
| Chroma demod 精度 | 標準 | 高頻 detail 多 50%+ |
| RF carrier modulation 細節 | 可解但 alias 風險 | 完全 over-sampled |
| linearBuffer 記憶體 | 2.88 MB | 5.76 MB |
| Ultra path 計算量 | 每 line 1024 sample | 每 line 2048 sample（~2× CPU） |
| GPU shader 取樣負擔 | 1024-wide texture | 2048-wide texture（GPU 完全不費力） |

**主要動機**：
- Avalonia 走 GPU CRT，emu thread 卸載大部分 CRT 後處理（Phase 5.1）
- emu thread 還剩的 NTSC 解調是 CPU + Parallel.For，可吃多一倍工作
- 更高採樣率讓 NTSC 訊號 simulation 更「類比」，特別是 RF noise/herring/chroma fringing

### 1.3 為什麼僅針對 Avalonia

NetFx 跟 Avalonia 共用 NesCore 程式碼（`NesCore/NTSC_CRT/Ntsc.cs` 等）。但兩邊性能特性差異大：

| 面向 | NetFx | Avalonia |
|---|---|---|
| CRT pipeline 後端 | Scalar / SIMD（CPU） | GPU SkSL（render-thread） |
| emu thread 負擔 | NTSC + CRT 全包 | NTSC 為主（CRT 在 render thread / GPU） |
| 性能餘裕 | 已被 CRT 吃掉 | 還有空間 |
| 升 2× NTSC 影響 | NetFx FPS 可能會掉 | Avalonia 幾乎沒影響 |

所以：**透過 build symbol 條件編譯**，NetFx 維持 1024，Avalonia 升 2048。NetFx 使用者完全不受影響。

---

## 2. Sampling-rate-dependent 常數盤點

以下是 **`Ntsc.cs` 內所有跟 sampling rate 相關的常數**，2048-sample 升級時必須處理：

### 2.1 取樣結構常數

| 常數 | 目前值 | 2048 版本 | 物理意義 |
|---|---|---|---|
| `kOutW` | 1024 | **2048** | linearBuffer 寬度（visible line samples） |
| `kSampDot` | 4 | **8** | samples per NES dot |
| `kWaveLen` | 1024 | **2048** | = kDots × kSampDot |
| `kBufLen` | kLeadPad×2 + 1024 = 1084 | kLeadPad×2 + 2048 = 2108 | wave buffer 含 padding |
| `kLeadPad` | 30 | 30 或 60 | 邊界 padding（看是否要等比例放大）|
| `kPlane` | 1024 × 240 | **2048 × 240** | linearBuffer 單 plane size |

### 2.2 Phase tables（cosTab6/sinTab6）

關鍵：**6-entry 是因為 NES master clock = 6 × Fsc**，每個 master tick 對應 60° subcarrier phase。

```csharp
cosTab6[k] = cos(k × 2π/6)   // k = 0..5
```

升 2048（每 master tick 採 2 個 sample）後：
- 一個 sample 對應 30° phase（= 半個 master tick）
- 需要 **12-entry** `cosTab12`/`sinTab12`，indexed by `cos(k × 2π/12)`
- 所有「phase += N mod 6」要改成「phase += M mod 12」

**phase 增量數字校準** — 目前 RunWaveformLoop 每個 dot 用 `tMod += 4 + ((..) & -6)`（mod 6 + 4）。2048 版本需要 `tMod += 8 + ((..) & -12)`（mod 12 + 8）。

### 2.3 Filter window 視窗（hannY, hannI, hannQ）

```
kWinY = 6      (luma  Hann window 樣本數)
kWinI = 18     (I 通道 Hann window)
kWinQ = 54     (Q 通道 Hann window — 1953 asymmetric)
       18     (Q 通道 Hann window — 1960s symmetric)
```

這些視窗長度是用 sample 數計算的，**對應同樣的物理頻率帶寬**：
- kWinY = 6 sample = 1 Fsc cycle 的 luma 帶寬
- kWinI = 18 sample = 3 Fsc cycle 的 I 帶寬  
- kWinQ = 54 sample = 9 Fsc cycle 的 Q 帶寬

升 2048 後（2× sample/Fsc cycle），要保持同樣的物理帶寬：
- `kWinY` 6 → **12**
- `kWinI` 18 → **36**
- `kWinQ` 54 → **108** (asymmetric) / 36 (symmetric)

`ComputeHann()` 函式不用改，自動計算。

### 2.4 Combined chroma tables（combinedI / combinedQ）

```csharp
combinedI = AllocUnmanaged(6 × kWinI × sizeof(float));  // = 6 × 18 = 108 floats
combinedQ = AllocUnmanaged(6 × kWinQ × sizeof(float));  // = 6 × 54 = 324 floats
```

升 2048：
- `6 × kWinI` → **12 × kWinI** = 12 × 36 = 432 floats（4× 大小）
- `6 × kWinQ` → **12 × kWinQ** = 12 × 108 = 1296 floats（4× 大小）

預計算迴圈中的 `(ph + n) % 6` 全部要改 `% 12`。

### 2.5 IIR 濾波係數（ChromaBlur / SlewRate / RingStrength）

```csharp
ChromaBlur, SlewRate, RingStrength  // per-sample IIR coefficients
```

這些是 `y[n] = (1-α) × y[n-1] + α × x[n]` 形式的 IIR。
**3dB cutoff = α × fs**，所以 sampling rate 加倍 → α 減半才能維持同樣的 cutoff frequency。

| Profile | RF / AV / SV | 1024 值 | 2048 值（÷2）|
|---|---|---|---|
| RF | NoiseIntensity | 0.04 | 0.04（per-sample noise，本身不該縮）|
| RF | SlewRate | 0.60 | **0.30** |
| RF | ChromaBlur | 0.10 | **0.05** |
| AV | NoiseIntensity | 0.003 | 0.003 |
| AV | SlewRate | 0.80 | **0.40** |
| AV | ChromaBlur | 0.35 | **0.175** |
| SV | NoiseIntensity | 0.00 | 0.00 |
| SV | SlewRate | 0.90 | **0.45** |
| SV | ChromaBlur | 0.45 | **0.225** |
| 共用 | RingStrength | 0.30 | **0.15**（在 RunWaveformLoop 裡 ringDamp = RingStrength × 0.5）|

### 2.6 Herring / RF buzz 係數

```csharp
const float HerringRadPerDot = 1.31683f;   // herringbone phase per NES dot
```

這個是 **per dot**（不是 per sample），不受 sampling rate 影響 — **保持不變**。

不過 RunWaveformLoop 裡的「4-step lookahead」（c1/c2/c3/c4 = 4 個 sample 的 cos/sin matrix）會變成 8-step lookahead（c1..c8），每個 dot 多算 4 步。

### 2.7 Output 階段（ResampleH_Bilinear）

非 CRT 路徑會把 1024 → Crt_DstW 做 bilinear。升 2048 後：
- `if (dstW != kOutW)` 自動跟（因為 kOutW 變 2048）
- ResampleH_Bilinear 函式自身 sampling-rate agnostic — **不用改**

---

## 3. CRT 層連帶影響

### 3.1 CRT backend 4 個檔案

| 檔案 | 改動 |
|---|---|
| `CrtScreen.cs` (Scalar) | linearBuffer stride = `kOutW`，自動跟。`HBeamSpread` 等 per-source-pixel 單位的常數要 ×2（保持同樣 dot 範圍模糊）|
| `CrtScreen.Simd.cs` | 同上 |
| `CrtScreen.Gpu.cs` | `SrcW = 1024` 常數 → `kOutW`（或 #if 切換）|
| `CrtGpuRenderThread.cs` | `SrcW = 1024` → 改 2048（Avalonia-only file）|

### 3.2 SkSL shader（`crt_core_*.sksl`）

shader 透過 `uSrcSize` uniform 拿 src dims，原則上 source-agnostic。需要驗證的點：
- `sampleHCatmullRom` / `sampleHMitchell` 4-tap helper：每 tap 距離 ±1 source pixel — source 變密 2× → **取樣半徑變一半（in dst-space）** → 細節保留更好。**理論上這對 2048 source 是好事**，不用改。
- `sampleWithBlur` 3-tap blur 的 ±1 offset 也是 per source pixel — 2048 source 下變更細的 blur，沒問題。
- Convergence offset：`uConvergence × relX × (uSrcSize.x / uDstSize.x)` — 已經是 ratio-based，自動跟。✓
- HBlur uniform：`uHBlurAlpha = HBeamSpread × 0.5f` 是「每 source pixel 的權重」— **C# 要把 `HBeamSpread × 2` 才能保持同樣 dot 範圍**（since source pixel 變半個 dot 寬度）。

### 3.3 NetFx 用的 CRT backend (Scalar / SIMD)

NetFx 不走 GPU shader，CRT 後處理在 CPU。如果 NetFx 的 kOutW 維持 1024，這兩個 backend 也要維持 1024 對應的常數。**透過 #if 條件編譯切兩套**。

---

## 4. 條件編譯設計

### 4.1 Build symbol

在 `AprNesAvalonia.csproj` 的 `<DefineConstants>` 加上：

```xml
<DefineConstants>$(DefineConstants);CRT_SIMD_AVAILABLE;CRT_GPU_AVAILABLE;HD_NTSC</DefineConstants>
```

NetFx (`AprNes.csproj`) 不定義 `HD_NTSC`。

### 4.2 Ntsc.cs 條件常數

```csharp
#if HD_NTSC
public const int kOutW = 2048;
const int kSampDot = 8;
const int kWinY = 12, kWinY_half = kWinY / 2;
const int kWinI = 36, kWinI_half = kWinI / 2;
const int kWinQ = 108, kWinQ_half = kWinQ / 2;
const int kPhaseEntries = 12;          // cosTab12 / sinTab12
const int kPhaseStep    = 8;           // tMod += 8 mod 12 per dot
const float kSampleRateScale = 0.5f;   // halve IIR coefficients
#else
public const int kOutW = 1024;
const int kSampDot = 4;
const int kWinY = 6, kWinY_half = kWinY / 2;
const int kWinI = 18, kWinI_half = kWinI / 2;
const int kWinQ = 54, kWinQ_half = kWinQ / 2;
const int kPhaseEntries = 6;
const int kPhaseStep    = 4;
const float kSampleRateScale = 1.0f;
#endif
```

`kSampleRateScale` 用於乘到 IIR 係數上：
```csharp
ChromaBlur = profileChromaBlur * kSampleRateScale;
SlewRate   = profileSlewRate   * kSampleRateScale;
ringDamp   = RingStrength * 0.5f * kSampleRateScale;
```

### 4.3 Phase tables 動態大小

```csharp
cosTab6 → cosTabPhase
sinTab6 → sinTabPhase

// 分配大小：kPhaseEntries × sizeof(float)
// Init 迴圈：for (int k = 0; k < kPhaseEntries; k++)
//             cosTabPhase[k] = cos(k × 2π / kPhaseEntries);
```

所有 `% 6` 改成 `% kPhaseEntries`。

### 4.4 phase 增量數字

```csharp
// scanPhaseBase += 2 + (((3 - scanPhaseBase) >> 31) & -6);
// → scanPhaseBase += kPhaseStepLine + (((kPhaseEntries/2 - 1 - scanPhaseBase) >> 31) & -kPhaseEntries);
```

需要重新算 per-line phase 增量、per-dot 增量等等。實際數字要驗證。

---

## 5. CRT 後端 conditional uniforms

`uHBlurAlpha`、`Convergence`、其他「per source pixel」單位的 uniform，C# 端要根據 `HD_NTSC` 切：

```csharp
#if HD_NTSC
uniforms["uHBlurAlpha"] = HBeamSpread * 0.5f * 2.0f;   // ×2 for 2× source density
#else
uniforms["uHBlurAlpha"] = HBeamSpread * 0.5f;
#endif
```

或在 `HBeamSpread` setter 內部做 scaling。

---

## 6. 實作 Phase 規劃

### Phase 1 — 基礎設施（半天）
- [ ] `HD_NTSC` build symbol 加進 Avalonia csproj
- [ ] Ntsc.cs 加 `#if HD_NTSC`/`#else` 區塊定義所有 sampling-rate-dependent 常數
- [ ] 改名 `cosTab6`/`sinTab6` → `cosTabPhase`/`sinTabPhase`，所有 hardcoded 6 改用 `kPhaseEntries`
- [ ] 改 `Ntsc_Init` 的 phase table init 迴圈（`for k = 0 to kPhaseEntries`）
- [ ] 改 `combinedI`/`combinedQ` 預計算迴圈（`% kPhaseEntries`）
- **驗證**：NetFx 編譯通過、行為完全不變（kOutW=1024 path 沒被 break）

### Phase 2 — Phase 增量校準（半天）
- [ ] `scanPhaseBase` per-line 增量：值校準
- [ ] `scanPhase6` per-line 增量（DecodeAV_Composite / DecodeScanline_Fast）
- [ ] RunWaveformLoop 內 `tMod += 4 + ...` 改用條件 macro
- [ ] DemodulateRow_Core 內 `tModI += 1 + ...`、`tModQ += 4 + ...` 校準
- **驗證**：Avalonia 編譯、跑分析模式有畫面（內容可能還沒對，但 pipeline 不 crash）

### Phase 3 — IIR 係數 scaling（1 hour）
- [ ] `Ntsc_ApplyProfile` 內 `ChromaBlur`/`SlewRate` 乘 `kSampleRateScale`
- [ ] `RunWaveformLoop` / `Ultra GenerateWaveform_SVideo` 內 `ringDamp` 乘 `kSampleRateScale`
- **驗證**：類比 RF/AV/SV 三種 profile 視覺感覺跟 1024 版接近（cutoff frequency 相同）

### Phase 4 — Ultra path RunWaveformLoop 8-step lookahead（半天）
- [ ] `c1..c4` herring rotation 擴張為 `c1..c8`
- [ ] 每個 dot 的內 loop 從 4-step 變 8-step
- [ ] 對應 `wdst[s]` `cdst[s]` 寫入索引擴張
- [ ] `waveTable`/`cTable` 大小變 `64 × kPhaseEntries × kSampDot` = 64 × 12 × 8 = 6144 floats（vs 1536）
- **驗證**：Ultra 模式視覺正常、無 stripe artifact

### Phase 5 — CRT 後端 + shader uniforms（半天）
- [ ] `CrtScreen.Gpu.cs` SrcW = `kOutW`（or 條件編譯）
- [ ] `CrtGpuRenderThread.cs` SrcW = 2048（Avalonia file，直接改）
- [ ] `_inputBitmap` size 跟 SrcW
- [ ] Stage 1 quantize 迴圈跟 SrcW
- [ ] uHBlurAlpha 條件編譯 ×2
- **驗證**：GPU CRT 視覺對齊 1024 版本

### Phase 6 — 驗證與 benchmark
- [ ] 4 種組合視覺檢查：digital / 非Ultra+CRT / Ultra+CRT / non-CRT analog
- [ ] 4 種 profile 視覺檢查：RF / AV / SV / + UltraOff
- [ ] benchmark 比 1024 vs 2048 emu/render FPS
- [ ] AC test 全部通過（NTSC 升級不應該影響 emu logic）
- [ ] 關鍵 test ROM 截圖比較（RF herring 模式應該細節更清楚）
- [ ] NetFx 編譯 + 跑 benchmark + 184/184 blargg 確保沒受影響

---

## 7. 風險登記

| 風險 | 嚴重度 | 緩解 |
|---|---|---|
| Phase table phase 增量數字算錯 → chroma 完全跑掉變紫紅或灰 | 高 | 先做 Phase 1（基礎設施）+ 跑 1024 path 確認沒 break；Phase 2 phase 增量逐個 commit + 視覺檢查 |
| IIR 係數 ÷2 之後感覺偏軟（過 smooth） | 中 | profile 預設值可微調；保留 1024 baseline 比對 |
| Ultra path 8-step lookahead 改錯 → vertical stripe artifact | 中 | 先做非 Ultra path（Phase 1-3），Ultra 留 Phase 4 |
| `combinedI`/`combinedQ` 4× 大小拖慢 cache | 低 | 432 + 1296 floats = 6.7 KB 還是 L1 範圍 |
| NetFx 不小心被影響 | 高 | 每個 phase 結尾驗 NetFx：編譯 + 184 blargg + benchmark |
| `kSampDot = 8` 後 RunWaveformLoop / DemodulateRow_SVideo 暴力攤平 4-step 變 8-step → 程式碼維護性下降 | 低 | 加註解；可能可以改用 vector loop 取代攤平 |

---

## 8. 預估工程量

- Phase 1：半天
- Phase 2：半天
- Phase 3：1 hour
- Phase 4：半天
- Phase 5：半天
- Phase 6：半天驗證

**總計 2-3 人天**，假設沒卡到 phase 增量數字校準的問題。如果視覺對不起來，phase 2 可能會多花 0.5-1 天 debug。

---

## 9. 可交付物

完成後：
- **Avalonia 視覺收益**：類比模式（特別 Ultra + RF mode + herringbone）細節提升、chroma fringing 更接近真實 NTSC 訊號特性
- **NetFx 完全不受影響**：仍跑 1024 path，性能/視覺完全相同
- **可逆**：拿掉 `HD_NTSC` define，Avalonia 退回 1024 行為
- **文件記錄**：本文件 + 實作 commit history

---

## 10. 不在這次 scope 內的東西

- 升 4096 / 更高（GPU 不會更好看，記憶體跟 cache 會壞）
- 動態切換 1024 ↔ 2048（runtime 切沒意義，build-time 決定）
- 把 NTSC 也搬到 GPU shader（一個更大的工程，需要 shader 重寫）
- 改變 NES master clock 換算（不是 sampling rate 議題）

---

## 11. 動工前確認

執行此 plan 之前：
1. 確認 `master` 上目前 1024 path 視覺基準（截圖留證）
2. 跑一輪 184 blargg + 138 AC v2 確認綠（這次不該影響邏輯，但留個 baseline）
3. benchmark 1024 path 的 emu/render FPS（4 種組合各一）

完成後比對：視覺品質 ↑、NetFx 完全不變、Avalonia FPS 略降（幅度應該 < 10% 在 GPU 模式）。
