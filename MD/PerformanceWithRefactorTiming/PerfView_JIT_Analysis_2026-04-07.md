# PerfView JIT & Inlining Analysis — Phase 2/3/4 + APU Extraction

- **日期**: 2026-04-07
- **Branch**: feature/performance-optimization @ 6c57529
- **Build**: Debug x64, .NET Framework 4.8.1
- **ROM**: ny2011.nes (Mapper 0)
- **Benchmark**: 104.31 FPS best-of-3 (vs baseline 87.19 = **+19.6%**)
- **PerfView run**: 40s, 98.40 FPS (PerfView overhead)
- **ETL**: `temp/aprnes_jit.etl`（CPU Stacks 需用 PerfView GUI 開啟）

---

## 1. CPU Sampling Profile（優化前基準參考）

> 以下 CPU cost 來自 Phase extraction **之前**的 profile (d49f3fb)。
> 如需最新 cost，用 PerfView GUI 開啟 `temp/aprnes_jit.etl` → CPU Stacks → Process: AprNes。

| # | Method | Exc % | Inc % | 說明 |
|---|--------|-------|-------|------|
| 1 | **ppu_step_new** | **53.9%** | 54.9% | PPU 主步進（最大熱點） |
| 2 | **run** | **20.6%** | 96.2% | 主迴圈 dispatch |
| 3 | **apu_step** | **10.2%** | 12.4% | APU 步進 |
| 4 | processLenCtrReloadNonHalf | 1.9% | 1.9% | APU length counter |
| 5 | cpu_step_one_cycle | 1.7% | 10.1% | CPU 指令分派 |
| 6 | CpuRead | 1.1% | 6.0% | 匯流排讀取 |
| 7 | ppu_r_2002 | 0.9% | 4.3% | PPU 狀態暫存器 |
| 8 | DoBranch | 0.6% | 1.1% | CPU 分支指令 |
| 9 | Mapper000.MapperR_RPG | 0.4% | 0.4% | PRG-ROM 讀取 |
| 10 | Op_2C (BIT abs) | 0.3% | 4.8% | 最熱 opcode |

**Top 3 = 84.7%** (PPU 53.9% + run 20.6% + APU 10.2%)

---

## 2. JIT 編譯狀態

### 所有核心方法 — 全部 JIT 成功 ✅

| 子系統 | 方法 |
|--------|------|
| PPU | ppu_step_new, ppu_half_step_new, PpuPhase2_DeferredUpdates, PpuPhase3_Events, PpuPhase4_SpriteEvalAndInit, Process2007StateMachine, PpuBusRead, PpuBusWrite, ComputeSpritePatternAddr |
| APU | apu_step, ApuFrameCounterStep, clockdmc, generateSample, setenvelope, setlength, setsweep, setlinctr, setvolumes, processLenCtrReloadNonHalf |
| CPU | cpu_step_one_cycle, CpuRead, CpuWrite, DoBranch, GetAddress*, Op_XX (80+ opcodes) |
| DMA | DmaFetch, DmaOneCycle, OamDma*, DmcDma* |
| Main | run, MasterClockTick, init |

**0 個 JIT 失敗**（254 started, 248 completed — 差異為 PerfView rundown 時序）

---

## 3. Inlining 分析

### 3.1 成功 Inline 的方法（按 caller 分組）

**ppu_step_new 內：**
| Callee | 說明 |
|--------|------|
| **ppu_half_step_new** | **PPU half step（重要！每 dot 呼叫）** |
| CIRAMAddr, CXinc, CopyHoriV, Yinc | 捲動/地址計算 |
| PpuBusRead | PPU 匯流排讀取 |

**PpuPhase4_SpriteEvalAndInit 內：**
| Callee | 說明 |
|--------|------|
| FlipByte | Sprite 翻轉 LUT |
| SpriteEvalInit / Tick / End | Sprite 評估 FSM |
| PrecomputeOverflow / PreRenderSprites | Sprite 預計算 |
| get/set_evalOamAddr | OAM property |

**MasterClockTick 內：**
| Callee | 說明 |
|--------|------|
| UpdateIRQLine | IRQ 狀態 |
| ProcessControllerShift / Strobe | 手把 |
| DmaOneCycle | DMA 分派 |
| OamDmaGet/Put/Halted | OAM DMA |
| DmcDmaGet/Put/Halted | DMC DMA |

**cpu_step_one_cycle 內：**
| Callee | 說明 |
|--------|------|
| PollInterrupts / PollInterruptsCantDisableIRQ | 中斷輪詢 |
| CompleteOperation / CompleteOperation_NoPoll | 指令完成 |
| SetNZ, SetFlag, GetFlag | 旗標操作 |
| Op_ADC, Op_SBC, Op_AND, Op_ORA, Op_EOR, Op_CMP | ALU 運算 |
| Op_ASL/LSR/ROL/ROR/INC/DEC_mem | RMW 運算 |
| GetImmediate, GetAddressZeroPage, CpuReadZP, CpuReadRMW | 定址模式 |
| StackPush | 堆疊操作 |

