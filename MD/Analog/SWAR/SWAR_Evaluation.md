# SWAR (SIMD Within A Register) 在 AprNes 的適用性評估

> 日期：2026-03-22

---

## 一、什麼是 SWAR

SWAR（SIMD Within A Register）是將多個小型計數器/值打包進單一 64-bit 整數，透過位元運算同時操作所有值的技巧。核心優勢：

- **更少暫存器** → JIT register allocation 壓力降低，減少 spill/reload
- **更少指令** → 主迴圈更小，I-cache 更友善
- **零分支** → 消除分支預測失敗成本
- **更好 inline** → 函數體更小，JIT 更願意 inline 進主迴圈

基本原理示例：
```csharp
// 傳統：4 個獨立計數器，4 次操作，4 個分支
if (--counterA < 0) counterA = reloadA;
if (--counterB < 0) counterB = reloadB;

// SWAR：打包進 64-bit，1 次操作，0 個分支
// |  counterA:16  |  counterB:16  |  padding:32  |
ulong packed -= 0x0001_0001_0000_0000UL;  // 同時遞減兩個計數器
// 用 sign bit extraction + mask 做 branchless reload
```

---

## 二、AprNes 各子系統 SWAR 候選項目

### 優先度總表

| 優先度 | 區域 | 子系統 | 打包值數量 | 預估增益 | 難度 | 建議 |
|:------:|------|--------|:---------:|:--------:|:----:|:----:|
| ★★★ | Pulse 計時器 | APU | 2×(timer+seq) | 6-8% | 中 | **實作** |
| ★★★ | DMC 延遲計數器 | APU | 3× countdown | 2-3% | 中 | **實作** |
| ★★☆ | Triangle/Noise 計時器 | APU | 2×(timer+seq) | 4-6% | 中高 | **實作** |
| ★★☆ | BG Shift Registers | PPU | 4× shift | 3-5% | 低 | **實作** |
| ★☆☆ | DMA 控制旗標 | MEM | 4× bool | 0.5-1% | 低 | 視情況 |
| ☆☆☆ | Envelope Dividers | APU | 4×(pos+cnt) | 0.5% | 高 | 跳過 |
| ☆☆☆ | Sprite Eval FSM | PPU | ~7 values | 0.3% | 極高 | 跳過 |
| ☆☆☆ | Scanline/Dot 計數器 | PPU | 2 counters | 0.2% | 中 | 跳過 |
| ☆☆☆ | Frame Counter | APU | 1 counter | — | — | 跳過 |

> 增益百分比為該子系統內的提升，非整體模擬速度。

---

## 三、高優先度候選項詳細分析

### 3.1 APU Pulse Channel 計時器（★★★，最高 ROI）

**現行程式碼**（APU.cs, apu_step 內）：
```csharp
// Pulse 0
if (--_pulseTimer[0] < 0) {
    _pulseTimer[0] = period0;
    _pulseSeq[0] = (_pulseSeq[0] + 1) & 7;
}
// Pulse 1（完全相同結構）
if (--_pulseTimer[1] < 0) {
    _pulseTimer[1] = period1;
    _pulseSeq[1] = (_pulseSeq[1] + 1) & 7;
}
```

**執行頻率**：每 2 APU cycle（~900,000 次/秒）

**問題**：4 個分支（2 timer × 2 check），每個分支都是 load-compare-branch 序列

**SWAR 方案**：
```
ulong packed:  [pulse0_timer:12 | pulse0_seq:3 | guard:1 | pulse1_timer:12 | pulse1_seq:3 | guard:1 | padding:32]
```

- 同時遞減兩個 timer（一條 SUB 指令）
- 用 sign bit extraction 偵測 underflow（零分支）
- 用 mask + conditional move 做 reload（零分支）
- **預估增益：6-8%**（APU 佔整體 ~40% 時間，此處是 APU 最熱路徑）

**交叉依賴**：Timer reload 時 seq 需要遞增。中等耦合——但可用 predicated logic 處理。

---

### 3.2 APU DMC 延遲計數器（★★★）

**現行程式碼**（APU.cs）：
```csharp
if (dmcDmaCooldown > 0) dmcDmaCooldown--;
if (dmcLoadDmaCountdown > 0) { ... --dmcLoadDmaCountdown; ... }
if (dmcStatusDelay > 0) { --dmcStatusDelay; if (dmcStatusDelay == 0) ... }
```

**執行頻率**：每 APU cycle

