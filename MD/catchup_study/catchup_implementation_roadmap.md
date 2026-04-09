# Catch-up 漸進式實作路線圖

**日期**: 2026-04-09
**性質**: 學術研究。任何導致測試回歸的階段即 revert。
**前提**: 184/184 blargg + 136/136 AccuracyCoin 必須始終滿分。

---

## 架構概覽

```
目前 (Polling):
  while (!exit)
    for (batch) MasterClockTick()     ← 每 tick 同步 CPU/PPU/APU

目標 (Hybrid Catch-up):
  while (!exit)
    CpuRunBatch(N)                    ← CPU 連續跑 N cycle
    SyncPPU(targetTime)               ← PPU 追上 CPU 的時間
    SyncAPU(targetTime)               ← APU 追上 CPU 的時間
    CheckEvents()                     ← NMI/IRQ/DMA 事件處理
```

Hybrid 策略：不是全面 Catch-up，而是**在安全場景啟用批次、在危險場景 fallback 到 Polling**。

---

## Phase 0 — VBlank PPU 快進

**目標**: VBlank 期間（scanline 241-260）PPU 不渲染，跳過逐 dot 步進。
**預估收益**: ~7-8% PPU 時間

### 子任務

#### P0-1: VBlank 偵測旗標
- 在 `ppu_step_new()` 的 scanline wrap 處設定 `static bool inVBlank`
- scanline 進入 241 時設 true，進入 preRenderLine 時設 false
- **檔案**: `ppu_new.cs`

#### P0-2: VBlank 期間 PPU 行為分析
列出 VBlank 期間 `ppu_step_new()` 實際執行的邏輯：

| 區塊 | VBlank 期間是否執行 | 可否跳過 |
|------|-------------------|---------|
| Phase 2 deferred updates | 是（$2006/$2005/$2000/$2007 SM） | **否** — register write 隨時可能發生 |
| open_bus_decay_timer-- | 是 | 可用數學：timer -= dots_skipped |
| Scroll increments | 否（gate: scanline < 240 \|\| preRenderLine） | 自然跳過 |
| Phase 3 events | 是（pendingVblank at sl=241 cx=0） | **否** — VBL 開始事件在此 |
| VSET latch pipeline | 是（每 dot） | **否** — VBL flag 管線必須正確推進 |
| MapperObj.PpuClock() | 是（每 dot） | **需分析**：多數 mapper 在 VBlank 期間 PpuClock 為空操作 |
| Odd frame skip | 是（preRenderLine 邊界） | **否** |
| Phase 4 sprite eval | 否（gate: isActiveScanline） | 自然跳過 |
| Phase 5 rendering | 否（gate: isActiveScanline） | 自然跳過 |
| DrawToScreen | 否（gate: scanline < 240） | 自然跳過 |
| Frame render (sl=240 cx=1) | 是 | **否** — RenderScreen 在此觸發 |

**結論**: VBlank 期間仍有 Phase 2、Phase 3、VSET pipeline、MapperObj.PpuClock() 需要逐 dot 執行。**純 VBlank 快進只有在確認無 pending register 操作時才安全**。

#### P0-3: 安全快進條件
只在以下**全部成立**時快進：
```csharp
bool canFastForward =
    inVBlank &&
    ppu2006UpdateDelay == 0 &&
    ppu2005UpdateDelay == 0 &&
    ppu2000UpdateDelay == 0 &&
    ppu2007SM >= 9 &&
    ppu2001UpdateDelay == 0 &&
    ppu2001EmphasisDelay == 0 &&
    !mapperNeedsPerDotVBlank;  // MMC3/MMC5 等需要逐 dot 的 mapper
```

#### P0-4: 快進實作
當條件滿足時，計算可跳過的 dot 數（到下一個事件或 VBlank 結束），直接推進：
```csharp
int dotsToSkip = CalculateNextEvent() - currentDot;
ppu_cycles_x += dotsToSkip;
open_bus_decay_timer -= dotsToSkip;
// 處理 scanline wrap
```

#### P0-5: 驗證
- [ ] blargg 184/184
- [ ] AccuracyCoin 136/136
- [ ] VBL/NMI timing 17/17（重點）
- [ ] even_odd_frames / even_odd_timing（重點）

### 依賴關係
```
P0-1 → P0-2 → P0-3 → P0-4 → P0-5
```

