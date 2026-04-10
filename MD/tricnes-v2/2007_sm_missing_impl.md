# $2007 State Machine — 缺失實作清單

**Branch**: `feature/tricnes-v2-port`
**基線**: 137/138 AC v2, 184/184 blargg
**目標**: 138/138 ($2007 Stress Test PASS)

---

## 缺失清單

### #1 EmulateUntilEndOfRead（7 master clocks）— 高優先
- **TriCNES**: $2007 read handler 內呼叫 `EmulateUntilEndOfRead()`，推進 7 master clocks（1.75 PPU cycles）
- **AprNes**: ppu_r_2007 直接設 SM=0 返回，不推進 PPU
- **影響**: PD_RB（buffer refill）觸發時機相對 rendering fetch 的偏移
- **參考**: Emulator.cs line 9059, line 750-758
- **狀態**: ⬜ 未實作

### #2 PPU_READ / PPU_ALE 信號 — 中優先
- **TriCNES**: `PPU_READ = PD_RB || (!BLNK && H0_DASH)`，`PPU_ALE = ReadALE || WriteALE || (!BLNK && !H0_DASH)`
- **AprNes**: 完全缺少這兩個信號
- **影響**: SM 和 rendering fetch 的交互控制（偶數/奇數 dot 行為改變）
- **參考**: Emulator.cs line 1782, 1796
- **狀態**: ⬜ 未實作

### #3 v increment 移到 half-step — 高優先
- **TriCNES**: `PPU_v += increment` 在 `PPU_DATA_StateMachine_Half()`（half-step，mid-dot）
- **AprNes**: v increment 在 SM state 4（full dot 開頭，Phase 2 deferred updates）
- **影響**: v increment 時機差 half-dot。TriCNES 註解：「放在 StateMachine() 會破壞 SMB1 標題畫面」
- **參考**: Emulator.cs line 1829-1837
- **狀態**: ⬜ 未實作

### #4 第 2 次 FetchPPU（half-step buffer refill）— 高優先
- **TriCNES**: PD_RB 觸發時做 2 次 FetchPPU：StateMachine2（full dot after rendering）+ StateMachine_Half（half-step）
- **AprNes**: 只做 1 次（deferred refill after tile fetch）
- **影響**: 第 2 次 FetchPPU 用 v increment 後的地址，結果可能覆蓋第 1 次
- **參考**: Emulator.cs line 1840-1848
- **狀態**: ⬜ 未實作

### #5 OctalLatch（8-bit address latch）— 中優先
- **TriCNES**: `FetchPPU()` 用 `(AddressBus & 0x3F00) | OctalLatch` 讀取
- **AprNes**: `PpuBusRead(ppuAddressBus)` 用完整地址
- **影響**: 當 SM ALE 和 rendering ALE 衝突時，低 8 bit 地址來源不同
- **參考**: Emulator.cs line 149-176（FetchPPU），line 8852（OctalLatch 宣告）
- **狀態**: ⬜ 未實作

### #6 Latch odd-index half-dot 推進 — 低優先
- **TriCNES**: `Latches[1] = !Latches[0]`, `Latches[3] = !Latches[2]` 在 half-step
- **AprNes**: integer counter 近似替代，無 latch chain
- **影響**: SR pipeline 內部時序精度，integer counter 大致等效
- **參考**: Emulator.cs line 1849-1854
- **狀態**: ⬜ 未實作

---

## 實作進度

| # | 項目 | 狀態 | commit |
|---|------|------|--------|
| 1 | EmulateUntilEndOfRead | ⬜ | — |
| 2 | PPU_READ / PPU_ALE | ⬜ | — |
| 3 | v increment → half-step | ⬜ | — |
| 4 | 第 2 次 FetchPPU | ⬜ | — |
| 5 | OctalLatch | ⬜ | — |
| 6 | Latch half-dot 推進 | ⬜ | — |

---

## 已完成的部分

- ✅ Even-dot ALE（tile fetch 偶數 dot 設 ppuAddressBus + ppuOctalLatch）
- ✅ Deferred refill（SM state 1/4 buffer refill 延遲到 tile fetch 之後，用 OctalLatch model）
- ✅ Debug log infrastructure（--debug-2007 flag）
- ✅ PPU_READ / PPU_ALE / BLNK 信號計算（#2）
- ✅ OctalLatch field + ALE 更新（#5）
- ✅ Rendering OFF 時 ppuAddressBus = vram_addr（TriCNES line 1532）
- ⚠️ v increment 移到 half-step（#3）— 已實作但造成 20/174 FAIL 回歸
- ⚠️ 第 2 次 FetchPPU in half-step（#4）— 已實作但可能有問題

## 當前狀態（2026-04-10）

**Branch**: feature/tricnes-v2-port
**blargg**: 154/174 PASS（20 FAIL 回歸）
**問題**: v increment 從 SM state 4 移到 half-step 後造成大量回歸
**根因待查**: 需要逐行比對 TriCNES StateMachine_Half 和我們的 half-step 實作
         重點是 v increment 的時機：TriCNES 用 TStep = TStep_Latch || PD_RB 控制
         我們用 flag（ppu2007SM_halfStepVInc）在 sm==4 時設置、half-step 執行
         可能問題：
         1. half-step 執行順序（v inc 在 BG shift 之後）vs TriCNES（在 StateMachine_Half 開頭）
         2. flag 在 updateVramAddrEarly 的 else 分支設置 — 若 updateVramAddrEarly=false 且 !(isRead && bufferLate) 時仍會設 flag
         3. half-step 的第 2 次 refill 用 OctalLatch model 可能地址不對

## 下一步

1. 逐行比對 TriCNES StateMachine_Half（line 1827-1868）vs 我們的 half-step SM
2. 特別檢查 v increment 的條件和時序
3. 確認 halfStepVInc flag 的設置條件是否正確覆蓋 read + write
4. 修正後跑 blargg 確認 174/174，再跑 AC v2 確認 stress test
