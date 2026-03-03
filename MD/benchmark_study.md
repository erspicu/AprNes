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

| # | 執行環境 | 總 Frame 數 | FPS |
|---|----------|------------|-----|
| 1 | .NET Framework 4.6.1 JIT | 5,320 | **532.0** |
| 2 | .NET 8 RyuJIT | 7,575 | **757.5** |
| 3 | Native AOT（NesCoreNative.dll） | 5,664 | **566.4** |

> 以 .NET Framework 4.6.1 JIT 為基準（100%）

| 執行環境 | 相對效能 |
|----------|---------|
| .NET Framework 4.6.1 JIT | 100% |
| .NET 8 RyuJIT | **+42.4%** ↑ |
| Native AOT | **+6.5%** ↑ |

---

## .NET 8：JIT 與 AOT 的比較

### JIT（Just-In-Time Compilation）— RyuJIT

**運作原理：**  
程式啟動時以 IL（Intermediate Language）形式載入，執行過程中由 JIT 編譯器將 hot path 編譯為原生機械碼。.NET 8 的 RyuJIT 加入了 **Profile-Guided Optimization（PGO）**，能根據實際執行行為動態調整編譯策略。

**優點：**
- ✅ 執行時掌握真實 hot path，針對最常走的分支做深度最佳化
- ✅ PGO / Tiered Compilation：第一層快速編譯，第二層根據 profiling 結果重新最佳化
- ✅ 可利用執行時已知資訊（如虛擬方法的實際型別）做 devirtualization
- ✅ 受益於 .NET 8 對 RyuJIT 的持續改進（SIMD、loop unrolling、向量化等）

**缺點：**
- ❌ 啟動時有 warm-up 時間（冷啟動較慢）
- ❌ 需要完整 .NET 8 Runtime 環境
- ❌ 部署包含 runtime，體積較大

### AOT（Ahead-Of-Time Compilation）— Native AOT

**運作原理：**  
在 **編譯期** 將 C# 直接編譯為平台原生機械碼（.dll / .exe），執行時不需要 JIT 編譯器，也不依賴 CLR Runtime。

**優點：**
- ✅ 啟動速度極快，無 JIT warm-up
- ✅ 記憶體佔用較少（無 JIT 基礎設施）
- ✅ 適合部署到嵌入式、容器或啟動時間敏感的場景
- ✅ 不依賴目標機器安裝 .NET Runtime（自帶 minimal runtime）

**缺點：**
- ❌ **缺乏執行時 PGO**：編譯時無法預知哪條路徑最熱，最佳化深度不如 JIT
- ❌ 部分反射、動態型別功能受限（需 trim-friendly 寫法）
- ❌ 對於 tight loop 密集計算，JIT 的動態重最佳化往往比靜態 AOT 更具優勢
- ❌ 產出二進位較大（包含 minimal runtime）

### 本測試的 JIT vs AOT 結論

```
.NET 8 RyuJIT : 757.5 FPS
Native AOT     : 566.4 FPS
差距            : -191.1 FPS（AOT 慢約 25%）
```

NES 模擬器的主迴圈是典型的 **CPU-bound tight loop**（每 frame 需執行數千個 CPU 指令週期），正是 JIT PGO 最能發揮效益的場景。AOT 在沒有 profiling 資訊的情況下，靜態編譯的結果無法達到 RyuJIT 動態最佳化的水準。

---

## 三種執行環境總比較

```
.NET Framework 4.6.1 JIT  ████████████████████████  532 FPS  (基準)
Native AOT                 ██████████████████████████  566 FPS  (+6.5%)
.NET 8 RyuJIT              ██████████████████████████████████  758 FPS  (+42.4%)
```

### 各場景適用時機

| 執行環境 | 最適場景 | 不適場景 |
|----------|---------|---------|
| .NET Framework 4.6.1 JIT | 現有 Windows 專案維護、WinForms 傳統應用 | 高效能需求、跨平台 |
| .NET 8 RyuJIT | **持續執行的高效能應用**（遊戲、模擬器、伺服器） | 啟動時間極度敏感、無 Runtime 環境 |
| Native AOT | CLI 工具、容器微服務、快速啟動場景 | CPU-bound 長時間運算 |

### 心得總結

1. **.NET 8 RyuJIT 是三者中效能最強的**：對 AprNes 這類 CPU-bound 模擬器，PGO + Tiered Compilation 帶來 +42% 的效能提升，遠超預期。

2. **Native AOT 並非萬能的效能銀彈**：AOT 的優勢在「啟動速度」和「部署便利性」，而非「運算吞吐量」。對於需要長時間執行的密集計算，JIT 的動態最佳化能力更強。

3. **.NET Framework 雖舊，仍具競爭力**：4.6.1 的 JIT 雖無現代 PGO，但 532 FPS 仍然流暢，對 NES（60 FPS 目標）有 8× 以上的 headroom。

4. **遷移 .NET 8 有實質效益**：若只是為了效能，從 .NET Framework 遷移到 .NET 8（保持 JIT）可獲得近乎免費的 42% 效能紅利，代價是需要目標機器安裝 .NET 8 Runtime。

5. **AOT 的定位是部署靈活性，而非效能極致**：在本專案中，NesCoreNative.dll（AOT）的存在意義是「讓不同語言/環境能直接呼叫 NES 核心」，而非取代 JIT 執行效能。

---

*測試工具：`benchmark.bat` / `benchmark.ps1`，結果儲存於 `benchmark.txt`*
