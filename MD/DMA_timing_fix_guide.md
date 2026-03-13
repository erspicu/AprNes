# DMA Timing 修復指南

**目標**: 修復 AccuracyCoin 剩餘 14 FAIL + 1 SKIP（全部與 DMA timing 相關）
**潛在收益**: +14 PASS（121/136 → 135/136），另外 +1 從 SKIP 變 PASS（→ 136/136）
**難度**: 極高（需要結構性改動，涉及 MEM.cs / APU.cs / CPU.cs 核心時序）
**狀態**: **已完成** — 最終成果 136/136 AC (BUGFIX33-56)，不需要方案 A (MCU Split)

---

## 一、問題總覽

| 根因 | 測試 | 數量 | 說明 |
|------|------|------|------|
| A: DMA sub-cycle 精度 | P13×6 + P10×5 + P20×2 | 13 | DMADMASync_PreTest 前置條件失敗 |
| B: DMC/APU 互動 | P14 DMC + P14 Strobe | 2 | DMC 子測試 + OAM DMA parity |
| D: DMC DMA 累積偏移 | P12 Test E | 1 SKIP | DMC DMA ~12 cycle drift → hang |

所有問題的共同根因：**我們的 DMA 引擎以整個 CPU cycle 為最小單位，缺乏 Mesen2 的 Start/End 半 cycle 精度**。

---

## 二、核心差異：AprNes vs Mesen2

### 2.1 CPU Cycle 的 Start/End 分離

**Mesen2 的模型**（NesCpu.cpp）：

```
MemoryRead(addr):
    ProcessPendingDma(addr)     ← DMA 在 bus access 前觸發
    StartCpuCycle(forRead=true) ← 前半 cycle：masterClock += 5 MCU, CC++, PPU catch-up, APU
    value = Read(addr)          ← 實際 bus read
    EndCpuCycle(forRead=true)   ← 後半 cycle：masterClock += 7 MCU, PPU catch-up, NMI edge, IRQ
    return value
```

**關鍵細節**：
- `StartCpuCycle(forRead=true)`: masterClock += `_startClockCount - 1` = **5 MCU**
- `EndCpuCycle(forRead=true)`: masterClock += `_endClockCount + 1` = **7 MCU**
- 合計 5 + 7 = **12 MCU** = 1 CPU cycle = 3 PPU dots
- 讀/寫不對稱：read = (5, 7), write = (7, 5)
- NMI edge detection 在 **EndCpuCycle** 中（φ2 階段）
- IRQ 狀態也在 **EndCpuCycle** 中採樣

**AprNes 目前的模型**（MEM.cs）：

```
Mem_r(addr):
    StartCpuCycle()              ← 完整 12 MCU：CC++, NMI promote, PPU×3, APU, IRQ
    ProcessPendingDma(addr)      ← DMA 在 StartCpuCycle 後觸發
    value = read(addr)           ← bus read
    EndCpuCycle()                ← 空（placeholder）
    return value
```

**問題**：
1. **所有 12 MCU 集中在 StartCpuCycle**：PPU 3 dots + APU + NMI + IRQ 全在 bus access 前完成
2. **無法表達半 cycle 精度**：DMA dummy read 的 side effects（如 $2002 清 VBL flag）在時序上不精確
3. **ProcessPendingDma 在 StartCpuCycle 後**：cpuCycleCount 已經 +1，需要翻轉 parity 補償

### 2.2 DMA 中的 phantom read side effects

**P13 測試要求**：DMA halt/dummy/alignment cycles 的 phantom read 需要有正確的 side effects：

- `$2002` 讀取：必須清除 VBL flag（DMA + $2002 Read 測試）
- `$4015` 讀取：必須清除 frame counter IRQ flag（DMA + $4015 Read 測試）
- `$4016/$4017`：必須推進 controller shift register（但 NES 型號有差異）

**目前問題**：phantom read 用 `mem_read_fun[readAddress](readAddress)` 直接呼叫，
但時序不在正確的半 cycle 位置，導致 side effects 的觀測時機錯誤。

### 2.3 Bus Conflict 合併

**P13 DMC DMA Bus Conflicts 測試**：當 DMC DMA 讀取的位址落在 $4000-$401F 範圍時，
內部 APU register 和外部匯流排同時驅動 data bus，結果是 AND 合併。

公式：`result = Read(dmcSampleAddr) AND Read($4000 | (dmcSampleAddr & 0x1F))`

**目前狀態**：ProcessDmaRead 已實作 bus conflict，但 timing 不正確導致讀取的值不對。

---

## 三、修復方案

### 方案 A：Start/End 半 Cycle 分離（推薦）

將 StartCpuCycle 的 12 MCU 拆為 5 + 7（讀）或 7 + 5（寫），與 Mesen2 對齊。

#### Step 1：拆分 StartCpuCycle / EndCpuCycle

