# Catch-up Architecture Study Notes

**來源**: `etc/catchup/01.txt` ~ `16.txt`（16 篇技術分析文件）
**日期**: 2026-04-09
**分析者**: Claude + 開發者評估

---

## 一、文件總覽

16 篇文件構成一個完整的技術諮詢系列，主題是：**Catch-up（事件驅動）架構 vs Polling（逐 tick 輪詢）架構**在 NES 模擬器中的取捨。

| 篇章 | 主題 | 核心論點 |
|------|------|---------|
| 01-02 | 理論基礎 | 為什麼 cycle-accurate 模擬昂貴；FSM 的離散數學形式化 |
| 03 | 微觀優化實例 | DMC 通道的 branchless 算術 + 硬體行為修正 |
| 04 | 優化分類學 | FSM 優化的三個維度：狀態最小化、轉移函數簡化、Catch-up |
| 05 | 現有架構分析 | MasterClockTick 的 5 個觸發點詳解 |
| 06-08 | Catch-up 實作計畫 | 四階段遷移路線圖 + 正確性保證 + 效能量化 |
| 09-10 | 並行化陷阱 | 為什麼多執行緒核心行不通；為什麼逐幀 batch 會壞 |
| 11-12 | APU 同步難題 | DMC DMA 時間悖論 + 干擾場景量化分析 |
| 13 | 靜態變數風險 | 髒讀問題 + Save State 序列化風險 |
| 14-15 | 精確度 vs 效能 | Polling 天然精確；完美 Catch-up 是地獄級工程 |
| 16 | 物理極限 | 沒有模擬器能 100% 通過所有測試（類比電路、晶片版本差異） |

---

## 二、關鍵技術論點與自評

### 2.1 Polling（輪詢）架構的優勢

**文件論點**: Polling 是「物理沙盒」— 只要逐 clock 的 FSM 正確，結果自動正確。不需要預測未來。

**自評**: **完全同意。** 這正是 AprNes 移植 TriCNES timing model 後能達成 174/174 + 136/136 滿分的原因。Polling 的代價是效能（每幀 357,368 次 MasterClockTick dispatch），但收穫是零預測錯誤。我們的 PerfView 分析驗證了 `run()` 佔 20.6% Exc 正是這個 dispatch 成本。

### 2.2 Catch-up 架構的效能估計

**文件論點**: 40-60% 計算量削減，潛在 2-3x FPS 提升。三大來源：
1. 消除 MasterClockTick dispatcher（~20%）
2. Cache 局部性改善（~15-20%）
3. VBlank 數學快進（~10-15%）

**自評**: **估計偏樂觀。** 針對 AprNes 的具體情況：
- 我們已經做了大量微觀優化（SWAR、branchless、method extraction），ceiling 更低
- ppu_step_new 佔 53.9%，即使 Catch-up 也無法跳過可見掃描線的逐 dot 計算
- VBlank 快進只省 20/262 = 7.6% 的 PPU 時間
- Cache 局部性改善真實存在但難以量化

**我的估計**: AprNes 採用 Catch-up 實際提升約 **25-40%**，不是 40-60%。這把 ~104 FPS 提升到 ~130-145 FPS，有意義但不如 .NET 10 遷移（已測得 119 FPS，且 PGO 能 inline 所有熱區方法）。

### 2.3 Catch-up 的正確性挑戰

**文件論點**: 三大死亡陷阱 — 中斷預測失敗、Mapper-PPU 耦合（MMC3 A12）、Open Bus 衰減。

**自評**: **完全同意，且難度被低估了。** 我們的實戰經驗：
- MMC3 的 A12 邊緣偵測依賴每 dot 的 `ppuAddressBus` 狀態，Catch-up 必須精確追蹤
- DMC DMA 的奇偶相位對齊（GET vs PUT cycle）需要數學公式預測，我們花了 BUGFIX53-56 四個修復才搞對
- OAM DMA 的 halt parity 依賴 `mcApuPutCycle` 的即時狀態
- 這些在 Polling 模型下自然正確，遷移到 Catch-up 需要為每一項寫預測公式

### 2.4 多執行緒核心 — 不可行

**文件論點**: CPU/PPU/APU 之間的高頻雙向依賴使並行化不切實際。背景 thread 應用於後處理（渲染、音效）。

**自評**: **完全同意。** 我們的實驗驗證了這一點：
- Digital async double buffer（filter + GDI 並行化）因為 deadlock 問題而 revert
- Analog async double buffer 成功運作，因為 NTSC+CRT 是純數學計算，無狀態依賴
- 核心的 `ppu_step_new()` 讀寫數十個 static fields，無法安全並行化

### 2.5 DMC 硬體行為修正

**文件論點（File 03）**: 真實 NES 硬體的 DMC 不做 clamp（截斷到 0/127），而是 **discard**（整個 +2/-2 操作不執行）。

**自評**: **經驗證確認正確。** 交叉比對 TriCNES 原始碼：
```csharp
// TriCNES (line 914-923) — discard 模式
if (APU_DMC_Output <= 125) APU_DMC_Output += 2;  // 只在不溢出時才加
if (APU_DMC_Output >= 2)   APU_DMC_Output -= 2;  // 只在不下溢時才減
```

