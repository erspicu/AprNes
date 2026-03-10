# Master Clock Scheduler 改造成本評估

**日期**: 2026-03-07
**基線**: blargg 174/174 (100%), AccuracyCoin 119/136 (87.5%)
**目標**: AccuracyCoin 136/136 (100%)

---

## 現狀摘要

目前以 CPU cycle 為最小時間單位（CPU-Driven Tick Model）。每次 `Mem_r()`/`Mem_w()` 呼叫 `tick_pre()` 推進 3 PPU dots + 1 APU step。無法區分 M2 rise/fall edge，DMA timing 使用近似模型。

**剩餘 16 FAIL + 1 SKIP 分布**：

| 類別 | 測試數 | 根因 | 需要的精度 |
|------|--------|------|-----------|
| P13 DMA 時序 | 6 | DMA halt/alignment cycle count | M2 phase + DMA state machine |
| P10 SH* bus conflict | 5 | DMA-to-instruction RDY 交互 | 內部 bus 仲裁 |
| P14 DMC/APUReg/Controller | 3 | 內部 address bus、DMA timing | sub-cycle bus state |
| P19 SprSL0/$2004Stress | 2 | pre-render sprite eval、per-dot OAM | per-dot PPU evaluation |
| P12 IFlagLatency | 1 | DMC DMA 累積時序偏移 | Master Clock scheduler |
| P20 ImpliedDummy | 1 | P13 前置條件連鎖失敗 | 同 P13 |
| **合計** | **17** | | |

---

## 程式碼規模