```csharp
// MEM.cs
static void StartCpuCycle(bool forRead)
{
    masterClock += forRead ? (MASTER_PER_CPU / 2 - 1) : (MASTER_PER_CPU / 2 + 1);
    // = forRead ? 5 : 7 (MCU)
    cpuCycleCount++;
    m2PhaseIsWrite = (cpuCycleCount & 1) != 0;
    catchUpPPU();  // PPU runs ~1.25 or ~1.75 dots
    catchUpAPU();  // APU may fire here
    if (strobeWritePending > 0) processStrobeWrite();
}

static void EndCpuCycle(bool forRead)
{
    masterClock += forRead ? (MASTER_PER_CPU / 2 + 1) : (MASTER_PER_CPU / 2 - 1);
    // = forRead ? 7 : 5 (MCU)
    catchUpPPU();  // PPU processes remaining ~1.75 or ~1.25 dots

    // NMI edge detection (φ2 phase, per Mesen2)
    if (nmi_delay_cycle >= 0 && cpuCycleCount > nmi_delay_cycle)
    { nmi_pending = true; nmi_delay_cycle = -1; }

    // IRQ sampling (penultimate cycle)
    irqLinePrev = irqLineCurrent;
    irqLineCurrent = (statusframeint && !apuintflag) || statusdmcint || statusmapperint;
}
```

#### Step 2：更新 Mem_r / Mem_w / ZP_r

```csharp
static byte Mem_r(ushort addr)
{
    ProcessPendingDma(addr);   // DMA 在 cycle 開始前
    StartCpuCycle(true);       // 前半 cycle (5 MCU)
    byte val = mem_read_fun[addr](addr);  // bus read
    EndCpuCycle(true);         // 後半 cycle (7 MCU)
    return val;
}

static void Mem_w(ushort addr, byte val)
{
    StartCpuCycle(false);      // 前半 cycle (7 MCU)
    mem_write_fun[addr](addr, val);  // bus write
    EndCpuCycle(false);        // 後半 cycle (5 MCU)
}
```

#### Step 3：更新 tick() 和 DMA 引擎

DMA 中的 tick() 也需要 Start+End：

```csharp
static void tick()
{
    StartCpuCycle(true);   // DMA cycles are reads
    EndCpuCycle(true);
}
```

ProcessPendingDma 中的每個 phantom read 需要在 Start 和 End 之間執行：

```csharp
// Halt cycle
StartCpuCycle(true);
if (!skipPhantomRead)
    mem_read_fun[readAddress](readAddress);  // phantom read with side effects
EndCpuCycle(true);
```

#### Step 4：修正 PPU register timing

**最大風險**：`ppu_r_2002` 的 VBL suppression 檢查目前依賴所有 3 dots 在 bus access 前完成。
拆分後，只有 ~1.25 dots 在 Start 中完成，剩餘 ~1.75 dots 在 End 中。

需要調整：
- `ppu_r_2002` 中 `scanline == 241 && cx == 1` 的 VBL suppression window
- VBL set timing（目前在 `ppu_step_new` 的 sl=241, cx=1）
- NMI delay promote（移到 EndCpuCycle）

**這是最困難也最容易引起回歸的部分**，需要大量測試驗證。

#### Step 5：ProcessPendingDma 移到 StartCpuCycle 前

目前的呼叫順序是 `StartCpuCycle → ProcessPendingDma`。需要改為：

```
ProcessPendingDma → StartCpuCycle → bus access → EndCpuCycle
```

這與 Mesen2 的 `MemoryRead` 一致。改動後可以移除 parity 補償 hack。

---

### 方案 B：Master Clock Scheduler（完全重構）

將 CPU/PPU/APU 改為事件驅動的 master clock scheduler，每個元件獨立排程。

**優點**：理論上可解決所有 timing 問題，包括 DMC DMA 累積偏移
**缺點**：工程量巨大，幾乎重寫整個時序系統，回歸風險極高

**不推薦作為第一步**。方案 A 已足夠解決大部分問題。

---

## 四、修復順序

### Phase 1：Start/End 拆分（基礎建設）

1. 實作 `StartCpuCycle(bool forRead)` / `EndCpuCycle(bool forRead)` 的 5+7 / 7+5 分配
2. 更新 `Mem_r`、`Mem_w`、`ZP_r` 的呼叫順序
3. 更新 DMA 引擎中的 `tick()` 使用 Start+End
4. **驗證**：blargg 174/174 無回歸（這一步最容易出問題）

**重點風險**：
- VBL/NMI timing 會改變（ppu_vbl_nmi 10 tests, vbl_nmi_timing 7 tests）
- Sprite 0 hit timing 可能偏移（sprite_hit_tests 11 tests）
- APU frame counter IRQ timing 可能偏移（blargg_apu 11 tests）

**緩解策略**：
- 先用 `MASTER_PER_CPU/2 = 6` 的等分拆（6+6 而非 5+7），確認基礎架構正確
- 再逐步調整為 5+7，修復每步引入的回歸
- 或者保持 NMI/IRQ 在 StartCpuCycle 中（不完全匹配 Mesen2，但減少回歸）

