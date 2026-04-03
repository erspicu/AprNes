# TriCNES Timing Model — 最終審計報告

**日期**：2026-04-03
**分支**：feature/ppu-high-precision
**基準**：139/174 (NTSC-only)

---

## 審計結果：全部對齊（1 項待修正）

### 完全一致 ✅

| 項目 | 狀態 |
|------|------|
| MasterClockTick gate 順序：CPU(0)→NMI(8)→PPU(0)→PPU_half(2)→IRQ(5)→APU(12) | ✅ |
| Counter init：mcCpuClock=0, mcPpuClock=0, cpuCycleCount=0, mcApuPutCycle=false | ✅ |
| NMI model：NMILine \|= at CPUClock==8, edge detect in CompleteOperation | ✅ |
| IRQ model：IRQLine latch at CPUClock==5, re-assertion, level detect in CompleteOperation | ✅ |
| DMA per-cycle dispatch：OAM halt/aligned/get/put, DMC halt+get, CPU_Read gate | ✅ |
| BRK handler：cycle 1-6 + PollInterrupts at cycle 4 | ✅ |
| APU frame counter：count-up model, 7457/14913/22371/29828-30/37281-82 | ✅ |
| APU $4017 write：deferred reset (mcApuPutCycle ? 3 : 4) | ✅ |
| APU $4015 read：immediate statusframeint clear | ✅ |
| $2000 write delay：phase 0,1=2; phase 2,3=1 | ✅ |
| $2005 write delay：phase 2=2; others=1 | ✅ |
| $2006 write delay：phase 2=5; others=4 | ✅ |
| $2001 emphasis delay：phase 0,3=2; phase 1,2=1 | ✅ |
| PollInterrupts in CompleteOperation（opcode!=0x00 時） | ✅ |

### 待修正 ✗

| 項目 | AprNes | TriCNES | 修正 |
|------|--------|---------|------|
| **$2001 write delay** | always 2 | phase 0,1,3=2; **phase 2=3** | 恢復 `((mcPpuClock & 3) == 2) ? 3 : 2` |

**原因**：之前的參數對齊時，錯誤地將 $2001 delay 從 `(phase2)?3:2` 改為 `always 2`。
TriCNES 實際上在 phase 2 使用 3 cycle delay。需要恢復原始值。

---

## 殘留測試失敗分析（35 個）

### APU 相關（11 個）— frame counter 架構轉換造成
- len_ctr, len_table, len_timing_mode0/mode1, len_halt_timing, len_reload_timing
- 4017_timing, 4017_written, reset_timing
- apu_test (umbrella)

**原因**：count-up 模型的 $4017 deferred reset 改變了 length counter clock timing。
需要微調 $4017 write handler 中的 5-step immediate clock 和 reset countdown 邏輯。

### NMI/PPU 相關（10 個）— $2000 delay 和 NMI 模型造成
- nmi_timing, nmi_on_timing, nmi_off_timing, suppression, nmi_disable
- ppu_vbl_nmi (umbrella)

**原因**：$2000 全延遲模型改變了 NMI enable/disable 的精確時機。

### DMA 相關（5 個）— per-cycle dispatch 精度
- dma_2007_read, dma_2007_write, dma_4016_read, sprdma_and_dmc_dma ×2

### IRQ/Interrupt 相關（4 個）
- nmi_and_brk, nmi_and_irq, irq_and_dma
- cpu_interrupts (umbrella)

### 其他（5 個）
- mmc3_test/4-scanline_timing ×2
- sprite_overflow 2/4
- read_joy3/thorough_test
