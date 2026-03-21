# Analog SIMD 優化 TODO List

**建立日期**: 2026-03-21
**基準效能**: 103.32 FPS (UltraAnalog+CRT+RF, AnalogSize=4, Release, AccuracyOptA=ON)
**目標框架**: .NET Framework 4.8.1 (AprNes) / .NET 10 (AprNesAvalonia)
**來源文件**: `etc/VIDEO/1-C# 螢幕模擬效能深度優化.md`, `etc/VIDEO/2-C# 螢幕模擬效能優化建議.md`

---

## 優先度 1：高確信度、明確收益

### S01 — CrtScreen: vFw 常數提升（Constant Hoisting）

**狀態**: 待實作
**框架相容性**: .NET 4.8.1 ✅ | .NET 10 ✅ — 純數學重排，無新 API 依賴
**影響範圍**: `CrtScreen.cs` Render() — is1to1 / isDouble SIMD 迴圈
**預估收益**: 中～高（內部迴圈減少 2 次 SIMD 乘法，約節省 20-30% SIMD 指令週期）

**分析**: 目前 SIMD 內迴圈：
```csharp
var vFw = (vWeight + vBright * vBloom * vOMW) * vBoost;
```
其中 `vWeight`, `vBloom`, `vOMW`, `vBoost` 對整條掃描線都是常數，只有 `vBright` 逐像素變化。每次迴圈重複計算 4 次向量乘法（Bloom×OMW, 結果×vBright, +vWeight, ×vBoost）。

**修改方案**: 在 X 迴圈外部預計算：
```csharp
var vConstA = vWeight * vBoost;                    // 掃描線常數部分
var vConstB = vBloom * vOMW * vBoost;              // vBright 的係數
// 迴圈內簡化為 1 次乘法 + 1 次 FMA：
var vFw = vConstA + vBright * vConstB;
```
內迴圈從 4 次向量乘法 → 1 次乘法 + 1 次加法（可觸發 FMA 融合指令）。
影響 is1to1 (AnalogSize=4) 和 isDouble (AnalogSize=8) 兩條路徑。
scalar fallback 路徑同理可改：`float fw = constA + bright * constB;`

---

### S02 — CrtScreen: SIMD 像素打包（消除 scalar extraction loop）

**狀態**: 待實作
**框架相容性**: .NET 4.8.1 ⚠️ 可行但需 workaround | .NET 7+ ✅ 最佳
- .NET 4.8.1: `Vector<int>` 無 `ShiftLeft`，需用 `* 256` / `* 65536` 乘法替代位元移位
- .NET 7+: 可用 `Vector.ShiftLeft(viG, 8)` 直接位元移位，更快
- .NET 10 (Avalonia): 可用 `Avx2.PackUnsignedSaturate` 等 HW intrinsics 做最佳像素打包
**影響範圍**: `CrtScreen.cs` Render() — is1to1 / isDouble 的 `for (int k = 0; k < VS; k++)` 迴圈
**預估收益**: 高（消除每幀 860,160 次 scalar 元素提取，改為純 SIMD 路徑）

**分析**: 目前 SIMD 計算完 vr/vg/vb 後，用 scalar 迴圈逐元素提取：
```csharp
for (int k = 0; k < VS; k++)
{
    int ri = Math.Min(255, (int)(vr[k] * 255.5f));
    int gi = Math.Min(255, (int)(vg[k] * 255.5f));
    int bi = Math.Min(255, (int)(vb[k] * 255.5f));
    rowPtr[x + k] = (uint)(bi | (gi << 8) | (ri << 16) | 0xFF000000u);
}
```
每次迭代提取 VS 個元素 × 3 通道 = 12-24 次 scalar 存取，完全打斷 SIMD 管線。

**修改方案** (.NET 4.8.1 可用)：
```csharp
// 預宣告常數向量（迴圈外）
var v255_5f = new Vector<float>(255.5f);
var v255i   = new Vector<int>(255);
var vZeroi  = new Vector<int>(0);
var v256i   = new Vector<int>(256);
var v65536i = new Vector<int>(65536);
var vAlpha  = new Vector<int>(unchecked((int)0xFF000000));

// SIMD 迴圈內（取代 scalar 提取）
var viR = Vector.Max(vZeroi, Vector.Min(Vector.ConvertToInt32(vr * v255_5f), v255i));
var viG = Vector.Max(vZeroi, Vector.Min(Vector.ConvertToInt32(vg * v255_5f), v255i));
var viB = Vector.Max(vZeroi, Vector.Min(Vector.ConvertToInt32(vb * v255_5f), v255i));
// 打包 BGRA（.NET 4.8.1 無 shift，用乘法替代）
var packed = Vector.BitwiseOr(Vector.BitwiseOr(viB, viG * v256i),
             Vector.BitwiseOr(viR * v65536i, vAlpha));
*(Vector<int>*)(rowPtr + x) = packed;
```
完全消除 scalar 提取迴圈，整條 SIMD 管線不中斷。