| 檔案 | 行數 | 描述 |
|------|------|------|
| CPU.cs | 2,395 | 241 opcodes 的 giant switch + 中斷處理 |
| PPU.cs | 1,040 | scanline 渲染 + VBL/NMI + sprite eval |
| APU.cs | 1,002 | 5 聲道 + frame counter + DMC DMA |
| MEM.cs | 345 | tick/memory dispatch + DMA |
| Main.cs | 313 | ROM loader + 主迴圈 |
| IO.cs | 93 | $2000-$4017 register dispatch |
| JoyPad.cs | 58 | 手把 strobe/read |
| Mapper/*.cs | 952 (13 files) | MMC3, UxROM, CNROM 等 |
| **總計** | **6,198** | |

**Mesen2 對比** (C++, 同功能範圍):
- NesCpu.cpp: 621 行 + NesCpu.h: 858 行 (共 1,479)
- 但 Mesen2 CPU 用 opcode table + addressing mode 分離設計，非 giant switch

---

## 方案比較

### 方案 A：完整 Master Clock + CPU State Machine

**改動範圍**：

| 檔案 | 改動量 | 說明 |
|------|--------|------|
| CPU.cs | **重寫 ~2,000 行** | 241 opcodes 從 inline 改為 per-cycle state machine。每個 opcode 需拆為 2~8 個 cycle state。估計新增 ~3,000 行。 |
| MEM.cs | **重寫 ~200 行** | tick()/tick_pre()/tick_post() → master clock scheduler。Mem_r/Mem_w 改為 catch-up + bus access。 |
| APU.cs | **改寫 ~300 行** | dmcfillbuffer() 完全重寫為 DMA state machine (halt/dummy/read states)。OAM DMA 併入統一 DMA scheduler。 |
| PPU.cs | **改寫 ~150 行** | ppu_step_new() 呼叫時機改為 master clock 排程（邏輯不變，但 caller 改變）。sprite eval 需改為真正的 per-dot FSM。 |
| IO.cs | **小改 ~20 行** | register dispatch 不變，但 write timing 需對齊 M2 phase |
| JoyPad.cs | **小改 ~10 行** | strobe timing 自然精確 |
| Main.cs | **改寫 ~50 行** | 主迴圈改為 master clock driven |
| Mapper/*.cs | **不需改** | 介面不變（MapperR/W 仍由 memory dispatch 呼叫） |

**工作量估計**：

| 階段 | 說明 | 估計工作量 |
|------|------|-----------|
| 1. CPU State Machine | 241 opcodes × 每個需拆解 cycle → ~500 個 state transitions | **極大** — 最耗時的單一任務 |
| 2. Master Clock Core | MEM.cs scheduler + catch-up 機制 | **中** — 結構明確 |
| 3. DMA State Machine | DMC DMA + OAM DMA 統一排程 | **大** — 最難除錯的部分 |
| 4. PPU Per-Dot Eval | sprite evaluation FSM (含 pre-render) | **中** — 邏輯已部分存在 |
| 5. 回歸測試 | 174 blargg + 136 AC 全部重跑、逐一除錯 | **極大** — 不可預測 |

**回歸風險**: **極高**。CPU giant switch 重寫等同從零開始驗證所有 174 blargg 測試。歷史經驗顯示每次 timing 改動都會引發連鎖回歸。

**預估收益**: 理論上可達 136/136，但實際取決於除錯品質。

---

### 方案 B：Mesen2 式 Catch-Up 架構（推薦折衷）

**核心思路**: 不改 CPU giant switch，保留 `Mem_r()`/`Mem_w()` 觸發 timing 的模式。但改 `StartCpuCycle()` 內部為精確的 master clock catch-up，加上 M2 phase 追蹤。

這正是 Mesen2 的做法：`NesCpu.cpp:317` 的 `StartCpuCycle()` 只是推進 master clock 並 catch-up PPU，CPU 本身仍是同步的 opcode 函數。

**改動範圍**：

| 檔案 | 改動量 | 說明 |
|------|--------|------|
| CPU.cs | **不需改** | giant switch 完全保留！ |
| MEM.cs | **改寫 ~100 行** | tick_pre() 加入 M2 phase 精確計算（read cycle vs write cycle 的 master clock offset 不同）。已部分實現（masterClock, catchUpPPU/APU）。 |
| APU.cs | **改寫 ~200 行** | DMA 重寫為 Mesen2 的 `ProcessPendingDma()` 模型：halt → dummy → read/write cycles，每個 cycle 有精確的 StartCpuCycle/EndCpuCycle。 |
| PPU.cs | **改寫 ~100 行** | sprite evaluation 改為 per-dot FSM（P19 需要）。$2004 read 行為細化。 |
| IO.cs | **不需改** | |
| JoyPad.cs | **不需改** | |

**工作量估計**：

| 階段 | 說明 | 估計工作量 |
|------|------|-----------|
| 1. M2 Phase 精確化 | tick_pre() 的 masterClock offset 區分 read/write | **小** — 已有 catch-up 架構 |
| 2. DMA 重寫 | ProcessPendingDma() 翻譯為 C# | **大** — Mesen2 有 ~150 行 DMA 邏輯 |
| 3. PPU Sprite Eval | per-dot FSM + pre-render line | **中** |
| 4. 回歸測試 | 主要影響 DMA 相關測試 | **中** — CPU 不變，回歸範圍較小 |

**回歸風險**: **中**。CPU opcode 完全不動，PPU rendering 核心不動，風險集中在 DMA 和 sprite eval。

**預估收益**: +12~15 項（130~134/136）。P10 SH* 可能仍需要內部 bus 仲裁才能通過。

---

### 方案 C：僅 DMA State Machine（最小改動）

只重寫 DMA 部分，不動 tick_pre/tick_post 結構。

| 檔案 | 改動量 |
|------|--------|
| APU.cs | 改寫 dmcfillbuffer() + oamDmaExecute() (~150 行) |
| MEM.cs | 小改 (~30 行)，DMA stolen cycle 精確化 |

**預估收益**: +6~8 項（P13 DMA 系列），風險最低但天花板也最低。

---

## 成本對照表

| | 方案 A (全重寫) | 方案 B (Catch-Up) | 方案 C (僅 DMA) |
|---|---|---|---|
| **改動行數** | ~3,500+ 行 | ~400 行 | ~180 行 |
| **檔案數** | 7 | 3 | 2 |
| **CPU.cs** | 重寫 | 不動 | 不動 |
| **回歸風險** | 極高 | 中 | 低 |
| **預估收益** | 136/136 (理論) | 130~134/136 | 125~127/136 |
| **最難部分** | CPU state machine (241 opcodes) | DMA state machine | DMA state machine |

---

## 建議

**推薦方案 B（Mesen2 式 Catch-Up）**，理由：

1. **CPU.cs 完全不動** — 這是最大的風險點（2,395 行 giant switch），方案 B 完全避開
2. **已有 catch-up 基礎** — MEM.cs 已實現 masterClock + catchUpPPU/catchUpAPU，只需精確化
3. **投入產出比最佳** — ~400 行改動換取 +11~15 項 AccuracyCoin
4. **漸進式可行** — 可先做 DMA (方案 C 子集) 驗證，再擴展到 sprite eval

剩餘無法解決的項目（P10 SH* 的 5 項）需要 6502 內部 bus 仲裁模擬，這在任何方案中都是最深層的改動，可視為「最後 2%」的問題。

---

## 執行順序建議（方案 B）

```
Phase 1: DMA State Machine          → 預估 +6~8 (P13 系列)
Phase 2: M2 Read/Write Offset       → 預估 +1~2 (P12 IFlagLatency, P20 ImplDummy)
Phase 3: PPU Per-Dot Sprite Eval    → 預估 +2 (P19 SprSL0, $2004Stress)
Phase 4: APU Internal Bus           → 預估 +1~2 (P14 APUReg, DMC)
Phase 5: SH* Bus Arbitration        → 預估 +0~5 (P10, 難度最高)
```
