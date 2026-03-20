# NTSC + CRT 效能優化研究報告

**日期**: 2026-03-20
**基準**: 60 FPS（低階電腦邊緣）
**目標**: 每個優化項目的可行性、預估收益、實作難度分析

---

## 一、當前效能概況

### 每幀工作量（Level 3 UltraAnalog）

| 函式 | 呼叫次數 | 每次工作 | 總 MACs/幀 |
|------|----------|----------|-----------|
| GenerateWaveform | 240 | 256dots×4samp + 1084 IIR | ~1.3M ops |
| DemodulateRow | 240 | 1024px × (6+18+54) MACs | ~19.2M MACs |
| WriteLinear | 240×1024 | 5 muls + 6 adds + 3 stores | ~3.1M ops |
| CrtScreen.Render | 840 rows | 1024px × SIMD(bloom+gamma) | ~5.5M ops |

**DemodulateRow 是 Level 3 最大瓶頸**：
- Q window (54-tap) 佔 55,296 MACs/scanline × 240 = **13.27M MACs/幀**
- I window (18-tap) 佔 18,432 MACs/scanline × 240 = **4.42M MACs/幀**
- 兩者合計佔全部 DemodulateRow 工作量的 94%

### Level 2 每幀工作量

| 函式 | 呼叫次數 | 每次工作 |
|------|----------|----------|
| GenerateSignal | 240 | 256 dots，分支+float ops |
| DecodeAV_Composite | 240 | 1024px，IIR + `% 6` + YiqToRgb |
| YiqToRgb | 240×1024 | 3×(3mul+2add) + 3×gamma + pack |

---

## 二、查表（LUT）優化機會

### 2-A：GenerateSignal → 64-entry YIQ 基底表【★★★ 高效益】

**問題**：目前 `GenerateSignal()` 每次 256 dots 逐一做分支判斷、lo/hi 修正、sat 計算。
**根本原因**：NES palette 只有 64 個有效顏色（`p & 0x3F`），Y/I/Q 完全由顏色決定，atten 是 per-scanline 純量。

**方案**：
```csharp
// Init() 階段一次性計算（64 entries）
static float* yBase;  // yBase[c] = (hi+lo)/2 at atten=1
static float* iBase;  // iBase[c] = iPhase[color] * sat
static float* qBase;  // qBase[c] = qPhase[color] * sat

// GenerateSignal() 退化為：
for (int d = 0; d < 256; d++) {
    int idx = palBuf[d] & 63;
    dotY[d] = yBase[idx] * atten;
    dotI[d] = iBase[idx] * atten;
    dotQ[d] = qBase[idx] * atten;
}
```
- **消除**：分支判斷（color==0, ==0xD, >0xD）、loLevels/hiLevels 查表、iPhase/qPhase 查表、sat 計算
- **節省**：~12 float ops → 3 mul + 3 load（4倍以上加速在 GenerateSignal）
- **注意**：`emphasisBits != 0` 的 `atten = Math.Pow(0.746, n)` 已預計算 8 個值（n=0..3）

---

### 2-B：GenerateWaveform → waveTable[64][6][4]【★★★ 高效益】

**問題**：Level 3 `GenerateWaveform()` 每個 dot 做 4 個 sample：
`waveBuf[idx+s] = Y + cos[tMod]*ip - sin[tMod]*qp`
三個量（Y/ip/qp）都只由 `palBuf[d] & 63` 決定，cos/sin 只有 6 相位。

**方案**：
```csharp
// 預計算 64 palette × 6 phase × 4 sample = 1536 floats（6KB，全在 L1）
static float* waveTable; // waveTable[(c*6+ph)*4 + s] = Y + cos[(ph+s)%6]*ip - sin[(ph+s)%6]*qp

// GenerateWaveform 主迴圈退化為：
for (int d = 0; d < kDots; d++) {
    float* src = waveTable + ((palBuf[d] & 63) * 6 + tMod) * 4;
    float* dst = waveBuf + kLeadPad + d * 4;
    // 4 float stores，可視需要 * atten
    dst[0] = src[0] * atten;
    dst[1] = src[1] * atten;
    dst[2] = src[2] * atten;
    dst[3] = src[3] * atten;
    tMod = (tMod == 5) ? 0 : tMod + 1;
}
```
- **消除**：per-dot 的 lo/hi 計算、atten 乘法（每4 sample → 每 dot 一次）、iPhase/qPhase 查表、cosTab6/sinTab6 查表
- **表大小**：6KB，完全在 L1 cache
- **注意**：atten ≠ 1 時需 ×atten；可另建 attenTable[64][8] 折進去但複雜度高

