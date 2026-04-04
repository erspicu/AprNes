# PPU TriCNES 完整移植 TODO

**分支**: `feature/ppu-high-precision`
**目標**: 將 TriCNES 的 PPU 子系統完整移植到 AprNes，達成架構對齊
**建立日期**: 2026-04-04
**最後更新**: 2026-04-04

---

## 總覽

TriCNES 是目前唯一 AccuracyCoin 136/136 滿分的參考實作。AprNes 已移植其 master clock gate order、VBL/NMI pipeline、register delays、sprite evaluation FSM 等核心機制，但仍有多項結構性差異。本計劃旨在逐項消除這些差異。

**進度統計**: 20 項 ✅ 完成 / 7 項 ⏸ 暫緩（當前架構下無行為影響）
**測試基線** (分支): blargg 171/174, AC 未重跑（實驗分支允許回歸）

---

## Phase 1: 核心時序基礎設施 — ✅ 全部完成

### ✅ P1-1. Per-dot persistent PPU Address Bus
**優先級**: 最高（MMC3 IRQ 2-dot 差異的根本原因）
**TriCNES**: `PPU_AddressBus`（ushort）持久性變數，在 tile fetch phase 1/3/5/7 更新，中間 dot 保持不變。
**已實作**: `static int ppuAddressBus` 欄位，BG/Sprite/Garbage NT fetch 各 phase 更新，渲染關閉時 = vram_addr。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 3555-3601 (BG), 2841-2970 (sprite), 1674-1678 (disabled)

### ✅ P1-2. Per-dot mapper PPUClock() callback
**優先級**: 最高（依賴 P1-1）
**TriCNES**: 每個 PPU dot 結束時呼叫 `PPU_MapperSpecificFunctions()` → `MapperChip.PPUClock()`。Mapper 自行偵測 A12 邊沿。
**已實作**: IMapper 新增 `void PpuClock()` 方法，所有 56 個 mapper 加入 stub。保留既有 NotifyA12() 機制（TriCNES M2 filter 模型與 blargg 測試不相容）。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 1627-1628, `Mapper_MMC3.cs` lines 293-350

### ✅ P1-3. $2001 Sprite Evaluation Alignment-Dependent Delay
**優先級**: 高
**TriCNES**: Sprite evaluation 的 mask delay 依 `CPUClock & 3` 分兩段：
- Phase ≠ 3: mask 延遲 1 PPU cycle（evaluation 前更新）
- Phase 3: mask 延遲 2 PPU cycle（evaluation 後更新）
**已實作**: `ShowBG_EvalDelay` / `ShowSpr_EvalDelay` 從 Tier 2 (ShowBackGround/ShowSprites) 取值，sprite eval 前後根據 `mcCpuClock & 3` 決定更新時機。取代舊的 `ppuRenderingEnabled_EvalDelay`（Tier 1 來源、固定 1 dot delay）。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 1652-1673

---

## Phase 2: 渲染管線 — 1/3 完成, 2 暫緩

### ✅ P2-1. Tile Fetch Deferred CXinc
**優先級**: 中
**TriCNES**: 使用 flag-based deferred commit，CXinc 在 phase 0（下一個 tile）執行而非 phase 7（同一 tile）。
**已實作**: `commitCXinc` flag — phase 7 設 flag，下一 dot 開頭執行 CXinc()。完整 deferred commit（NT/AT/CHR fetch）與 pixel pipeline 耦合，留待 P2-2。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 3555-3666

### ⏸ P2-2. 3-Dot Pixel Output Pipeline
**優先級**: 中 → **暫緩**（visual output timing only，不影響測試結果）
**TriCNES**: 像素顏色有 3-cycle pipeline：
```
PrevPrevPrevDotColor → PrevPrevDotColor → PrevDotColor → DotColor
```
DrawToScreen 使用 PrevPrevPrevDotColor（3 dot delay）。
**AprNes 現狀**: `ppu_half_step()` 直接輸出像素（~0.5 dot delay）。
**暫緩原因**: 純視覺輸出時序差異，不影響任何測試 ROM 的 pass/fail。完整 deferred commit (P2-1 剩餘) 也依賴此項。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 1724-1727

### ⏸ P2-3. Sprite Shift Register Counter Setup at Dot 339
**優先級**: 中 → **暫緩**（requires per-dot sprite rendering）
**TriCNES**: Dot 339 設定 `PPU_SpriteShifterCounter[i] = PPU_SpriteXposition[i]`，用於下一條 scanline 的 sprite 像素輸出。
**AprNes 現狀**: Sprite rendering 在 dot 257 batch 處理。
**暫緩原因**: 需要先實作 per-dot sprite pixel output（配合 P2-2），是更大的架構變更。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 2996-3008, 3718-3743

---

## Phase 3: Register 精確度 — 1/3 完成, 2 暫緩

### ⏸ P3-1. $2000 DataBus Glitch (1-cycle open bus)
**優先級**: 低-中 → **暫緩**（structural only, dataBus == In at write time）
**TriCNES**: $2000 寫入時，某些欄位在第 1 個 PPU cycle 使用 `dataBus`（CPU data bus）而非 `Input` 值。Alignment 0,1 可見此 glitch（2 cycle delay），Alignment 2,3 下一個 cycle 就修正。
**AprNes 現狀**: 所有欄位立即使用正確值。
**暫緩原因**: 分析確認 AprNes 中 dataBus == In（寫入值就是 CPU bus 值），glitch 無法被觀察到，純結構性差異。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 9466-9499

