# NES 模擬器 Timing Model 與 Catch-up 架構教學

> 本文以 AprNes 專案的開發經驗為基礎，介紹 NES 模擬器中不同的時序模型設計，以及 Catch-up（事件驅動）架構的概念。適合具有電資理工背景、對模擬器或嵌入式系統有興趣的讀者。

---

## 1. 為什麼 NES 模擬需要「Timing Model」？

NES（Nintendo Entertainment System）內部有三個主要硬體元件同時運作：

- **CPU** (MOS 6502) — 執行遊戲程式指令
- **PPU** (Picture Processing Unit) — 逐像素繪製畫面
- **APU** (Audio Processing Unit) — 產生音效

在真實硬體上，這三者是**獨立的電路**，透過物理電信號同步，彼此**同時**運作。但在軟體模擬器中，我們只有一個 CPU 核心（模擬器執行在你的電腦上），必須**依序**模擬這些本應並行的元件。

**Timing Model（時序模型）**就是決定「用什麼順序、以什麼粒度來交替執行 CPU/PPU/APU」的架構設計。這個選擇直接影響：

- **精確度** — 遊戲畫面是否正確、音效是否同步
- **效能** — 模擬器能跑多快
- **開發複雜度** — 程式碼有多難寫和維護

---

## 2. 三種主流 Timing Model

### 2.1 指令級（Instruction-Level）— 最簡單

```
每一步：
  CPU 執行一條完整指令（2-7 個 cycle）
  PPU 一次推進對應的所有 dot（6-21 個 dot）
  APU 推進對應的 cycle 數
```

**優點**：實作最簡單，效能最好。每秒只需要約 3 萬次主迴圈迭代。

**缺點**：精確度差。許多遊戲在一條指令的「中間」就會觀測到 PPU/APU 的狀態變化。例如：

- Super Mario Bros. 的卷軸分割需要 PPU 在精確的 dot 位置回報 Sprite 0 Hit
- 如果 PPU 一次推進 21 個 dot，可能錯過精確的觸發時機

**適合**：早期簡單遊戲（Donkey Kong、Ice Climber）。約 60% 的 NES 遊戲在此精度下可正常運作。

### 2.2 Cycle-Level（Cycle-Accurate）— 主流精確方案

```
每一步：
  CPU 執行一個 cycle
  PPU 推進 3 個 dot（NTSC 的 CPU:PPU = 1:3）
  APU 推進 1 個 cycle
```

**優點**：精確度高，絕大多數遊戲正確。CPU 的每一個 cycle 都能看到 PPU/APU 的最新狀態。

**缺點**：效能成本高。每秒約 178 萬次主迴圈迭代（NTSC 約 29,781 CPU cycles/frame × 60 fps）。

**適合**：大多數 NES 模擬器的目標精度。Mesen、Nintendulator 等知名模擬器採用此模型。

### 2.3 Master Clock Level（Sub-Cycle）— 最高精度

```
每一步：
  推進 1 個 Master Clock tick
  依據計數器狀態決定本 tick 要執行 CPU、PPU 還是 APU
```

NTSC 的 Master Clock 頻率為 21.477 MHz，CPU 為其 1/12，PPU 為其 1/4。這意味著：

- 1 個 CPU cycle = 12 個 Master Clock ticks
- 1 個 PPU dot = 4 個 Master Clock ticks
- 在一個 CPU cycle 內，PPU 會在不同的 sub-cycle 時間點執行全步和半步

```
Master Clock ticks within 1 CPU cycle (NTSC):
Tick  0: CPU 指令執行 / DMA 處理
Tick  2: PPU 半步（shift register 移位、VBL latch）
Tick  4: PPU 全步（完整 dot 處理）
Tick  5: IRQ 捕獲
Tick  6: PPU 半步
Tick  8: NMI 偵測 + PPU 全步
Tick 10: PPU 半步
Tick 12: APU 步進
```

**優點**：能模擬硬體在 sub-cycle 層級的行為，如 PPU shift register 在 dot 中間的電氣轉換。這是目前已知最高精度的模型。

**缺點**：效能成本極高。每秒約 3,570 萬次 Master Clock tick（每幀 357,368 tick × 100 fps）。

**適合**：追求通過所有已知測試 ROM 的極致精確模擬器。TriCNES 採用此模型並達成 AccuracyCoin 136/136 滿分。

---

## 3. AprNes 為什麼選擇 Master Clock Model

### 3.1 問題的發現