---

### 2-C：Gamma LUT（256 bytes）→ 替代 float gamma【★★★ 超高效益，通殺 Level 2+3+CRT】

**問題**：`YiqToRgb()` 和 `CrtScreen.Render()` 都有：
```csharp
r += 0.229f * r * (r - 1f);  // fast gamma ≈ pow(v, 1/1.13)
g += 0.229f * g * (g - 1f);  // 3 次 float 乘法 + 加法 per channel
b += 0.229f * b * (b - 1f);
```
這 3 組 float 乘法在 `YiqToRgb` 中每次都要算。

**方案**：預計算 256-byte gamma LUT，輸入 [0,255]，輸出 gamma 校正後的 byte：
```csharp
static byte* gammaLUT;  // 256 bytes

// Init():
for (int v = 0; v < 256; v++) {
    float fv = v / 255.0f;
    fv += 0.229f * fv * (fv - 1f);
    gammaLUT[v] = (byte)Math.Max(0, Math.Min(255, (int)(fv * 255.5f)));
}

// YiqToRgb 改為：
int ri = (int)(r * 255.5f); if (ri > 255) ri = 255; if (ri < 0) ri = 0;
int gi = ...;
int bi = ...;
return (uint)(gammaLUT[bi] | (gammaLUT[gi] << 8) | (gammaLUT[ri] << 16) | 0xFF000000u);
```
- **消除**：每 pixel 3 次 float mul + add → 3 byte LUT lookup
- **LUT 大小**：256 bytes（1/4 cache line，熱路徑下常駐 L1）
- **適用範圍**：Level 2 `YiqToRgb()`（240×1024 calls/幀）+ Level 3 `YiqToRgb()`（直接輸出路徑）+ `CrtScreen.Render()` scalar tail

---

### 2-D：NoiseIntensity 預縮放 LUT（256 entries）【★★ 中效益】

**問題**：噪聲轉換 `((ns & 0xFF) / 255.0f - 0.5f) * 2f * NoiseIntensity` 每個 sample 做一次 float 除法 + 乘法。

**方案**：per-scanline 建立 256-entry float table（只在 NoiseIntensity > 0 時建）：
```csharp
// 每幀一次（NoiseIntensity 是常量）
float[] noiseTab = new float[256];  // 或 stackalloc
float amp = 2f * NoiseIntensity;
for (int v = 0; v < 256; v++)
    noiseTab[v] = (v / 255.0f - 0.5f) * amp;

// 迴圈改為：
waveBuf[i] += noiseTab[ns & 0xFF];
```
- **節省**：float 除法 + 乘法 → 1 array index + float add
- **RF 才有雜訊**：S-Video NoiseIntensity=0，AV 極低，主要 RF 受益

---

### 2-E：Emphasis atten 8-entry 預計算【★ 微優化，但幾乎零成本】

**問題**：`Math.Pow(0.746, n)` 每 scanline 呼叫一次（n 只有 0..3）。

**方案**：Init() 預計算 `attenTab[4] = {1.0, 0.746, 0.556, 0.415}`，直接查表。

---

## 三、浮點 → 整數近似值

### 3-A：Gamma LUT（同 2-C，整數查表取代 float）【★★★ 已在 2-C 說明】

### 3-B：相位遞增取代 `% 6` 除法【★★★ 高效益，幾乎零成本】

**問題**：`DecodeAV_Composite()` 每 pixel：
```csharp
int ph = (phase0 + outX) % 6;  // 1024次 mod 運算
```
**方案**：整數遞增加邊界重置：
```csharp
int ph = phase0;
for (int outX = 0; outX < kOutW; outX++) {
    // ... use ph ...
    if (++ph == 6) ph = 0;  // branch prediction 友好（每6次才觸發）
}
```
- **消除**：1024 次 `% 6` 整數除法（現代 CPU 整數除法 ~20-30 cycles）→ 1 inc + 1 比較 + 偶爾 assign

同理適用於 `DemodulateRow()` 中的 `tModI` 和 `tModQ`，見第五節。

---

### 3-C：`outX * 256 / kOutW` → bitshift【★★★ 零成本改進】