### 風險評估
- **風險**: 低。VBlank 期間 PPU 不渲染，跳過的都是空操作。
- **主要陷阱**: VSET latch pipeline 跨越 VBlank 邊界（sl=241 的 pendingVblank → isVblank 延遲）。必須確保快進不跳過 VBL 開始的前幾個 dot。
- **Mapper 例外**: MMC5 的 `ppuIdleCounter`（在 `CpuCycle()` 中遞減）需要持續推進。MMC3 在 VBlank 期間通常不需要 A12 追蹤（rendering disabled），但保險起見應排除。

---

## Phase 1 — Category A Mapper IRQ 批次計算

**目標**: 純 CPU cycle counter 的 mapper IRQ 用數學取代逐 tick。
**預估收益**: 微小（mapper IRQ 計算本身不是瓶頸）

### 子任務

#### P1-1: 識別 Category A Mapper
已完成（見 interference_analysis.md）：016, 018, 019, 065, 067, 069, VRC cycle mode

#### P1-2: 抽象 IRQ 批次介面
```csharp
interface IBatchableIRQ
{
    // 批次推進 N 個 CPU cycle，回傳是否有 IRQ 觸發
    bool AdvanceCycles(int cycles);
    // 取得下一次 IRQ 觸發的距離（CPU cycles）
    int CyclesUntilNextIRQ();
}
```

#### P1-3: 逐 Mapper 實作
每個 Category A mapper 實作 `AdvanceCycles` 和 `CyclesUntilNextIRQ`。

#### P1-4: 整合到 CPU 批次邏輯
CPU 批次的上限 = `min(nextIOAccess, mapper.CyclesUntilNextIRQ())`

#### P1-5: 驗證
- [ ] 各 mapper 專屬測試 ROM
- [ ] blargg 184/184

### 依賴關係
```
P1-1 → P1-2 → P1-3 → P1-4 → P1-5
（可與 Phase 0 並行）
```

### 風險評估
- **風險**: 低。只影響特定 mapper 的 IRQ 計數，不碰 PPU/APU 核心。
- **主要陷阱**: mapper register write 之間必須分段。不能跨越 register write 批次計算。

---

## Phase 2 — CPU 微批次 + IO 同步屏障

**目標**: CPU 連續執行數條指令，只在 I/O 存取時同步。
**預估收益**: 10-20%（消除 MasterClockTick dispatch + cache 局部性改善）

### 子任務

#### P2-1: 全域時間戳機制
```csharp
static long globalMasterTime;    // 全域 master clock 計數
static long ppuSyncedUntil;      // PPU 已同步到的時間點
static long apuSyncedUntil;      // APU 已同步到的時間點
```

#### P2-2: IO 同步屏障
修改 `IO_read` / `IO_write`，在存取 $2000-$5FFF 時先同步：
```csharp
static byte IO_read(ushort addr)
{
    SyncPPU(globalMasterTime);   // PPU 追上 CPU
    SyncAPU(globalMasterTime);   // APU 追上 CPU
    // 原有 handler...
}
```

**必須覆蓋的地址範圍**:

| 地址 | Handler | 同步需求 |
|------|---------|---------|
| $2000-$2007 | PPU registers | SyncPPU + SyncAPU（$2002 的 7-tick 推進需要 APU 同步） |
| $4000-$4013 | APU registers | SyncAPU |
| $4014 | OAM DMA | SyncPPU + SyncAPU（DMA 依賴 mcApuPutCycle） |
| $4015 | APU status | SyncAPU（讀寫都有 deferred 行為） |
| $4016-$4017 | Controller / APU FC | SyncAPU（$4017 寫入依賴 mcApuPutCycle） |
| $8000-$FFFF | Mapper registers | SyncPPU（MMC3 需要）+ SyncMapper |

#### P2-3: SyncPPU 實作
初期使用 while loop 呼叫現有 `ppu_step_new()`：
```csharp
static void SyncPPU(long targetTime)
{
    while (ppuSyncedUntil < targetTime)
    {
        ppu_step_new();
        ppuSyncedUntil += masterPerPpu;  // 4 (NTSC)
    }
}
```

#### P2-4: SyncAPU 實作
APU 必須逐 cycle 跑（不可數學跳過）：
```csharp
static void SyncAPU(long targetTime)
{
    while (apuSyncedUntil < targetTime)
    {
        apu_step();
        mcApuPutCycle = !mcApuPutCycle;
        apuSyncedUntil += masterPerCpu;  // 12 (NTSC)
    }
}
```
**關鍵**: `mcApuPutCycle` 必須在此正確翻轉。

