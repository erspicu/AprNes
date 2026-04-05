# PPU TriCNES Complete Port TODO

## 目標
完整翻譯 TriCNES `_EmulatePPU` + `_EmulateHalfPPU` → AprNes `ppu_new.cs`

## Branch: `feature/ppu-tricnes-port`

## 來源: `ref/TriCNES-main/Emulator.cs`

## 前置確認 ✅
- [x] Master clock 模型已一致：MasterClockTick 的 gate order = CPU(0)→NMI(8)→PPU(0)→PPU_half(2)→IRQ(5)→APU(12)
- [x] CpuWrite 直接寫入 register（不經過 tick），CPU 在 PPU 之前執行 → write-before-tick ✅
- [x] 不需要改 MEM.cs，delay 值可直接用 TriCNES 原始值
- [x] IO.cs（ppu_w_2000/2001/2005/2006）的 delay 設定用 mcPpuClock 對應正確
- [x] ppu_r_2002/2004: EmulateUntilEndOfRead（7 master ticks）與 TriCNES 一致
- [x] DMA (OAM/DMC): 由 DmaOneCycle 處理，透過 master clock 與 PPU 互動，不受影響
- [x] APU: mcCpuClock==12，不受 PPU port 影響
- [x] NMI/IRQ: 在 master tick 正確位置（8/5），PPU 只需正確設定 flag

## 影響範圍評估
**需要替換的：**
- `ppu_step()` + `ppu_step_common()` + `ppu_step_rendering()` + `ppu_rendering_tick()` → `ppu_step_new()`
- `ppu_half_step()` → `ppu_half_step_new()`
- 所有子函數（tile fetch, sprite eval, CalculatePixel, etc.）

**不需要改動的：**
- Main.cs MasterClockTick — 不動
- MEM.cs — 不動
- IO.cs — register handlers 保留，可能微調配合新 state 變數
- CPU.cs — 不動
- APU.cs — 不動
- Mapper/*.cs — 不動（PpuClock/CpuClockRise 介面不變）
- ppu_w_2000/2001/2005/2006 — delay 設定保留，countdown 移入 ppu_step_new
- ppu_r_2002/2004/2007 — 保留（含 EmulateUntilEndOfRead）
- $2007 state machine — 搬入 ppu_step_new 對應位置，邏輯不變

---

## Phase 1: 骨架
- [ ] 建立 `AprNes/NesCore/ppu_new.cs` (partial class NesCore)
- [ ] `ppu_step_new()` + `ppu_half_step_new()` 空殼
- [ ] Main.cs 新舊切換開關

## Phase 2: _EmulatePPU 前半 (deferred + scroll + dot++ + wrap)
- [ ] $2006/$2005/$2000 delayed updates (lines 1264-1320)
- [ ] $2007 state machine (lines 1322-1496)
- [ ] Scroll: Yinc/CopyHoriV/ResetY (lines 1498-1516)
- [ ] PPU_Dot++ + wrap (lines 1518-1530)

## Phase 3: Events + mapper + odd frame skip
- [ ] VBL/flag events (lines 1532-1606)
- [ ] VSET latch (lines 1608-1620)
- [ ] PpuClock + A12_Prev (lines 1627-1628)
- [ ] Odd frame skip (lines 1629-1643)

## Phase 4: Eval + $2001 update
- [ ] Eval delay (lines 1652-1673)
- [ ] Sprite eval: PPU_Render_SpriteEvaluation (line 1664)
- [ ] ppuAddressBus = vram_addr (line 1674)
- [ ] $2001/$2001emphasis delayed updates (lines 1681-1722)

## Phase 5: Rendering
- [ ] Pipeline shift (line 1724)
- [ ] CommitShiftRegistersAndBitPlanes — UNGATED (line 1727)
- [ ] Tile fetch (lines 1728-1743)
- [ ] CalculatePixel + UpdateSpriteShift (lines 1745-1751)
- [ ] DrawToScreen (line 1764)

## Phase 6: _EmulateHalfPPU
- [ ] BG shift (line 1818)
- [ ] CommitHalfDot (line 1822)
- [ ] HalfDot tile fetch (lines 1823-1831)
- [ ] VBL/Sprite0 half-step pipeline (lines 1833-1870)

## Phase 7: 子函數
- [ ] ShiftRegistersAndBitPlanes (line 3555)
- [ ] ShiftRegistersAndBitPlanes_HalfDot (line 3604)
- [ ] CommitShiftRegistersAndBitPlanes (line 3625)
- [ ] CommitShiftRegistersAndBitPlanes_HalfDot (line 3659)
- [ ] LoadShiftRegisters (line 3745)
- [ ] UpdateShiftRegisters (line 3710)
- [ ] CalculatePixel (line 3073)
- [ ] UpdateSpriteShiftRegisters (line 3718)
- [ ] SpriteEvaluation (line 2817)
- [ ] GetSpriteAddress (line 3014)

## Phase 8: AprNes 擴展
- [ ] MMC5 ExRAM
- [ ] Analog NTSC
- [ ] Palette cache
- [ ] AccuracyOptA sprite eval
- [ ] Buffer_BG_array

## Phase 9: 測試
- [ ] blargg 174
- [ ] AC 136 (P19 重點)
- [ ] 修回歸 → 合併