**問題**：`DecodeAV_Composite()` 每 pixel：
```csharp
int d = outX * 256 / kOutW;  // kOutW=1024
```
**展開**：`outX * 256 / 1024 = outX / 4 = outX >> 2`

**方案**：
```csharp
int d = outX >> 2;
```
- **成本**：一行修改，零風險
- **節省**：整數乘法 + 除法 → 1 右移

---

### 3-D：固定點 int16 波形緩衝（高風險大改造）【★ 評估後建議低優先】

**概念**：waveBuf 目前為 float（信號範圍約 [-0.5, 1.5]）。
改為 int16 Q13（scale = 8192）：
- 範圍 [-4096, 12288]，fits in int16 [-32768, 32767]
- 記憶體：1084 × 2 bytes = 2,168 bytes（vs 現在 4,336 bytes），更好的 cache 使用率
- Vector<short> 可容納 8 元素（vs float 的 4），理論上 2× SIMD 寬度

**問題**：
- `System.Numerics.Vectors` 的 `Vector<short>` 缺乏 widening multiply（int16×int16→int32 的飽和乘法），需要手動拆分
- 整個 GenerateWaveform + SlewRate IIR + DemodulateRow 都需重寫
- 精度：Q13 = 13位小數位元 vs float 23位，對 8-bit 輸出已足夠，但 IIR 累積誤差需驗證

**結論**：.NET Framework 的 Vector<T> 缺少整數乘法擴展（需要 System.Runtime.Intrinsics），改動大但優化空間受限。建議移植到 Avalonia/.NET 10 後用 AVX2 intrinsics 實作，暫緩。

---

### 3-E：YIQ→RGB 矩陣整數化（Q12）【★★ 中效益，中複雜度】

**現況**：WriteLinear / YiqToRgb 做浮點矩陣乘法。

**方案**：輸入 Y/I/Q 在 [−1, 2] 範圍，以 Q12（×4096）表示：
```csharp
// 矩陣係數 × 4096（預計算）
// R = Y*4096 + I*4441 + Q*1444  → 右移 12 得 [0..255]（×256/4096）
// G = Y*4096 - I*1762 - Q*2272
// B = Y*4096 - I*2567 + Q*7905
int Y_i = (int)(y * 4096f);
int I_i = (int)(i * 4096f);
int Q_i = (int)(q * 4096f);
int ri = (Y_i + 4441*I_i/4096 + 1444*Q_i/4096 + 2048) >> 12;
// ...然後 clamp + gammaLUT[ri]
```
**問題**：需要先將浮點 Y/I/Q 轉成整數（1 cast per channel），再做整數矩陣乘法。如果搭配 gamma LUT，效果顯著。整體節省約 1/3 的 YiqToRgb 時間。
**建議**：搭配 2-C gamma LUT 一起實作，合而為一。

---

## 四、Filter 數學化簡

### 4-A：DemodulateRow 滑動指標（消去乘法）【★★★ 高效益，低成本】

**問題**：`DemodulateRow()` 每 pixel p：
```csharp
int center = kLeadPad + p * kWaveLen / kOutW;  // kWaveLen == kOutW == 1024
// 實際上 = kLeadPad + p（乘法可消去！）
int startY = center - kWinY_half;
int startI = center - kWinI_half;
int startQ = center - kWinQ_half;
```

由於 `kWaveLen = kOutW = 1024`，`p * kWaveLen / kOutW = p * 1 = p`。

**方案**：改為滑動指標（循環外初始化，循環內遞增）：
```csharp
float* wvY = waveBuf + kLeadPad - kWinY_half;  // 初始化
float* wvI = waveBuf + kLeadPad - kWinI_half;
float* wvQ = waveBuf + kLeadPad - kWinQ_half;

// 計算初始 tModI、tModQ（各只算一次）
int tModI = ((phase0 - kWinI_half) % 6 + 6) % 6;
int tModQ = ((phase0 - kWinQ_half) % 6 + 6) % 6;

for (int p = 0; p < kOutW; p++) {
    // Y: dot(hannY, wvY)
    float sumY = ...;

    // I: SIMD dot(combinedI[tModI], wvI)
    float* cwI = combinedI + tModI * kWinI;
    float sumI = ...;

    // Q: SIMD dot(combinedQ[tModQ], wvQ)
    float* cwQ = combinedQ + tModQ * kWinQ;
    float sumQ = ...;

    wvY++; wvI++; wvQ++;
    if (++tModI == 6) tModI = 0;
    if (++tModQ == 6) tModQ = 0;
}
```
- **消除**：每 pixel 的乘法+除法（center 計算）、雙重 `% 6`（改為遞增+重置）
- **節省**：1024 × (2 mul + 2 div + 2 double-mod) = 顯著改善

