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

## 當前狀態（2026-04-11）

**Branch**: feature/tricnes-v2-port
**blargg**: ~162/174 PASS（12 unique FAIL, 2 pre-existing MMC3）
**AC v2 P19**: 5/7 PASS（BG Serial In FAIL, SprOnSL0 FAIL 3, $2007 Stress FAIL 1）

## 已完成的 SR latch 3-phase 移植

- ✅ PPU_DATA_StateMachine() — 完整 SR latch pipeline, signals
- ✅ PPU_DATA_StateMachine2() — PD_RB → FetchPPU (OctalLatch model)
- ✅ PPU_DATA_StateMachine_Half() — TStep, v inc, 2nd FetchPPU, latch advance, write
- ✅ ppu_r_2007 / ppu_w_2007 — 簡化對齊 TriCNES（SR latch trigger only）
- ✅ 7MC EmulateUntilEndOfRead in handlers
- ✅ SM 呼叫位置（after dot++, events, mapper）
- ✅ H0_DASH polarity（odd cx = READ）
- ✅ FetchPPU bus side effect（all fetch points）
- ✅ OctalLatch guards（BG fetch + DummyNT + sprite fetch）
- ✅ Odd-dot READ 用 OctalLatch model
- ✅ Tile fetch range cx>=1 + DummyNT

## 10 個新回歸待查（非 pre-existing）

| 測試 | 類型 | 可能原因 |
|------|------|---------|
| ppu_read_buffer | $2007 buffer | SR latch timing vs integer SM |
| 4× dmc_dma_during_read4 | DMA+$2007 | 7MC 推進影響 DMA timing |
| vram_access | PPU VRAM | write handler 簡化移除了 consecutive detection |
| cpu_dummy_writes_ppumem | CPU+PPU | 同上 |
| cpu_exec_space_ppuio | CPU+PPU | 同上 |
| sprite_hit 05/09 | Sprite timing | tile fetch range 或 DummyNT 改動 |

## 下一步

繼續逐區域比對，重點：
1. CalculatePixel range: AprNes cx<=257 vs TriCNES PPU_Dot<=256
2. Sprite fetch CHR: AprNes 直接用 addr vs TriCNES OctalLatch model
3. Write handler: TriCNES 的 consecutive access 由 SR pipeline 自然處理，需確認 SR pipeline 正確覆蓋
4. DMA + $2007 交互：7MC 推進期間 DMA 是否正確暫停