AprNes 最初採用 Cycle-Level 模型。在通過 blargg 174 項測試和 AccuracyCoin 136 項測試後，我們在實際遊玩中發現部分畫面仍有細微瑕疵（如 `scanline-a1` 和 `colorwin_ntsc.nes` 測試畫面不完全正確）。

追查後確認根因是 **PPU 在 sub-dot 層級的行為精度不足**。Cycle-Level 模型將 PPU 的 3 個 dot 視為「一次性完成」，但真實硬體中，每個 dot 內部還有更細的時序行為。

### 3.2 TriCNES Timing Model 的移植

我們決定移植 TriCNES 的 Master Clock 模型。TriCNES 由 AccuracyCoin 測試 ROM 的作者撰寫，其 timing 架構經過硬體驗證。

移植的核心是 `MasterClockTick()` 函數：

```csharp
static void MasterClockTick()
{
    // CPU gate: 每 12 tick 觸發一次
    if (mcCpuClock == 0) { cpu_step_one_cycle(); ... }
    else if (mcCpuClock == 8) { /* NMI 偵測 */ }

    // PPU gate: 每 4 tick 觸發一次（全步），每 4 tick 的中間觸發半步
    if (mcPpuClock == 0) { ppu_step_new(); }
    else if (mcPpuClock == 2) { ppu_half_step_new(); }

    // IRQ/APU gate
    if (mcCpuClock == 5) { /* IRQ 捕獲 */ }
    else if (mcCpuClock == 12) { apu_step(); }

    mcCpuClock--;
    mcPpuClock--;
}
```

### 3.3 移植結果 — 優缺點

**優點**：
- 達成所有測試滿分（blargg 184/184 + AccuracyCoin 136/136）
- PPU sub-dot 行為正確（VBL latch、sprite 0 hit 延遲管線等）
- 先前無法通過的 timing 敏感遊戲畫面恢復正常
- Mapper 064 (Tengen RAMBO-1) 的 Klax 畫面自動修復

**缺點**：
- 效能從 264 FPS 降至 87 FPS（降 67%）
- 經過大量 JIT 層級優化後回升至 ~104 FPS，但仍低於移植前
- 在類比模式（Ultra NTSC + CRT 模擬）8x 解析度下低於 60 FPS 即時速度
- 程式碼複雜度大幅提升

---

## 4. Catch-up 架構 — 「預測未來」的效能解法

### 4.1 基本概念

Catch-up（又稱 Event-Driven）架構的核心思想是：**不需要每一個 tick 都推進所有元件，只在元件之間真正需要互動時才同步**。

以現實生活類比：
- **Polling（輪詢）**：你每秒鐘看一次手機是否有新訊息（不管有沒有）
- **Catch-up（事件驅動）**：手機響了才看（只在有事時才處理）

在 NES 模擬中：
- **Polling**：每一個 Master Clock tick 都同步 CPU/PPU/APU
- **Catch-up**：CPU 自由奔跑，只在 CPU 讀寫 PPU/APU 暫存器時才讓 PPU/APU「追上來」

```
Polling 模式:
  tick 1: CPU · PPU · APU
  tick 2: CPU · PPU · APU
  tick 3: CPU · PPU · APU
  ...（每 tick 都同步所有元件）

Catch-up 模式:
  CPU 跑 100 個 cycle（PPU/APU 不動）
  CPU 讀取 $2002（PPU 狀態暫存器）
  → 此時 PPU 立刻「追上」300 個 dot
  CPU 繼續跑...
```

### 4.2 為什麼 Catch-up 更快

1. **消除 dispatch 開銷**：Polling 每幀呼叫 357,368 次 MasterClockTick；Catch-up 只在 I/O 存取時才同步，次數可能降到數千次
2. **Cache 友善**：CPU 連續執行時，CPU 相關的記憶體保持在 L1 cache 中；PPU 連續執行時同理。Polling 模式不斷切換元件，造成 cache 震盪
3. **VBlank 快進**：PPU 在 VBlank 期間（scanline 241-260）不渲染畫面。如果沒有 I/O 存取，可以用數學直接計算跳過，省去數千次 PPU step

### 4.3 為什麼 Catch-up 很難做對

核心困難在於**預測未來**。CPU 自由奔跑時，必須知道「什麼時候該停下來」：

**困難 1 — 中斷時機預測**：NMI（不可遮罩中斷）在 VBlank 開始時觸發。如果 CPU 跑過了觸發點，就錯過中斷，遊戲邏輯崩壞。必須預先計算下一次 NMI 的精確時間。