**apu_step 內：**
| Callee | 說明 |
|--------|------|
| setvolumes | 音量計算 |
| AudioPlus_PushApuCycle | 音效管線 |
| ProcessControllerShift / Strobe | 手把 |
| authMix_GetVoltage | 混音 |
| cmf_Process | 濾波器 |
| mmix_PushChannels / TryGetStereoSample | 混音管線 |
| ose_PushSample / TryGetSample / Convolve | 過取樣 |
| mfx_ProcessSample | 後處理 |

### 3.2 Inline 失敗 — 按原因分類

**`too many il bytes`（方法體太大）：**
| Method | 說明 |
|--------|------|
| ppu_step_new | PPU 主步進（已拆出 Phase 2/3/4 仍然太大） |
| apu_step | APU 主步進（已拆出 frame counter 仍然太大） |
| cpu_step_one_cycle | CPU 指令分派 |
| Process2007StateMachine | $2007 狀態機 |
| ComputeSpritePatternAddr | Sprite 圖案地址計算 |
| clockdmc | DMC 時鐘 |
| setenvelope / setlength / setsweep | APU envelope/length/sweep |
| RenderScreen | 螢幕渲染 |

**`noinline per IL/cached result`（`[NoInlining]` 標記或 JIT 快取）：**
| Method | 說明 |
|--------|------|
| PpuPhase2_DeferredUpdates | 預期：`[NoInlining]` |
| PpuPhase3_Events | 預期：`[NoInlining]` |
| PpuPhase4_SpriteEvalAndInit | 預期：`[NoInlining]` |
| ApuFrameCounterStep | 預期：`[NoInlining]` |
| clockdmc | 預期：`[NoInlining]` |

**`target not direct`（interface/virtual dispatch）：**
| Caller | Callee | 說明 |
|--------|--------|------|
| ppu_step_new | IMapper.PpuClock() | 每 dot 一次 interface call |
| MasterClockTick | IMapper.CpuClockRise() | 每 CPU cycle |
| MasterClockTick | IMapper.CpuCycle() | 每 CPU cycle |
| PpuBusRead | IMapper.MapperR_CHR() | CHR 讀取 |
| cpu_step_one_cycle | (op handler) | target not direct managed |

**`delegate invoke`（function pointer table）：**
| Method | 說明 |
|--------|------|
| CpuRead → mem_read_fun[] | CPU 記憶體讀取 |
| CpuWrite → mem_write_fun[] | CPU 記憶體寫入 |
| DmaFetch → mem_read_fun[] | DMA 抓取 |
| generateSample → AudioSampleReady | 音效回呼 |
| ap_OutputStereo → (delegate) | 立體聲輸出 |

**`unprofitable inline`（JIT 判斷不值得）：**
| Method | 說明 |
|--------|------|
| NotifyMapperA12 | A12 通知（非所有 mapper 需要） |
| DecodeScanline | Analog 掃描線解碼 |
| ProcessOamCorruption | OAM 損壞處理（極少觸發） |
| processLenCtrReloadNonHalf | length counter reload |
| dmcSetReadBuffer / dmcStopTransfer | DMC 控制 |
| setlinctr | 線性計數器 |
| run | 主迴圈（頂層，沒有 caller 嘗試 inline） |

---

## 4. 架構限制總結

| 限制類型 | 影響 | .NET Framework 4.8 | .NET 8/10 |
|----------|------|-------------------|-----------|
| 方法體太大無法 inline | ppu_step_new, apu_step, cpu_step | 無解 | PGO + OSR 可處理 |
| Interface dispatch | IMapper 所有方法 | 無 devirtualization | Guarded devirtualization |
| Delegate dispatch | mem_read/write_fun, AudioSampleReady | 無法 inline | 仍無法 inline（需改 func ptr） |
| 大方法內部優化不佳 | register allocation, I-Cache | 受限 | RyuJIT 改善但仍受限 |

---

## 5. 優化歷程

| 版本 | 變更 | Best FPS | vs Baseline |
|------|------|----------|-------------|
| #001 | TriCNES port 完成 | 87.19 | — |
| #011 | branchless + dead code | 95.75 | +9.8% |
| #020 | Phase 4 extraction + SWAR sprite shift | 101.86 | +16.8% |
| #021 | + Phase 2+3 extraction | 102.06 | +17.0% |
| **#022** | **+ APU frame counter + clockdmc NoInlining** | **104.31** | **+19.6%** |

---

## 6. 下一步建議

| 優先級 | 方向 | 難度 | 預期 |
|--------|------|------|------|
| 中 | cpu_step_one_cycle 拆分（1.7% Exc, 10.1% Inc） | 中 | 可能解鎖更多 op handler inline |
| 中 | run() 主迴圈微調（20.6% Exc） | 低 | 減少 dispatch overhead |
| 低 | IMapper → sealed class + 泛型特化 | 高 | 消除 interface dispatch |
| 低 | 遷移 .NET 8/10 | 高 | 全面解鎖 PGO/OSR |