**注意**: .NET 4.8.1 的 Vector\<int\> 無 shift 運算（.NET 7+ 才有），用 `* 256` / `* 65536` 等效替代。.NET 10 (Avalonia) 可改用 `Vector.ShiftLeft`。

---

### S03 — Ntsc: `new Vector<float>(1f)` 提升至迴圈外

**狀態**: 待實作
**框架相容性**: .NET 4.8.1 ✅ | .NET 10 ✅ — 純程式碼重排，無新 API 依賴
**影響範圍**: `Ntsc.cs` DemodulateRow / DemodulateRow_SVideo — Q@dot 迴圈 + I 解調迴圈
**預估收益**: 低～中（256+1024 次迴圈內 vector 建構，JIT 可能已 hoist，但明確提出可保證）

**分析**: 4 處 `Vector.Dot(acc, new Vector<float>(1f))` 位於 tight loop 內：
- DemodulateRow: Q@dot loop (256 iter) + I loop (1024 iter)
- DemodulateRow_SVideo: 同上

JIT *可能*會 hoist 這個常數建構，但不保證。明確提出為 `vOne` 可確保零分配。

**修改方案**: 在 for 迴圈外宣告：
```csharp
var vOne = new Vector<float>(1f);
```
內部改為 `sumI = Vector.Dot(acc, vOne);`

---

### S04 — Ntsc/CrtScreen: 無分支 Clamping

**狀態**: 待實作
**框架相容性**: .NET 4.8.1 ⚠️ 可用 `Math.Max`/`Math.Min` | .NET 6+ ✅ 可用 `Math.Clamp`
- .NET 4.8.1: 無 `Math.Clamp`，需寫成 `Math.Max(0f, Math.Min(1f, r))`，JIT 通常仍產生 MAXSS/MINSS
- .NET 6+: `Math.Clamp(r, 0f, 1f)` 由 JIT 直接編譯為 MAXSS + MINSS 硬體指令，語意更清晰
**影響範圍**: `Ntsc.cs` WriteLinear() + YiqToRgb() + `CrtScreen.cs` scalar 路徑
**預估收益**: 中（消除 WriteLinear 的 6 個分支 × 245,760 次/幀 = 1.47M 分支預測負擔）

**分析**: 目前 WriteLinear (Level 3 hot path，每幀呼叫 1024×240 = 245,760 次)：
```csharp
if (r < 0f) r = 0f; else if (r > 1f) r = 1f;
```
每次 3 通道 × 2 分支 = 6 個條件分支，影響 CPU 管線預測。

**修改方案**:
```csharp
// .NET 4.8.1:
r = Math.Max(0f, Math.Min(1f, r));
// .NET 6+ / .NET 10 (Avalonia):
r = Math.Clamp(r, 0f, 1f);
```
適用於所有 scalar clamping 路徑（WriteLinear, YiqToRgb, CrtScreen scalar fallback）。

---

## 優先度 2：中等收益、需要驗證

### S05 — Scanline Pipelining（Stage 1 → Stage 2 行級流水線）

**狀態**: 待評估（需 benchmark 驗證）
**框架相容性**: .NET 4.8.1 ✅ | .NET 10 ✅ — 架構重構，無新 API 依賴
**影響範圍**: `PPU.cs` + `Ntsc.cs` + `CrtScreen.cs` 架構重構
**預估收益**: 不確定（快取收益 1.5-2x vs 失去 Parallel.For 多核並行的取捨）

**分析**: 目前 linearBuffer 大小 = 1024 × 240 × 3 × 4 = **2.8 MB**，遠超 L1 (32-64KB) 和 L2 (256-512KB)。流程是：
1. PPU 每條 scanline 呼叫 `Ntsc.DecodeScanline()` → 寫入 linearBuffer 的 1 行 (~12KB)
2. 240 條全部完成後，`CrtScreen.Render()` 從頭讀取整個 linearBuffer（2.8 MB 從 L3/RAM）

Scanline Pipelining：Ntsc 解碼完第 N 條後，立刻觸發 CrtScreen 渲染對應的 ~3.5 行 ty，資料留在 L1 Cache。

**風險**:
- 每條 scanline 只產生 ~3.5 行 CrtScreen 輸出，太少無法有效並行化
- 目前 `Parallel.For(0, dstH, ...)` 在 6 核 CPU 上可提供 ~4-5x 加速
- 若改為 per-scanline 順序處理，失去多核並行，可能淨損

**可能折衷方案**:
- 批次 8-16 條 scanline → 渲染對應的 28-56 行 CrtScreen 輸出
- 或保留 Parallel.For 但分段執行（每 8 條 source scanline 跑一次 Parallel.For）

