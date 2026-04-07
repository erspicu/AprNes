# 深層邏輯差異 — 修正清單

**日期**：2026-04-03
**分支**：feature/ppu-high-precision
**起始基準**：139/174 → **144/174** (E+F+G 完成)

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

## F. ~~PPU $2002 Read Deferred Clear~~ ✅ DONE (+5 recovered with G)
- ppu2002ReadPending flag：ppu_r_2002 設 flag，ppu_step full step 處理
- 144/174

## G. ~~PPU $2007 State Machine CPU Phase~~ ✅ DONE
- buffer-late：(mcPpuClock & 3) → (mcCpuClock & 3)
- 144/174

## 修正完成

| 項目 | 結果 |
|------|------|
| E. APU Length Counter | ✅ 零回歸（架構對齊） |
| F. $2002 Deferred Clear | ✅ +5 recovered |
| G. $2007 CPU Phase | ✅ (與 F 一起) |
