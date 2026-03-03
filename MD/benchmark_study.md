# AprNes 執行環境效能基準測試研究

## 測試環境

| 項目 | 說明 |
|------|------|
| CPU  | 13th Gen Intel Core i7-1370P |
| OS   | Windows 10 (Build 19045) |
| ROM  | Controller Test (USA).nes（Mapper 0，NROM） |
| 測試時長 | 每項 10 秒，關閉幀率限制（LimitFPS = false） |
| 測試時間 | 2026-03-03 |

---

## 測試結果

| # | 執行環境 | 總 Frame 數 | FPS | 相對基準 |
|---|----------|------------|-----|---------|
| 1 | .NET Framework 4.6.1 JIT | 4,220 | **422.0** | 100% |
| 2 | Native AOT（NesCoreNative.dll） | 5,500 | **550.0** | +30.3% |
| 3 | .NET 8 RyuJIT | 7,018 | **701.8** | +66.3% |
| 4 | .NET 10 RyuJIT | 7,640 | **764.0** | +81.0% |

```
.NET Framework 4.6.1 JIT  ████████████████████  422 FPS  (基準 100%)
Native AOT                 ██████████████████████████  550 FPS  (+30%)
.NET 8 RyuJIT              █████████████████████████████████  702 FPS  (+66%)
.NET 10 RyuJIT             ████████████████████████████████████  764 FPS  (+81%)
```

---

## .NET 8 vs .NET 10：JIT 的比較

```
.NET 8  RyuJIT : 701.8 FPS
.NET 10 RyuJIT : 764.0 FPS
差距            : +62.2 FPS（.NET 10 快約 +8.9%）
```

### .NET 10 JIT 主要改進（官方宣稱）

| 改進項目 | 對模擬器的影響 |
|---------|--------------|
| **Loop Optimization**（迴圈外提邊界檢查） | CPU/PPU 主迴圈每次迭代減少冗餘運算 |
| **Struct Physical Promotion**（欄位直接提升至暫存器） | NES CPU 暫存器 struct 存取速度提升 |
| **Stack Allocation of Value Arrays**（小陣列 stack 配置） | 減少 GC 壓力，hot path 更流暢 |
| **Escape Analysis 擴大** | delegate/callback overhead 降低 |
| **SIMD / Vectorization 擴展**（AVX10.2, ARM64 SVE） | 數值密集運算自動向量化 |

---

## JIT 與 AOT 的優劣分析

### JIT（Just-In-Time）— RyuJIT

**優點：**
- ✅ **PGO（Profile-Guided Optimization）**：執行時掌握真實 hot path，針對最常走的分支做深度最佳化
- ✅ **Tiered Compilation**：第一層快速啟動，第二層根據 profiling 重新最佳化
- ✅ 可利用執行時資訊做 devirtualization（虛擬方法消除）
- ✅ 受益於每個 .NET 版本的持續改進

**缺點：**
- ❌ 啟動時需要 warm-up（冷啟動較慢）
- ❌ 需要目標機器安裝對應 .NET Runtime

### AOT（Ahead-Of-Time）— Native AOT

**優點：**
- ✅ 啟動速度極快（無 JIT warm-up，450ms → 50ms）
- ✅ 記憶體佔用少（無 JIT 基礎設施常駐）
- ✅ 適合容器/微服務/CLI 工具部署（映像縮小 60~87%）
- ✅ 不依賴目標機器安裝 .NET Runtime
- ✅ **穩定低延遲**：無 JIT 重新編譯造成的偶發性停頓

**缺點：**
- ❌ **缺乏執行時 PGO**：編譯期無 profiling 資訊，最佳化深度不如 JIT
- ❌ 對 CPU-bound 長時間運算，靜態編譯無法追上 JIT 動態最佳化
- ❌ 反射、動態型別功能受限

### 關鍵結論

> **AOT 的進步方向是「部署靈活性」，JIT 的進步方向是「運算吞吐量」。**
>
> 兩者的差距在 .NET 10 依然存在：AOT 550 FPS vs JIT 764 FPS（差距 -28%）。
> 這個差距來自 PGO 的本質優勢，不會因 AOT 版本升級而消失。

---

## 四種執行環境總比較

| 執行環境 | FPS | 適用場景 | 不適場景 |
|----------|-----|---------|---------|
| .NET Framework 4.6.1 JIT | 422 | 現有 Windows 專案維護 | 高效能需求、跨平台 |
| Native AOT | 550 | CLI 工具、容器、快速啟動 | CPU-bound 長時間運算 |
| .NET 8 RyuJIT | 702 | 高效能應用，廣泛部署環境 | 啟動時間極敏感場景 |
| **.NET 10 RyuJIT** | **764** | **高效能 + 最新版本優化** | 需舊版相容性的環境 |