**結論**: 需先用 profiler 確認 CrtScreen.Render 的 L3 miss 是否為瓶頸。若 CrtScreen 耗時佔比 < 30%，此優化收益有限。建議先完成 S01+S02 後重新 benchmark，再決定是否值得實作。

---

### S06 — Parallel.For Partitioner 優化

**狀態**: 待評估
**框架相容性**: .NET 4.8.1 ✅ | .NET 10 ✅ — `Partitioner.Create` 自 .NET 4.0 即可用
**影響範圍**: `CrtScreen.cs` Render() — Parallel.For
**預估收益**: 低（預設 TPL 分區在 840 iter 下通常已合理）

**分析**: 目前 `Parallel.For(0, dstH, ...)` 預設分區策略。對 840 行（N=4），TPL 通常切成 ProcessorCount 個 chunk，已接近最佳。

**修改方案**: 使用自訂 Partitioner：
```csharp
var partitioner = Partitioner.Create(0, dstH, dstH / Environment.ProcessorCount);
Parallel.ForEach(partitioner, range => { for (int ty = range.Item1; ty < range.Item2; ty++) { ... } });
```
減少執行緒排程開銷，但改善幅度可能 < 5%。

---

## 暫時排除（下一階段優化評估 ISSUE）

以下項目經分析後暫不列入本輪實作，但保留作為下一階段優化討論的候選。

| 建議 | 排除原因 | 重新評估條件 |
|------|----------|-------------|
| NativeMemory.AlignedAlloc | .NET 4.8.1 無此 API（需 .NET 6+）。SSE2 (16B) 下 AllocHGlobal 已足夠對齊 | 遷移至 Avalonia (.NET 10) 後，若 AVX2 (32B) 啟用時出現未對齊效能損失 |
| IIR SIMD Prefix-Sum | 一階 IIR 的前綴和展開極度複雜（矩陣指數），且對 1024 點 scalar IIR 的實際增速有限（IIR 本質序列依賴） | 若 profiler 顯示 IIR 迴圈佔比 > 40%，可考慮 4-scanline 跨行 SIMD 替代方案 |
| Math.Sin 遞推正弦波 | RF buzzRow 每條 scanline 只算 1 次 Math.Sin（240 次/幀），耗時可忽略 | 若未來加入更高頻率的 RF 模擬（per-sample Sin），可改用遞推複數旋轉 |
| tmpBuf 消除 | 僅在 `!toCrt && dstW != kOutW` 時使用。測試配置 (UltraAnalog+CRT) 不走此路徑 | 若 Level 2 fast path 也需要效能優化時，重新評估 |
| Gamma LUT 全面取代 Math.Pow | CrtScreen.cs 已全面使用 `r += 0.229f * r * (r-1f)` 快速近似，無 Math.Pow 呼叫。**已完成** | — |
| Math.Clamp | .NET 4.8.1 無此 API（需 .NET Core 2.0+）。本輪用 `Math.Max`/`Math.Min` 達成相同效果（見 S04） | Avalonia 版直接使用 `Math.Clamp` 即可，無需額外工作 |

---

## 建議實作順序

```
S01 (CrtScreen vFw 常數提升)     ← 最簡單，改 4 行，收益確定
S02 (CrtScreen SIMD 像素打包)    ← 收益最大，消除 scalar extraction
S03 (Ntsc vOne 提升)             ← 最簡單，1 行
S04 (無分支 Clamping)            ← 收益明確，改法安全
── benchmark ──                   ← 重新測量，評估剩餘瓶頸
S05 (Scanline Pipelining)        ← 視 benchmark 結果決定
S06 (Partitioner)                ← 視 benchmark 結果決定
```

---

## Avalonia (.NET 10) 額外可用優化（.NET 4.8.1 無法使用）

| 項目 | 最低版本 | 說明 |
|------|:--------:|------|
| `NativeMemory.AlignedAlloc` | **.NET 6** | 32-byte 對齊所有 SIMD buffer（AVX2 最佳）。.NET 4.8.1 僅有 `Marshal.AllocHGlobal`（8-16B 對齊） |
| `Vector.ShiftLeft` | **.NET 7** | 取代 S02 像素打包的 `* 256` / `* 65536` 乘法 workaround，直接用硬體位元移位指令 |
| `Vector128/256` HW Intrinsics | **.NET Core 3.0** | 可使用 `Avx2.PackUnsignedSaturate`、`Sse2.PackUnsignedSaturate128` 等做最佳像素打包，繞過 `Vector<T>` 抽象層 |
| `Math.Clamp` | **.NET Core 2.0** | JIT 直接編譯為 MAXSS + MINSS，取代 S04 的 `Math.Max(0f, Math.Min(1f, r))` 寫法 |
| AVX-512 (`Vector512<T>`) | **.NET 8** | 若 CPU 支援 AVX-512，Vector\<float\>.Count=16，CrtScreen 內迴圈寬度翻倍 |
