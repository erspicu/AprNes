# PerfView JIT & CPU Profile Analysis — Phase 4 Extraction 後

- **日期**: 2026-04-07
- **Branch**: feature/performance-optimization @ d49f3fb (+ uncommitted Phase 4 extraction)
- **Build**: Debug x64, .NET Framework 4.8.1
- **ROM**: ny2011.nes (Mapper 0)
- **Benchmark**: 40s, NTSC, Audio Mode 0, 98.85 FPS (PerfView overhead 下的結果)

---

## 1. CPU Sampling Profile（優化前基準，d49f3fb）

> 以下資料來自 Phase 4 extraction **之前**的 PerfView CPU profile（`temp/result.txt`）。
> Phase 4 extraction 後，`ppu_step_new` 的 cost 預期會分散到 `PpuPhase4_SpriteEvalAndInit`。

| # | Method | Exc % | Exc Samples | Inc % | Inc Samples | 備註 |
|---|--------|-------|-------------|-------|-------------|------|
| 1 | **ppu_step_new** | **53.9%** | 5,480 | 54.9% | 5,589 | PPU 主步進（最大熱點） |
| 2 | **run** | **20.6%** | 2,092 | 96.2% | 9,790 | 主迴圈 dispatch |
| 3 | **apu_step** | **10.2%** | 1,035 | 12.4% | 1,261 | APU 步進 |
| 4 | processLenCtrReloadNonHalf | 1.9% | 196 | 1.9% | 197 | APU length counter |
| 5 | cpu_step_one_cycle | 1.7% | 172 | 10.1% | 1,030 | CPU 指令執行 |
| 6 | CpuRead | 1.1% | 112 | 6.0% | 615 | CPU 匯流排讀取 |
| 7 | ppu_r_2002 | 0.9% | 95 | 4.3% | 440 | PPU 狀態暫存器讀取 |
| 8 | DoBranch | 0.6% | 63 | 1.1% | 112 | CPU 分支指令 |
| 9 | Mapper000.MapperR_RPG | 0.4% | 45 | 0.4% | 45 | PRG-ROM 讀取 |
| 10 | Op_2C (BIT abs) | 0.3% | 34 | 4.8% | 487 | 最熱 opcode |

**Top 3 合計佔 84.7%** (ppu_step_new 53.9% + run 20.6% + apu_step 10.2%)

### CPU 呼叫鏈（Inc% 分析）
```
run() [96.2%]
  └─ MasterClockTick() → ppu_step_new [54.9%]
  └─ MasterClockTick() → apu_step [12.4%]
  └─ MasterClockTick() → cpu_step_one_cycle [10.1%]
       └─ CpuRead [6.0%] → ppu_r_2002 [4.3%], Op_2C [4.8%]
```

---

## 2. JIT 編譯狀態（Phase 4 extraction 後）

> 以下資料來自 `temp/aprnes_jit.etl`，已包含 Phase 4 extraction + SWAR sprite shift 修改。

### 2.1 核心方法 JIT 狀態

| Method | JIT 編譯 | Native Size (hex) | 備註 |
|--------|---------|-------------------|------|
| ppu_step_new | ✅ Jitted | — | 主 PPU step（已縮減） |
| **PpuPhase4_SpriteEvalAndInit** | ✅ Jitted | — | **新增：抽離的 sprite eval** |
| ppu_half_step_new | ✅ Jitted | — | PPU half step |
| MasterClockTick | ✅ Jitted | — | 主時鐘 |
| apu_step | ✅ Jitted | — | APU step |
| cpu_step_one_cycle | ✅ Jitted | — | CPU 指令分派 |
| Process2007StateMachine | ✅ Jitted | — | $2007 SM |
| run | ✅ Jitted | — | 主迴圈 |
| CpuRead | ✅ Jitted | — | 匯流排讀取 |
| CpuWrite | ✅ Jitted | — | 匯流排寫入 |
| DmaFetch | ✅ Jitted | — | DMA 抓取 |
| DmaOneCycle | ✅ Jitted | — | DMA 週期 |
| PpuBusRead | ✅ Jitted | — | PPU 匯流排讀取 |
| PpuBusWrite | ✅ Jitted | — | PPU 匯流排寫入 |

**所有核心方法均已成功 JIT 編譯。**

### 2.2 Inlining 決策（核心熱區）

| Caller | Callee | 結果 | 失敗原因 |
|--------|--------|------|----------|
| run → | ppu_step_new | ❌ Failed | **too many locals** |
| run → | apu_step | ❌ Failed | too many il bytes |
| run → | cpu_step_one_cycle | ❌ Failed | too many il bytes |
| ppu_step_new → | PpuPhase4_SpriteEvalAndInit | ❌ Failed (預期) | **noinline per IL/cached result** (`[NoInlining]`) |
| ppu_step_new → | IMapper.PpuClock | ❌ Failed | target not direct (interface call) |
| ppu_step_new → | Mapper005.NotifyVramRead | ❌ Failed | too many il bytes (cascaded) |
| MasterClockTick → | IMapper.CpuClockRise | ❌ Failed | target not direct |
| MasterClockTick → | IMapper.CpuCycle | ❌ Failed | target not direct |
| PpuBusRead → | IMapper.MapperR_CHR | ❌ Failed | target not direct |
| cpu_step_one_cycle → | (Op handler) | ❌ Failed | target not direct managed (delegate) |
| CpuRead → | (mem_read_fun) | ❌ Failed | delegate invoke |
| apu_step → | (internal) | ❌ Failed | too many il bytes |