---

### 4-B：Q channel 降採樣至 dot 率（256 → 1024）【★★★ 最大潛力優化】

**核心洞察**：NES 顏色每 dot（4 個 sub-pixel sample）才改變一次。Q channel 帶寬 ≈ 0.4 MHz，遠低於 dot 率（≈ 1.07 MHz）。在 dot 率計算 Q 已足夠（仍有 2.7× 過採樣）。

**現況**：1024 pixels × 54 MACs = **55,296 MACs/scanline**
**方案**：只在每個 dot 中央（256 次）計算 Q，每次 54 MACs = **13,824 MACs/scanline**（4× 減少）

```csharp
// 預計算 256 個 Q 值，存入 dotQDemod[256]
for (int d = 0; d < 256; d++) {
    int center = kLeadPad + d * 4 + 2;  // dot 中央 sample（非 sub-pixel）
    int startQ = center - kWinQ_half;
    int tModQ  = ((phase0 + startQ - kLeadPad) % 6 + 6) % 6;
    // SIMD dot product of combinedQ[tModQ] × waveBuf[startQ..+54]
    dotQDemod[d] = ComputeQ(startQ, tModQ);
}

// DemodulateRow 主迴圈：Q 直接查表（不再重算）
for (int p = 0; p < kOutW; p++) {
    int d = p >> 2;  // dot index
    float Q = -2f * dotQDemod[d];
    // Y 和 I 仍按 sub-pixel 計算
    ...
}
```

**物理正確性分析**：
- Q 在 NES 信號中的實際頻率上限：色調最高頻率 ≈ 0.4 MHz
- dot 率 = 1.07 MHz → 在 dot 率採樣 Q，奈奎斯特頻率 = 0.54 MHz > 0.4 MHz ✓
- Sub-pixel Q 的差異：完全由副載波調製造成，屬於「應被濾除的分量」，dot 率採樣反而更精確

---

### 4-C：I channel 降採樣（2× → 512 計算點）【★★ 中效益】

**I channel 帶寬** ≈ 1.3 MHz，dot 率 1.07 MHz 稍微欠採樣，需以 2× rate（512 計算點）確保奈奎斯特。

```
現況: 1024 × 18 = 18,432 MACs
方案: 512 × 18 = 9,216 MACs（2× 減少）
```
每 2 個 sub-pixel 計算一次 I，線性插值到 1024。

---

### 4-D：SlewRate IIR 折疊進 GenerateWaveform 單次掃描【★★ 低成本改進】

**問題**：目前 GenerateWaveform 先寫入 waveBuf，再用單獨迴圈做 SlewRate IIR。這是 2 次 waveBuf 掃描（寫 + 讀/改）。

**方案**：折疊為單次掃描：
```csharp
float vPrev = firstY;
for (int i = 0; i < kBufLen; i++) {
    vPrev += SlewRate * (waveBufRaw[i] - vPrev);  // 邊算邊 IIR
    waveBuf[i] = vPrev;
}
```
或在主 dot 迴圈生成時直接套用 IIR。
- **節省**：減少一次 waveBuf 讀取（1084 × 4 bytes 的 cache reload）

---

### 4-E：CrtScreen 高斯權重 + Bloom 合併為單一預計算值【★ 已接近最優】

**現況**：`_weights[ty]` 已是 pre-computed Gaussian。
**改進空間**：若 BloomStrength=0（S-Video 端子），inner loop 退化為純亮度縮放，可跳過 bright 計算：
```csharp
if (bloom == 0f) {
    // 簡化路徑：vFw = vWeight * vBoost（無 Bloom 計算）
} else {
    // 完整路徑
}
```
- 對 S-Video + CrtEnabled 的組合：每 scanline 跳過 1024 × (3 muls + 2 adds)

---

### 4-F：CrtScreen 整數 BGRA 打包（消去 scalar 尾巴）【★★ 中效益】

