# PPU TriCNES 完整移植 TODO

**分支**: `feature/ppu-high-precision`
**目標**: 將 TriCNES 的 PPU 子系統完整移植到 AprNes，達成架構對齊
**建立日期**: 2026-04-04

---

## 總覽

TriCNES 是目前唯一 AccuracyCoin 136/136 滿分的參考實作。AprNes 已移植其 master clock gate order、VBL/NMI pipeline、register delays、sprite evaluation FSM 等核心機制，但仍有多項結構性差異。本計劃旨在逐項消除這些差異。

---

## Phase 1: 核心時序基礎設施

### P1-1. Per-dot persistent PPU Address Bus
**優先級**: 最高（MMC3 IRQ 2-dot 差異的根本原因）
**TriCNES**: `PPU_AddressBus`（ushort）持久性變數，在 tile fetch phase 1/3/5/7 更新，中間 dot 保持不變。
**AprNes 現狀**: 無持久性 address bus。`NotifyMapperA12()` 只在特定 phase 被呼叫，傳入一次性地址。

**需實作**:
- 新增 `static int ppuAddressBus` 欄位
- BG tile fetch: phase 1 設定 NT addr, phase 3 設定 AT addr, phase 5 設定 CHR low addr, phase 7 設定 CHR high addr
- Sprite fetch (257-320): dummy NT phases 設定 NT/AT addr, phase 4 設定 sprite CHR addr, phase 6 設定 CHR+8
- Garbage NT fetch (337/339): 設定 NT addr
- 渲染關閉時: `ppuAddressBus = vram_addr`（TriCNES line 1676）

**參考**: `ref/TriCNES-main/Emulator.cs` lines 3555-3601 (BG), 2841-2970 (sprite), 1674-1678 (disabled)

### P1-2. Per-dot mapper PPUClock() callback
**優先級**: 最高（依賴 P1-1）
**TriCNES**: 每個 PPU dot 結束時呼叫 `PPU_MapperSpecificFunctions()` → `MapperChip.PPUClock()`。Mapper 自行偵測 A12 邊沿。
**AprNes 現狀**: 離散 `NotifyMapperA12()` 呼叫，只在特定 phase 觸發。

**需實作**:
- IMapper 新增 `void PpuClock()` 方法
- 在 ppu_step() 末尾（scanline/dot increment 之前）呼叫 `MapperObj.PpuClock()`
- Mapper004 (MMC3): `PpuClock()` 內檢查 `ppuAddressBus` bit 12 vs `lastA12`，偵測 A12 邊沿
- 移除現有的離散 `NotifyMapperA12()` 呼叫（phase 1, 5, sprite phase 0/4, dot 337）
- CpuClockRise: 改為檢查 `ppuAddressBus` bit 12（而非 lastA12）
- `ppuA12Prev` 在 PpuClock() 末尾更新

**參考**: `ref/TriCNES-main/Emulator.cs` lines 1627-1628, `Mapper_MMC3.cs` lines 293-350

### P1-3. $2001 Sprite Evaluation Alignment-Dependent Delay
**優先級**: 高
**TriCNES**: Sprite evaluation 的 mask delay 依 `CPUClock & 3` 分兩段：
- Phase ≠ 3: mask 延遲 1 PPU cycle（evaluation 前更新）
- Phase 3: mask 延遲 2 PPU cycle（evaluation 後更新）
**AprNes 現狀**: `ppuRenderingEnabled_EvalDelay`（固定 1 dot delay），不分 alignment。

**需實作**:
- 新增 `ShowBG_SprEval`, `ShowSpr_SprEval` delayed flags
- 在 ppu_step() 的 sprite evaluation 區段前後，根據 `mcCpuClock & 3` 決定更新時機
- Sprite evaluation 使用這些 delayed flags 而非 ppuRenderingEnabled

**參考**: `ref/TriCNES-main/Emulator.cs` lines 1652-1673

---

## Phase 2: 渲染管線

### P2-1. Tile Fetch Deferred Commit Model
**優先級**: 中（影響精確的 per-dot 渲染行為）
**TriCNES**: 使用 flag-based deferred commit：
- `PPU_Render_ShiftRegistersAndBitPlanes()`: 設定 `PPU_Commit_*` flags
- `PPU_Render_CommitShiftRegistersAndBitPlanes()`: 在下一個 full step 開頭 commit
- Half-dot: `PPU_Commit_LoadShiftRegisters` flag 控制 shift register reload

**AprNes 現狀**: Tile fetch 在 `ppu_rendering_tick()` 內 inline 執行，無 deferred commit。