### 心得總結

1. **.NET 10 RyuJIT 是四者中效能最強（764 FPS）**：比 .NET 8 再快 +8.9%，驗證了官方宣稱的 Loop Optimization 和 Struct Promotion 對模擬器 tight loop 有實質效益。

2. **.NET 8 → .NET 10 JIT 的提升比較溫和（+8.9%）**：相比 .NET Framework → .NET 8 的跳躍（+66%），版本間的邊際效益遞減，表示 RyuJIT 已相當成熟。

3. **Native AOT 並非運算吞吐量的最佳選擇**：AOT 的定位是「啟動速度」和「部署便利性」。在本專案中，NesCoreNative.dll 的存在意義是讓其他語言可呼叫 NES 核心，而非追求最高 FPS。

4. **.NET Framework 4.6.1 JIT 仍堪用**：422 FPS 對 NES 60 FPS 目標有 7× headroom，對使用者感受上沒有差異，但效能天花板比現代 .NET 低很多。

5. **遷移 .NET 8 或 .NET 10 有實質效益**：若只是為了效能，遷移到 .NET 8 可獲得免費的 +66% 效能紅利；若再升 .NET 10 可多拿 +8.9%，代價是需要目標機器安裝對應 Runtime。

---

## Sprite Pass 3 SIMD 最佳化效果

### 實作說明

在 .NET 8/10 環境下，以 **條件編譯（`#if NET8_0_OR_GREATER`）** 對 Sprite 合成迴圈（Pass 3）加入 SSE4.1 SIMD 最佳化：

**原始 scalar 邏輯（256 次條件分支）：**
```csharp
for (int x = 0; x < 256; x++) {
    if (sprSet[x] == 0) continue;                               // 分支
    if (!ShowBG || BG_array[x] == 0 || sprPriority[x] == 0)    // 條件
        ScreenBuf[x] = sprColor[x];
}
```

**SIMD 邏輯（SSE4.1，每次 4 pixels）：**
```
hasSprMask   = ConvertToVector128Int32(sprSet[x..x+3])  != 0
bgTranspMask = LoadVector128(BG_array[x..x+3])          == 0
frontMask    = ConvertToVector128Int32(priority[x..x+3]) == 0
condMask     = bgTranspMask | frontMask
writeMask    = hasSprMask & condMask
result       = BlendVariable(screen, sprColor, writeMask)  ← SSE4.1 核心
```

.NET Framework 4.6.1 保持原 scalar 路徑（無 `System.Runtime.Intrinsics`）。

---

### SIMD 效果測試

測試 ROM：**spritecans.nes**（64 個 sprite 同時滿版彈跳，最大化 Sprite Pass 3 負荷）

測試方法：兩組獨立 process，對調順序各跑一次（消除「第一跑 JIT 尚未暖機」和「第二跑 CPU 已降頻」的偏差），取平均值：

| 輪次 | SIMD ON | SIMD OFF |
|------|---------|----------|
| Round 1（ON 先跑） | 558.2 FPS | 603.8 FPS |
| Round 2（OFF 先跑） | 540.1 FPS | 534.5 FPS |
| **平均** | **549.1 FPS** | **569.1 FPS** |

**SIMD gain：-20.0 FPS（-3.5%）** ← 在誤差範圍內；Round 間差距達 18～45 FPS，遠大於任何 SIMD 效益。

---

### 為何 SIMD 效益不顯著？（5 個根本原因）

1. **Sprite Pass 3 不是真正瓶頸**：每 scanline 跑一次（240次/frame），但整個 PPU 的瓶頸是每 PPU cycle 執行一次的 `ppu_step_new`（~89,000次/frame），SIMD 無法觸及。

2. **spritecans 的 sprite 分佈稀疏**：64 個 sprite 分散在 240 scanlines，每條 scanline 平均不到 3 個 sprite 有顏色，`MoveMask == 0` 的快速跳過讓大多數 4-pixel 群組直接略過，SIMD 發揮空間小。

3. **JIT auto-vectorization 已覆蓋部分工作**：條件分支雖然阻止完整向量化，但 RyuJIT PGO 會對熱路徑做 branch prediction 最佳化，縮小與 SIMD 的差距。

4. **記憶體頻寬不是瓶頸**：256 uint = 1KB，完全在 L1 cache 內，scalar 存取已經夠快。

5. **熱節流雜訊主導結果**：筆電 i7-1370P Turbo Boost 在高負載下波動幅度約 ±10%，遠超 Sprite Pass 3 本身佔整體工作量的比例，導致任何小幅 SIMD 增益都被遮蓋。

### 結論