### Phase 2：DMA Phantom Read Side Effects

1. 確保 halt/dummy cycles 的 phantom read 通過正常的 `Mem_r` 路徑（包含 side effects）
2. 修正 `ProcessDmaRead` 的 bus conflict timing
3. **驗證**：P13 DMADMASync_PreTest 通過 → 解鎖其他 P13 測試

### Phase 3：DMA Parity 和 Interleaving

1. 移除 parity 補償 hack（ProcessPendingDma 已在正確位置）
2. 修正 DMC DMA + OAM DMA 重疊時的 cycle count
3. **驗證**：P13 全部 6 tests、P20 Instruction Timing + Implied Dummy Reads

### Phase 4：SH* DMA Interaction

1. 實作 RDY line 行為：DMA halt 在 write cycle 時延遲到下一個 read cycle
2. SH* 指令的 write cycle 不被 DMA halt 中斷
3. **驗證**：P10 SH* 5 tests（err=7 → PASS）

### Phase 5：DMC Sub-tests

1. 修正 DMC Load DMA 的精確 timing（parity-dependent countdown）
2. 修正 DMC buffer refill 的 1-byte timing
3. **驗證**：P14 DMC test（err=21 → 更低的 err 或 PASS）

### Phase 6：Controller Strobe Parity

1. OAM DMA 後的 PUT/GET parity 影響 controller read
2. **驗證**：P14 Controller Strobing（err=1 → PASS）

### Phase 7：IRQ Flag Latency Test E

1. 減少 DMC DMA 累積偏移（每次 DMA 的 micro-timing drift）
2. 需要 Start/End 拆分正確才能解決
3. **驗證**：P12 Test E 不再 hang（SKIP → PASS）

---

## 五、關鍵參考資料

### 檔案

| 路徑 | 內容 |
|------|------|
| `ref/Mesen2-master/Core/NES/NesCpu.cpp` | Mesen2 DMA 實作（L317-448） |
| `ref/Mesen2-master/Core/NES/NesCpu.h` | DMA state 定義 |
| `ref/Mesen2-master/Core/NES/APU/NesApu.cpp` | DMC DMA trigger |
| `ref/DMA - NESdev Wiki.html` | DMA timing spec |
| `ref/APU DMC - NESdev Wiki.html` | DMC 行為 |
| `ref/DMC_DMA timing and quirks - nesdev.org.html` | 進階 DMA timing |
| `AprNes/NesCore/MEM.cs` | 目前 DMA 引擎 |
| `AprNes/NesCore/APU.cs` | DMC control |

### NESdev Wiki 重點

1. **Get/Put Cycles**：DMA read 只能在 GET cycle（M2 high），write 只能在 PUT cycle（M2 low）。
   GET/PUT 由 APU clock phase 決定，不是簡單的 even/odd CPU cycle。

2. **Halt 時機**：DMA 只能在 read cycle halt CPU。如果 CPU 在 write cycle，halt 延遲到下一個 read。

3. **DMC DMA 優先級**：DMC read 優先於 OAM read。重疊時 OAM 需要多一個 alignment cycle。

4. **Phantom Read**：halt/dummy cycles 重複 CPU 最後一次的 bus access。
   如果最後一次是讀 $4015，phantom read 也讀 $4015（有 side effect）。

### Mesen2 StartCpuCycle 的 master clock 公式

```cpp
// _startClockCount = 6 (NTSC), _endClockCount = 6
StartCpuCycle(forRead=true):  masterClock += 6 - 1 = 5
EndCpuCycle(forRead=true):    masterClock += 6 + 1 = 7
// Total: 12 MCU per read cycle

StartCpuCycle(forRead=false): masterClock += 6 + 1 = 7
EndCpuCycle(forRead=false):   masterClock += 6 - 1 = 5
// Total: 12 MCU per write cycle
```

讀寫不對稱的原因：M2 信號的上升/下降沿在 CPU cycle 中不對稱。
讀操作在 M2 high 期間（較長的後半段），寫操作在 M2 low 期間（較長的前半段）。

---

## 六、風險評估

| Phase | 回歸風險 | 影響範圍 | 緩解 |
|-------|----------|----------|------|
| 1 (Start/End 拆分) | **極高** | 全部 174 tests | 先 6+6 等分，再調整 |
| 2 (Phantom read) | 中 | DMA 相關 | 限制在 ProcessPendingDma |
| 3 (Parity) | 高 | OAM DMA timing | 逐步調整 |
| 4 (SH* RDY) | 低 | P10 only | 獨立修改 |
| 5 (DMC sub-tests) | 中 | P14 DMC | 逐個子測試修復 |
| 6 (Strobe) | 低 | P14 Strobe only | 獨立修改 |
| 7 (IRQ Latency) | 低 | P12 only | 依賴 Phase 1 正確 |

**總結**：Phase 1 是整個計畫的基石和最大風險點。建議在 git branch 上進行，
每個 sub-step 都跑完整 174 test 回歸。預期 Phase 1 需要多次迭代才能穩定。