**需實作**:
- 新增 commit flags: `commitNTFetch`, `commitATFetch`, `commitCHRLowFetch`, `commitCHRHighFetch`, `commitLoadShiftReg`
- 拆分 tile fetch 為 "address bus + flag" 階段和 "commit + data read" 階段
- Full step: fetch 設 flag → commit 上一個 dot 的 flag → 輸出像素
- Half step: 處理 `commitLoadShiftReg`

**參考**: `ref/TriCNES-main/Emulator.cs` lines 3555-3666

### P2-2. 3-Dot Pixel Output Pipeline
**優先級**: 中
**TriCNES**: 像素顏色有 3-cycle pipeline：
```
PrevPrevPrevDotColor → PrevPrevDotColor → PrevDotColor → DotColor
```
DrawToScreen 使用 PrevPrevPrevDotColor（3 dot delay）。
**AprNes 現狀**: `ppu_half_step()` 直接輸出像素（~0.5 dot delay）。

**需實作**:
- 新增 3-stage color pipeline 變數
- 每個 full step: shift pipeline
- DrawToScreen 使用 3-dot-ago 的顏色
- 影響 sprite 0 hit 偵測的精確 dot 位置

**參考**: `ref/TriCNES-main/Emulator.cs` lines 1724-1727

### P2-3. Sprite Shift Register Counter Setup at Dot 339
**優先級**: 中
**TriCNES**: Dot 339 設定 `PPU_SpriteShifterCounter[i] = PPU_SpriteXposition[i]`，用於下一條 scanline 的 sprite 像素輸出。
**AprNes 現狀**: Sprite rendering 在 dot 257 batch 處理。

**需實作**:
- 新增 per-sprite X counter array
- Dot 339: 從 sprite X position 初始化 counters
- 每個可見 dot: decrement counter，counter==0 時輸出 sprite 像素
- 配合 P2-2 的 per-dot pixel pipeline

**參考**: `ref/TriCNES-main/Emulator.cs` lines 2996-3008, 3718-3743

---

## Phase 3: Register 精確度

### P3-1. $2000 DataBus Glitch (1-cycle open bus)
**優先級**: 低-中
**TriCNES**: $2000 寫入時，某些欄位在第 1 個 PPU cycle 使用 `dataBus`（CPU data bus）而非 `Input` 值。Alignment 0,1 可見此 glitch（2 cycle delay），Alignment 2,3 下一個 cycle 就修正。
**AprNes 現狀**: 所有欄位立即使用正確值。

**需實作**:
- Immediate 階段: NMI enable + pattern table select 使用正確值，其餘使用 cpubus
- Delayed 階段（1-2 PPU cycle 後）: 用 pending value 修正所有欄位
- 主要影響 dot 257 的 nametable bits（TriCNES 註解: "visual bug if write on wrong ppu cycle"）

**參考**: `ref/TriCNES-main/Emulator.cs` lines 9466-9499

### P3-2. $2005 Latch Flip Timing
**優先級**: 中
**TriCNES**: `PPUAddrLatch` 在 **deferred handler 中翻轉**（delay 到期後），而非寫入時立即翻轉。
**AprNes 現狀**: `vram_latch = !vram_latch` 在 `ppu_w_2005()` 和 `ppu_w_2006()` 寫入時**立即翻轉**。

**需實作**:
- 確認 TriCNES 的確切行為（讀取 Emulator.cs lines 9615-9642 + deferred handler 1286-1304）
- 若 latch 確實是 deferred，則改為在 delay 到期時翻轉
- 影響快速連續 $2005/$2006 寫入的行為

**參考**: `ref/TriCNES-main/Emulator.cs` lines 9615-9642, 1286-1304

### P3-3. $2007 State Machine Mystery Write Completion
**優先級**: 低-中
**TriCNES**: $2007 有 9 個 state (0-8, idle=9) + alignment-specific mystery write：
- RMW instruction 對 $2007: state 3/6 時 混合 address high byte + written low byte
- Phase 1-3: 額外 mystery write
- State 8: interrupted read-to-write 的 deferred write + extra increment
**AprNes 現狀**: 有 8+ state SM + interrupted read-to-write，但 mystery write 的 alignment 行為可能不完整。

**需實作**:
- 對照 TriCNES lines 1322-1496 逐 state 驗證
- 補全 alignment-specific mystery write 路徑
- 確認 `PPU_Data_StateMachine_UpdateVRAMAddressEarly` 等 flag 的對應

**參考**: `ref/TriCNES-main/Emulator.cs` lines 1322-1496, 8968-9047, 9675-9719