**問題**：3 個獨立的 > 0 check + decrement + == 0 check = 6 個分支

**SWAR 方案**：
```
ulong packed:  [dmcCooldown:8 | dmcLoadCountdown:8 | dmcStatusDelay:8 | padding:40]
```

- 所有值都是小計數器（0-5 範圍），8 bits 綽綽有餘
- 用 saturating decrement（位元技巧防止 underflow 到 255）
- 用 zero-detect mask 觸發對應事件
- **預估增益：2-3%**

**注意**：dmcLoadDmaCountdown 有 parity gating（只在偶數 cycle 遞減），需要額外 mask 處理。

---

### 3.3 APU Triangle/Noise 計時器（★★☆）

**現行程式碼**（APU.cs）：
```csharp
// Triangle
if (--_triTimer < 0) {
    _triTimer = triPeriod;
    if (linCtr > 0 && lc2 > 0 && triPeriod >= 2)
        _triSeq = (_triSeq + 1) & 31;
}
// Noise
if (--_noiseTimer < 0) {
    _noiseTimer = noisePeriod;
    int fb = (_noiseLfsr & 1) ^ ((_noiseLfsr >> X) & 1);
    _noiseLfsr = (ushort)((_noiseLfsr >> 1) | (fb << 14));
}
```

**執行頻率**：每 APU cycle

**SWAR 方案**：
```
ulong packed:  [triTimer:12 | triSeq:5 | noiseTimer:12 | padding:35]
```

- Triangle timer + seq 可打包，與 Pulse 類似
- **Noise LFSR 是瓶頸**：LFSR feedback 是固有的序列操作（bit shift + XOR），無法與其他計數器平行化
- **預估增益：4-6%**（Triangle 部分受益明顯，Noise LFSR 限制整體收益）

**難度**：中高——LFSR 本身不適合 SWAR，但 timer 部分仍可受益。

---

### 3.4 PPU BG Shift Registers（★★☆，最容易實作）

**現行程式碼**（PPU.cs, ppu_step_new 內）：
```csharp
lowshift_s0 <<= 1;
highshift_s0 = (ushort)((highshift_s0 << 1) | 1);
// 另外還有主 shift registers
```

**執行頻率**：每個可見 dot（~90,000/frame = 5.4M/sec @60fps）

**SWAR 方案**：
```
ulong packed:  [lowshift:16 | highshift:16 | lowshift_s0:16 | highshift_s0:16]  = 剛好 64 bits
```

- 4 個 shift 打包後，一條左移指令同時處理（需處理跨欄位的進位 mask）
- **零交叉依賴**：4 個 shift register 完全獨立
- **預估增益：3-5%**（PPU 可見 dot 渲染的熱路徑）

**難度**：低——純算術位移，無分支，無狀態依賴。這是最適合練手的 SWAR 候選項。

---

## 四、不適合 SWAR 的區域（及原因）

### 4.1 Sprite Evaluation FSM（跳過）

FSM 有 4+ 條件路徑，狀態之間高度耦合（spriteInRange、oamCopyDone 閘控多個分支）。SWAR 需要 predicated writes，複雜度遠超收益。

### 4.2 Tile Fetch Phase Dispatch（跳過）

`ppu_rendering_tick` 的 phase 0-7 dispatch 是 8 個完全不同的計算路徑。這不是「多個計數器做同一件事」而是「根據計數器值做不同的事」，SWAR 無法消除這種分支。Jump table 或 if/else chain 更適合。

### 4.3 Envelope Dividers（跳過）

每 frame counter event 才觸發（~240 Hz），頻率太低，優化收益可忽略。且 startFlag 條件重置造成高控制流依賴。

### 4.4 Scanline/Dot 計數器（跳過）

簡單遞增 + 稀疏閾值比較（cx==1, cx==341, sl==241 等），SWAR 無法消除這類稀疏比較的開銷。

### 4.5 Frame Counter（跳過）

單一計數器 + data-dependent reload（查表），無法批次化。

---

## 五、SWAR Branchless 技巧速查

### 5.1 Saturating Decrement（飽和遞減）

```csharp
// 遞減但不低於 0（8-bit 子欄位）
// 傳統：if (val > 0) val--;
// SWAR：
ulong mask = packed & 0xFF00FF00FF00FF00UL;  // 抽出每個子欄位的 high bit
ulong nonzero = (mask | (mask >> 1) | ... ) & 0x0100010001000100UL; // 非零偵測
packed -= nonzero;  // 只遞減非零的欄位
```

