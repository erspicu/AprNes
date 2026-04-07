# 殘留 Timing 模型差異 — 深度分析 TODO

**日期**：2026-04-03
**分支**：feature/ppu-high-precision
**起始基準**：139/174 (NTSC-only, 架構+參數已對齊 TriCNES)

已完成的對齊：MasterClockTick gate 結構、NMI/IRQ 模型、DMA per-cycle、
frame counter count-up、register write delays、counter init、PollInterrupts 位置。

以下是**尚未對齊的 timing 模型**，為剩餘 35 個測試失敗的根因。

---

## A. APU apu_step() 內部流程順序（影響 11 個 APU 測試）

### 差異描述

| 步驟 | AprNes 順序 | TriCNES 順序 |
|------|-------------|-------------|
| 1 | apucycle++ | Controller strobe |
| 2 | Length counter snapshot | **Pulse/Noise timer decrement**（GET cycle only） |
| 3 | **Frame counter reset countdown** | **DMC clock** |
| 4 | **Frame counter increment** | Delayed DMC $4015 |
| 5 | **Quarter/Half frame flags** | **Triangle timer decrement** |
| 6 | **Envelope/Length/Sweep execute** | **Frame counter reset countdown** |
| 7 | Pulse/Noise timer decrement | **Frame counter increment** |
| 8 | Triangle timer decrement | **Quarter/Half frame flags** |
| 9 | DMC clock | **Envelope/Length/Sweep execute** |

**核心差異**：
1. TriCNES **先 decrement timers**，再做 frame counter。AprNes 相反。
2. TriCNES DMC clock 在 frame counter 之前。AprNes 在最後。
3. TriCNES Pulse/Noise 只在 GET cycle (!APU_PutCycle) decrement。AprNes 用 `(apucycle & 1) == 0`。

### 影響測試
- len_ctr, len_table, len_timing_mode0, len_timing_mode1
- len_halt_timing, len_reload_timing
- 4017_timing, 4017_written, reset_timing
- apu_test (umbrella), 08.irq_timing

### 修正方向
重排 apu_step() 內部順序對齊 TriCNES：
```
1. Pulse/Noise timer decrement (GET cycle only)
2. DMC clock
3. Triangle timer decrement
4. Frame counter reset countdown
5. Frame counter increment + switch
6. Quarter/Half frame → Envelope/Length/Sweep
```

---

## B. CPU 指令內部 PollInterrupts 時機（影響 4 個 interrupt 測試）

### 差異描述

| 項目 | AprNes | TriCNES |
|------|--------|---------|
| PollInterrupts 位置 | 只在 CompleteOperation | 每條指令最後一 cycle，CompleteOperation 前 |
| 2-cycle 指令 | cycle 2 結束時 poll | cycle 1 結束時 poll |
| Branch page-cross | 單次 poll（cycle 3） | **雙次 poll**：cycle 1 + cycle 3 (CantDisableIRQ) |
| PollInterrupts_CantDisableIRQ | 無此變體 | 存在：只設 DoIRQ 如果尚未設定（保護已偵測的 IRQ） |

**核心差異**：
- AprNes 的 PollInterrupts 在 CompleteOperation() 內，等效於指令最後一 cycle 結束
- TriCNES 在指令最後 cycle 的 handler 內呼叫，然後才 CompleteOperation
- 對 2-cycle 指令，AprNes poll 時機晚 1 cycle（cycle 2 vs cycle 1）
- Branch page-cross 缺少第一次 poll（cycle 1）和 CantDisableIRQ 保護

### 影響測試
- nmi_and_brk, nmi_and_irq, irq_and_dma
- cpu_interrupts (umbrella), branch_delays_irq

### 修正方向
- 短期：在 CompleteOperation 的 PollInterrupts 前，對 2-cycle 指令修正 timing offset
- 長期：對 branch page-cross 實作雙次 poll + CantDisableIRQ 變體
- 注意：不需要改動所有 256 個 opcode handler，只需處理特殊情況

---

## C. PPU Rendering Pipeline 細節（影響 10+ 個 PPU/NMI 測試）

### C1. VBL Latch 多一層

| 項目 | AprNes | TriCNES |
|------|--------|---------|
| VBL 設定 | pendingVblank → isVblank（1 stage, half-step 直接推進） | PendingVBlank → PPU_VSET → Latch1 → Latch2 → VBlank（多 1 stage） |