#### P2-5: NMI/IRQ 事件預測器
CPU 批次不能跑過 NMI/IRQ 觸發點：
```csharp
static long nextNMITime;         // 下一次 NMI 的 master clock 時間
static long nextFrameCounterIRQ; // 下一次 APU Frame Counter IRQ
static long nextDMCTrigger;      // 下一次 DMC timer 歸零
static long nextMapperIRQ;       // 下一次 mapper IRQ（Category A）

static long NextEventTime()
{
    return Math.Min(nextNMITime,
           Math.Min(nextFrameCounterIRQ,
           Math.Min(nextDMCTrigger, nextMapperIRQ)));
}
```

CPU 批次上限 = `NextEventTime() - globalMasterTime`

#### P2-6: NMI 預測計算
NMI 發生在 scanline 241, dot 0（加 1.5 dot 管線延遲 + mcCpuClock==8 鎖存）：
```csharp
// NMI 大約在 sl=241 dot 1-2 可見，精確時間需要考慮：
// - pendingVblank 在 full step (sl=241 cx=0)
// - ppuVSET latch 在 half step
// - isVblank 在下一個 full step
// - NMILine 在 mcCpuClock==8
// 總延遲 ≈ 6-8 master ticks from sl=241 cx=0
```

#### P2-7: DMC 活躍時 fallback
```csharp
if (dmcDmaRunning || dmcsamplesleft > 0)
    FallbackToPolling();  // 退化為 MasterClockTick 逐 tick
```

#### P2-8: PPU half step 整合
目前 `ppu_half_step_new()` 在 mcPpuClock==2 執行。SyncPPU 必須也推進 half step：
```csharp
static void SyncPPU(long targetTime)
{
    while (ppuSyncedUntil < targetTime)
    {
        int phase = (int)((ppuSyncedUntil - ppuPhaseBase) % masterPerPpu);
        if (phase == 0) ppu_step_new();
        else if (phase == masterPerPpuHalf) ppu_half_step_new();
        ppuSyncedUntil++;
    }
}
```

#### P2-9: $2002 的 7-tick EmulateUntilEndOfRead
`ppu_r_2002()` 內部呼叫 7 次 `MasterClockTick()`。在 Catch-up 模式下，這要改為推進 globalMasterTime + 7 並同步所有子系統。

**這是 Phase 2 最困難的部分**。

#### P2-10: mcCpuClock 的處理
PPU 讀取 `mcCpuClock & 3` 做 alignment-dependent 行為。在 Catch-up 模式下，`mcCpuClock` 必須在 SyncPPU 時正確反映 CPU 和 PPU 的相對相位。

可能的做法：從 `globalMasterTime` 推導：
```csharp
mcCpuClock = (int)(masterPerCpu - (globalMasterTime % masterPerCpu));
```

#### P2-11: 驗證
- [ ] blargg 184/184
- [ ] AccuracyCoin 136/136
- [ ] 所有 VBL/NMI timing 測試
- [ ] 所有 DMA 測試
- [ ] 所有 APU timing 測試
- [ ] CPU interrupt timing 測試

### 依賴關係
```
P2-1 → P2-2 → P2-3 + P2-4 (並行)
P2-3 → P2-8
P2-4 → (確認 mcApuPutCycle 正確)
P2-5 → P2-6
P2-2 + P2-5 → P2-7
P2-3 + P2-8 → P2-9 → P2-10
全部 → P2-11
```

### 風險評估
- **風險**: 中高。mcApuPutCycle parity 和 mcCpuClock alignment 是最容易出錯的地方。
- **主要陷阱**:
  - `mcApuPutCycle` 在 SyncAPU 中翻轉次數必須精確
  - `mcCpuClock & 3` 的 alignment 推導必須與 Polling 模式完全一致
  - $2002 的 7-tick 推進必須同時推進 PPU 和 APU
  - PPU half step 的相位必須正確

---

## Phase 3 — MMC3 / DMC DMA 深水區

**目標**: 在 MMC3 遊戲和 DMC 活躍期間也能做部分 Catch-up。
**預估收益**: 額外 5-10%（但只對 Category B mapper 遊戲有效）

### 子任務

#### P3-1: MMC3 M2 Filter 分析
M2 filter 在 mcCpuClock==5 取樣 `ppuAddressBus`。如果 CPU 和 PPU 同步推進，可以在 SyncPPU 時預計算每個 dot 的 `ppuAddressBus` 並模擬 M2 filter。

**但這等同於逐 dot 跑 PPU + 逐 cycle 跑 M2 filter**，節省有限。

#### P3-2: DMC DMA 預測模型
DMC trigger 時間 = 當前 `dmctimer` / 2（每 GET cycle 減 2）。但 `$4015` 寫入會重置，使預測失效。

