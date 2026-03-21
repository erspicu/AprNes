# Analog SIMD 優化 TODO List

**建立日期**: 2026-03-21
**基準效能**: 103.32 FPS (UltraAnalog+CRT+RF, AnalogSize=4, Release, AccuracyOptA=ON)
**目標框架**: .NET Framework 4.8.1 (AprNes) / .NET 10 (AprNesAvalonia)
**來源文件**: `etc/VIDEO/1-C# 螢幕模擬效能深度優化.md`, `etc/VIDEO/2-C# 螢幕模擬效能優化建議.md`

---

## 優化結果總覽

| 項目 | 描述 | 結果 | FPS | 變化 | 狀態 |
|:----:|------|:----:|:---:|:----:|:----:|
| S01 | CrtScreen vFw 常數提升 | **採用** | 104.48 | **+1.12%** | ✅ ADOPTED |
| S02 | CrtScreen SIMD 像素打包 | **採用** | 107.17 | **+2.57%** (累積 +3.72%) | ✅ ADOPTED |
| S03 | Ntsc vOne 提升 | 不採用 | 107.77 | +0.56% (< 1% 門檻) | ❌ REJECTED |
| S04 | 無分支 Clamping (Math.Max/Min) | 不採用 | 100.05 | **-6.64%** 回歸 | ❌ REJECTED |
| S05 | Scanline Pipelining | 未測試 | — | — | ⏸ DEFERRED |
| S06 | Parallel.For Partitioner | 不採用 | 105.80 | **-1.28%** 回歸 | ❌ REJECTED |

**最終效能**: S01+S02 → **107.17 FPS** (相對原始基準 103.32 提升 **+3.72%**)

---

## 優先度 1：高確信度、明確收益

### S01 — CrtScreen: vFw 常數提升（Constant Hoisting） ✅ ADOPTED

**狀態**: ✅ 已採用 — **+1.12%** (103.32 → 104.48 FPS)
**框架相容性**: .NET 4.8.1 ✅ | .NET 10 ✅ — 純數學重排，無新 API 依賴
**影響範圍**: `CrtScreen.cs` Render() — is1to1 / isDouble SIMD 迴圈

**實作**: 在 X 迴圈外部預計算常數：
```csharp
var vConstA = new Vector<float>(weight * boost);
var vConstB = new Vector<float>(bloom * omw * boost);
// 迴圈內：var vFw = vConstA + vBright * vConstB;
```
內迴圈從 4 次向量乘法 → 1 次乘法 + 1 次加法。scalar fallback 同步更新。

**Benchmark**:
| 回合 | FPS |
|:----:|:---:|
| Run 1 (JIT, 不計) | 102.48 |
| Run 2 | 104.50 |
| Run 3 | 104.46 |
| **平均** | **104.48** |

---

### S02 — CrtScreen: SIMD 像素打包（消除 scalar extraction loop） ✅ ADOPTED

**狀態**: ✅ 已採用 — 累積 **+3.72%** (103.32 → 107.17 FPS)，S02 單獨貢獻 **+2.57%**
**框架相容性**: .NET 4.8.1 ⚠️ 可行但需 workaround | .NET 7+ ✅ 最佳
- .NET 4.8.1: `Vector<int>` 無 `ShiftLeft`，需用 `* 256` / `* 65536` 乘法替代位元移位
- .NET 7+: 可用 `Vector.ShiftLeft(viG, 8)` 直接位元移位，更快

**實作**: 全 SIMD 像素打包取代 scalar extraction loop：
```csharp
var viR = Vector.Max(vZeroi, Vector.Min(Vector.ConvertToInt32(vr * v255_5f), v255i));
var viG = Vector.Max(vZeroi, Vector.Min(Vector.ConvertToInt32(vg * v255_5f), v255i));
var viB = Vector.Max(vZeroi, Vector.Min(Vector.ConvertToInt32(vb * v255_5f), v255i));
*(Vector<int>*)(rowPtr + x) = Vector.BitwiseOr(
    Vector.BitwiseOr(viB, viG * v256i),
    Vector.BitwiseOr(viR * v65536i, vAlphai));
```

**Benchmark** (S01+S02 累積):
| 回合 | FPS |
|:----:|:---:|
| Run 1 (JIT, 不計) | 104.01 |
| Run 2 | 107.29 |
| Run 3 | 107.05 |
| **平均** | **107.17** |

---

### S03 — Ntsc: `new Vector<float>(1f)` 提升至迴圈外 ❌ REJECTED

**狀態**: ❌ 不採用 — **+0.56%** (107.17 → 107.77 FPS)，低於 1% 門檻
**框架相容性**: .NET 4.8.1 ✅ | .NET 10 ✅ — 純程式碼重排，無新 API 依賴
**影響範圍**: `Ntsc.cs` DemodulateRow / DemodulateRow_SVideo — Q@dot 迴圈 + I 解調迴圈

**分析**: 4 處 `Vector.Dot(acc, new Vector<float>(1f))` 位於 tight loop 內。
JIT 已自行 hoist 此常數建構，手動提升效果可忽略。