**問題**：SIMD 主迴圈之後有 scalar pack loop：
```csharp
for (int k = 0; k < VS; k++) {
    int ri = Math.Min(255, (int)(vr[k] * 255.5f));
    // ...
    rowPtr[x + k] = (uint)(bi | (gi << 8) | (ri << 16) | 0xFF000000u);
}
```
`vr[k]` 存取是 Vector<float> indexer，效能不好（需 shuffle + extract）。

**方案**：用 Gamma LUT 將 `(int)(v * 255.5f)` 整合後，可考慮：
1. 將 float[k] 暫存到 stackalloc float[VS]，再用 for 讀取（避免 Vector indexer）
2. 或：直接用 scalar float 運算取代 SIMD（若 pack overhead > SIMD benefit）

若搭配 Gamma LUT（byte 查表），pack 步驟變為：
```csharp
stackalloc float[VS] tmp; // 存 SIMD 結果
// 用 fixed(&tmp[0]) 寫入，再 scalar 讀取 + LUT 查表
```

---

## 五、滑動指標 / 遞增式微優化（合集）

| 優化 | 位置 | 現況 | 改進 | 難度 |
|------|------|------|------|------|
| dot index | DecodeAV_Composite | `outX * 256 / kOutW` | `outX >> 2` | 🟢 trivial |
| phase | DecodeAV_Composite | `(phase0 + outX) % 6` | inc + wrap | 🟢 trivial |
| center | DemodulateRow | `kLeadPad + p * 1024/1024` | `kLeadPad + p` | 🟢 trivial |
| wvY/wvI/wvQ | DemodulateRow | 每 pixel 重算 start | 滑動指標++ | 🟡 easy |
| tModI/tModQ | DemodulateRow | double `% 6` × 1024 | init + inc+wrap | 🟡 easy |
| center | DemodulateRow_SVideo | 同上 | 同上 | 🟡 easy |

---

## 六、並行化機會

### 6-A：Level 3 Scanline-level Parallel.For【★★★ 高效益，中難度】

**問題**：目前 Level 3 按 scanline 序列呼叫（由 PPU 驅動），無跨行並行。
**障礙**：`waveBuf` 和 `cBuf` 是 static，不支援多執行緒同時使用。

**方案**：在 PPU 完成 frame 後，批次並行處理 240 scanlines：
```csharp
// 每幀：先收集 240 scanlines 的 palBuf 快照（double-buffer）
// 然後批次處理：
Parallel.For(0, 240, sl => {
    // 每 thread 使用 stackalloc（不需 heap allocation）
    float* waveBuf = stackalloc float[kBufLen];  // 4,336 bytes per thread
    float* cBuf    = stackalloc float[kBufLen];
    int phase0 = (initialPhase + sl * 2) % 6;   // 可預計算，無序列依賴
    GenerateWaveform_local(palBuf[sl], emphBits[sl], isRF, sl, phase0, waveBuf);
    DemodulateRow_local(sl, phase0, waveBuf, linearBuffer);
});
```
- **線程安全**：每 thread 有自己的 waveBuf（stack），linearBuffer 寫入不同行（sl × kOutW），無競態
- **節省**：~8× 加速（8-core CPU）

### 6-B：Level 2 Scanline Parallel.For【★★ 中效益，中難度】

Level 2 的 DecodeAV_Composite 有 IIR 狀態（iFilt/qFilt/yFilt），但 IIR 狀態不跨 scanline（每 scanline 重新初始化）。因此 scanline 間完全獨立，可 Parallel.For(0, 240)。

但 dotY/dotI/dotQ 是 static shared，需改為 per-thread（可 stackalloc 256 floats per thread）。

---

## 七、記憶體與快取優化

### 7-A：DemodulateRow combinedI/Q 記憶體佈局【已優化，確認】

目前 `combinedI[ph * kWinI + n]`，對 ph 固定、n 連續存取 → 完全連續，SIMD 友好 ✓

### 7-B：waveTable 和 combinedI/Q 合併預計算【★ 考量】

如果實作 2-B waveTable，其大小 1536 floats = 6KB。加上 combinedI（108 floats）+ combinedQ（324 floats）= 432 floats = 1.7KB。合計 7.7KB，接近 L1 大小（通常 32KB）。合理。

### 7-C：linearBuffer 存取模式【已優化 planar layout ✓】

CrtScreen.Render() 讀取三個連續平面，DemodulateRow 寫入三個平面。目前 planar layout 已是最優。

