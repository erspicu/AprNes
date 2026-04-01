# AprNes vs AprNesAvalonia — Debug / Release 效能對比

**日期**: 2026-04-01

## 測試目的

比較 AprNes（.NET Framework 4.8, WinForms）與 AprNesAvalonia（.NET 10, Avalonia）在 Debug 與 Release 組態下的效能差異，驗證 .NET 10 TieredPGO 對 Debug/Release 落差的影響。

---

## 測試條件

| 項目 | 設定 |
|------|------|
| 測試 ROM | ny2011.nes (Mapper 004, MMC3) |
| AccuracyOptA | ON |
| AnalogMode | ON (Ultra Analog + CRT) |
| AnalogOutput | RF |
| Audio DSP | Mode 2 (Modern Stereo) |
| 音效播放 | OFF (headless, DSP 處理完後丟棄) |
| 畫面顯示 | OFF (headless, 無 GPU rendering) |
| 測試時長 | 30 秒 / 回合 |
| 協議 | JIT warmup 10s (discard) → 4 resolutions × 30s |

| 版本 | 框架 | Runtime |
|------|------|---------|
| AprNes | .NET Framework 4.8.1 | CLR JIT (單層) |
| AprNesAvalonia | .NET 10 | RyuJIT + TieredPGO (Release) / Tier-0 only (Debug) |

---

## 測試結果

### FPS 總覽

| AnalogSize | 解析度 | AprNes Debug | AprNes Release | Avalonia Debug | Avalonia Release |
|:----------:|:------:|:------------:|:--------------:|:--------------:|:----------------:|
| 2x | 512×420 | 117.31 | 116.14 | 34.99 | **132.97** |
| 4x | 1024×840 | 109.21 | 105.99 | 33.05 | **120.83** |
| 6x | 1536×1260 | 86.51 | 83.55 | 26.68 | **95.76** |
| 8x | 2048×1680 | 74.86 | 73.06 | 25.13 | **78.37** |

### Debug/Release 倍率

| AnalogSize | AprNes (Release/Debug) | Avalonia (Release/Debug) |
|:----------:|:----------------------:|:------------------------:|
| 2x | 0.99x | **3.80x** |
| 4x | 0.97x | **3.66x** |
| 6x | 0.97x | **3.59x** |
| 8x | 0.98x | **3.12x** |

### 跨平台對比 (Release vs Release)

| AnalogSize | AprNes Release | Avalonia Release | Avalonia 加速比 |
|:----------:|:--------------:|:----------------:|:---------------:|
| 2x | 116.14 | **132.97** | **+14.5%** |
| 4x | 105.99 | **120.83** | **+14.0%** |
| 6x | 83.55 | **95.76** | **+14.6%** |
| 8x | 73.06 | **78.37** | **+7.3%** |

### 跨平台對比 (Debug vs Debug)

| AnalogSize | AprNes Debug | Avalonia Debug | Avalonia 相對 |
|:----------:|:------------:|:--------------:|:-------------:|
| 2x | 117.31 | 34.99 | **-70.2%** |
| 4x | 109.21 | 33.05 | **-69.7%** |
| 6x | 86.51 | 26.68 | **-69.2%** |
| 8x | 74.86 | 25.13 | **-66.4%** |

---

## 分析

### .NET Framework 4.8: Debug ≈ Release

AprNes 的 Debug 和 Release 效能幾乎一樣（差距 < 3%）。.NET Framework 的 CLR JIT 是單層架構，Debug 組態僅多了 `[Debuggable]` attribute 和少量優化抑制，對 unsafe 指標密集的模擬器核心影響極小。

### .NET 10: Debug << Release（3.1~3.8 倍差距）

AprNesAvalonia 的 Debug 版慢了 3-4 倍，原因：

1. **TieredPGO 完全關閉** — Release 才啟用 Tier-0 → profile → Tier-1 + PGO 重編譯，是最大的加速來源
2. **Inlining 抑制** — Debug 幾乎不做 method inlining，NesCore 大量小方法（tick、ppu_step、apu_step）無法內聯
3. **SIMD 不展開** — `Vector128/256` intrinsics 在 Debug 走 fallback 路徑
4. **Bounds check 不消除** — Release 的 JIT 會靜態分析消除陣列/指標的邊界檢查

### Release 對 Release: .NET 10 勝出 7~15%

在 Release 組態下，AprNesAvalonia 比 AprNes 快 7-15%，得益於：
- RyuJIT + TieredPGO 的熱路徑最佳化
- .NET 10 更好的 SIMD codegen
- 更新的 GC（雖然模擬器核心幾乎不產生 GC 壓力）

### 結論

> **使用 .NET 10 開發時，必須用 Release 組態測試效能。Debug 組態的數字不具參考價值，會低估實際效能 3-4 倍。**
>
> 這與 .NET Framework 的習慣不同——.NET Framework Debug/Release 差異可忽略，但 .NET 10 不行。
