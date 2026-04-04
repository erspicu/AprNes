# PPU TriCNES 完整移植 TODO

**分支**: `feature/ppu-high-precision`
**目標**: 將 TriCNES 的 PPU 子系統完整移植到 AprNes，達成架構對齊
**建立日期**: 2026-04-04
**最後更新**: 2026-04-04

---

## 總覽

TriCNES 是目前唯一 AccuracyCoin 136/136 滿分的參考實作。AprNes 已移植其 master clock gate order、VBL/NMI pipeline、register delays、sprite evaluation FSM 等核心機制，並完成所有渲染管線與 edge case 的結構性移植。

**進度統計**: 30 項 ✅ 完成 / 0 項 ⏸ 暫緩 — **全部完成**
**DMA 重構**: ✅ 全部完成（D1-D7 + gate/abort/handler 全面驗證）
**測試基線** (分支): blargg 171/174, AC 127/136（實驗分支允許回歸）

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

## Phase 2: 渲染管線 — ✅ 全部完成

### ✅ P2-1. Tile Fetch Deferred CXinc
**優先級**: 中
**TriCNES**: 使用 flag-based deferred commit，CXinc 在 phase 0（下一個 tile）執行而非 phase 7（同一 tile）。
**已實作**: `commitCXinc` flag — phase 7 設 flag，下一 dot 開頭執行 CXinc()。完整 deferred commit（NT/AT/CHR fetch）與 pixel pipeline 耦合，留待 P2-2。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 3555-3666

### ✅ P2-2. 3-Dot Pixel Output Pipeline
**優先級**: 中
**TriCNES**: 像素顏色有 3-cycle pipeline：
```
PrevPrevPrevDotColor → PrevPrevDotColor → PrevDotColor → DotColor
```
DrawToScreen 使用 PrevPrevPrevDotColor（3 dot delay）。
**已實作**: `dotColor/prevDotColor/prevPrevDotColor/prevPrevPrevDotColor` 四級 pipeline，ppu_half_step 開頭 shift pipeline，結尾寫 `prevPrevPrevDotColor` 到 ScreenBuf1x。同時 palIdx pipeline 供 Analog mode 使用。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 1724-1727

### ✅ P2-3. Per-Dot Sprite Shift Register Rendering
**優先級**: 中
**TriCNES**: Sprite tiles 在 dots 257-320 取得，X counters 在 dot 339 設定，dots 1-256 逐 dot 輸出 sprite pixels。
**已實作**: `sprShiftL/H[8]`, `sprXCounter[8]`, `sprFetchAttr[8]`, `sprXPos[8]` shift register arrays。Dots 257-320 從 secondary OAM fetch tiles（含 FlipByte 水平翻轉），dot 339 初始化 X counters，dots 1-256 在 ppu_half_step 中逐 dot shift + composite。替代舊的 dot 257 batch `RenderSpritesLine_Batch()`。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 2996-3008, 3718-3743

---

## Phase 3: Register 精確度 — ✅ 全部完成

### ✅ P3-1. $2000 DataBus Glitch (1-cycle open bus)
**優先級**: 低-中 → **已實作**
**TriCNES**: $2000 寫入時，某些欄位在第 1 個 PPU cycle 使用 `dataBus`（CPU data bus）而非 `Input` 值。Alignment 0,1 可見此 glitch（2 cycle delay），Alignment 2,3 下一個 cycle 就修正。
**已實作**: CpuWrite() 中 `cpubus = val` 移至 write handler 之後（handler 執行期間 cpubus 保持上次 READ 值）。ppu_w_2000 中 glitch-affected fields（bits 0-1 NT, bit 2 increment, bit 5 sprite size）使用 `cpubus`，非 glitch fields（NMI, pattern table addr）使用 `value`。Delayed handler 以正確值修正。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 9466-9499

### ✅ P3-2. $2005 Latch Flip Timing
**優先級**: 中
**TriCNES**: `PPUAddrLatch` 在 **deferred handler 中翻轉**（delay 到期後），而非寫入時立即翻轉。$2006 則是寫入時立即翻轉。
**已實作**: ppu_w_2005() 不再翻轉 vram_latch，改為在 delay handler 到期時檢查 latch 狀態並翻轉。$2006 保持寫入時立即翻轉（與 TriCNES 一致）。影響快速連續 $2005 寫入的行為。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 9615-9642, 1286-1304

