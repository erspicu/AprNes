# 類比管線效能優化分析

**日期**: 2026-03-21
**範圍**: `Ntsc.cs`（Stage 1 訊號解碼）+ `CrtScreen.cs`（Stage 2 CRT 顯示）
**目標**: 列出所有可行的效能優化項目，供後續逐一評估與實作

---

## 目錄

- [A. Ntsc.cs 優化項目](#a-ntsccs-優化項目)
  - [A1. DemodulateRow WriteLinear SIMD 批次化](#a1-demodulaterow-writelinear-simd-批次化)
  - [A2. Level 2 DecodeAV 迴圈整數除法消除](#a2-level-2-decodeav-迴圈整數除法消除)
  - [A3. Level 2 DecodeAV YiqToRgb SIMD 批次化](#a3-level-2-decodeav-yiqtorgb-simd-批次化)
  - [A4. GenerateWaveform 內層展開 + 分支消除](#a4-generatewaveform-內層展開--分支消除)
  - [A5. DemodulateRow I-channel FIR 展開](#a5-demodulaterow-i-channel-fir-展開)
  - [A6. Level 2 Herringbone 分支移出主迴圈](#a6-level-2-herringbone-分支移出主迴圈)
- [B. CrtScreen.cs 優化項目](#b-crtscreencs-優化項目)
  - [B1. isDouble 路徑 SIMD 雙倍寫入消除標量提取](#b1-isdouble-路徑-simd-雙倍寫入消除標量提取)
  - [B2. ApplyPhosphorPersistence SIMD 向量化](#b2-applyphosphorpersistence-simd-向量化)
  - [B3. ApplyShadowMask SIMD 向量化](#b3-applyshadowmask-simd-向量化)
  - [B4. ApplyHorizontalBlur SIMD 向量化](#b4-applyhorizontalblur-simd-向量化)
  - [B5. ApplyBeamConvergence 偏移量預計算](#b5-applybeamconvergence-偏移量預計算)
  - [B6. BeamConvergence + Curvature 合併 MemoryCopy](#b6-beamconvergence--curvature-合併-memorycopy)
  - [B7. Scalar 路徑（N=2/6）SIMD 化](#b7-scalar-路徑n26-simd-化)
  - [B8. 後處理 Pass 合併](#b8-後處理-pass-合併)
- [C. 架構層級優化](#c-架構層級優化)
  - [C1. Parallel.For 粒度調整](#c1-parallelfor-粒度調整)
  - [C2. Level 2 直寫 AnalogScreenBuf 行複製優化](#c2-level-2-直寫-analogscreenbuf-行複製優化)
  - [C3. Curvature 中心區域跳過](#c3-curvature-中心區域跳過)

---

## A. Ntsc.cs 優化項目

### A1. DemodulateRow WriteLinear SIMD 批次化

**位置**: `Ntsc.cs:699-728`（DemodulateRow 主迴圈）+ `Ntsc.cs:835-848`（WriteLinear）

**現狀**: 主迴圈每次迭代處理 1 個像素：
```
for (int p = 0; p < 1024; p++) {
    Y = 6-tap FIR;
    I = 18-tap FIR (SIMD);
    Q = lookup;
    WriteLinear(sl, p, Y, I, Q);  // 3次矩陣乘法 + 3次 clamp + 3次散射寫入
}
```

WriteLinear 做 3×3 矩陣乘法（YIQ→RGB）後分別寫入 `linearBuffer[idx]`、`linearBuffer[kPlane+idx]`、`linearBuffer[2*kPlane+idx]`——三個相距 245,760 floats 的位置。

**優化方案**: 累積 VS (4/8) 個像素的 Y/I/Q 向量，做 SIMD 矩陣乘法，然後連續寫出 R/G/B 各 VS 個 float：

```csharp
// 累積 VS 個 Y/I/Q
Vector<float> vY, vI, vQ;
// SIMD 矩陣乘法
var vR = vY * vrY + vI * vrI + vQ * vrQ;  // 3 FMA
var vG = vY * vgY + vI * vgI + vQ * vgQ;
var vB = vY * vbY + vI * vbI + vQ * vbQ;
// clamp
vR = Vector.Min(Vector.Max(vR, vZero), vOne);
// 連續寫入
*(Vector<float>*)(lb_r + idx) = vR;
*(Vector<float>*)(lb_g + idx) = vG;
*(Vector<float>*)(lb_b + idx) = vB;
```

**挑戰**: Y 的 6-tap FIR 已手動展開成 6 次標量乘加（非常快），I 的 18-tap FIR 是 SIMD 但每次只產生 1 個 scalar 結果。要累積 VS 個 Y 需要 Y FIR 滑動 VS 步，I FIR 也要滑動 VS 步——兩個 FIR 的窗口之間有重疊數據，可以利用 SIMD 寄存器 shuffle 做水平滑動。

**預估收益**: 中。消除 3 次散射寫入的 cache miss penalty，但 FIR 本身已經很快。主要瓶頸可能在 I FIR 的 memory bandwidth（18-tap × 1024 pixels = 18K float reads）。

**複雜度**: 高。需重構 DemodulateRow 主迴圈結構。

**風險**: 低（純計算，不影響行為）。

---

### A2. Level 2 DecodeAV 迴圈整數除法消除

**位置**: `Ntsc.cs:428` + `Ntsc.cs:473`

**現狀**:
```csharp
for (int outX = 0; outX < dstW; outX++) {
    int d = outX / N;  // 整數除法，每像素一次
```

`N` 是 AnalogSize (2/4/6/8)。在 dstW=1024 (N=4) 的情況下，每幀 240 × 1024 = 245,760 次整數除法。

**優化方案**: 用遞增計數器取代除法：
```csharp
int d = 0, dCount = 0;
for (int outX = 0; outX < dstW; outX++) {
    // d = outX / N，但無除法
    if (++dCount == N) { dCount = 0; d++; }
```

或利用位移（N 是 2 的冪次時）：`d = outX >> shift`（N=2 → shift=1, N=4 → shift=2, N=8 → shift=3）。N=6 不是 2 的冪次，需要遞增計數器。

**預估收益**: 低~中。現代 CPU 整數除法 ~20-30 cycles（常數除數 JIT 可能自動轉為乘法），但量大。

**複雜度**: 極低。幾行修改。

**風險**: 無。

---

### A3. Level 2 DecodeAV YiqToRgb SIMD 批次化

**位置**: `Ntsc.cs:447`（DecodeAV_Composite）、`Ntsc.cs:477`（DecodeAV_SVideo）

**現狀**: 每像素調用 `YiqToRgb(y, iFilt, qFilt)`，執行 3×3 矩陣乘法 + 3 次 float→int + 3 次 gammaLUT 查表。

**障礙**: Level 2 的 IIR 濾波器（yFilt、iFilt、qFilt）是嚴格串行的——每個像素的輸出取決於上一個像素的狀態。**這是根本性的串行依賴，無法用 SIMD 並行化 IIR 本身**。

**有限的優化空間**: 只有 YiqToRgb 裡的矩陣乘法和打包可以累積批次化，但 IIR 仍必須逐像素串行計算。可以每算出 4 個 YIQ，做一次 SIMD 矩陣轉換和打包。

**預估收益**: 低。IIR 串行計算是真正的瓶頸，矩陣乘法佔比不大。

**複雜度**: 中。需要拆分 IIR 計算和色彩轉換。

**風險**: 無。

---

### A4. GenerateWaveform 內層展開 + 分支消除

**位置**: `Ntsc.cs:567-585`（Composite）、`Ntsc.cs:625-644`（SVideo）

**現狀**:
```csharp
for (int d = 0; d < 256; d++) {
    for (int s = 0; s < 4; s++) {
        float x = src[s] * ea[tMod + s];
        if (herring) { ... }  // per-sample 分支
        if (addNoise) { ... } // per-sample 分支
        vVel = vVel * ringDamp + (x - vPrev) * SlewRate;
        vPrev += vVel;
        waveBuf[baseIdx + s] = vPrev;
    }
    tMod += 4; if (tMod >= 6) tMod -= 6;
}
```

**優化方案**:

1. **展開 `s=0..3` 內層迴圈**：4 次迭代完全展開，消除 loop overhead 和 `s` 遞增/比較。
2. **4 路特化版本**（template pattern）：根據 `herring` × `addNoise` 組合（4 種），在迴圈外選擇不同的特化函數，消除內層 `if` 分支：
   ```csharp
   if (herring && addNoise)      GenerateWaveform_HN(...)
   else if (herring)             GenerateWaveform_H(...)
   else if (addNoise)            GenerateWaveform_N(...)
   else                          GenerateWaveform_Plain(...)
   ```

**預估收益**: 低~中。1024 次迭代 × 2 個分支 = 消除 ~2048 個分支預測。JIT 可能已有不錯的 branch prediction，但消除仍可減少 pipeline stall。

**複雜度**: 中（4 路特化會增加程式碼量，但邏輯不變）。

**風險**: 無。

---

### A5. DemodulateRow I-channel FIR 展開

**位置**: `Ntsc.cs:707-713`

**現狀**: I-channel 18-tap FIR 用 `Vector<float>` SIMD 迴圈：
```csharp
for (; n <= kWinI - VS; n += VS)
    acc += *(Vector<float>*)(cwI + n) * *(Vector<float>*)(wvI + n);
sumI = Vector.Dot(acc, new Vector<float>(1f));
for (; n < kWinI; n++) sumI += cwI[n] * wvI[n];
```

kWinI=18。在 SSE2 (VS=4) 下：4 次 SIMD 迴圈 + 2 次 scalar 尾端。在 AVX2 (VS=8) 下：2 次 SIMD + 2 次 scalar。

**優化方案**: 手動展開為固定次數的 SIMD 加載/乘加：
```csharp
// SSE2: 4 loads × 4-wide = 16 elements, + 2 scalar
var a0 = *(Vector<float>*)(cwI) * *(Vector<float>*)(wvI);
var a1 = *(Vector<float>*)(cwI+4) * *(Vector<float>*)(wvI+4);
var a2 = *(Vector<float>*)(cwI+8) * *(Vector<float>*)(wvI+8);
var a3 = *(Vector<float>*)(cwI+12) * *(Vector<float>*)(wvI+12);
float sumI = Vector.Dot(a0+a1+a2+a3, vOne) + cwI[16]*wvI[16] + cwI[17]*wvI[17];
```

消除迴圈控制變數 `n`、比較 `n <= kWinI - VS`、遞增。

**預估收益**: 低。JIT 常量折疊可能已展開此迴圈。但 1024 次調用 × 迴圈 overhead 仍有潛在收益。

**複雜度**: 低。

**風險**: 無。需依 VS 分支（SSE2 vs AVX2），或保留泛型 Vector 讓 JIT 處理。

---

### A6. Level 2 Herringbone 分支移出主迴圈

**位置**: `Ntsc.cs:426-450`（DecodeAV_Composite 主迴圈）

**現狀**:
```csharp
for (int outX = 0; outX < dstW; outX++) {
    ...
    if (herring2) { y += hI2; ... }  // per-pixel 分支
    if (addNoise) { ... }            // per-pixel 分支
    row0[outX] = YiqToRgb(y, iFilt, qFilt);
}
```

**優化方案**: 與 A4 相同的特化策略。根據 `herring2` × `addNoise` 在迴圈外選擇特化版本。

**預估收益**: 低。Level 2 的瓶頸是 IIR 串行依賴，分支消除的收益有限。

**複雜度**: 中。

**風險**: 無。

---

## B. CrtScreen.cs 優化項目

### B1. isDouble 路徑 SIMD 雙倍寫入消除標量提取

**位置**: `CrtScreen.cs:504-510`

**現狀**: N=8 (AnalogSize=8) 路徑用 SIMD 計算 VS 個像素後，用標量循環逐一提取並寫入兩個相鄰輸出：
```csharp
for (int k = 0; k < VS; k++) {
    uint px = ((uint*)&packed)[k];  // 標量提取
    int outX = (srcX + k) * 2;
    rowPtr[outX]     = px;          // 寫兩次
    rowPtr[outX + 1] = px;
}
```

**優化方案**: 使用 SIMD interleave/duplicate 指令。在 SSE2 下，`Vector128.UnpackLow` + `Vector128.UnpackHigh` 可以把 [A,B,C,D] 變成 [A,A,B,B] 和 [C,C,D,D]，直接做 2 次 128-bit store：

```csharp
// SSE2 (VS=4): packed = [P0, P1, P2, P3]
var lo = Sse2.UnpackLow(packed, packed);   // [P0,P0,P1,P1]
var hi = Sse2.UnpackHigh(packed, packed);  // [P2,P2,P3,P3]
Sse2.Store(rowPtr + srcX*2,     lo);
Sse2.Store(rowPtr + srcX*2 + 4, hi);
```

AVX2 下同理但更寬。

**預估收益**: 中。消除 VS 次標量提取 + VS 次地址計算。N=8 路徑是高解析度模式（2048×1680），每幀 1680 行 × 1024/VS 次 SIMD 迭代，每次節省 ~VS 個 scalar ops。

**複雜度**: 低~中。需引入 `System.Runtime.Intrinsics` 的 SSE2 指令（目前用的是 `System.Numerics.Vector<T>`）。

**風險**: 需確認 .NET Framework 4.8.1 是否支援 `Sse2.UnpackLow(Vector128<int>)`。若不支援，可用 `Vector<int>` 的 bitwise 操作模擬。

---

### B2. ApplyPhosphorPersistence SIMD 向量化

**位置**: `CrtScreen.cs:303-328`

**現狀**: 每像素提取 RGB 分量（3 次 mask + shift）、乘以 decay（3 次 float 乘法）、與當前像素比較（3 次 max）、重新打包（位運算）。全部標量。

**優化方案**: 用整數 SIMD 處理 4 個 BGRA 像素為一組：

```csharp
// 載入 4 個 uint32 (BGRA)
Vector128<int> cur = Sse2.LoadVector128((int*)(dst + idx));
Vector128<int> prv = Sse2.LoadVector128((int*)(prev + idx));

// 拆分為 byte lanes, 乘以 decay (fixed-point), 取 max
// 方法一：用 Sse2.UnpackLow/High 把 byte 擴展為 16-bit，乘法，再 pack
// 方法二：預算 decay 為 fixed-point 8.8，用 Ssse3.MultiplyAddAdjacent
```

**或使用 `Vector<byte>` 高階 API**（VS=16/32 bytes = 4/8 pixels）：
```csharp
// 近似：prev_byte = (byte)(prev_byte * decay_byte >> 8)
// 然後 max(cur_byte, prev_byte) 逐 byte
```

**預估收益**: 中~高。每幀處理 DstW × DstH 像素（N=4: 860K，N=8: 3.4M）。目前每像素 ~15 ops（3 extract + 3 mul + 3 compare + 3 pack + 2 store），SIMD 化可減至 ~4 ops/pixel（load + mul16 + max + store）。

**複雜度**: 中。fixed-point 乘法需要精確模擬 float decay，可能有 ±1 的 rounding 差異（視覺不可見）。

**風險**: 低。可能有 rounding 微差，但對磷光餘輝效果無可見影響。

---

### B3. ApplyShadowMask SIMD 向量化

**位置**: `CrtScreen.cs:204-225`

**現狀**: 每像素 switch(phase)、提取 RGB、兩通道乘以 `dimI`（integer multiply + shift）、重新打包。Phase 以 3 為週期循環。

**優化方案**: 蔭罩是週期 3 的重複圖案。可以預計算 3 像素為一組的 SIMD mask（哪些 byte lane 需要衰減），然後每 3 像素一批處理：

```csharp
// Aperture Grille: phase 0=[R dim:G,B], phase 1=[G dim:R,B], phase 2=[B dim:R,G]
// 預計算 3 組 byte mask，指示哪些 byte 位置乘以 dimI
// 每 3×4=12 bytes 為一個完整週期，剛好裝入 Vector128<byte> (16 bytes, 4 pixels)
// 但 3 不整除 4，需要以 LCM(3,4)=12 pixels 為一組
```

或更簡單：ApertureGrille 模式下，每個 pixel 只有一個通道保持原值（主色），其他兩個乘以 dimI。等價於 `pixel & mask | (pixel * dim >> 8) & ~mask`。可以用 byte-level SIMD 做 blend。

**預估收益**: 中。與 B2 相同的像素數量，但每像素運算量較少（~8 ops → ~2 ops/pixel）。

**複雜度**: 中。週期 3 與 SIMD 寬度（4/8）不對齊，需要特殊處理。

**風險**: 無。

---

### B4. ApplyHorizontalBlur SIMD 向量化

**位置**: `CrtScreen.cs:266-277`

**現狀**: 3-tap FIR `output[x] = prev*α + cur*(1-α) + next*α`，標量逐像素處理。240 行 × 3 plane × 1024 float = 737K 迭代。

**關鍵觀察**: 此 filter 是「非破壞性」——`prev` 保存的是修改前的原始值。等價於：
```
output[x] = original[x-1]*α + original[x]*center + original[x+1]*α
```
因此可以 SIMD 化：讀取連續 VS+2 個 float，左移/右移 1 個元素，做 FMA。

**優化方案**:
```csharp
// 處理 VS 個像素為一組
// prev_vec = [p[x-1], p[x], p[x+1], p[x+2], ...]  (左移 1)
// cur_vec  = [p[x],   p[x+1], ...]
// next_vec = [p[x+1], p[x+2], ...]  (右移 1)
// output = prev_vec * vAlpha + cur_vec * vCenter + next_vec * vAlpha
```

需要在每個 SIMD chunk 邊界保存左鄰和右鄰。但因為是 in-place（寫回同一陣列），需要先暫存原始值或從右到左處理。

**更簡單的方案**: 讀取時多讀 1 個元素做 overlap，寫回不影響尚未讀取的數據（因為我們從左到右，而 `next` 讀的是尚未修改的右鄰）。

**預估收益**: 中。737K 標量乘加 → ~184K SIMD 乘加（VS=4）或 ~92K（VS=8）。

**複雜度**: 低~中。

**風險**: 無。邊界條件需仔細處理（x=0 的 prev、x=SrcW-1 的 next）。

---

### B5. ApplyBeamConvergence 偏移量預計算

**位置**: `CrtScreen.cs:346-369`

**現狀**: 每像素計算 `cx = (tx - halfW) * invHW` 和 `ioff = (int)(cx * maxOff + rounding)`。偏移量 `ioff` **只取決於 `tx`（列位置），與 `ty`（行）無關**。但目前每行每像素都重複計算。

**優化方案**: 預計算 `ioff[dstW]` 陣列（和 `rxR[dstW]`、`rxB[dstW]`），在 Parallel.For 外部計算一次：
```csharp
int* offR = stackalloc int[dstW];
int* offB = stackalloc int[dstW];
for (int tx = 0; tx < dstW; tx++) {
    float cx = (tx - halfW) * invHW;
    int ioff = (int)(cx * maxOff + (cx >= 0 ? 0.5f : -0.5f));
    offR[tx] = Math.Clamp(tx + ioff, 0, dstW - 1);
    offB[tx] = Math.Clamp(tx - ioff, 0, dstW - 1);
}
```

**預估收益**: 中。消除 DstH 次重複計算（N=4: 840 行 × 1024 列 = 860K 次浮點運算 → 1024 次）。

**複雜度**: 極低。

**風險**: 無。預計算陣列大小 = DstW × 2 × 4 bytes ≈ 8KB（N=4），可用 stackalloc。

---

### B6. BeamConvergence + Curvature 合併 MemoryCopy

**位置**: `CrtScreen.cs:344`（Convergence）+ `CrtScreen.cs:240`（Curvature）

**現狀**: 兩個後處理步驟各自做一次 `Buffer.MemoryCopy` 把整幀複製到暫存 buffer：
- BeamConvergence: `dst → _curvTemp`（讀取 tmp 做 R/B 偏移讀取）
- Curvature: `dst → _curvTemp`（讀取 tmp 做 barrel distortion 重映射）

N=4 時每次 copy ~3.4MB，兩次共 ~6.8MB。

**優化方案**: 合併為一個 pass——在 Curvature 的重映射中同時做 Convergence 的 RGB 通道分離讀取：
```csharp
// 合併後：一次 MemoryCopy，然後在 curvature remap 時同時處理 convergence
for (int tx = 0; tx < dstW; tx++) {
    int curvIdx = curvMap[ty * dstW + tx];
    if (curvIdx < 0) { dst[...] = black; continue; }
    int curvY = curvIdx / dstW, curvX = curvIdx % dstW;
    // 在 curvature 重映射後的座標上做 convergence
    int rxR = clamp(curvX + offR[curvX]);
    int rxB = clamp(curvX - offR[curvX]);
    uint srcR = tmp[curvY * dstW + rxR];
    uint srcG = tmp[curvIdx];
    uint srcB = tmp[curvY * dstW + rxB];
    // 提取+合併
    dst[...] = (srcR & 0xFF0000) | (srcG & 0xFF00) | (srcB & 0xFF) | 0xFF000000;
}
```

**預估收益**: 中。省 ~3.4MB memcpy + 一次全幀遍歷。但合併後每像素的運算量增加（需從 curvIdx 反推 curvX/curvY 做除法）。

**替代方案**: 預計算 `_curvMapXY`（分開存 X 和 Y），避免合併 pass 裡的除法。或只合併 MemoryCopy（兩步共用同一份 tmp）。

**複雜度**: 中~高。合併改變了兩步的執行順序和數據流。

**風險**: 低。需驗證 convergence + curvature 的組合結果與分離執行一致。

---

### B7. Scalar 路徑（N=2/6）SIMD 化

**位置**: `CrtScreen.cs:537-565`

**現狀**: AnalogSize=2 或 6 時走純標量路徑，有 fixed-point 水平重採樣 + 亮度計算 + gamma + 打包。

**觀察**: N=2 (512×420) 和 N=6 (1536×1260) 的 linearBuffer 仍然是 1024 寬。N=2 需要 2:1 降採樣（每 2 個 source 取 1 個 output），N=6 需要 1024→1536 上採樣（每 source 像素約 1.5 個 output）。

**優化方案**:
- **N=2（降採樣）**: 直接用 SIMD 讀取 2×VS 個 source，每隔一個取值（stride-2 gather），做 SIMD 計算。或更簡單：讀 VS 個，跳過 VS 個。
- **N=6（上採樣）**: 更複雜，因為 1024→1536 的映射比例是 2:3，需要線性插值。可以用 SIMD 做批次插值。

**預估收益**: 低~中。N=2 和 N=6 是較少使用的模式。

**複雜度**: 中。

**風險**: 無。

---

### B8. 後處理 Pass 合併

**位置**: `CrtScreen.cs:591-594`

**現狀**: 4 個獨立的後處理步驟依序執行，每步遍歷整個輸出 buffer：
```
Render(Parallel.For) → ShadowMask → PhosphorPersistence → BeamConvergence → Curvature
```

N=4 時 860K 像素 × 4 passes = 3.4M 像素存取。每步都是 Parallel.For + 全幀遍歷。

**可合併的組合**:
1. **ShadowMask + PhosphorPersistence**: 可合併為一步——先算 mask 衰減，再和 prev frame 做 max。兩步都是 per-pixel 獨立的，無數據依賴衝突。
2. **BeamConvergence + Curvature**: 如 B6 所述。

**合併後的 pass 結構**:
```
Render → ShadowMask+Phosphor → Convergence+Curvature
```
從 4 pass 降為 2 pass。

**預估收益**: 中~高。減少 2 次全幀遍歷（2 × ~3.4MB read+write），大幅降低 memory bandwidth 壓力。尤其在高解析度 N=8（3.4M 像素 × 4 bytes = 13.7MB per pass）下效益最大。

**複雜度**: 中。合併需要把兩步的邏輯寫進同一個 Parallel.For lambda。

**風險**: 低。需確認合併後的視覺結果與分離執行一致。

---

## C. 架構層級優化

### C1. Parallel.For 粒度調整

**位置**: 整個 CrtScreen.cs 和 Ntsc.cs 的 Parallel.For 調用

**現狀**: 多處使用 `Parallel.For(0, DstH, ty => {...})`，每行是一個工作單位。

- HorizontalBlur: `Parallel.For(0, 240, ...)` × 3 plane = 3 次 Parallel.For，每次 240 個工作單位。每單位只做 1024 float 的 3-tap FIR（~3K FLOPs）。
- ShadowMask, PhosphorPersistence, Convergence, Curvature: 各一次 `Parallel.For(0, DstH, ...)`。

**問題**: Parallel.For 有線程池調度開銷。過多的 Parallel.For 調用（一幀中 ~7 次）會累積調度成本。工作單位過小（1024 × 3 FLOPs ≈ 12μs）時，調度開銷可能超過計算本身。

**優化方案**:
1. **HorizontalBlur 3 plane 合併**: 把 3 個 `Parallel.For` 合併為 1 個，每個工作單位處理同一行的 R/G/B 三個 plane。
2. **使用 `ParallelOptions.MaxDegreeOfParallelism`**: 限制並行度以減少 context switch（特別是在低核心數系統上）。
3. **如 B8 所述合併後處理 pass**: 減少 Parallel.For 調用次數。

**預估收益**: 低~中。取決於系統核心數和 Parallel.For 的 overhead。

**複雜度**: 低。

**風險**: 無。

---

### C2. Level 2 直寫 AnalogScreenBuf 行複製優化

**位置**: `Ntsc.cs:452-454`（Composite）+ `Ntsc.cs:480-482`（SVideo）

**現狀**: Level 2 計算完一行後，用 `Buffer.MemoryCopy` 把結果複製到垂直跨度內的其他行（通常 3-4 行）：
```csharp
for (int row = rowStart + 1; row < rowEnd; row++)
    Buffer.MemoryCopy(row0, dst + row * dstW, dstW * sizeof(uint), dstW * sizeof(uint));
```

N=4 時每行複製 2-3 次 × 4KB ≈ 8-12KB。240 行共 ~2.4MB。

**觀察**: 如果 CrtScreen 不啟用（Level 2 直接輸出），這些複製是必要的（填滿螢幕）。但如果啟用了 CrtScreen（Level 3），這些行會被 CrtScreen 的垂直高斯掃描線完全覆蓋。

**不過**: Level 2 本身不走 CrtScreen，所以複製都是必要的。

**替代方案**: 如果輸出端支援 stride（不要求 buffer 是連續的），可以不複製，改用 stride render。但 Windows Forms PictureBox 要求連續 bitmap data。

**預估收益**: 無明顯優化空間（已是最佳做法）。

**複雜度**: N/A。

---

### C3. Curvature 中心區域跳過

**位置**: `CrtScreen.cs:242-250`

**現狀**: Curvature remap 遍歷每個像素，但 barrel distortion 在螢幕中心附近接近 identity mapping（`srcIdx ≈ ty * dstW + tx`）。

**優化方案**: 預計算「接近 identity」的 Y 範圍（例如 |curvMap[idx] - idx| ≤ 1 的行），這些行直接跳過。只處理偏移量顯著的邊緣區域。

**依 CurvatureStrength=0.12 估算**: 中心約 60-70% 的像素 remap 偏差 ≤ 1 pixel。若能跳過，可節省 ~60% 的 curvature pass 工作量。

**預估收益**: 中。取決於 CurvatureStrength 設定。

**複雜度**: 低~中。需在 PrecomputeCurvature 時額外計算 skip 範圍。

**風險**: 需確保 skip 邊界的像素連續性（不會出現可見的接縫）。

---

## 優先順序建議

依「收益/複雜度」比排序：

| 優先 | ID | 描述 | 預估收益 | 複雜度 | 備註 |
|:----:|:--:|------|:--------:|:------:|------|
| 1 | B5 | Convergence 偏移量預計算 | 中 | 極低 | 最簡單的即時收益 |
| 2 | A2 | Level 2 整數除法消除 | 低~中 | 極低 | 幾行修改 |
| 3 | B8 | 後處理 Pass 合併 (Mask+Phosphor) | 中~高 | 中 | 最大 bandwidth 節省 |
| 4 | B4 | HorizontalBlur SIMD | 中 | 低~中 | 標準 3-tap FIR SIMD |
| 5 | B2 | PhosphorPersistence SIMD | 中~高 | 中 | 整數 SIMD，pixel 量大 |
| 6 | B1 | isDouble 路徑 duplicate 寫入 | 中 | 低~中 | 高解析度模式收益大 |
| 7 | B6 | Convergence+Curvature 合併 | 中 | 中~高 | 省一次 3.4MB copy |
| 8 | A1 | DemodulateRow WriteLinear 批次 | 中 | 高 | 需重構主迴圈 |
| 9 | B3 | ShadowMask SIMD | 中 | 中 | 週期 3 對齊問題 |
| 10 | C1 | Parallel.For 粒度調整 | 低~中 | 低 | HBlur 3 plane 合併 |
| 11 | A4 | GenerateWaveform 展開 + 特化 | 低~中 | 中 | 程式碼量增加 |
| 12 | A5 | I-channel FIR 展開 | 低 | 低 | JIT 可能已做 |
| 13 | C3 | Curvature 中心跳過 | 中 | 低~中 | CurvatureStrength 依賴 |
| 14 | A6 | Level 2 Herringbone 特化 | 低 | 中 | IIR 是瓶頸 |
| 15 | A3 | Level 2 YiqToRgb 批次 | 低 | 中 | IIR 串行限制 |
| 16 | B7 | Scalar 路徑 SIMD | 低~中 | 中 | 少用模式 |

---

## 附錄：不可優化的根本瓶頸

### IIR 濾波器串行依賴

Level 2 的 `yFilt`、`iFilt`、`qFilt` 和 Level 3 的 `vPrev`+`vVel`（振鈴）是嚴格的資料依賴鏈——每個像素的輸出取決於前一個像素的狀態。**這是類比電路模擬的本質，無法用 SIMD 或多線程加速**。

這是 AprNes 的物理模擬與 Blargg LUT 的根本區別：LUT 每個像素獨立查表，天然可並行；IIR 有狀態，天然串行。這個串行鏈是整個 Ntsc.cs 的效能上限。

### Per-scanline 調用模式

`DecodeScanline` 由 PPU 每 scanline 調用一次（240 次/幀），無法 batch。每次調用處理 256 dots × 4 samples = 1024 個浮點數，工作量太小以致 Parallel.For 的開銷不划算（Level 3 的 DemodulateRow 已是串行迴圈）。

### 記憶體頻寬

Level 3 的 linearBuffer（2.9MB）+ AnalogScreenBuf（N=4: 3.4MB）+ 後處理暫存（3.4MB）= ~10MB per frame。在 60 FPS 下約 600MB/s。這接近 DDR4 單核 bandwidth 的 10-15%，尚有餘裕，但後處理的多次遍歷（4 pass × 3.4MB = 13.6MB）會造成 cache thrashing。**Pass 合併是最有效的改善手段**。
