# MasterClockTick Structural Unroll — 可行性分析與實作計畫

**日期**: 2026-04-14
**分支**: `feature/remove-legacy-masterclocktick`（本計畫的前置條件已在此分支完成）
**狀態**: 📋 **規劃中（未實作）**

---

## 0. 背景

`master` 已完成的靜態分派重構（commit `2780287` → `b505da4`）達到：
- 4 條 region-specific 主迴圈（`Run_NTSC/FDS/Dendy/PAL`）
- `mcTickFn` 統一所有 tick caller（PPU register handlers + `AlignPhaseForFastPath` 都走 region-specific inline 版）
- Legacy `MasterClockTick` 已移除

但**結構式 unroll（把 12 MC 展開在一個函式內）**當時被判定不可能，因為：
- PPU register handlers 從 `cpu_step` 內呼叫 `mcTickFn` 數次
- 這些巢狀呼叫從內部改變 `mcCpuClock` / `mcPpuClock`
- 外層 unroll 無法預測 nested 消耗幾個 tick → 事件對位必然錯亂

## 1. 新洞察：解耦 nested 後 unroll 可行

**核心**：若 PPU handlers 改呼叫**獨立自含的 `NestedTickN` 函式**（不回呼 `mcTickFn`）：

- Nested 的事件序列**完全可預測**（從起始 `(masterPerCpu, 0)` 出發，N 步的 event 序列是 deterministic）
- `MasterClockTickInlineNTSC` 成為**唯一 outer tick caller** — 從 `Run_NTSC` 的 for-loop 呼叫
- 外層對 nested 的資訊來源收斂成一個：**`cpu_step` 回來後的 `mcCpuClock` 值**

這打破了原本的多來源耦合，讓外層 unroll 變成單一決策問題。

## 2. 架構變化圖

### 現況（已 commit `b505da4`）
```
Run_NTSC loop
  └── MasterClockTickInlineNTSC (gated, 1 MC per call)
      └── cpu_step
          └── ppu_r_2002 → mcTickFn × 7 ← 遞迴回 MasterClockTickInlineNTSC
                           (外層無法預測位置)
```

### 目標（本計畫）
```
Run_NTSC loop
  └── MasterClockTickUnrolledNTSC (12 MC per call)
      └── cpu_step
          └── ppu_r_2002 → NestedTick7_NTSC (self-contained)
          └── ppu_w_2000 → NestedTick2_NTSC (self-contained)
      └── [switch on mcCpuClock post-cpu_step]
          ├── case 12: 無 nested → 完整展開 12 MC 事件序列
          ├── case 10: NestedTick2 ran (已 fire MC 0 events) → 展開 MC 2-11
          └── case 5:  NestedTick7 ran (已 fire MC 0-6 events) → 展開 MC 7-11
```

## 3. 關鍵 trace 表（NTSC 為例）

### NestedTick7 從 `(12, 0)` 起 7 步

| Call | 起始 | 命中 gate | End |
|------|------|-----------|-----|
| 1 | (12, 0) | `mcCpu==12` → APU；`mcPpu==0` → PPU full | (11, 3) |
| 2 | (11, 3) | 無 | (10, 2) |
| 3 | (10, 2) | `mcPpu==2` → PPU half | (9, 1) |
| 4 | (9, 1) | 無 | (8, 0) |
| 5 | (8, 0) | `mcCpu==8` → NMI；`mcPpu==0` → PPU full | (7, 3) |
| 6 | (7, 3) | 無 | (6, 2) |
| 7 | (6, 2) | `mcPpu==2` → PPU half | (5, 1) |

**永不命中的 gate**: `mcCpu==0`（CPU）、`mcCpu==5`（IRQ）

### NestedTick2 從 `(12, 0)` 起 2 步

| Call | 起始 | 命中 gate | End |
|------|------|-----------|-----|
| 1 | (12, 0) | `mcCpu==12` → APU；`mcPpu==0` → PPU full | (11, 3) |
| 2 | (11, 3) | 無 | (10, 2) |

**永不命中的 gate**: `mcCpu==0`、`mcCpu==8`、`mcCpu==5`、`mcPpu==2`

### PAL / Dendy 的序列不同