策略：維護 `nextDMCTrigger` 時間戳，`$4015` 寫入時立即更新。DMC 觸發後 fallback 到 Polling 直到 DMA 完成。

#### P3-3: 運行時自適應
```csharp
enum CatchupMode { Polling, VBlankOnly, PartialBatch, FullBatch }

static CatchupMode DetermineCatchupMode()
{
    if (dmcDmaRunning || dmcsamplesleft > 0)
        return CatchupMode.Polling;
    if (mapperA12IsMmc3 || mmc5Ref != null)
        return CatchupMode.VBlankOnly;
    if (inVBlank && noPendingRegisters)
        return CatchupMode.FullBatch;
    return CatchupMode.PartialBatch;
}
```

#### P3-4: 驗證
- [ ] MMC3 IRQ 18/18
- [ ] DMC DMA 5/5
- [ ] sprdma_and_dmc_dma 2/2
- [ ] 完整 184/184 + 136/136

### 風險評估
- **風險**: 極高。MMC3 的 M2 filter 跨域依賴可能無法在 Catch-up 下完美重現。
- **可能結論**: MMC3 遊戲永遠 fallback 到 Polling，只對 Category A mapper 遊戲做 Catch-up。這仍然有意義 — Category A 覆蓋了大量日系遊戲（Konami、Jaleco、Irem、Namco、Sunsoft）。

---

## 全域架構圖

```
                    ┌─────────────────────────────────────┐
                    │            run() 主迴圈              │
                    └──────────────┬──────────────────────┘
                                   │
                    ┌──────────────▼──────────────────────┐
                    │     DetermineCatchupMode()           │
                    └──────┬───────┬───────┬──────────────┘
                           │       │       │
              ┌────────────▼┐ ┌───▼────┐ ┌▼────────────────┐
              │   Polling    │ │VBlank  │ │ PartialBatch    │
              │(MasterClock  │ │FastFwd │ │ (CPU 微批次)     │
              │ Tick 逐 tick)│ │        │ │                  │
              └──────────────┘ └────────┘ └─────┬────────────┘
                                                │
                                    ┌───────────▼──────────┐
                                    │  IO 同步屏障          │
                                    │  $2000-$5FFF → Sync  │
                                    │  $8000-$FFFF → Sync  │
                                    └───────────┬──────────┘
                                                │
                              ┌─────────────────┼─────────────┐
                              │                 │             │
                        ┌─────▼─────┐    ┌─────▼─────┐ ┌────▼────┐
                        │ SyncPPU() │    │ SyncAPU() │ │SyncMap()│
                        │ while loop│    │ while loop│ │ batch   │
                        │ ppu_step  │    │ apu_step  │ │ or tick │
                        │ + half    │    │ + toggle  │ │         │
                        └───────────┘    └───────────┘ └─────────┘
```

---

## 關鍵設計決策記錄

| 決策 | 選擇 | 理由 |
|------|------|------|
| SyncAPU 是否可以數學跳過？ | **否，必須逐 cycle** | timer reload 值可能被 register write 中途改變；LFSR 不可跳過 |
| SyncPPU VBlank 期間可跳過？ | **有條件可以** | 無 pending register + 非 MMC3/MMC5 時 |
| DMC 活躍時的策略？ | **Fallback Polling** | DMA cycle stealing 依賴 cpuIsRead + mcApuPutCycle，無法預測 |
| MMC3 遊戲的策略？ | **Fallback Polling 或 VBlankOnly** | M2 filter 跨域依賴不可解耦 |
| mcApuPutCycle 如何維護？ | **SyncAPU 中正確翻轉** | 6+ 子系統依賴此欄位 |
| mcCpuClock 如何推導？ | **從 globalMasterTime 計算** | 避免維護第二份計數器 |
| $2002 的 7-tick 如何處理？ | **推進 globalMasterTime + 同步所有子系統** | 最緊密的耦合點，不可簡化 |

---

## 預估效能提升（保守）

| Phase | 前提條件 | 預估提升 | 累計 |
|-------|---------|---------|------|
| Phase 0 | VBlank 無 pending register | 3-5% | 3-5% |
| Phase 1 | Category A mapper | 1-2% | 4-7% |
| Phase 2 | 非 MMC3、非 DMC 活躍 | 10-15% | 14-22% |
| Phase 3 | 自適應（多數場景 fallback） | 3-5% | 17-27% |

**注意**: 這些數字是在 .NET Framework 4.8 上的估計。在 .NET 10 上，PGO 已經 inline 了所有熱區方法，MasterClockTick dispatch 開銷大幅降低，Catch-up 的相對收益會更小。
