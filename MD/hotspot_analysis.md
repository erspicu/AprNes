# AprNes 效能熱點分析

*分析日期：2026-03-15*
*基線：174/174 blargg PASS，136/136 AccuracyCoin PASS*
*效能基線：Debug 242 FPS / Release 241 FPS / .NET 10 RyuJIT 326 FPS*

---

## 10 大效能熱點

| 排名 | 函數 | 檔案 | 呼叫頻率/秒 | 主要開銷 |
|------|------|------|-------------|----------|
| 1 | `ppu_step_new()` | PPU.cs | **5.37M** | 30+ 個 branch，sprite 0 逐 dot 判斷 |
| 2 | `CpuRead()` / `CpuReadZP()` | CPU.cs | **3.5～5.3M** | 每次都呼叫 StartCpuCycle（含 PPU/APU catch-up） |
| 3 | `StartCpuCycle()` | MEM.cs | **1.79M** | 8 個 static field write + 2 個 catch-up loop |
| 4 | `EndCpuCycle()` | MEM.cs | **1.79M** | 複雜 IRQ OR 運算式（每次 6 field accesses） |
| 5 | `apu_step()` | APU.cs | **1.79M** | even-cycle gate + IRQ state machine + 30 field 存取 |
| 6 | `ppu_rendering_tick()` | PPU.cs | **1.8M** | 7-way switch，內呼叫 RenderBGTile() |
| 7 | `RenderBGTile()` | PPU.cs | **231K** | 8×8 pixel 展開迴圈，每次 palette cache refresh |
| 8 | `catchUpPPU()` | MEM.cs | *(由 StartCpuCycle 觸發)* | 每 CPU 週期最多呼叫 3 次 ppu_step_new |
| 9 | `ProcessPendingDma()` | MEM.cs | **50～200** | 單次呼叫執行 500～2000 個 static field 存取 |
| 10 | `generateSample()` | APU.cs | **44.1K** | 5 channel mix + WaveOut buffer write |

---

## 各熱點詳細分析

### 1. `ppu_step_new()` — PPU.cs
**最高呼叫頻率，branch predictor 壓力最大**

- NTSC PPU 每秒 5,369,318 dots，每 dot 呼叫一次
- 30+ 個 conditional branch（scanline/cycle 相位判斷）
- Sprite 0 hit 逐 dot 偵測（dots 2～255，每 dot 6 次比較）
- Secondary OAM 評估 FSM、shift register clocking、VBL flag 管理
- 每次呼叫約 20 reads + 15 writes（靜態欄位）

**瓶頸**：大量 if/else 分支，sprite 0 loop 每 dot 無法跳過。

---

### 2. `CpuRead()` / `CpuReadZP()` — CPU.cs
**呼叫鏈頂端，每次都帶動整個 PPU/APU 前進**

- 每個 CPU 指令平均 2～4 次記憶體讀取 → 估計 3.5～5.3M 次/秒
- 每次都呼叫 StartCpuCycle → catchUpPPU (loop × 3) → ppu_step_new × 3
- function pointer dispatch table (`mem_read_fun[addr]`)
- 每次呼叫帶動約 20 個 static field 存取（StartCpuCycle 主導）

**瓶頸**：無法避免的呼叫鏈，每次記憶體存取都驅動整個模擬前進。

---

### 3. `StartCpuCycle()` — MEM.cs
**每 CPU 週期必執行，開銷集中點**

```
masterClock    += 12      // write
cpuCycleCount  ++         // write
m2PhaseIsWrite = ...      // write
// + NMI delay check
// + catchUpPPU loop（最多 3 次 ppu_step_new）
// + catchUpAPU loop
// + strobeWritePending check
```

- 每秒 1.79M 次 × 13 field accesses = **2330 萬次** 記憶體存取
- catchUpPPU 是內部最貴的部分（3× ppu_step_new）

---

### 4. `EndCpuCycle()` — MEM.cs
**每 CPU 週期必執行，IRQ 判斷**

```csharp
irqLinePrev    = irqLineCurrent;
irqLineCurrent = (statusframeint && !apuintflag) || statusdmcint || statusmapperint;
```

- 每秒 1.79M 次 × 6 field accesses = **1070 萬次** 記憶體存取
- IRQ 運算式每次都從 static field 讀取，無法 cache

**潛在優化**：改為 dirty flag，僅在 APU 寫入時更新 `irqLineCurrent`。

---

### 5. `apu_step()` — APU.cs
**理論高頻，實際受 catch-up 攤平**