PAL masterPerCpu=16，從 (16, 0) 起 7 步：
- End (9, 3)
- Events: APU@1, PPU-full@1, PPU-half@4, NMI@5, PPU-full@6（注意 NMI 在 `mcCpu==12`）

Dendy masterPerCpu=15，從 (15, 0) 起 7 步：
- End (8, 3) 或類似
- Events 對應 Dendy gate 常數（NMI `mcCpu==11`, IRQ `mcCpu==5`）

**每個 region 的 NestedTickN 序列都不同**，須各自 trace 驗證。

## 4. 5 處 PPU register handler 的 N 統計

| Handler | N | 出現頻率（ny2011 實測） |
|---------|---|----------------------|
| `ppu_r_2002` | 7 | 高（VBL polling） |
| `ppu_r_2007` | 7 | 中（VRAM read）|
| `ppu_r_2004` | 7 | 低 |
| `ppu_w_2000` | 2 | 低（每 frame 1-3 次） |
| `ppu_w_2007` | 7 | 中（VRAM write） |

總計：**4 × N=7 + 1 × N=2**。需要 2 種變體（`NestedTick7` + `NestedTick2`）× 3 region（NTSC/PAL/Dendy；FDS 共用 NTSC，因 nested 不走 CPU gate）= **6 個新函式**。

