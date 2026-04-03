# 深層邏輯差異 — 修正清單

**日期**：2026-04-03
**分支**：feature/ppu-high-precision
**起始基準**：139/174 (NTSC-only)

架構+參數已完全對齊 TriCNES。以下是**子系統內部邏輯**的剩餘差異。

---

## E. APU Length Counter 模型重構（影響 11 個測試）

### E1. Length Counter Reload 機制
- **AprNes**：$400x 寫入時**立即 reload** length counter
- **TriCNES**：$400x 寫入設 **deferred reload flag**，在 HalfFrame 時才 reload（且僅當 counter==0）
- **影響**：len_reload, len_timing, len_ctr

### E2. Length Counter Decrement 順序
- **AprNes**：簡單 decrement（`if (halt==0 && ctr>0) --ctr`）
- **TriCNES**：**reload-first → decrement**。decrement 時檢查 `!reloadFlag`（防止同 cycle reload+decrement）
- **影響**：len_ctr, len_timing, len_halt

### E3. Halt Flag 更新時機
- **AprNes**：只在 register write 時更新 halt
- **TriCNES**：每次 HalfFrame 從 register 重新讀取 halt
- **影響**：len_halt_timing

### E4. $4015 Read Length Counter Snapshot
- **AprNes**：apu_step 開頭 snapshot，$4015 讀 snapshot（pre-step 值）
- **TriCNES**：直接讀當前值（無 snapshot）
- **影響**：len_timing

### E5. $4015 Write 立即 Zero
- **AprNes**：$4015 write bit=0 → 立即 zero counter
- **TriCNES**：$4015 write bit=0 → 在 HalfFrame 時 zero（deferred）
- **影響**：len_ctr

### E6. Soft Reset $4017 Re-apply
- **AprNes**：soft reset 時如果 ctrmode==5，立即觸發 quarter+half frame
- **TriCNES**：不在 soft reset 重新觸發
- **影響**：reset_timing

### 修正方向
重寫 length counter 為 deferred reload flag 模型：
- 4 個 channel 各自有 `reloadFlag` + `reloadValue`
- HalfFrame 時：先 reload（if counter==0 && flag）→ 再 decrement（if !halt && !reloadFlag）
- $4015 read 移除 snapshot，直接讀 current 值
- $4015 write 不立即 zero，改設 disable flag，HalfFrame 時 zero

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