---

## 八、優先矩陣

### 快速收益（Low effort, High gain）— 建議最先實作

| # | 優化 | 估計收益 | 實作複雜度 | 適用路徑 |
|---|------|----------|------------|---------|
| 1 | `outX >> 2`（取代 `*256/kOutW`） | 小但零成本 | 🟢 1行 | Level 2 Composite |
| 2 | 相位遞增取代 `% 6` | 中（節省 ~1024 div/scanline） | 🟢 5行 | Level 2 Composite |
| 3 | `center = kLeadPad + p`（消去乘法） | 小但零成本 | 🟢 1行 | Level 3 Demod |
| 4 | tModI/tModQ 滑動遞增 | 中（消去 2048 double-mod/scanline） | 🟡 10行 | Level 3 Demod |
| 5 | DemodulateRow 滑動指標 | 小（消去 3072 pointer 計算） | 🟡 10行 | Level 3 Demod |
| 6 | Gamma LUT（256 bytes） | **高**（取代 3 float mul/pixel × 240K）| 🟡 15行 | Level 2+3+CRT |
| 7 | Emphasis atten 預計算 | 微 | 🟢 5行 | 通用 |

### 中期優化（Medium effort, High gain）

| # | 優化 | 估計收益 | 實作複雜度 | 適用路徑 |
|---|------|----------|------------|---------|
| 8 | GenerateSignal YIQ 64-LUT | **高**（重構 GenerateSignal） | 🟡 20行 | Level 2 |
| 9 | GenerateWaveform waveTable[64×6×4] | **高**（主迴圈 4× 加速） | 🟡 30行 | Level 3 |
| 10 | Q channel dot 率計算（4× 減少 Q MACs） | **極高**（減少 41K MACs/scanline） | 🟡 30行 | Level 3 |
| 11 | I channel 2× 降採樣 | 高（減少 9K MACs/scanline） | 🟡 25行 | Level 3 |
| 12 | SlewRate IIR 折疊進生成迴圈 | 中（減少 cache reload） | 🟡 10行 | Level 3 |
| 13 | NoiseIntensity 256-LUT | 低（僅 RF 有效） | 🟡 10行 | RF path |

### 長期優化（High effort, Very High gain）

| # | 優化 | 估計收益 | 實作複雜度 | 適用路徑 |
|---|------|----------|------------|---------|
| 14 | Scanline-level Parallel.For（Level 3） | **極高**（N-core 倍速） | 🔴 架構重構 | Level 3 |
| 15 | Scanline-level Parallel.For（Level 2） | 高 | 🟠 中難度 | Level 2 |
| 16 | int16 Q13 waveBuf + System.Runtime.Intrinsics | **極高**（2× SIMD width）| 🔴 移植+重寫 | Level 3 / Avalonia |

---

## 九、實測建議順序

```
Phase A（本次 session）: #1 #2 #3 #4 #5 #7 — 全部微改動，零風險
Phase B: #6 Gamma LUT — 需確認色彩一致性（screenshot diff）
Phase C: #8 #9 — GenerateSignal/Waveform LUT 重構
Phase D: #10 #11 — Q/I 降採樣（需驗證視覺質量）
Phase E: #14 — Parallel.For 架構（需 double-buffer palBuf + stackalloc）
Phase F: #16 — .NET 10 / Avalonia 路徑 int16 SIMD
```

---

## 十、估計整體效能提升

假設目前 Level 3 耗時 T：

| 階段完成 | 預估 FPS 提升 |
|---------|--------------|
| Phase A（微優化）| +5-10% |
| Phase A+B（Gamma LUT）| +10-15% |
| Phase A+B+C（LUT 重構）| +20-30% |
| Phase A~D（Q/I 降採樣）| **+50-70%** |
| Phase A~E（Parallel.For）| **+150-300%**（多核）|

> **結論**：最高 CP 值的單次優化是 **Q channel 降採樣到 dot 率**（4× 減少最大熱點的 MACs，只需重構 30 行，零精度損失）。
> 其次是 **Gamma LUT**（通殺所有路徑，15 行，100% 等效）。
> 長期看 **Scanline Parallel.For** 是 breakthrough，但需要架構調整。

---

*分析基於 Ntsc.cs (785行) + CrtScreen.cs (175行) 原始碼，2026-03-20*