**困難 2 — Mapper 耦合**：MMC3 Mapper 透過監聽 PPU 的地址線（A12）來計算掃描線。如果 PPU 沒有即時更新地址線，Mapper 的 IRQ 計數會出錯。

**困難 3 — DMC DMA 時間悖論**：APU 的 DMC 通道會「偷走」CPU 的 3-4 個 cycle 來讀取音效資料。如果 CPU 已經跑過了 DMA 觸發點，就必須「回退時間」— 這在軟體中極難正確實作。

**困難 4 — Sprite 0 Hit 輪詢**：Super Mario Bros. 的 CPU 在每一幀都會持續讀取 $2002 等待 PPU 回報 Sprite 0 碰撞。這迫使 Catch-up 在此時退化到幾乎逐 cycle 的同步頻率。

### 4.4 Polling vs Catch-up — 精確度的差異

Polling 是「物理模擬」— 只要 FSM（有限狀態機）的邏輯正確，結果自動正確，因為它老老實實地模擬每一個時鐘。

Catch-up 是「數學預測」— 開發者必須為每一種同步場景寫出正確的預測公式。任何公式錯誤（哪怕差 1 個 clock）都可能導致測試失敗。

這解釋了為什麼 AprNes 的 Polling 架構能達成 136/136 AccuracyCoin 滿分 — 它從不需要「猜測」未來會發生什麼。

---

## 5. AprNes 的現狀與未來方向

### 5.1 效能現狀

移植 TriCNES Master Clock 模型後，經過大量效能優化（方法拆分、SWAR、branchless 等），在 .NET Framework 4.8 上達到 ~104 FPS。在 .NET 10（AprNesAvalonia）上達到 ~119 FPS，得益於 TieredPGO 的 JIT 優化。

| 平台 | FPS | 說明 |
|------|-----|------|
| .NET Framework 4.8 (Debug) | ~104 | 已觸及 JIT 優化天花板 |
| .NET 10 (Release, PGO) | ~119 | PGO inline 所有熱區方法 |
| 類比 Ultra+CRT 8x | ~57 | 最大視覺負載 |

### 5.2 Catch-up 的學術研究可能性

我們有興趣在未來以**學術研究**的性質，嘗試在現有 Master Clock 模型的基礎上加入 Catch-up 機制。預估可帶來 25-40% 的效能提升。

但這是有嚴格前提的探索：

1. **所有 blargg 測試（184/184）必須維持滿分**
2. **所有 AccuracyCoin 測試（136/136）必須維持滿分**
3. 任何導致測試回歸的 Catch-up 實作都會被 revert

換言之：**精確度永遠優先於效能**。如果 Catch-up 無法在不犧牲任何測試通過率的前提下提升效能，我們寧可不用。

最小可行的切入點是 **VBlank 快進**：在 scanline 241-260 期間，PPU 不渲染畫面，如果沒有 I/O 存取，可以安全地用數學快進。這是風險最低的部分性 Catch-up，預估省 ~7-8% PPU 時間。

### 5.3 為什麼 .NET 10 遷移優先於 Catch-up

透過 PerfView JIT 分析，我們驗證了 .NET 10 的 TieredPGO 能將 `ppu_step_new`、`apu_step`、`cpu_step_one_cycle` 等所有熱區方法成功 inline — 這在 .NET Framework 4.8 上因為 IL size 門檻（100 bytes）而完全不可能。

.NET 10 遷移提供了：
- **零風險**的效能提升（相同 NesCore 程式碼，不改 timing 邏輯）
- PGO 自動識別熱路徑並最佳化
- 未來如果實作 Catch-up，在 .NET 10 上的效益更大（inline + catch-up 疊加）

---

## 6. 總結

| 模型 | 精度 | 效能 | 複雜度 | 適用場景 |
|------|------|------|--------|---------|
| Instruction-Level | 低 | 高 | 低 | 簡單遊戲、教學用途 |
| Cycle-Level | 高 | 中 | 中 | 多數精確模擬器 |
| Master Clock | 極高 | 低 | 高 | 追求滿分的極致精確模擬器 |
| Catch-up | 取決於實作 | 高 | 極高 | 效能敏感的精確模擬器 |

AprNes 選擇了 Master Clock 模型，以「老老實實模擬每一個時鐘」的方式達成測試滿分。這是用效能換取精確度和簡潔性的選擇。未來可能在不犧牲精確度的前提下，以學術研究的態度探索 Catch-up 優化的可能性。

> 「正確地模擬產生結果的過程，而非走捷徑模擬結果本身。」
