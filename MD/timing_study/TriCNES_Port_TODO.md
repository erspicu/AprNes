# TriCNES Timing Model 移植 — 子系統 TODO

**日期**：2026-04-03
**分支**：feature/ppu-high-precision
**當前**：161/174
**目標**：174/174（扣除 5 個 TriCNES 已知錯誤後 = 169/174）

---

## 移植狀態總覽

### 已完成移植 ✅
- [x] MasterClockTick gate 結構與順序
- [x] NMI model（NMILine level + edge detect at PollInterrupts）
- [x] IRQ model（IRQLine latch at CPUClock==5 + level detect）
- [x] DMA per-cycle dispatch（OAM halt/aligned/get/put, DMC halt+get, CPU_Read gate）
- [x] APU frame counter（count-up model, step positions, deferred $4017 reset）
- [x] APU apu_step 內部順序（GET/PUT split, timers before frame counter）
- [x] PPU register write delays（$2000/$2001/$2005/$2006 phase-dependent）
- [x] $2000 write（immediate + delayed re-application）
- [x] $2002 read（deferred VBL clear via ppu2002ReadPending）
- [x] $2002 split timing（VBL at start, sprite flags delayed）
- [x] VBL latch pipeline（ppuVSET → Latch1/Latch2）
- [x] Sprite0 hit pipeline（1.5 dot delay, pending2）
- [x] PollInterrupts in CompleteOperation（end of instruction）
- [x] Branch PollInterrupts（cycle 1 poll + CantDisableIRQ）
- [x] BRK cycle 4 full PollInterrupts
- [x] DMC timer -2/GET cycle model
- [x] Length counter deferred reload flag model
- [x] Counter init（mcCpuClock=0, mcPpuClock=0, cpuCycleCount=0）

### 待移植 — 子系統邏輯重寫

#### H. Sprite Overflow 即時評估（影響 2 個測試）
- **現況**：dot 0 預算（PrecomputeOverflow），結果在指定 cycle 一次套用
- **TriCNES**：dots 65-256 **逐 dot 即時評估** FSM，動態響應 rendering enable 變化
- **影響**：sprite_overflow_tests/2.Details, 4.Obscure
- **修正**：重寫 sprite overflow 為 per-dot 即時 FSM（參考 TriCNES sprite evaluation）
- **檔案**：PPU.cs PrecomputeOverflow → per-dot evaluation

#### I. MMC3 A12 Notification Timing（影響 2 個測試）
- **現況**：phase-based batch notification 在 ppu_rendering_tick 內
- **TriCNES**：per-dot A12 edge detection，精確到每個 tile fetch phase
- **影響**：mmc3_test/4-scanline_timing ×2
- **修正**：A12 notification 改為 per-dot，在正確的 fetch phase 觸發
- **檔案**：PPU.cs ppu_rendering_tick A12 section + Mapper004.cs

#### J. DMA $2007/$4016 Interaction（影響 5 個測試）
- **現況**：DMA dummy read 設 ppu2007SM=9（bypass），但 $2007 state machine 與 DMA interleave 不精確
- **TriCNES**：DMA cycle 的 bus access 會影響 $2007 state machine 的進行
- **影響**：dma_2007_read, dma_2007_write, dma_4016_read, sprdma_and_dmc_dma ×2
- **修正**：DMA dummy read 的 bus behavior 對齊 TriCNES
- **檔案**：MEM.cs DmaOneCycle + PPU.cs $2007 SM

#### K. BRK/NMI/IRQ 交互（影響 2 個測試）
- **現況**：BRK cycle 4 edge detection + nmi_just_deferred
- **TriCNES**：PollInterrupts 在 BRK cycle 4 用 nmi_pending level check，且有完整的 NMI hijack 邏輯
- **影響**：3-nmi_and_irq, 4-irq_and_dma
- **修正**：BRK cycle 4 的 NMI 偵測改為 level-based（如 master 的 nmi_pending 模型）
- **檔案**：CPU.cs Op_00

#### L. APU Reset Sequence（影響 3 個測試）
- **現況**：power-on pre-advance=9，soft reset deferred-only
- **TriCNES**：CPU reset sequence 通過主迴圈執行（7 cycle BRK-like），APU 自然推進
- **影響**：4017_timing, 4017_written, len_halt_timing
- **修正**：reset sequence 改為通過 MasterClockTick 執行（而非 init 直接讀 reset vector）
- **檔案**：Main.cs init(), CPU.cs reset sequence

---

## 修正優先順序

| 優先 | 項目 | 測試數 | 複雜度 | 說明 |
|------|------|--------|--------|------|
| 1 | **J. DMA interaction** | 5 | 中 | 影響最多測試 |
| 2 | **L. APU reset sequence** | 3 | 高 | 需改 init 架構 |
| 3 | **H. Sprite overflow** | 2 | 高 | 預算→即時 FSM |
| 4 | **I. MMC3 A12** | 2 | 中 | A12 per-dot |
| 5 | **K. BRK/NMI/IRQ** | 2 | 中 | level vs edge |

## TriCNES 已知錯誤（不移植）
收斂到 169/174 後，以下 5 個用 NESdev wiki 處理：
- 6-MMC3_alt, 6-MMC6, 5-MMC3_rev_A（MMC3/MMC6 變體行為）
- read_write_2007（$2007 邊界行為）
- power_up_palette（開機調色盤狀態）
