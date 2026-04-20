# C# JIT 與 I-Cache 優化教學

> 從 Game Loop 出發，一路談到 CPU 快取階層、冷熱路徑拆分、多核流水線、執行緒親和性，並結合本專案（AprNes NES 模擬器）實戰的 PMU / ETW 分析流程。
>
> 本文整理自多輪 Q&A 討論，重新編排為教學文件。目標讀者是正在撰寫遊戲／模擬器／高效能服務、並且想從 JIT 行為與 CPU 微架構層級理解效能的 C# 開發者。

---

## 目錄

1. [Game Loop：一切的起點](#1-game-loop一切的起點)
2. [Inline 與 I-Cache 的拉鋸戰](#2-inline-與-i-cache-的拉鋸戰)
3. [如何找到最優解：量化工具與階梯式策略](#3-如何找到最優解量化工具與階梯式策略)
4. [熱路徑過剩：當核心邏輯本身就塞不下 L1](#4-熱路徑過剩當核心邏輯本身就塞不下-l1)
5. [多核流水線：用多份 I-Cache 協作](#5-多核流水線用多份-i-cache-協作)
6. [I-Cache 究竟是哪一層快取？](#6-i-cache-究竟是哪一層快取)
7. [核心間通訊的代價](#7-核心間通訊的代價)
8. [在 C# 裡確保 Thread 真的分到不同核心](#8-在-c-裡確保-thread-真的分到不同核心)
9. [這些觀念在其他語言通用嗎？](#9-這些觀念在其他語言通用嗎)
10. [延伸到網路服務的高併發場景](#10-延伸到網路服務的高併發場景)
11. [實戰附錄：AprNes 的 JIT / I-Cache 分析流程](#11-實戰附錄aprnes-的-jit--i-cache-分析流程)

---

## 1. Game Loop：一切的起點

### Q：使用 C# 開發遊戲或模擬器時，為何一定會有一個被稱為「Game Loop」的核心迴圈？它的角色是什麼？

Game Loop（遊戲迴圈）是所有即時互動程式的靈魂。一般的 Console 或網頁表單是**被動式**——使用者有動作才反應；而遊戲／模擬器是**主動式**——即使玩家不操作，世界也要持續運作（樹葉會動、NPC 會巡邏、模擬器的 CPU cycle 要持續推進）。這就需要一個持續轉動的齒輪。

### 核心三部曲

```text
while (isRunning) {
    1. Process Input   ── 讀取鍵盤／搖桿／滑鼠
    2. Update State    ── 套用物理規則、AI 邏輯、狀態機推進
    3. Render          ── 把運算結果畫到螢幕
}
```

最小骨架：

```csharp
bool isRunning = true;
while (isRunning)
{
    var input = GetPlayerInput();
    UpdateGameLogic(input);
    DrawToScreen();
    // 必要時在這裡控制 frame rate（Thread.Sleep / vsync / 自行計時）
}
```

### 不同框架中的體現

| 框架 | 你要寫的部分 | 由誰提供主迴圈 |
| --- | --- | --- |
| **Unity** | 只寫 `Update()` / `FixedUpdate()` / `LateUpdate()` | 引擎 |
| **MonoGame / XNA** | 覆寫 `Update(GameTime)` + `Draw(GameTime)` | 框架骨架 |
| **原生 C#（模擬器）** | 自己寫 `while` 迴圈，並對時脈週期嚴格計時 | 自己 |

模擬器的 Loop 比一般遊戲更嚴謹：每秒執行的指令數必須對得上原始硬體（例如 NES 的 ~1.79 MHz CPU + PPU），否則畫面與音訊都會變調。

### 一個關鍵概念：Delta Time

每台電腦效能不同，強機一秒跑 200 圈、弱機可能只有 30 圈。為了讓「視覺上移動速度一致」，位移要乘上兩幀之間的時間差：

```text
NewPosition = CurrentPosition + Speed × DeltaTime
```

模擬器通常不用 Delta Time，而改用「固定時脈的 cycle counter」——這是為了保證精確性而非視覺平滑。

---

## 2. Inline 與 I-Cache 的拉鋸戰

### Q：從 C# JIT 的角度來看，若把大量方法強制 Inline 進 Game Loop，是否可能撐爆 L1 I-Cache 反而造成 Cache Miss？但頻繁呼叫外部方法本身也有成本——這種「冷熱路徑分離」該如何拿捏？不同處理器（例如擁有更大 I-Cache 的 X3D 系列）是否會改變這個平衡點？

這是高效能開發中經典的「空間換時間」與「記憶體階層博弈」。它**不是單純的技術題**，而是一門平衡取捨的藝術。

### 2.1 兩端的成本

| 選擇 | 主要成本 |
| --- | --- |
| **呼叫 Method（不 Inline）** | 分支預測命中的前提下，一次 call 約幾個 cycle。真正的代價是**阻礙編譯器進行暫存器分配與流水線重排** |
| **強制 Inline 一切** | 熱路徑膨脹，一旦超過 L1 I-Cache（每核心約 32–64 KB）就得向 L2 / L3 取指令，stall 懲罰可達 call 成本的**數十倍** |

結論是：如果 Game Loop 的核心邏輯本身就很大（物理 + AI + 渲染提交），強制全部 Inline 絕對是反效果。

### 2.2 冷熱路徑分離（Hot / Cold Path Splitting）

核心思想：**讓最常執行的指令在記憶體中盡可能緊湊**。

- **熱路徑（Hot Path）**：每一幀都跑的邏輯（座標更新、輸入分派）。保持精簡、允許 JIT 積極 Inline。
- **冷路徑（Cold Path）**：很少觸發但邏輯肥大的分支（錯誤處理、初始化、特殊事件）。手動標註 `NoInlining`，強制 JIT 把這段機器碼「搬到很遠的地方」。

```csharp
void GameLoop()
{
    // Hot：保持小，讓 JIT 自由 Inline
    UpdatePhysics();

    if (unlikelyEvent)
        HandleComplexEvent();  // 冷路徑
}

[MethodImpl(MethodImplOptions.NoInlining)]
void HandleComplexEvent()
{
    // 幾百行平常不會執行的程式碼
}
```

### 2.3 拿捏的三個原則

| 原則 | 說明 |
| --- | --- |
| **小方法原則** | 方法盡量寫小（< 16 bytes IL），JIT 會傾向自動 Inline；且機器碼容易擠進 I-Cache |
| **`AggressiveInlining` 要謹慎用** | 只用在「微小且極度頻繁」的方法（向量加法、屬性 getter、位元操作）——過度使用反而撐爆 I-Cache |
| **優先處理 D-Cache 再談 I-Cache** | 多數 C# 程式的瓶頸是 GC / 引用型別造成的 **D-Cache Miss**，而不是 I-Cache。先讓資料連續（`struct` 陣列、`Span<T>`），效能提升往往比調 Inline 更明顯 |

### 2.4 硬體條件的影響

AMD 3D V-Cache（Ryzen 7800X3D 之類）把 L3 堆到 96 MB 以上，對「代碼膨脹」的容忍度顯著提升。即使 L1 miss，從 L3 取指令速度仍遠快於 RAM。這代表：

- **桌機／高階 X3D 硬體**：可較激進地 Inline 與 Loop Unrolling。
- **行動裝置、低階 CPU**：I-Cache 珍貴，過度 Inline 幾乎必然是效能殺手。

實務建議是針對「最小公分母」優化，而非針對你自己的機器。

---

## 3. 如何找到最優解：量化工具與階梯式策略

### Q：有沒有系統化的方法可以幫助我們找到 Inline 策略的最優解？有哪些軟體或工具提供這方面的量化分析？

靠直覺看程式碼幾乎不可能找到最優點。真正可行的是**「科學實驗法」**：改動、量測、對比、再改動。

### 3.1 微基準測試：`BenchmarkDotNet`

.NET 效能優化的工業標準，能精確到奈秒並支援硬體計數器。

```csharp
[HardwareCounters(
    HardwareCounter.InstructionCacheMisses,
    HardwareCounter.BranchMispredictions)]
public class GameLoopBenchmark
{
    [Benchmark] public void InlineVersion()  { /* ... */ }
    [Benchmark] public void CallVersion()    { /* ... */ }
}
```

優勢：
- 自動處理 JIT 暖機（先跑一輪 Tier-0 收 PGO，再量測 Tier-1）。
- 可同時吐 JIT 彙編碼，驗證 `[MethodImpl]` 有沒有真的生效。
- 直接讀取 CPU PMU（效能監控單元），報告 I-Cache Miss 次數。

### 3.2 系統層級 Profiler

| 工具 | 特性 | 適用場景 |
| --- | --- | --- |
| **Intel VTune Profiler** | Top-Down Microarchitecture Analysis，能標記 Front-End Bound（通常就是 I-Cache 瓶頸），支援 C# 與 JIT 機器碼對應 | Intel CPU 上最強的微架構分析 |
| **AMD uProf** | L1 / L2 / L3 命中率逐行分析；對 X3D 大快取特別有用 | AMD CPU |
| **PerfView（Microsoft）** | 醜、陡峭，但是 .NET Runtime 事件（JIT / GC / ETW）最權威的工具；免費 | 診斷 JIT 編譯事件、Inlining 決策、GC 行為 |

### 3.3 階梯式優化策略

不要一上來就猛調 Inline。應該照這個順序檢查：

```text
1. 資料導向設計（D-Cache 優先）
   ├─ struct 陣列而非 class List
   ├─ 避免 boxing
   └─ 減少 GC 壓力

2. 讓小方法維持小──讓 JIT 自動 Inline
   └─ < 16 bytes IL 是甜點位

3. 手動標註冷路徑
   ├─ throw / WriteLine / 錯誤處理 → [MethodImpl(NoInlining)]
   └─ 讓 Hot Path 在機器碼層級盡量緊湊

4. 針對性實驗
   └─ 只有當 Profiler 指出 I-Cache Miss 偏高時，才動手拆分 Inline
```

> Donald Knuth：**「過早優化是萬惡之源。」**
> 先保留可讀性；只有 Profiler 指向熱點時才手動干預。

---

## 4. 熱路徑過剩：當核心邏輯本身就塞不下 L1

### Q：如果熱路徑本身就極度龐大，整段放進同一個迴圈必然超出 L1 I-Cache 容量——這種「熱路徑過剩」的情境，是否屬於優化中最棘手的問題？

是的，這是高效能開發中最令人頭痛的**天花板問題**。當核心邏輯本身就超出 L1 容量，靠單純的程式碼調整已經救不了——你要處理的是**架構層級的重構**。

以下是幾種進階對抗策略。

### 4.1 拆分階段：把一個大 Loop 拆成多個 Pass

若原本一個 Loop 同時做 A、B、C 三件都很重的事，加起來塞不下 I-Cache：

- 不要讓 CPU 在一次迭代內頻繁發生 I-Cache Miss。
- 改為跑一次 Loop 只做 A，結果存到中間陣列；再跑一次 Loop 只做 B。

代價：D-Cache 的讀寫流量增加。收益：**I-Cache 命中率逼近 100%**。當 I-Cache miss 造成 pipeline 卡死時，這招往往能換回整數倍效能。

### 4.2 指令對齊與 Profile-Guided Optimization（PGO）

C# 對機器碼位置的控制力不如 C++，但透過 JIT 特性可以間接影響佈局：

- **動態 PGO（.NET 6/7/8+）**：JIT 會先以 Tier-0 執行並收集分支資訊，再在執行期重新編譯熱點程式碼，把最常跑的 basic block 在記憶體中排列得連續。
- **啟用方式**：`DOTNET_TieredPGO=1`（.NET 6+ 預設已開）。
- 對模擬器這種持續跑相同熱點的程式特別有效。

### 4.3 SIMD：用更少指令完成同樣的事

如果 Loop 的本質是向量運算，導入 SIMD（`Vector128<T>` / `Vector256<T>` / `Vector512<T>`）通常能把原本 100 條指令的工作壓縮到 10 條：

- 指令變少了，**I-Cache 壓力自然消失**。
- 對 NES 的 PPU 背景合成、音訊混音、NTSC 解調這類像素／樣本級運算，SIMD 是最有效的減肥藥。
- 本專案 AprNes 大量使用 SWAR（SIMD Within A Register）+ `Vector256<uint>` 處理 scanline，單輪 commit 就能拿到 +10% 以上 FPS。

### 4.4 執行緒親和性（Thread Affinity）

把熱路徑鎖死在特定 CPU 核心：

- 避免作業系統把執行緒移來移去（Context Switch）。
- 確保該核心的 L1 I-Cache 內容不會被其他程式「汙染」。

### 4.5 判斷指標：CPI（Cycles Per Instruction）

| 現象 | 原因 | 解決方向 |
| --- | --- | --- |
| CPI 高，CPU 使用率滿載 | I-Cache / D-Cache Miss，CPU 在等資料或指令 | 縮減代碼體積、拆分 Loop、預取資料 |
| CPI 低，但仍然跑不夠快 | 指令執行已最佳化，是純計算量太大 | 改演算法、SIMD、或多核並行 |

### 4.6 總結：解構 + 流水線

當熱路徑塞不下時，最專業的做法是：

> **不要試圖在一個時間點做完所有事。**

把複雜邏輯**解構**成多個能各自塞進 L1 的微小模組，像工廠流水線串起來。這會增加記憶體頻寬，但現代 CPU 的 D-Cache 預取（Prefetcher）遠比 I-Cache 預取強壯，這筆交易通常划算。

---

## 5. 多核流水線：用多份 I-Cache 協作

### Q：若熱路徑過大塞不下單核 L1，是否可以把邏輯切割、分散到不同核心上，讓每顆核心各自擁有的 L1 I-Cache 共同分擔整體的指令容量？

這是非常正確的直覺，高效能領域稱為 **「核心級指令流水線（Instruction Pipelining at Core Level）」**。

### 5.1 類比：工廠車間

把 Game Loop 想成工廠：

- **傳統做法**：在一個小車間裡不斷搬進搬出工具（I-Cache 反覆刷新）。
- **多核流水線**：開三個車間，各放一套工具，資料（零件）在車間之間流動。

每個核心只處理一段邏輯 → 該段邏輯完全駐留自己的 L1 I-Cache → **指令取用零延遲**。

### 5.2 收益與風險

**收益：**
1. 每顆核心的熱路徑都能完美塞進其私有 L1 I-Cache。
2. 整體 Throughput（每秒處理量）大幅提升，雖然單筆 Latency 可能因跨核傳輸略增。

**風險：**
1. 跨核資料傳輸要走 L3 或 Infinity Fabric／Ring Bus。
2. 快取一致性協定（MESI）帶來同步成本。
3. 若同步頻率過高，節省的 I-Cache miss < 增加的通訊延遲 → **負優化**。

### 5.3 什麼情況才划算？

看「計算密度（Compute Density）」：

| 計算密度 | 範例 | 建議 |
| --- | --- | --- |
| **低**（邏輯大、單筆計算簡單） | 單純的 memcpy、簡單加總 | 不拆，單核執行更好 |
| **高**（邏輯大、計算複雜） | NES PPU 渲染（調色盤 + 精靈碰撞 + 背景合成）、AAA 物理引擎 | 適合拆分到專用核心 |

對 AprNes 這類模擬器：將 PPU 拆到獨立核心、以生產者／消費者模型與 CPU 核心非同步協作，是業界常見的進階招式。

### 5.4 建議的實作骨架

1. **Thread Affinity**：把每個熱路徑執行緒固定在特定核心。
2. **SPSC（Single-Producer Single-Consumer）Ring Buffer**：無鎖的環形緩衝區，避免使用 `lock`。
3. **結構化資料**：傳遞的 struct 盡量塞進一個 Cache Line（64 bytes）。
4. **避開 Hyper-Thread 共享**：盡量選「不同實體核」的邏輯核（通常是偶數編號）。

---

## 6. I-Cache 究竟是哪一層快取？

### Q：我們在討論效能時提到的「I-Cache」，具體是指 CPU 的哪一層快取？與 L2 / L3 的差異為何？

**是 L1 指令快取（L1 Instruction Cache）。**

### 6.1 哈佛架構 vs 馮諾依曼

L1 採用「哈佛架構」——指令與資料**完全分開**：

- **L1 I-Cache**：只存 CPU 即將執行的機器碼
- **L1 D-Cache**：只存程式運算用的變數 / 物件 / 陣列

L2 以後就混合了，不再區分 I / D。

### 6.2 快取階層數據

| 層級 | 延遲 | 典型容量 | 角色 |
| --- | --- | --- | --- |
| **L1 I-Cache** | **~1–4 cycles** | **32–64 KB** | 決定 Game Loop 能否無間斷執行 |
| **L1 D-Cache** | ~1–4 cycles | 32–64 KB | 熱資料的家 |
| **L2 Cache** | ~10–15 cycles | 256 KB – 1 MB | 指令與資料混合 |
| **L3 Cache** | ~40–60 cycles | 2 MB – 96 MB+ | 核心間共享 |
| **RAM** | **~100–300+ cycles** | GB 等級 | 龜速：一旦掉到這裡，效能瞬間崩潰 |

### 6.3 為什麼「大」邏輯會殺死效能？

當熱路徑機器碼超過 32 KB：

1. **填滿**：Loop 跑到一半就塞爆 L1 I-Cache。
2. **驅逐（Eviction）**：CPU 把前半段指令踢出去，改從 L2 / L3 抓後半段。
3. **抖動（Thrashing）**：下一影格開始，又把後半段踢掉去抓前半段，來回刷新。

這就像大腦一次只能記 10 個動作，但工作清單有 50 個——每做一步都要回頭翻手冊，速度自然快不起來。

### 6.4 多核拆分的本質

L1 I-Cache 是**每個核心私有**的。所以：

- 單核執行 → 只有 1 份 32 KB 容量。
- 拆到 4 個核心 → 等於有 4 份 32 KB（共 128 KB）來駐留指令。

這正是「多核流水線」能緩解 I-Cache 壓力的根本原因。

---

## 7. 核心間通訊的代價

### Q：即使把熱路徑分到多核跑，看來也必須正視「核心間通訊」的代價——這部分的具體開銷有哪些？

沒錯，這是整場博弈中**最核心的難點**。如果省下了「翻書時間」（I-Cache）卻把零件在高雄、台北之間來回運送（核心通訊），總成本不見得更低。

### 7.1 具體延遲數字

| 情境 | 延遲 | 換算 cycle（以 4 GHz 計） |
| --- | --- | --- |
| **同 CCX / 相鄰核心**（走共享 L3 或 Ring Bus） | **30–50 ns** | ~100–200 cycles |
| **跨 CCX / 跨 Die**（Ryzen 非對稱核心、多 socket） | **100 ns+** | 400+ cycles |

100 cycles 已經足夠 CPU 執行數百條指令。如果你拆出來的邏輯只為了省 10 條指令的 I-Cache miss，**絕對是虧本生意**。

### 7.2 MESI 協定的隱形成本

多核通訊不是單純「傳資料」，而是「狀態同步」：

1. 核心 A 修改某個 Cache Line → 標記為 Modified。
2. 核心 B 偵測到該行失效 → 發出請求。
3. 硬體確保兩核看到的值一致。

整個過程透過 CPU 互連（Infinity Fabric / Ring Bus）傳遞，會產生 **Bus Traffic**。如果 Loop 每秒同步數百萬次，這條匯流排會瞬間爆掉。

### 7.3 解決方向：批次、無鎖、避免偽共享

#### 7.3.1 增加運算顆粒度（Granularity）

**不要**每算一個像素就傳給下一核。**要**讓核心 A 一次算完一整條 Scanline 甚至一整幀再交棒。

#### 7.3.2 無鎖環形緩衝區

`System.Threading.Channels` 是不錯的起點，極致追求效能時可以自己寫 struct-based SPSC Ring Buffer。避開 `lock` / `Monitor`。

#### 7.3.3 避免偽共享（False Sharing）

兩個核心改不同變數，但兩個變數剛好在同一個 64-byte Cache Line 內 → CPU 會以為整行被污染，不斷 invalidate。

**解法**：用 `StructLayout` + `FieldOffset` 把變數強制隔到不同 Cache Line：

```csharp
[StructLayout(LayoutKind.Explicit, Size = 128)]
public struct PaddedCounters
{
    [FieldOffset(0)]  public long CpuCounter;  // Core 1 讀寫
    [FieldOffset(64)] public long PpuCounter;  // Core 2 讀寫
}
```

### 7.4 決策公式

```text
淨收益 = 減少的 I-Cache Miss 成本 − 增加的核心通訊延遲
```

| 情況 | 建議手段 |
| --- | --- |
| **邏輯小、同步頻繁** | 單核執行 + `NoInlining` 推開冷路徑 |
| **邏輯大、計算密度高** | 多核流水線 + 緩衝區非同步（例如 PPU 領先 CPU 半影格） |

---

## 8. 在 C# 裡確保 Thread 真的分到不同核心

### Q：在 C# 中單純開兩個 Thread，並不能保證它們真正被分派到不同的實體核心上。有哪些建議的處理方式能確保執行緒實際利用到多核資源？

作業系統排程器（Scheduler）為了節能、平衡發熱、或優先處理背景任務，隨時可能把你的 Thread 塞到同一核，或在不同核之間來回跳（Context Switch）——**辛苦建立的 I-Cache 瞬間全數作廢**。

以下是「真正多核」的實作 SOP。

### 8.1 設定 Thread Affinity

這是最重要的一招。告訴 OS：「這個 Thread 只能在第 N 號核心跑」。

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;

[DllImport("kernel32.dll")]
static extern int GetCurrentThreadId();

public static void PinToCore(int coreIndex)
{
    int tid = GetCurrentThreadId();
    foreach (ProcessThread pt in Process.GetCurrentProcess().Threads)
    {
        if (pt.Id == tid)
        {
            // Bitmask：1<<0 = Core 0, 1<<1 = Core 1, ...
            pt.ProcessorAffinity = (IntPtr)(1 << coreIndex);
            break;
        }
    }
}
```

**小提醒**：Windows 上盡量避開 Core 0，很多系統中斷（interrupt handler）駐在那裡。

### 8.2 用 `new Thread()`、不要用 `Task.Run` / ThreadPool

```csharp
var ppuThread = new Thread(PpuLoop)
{
    IsBackground = true,
    Priority = ThreadPriority.Highest
};
ppuThread.Start();
```

原因：
- **ThreadPool** 是為「短暫、大量」工作設計的，會自動調整規模、會回收 Thread → 你的 affinity 設定會失效、I-Cache 會被換掉。
- **Task / async-await** 底層也走 ThreadPool，不適合長期住在固定核心的場景。

模擬器這類要永久佔用 N 顆核心的程式，應該明確 `new Thread()` 並保證生命週期。

### 8.3 解決偽共享

如果兩個核心共用一個物件，務必把跨核讀寫的欄位 Padding 到不同 Cache Line。細節見 §7.3.3。

### 8.4 小心超執行緒（Hyper-Threading / SMT）

`Environment.ProcessorCount` 回傳的是**邏輯核**數量，不等於實體核：

- 在 8 核 16 緒的 CPU 上，邏輯 Core 0 和 Core 1 其實是**共享同一個實體核的 L1 I-Cache**。
- 極致 I-Cache 利用要挑偶數編號邏輯核（0, 2, 4, 6…），以對應不同實體核。

### 8.5 實用監控：Process Lasso

**Process Lasso** 可以即時觀察每個 Thread 實際落在哪顆核，並強制鎖定。開發期拿來驗證 `SetThreadAffinityMask` 有沒有生效非常方便。

### 8.6 建議 SOP 總結

1. 手動 `new Thread()`，設 `Highest` 優先級。
2. 進入 Thread 後立刻 `PinToCore(n)`。
3. 跨核通訊走無鎖 Ring Buffer。
4. 共享資料做 Padding，避免偽共享。
5. 用 Process Lasso / VTune 驗證實際落點。

---

## 9. 這些觀念在其他語言通用嗎？

### Q：這些優化思維即便脫離了 C# 與 JIT 的脈絡，是否在其他語言（如 C++ / Rust）同樣適用？

**幾乎完全通用。** 因為這些技巧優化的對象不是「語言」，而是**底層 CPU 架構的三座大山**：

1. **Pipeline（流水線）**
2. **Cache Hierarchy（快取階層）**
3. **Branch Prediction（分支預測）**

無論你寫哪種語言，最終 CPU 都要面對這三者。

### 9.1 不同語言的起點差異

| 語言 | 控制細膩度 | 手段 |
| --- | --- | --- |
| **C / C++ / Rust / Zig** | **最高** | 直接控制 struct 對齊（`alignas` / `#[repr(C, align(64))]`）、內嵌組語、PGO／LTO、manual prefetch intrinsic |
| **C# / Java / Kotlin（Managed）** | **中等** | 受限於 JIT 和 GC，但可以用 `StructLayout` / `ValueType` / `Span<T>` 引導 |
| **Python / JavaScript** | **低** | 大多跑在 VM / 解釋器上；真正優化的是「解釋器的」快取而非「你的」代碼 |

### 9.2 通用問題案例

- **Code Bloat**：C++ 過度 `inline` / Template 大量展開 → 一樣撐爆 L1 I-Cache。
- **False Sharing**：Java、Go、C++ 都有同樣問題，解法一樣是 Padding / 對齊。
- **PGO**：C++（Clang `-fprofile-use`、MSVC PGO）、Rust（`cargo-pgo`）、.NET 動態 PGO 全都一樣的哲學——先跑一次收集資料，再針對熱點重排。

### 9.3 實務場景

這類「多核流水線 + 每核心一段小邏輯」的設計，在業界廣泛使用：

- **AAA 遊戲引擎**（Unreal 的 Job System）。
- **高頻交易 HFT**：C++ 鎖死核心、甚至繞過 OS 內核（kernel bypass）、用 FPGA 硬化熱路徑。
- **網路交換設備 DPDK**：多核流水線處理千萬級封包 / 秒。

### 9.4 硬體進步 vs 軟體優化

- 大快取（X3D 系列）**會遮蓋**爛代碼的問題——但也僅止於遮蓋。
- 針對 L1 優化過的代碼在大快取 CPU 上只會更猛，並能騰出 CPU 預算做更多額外工作。

**你在 AprNes 上學到的「拿捏術」，跳脫了 C# 框架，進入了「計算架構師」思維。**

---

## 10. 延伸到網路服務的高併發場景

### Q：這類偏向硬體導向的效能優化技巧，對於網路服務在高併發、高流量情境下的承載能力，是否同樣有幫助？

**是的，而且是「一般工程師」與「系統架構師」的分水嶺之一。**

當 QPS 達到每秒數十萬甚至數百萬時，任何微小的指令效率低下都會被放大成災難。

### 10.1 I-Cache 與「指令緊湊性」

- 忌諱「萬能函式」：處理每個 request 都跑幾千行的 routing 判斷 → I-Cache 不斷被刷。
- 高性能 Web Server（Nginx 模組、Kestrel、Envoy）都極力縮減 hot path middleware 的機器碼大小，確保主迴圈可以完全待在 L1 I-Cache。

### 10.2 多核流水線 vs 網路卡 RSS

- 現代網卡支援 **RSS（Receive Side Scaling）**，把封包自動分派到不同核心的 queue。
- 軟體層對應：Core A 處理 TCP、Core B 處理商務邏輯、Core C 處理 DB I/O，與模擬器裡的 CPU/PPU 拆分完全同構。
- 通訊成本在此場景體現為 **Context Switch**，解法一樣是 Thread Affinity。

### 10.3 偽共享是高併發的隱形殺手

典型案例：全域計數器 `RequestCount`，多個 Thread 用 `Interlocked.Increment` 爭搶同一個 Cache Line。執行緒安全但效能隨核心數增加反而**下降**。

**解法**：每核獨立計數器，最後才加總；或者把欄位 Padding 開。

### 10.4 記憶體佈局與 GC 壓力（D-Cache）

網路服務中 D-Cache Miss 通常比 I-Cache 更致命：

- `class` 是引用型別，分散在 GC Heap 上，遍歷 User 列表就是一堆隨機 RAM access。
- 改用 `struct` 陣列 / `MemoryPool<T>` / `ArrayPool<T>` → 讓資料連續 → CPU Prefetcher 可以在指令到達前就把下一筆載入 L1。

### 10.5 思維轉變對照表

| 面向 | 一般網路服務 | 高頻高流量優化 |
| --- | --- | --- |
| 可擴展性 | Scale Out（加機器） | Scale Up（壓榨單機） |
| 程式碼結構 | 越抽象、層次越多越好 | 熱路徑越平坦、越緊湊越好 |
| 效能指標 | 平均延遲 | **P99 / P99.9 尾部延遲** |

**尾部延遲（tail latency）**正是 I-Cache Miss、GC pause、Context Switch 造成的。模擬器開發者天然就在訓練這塊。

---

## 11. 實戰附錄：AprNes 的 JIT / I-Cache 分析流程

本節是**實戰補充**——把本專案（AprNes NES 模擬器）長期使用的 JIT + I-Cache 量化流程整理出來，讓抽象概念落地到可重現的指令。

### 11.1 工具總覽

| 工具 | 用途 | 路徑 |
| --- | --- | --- |
| **PerfView.exe** | ETW + PMU trace 收集 | `temp/PerfView.exe` |
| **bench_profile.bat** | 啟動 AprNes 跑 benchmark ROM | `temp/bench_profile.bat` |
| **run_perfview.bat** | CPU sampling + JIT events 收集 | `temp/run_perfview.bat` |
| **run_perfview_pmu.bat** | PMU 硬體計數器收集（I-Cache miss 等） | `temp/run_perfview_pmu.bat` |
| **EtlAnalyzer**（.NET 10） | 解析 ETL → CPU hotspot + JIT / Inlining 報告 | `temp/EtlAnalyzer/` |
| **PmuAnalyzer**（.NET 10） | 解析 PMU 事件 → 每方法 I-Cache miss 率 | `temp/PmuAnalyzer/` |
| 報告輸出 | 帶時間戳的 md | `MD/jit/` |

### 11.2 一般 JIT / CPU 熱點分析（日常用）

```text
Step 1: 編譯目標版本
  powershell -NoProfile -Command "& 'C:\Program Files\Microsoft Visual Studio\
    2022\Community\MSBuild\Current\Bin\MSBuild.exe' AprNes.csproj /p:Configuration=Debug ..."

Step 2: 啟動 trace 收集
  cmd //C "temp\run_perfview.bat"
  → 產出 temp/aprnes_jit.etl（約 18 MB，含 CPU sampling + JIT/Inlining events）

Step 3: 解析
  dotnet run --project temp/EtlAnalyzer -c Release
  → 產出 temp/profile_report.txt

Step 4: 整理入庫
  cp temp/profile_report.txt MD/jit/<YYYYMMDD_HHMMSS>_<topic>.md
```

EtlAnalyzer 報告內容：

1. **CPU Sampling — Exclusive**：各方法自身 CPU 時間佔比
2. **CPU Sampling — Inclusive**：各方法含 callees 的 CPU 時間佔比
3. **NesCore-only Exclusive**：僅模擬器核心方法，含 NesCore 總佔比
4. **JIT Compilation**：所有被 JIT 的方法 + IL size
5. **Inlining**：成功 / 失敗 inline 的方法及原因
6. **Hot Path Inline Status**：交叉分析——熱點方法有沒有被 inline

### 11.3 PMU 硬體計數器：真正看到 I-Cache Miss

PerfView 透過 `/CpuCounters` 支援 PMU 硬體計數器。以 AMD Ryzen 7 3700X（Zen 2）為例，可用的計數器包括：

| ID | 計數器 | 意義 |
| --- | --- | --- |
| 0 | `Timer` | 傳統時脈取樣 |
| 9 | `IcacheMisses` | **L1 I-Cache miss 次數** |
| 19 | `TotalCycles` | 總 cycle 數 |
| 20 | `IcacheIssues` | **L1 I-Cache fetch 次數**（分母） |

PMU 硬體只有 4–6 個可程式化 slot（Zen 2 是 4 個），所以一次最多開 4 個計數器。

```text
cmd //C "temp\run_perfview_pmu.bat"    # 收集約 30 秒、~3M samples
dotnet run --project temp/PmuAnalyzer -c Release
→ 產出 temp/pmu_report.txt
```

PmuAnalyzer 會讀 ETL 裡的 `PMCSample` 事件，依 JIT'd method name 歸類，輸出每方法的 miss 率（miss / fetch）。

### 11.4 解讀指標：健康門檻

| 全域 I-Cache Miss 率 | 狀態 | 含意 |
| --- | --- | --- |
| **< 1%** | excellent | 工作集舒適落在 L1 |
| **1–3%** | healthy | 有輕微驅逐，L2 吸收成本 |
| **3–10%** | concerning | L2 流量顯著 |
| **> 10%** | bad | 可觀測 stall 造成 FPS 下降 |

### 11.5 AprNes 的實測數據（2026-04-14，master @ 47f7876）

- 全域 L1 I-Cache miss rate：**0.52%**（3,143 misses / 603,569 fetches）
- 熱點方法 miss rate：

| 方法 | Miss % |
| --- | --- |
| `ppu_step_new` | 0.31% |
| `Run_NTSC` | 0.36% |
| `PpuPhase4_SpriteEvalAndInit` | 0.36% |
| `apu_step` | 0.47% |
| `Crt_Render`（CRT pipeline lambda） | 0.93% |
| `Curvature+Convergence`（lambda） | 1.28% |
| `DemodulateRow_Core` | **1.43%**（pipeline 中 IL 體積最大者） |

結論：模擬核心穩穩在 excellent 區，CRT pipeline 雖然較高但仍健康。這證明 AprNes 核心（~20 KB 機器碼）能舒適塞進 Zen 2 的 32 KB L1 I-Cache。

### 11.6 為什麼靜態估算常常高估？

早期靜態 IL × 4 的估算曾推出「熱路徑約 47 KB」的結論，看似已經撐爆 L1；實測卻只有 0.52% miss。原因：

1. **執行窗口窄**：任一 12-MC 窗口內，真正 active 的方法只有 2–3 個（`Run_NTSC` + `ppu_step_new` 或 `apu_step`），同時命中 L1 的機器碼遠小於總和。
2. **重度的 branch locality**：分支高度可預測，基本上跑熱路徑同一條 basic block 路徑，即使方法總量大，實際 fetch 的指令段很小。
3. **預取器（Prefetcher）**：現代 CPU 對順序執行的機器碼有強大 prefetch，等同「隱藏」了一部分 miss 成本。

這也呼應本文第 3 節的階梯原則——**實測永遠優於估算**。

### 11.7 分析流程總結

```text
┌─────────────────────┐
│ 1. 想改 hot path    │
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 2. 收 baseline trace│  ← run_perfview.bat + run_perfview_pmu.bat
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 3. 解析報告         │  ← EtlAnalyzer + PmuAnalyzer
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 4. 改 code          │
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 5. 重新收 trace     │
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 6. Diff 兩次報告    │
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ 7. 入庫 MD/jit/     │
└─────────────────────┘
```

每輪都要完整走完，才知道某次改動是「真的優化」還是「湊巧看起來快」。

### 11.8 搭配 Benchmark 協議（3 次法）

量 FPS 時使用本專案一貫的 **3 次法**：

1. **第 1 次**：JIT 暖機，**不採計**（.NET TieredPGO 以 Tier-0 執行並收集 PGO）
2. sleep 60（讓 CPU 降溫、避免熱節流）
3. **第 2 次**：採計（此時已是 Tier-1 最佳化機器碼）
4. sleep 60
5. **第 3 次**：採計
6. 取第 2、3 次平均

PMU 分析也建議在「暖機後」再採集，避免把 Tier-0 編譯的 overhead 當成穩態行為。

---

## 附錄：常用指令速查

```csharp
// 強制不 Inline（冷路徑）
[MethodImpl(MethodImplOptions.NoInlining)]
void ColdHandler() { /* ... */ }

// 積極 Inline（微小熱方法）
[MethodImpl(MethodImplOptions.AggressiveInlining)]
static int FastAdd(int a, int b) => a + b;

// 避免偽共享
[StructLayout(LayoutKind.Explicit, Size = 128)]
public struct PerCoreState
{
    [FieldOffset(0)]  public long CpuCounter;
    [FieldOffset(64)] public long PpuCounter;
}

// 綁核
[DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
foreach (ProcessThread pt in Process.GetCurrentProcess().Threads)
    if (pt.Id == GetCurrentThreadId())
        pt.ProcessorAffinity = (IntPtr)(1 << coreIndex);

// 環境變數：開啟動態 PGO
//   set DOTNET_TieredPGO=1
//   set DOTNET_TC_QuickJitForLoops=1
```

```bat
REM PerfView 一般 trace
temp\run_perfview.bat

REM PerfView PMU trace（需要以系統管理員執行）
temp\run_perfview_pmu.bat

REM 解析
dotnet run --project temp\EtlAnalyzer -c Release
dotnet run --project temp\PmuAnalyzer -c Release
```

---

## 結語

從 Game Loop 的基礎出發，我們一路走過 Inline 策略、冷熱路徑拆分、I-Cache 拓撲、多核流水線、執行緒親和性、False Sharing、一路延伸到網路服務的尾部延遲。

這些觀念的核心其實只有一句話：

> **讓最常跑的指令與最常用的資料，盡可能近、盡可能連續、盡可能不被打斷。**

什麼時候 Inline、什麼時候拆核、什麼時候硬體同步划算，全都是這句話在不同場景的具體展開。

只要把這套思維內化，無論手上是 C# 模擬器、Rust 區塊鏈節點、C++ 自動駕駛控制器，或 Go 寫的 API gateway，都能一眼看出效能瓶頸在哪。而 AprNes 的 PMU / EtlAnalyzer 流程，就是把這套思維變成可重複量測的工程實務的具體落地。