### ✅ P3-3. $2007 State Machine Mystery Write Completion
**優先級**: 低-中 → **已實作**
**TriCNES**: $2007 有 9 個 state (0-8, idle=9) + alignment-specific mystery write：
- RMW instruction 對 $2007: state 3/6 時 混合 address high byte + written low byte
- Phase 1-3: 額外 mystery write at post-increment address
- State 8: interrupted read-to-write 的 deferred write + extra increment
**已實作**: 完整 TriCNES SM model — 5 個新 flags (`performMysteryWrite`, `normalWriteBehavior`, `updateVramAddrEarly`, `readDelayed`, `mysteryAddr`)。State 3: NormalWriteBehavior guard + mystery $YYZZ rewrite。State 4: UpdateVRAMAddressEarly double increment + alignment-gated mystery write (mcCpuClock & 3 != 0)。State 8: alignment gate。$2007 read: consecutive read (SM==3 && isRead) with per-phase behavior。$2007 write: SM==3||6 consecutive detection + NormalWriteBehavior flag。SM tick 抽出 `Ppu2007SmTick()` 共享方法（消除 half-step/full-step 重複）。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 1322-1496, 8968-9047, 9675-9719

---

## Phase 4: Edge Cases — ✅ 全部完成

### ✅ P4-1. OAM Corruption Per-Alignment Suppression
**優先級**: 低 → **已實作**（alignment gate 結構就位，alignment 0 下行為與舊版等價）
**TriCNES**: 完整的 per-alignment OAM corruption model，CPUClock 0/1/2/3 各有不同行為。Re-enable 時 alignment 1,2 抑制 corruption。
**已實作**: 保留原有 `SetOamCorruptionFlags()` + `ProcessOamCorruption()` bitmap 模型，新增 `oamCorruptPending` / `oamCorruptSuppressed` flags。Re-enable 時檢查 `mcCpuClock & 3`：alignment 1,2 清除 flags 不執行 corruption，alignment 0,3 正常執行。AprNes 固定 alignment 0 → 永遠正常執行（gate 不觸發）。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 9531-9563, 2787-2812, 2832-2838

### ✅ P4-2. Palette Corruption on $2006 Transition + Rendering Disable
**優先級**: 低 → **已實作**（alignment 2 gate + flag detection 就位，alignment 0 下永不觸發）
**TriCNES**: 兩種 palette corruption trigger：
1. $2006 deferred copy 從 palette 區域（≥$3F00）切換到非 palette 區域（<$3F00）
2. Rendering disable 時 dot 在 NT fetch 前 2 dot + VRAM addr ≥ $3C00
**已實作**: `paletteCorruptFromVAddr` flag 在 $2006 delay handler 中 detect palette→non-palette transition。`paletteCorruptFromDisable` flag 在 $2001 disable path 中 detect NT fetch timing + palette addr。Both 在 ppu_half_step pixel output 中消費，但只在 `(mcCpuClock & 3) == 2` 時觸發（alignment 2 only）。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 1273-1282, 9538-9545, 3207-3217

### ✅ P4-3. $2004 OAMBuffer Half-Cycle Updates
**優先級**: 低 → **已實作**
**TriCNES**: `PPU_OAMBuffer` 在 half-step 中根據 dot 區間更新（dot 0/321+: secondaryOAM[0], dot 1-64: 0xFF, dot 65-320: PPU_OAMLatch）。$2004 read 返回 cached buffer。
**已實作**: `ppuOamBuffer` 欄位，在 ppu_step() VSET latch 後更新（per-dot, visible scanlines only）。ppu_r_2004() 在 rendering 期間直接返回 `ppuOamBuffer`（移除 on-the-fly computation）。

### ✅ P4-4. Odd Frame Skip Side Effects (SkippedPreRenderDot341)
**優先級**: 低 → **已實作**（flag + clear logic 就位）
**TriCNES**: `SkippedPreRenderDot341` flag 在 odd frame skip 時設定，持續到 scanline 0 dot 2，影響 sprite shifter 和 dummy NT fetch。
**已實作**: `skippedPreRenderDot341` static bool，odd frame skip 時設 true，scanline 0 dot 2 清除。Sprite shifter / dummy NT 的 side effects 依賴 per-dot sprite rendering (P2-3)，已可配合使用。

**參考**: `ref/TriCNES-main/Emulator.cs` lines 1629-1643

---

## DMA 子系統重構 — 進行中

### ✅ D1. dataPinsNotFloating Bus Tracking
**TriCNES**: 追蹤 data bus 是否被主動驅動（RAM/ROM/PPU regs 驅動，其他 floating）。
**已實作**: `dataPinsNotFloating` 欄位，DmaFetch 中根據地址範圍設定。