**AprNes 目前的實作是 clamp（截斷），不是 discard。** 這是一個潛在的精確度 bug：
```csharp
// AprNes 目前 — clamp 模式（不正確）
int nextValue = dmcvalue + ((dmcshiftregister & 1) << 2) - 2;
if ((uint)nextValue > 0x7F) nextValue = (nextValue >> 31) == 0 ? 0x7F : 0;
dmcvalue = nextValue;
```
應改為：
```csharp
// 正確的 discard 模式
int delta = ((dmcshiftregister & 1) << 2) - 2;
int nextValue = dmcvalue + delta;
if ((uint)nextValue <= 0x7F) dmcvalue = nextValue; // 溢出時不更新
```
目前 blargg 測試未檢測到此差異，但在特定 DMC 樣本播放場景下可能產生音質差異。

### 2.6 靜態變數與 Catch-up 的衝突

**文件論點**: all-static 架構在 Catch-up 下會產生「髒讀」— CPU 讀到 PPU 尚未更新的舊值。

**自評**: **嚴重問題。** AprNes 的整個 NesCore 是一個 `partial class` 上的 static fields。在 Catch-up 下：
- CPU 讀 `scanline`（PPU 的狀態）時，PPU 可能還停在上一個同步點
- 所有 PPU register read handler（`ppu_r_2002` 等）必須先 `SyncPPU()` 再讀
- 目前沒有任何存取控制機制防止直接讀取

遷移難度：**高**。需要在所有 IO handler 入口加 sync barrier，並審計每一個跨子系統的 static field 存取。

### 2.7 物理極限

**文件論點**: 沒有模擬器能 100% 通過所有測試 — 類比電路衰減、電源上電隨機性、晶片版本差異。

**自評**: **正確且有啟發性。** 我們的 `open_bus_decay_timer = 77777` 就是「固定數位計時器近似類比電容放電」的典型例子。不同硬體上的衰減速率可能差數倍。

---

## 三、對 AprNes 的策略建議

### 3.1 Catch-up vs .NET 10 遷移 — 投資報酬比較

| 策略 | 預估工期 | 預估提升 | 風險 |
|------|---------|---------|------|
| **Catch-up 重構** | 數週～月 | +25-40% (~130-145 FPS) | 高：136/136 可能回歸 |
| **.NET 10 遷移** | 已完成基礎 | +14% 已測（119 FPS），PGO 潛力更高 | 低：相同 NesCore |
| **兩者結合** | 月+ | +50-80% (~155-190 FPS) | 極高 |

**.NET 10 遷移是更好的第一步**：
- 已有 AprNesAvalonia 基礎（119 FPS baseline）
- PGO 把 ppu_step_new / apu_step / cpu_step_one_cycle 全部成功 inline（已驗證）
- 不需要改動任何 timing 邏輯，零回歸風險
- Catch-up 如果要做，在 .NET 10 上做效益更大（inline + catch-up 疊加）

### 3.2 可立即採用的改善（不需要 Catch-up）

1. **DMC discard 修正**（File 03 指出）— 簡單改動，提升硬體精確度
2. **VBlank 期間的微優化** — 雖然不能跳過 PPU step，但 VBlank 期間的分支預測模式更簡單
3. **持續在 .NET 10 上做效能分析** — PGO inline 後的瓶頸分佈可能完全不同

### 3.3 Catch-up 如果要做 — 最小可行方案

如果未來決定實施 Catch-up，建議的最小切入點：
1. **僅 VBlank 快進** — scanline 241-260 期間，PPU 不渲染，可安全跳過逐 dot 步進
2. **保留逐 dot 步進做 visible scanlines** — 不碰渲染邏輯，避免精確度回歸
3. **在 IO handler 加 sync barrier** — `ppu_r_2002()` 等入口呼叫 `SyncPPU()`

這是風險最低的部分 Catch-up，預估省 ~7-8% PPU 時間。

---

## 四、整體評價

這 16 篇文件的技術深度和準確度**整體很高**，對 Polling vs Catch-up 的分析全面且有理有據。主要的偏差是：

1. **效能估計偏樂觀** — 40-60% 的數字來自一般情境，AprNes 已有大量微觀優化，實際 ceiling 更低
2. **工程難度被輕描淡寫** — "四階段遷移" 聽起來有序，但 DMC DMA 相位預測和 MMC3 A12 同步的實際除錯可能耗費數週
3. **DMC discard 行為的指出非常有價值** — 這是一個真實的精確度 bug，值得立即修正
4. **.NET 10 遷移路徑被低估** — PGO 的 inline 效果（我們已驗證所有熱區方法都成功 inline）提供了不需要架構變更的顯著提升

**最重要的結論**: AprNes 的 Polling 架構是精確度的保證，不應為了效能輕易放棄。效能瓶頸的最佳解法是 .NET 10 遷移 + 選擇性 VBlank 快進，而非全面 Catch-up 重構。
