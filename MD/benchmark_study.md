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

## 備註

> 本測試使用 `Controller Test (USA).nes`（Mapper 0，NROM），為純 CPU-bound 測試場景，
> 關閉 LimitFPS 以測試最大吞吐量。實際遊戲效能因 ROM 複雜度而異，但相對排名應維持一致。

*測試工具：`benchmark.bat` / `benchmark.ps1`，結果儲存於 `benchmark.txt`*