### 2.3 Inlining 成功的方法

以下方法在被呼叫時**成功被 inline**（從 `InliningSucceeded` 事件確認）：

**ppu_step_new 內的 inline 成功：**
| Callee | 說明 |
|--------|------|
| CIRAMAddr | CIRAM 地址計算 |
| CXinc | 水平捲動遞增 |
| CopyHoriV | 水平捲動複製 |
| Yinc | 垂直捲動遞增 |
| PpuBusRead | PPU 匯流排讀取 |

**PpuPhase4_SpriteEvalAndInit 內的 inline 成功：**
| Callee | 說明 |
|--------|------|
| FlipByte | Sprite 水平翻轉 (LUT) |
| SpriteEvalInit | Sprite 評估初始化 |
| SpriteEvalTick | Sprite 評估每 dot |
| SpriteEvalEnd | Sprite 評估結束 |
| PrecomputeOverflow | Sprite overflow 預計算 |
| PrecomputePreRenderSprites | Pre-render sprite 預處理 |
| get/set_evalOamAddr | OAM 地址 property |

**MasterClockTick 內的 inline 成功：**
| Callee | 說明 |
|--------|------|
| UpdateIRQLine | IRQ 狀態更新 |
| ProcessControllerShift | 手把 shift |
| ProcessControllerStrobe | 手把 strobe |

**apu_step 內的 inline 成功：**
| Callee | 說明 |
|--------|------|
| setvolumes | 音量設定 |
| AudioPlus_PushApuCycle | 音效推送 |
| authMix_GetVoltage | 混音電壓計算 |

---

## 3. 關鍵發現

### 3.1 ppu_step_new 的 inline 瓶頸變遷

| 階段 | Inline 失敗原因 | 說明 |
|------|-----------------|------|
| Phase 4 extraction 前 | **too many il bytes** | 方法體 ~536 行，IL 超過 JIT inline 門檻 |
| Phase 4 extraction 後 | **too many locals** | IL bytes 已縮減至門檻以下，但本地變數數量仍超標 |

`too many locals` 表示 JIT 認為方法的 local variable slots 太多。Phase 5 中的 `bgColor`, `sprColor`, `sprSlot`, `compositeColor`, `sprPriority`, `sprPalette`, `showBG`, `showSpr` 等大量本地變數是主因。

### 3.2 Interface dispatch 是不可消除的成本

`IMapper.PpuClock()`, `IMapper.CpuClockRise()`, `IMapper.MapperR_CHR()` 等介面呼叫無法被 inline（`target not direct`）。這是 .NET Framework 的限制 — 沒有 devirtualization。

.NET 6+ 的 PGO + guarded devirtualization 可以解決此問題，但 .NET Framework 4.8 不支援。

### 3.3 Delegate dispatch 限制 CPU 路徑

`CpuRead`/`CpuWrite` 透過 `Func<ushort, byte>`/`Action<ushort, byte>` delegate 呼叫 memory handler。JIT 無法 inline delegate invoke（`delegate invoke` / `cannot get method info`）。

### 3.4 Phase 4 extraction 的實際效果

- `PpuPhase4_SpriteEvalAndInit` 成功被 JIT 為獨立方法
- `[NoInlining]` 正確生效，確保冷路徑不會被塞回 ppu_step_new
- SpriteEvalInit/Tick/End、FlipByte、PrecomputeOverflow 等小方法成功被 inline 進 PpuPhase4
- ppu_step_new 本身的 CIRAMAddr、CXinc、Yinc、CopyHoriV、PpuBusRead 也成功被 inline

---

## 4. 效能提升建議（基於分析結果）

| 優先級 | 建議 | 預期效果 | 難度 |
|--------|------|----------|------|
| 高 | 減少 ppu_step_new 的 local variables | 可能解鎖 inline → run() | 中 |
| 高 | apu_step 拆分（10.2% CPU） | 縮減 IL → 可能解鎖 inline | 中 |
| 中 | 將 IMapper 改為 struct + 泛型（消除 interface dispatch） | 消除 vtable call overhead | 高 |
| 低 | 將 mem_read_fun/mem_write_fun 從 delegate 改為 function pointer | 消除 delegate overhead | 高 |

---

## 5. Benchmark 結果比較

| 指標 | #001 Baseline | #011 Last Recorded | #020 Current |
|------|-------------|-------------------|-------------|
| FPS | 87.19 | 95.75 | **101.86** |
| vs #001 | — | +9.8% | **+16.8%** |