---

## Phase 4: Edge Cases

### P4-1. OAM Corruption on Mid-Frame Rendering Enable/Disable
**優先級**: 低
**TriCNES**: 完整的 per-alignment OAM corruption model：
- 渲染關閉時: `PPU_OAMCorruptionRenderingDisabledOutOfVBlank` + per-dot/per-alignment index
- 渲染開啟時: `PPU_OAMCorruptionRenderingEnabledOutOfVBlank` + pending corruption
- Multiple alignment cases: CPUClock 0/1/2/3 各有不同行為
**AprNes 現狀**: 基本的 `SetOamCorruptionFlags()` + `ProcessOamCorruption()`，無 per-alignment 精確度。

**需實作**:
- 擴展 corruption model to track CPUClock alignment
- 實作 rendering disable/enable 的 pending corruption 機制
- 加入 palette corruption detection

**參考**: `ref/TriCNES-main/Emulator.cs` lines 9531-9563, 2787-2812, 2832-2838

### P4-2. Palette Corruption on $2006 Transition
**優先級**: 低
**TriCNES**: 當 $2006 deferred copy 從 palette 區域（≥$3F00）切換到非 palette 區域（<$3F00）時，若在 visible dot 且 low nybble ≠ 0，觸發 palette corruption。
**AprNes 現狀**: 未實作。

**需實作**:
- 在 $2006 delay 到期時檢查前一個地址和新地址的 palette 區域
- 只在 scanline < 240 && dot <= 256 觸發
- 修改對應的 palette entry

**參考**: `ref/TriCNES-main/Emulator.cs` lines 1273-1282

### P4-3. $2004 OAMBuffer Half-Cycle Updates
**優先級**: 低
**TriCNES**: `PPU_OAMBuffer` 在 half-step 中根據 dot 區間更新：
- Dot 0 / >320: OAM2[0]
- Dots 1-64: 0xFF
- Dots 65-256: PPU_OAMLatch（evaluation result）
- Dots 257-320: PPU_OAMLatch（fetch result）
**AprNes 現狀**: 在 `ppu_r_2004()` 中根據 ppu_cycles_x 直接計算，非 half-cycle buffer。

**差異**: AprNes 的做法功能等價但時序可能差 0.5 dot。保持現狀或遷移到 buffer model。

### P4-4. Odd Frame Skip Side Effects
**優先級**: 低
**TriCNES**: `SkippedPreRenderDot341` flag 持續到 scanline 0, dot 2，影響 sprite shifter 和 dummy NT fetch。
**AprNes 現狀**: 有 odd frame skip 但無 `SkippedPreRenderDot341` side effects。

**需實作**:
- 新增 skippedPreRenderDot flag
- 追蹤影響的區域（sprite evaluation, dummy NT）

**參考**: `ref/TriCNES-main/Emulator.cs` lines 1629-1643

---

## 實作順序建議

```
P1-1 → P1-2 → [跑 174 + AC 測試]
   ↓
P1-3 → [跑測試]
   ↓
P2-1 → P2-2 → P2-3 → [跑測試]
   ↓
P3-1 → P3-2 → P3-3 → [跑測試]
   ↓
P4-1 → P4-2 → P4-3 → P4-4 → [跑測試]
```

Phase 1 是核心基礎，必須先完成。Phase 2-4 可按需調整順序。每個 Phase 完成後都應跑完整測試確認無回歸。

---

## 已完成的移植項目（參考）

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
- ✅ Per-dot mapper PpuClock() callback (IMapper interface)
- ✅ $2001 Sprite Eval Alignment-Dependent Delay (ShowBG/ShowSpr_EvalDelay, mcCpuClock & 3)
- ✅ Tile Fetch Deferred CXinc (commitCXinc flag, phase 7 → next dot)
- ✅ $2005 Deferred Latch Flip (latch flips at delay expiry, not at write time)

### Deferred (no behavioral impact in current architecture)
- ⏸ 3-Dot Pixel Output Pipeline (visual output timing only)
- ⏸ Sprite Shift Register Counter at Dot 339 (requires per-dot sprite rendering)
- ⏸ $2000 DataBus Glitch (dataBus == In at write time, structural only)
- ⏸ $2007 Mystery Write Alignment (no test ROMs exercise this)
- ⏸ Per-Alignment OAM Corruption (requires variable CPU/PPU alignment)
- ⏸ Palette Corruption on $2006 Transition (alignment 2 only, AprNes fixed alignment 0)
- ⏸ SkippedPreRenderDot341 Side Effects (requires per-dot sprite shift registers)
