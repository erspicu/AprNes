# AprNes Per-Master-Clock 執行模型重構計畫

**日期**：2026-04-02
**分支**：feature/ppu-high-precision
**目標**：移除 catch-up 批次模型，採用 TriCNES 的 per-master-clock 執行模型

---

## 為什麼需要重構

AprNes 目前使用 **catch-up 模型**：每個 CPU cycle 批次跑 3 PPU dots + 1 APU step。
TriCNES 使用 **per-master-clock 模型**：每個 master clock tick 依序觸發 CPU/PPU/APU。

核心差異：
- **TriCNES**：CPU 寫入 PPU register 時，`PPUClock & 3` 反映**真實的 master clock 相位**
- **AprNes**：CPU 寫入時，3 個 PPU dot 已經跑完，`ppuAlignPhase` 只是近似值
- 導致 register write delay、half-step timing、DMA interleaving 等都無法精確對齊

---

## 架構對照

### TriCNES 主迴圈（`_EmulatorCore()`）
```
每個 master clock tick 一次呼叫：
  if (CPUClock == 0)  → 執行 CPU（_6502）+ CPUClock = 12 + mapper callback
  if (CPUClock == 8)  → NMI promotion
  if (PPUClock == 0)  → PPU full step + PPUClock = 4
  if (PPUClock == 2)  → PPU half step
  if (CPUClock == 5)  → IRQ check + mapper CPUClockRise
  if (CPUClock == 12) → APU step + toggle M2 phase
  CPUClock--; PPUClock--
```

### AprNes 目前（catch-up）
```
每個 CPU memory access 一次呼叫 tick()：
  StartCpuCycle():
    masterClock += 12
    catchUpPPU() → 3× (ppu_step + ppu_half_step)  ← 批次
    catchUpAPU() → 1× apu_step                     ← 批次
  [CPU 操作（read/write）]
  EndCpuCycle() → mapper callback
```

---

## 重構策略：最大化現有程式碼重用

### 可直接重用（不改或極小改動）