**影響**：VBL flag 晚 1 dot 被 CPU 看到。影響 nmi_timing, suppression。

### C2. Sprite 0 Hit 延遲

| 項目 | AprNes | TriCNES |
|------|--------|---------|
| 延遲 | 1 dot（pending → actual in half-step） | **1.5 dot**（Pending → Pending2 → Actual，兩層 latch） |

**影響**：sprite_hit timing 測試（目前通過，但精度不足）。

### C3. $2002 Read Split Timing

| 項目 | AprNes | TriCNES |
|------|--------|---------|
| VBL flag 讀取 | 同步讀所有 flags | VBL 在 read 開頭讀，Sprite flags 在 read 結尾讀（~2 PPU dot 差） |

**影響**：suppression, nmi_off_timing, vbl_clear_time。

### C4. Odd Frame Skip Dot

| 項目 | AprNes | TriCNES |
|------|--------|---------|
| Skip 位置 | dot 339→skip dot 340 | dot 340→skip to dot 0 |

**影響**：even_odd_timing（目前通過）。

### C5. Rendering Mask Delay CPU Phase

| 項目 | AprNes | TriCNES |
|------|--------|---------|
| $2001 delay 模型 | 4-tier，無 CPU clock phase | 3-tier + CPU clock phase `(CPUClock & 3)` 影響更新時機 |

**影響**：sprite_overflow timing。

### 影響測試
- nmi_timing, nmi_on_timing, nmi_off_timing, suppression, nmi_disable
- ppu_vbl_nmi (umbrella)
- sprite_overflow 2/4

### 修正方向（由高到低影響排序）
1. VBL 多一層 latch（C1）— 最大影響，需加 PPU_VSET + Latch2 機制
2. $2002 split timing（C3）— 中度影響，需分離 VBL/Sprite flag 讀取時間
3. Sprite 0 hit 多一層 pending（C2）— 低影響（目前 sprite_hit 全過）
4. Odd frame skip dot（C4）— 低影響（目前通過）
5. Rendering mask CPU phase（C5）— 可能影響 sprite_overflow

---

## D. DMC Reload/Timer 模型差異（影響 5 個 DMA 測試）

### 差異描述

| 項目 | AprNes | TriCNES |
|------|--------|---------|
| Timer decrement 速率 | 每 CPU cycle -1 | 每 GET cycle -2 |
| Timer 值 | 從 dmcperiods[] 載入，原始值 | 從 APU_DMCRateLUT[] 載入，除以 2（因為 -2/GET） |
| Buffer reload trigger | apu_step 內，frame counter 之後 | _EmulateAPU 內，timer fire handler 中 |
| DMA delay parity | GET cycle decrement（注釋: inverted） | PUT cycle decrement（native） |
| Implicit abort check | dmctimer == 8/9 based on parity | APU_ChannelTimer_DMC == 10/8 based on parity |

**核心差異**：AprNes 用 -1/cycle 模型（加倍解析度），TriCNES 用 -2/GET cycle 模型。
AprNes 用 parity-inverted countdown 補償 APU 在 CPU 之前執行的差異，但現在 APU 已移到 CPUClock==12（之後），所以 parity inversion 可能不再正確。

### 影響測試
- dma_2007_read, dma_2007_write, dma_4016_read
- sprdma_and_dmc_dma, sprdma_and_dmc_dma_512

### 修正方向
1. DMC timer 改為 -2/GET cycle 模型（對齊 TriCNES）
2. 移除 parity inversion 補償（APU 已在 CPU 之後執行）
3. 調整 implicit abort timer check 值

---

## 修正優先順序建議

| 優先 | 項目 | 影響測試數 | 複雜度 |
|------|------|-----------|--------|
| 1 | **A. APU 流程重排** | 11 | 中（重排程式碼順序） |
| 2 | **C1. VBL 多層 latch** | 6+ | 中（加 latch 機制） |
| 3 | **D. DMC timer 模型** | 5 | 高（改變 timer 精度模型） |
| 4 | **B. PollInterrupts 時機** | 4 | 低（2-cycle 指令 offset） |
| 5 | **C3. $2002 split timing** | 3 | 低（分離讀取時機） |
| 6 | **C2/C4/C5** | 2-3 | 低 |