- 理論 1.79M/sec，catch-up 攤平後約 356K/sec（每 5 CPU 週期追一次）
- 每次執行約 30 個 static field 存取（Pulse/Triangle/Noise/DMC channel 狀態）
- Even-cycle gate（pulse/noise 每 2 個 APU cycle 才 clock 一次）每次都 check
- IRQ state machine（irqAssertCycles 倒數）
- 內部呼叫 `clockdmc()`（DMC DMA state machine）

---

### 6. `ppu_rendering_tick()` — PPU.cs
**tile fetch pipeline 的 8-phase switch**

- 可見 scanline（0～239）× 256 dots = 61,440 calls/frame → 約 1.8M/sec
- 7-way switch on `(cx & 7)`：NT fetch → AT fetch → CHR low → CHR high → RenderBGTile
- 每 8-phase cycle 呼叫 3 次 Mapper A12 notification（MMC3 scanline IRQ）
- Phase 7 呼叫 `RenderBGTile()`（最貴的子函數）

---

### 7. `RenderBGTile()` — PPU.cs
**每 tile 8 pixel 展開，palette 存取密集**

- 每 frame 240 scanlines × 32 tiles = 7,680 calls/frame → 231K/sec（60 FPS）
- 每次執行：8 次 palette 讀取（ppu_ram）+ 8-pixel 展開迴圈
- 每 pixel：bitwise shift/AND、2 次 attribute lookup、2 次 buffer write（ScreenBuf1x + Buffer_BG_array）
- 每次呼叫約 15 reads + 2 writes

**潛在優化**：SIMD 一次寫入 8 pixels（.NET 8+ `Vector256`）。

---

### 8. `catchUpPPU()` — MEM.cs
**由 StartCpuCycle 觸發，串接 ppu_step_new**

- 並非獨立呼叫，而是 StartCpuCycle 內部的 loop
- 每 CPU 週期呼叫最多 3 次 ppu_step_new（3 PPU dots / CPU cycle）
- 等效於 ppu_step_new 的 wrapper，本身開銷極小

---

### 9. `ProcessPendingDma()` — MEM.cs
**低頻但單次極貴**

- OAM DMA：每次 256 reads + 256 writes = 512+ 個 sub-cycle（含 halt + alignment）
- DMC DMA：3～4 個 stolen cycle per sample fetch
- 每次呼叫執行 500～2000 個 static field 存取
- 複雜 state machine：10+ 個 branch conditions per cycle（DMA type、abort、bus 狀態）
- 影響整體 timing 正確性，優化空間受限

---

### 10. `generateSample()` — APU.cs
**低頻但 I/O 相關**

- 44,100 Hz 輸出 → 44,100 次/秒
- 5 channel mixing（Pulse1 + Pulse2 + Triangle + Noise + DMC）
- WaveOut buffer write（winmm.dll 呼叫）
- 每次執行：讀取 5 個 channel volume + 計算 mix + buffer 寫入

---

## Static Field 的結構性瓶頸

這套架構全部使用 static field，導致 JIT 無法 enregister（放進 CPU register）：

```csharp
// JIT 每次都必須從記憶體 load：
if (ppu_cycle >= 341) {     // memory load
    ppu_cycle -= 341;       // memory load + store
    scanline++;             // memory load + store
}

// 若改為 local snapshot，JIT 可 enregister：
int cy = ppu_cycle, sl = scanline;
if (cy >= 341) { cy -= 341; sl++; }
ppu_cycle = cy; scanline = sl;
```

**這正是 Debug ≈ Release（242 vs 241 FPS）的根本原因**——static field 限制了 Release 的優化空間。
.NET 10 RyuJIT 的 +34.7% 部分來自對靜態欄位存取的更激進 load elimination。

---

## 優化優先順序建議

| 優化目標 | 方法 | 預估收益 | 難度 |
|---------|------|---------|------|
| `RenderBGTile` | SIMD 8-pixel 一次寫入（`Vector128<uint>`） | 高 | 中 |
| `StartCpuCycle` / `EndCpuCycle` | 熱點 local 快照 static field | 中 | 低 |
| `ppu_step_new` sprite 0 | 改為 per-scanline lookup 而非 per-dot | 中 | 中 |
| `EndCpuCycle` IRQ | dirty flag 僅在 APU 寫入時更新 | 小～中 | 低 |
| `ppu_rendering_tick` switch | 8 個 phase 拆成 inlined method | 小 | 低 |

---

## NES 時序參考

| 單位 | 數值 |
|------|------|
| CPU clock (NTSC) | 1,789,773 Hz |
| PPU dots/sec | 5,369,318（CPU × 3） |
| APU steps/sec | 1,789,773（與 CPU 同步） |
| Frame rate | 60.0988 FPS |
| PPU dots/frame | 89,342 |
| CPU cycles/frame | 29,781 |