| 元件 | 檔案 | 說明 |
|------|------|------|
| CPU 指令解碼 | `CPU.cs` opFnPtrs[] | 每個 opcode 的 per-cycle 邏輯已是正確粒度 |
| CPU 一個 cycle 的執行 | `CPU.cs` cpu_step_one_cycle() | 跟 TriCNES 的 _6502 內部邏輯等價 |
| PPU per-dot 邏輯 | `PPU.cs` ppu_step_common/rendering | 不改內容，只改觸發時機 |
| PPU half-step 邏輯 | `PPU.cs` ppu_half_step() | 不改內容，改為 PPUClock==2 觸發 |
| APU per-step 邏輯 | `APU.cs` apu_step() | 不改內容，改為 CPUClock==12 觸發 |
| Mapper CpuCycle callback | 已有 | 保持在 CPU cycle 結束後呼叫 |
| PPU register delay 機制 | PPU.cs 所有 delay counters | 保持，alignment 由真正的 PPUClock 驅動 |
| $2007 state machine | PPU.cs | 保持，tick 時機跟隨 PPUClock |
| PpuBusRead/Write | PPU.cs | 完全不動 |
| Per-dot shift registers | PPU.cs renderLow/renderHigh | 保持，只改觸發時機 |
| 所有 mapper 實作 | Mapper/*.cs | 完全不動 |

### 需要小幅修改

| 元件 | 修改內容 | 估計行數 |
|------|---------|:--------:|
| 加入 `cpuClock`/`ppuClock` 倒數計數器 | MEM.cs 新增變數 | +5 |
| `ppuAlignPhase` 改為從 `ppuClock & 3` 讀取 | PPU.cs register write handlers | ~10 改 |
| NMI edge detection 移到 `cpuClock == 8` | 從 catchUpPPU 移到新主迴圈 | ~5 移動 |
| APU M2 phase 改用 `apuPutCycle` toggle | MEM.cs/APU.cs | +3 |
| Mapper 加入 `CpuClockRise` callback | IMapper + 實作 | +10 |

### 需要重寫的部分

| 元件 | 目前 | 目標 | 估計行數 |
|------|------|------|:--------:|
| **主迴圈** `run()` | 以 CPU 指令為驅動 | 以 master clock tick 為驅動 | ~80 重寫 |
| **時鐘推進** `StartCpuCycle/EndCpuCycle` | 批次 catch-up | 改為 per-tick gate check | ~30 重寫 |
| **catchUpPPU/catchUpAPU** | 批次函式 | **刪除**，改由主迴圈 gate 觸發 | -30 刪除 |
| **DMA 引擎** `ProcessPendingDma` | 阻塞式一次跑完 | Per-cycle gate（跟 TriCNES _6502 的 DMA gate 類似） | ~150 重構 |

### 可刪除的程式碼

| 元件 | 原因 |
|------|------|
| `catchUpPPU_ntsc/pal/dendy()` | 由 master clock gate 取代 |
| `catchUpAPU()` | 由 master clock gate 取代 |
| `cpu_step()` 外層迴圈 | 改為每 tick 執行一個 cycle |
| `ppuAlignPhase` per-dot increment | 直接從 ppuClock 讀取 |

---

## 實作階段

### Phase 1：加入 Master Clock 基礎設施（低風險）
**檔案**：`MEM.cs`
- 加入 `cpuClock`（byte，倒數 12→0）
- 加入 `ppuClockCounter`（byte，倒數 4→0）
- 加入 `apuPutCycle`（bool，每 APU step toggle）
- 初始化在 `init()`

### Phase 2：建立 `EmulatorCore()` 主迴圈（中風險）
**檔案**：`MEM.cs` 或新檔
```csharp
static void EmulatorCore()
{
    if (cpuClock == 0)
    {
        cpuClock = 12;
        cpu_step_one_cycle();    // 現有函式，只執行 1 cycle
        MapperObj.CpuCycle();
    }
    if (cpuClock == 8)
    {
        // NMI promotion（從 catchUpPPU 搬過來）
        bool o = isVblank && NMIable;
        if (o && !nmi_output_prev) nmi_delay_cycle = cpuCycleCount;
        nmi_output_prev = o;
    }
    if (ppuClockCounter == 0)
    {
        ppuClockCounter = 4;
        ppu_step_current_region();  // 現有 ppu_step_ntsc/pal/dendy
    }
    if (ppuClockCounter == 2)
    {
        ppu_half_step();            // 現有函式
    }
    if (cpuClock == 5)
    {
        irqLineCurrent = ...;       // IRQ level check
        MapperObj.CpuClockRise();   // 新 callback
    }
    if (cpuClock == 12)
    {
        apu_step();                 // 現有函式
        apuPutCycle = !apuPutCycle;
    }
    cpuClock--;
    ppuClockCounter--;
}
```

### Phase 3：改寫 `run()` 主迴圈（中風險）
**檔案**：`Main.cs`
```csharp
static public void run()
{
    while (!exit)
    {
        _event.WaitOne();
        EmulatorCore();  // 每次一個 master clock tick
    }
}
```

但注意：TriCNES 的 `_CoreFrameAdvance()` 會跑到 VBlank 才停。需要類似的 frame 邊界處理。

### Phase 4：移除 catch-up 批次（低風險）
**檔案**：`MEM.cs`
- 刪除 `catchUpPPU_ntsc/pal/dendy()`
- 刪除 `catchUpAPU()`
- 修改 `StartCpuCycle()` 不再呼叫 catch-up
- `ppuAlignPhase` 改為 `ppuClockCounter & 3` 直讀

### Phase 5：重構 DMA（高風險）
**檔案**：`MEM.cs`
- `ProcessPendingDma()` 從阻塞式改為 per-cycle gate
- 在 `cpu_step_one_cycle()` 開頭加 DMA gate check（跟 TriCNES `_6502` line 3974 一致）
- Halt/Get/Put 邏輯保留但拆成 per-cycle
- 保持 Mesen2 風格的狀態機（dmcDmaRunning, spriteDmaTransfer 等）

### Phase 6：加入 Mapper `CpuClockRise`（低風險）
**檔案**：`IMapper.cs` + mapper 實作
- 加入 `CpuClockRise()` 空方法
- MMC3 在此觸發 M2 rising edge（已有 A12 通知，此為補充）

---

## 風險與緩解

| 風險 | 嚴重度 | 緩解策略 |
|------|:------:|---------|
| DMA timing 回歸 | 高 | 保持 Mesen2 狀態機邏輯不變，只改觸發方式 |
| NMI timing 回歸 | 中 | 移動到 cpuClock==8 後立即測試 VBL/NMI tests |
| 效能下降 | 中 | per-master-clock 每幀 ~357,366 次呼叫（vs 目前 ~29,781 CPU cycles），約 12x。可用 AggressiveInlining 緩解 |
| PAL/Dendy 相容 | 低 | PAL: cpuClock=16, ppuClock=5; Dendy: cpuClock=15, ppuClock=5。只改常數 |

## 效能評估

| 指標 | 目前（catch-up） | 重構後（per-master-clock） |
|------|:----------------:|:------------------------:|
| 主迴圈呼叫次數/幀 | ~29,781 (CPU cycles) | **~357,366** (master ticks) |
| PPU step 呼叫次數/幀 | ~89,342 (3x CPU) | ~89,342（不變） |
| APU step 呼叫次數/幀 | ~29,781 | ~29,781（不變） |
| Gate check overhead | 無 | ~357,366 × (6 個 if 判斷) |

主迴圈次數增加 12x，但每次只做簡單的 if 判斷（整數比較）。PPU/APU 的實際執行次數不變。預估效能影響 10-20%，可接受。

---

## 執行進度

| Phase | 狀態 | Commit | blargg | 備註 |
|:-----:|:----:|--------|:------:|------|
| 1 | ✅ | `f644784` | 174/174 | mcCpuClock/mcPpuClock/mcApuPutCycle 加入 |
| 2 | ✅ | `8ad749d` | 174/174 | EmulatorCoreTick_NTSC 建立（未啟用） |
| 3 | ✅ | `6c4ff36` | 169/174 | StartCpuCycle 切換 per-master-clock NTSC（-5 NMI/sprite） |
| 4 | ✅ | `88105e3` | 166/174 | 刪除 legacy catch-up，全區域 per-master-clock（-84 行） |
| 5 | ✅ 評估 | — | — | DMA 引擎已在 CpuRead gate，跟 TriCNES 等價，不需大改 |
| 6 | ✅ | `f25049a` | 166/174 | CpuClockRise() 加入 IMapper + 60 mappers |

**全部 6 Phase 完成。** 架構已切換為 per-master-clock gates。
166/174 blargg（8 NMI/sprite timing regression — 需要後續調整）。

---

## 測試策略

1. **Phase 1-2 完成後**：跑 174 blargg — 確認基礎架構不回歸
2. **Phase 3 完成後**：跑 174 blargg + AC 136 — 確認主迴圈切換正確
3. **Phase 5 完成後**：重點測 DMA 相關 tests（dmc_dma_during_read4 系列）
4. **全部完成後**：SMB3 畫面驗證（綠線應消失）+ scanline/colorwin demo ROMs
5. **效能測試**：Release build benchmark 對比