### ✅ D2. OAM DMA $4016/$4017 Read Skip
**TriCNES**: OAM DMA 讀取 $4016/$4017 時跳過 shift side effect，返回 cpubus。
**已實作**: DmaFetch 中 `if (spriteDmaTransfer && (addr == 0x4016 || addr == 0x4017)) return cpubus;`

### ✅ D3. DMC Timer Model — bitsRemaining==0 Unified Handler
**TriCNES**: DMA trigger + implicit abort promotion + shifter load 全部在 bitsRemaining==0 handler 內。
**已實作**: 移除獨立 reload block，移除 dmcBufferEmpty flag（TriCNES 不追蹤），shifter 永遠從 buffer 載入。

### ✅ D4. DMC Load DMA ($4015) — Silent Guard + Shifter Load
**TriCNES**: $4015 啟用 DMC 時只在 `APU_Silent` 時設定 DMCDMADelay。Delay 到期時載入 shifter + 清除 silence。
**已實作**: `if (dmcsilence) { dmcLoadDmaCountdown = 2; }`，countdown fire 時 `dmcshiftregister = dmcbuffer; dmcsilence = false;`

### ✅ D5. Controller Shift Register Model
**TriCNES**: 8-bit parallel-to-serial shift register，MSB 先讀，2-cycle deferred shift（counter 在 APU step 遞減）。
**已實作**: 完全重寫 JoyPad.cs — P1/P2_Port (button state) → P1/P2_ShiftRegister (shift regs) → P1/P2_ShiftCounter (defer)。ProcessControllerShift() 在 apu_step 頂部，ProcessControllerStrobe() 在 GET cycle。

### ✅ D6. $2002/$2004 EmulateUntilEndOfRead
**TriCNES**: 讀 $2002 時 VBL flag 在 read start 取樣，然後推進 7 master clocks (~1.75 PPU dots)，sprite flags 在 read end 取樣。$2004 也推進 7 master clocks 再讀 OAM。
**已實作**: ppu_r_2002/ppu_r_2004 中 `for (int i = 0; i < 7; i++) MasterClockTick();`

### ✅ D7. Deferred Frame Interrupt Clear ($4015 Read)
**TriCNES**: $4015 讀取設 `Clearing_APU_FrameInterrupt = true`，在下一個 PUT cycle 才清除 `APU_Status_FrameInterrupt` + `IRQ_LevelDetector`。
**已實作**: `clearingFrameInterrupt` flag，在 apu_step() PUT cycle 處理。

### DMA 移植完成
- DMA Gate Condition: 已驗證完全對齊（cpuIsRead ↔ CPU_Read, CompleteOperation 重置）
- Implicit Abort: 已驗證完全對齊（DmaOneCycle 內 + MasterClockTick 後雙重清除）
- $4014/$4015 Write Handler: 已驗證完全對齊
- 所有 TriCNES DMA state variables 均有 AprNes 對應（12/12 映射完成）

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
- ✅ 3-Dot Pixel Output Pipeline (dotColor → prev → prevPrev → prevPrevPrev → screen)
- ✅ Per-Dot Sprite Shift Register Rendering (sprShiftL/H, sprXCounter, dot 339 init)
- ✅ OAM Corruption Per-Alignment Suppression (alignment 1,2 suppress on re-enable)
- ✅ Palette Corruption Detection ($2006 transition + rendering disable, alignment 2 gate)
- ✅ Palette Corruption from Rendering Disable (NT fetch timing + VRAM addr ≥ $3C00)
- ✅ SkippedPreRenderDot341 Flag (odd frame skip, clear at scanline 0 dot 2)
- ✅ dataPinsNotFloating Bus Tracking (DMA bus driven state)
- ✅ OAM DMA $4016/$4017 Read Skip (prevent shift side effects)
- ✅ DMC Timer Model — bitsRemaining==0 Unified Handler (remove dmcBufferEmpty)
- ✅ DMC Load DMA Silent Guard + Shifter Load ($4015 path)
- ✅ Controller Shift Register Model (8-bit shift + 2-cycle defer + strobe)
- ✅ $2002/$2004 EmulateUntilEndOfRead (7 master clock mid-read PPU advance)
- ✅ Deferred Frame Interrupt Clear ($4015 read → next PUT cycle)
- ✅ $2000 DataBus Glitch (cpubus for glitch-affected fields, value for non-glitch)
- ✅ $2007 Mystery Write Complete (NormalWriteBehavior + mystery $YYZZ + alignment gate + consecutive reads)
- ✅ $2004 OAMBuffer Half-Cycle (ppuOamBuffer per-dot update, cached read)