### ✅ P3-2. $2005 Latch Flip Timing
**優先級**: 中
**TriCNES**: `PPUAddrLatch` 在 **deferred handler 中翻轉**（delay 到期後），而非寫入時立即翻轉。$2006 則是寫入時立即翻轉。
**已實作**: ppu_w_2005() 不再翻轉 vram_latch，改為在 delay handler 到期時檢查 latch 狀態並翻轉。$2006 保持寫入時立即翻轉（與 TriCNES 一致）。影響快速連續 $2005 寫入的行為。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 9615-9642, 1286-1304

### ⏸ P3-3. $2007 State Machine Mystery Write Completion
**優先級**: 低-中 → **暫緩**（no test ROMs exercise this）
**TriCNES**: $2007 有 9 個 state (0-8, idle=9) + alignment-specific mystery write：
- RMW instruction 對 $2007: state 3/6 時 混合 address high byte + written low byte
- Phase 1-3: 額外 mystery write
- State 8: interrupted read-to-write 的 deferred write + extra increment
**AprNes 現狀**: 有 8+ state SM + interrupted read-to-write，alignment 行為部分完整。
**暫緩原因**: 沒有已知測試 ROM 能觸發 mystery write alignment 差異。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 1322-1496, 8968-9047, 9675-9719

---

## Phase 4: Edge Cases — 全部暫緩

### ⏸ P4-1. OAM Corruption on Mid-Frame Rendering Enable/Disable
**優先級**: 低 → **暫緩**（requires variable CPU/PPU alignment）
**TriCNES**: 完整的 per-alignment OAM corruption model，CPUClock 0/1/2/3 各有不同行為。
**AprNes 現狀**: 基本的 `SetOamCorruptionFlags()` + `ProcessOamCorruption()`，無 per-alignment 精確度。
**暫緩原因**: AprNes 固定 alignment 0，其他 alignment 的 corruption 行為無法被觸發。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 9531-9563, 2787-2812, 2832-2838

### ⏸ P4-2. Palette Corruption on $2006 Transition
**優先級**: 低 → **暫緩**（alignment 2 only, AprNes fixed alignment 0）
**TriCNES**: 當 $2006 deferred copy 從 palette 區域（≥$3F00）切換到非 palette 區域（<$3F00）時，觸發 palette corruption。
**AprNes 現狀**: 未實作。
**暫緩原因**: 此行為僅在 alignment 2 下觸發，AprNes 固定 alignment 0。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 1273-1282

### ⏸ P4-3. $2004 OAMBuffer Half-Cycle Updates
**優先級**: 低 → **保持現狀**
**TriCNES**: `PPU_OAMBuffer` 在 half-step 中根據 dot 區間更新。
**AprNes 現狀**: 在 `ppu_r_2004()` 中根據 ppu_cycles_x 直接計算，功能等價但時序可能差 0.5 dot。
**保持原因**: 功能等價，差異僅 0.5 dot，無測試可觀察。

### ⏸ P4-4. Odd Frame Skip Side Effects
**優先級**: 低 → **暫緩**（requires per-dot sprite shift registers）
**TriCNES**: `SkippedPreRenderDot341` flag 持續到 scanline 0, dot 2，影響 sprite shifter 和 dummy NT fetch。
**AprNes 現狀**: 有 odd frame skip 但無 `SkippedPreRenderDot341` side effects。
**暫緩原因**: 需要先實作 per-dot sprite shift register (P2-3)。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 1629-1643

---

## 暫緩項目共同分析

7 個暫緩項目歸為 3 類：

1. **需要 variable CPU/PPU alignment** (P4-1, P4-2): AprNes 固定 alignment 0，這些行為只在其他 alignment 下可觀察。解鎖需引入 variable alignment 模型。
2. **需要 per-dot pixel/sprite pipeline** (P2-2, P2-3, P4-4): 需要從 batch rendering 遷移到 per-dot rendering 架構，是較大的重構。
3. **無可觀測差異** (P3-1, P3-3, P4-3): 結構性差異或無測試 ROM 覆蓋。

---

## 已完成的移植項目（完整列表）

- ✅ Master Clock Gate Order (CPU→NMI→PPU→PPU_half→IRQ→APU)
- ✅ VBL Latch 3-stage Pipeline
- ✅ Sprite 0 Hit 1.5-dot Pipeline
- ✅ Register Delays ($2000/$2001/$2005/$2006 alignment-dependent)
- ✅ $2007 State Machine (basic + interrupted read-to-write)
- ✅ Rendering Enable 4-Tier Model
- ✅ Scroll Updates (CXinc/Yinc/CopyHoriV/CopyVertV)
- ✅ MMC3 M2 Filter (3 CPU cycle counter)
- ✅ Sprite Evaluation Per-Dot FSM
- ✅ NMI Cycle-Based Delay
- ✅ $2002 Split-Read (VBL at start, sprites at end, delayed flags)
- ✅ $2004 Per-Dot Buffer During Rendering
- ✅ $2004 Write During Rendering (increment by 4)
- ✅ $2001 Emphasis Bits Separate Delay
- ✅ Odd Frame Dot Skip
- ✅ OAM Corruption Basic Model
- ✅ Per-dot persistent PPU Address Bus (ppuAddressBus)
- ✅ Per-dot mapper PpuClock() callback (IMapper interface, 56 mappers)
- ✅ $2001 Sprite Eval Alignment-Dependent Delay (ShowBG/ShowSpr_EvalDelay, mcCpuClock & 3)
- ✅ Tile Fetch Deferred CXinc (commitCXinc flag, phase 7 → next dot)
- ✅ $2005 Deferred Latch Flip (latch flips at delay expiry, not at write time)