**Benchmark** (S01+S02+S03):
| 回合 | FPS |
|:----:|:---:|
| Run 1 (JIT, 不計) | 106.43 |
| Run 2 | 107.58 |
| Run 3 | 107.96 |
| **平均** | **107.77** |

---

### S04 — Ntsc/CrtScreen: 無分支 Clamping ❌ REJECTED

**狀態**: ❌ 不採用 — **-6.64%** 回歸 (107.17 → 100.05 FPS)
**框架相容性**: .NET 4.8.1 ⚠️ `Math.Max`/`Math.Min` 非 intrinsic | .NET 6+ ✅ 可用 `Math.Clamp`

**根因分析**: .NET Framework 4.8.1 的 JIT **不會**將 `Math.Max(float, float)` / `Math.Min(float, float)` 編譯為 MAXSS/MINSS 硬體指令。每次呼叫涉及方法呼叫開銷 + NaN 處理邏輯，反而比分支預測良好的 `if/else` 慢得多。

在 WriteLinear 的使用場景下，RGB 值通常在 [0,1] 範圍內（溢出少見），分支預測命中率極高，因此原始 if/else 是最優解。

**結論**: 此優化僅適用於 .NET 6+ (intrinsic Math.Clamp)。.NET 4.8.1 請保持原始 if/else 分支。

**Benchmark** (S01+S02+S04):
| 回合 | FPS |
|:----:|:---:|
| Run 1 (JIT, 不計) | 101.07 |
| Run 2 | 99.62 |
| Run 3 | 100.47 |
| **平均** | **100.05** |

---

## 優先度 2：中等收益、需要驗證

### S05 — Scanline Pipelining（Stage 1 → Stage 2 行級流水線） ⏸ DEFERRED

**狀態**: ⏸ 暫緩 — 需大規模架構重構，風險高
**框架相容性**: .NET 4.8.1 ✅ | .NET 10 ✅ — 架構重構，無新 API 依賴
**影響範圍**: `PPU.cs` + `Ntsc.cs` + `CrtScreen.cs` 架構重構

**分析**: linearBuffer 2.8 MB 遠超 L1/L2，CrtScreen.Render 讀取時全從 L3/RAM。
但 Parallel.For 多核並行帶來的加速可能遠超 L1 cache hit 的收益。
重構風險高且收益不確定，暫不實作。

---

### S06 — Parallel.For Partitioner 優化 ❌ REJECTED

**狀態**: ❌ 不採用 — **-1.28%** 回歸 (107.17 → 105.80 FPS)
**框架相容性**: .NET 4.8.1 ✅ | .NET 10 ✅ — `Partitioner.Create` 自 .NET 4.0 即可用
**影響範圍**: `CrtScreen.cs` Render() — Parallel.For

**根因分析**: TPL 預設分區策略在 840 行的工作量下已經足夠智能。
自訂 `Partitioner.Create` 反而因為固定 chunk size 無法動態調整，
在不同核心完成速度不同時造成 load imbalance，效能反降。

**Benchmark** (S01+S02+S06):
| 回合 | FPS |
|:----:|:---:|
| Run 1 (JIT, 不計) | 105.32 |
| Run 2 | 105.53 |
| Run 3 | 106.07 |
| **平均** | **105.80** |

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
| Math.Clamp | .NET 4.8.1 無此 API（需 .NET Core 2.0+）。且 `Math.Max`/`Math.Min` 在 .NET 4.8.1 非 intrinsic，反而更慢（見 S04） | Avalonia 版 (.NET 6+) 直接使用 `Math.Clamp` 即可，JIT 會編譯為 MAXSS+MINSS |

---

## Avalonia (.NET 10) 額外可用優化（.NET 4.8.1 無法使用）

| 項目 | 最低版本 | 說明 |
|------|:--------:|------|
| `NativeMemory.AlignedAlloc` | **.NET 6** | 32-byte 對齊所有 SIMD buffer（AVX2 最佳）。.NET 4.8.1 僅有 `Marshal.AllocHGlobal`（8-16B 對齊） |
| `Vector.ShiftLeft` | **.NET 7** | 取代 S02 像素打包的 `* 256` / `* 65536` 乘法 workaround，直接用硬體位元移位指令 |
| `Vector128/256` HW Intrinsics | **.NET Core 3.0** | 可使用 `Avx2.PackUnsignedSaturate`、`Sse2.PackUnsignedSaturate128` 等做最佳像素打包，繞過 `Vector<T>` 抽象層 |
| `Math.Clamp` | **.NET Core 2.0** | JIT 直接編譯為 MAXSS + MINSS，取代 S04 的 `if/else` 分支寫法。**注意**：.NET 4.8.1 的 `Math.Max/Min` 非 intrinsic，不可用 |
| AVX-512 (`Vector512<T>`) | **.NET 8** | 若 CPU 支援 AVX-512，Vector\<float\>.Count=16，CrtScreen 內迴圈寬度翻倍 |
