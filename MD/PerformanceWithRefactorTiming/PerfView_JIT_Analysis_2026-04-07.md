# PerfView JIT & CPU Profile Analysis — Phase 2/3/4 Extraction

- **日期**: 2026-04-07
- **Branch**: feature/performance-optimization @ bc7e634
- **Build**: Debug x64, .NET Framework 4.8.1
- **ROM**: ny2011.nes (Mapper 0)
- **Benchmark**: 40s, NTSC, Audio Mode 0, 101.14 FPS (PerfView overhead 下)
- **ETL**: `temp/aprnes_jit.etl`（可用 PerfView GUI 開啟查看 CPU Stacks）

---

## 1. CPU Sampling Profile（優化前基準，d49f3fb）

> CPU cost 資料來自 Phase 2/3/4 extraction **之前**的 PerfView CPU profile。
> extraction 後 `ppu_step_new` 的 cost 會分散到 `PpuPhase2/3/4`。
> 如需最新 CPU cost，開啟 `temp/aprnes_jit.etl` → CPU Stacks → Process: AprNes。

| # | Method | Exc % | Exc | Inc % | Inc |
|---|--------|-------|-----|-------|-----|
| 1 | **ppu_step_new** | **53.9%** | 5,480 | 54.9% | 5,589 |
| 2 | **run** | **20.6%** | 2,092 | 96.2% | 9,790 |
| 3 | **apu_step** | **10.2%** | 1,035 | 12.4% | 1,261 |
| 4 | processLenCtrReloadNonHalf | 1.9% | 196 | 1.9% | 197 |
| 5 | cpu_step_one_cycle | 1.7% | 172 | 10.1% | 1,030 |
| 6 | CpuRead | 1.1% | 112 | 6.0% | 615 |
| 7 | ppu_r_2002 | 0.9% | 95 | 4.3% | 440 |
| 8 | DoBranch | 0.6% | 63 | 1.1% | 112 |
| 9 | Mapper000.MapperR_RPG | 0.4% | 45 | 0.4% | 45 |
| 10 | Op_2C (BIT abs) | 0.3% | 34 | 4.8% | 487 |

**Top 3 合計 84.7%** (ppu_step_new 53.9% + run 20.6% + apu_step 10.2%)

### CPU 呼叫鏈
```
run() [96.2%]
  └─ MasterClockTick() → ppu_step_new [54.9%]
  └─ MasterClockTick() → apu_step [12.4%]
  └─ MasterClockTick() → cpu_step_one_cycle [10.1%]
       └─ CpuRead [6.0%] → ppu_r_2002 [4.3%], Op_2C [4.8%]
```

---

## 2. JIT 編譯狀態

### 2.1 核心方法 JIT 狀態 — 全部成功

| Method | JIT | 說明 |
|--------|-----|------|
| ppu_step_new | ✅ | PPU 主步進（Phase 5 hot path） |
| **PpuPhase2_DeferredUpdates** | ✅ | **新：deferred register updates** |
| **PpuPhase3_Events** | ✅ | **新：VBL/pre-render events** |
| PpuPhase4_SpriteEvalAndInit | ✅ | sprite eval + scanline init |
| ppu_half_step_new | ✅ | PPU half step |
| MasterClockTick | ✅ | 主時鐘 |
| apu_step | ✅ | APU step |
| cpu_step_one_cycle | ✅ | CPU 指令分派 |
| Process2007StateMachine | ✅ | $2007 SM |
| run | ✅ | 主迴圈 |

### 2.2 Inlining 決策 — 失敗

| Caller → Callee | 失敗原因 | 說明 |
|-----------------|----------|------|
| → **ppu_step_new** | **too many il bytes** | 仍然太大，無法 inline |
| → PpuPhase2_DeferredUpdates | noinline (預期) | `[NoInlining]` 生效 |
| → PpuPhase3_Events | noinline (預期) | `[NoInlining]` 生效 |
| → PpuPhase4_SpriteEvalAndInit | noinline (預期) | `[NoInlining]` 生效 |
| → apu_step | too many il bytes | |
| → cpu_step_one_cycle | too many il bytes | |
| → Process2007StateMachine | too many il bytes | |
| → IMapper.PpuClock | target not direct | interface dispatch |
| → IMapper.CpuClockRise | target not direct | interface dispatch |
| → IMapper.MapperR_CHR | target not direct | interface dispatch |
| → CpuRead (mem_read_fun) | delegate invoke | delegate dispatch |
| → DmaFetch (mem_read_fun) | delegate invoke | delegate dispatch |

