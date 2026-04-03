# 深層邏輯差異 — 修正清單

**日期**：2026-04-03
**分支**：feature/ppu-high-precision
**起始基準**：139/174 (NTSC-only)

架構+參數已完全對齊 TriCNES。以下是**子系統內部邏輯**的剩餘差異。

---

## E. ~~APU Length Counter 模型重構~~ ✅ DONE（139/174 零回歸）

- E1 ✅ deferred reload flag model（lenCtrReloadFlag/Value，register write 設 flag，HalfFrame reload if ctr==0）
- E2 ✅ reload-first → status-zero → decrement (guarded by !reloadFlag)
- E3 ✅ halt 從 apuRegister 每 HalfFrame 重新讀取
- E4 ✅ $4015 read 移除 snapshot，直接讀 current 值
- E5 待驗證：$4015 write 立即 zero 保留（TriCNES 在 HalfFrame zero，但需確認測試影響）
- E6 待驗證：soft reset $4017 re-apply 保留
- 移除：lenctrHalt, lengthClockThisCycle, lengthctr_snapshot
- 新增：lenCtrReloadFlag[4], lenCtrReloadValue[4], apuRegister[16], processLenCtrReloadNonHalf()

---

## F. PPU $2002 Read Deferred Clear（影響 3+ 個測試）

### F1. EmulateUntilEndOfRead
- **AprNes**：$2002 read 是同步操作，VBL 立即清除
- **TriCNES**：$2002 read 觸發 `EmulateUntilEndOfRead()`（跑 7 個 master clock），VBL 在 PPU step 中 deferred 清除
- **影響**：06-suppression, vbl_timing, nmi_suppression

### F2. PPU_Read2002 Flag
- **AprNes**：ppu_r_2002 直接 `isVblank = false`
- **TriCNES**：設 `PPU_Read2002 = true`，在下次 _EmulatePPU 的 full step 中清除 VBlank
- **影響**：VBL clear 延遲 1-2 PPU dot

### 修正方向
- 加入 `ppu2002ReadPending` flag
- ppu_r_2002 設 flag（不直接清 isVblank）
- ppu_step full step 中：if flag → clear isVblank + ppuVSET + flag

---

## G. PPU $2007 State Machine CPU Phase（影響 DMA 測試）

### G1. Phase 判斷基準
- **AprNes**：使用 `(mcPpuClock & 3)` 判斷 buffer late
- **TriCNES**：使用 `(CPUClock & 3)` 判斷
- 兩者應等效（CPU 和 PPU clock 有固定相位關係），但邊界情況可能不同
- **影響**：dma_2007_read, dma_2007_write

---

## 修正優先順序

| 優先 | 項目 | 影響測試數 | 複雜度 |
|------|------|-----------|--------|
| 1 | **E. APU Length Counter 重構** | 11 | 高 |
| 2 | **F. $2002 Deferred Clear** | 3+ | 中 |
| 3 | **G. $2007 Phase 基準** | 2 | 低 |