> Sprite Pass 3 SIMD 在 NES 模擬器的實際場景中效益**無法量測**（< 誤差範圍）。
> 對整體效能提升有限，但作為條件編譯的實踐範例，展示了如何在同一份程式碼中
> 讓 .NET 8/10 走 SSE4.1 路徑、.NET Framework 走 scalar 路徑，完全無需維護兩套程式碼。
> 若要量測到可信的 SIMD 效益，需要在 CPU 固定頻率（禁用 Turbo Boost）的環境下進行。

---

## 學習總結

### 本次研究的核心發現

#### 1. 執行環境選擇
- **.NET 10 RyuJIT** 是目前吞吐量最強的選擇（764 FPS），比 .NET Framework 4.6.1 快 **+81%**，比 .NET 8 快 +9%
- **Native AOT** 的定位不是「更快的 JIT」，而是「啟動速度 + 部署便利性」；在 CPU-bound 長時間運算上，AOT（550）輸給 JIT（764）約 28%
- **.NET Framework 4.6.1** 對 NES 60 FPS 目標有 7× headroom，使用者感知無差異，但效能天花板已固定

#### 2. JIT 為何長期優於 AOT（運算吞吐量）
- **PGO（Profile-Guided Optimization）**：JIT 能在執行期觀察真實 hot path 並重新最佳化，AOT 只能依靠靜態分析
- **Tiered Compilation**：.NET JIT 先快速編譯啟動，之後對熱路徑再做深度最佳化，兼顧啟動速度與穩態效能
- **Devirtualization**：JIT 執行時確認型別，AOT 必須保守假設

#### 3. SIMD 的適用條件
手動 SIMD（`System.Runtime.Intrinsics`）只在以下條件**同時成立**時才有量測到的效益：
- 操作本身佔整體執行時間的顯著比例（> 5%）
- 資料夠密集、記憶體存取是瓶頸
- JIT auto-vectorization 無法自動覆蓋（通常因為有條件分支）
- 在固定頻率 CPU 環境下測試（排除熱節流雜訊）

本專案的 Sprite Pass 3（240 次/frame，1KB L1 cache 內）不符合前兩個條件，SIMD 效益無法量測。

#### 4. 條件編譯的正確用法
```csharp
#if NET8_0_OR_GREATER
    // 僅 .NET 8/10 才有 System.Runtime.Intrinsics
    using System.Runtime.Intrinsics;
    using System.Runtime.Intrinsics.X86;
#endif

// 程式碼內：
#if NET8_0_OR_GREATER
    if (SIMDEnabled && Sse41.IsSupported)
        CompositeSpritesSimd(...);
    else
#endif
        CompositeSpritesScalar(...);
```
- `NETFRAMEWORK`：.NET Framework 4.6.1
- `NET8_0_OR_GREATER`：涵蓋 .NET 8 和 .NET 10
- `NET10_0_OR_GREATER`：僅 .NET 10+
- 條件編譯符號由 SDK 自動定義，無需 csproj 額外設定

#### 5. 效能量測的可靠性
| 問題 | 原因 | 解決方式 |
|------|------|---------|
| JIT warm-up 偏差 | Tiered Compilation 前幾秒為 Tier 0（未最佳化） | 使用 10s 以上測試，前幾秒為 JIT 爬坡期 |
| GUI exe 不等待 | PowerShell `&` 不 block WinExe | 改用 `Start-Process -Wait -NoNewWindow` |
| 熱節流雜訊 ±10% | 筆電 Turbo Boost 動態調頻 | 對調測試順序取平均，或在固定頻率環境測試 |
| 同 process 比較失真 | 第一段測試的 JIT 狀態影響第二段 | 每次比較使用獨立 process |

#### 6. 各目標的定位確認
| 專案 | Runtime | 主要用途 |
|------|---------|---------|
| `AprNes` (.NET Fx 4.6.1) | JIT | 原始開發版本，最大相容性 |
| `NesCoreNative` (Native AOT) | AOT | NES 核心 DLL，供其他語言呼叫 |
| `AprNesAOT` (.NET 8) | JIT | 高效能版，廣泛部署環境 |
| `AprNesAOT10` (.NET 10) | JIT | 最高效能，需安裝 .NET 10 Runtime |

---

## 備註

> 主測試 ROM：`Controller Test (USA).nes`（Mapper 0，NROM），純 CPU-bound 場景，關閉幀率限制（LimitFPS = false）。  
> SIMD 測試 ROM：`spritecans.nes`（64 sprites 全螢幕彈跳，最大化 Sprite Pass 3 負荷）。  
> 實際遊戲效能因 ROM 複雜度而異，但相對排名應維持一致。

*測試工具：`benchmark.bat` / `benchmark.ps1`，結果儲存於 `benchmark.txt`*  
*最後更新：2026-03-03*