## 5. Unrolled Outer 骨架（NTSC）

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
static void MasterClockTickUnrolledNTSC()
{
    // ── MC 0: CPU gate ──
    mcCpuClock = 12;
    bool isDmcActive = dmcDmaRunning & (dmcStatusEnabled | dmcImplicitAbortActive);
    if (cpuIsRead & (isDmcActive | spriteDmaTransfer)) DmaOneCycle();
    else cpu_step_one_cycle();   // may trigger NestedTickN
    if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive = false;

    int state = mcCpuClock;   // 12 = no nesting, 10 = NestedTick2, 5 = NestedTick7

    if (state == 12)
    {
        // Full 12-MC sequence
        MapperObj.CpuCycle();
        apu_step(); mcApuPutCycle = !mcApuPutCycle;
        ppu_step_new();                               // MC 0 PPU full
        ppu_half_step_new();                          // MC 2
        // MC 4: NMI + PPU full
        NMILine |= NMIable && isVblank;
        if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
        ppu_step_new();
        ppu_half_step_new();                          // MC 6
        // MC 7: IRQ + Mapper.CpuClockRise
        IRQLine = irqLineCurrent;
        if (statusframeint && !apuintflag) irqLineCurrent = true;
        MapperObj.CpuClockRise();
        ppu_step_new();                               // MC 8
        ppu_half_step_new();                          // MC 10
    }
    else if (state == 10)
    {
        // NestedTick2 ran — MC 0 events already fired inside
        MapperObj.CpuCycle();
        ppu_half_step_new();                          // MC 2
        // MC 4-10 tail (same as state==12 tail from MC 2 onwards)
        NMILine |= NMIable && isVblank;
        if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
        ppu_step_new();
        ppu_half_step_new();
        IRQLine = irqLineCurrent;
        if (statusframeint && !apuintflag) irqLineCurrent = true;
        MapperObj.CpuClockRise();
        ppu_step_new();
        ppu_half_step_new();
    }
    else // state == 5: NestedTick7 ran
    {
        MapperObj.CpuCycle();
        // MC 0-6 done by NestedTick7
        // MC 7: IRQ
        IRQLine = irqLineCurrent;
        if (statusframeint && !apuintflag) irqLineCurrent = true;
        MapperObj.CpuClockRise();
        ppu_step_new();                               // MC 8
        ppu_half_step_new();                          // MC 10
    }

    // End: counters normalized for next iteration
    mcCpuClock = 0;
    mcPpuClock = 0;
}
```

## 6. 預期收益

### Per 12-MC 窗口比較

| 項目 | Gated 版（現況）| Unrolled 版 | 省 |
|------|----------------|------------|---|
| Gate checks | 72（6 × 12 ticks） | ~0（含 3-way switch） | ~70 |
| Counter decrements | 24（2 × 12） | ~2（start/end state） | ~22 |
| Function call boundaries | 12 | 1 | 11 |

### FPS 預估

- 99% 的 outer iteration 走 fast path「無 nested 完整展開」
- 每 batch 省 ~93 ops × ~30K batches/sec = ~2.8M ops/sec
- 每 op ~1-2 cycles → ~3-6M cycles/sec 省 → **~0.1-0.2% CPU**

保守估計：**+2-5% FPS**（Debug build，含 analog pipeline）。

## 7. 實作階段規劃

### Phase 1: NestedTickN 變體（去遞迴化）

**目標**：消除 PPU handler 的遞迴，效能中性，確保正確性。

1. 新增 6 個函式（`NestedTick7_NTSC/PAL/Dendy` + `NestedTick2_NTSC/PAL/Dendy`）
2. 新增 2 個 function pointer：`nestedTick7Fn`、`nestedTick2Fn`
3. 各 `Run_X` 入口設定這 2 個 pointer
4. 修改 `PPU.cs` 5 處 handler：`for(i=0;i<N;i++) mcTickFn()` → `nestedTickNFn()` 單次呼叫
5. **驗證**：184/184 blargg + AC 138/138（行為需與現況等價）

**預期**：效能中性（可能 ±0.5% 範圍內），架構無遞迴。

### Phase 2: Unrolled Outer

**目標**：FPS 提升，保持正確性。

1. 新增 `MasterClockTickUnrolledNTSC/PAL/Dendy/FDS`（4 個，依 region 常數差異各寫）
2. 各 `Run_X` 的 inner loop 改呼叫 unrolled 版（ExitCheckInterval 除 12 / 15 / 80）
3. **驗證**：184/184 blargg + AC 138/138 + FPS benchmark（應 +2-5%）
4. PAL/Dendy 無 Dendy-專屬測試 ROM，靠 `pal_apu_tests` 把關 PAL

**預期**：+2-5% FPS，測試全綠。

### Phase 3（可選）: 刪 gated form

若 Phase 2 穩定通過所有測試 + 人工煙霧測試後：
- 移除 `MasterClockTickInlineNTSC/FDS/Dendy/PAL` 的 gated 版（僅保留 unrolled 版）
- `AlignPhaseForFastPath` 需改用其他 1-tick 對齊機制（或移除，改初始化時直接 set 到 (0,0)）

## 8. 風險

| 風險 | 緩解 |
|------|------|
| Nested event 序列 trace 寫錯 | 逐個 trace 表人工比對 slow path；撰寫期間開 debug log 對照 |
| PAL/Dendy 序列驗證困難 | 以 `pal_apu_tests` 為底線；Dendy 手動跑 homebrew demo |
| 將來新增 N 值（例如 3 或 5）| Phase 1 的 `NestedTickN` 設計支援任意 N 變體，擴展成本低 |
| Event ordering subtle bug | 每個 Phase 獨立 commit，可 bisect |
| `cpu_step` 可能在同一 cycle 內觸發兩次 nested？（例如 read-modify-write opcodes 寫 PPU register 時）| 需驗證 — 若存在，unrolled 的 switch 需處理更多 state |

## 9. 不在本計畫範圍內

- CPU/PPU/APU 核心邏輯變動（保持 TriCNES v2 精度）
- Mapper 介面變動
- CRT pipeline 優化（另一個獨立方向，ROI 更高）
- 跳到 Mesen2 式 catch-up 時序模型（rewrite 等級，不在此）

## 10. 當前狀態

**本計畫未實作**，僅作為：
1. 技術構想的記錄（避免將來重走錯誤的路線）
2. 潛在下次優化的起點

當前 master 已達 63.28 FPS（Debug, Ultra Analog + CRT + 4x），gated form 為此架構的階段性合理解。本計畫是**next-level** 優化 — 工作量中等（1-2 天），預期收益中等（+2-5% FPS），無阻擋性需求時不急著做。

---

## 附錄：相關 commit / 文件

- 靜態分派起點：master `2780287` Merge feature/static-dispatch-mainloop
- mcTickFn 路由：master `bdb62ca` (feature/remove-legacy-masterclocktick)
- 架構註解：`Main.cs` Run_NTSC 上方「why no structural unroll」註解（此計畫實作後可移除）
- 先前 JIT 報告：
  - `MD/jit/20260413_215013_profile_static_dispatch.md`
  - `MD/jit/20260413_222721_profile_direct_inline.md`
  - `MD/jit/20260414_005000_pmu_icache_analysis.md`
  - `MD/jit/20260414_022201_profile_mctickfn.md`