### 5.2 Zero Detection（零值偵測）

```csharp
// 偵測哪些 8-bit 子欄位為 0
// 用 (val - 1) 的借位傳播：
ulong tmp = (packed - 0x0101010101010101UL) & ~packed & 0x8080808080808080UL;
// tmp 中每個子欄位的 high bit 為 1 表示該欄位為 0
```

### 5.3 Branchless Conditional Reload（無分支條件重載）

```csharp
// 如果 timer underflow（sign bit set），reload 為 period；否則保留
long signMask = (long)packed >> 63;  // 算術右移，全 1 或全 0
packed = (packed & ~(ulong)signMask) | (reloadPacked & (ulong)signMask);
```

### 5.4 Parallel Increment with Wrap（平行遞增 + 回繞）

```csharp
// 將 3-bit seq (0-7) 遞增並 wrap
ulong seqMask = 0x0007_0000_0007_0000UL;  // seq 欄位位置
ulong inc = (packed + 0x0001_0000_0001_0000UL) & seqMask;
packed = (packed & ~seqMask) | inc;
```

---

## 六、建議實作 Bundle 與順序

### Bundle 1：APU Pulse Timers（最高 ROI，先做）

```
ulong apuPulse: [p0_timer:12 | p0_seq:3 | guard:1 | p1_timer:12 | p1_seq:3 | guard:1 | padding:32]
```

- 涵蓋：Pulse 0 + Pulse 1 的 timer 與 sequencer
- 執行頻率：最高（每 2 APU cycle）
- 預估增益：6-8% APU throughput
- 先做這個驗證 SWAR 在 .NET Framework 4.8 JIT 下是否真的有效

### Bundle 2：APU DMC + Triangle 計數器

```
ulong apuCounters: [dmcCooldown:8 | dmcLoadCnt:8 | dmcStatusDelay:8 | triTimer:12 | triSeq:5 | padding:23]
```

- 涵蓋：3 個 DMC delay + Triangle timer/seq
- 預估增益：4-6% APU throughput

### Bundle 3：PPU BG Shift Registers

```
ulong bgShifts: [low:16 | high:16 | low_s0:16 | high_s0:16]
```

- 涵蓋：4 個背景 shift register
- 預估增益：3-5% PPU 可見 dot 渲染
- 零依賴，最容易實作

---

## 七、預估整體效果

| Bundle | 子系統增益 | 子系統佔比 | 整體增益 |
|--------|:---------:|:---------:|:--------:|
| Bundle 1 (Pulse) | 6-8% | APU ~40% | ~2.5-3.2% |
| Bundle 2 (DMC+Tri) | 4-6% | APU ~40% | ~1.6-2.4% |
| Bundle 3 (BG Shift) | 3-5% | PPU ~35% | ~1.0-1.8% |
| **合計** | | | **~5-7%** |

> 前提：需 profiling 確認 APU/PPU 時間佔比。.NET Framework Debug JIT 下可能更高（JIT 不做 register allocation 優化，SWAR 手動減少暫存器壓力的效果更明顯）。

---

## 八、風險與注意事項

1. **.NET Framework 4.8 JIT 限制**：Debug 模式 JIT 不做 register allocation 優化，SWAR 減少暫存器數量的好處反而更明顯。但 Release 模式下 RyuJIT 可能已經把分散的小變數 enregister，SWAR 的增益可能較小。**必須以 benchmark 驗證**。

2. **可讀性降低**：SWAR 程式碼可讀性遠低於直觀寫法。建議每個 bundle 封裝成獨立的 inline helper method，並加上清楚的 bit layout 註解。

3. **正確性驗證**：計數器打包後，邊界條件（overflow、underflow、跨欄位進位）容易出錯。必須通過全部 174 blargg + 136 AC 測試後才能合併。

4. **Guard bits 必要性**：相鄰子欄位之間需保留 guard bit 防止進位/借位溢出。例如 12-bit timer 用 16-bit slot（4 bits guard）。

5. **Noise LFSR 不適合**：LFSR 的 bit feedback 是固有序列操作，不適合與其他計數器平行處理。保持獨立實作。

6. **先做 Bundle 1 驗證**：如果 Pulse timer SWAR 在 benchmark 中沒有可量測的增益，則其餘 bundle 也不值得做。把 Bundle 1 當作 feasibility test。