### 2.3 Inlining 決策 — 成功

**ppu_step_new 內成功 inline：**

| Callee | 說明 |
|--------|------|
| CIRAMAddr | CIRAM 地址計算 |
| CXinc | 水平捲動遞增 |
| CopyHoriV | 水平捲動複製 |
| Yinc | 垂直捲動遞增 |
| PpuBusRead | PPU 匯流排讀取 |
| **ppu_half_step_new** | **PPU half step（新成功！）** |

**PpuPhase4_SpriteEvalAndInit 內成功 inline：**

| Callee | 說明 |
|--------|------|
| FlipByte | Sprite 水平翻轉 (LUT) |
| SpriteEvalInit / Tick / End | Sprite 評估 FSM |
| PrecomputeOverflow | Sprite overflow 預計算 |
| PrecomputePreRenderSprites | Pre-render sprite 預處理 |
| get/set_evalOamAddr | OAM 地址 property |

**MasterClockTick 內成功 inline：**

| Callee | 說明 |
|--------|------|
| UpdateIRQLine | IRQ 狀態更新 |
| ProcessControllerShift / Strobe | 手把處理 |
| DmaOneCycle | DMA 週期 |
| OamDmaGet/Put/Halted | OAM DMA 子操作 |
| DmcDmaGet/Put/Halted | DMC DMA 子操作 |

**apu_step 內成功 inline：**

| Callee | 說明 |
|--------|------|
| setvolumes | 音量設定 |
| AudioPlus_PushApuCycle | 音效推送 |
| authMix_GetVoltage | 混音計算 |
| cmf_Process | 濾波器處理 |
| mmix_PushChannels / TryGetStereoSample | 混音管線 |
| ose_PushSample / TryGetSample / Convolve | 過取樣引擎 |
| mfx_ProcessSample | 後處理效果 |

---

## 3. 關鍵發現

### 3.1 ppu_step_new inline 瓶頸變遷

| 版本 | 失敗原因 | 說明 |
|------|----------|------|
| Phase 4 extraction 前 | too many il bytes | 原始 ~536 行 |
| Phase 4 extraction 後 | **too many locals** | IL 縮減，但 locals 超標 |
| Phase 2+3+4 extraction 後 | **too many il bytes** | locals 解決，IL 又成瓶頸 |

**結論**：ppu_step_new 的 IL size 和 locals 都接近 JIT inline 門檻，解決一個就碰到另一個。在 .NET Framework 4.8 下無法被 inline。

### 3.2 ppu_half_step_new 成功被 inline（新發現！）

Phase 2+3 extraction 後，`ppu_half_step_new` **首次出現在 InliningSucceeded 清單**。
這表示 ppu_step_new 的 IL 縮減讓 JIT 有更多預算去 inline 其子方法。
ppu_half_step_new 每 dot 呼叫一次（~894 萬次/秒），inline 省掉的 call overhead 是有意義的。

### 3.3 DMA 子操作全部 inline 成功

OamDmaGet/Put/Halted 和 DmcDmaGet/Put/Halted 全部成功 inline 進 MasterClockTick。
這在之前的分析中沒有出現，可能是 DmaOneCycle inline 後帶來的連鎖效應。

### 3.4 不可消除的限制

| 限制 | 影響的方法 | 原因 |
|------|-----------|------|
| Interface dispatch | IMapper.PpuClock, CpuClockRise, MapperR_CHR | .NET Framework 無 devirtualization |
| Delegate dispatch | CpuRead, CpuWrite, DmaFetch | function pointer table 用 Func/Action |
| Method too large | apu_step, cpu_step_one_cycle | IL bytes 超標 |

---

## 4. 效能提升建議

| 優先級 | 方向 | 預期效果 |
|--------|------|----------|
| 高 | apu_step 拆分（10.2% CPU） | 縮減 IL，可能解鎖更多 sub-inline |
| 中 | IMapper → struct + 泛型 | 消除 interface vtable overhead |
| 中 | mem_read/write_fun → unsafe function pointer | 消除 delegate overhead |
| 低 | 遷移至 .NET 8/10 | PGO + OSR 解決所有 inline 限制 |

---

## 5. Benchmark 歷程

| 版本 | Best FPS | vs Baseline |
|------|----------|-------------|
| #001 Baseline (TriCNES port) | 87.19 | — |
| #011 (branchless + dead code) | 95.75 | +9.8% |
| #020 (Phase 4 extraction) | 101.86 | +16.8% |
| #021 (+ Phase 2+3 extraction) | **102.06** | **+17.0%** |
