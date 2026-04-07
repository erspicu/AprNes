# PerfView CPU Profile 分析報告

**日期**: 2026-04-07  
**分支**: feature/performance-optimization  
**ROM**: ny2011.nes (Mapper 000)  
**配置**: Release x64 / NTSC / Audio Mode 0 / Benchmark 10s  
**工具**: PerfView (ETW CPU Sampling)  
**取樣數**: 10,174 samples

---

## TOP 10 熱點方法（Exc % = 方法自身耗時，不含子呼叫）

| # | 方法 | Exc % | Exc Samples | Inc % | Inc Samples | 備註 |
|---|------|-------|-------------|-------|-------------|------|
| 1 | **ppu_step_new()** | **53.9%** | 5,480 | 54.9% | 5,589 | PPU 全步進，絕對瓶頸 |
| 2 | **run()** | **20.6%** | 2,092 | 96.2% | 9,790 | MasterClockTick 被 inline → 計數器管理開銷 |
| 3 | **apu_step()** | **10.2%** | 1,035 | 12.4% | 1,261 | APU 每 CPU cycle 呼叫 |
| 4 | ?!? (系統/未解析) | 3.8% | 389 | 4.0% | 408 | OS kernel + 未解析符號 |
| 5 | **processLenCtrReloadNonHalf()** | **1.9%** | 196 | 1.9% | 197 | APU length counter |
| 6 | **cpu_step_one_cycle()** | **1.7%** | 172 | 10.1% | 1,030 | CPU 核心步進 |
| 7 | **CpuRead()** | **1.1%** | 112 | 6.0% | 615 | CPU 記憶體讀取 |
| 8 | ntoskrnl (OS kernel) | 1.0% | 102 | 1.3% | 129 | OS 排程/中斷 |
| 9 | **ppu_r_2002()** | **0.9%** | 95 | 4.3% | 440 | PPU $2002 讀取 |
| 10 | **DoBranch()** | **0.6%** | 63 | 1.1% | 112 | CPU 分支指令 |

---

## CPU 時間分佈（圓餅圖概念）

```
PPU 渲染 (ppu_step_new)     ████████████████████████████████████████████████████  53.9%
主迴圈調度 (run/MCT inline) ████████████████████                                 20.6%
APU 音效 (apu_step+helpers) ████████████                                         12.1%
CPU 執行 (cpu_step+opcodes) ████                                                  4.5%
記憶體/IO (CpuRead+IO+2002) ███                                                   3.0%
OS/系統開銷                  █████                                                 5.9%
```

---

## JIT Inline 分析

### 成功 Inline（方法不在 profile 中獨立出現）

| 方法 | 證據 |
|------|------|
| **MasterClockTick()** | 不在列表中，Exc 全計入 `run()` (20.6%) → **完全 inline** |
| **SetNZ()** | 不在列表中 → inline 進 Op_XX |
| **Op_ADC() / Op_SBC()** | 不在列表中 → inline 進呼叫者 |
| **Op_AND() / Op_ORA() / Op_EOR()** | 不在列表中 → inline |
| **PollInterrupts()** | 不在列表中 → inline 進 CompleteOperation |
| **CompleteOperation()** | 不在列表中 → inline |
| **GetImmediate()** | 不在列表中 → inline |
| **ProcessControllerShift()** | 不在列表中 → inline 進 apu_step |
| **ProcessControllerStrobe()** | 不在列表中 → inline 進 apu_step |
| **ppu_half_step_new()** | 不在列表中 → inline 進 run() |

### 未 Inline（獨立出現）

| 方法 | Exc % | 原因推測 |
|------|-------|----------|
| **ppu_step_new()** | 53.9% | 方法體過大，JIT 不 inline |
| **apu_step()** | 10.2% | 方法體過大 |
| **cpu_step_one_cycle()** | 1.7% | 含 function pointer dispatch |
| **CpuRead()** | 1.1% | 含 function pointer table 查表 |
| **ppu_r_2002()** | 0.9% | 被 function pointer table 呼叫 |
| **processLenCtrReloadNonHalf()** | 1.9% | 從 apu_step 呼叫 |
| **DoBranch()** | 0.6% | 含 PollInterrupts + tick 邏輯 |
| **Op_2C() (BIT abs)** | 0.3% | 含 CpuReadRMW 呼叫鏈 |
| **GetAddressAbsolute()** | 0.3% | 通用定址模式 |
| **Mapper000.MapperR_RPG()** | 0.4% | 透過 function pointer 呼叫 |
| **Mapper000.MapperR_CHR()** | 0.3% | 透過 function pointer 呼叫 |

### JIT 編譯開銷

| 項目 | Exc % |
|------|-------|
| clrjit (JIT 編譯器) | 0.6% |
| clr (CLR 執行環境) | 0.4% |

JIT 編譯集中在啟動階段（When 欄位顯示 `11___...`），對穩態效能無影響。

---

## 關鍵發現與優化建議

### 1. ppu_step_new() 是唯一值得優化的瓶頸（53.9%）

佔超過一半 CPU 時間。任何能減少此方法內部工作的改動都有最高 ROI。
- 像素計算（CalculatePixel / sprite overlay）
- tile fetch 和 shift register 操作
- 條件分支（isActiveScanline gate 已做）

### 2. run() 的 20.6% 是 MasterClockTick 調度開銷

MasterClockTick 被完全 inline 進 run()，但計數器管理 (`mcCpuClock--`, `mcPpuClock--`, 比較分支) 本身佔 20.6%。這是架構性開銷，很難進一步壓縮。

### 3. APU 12.1% (apu_step + processLenCtrReloadNonHalf)

第三大消耗。`processLenCtrReloadNonHalf` 單獨佔 1.9%，值得檢視是否可簡化。

### 4. CPU 核心已極度高效（4.5%）

`cpu_step_one_cycle` 僅 1.7%，大部分 Op_XX 方法成功 inline。opcode dispatch 透過 function pointer table 運作正常。

### 5. 所有核心方法都成功 JIT 編譯

沒有出現任何 interpreted 或 Tier-0 方法。`clrjit` 開銷僅在啟動時出現。
