# ppu_step_new() Region 分版計畫

## 目標

將 `ppu_step_new()` 分成 NTSC / PAL / Dendy 三個版本，所有 region-dependent 的變數改為 hardcode 常數，讓 JIT 能完全消除 branch 和靜態欄位讀取的開銷。

---

## 現有 region-dependent 使用點 (ppu_step_new 內)

| 行 | 原始碼 | 變數 | NTSC | PAL | Dendy |
|----|--------|------|------|-----|-------|
| L545 | `scanline == preRenderLine` | `preRenderLine` | 261 | 311 | 311 |
| L553 | `scanline < 240 \|\| scanline == preRenderLine` | `preRenderLine` | 261 | 311 | 311 |
| L585 | `scanline == preRenderLine && cx == 65` | `preRenderLine` | 261 | 311 | 311 |
| L631 | `scanline == preRenderLine && cx == 257` | `preRenderLine` | 261 | 311 | 311 |
| L653 | `scanline == nmiTriggerLine && cx == 1` | `nmiTriggerLine` | 241 | 241 | **291** |
| L665 | `scanline == preRenderLine && cx == 1` | `preRenderLine` | 261 | 311 | 311 |
| L667 | `scanline == preRenderLine && cx == 2` | `preRenderLine` | 261 | 311 | 311 |
| L672 | `Region == RegionType.NTSC && ...` | `Region` | **有 dot skip** | 無 | 無 |
| L688 | `scanline == totalScanlines` | `totalScanlines` | 262 | 312 | 312 |

**統計**: 6 處 `preRenderLine`, 1 處 `nmiTriggerLine`, 1 處 `totalScanlines`, 1 處 `Region` 判斷

---

## ppu_step_new 之外的相關 region-dependent 點

| 位置 | 方法 | 變數 | 說明 |
|------|------|------|------|
| PPU.cs L454 | `ppu_rendering_tick()` | `preRenderLine` | 在 ppu_step_new 呼叫的子函式內 |
| PPU.cs L1266 | `ppu_r_2002()` | `nmiTriggerLine` | 不在 hot path loop 內（CPU 讀取觸發） |
| PPU.cs L1330 | `ppu_w_2001()` emphasis swap | `Region` | 不在 hot path（CPU 寫入觸發） |
| MEM.cs L41-52 | `catchUpPPU()` | `masterPerPpu`, `ppuClock < masterClock` | 呼叫 ppu_step_new 的外框 |
| MEM.cs L60 | `catchUpAPU()` | `masterPerCpu` | APU 同步 |
| MEM.cs L74 | `StartCpuCycle()` | `masterPerCpu` | master clock 推進 |

---

## 分割方案

### 方案: 函式指標 + 完整分版（catchUpPPU + ppu_step 一起分）

#### 分割層級

```
StartCpuCycle()               ← 最外層，per CPU cycle
  ├─ catchUpPPU()             ← ⭐ 分割點：用函式指標選版本
  │    ├─ ppu_step_ntsc()     ← hardcode: preRenderLine=261, nmiTriggerLine=241,
  │    │                         totalScanlines=262, 有 dot skip
  │    ├─ ppu_step_pal()      ← hardcode: preRenderLine=311, nmiTriggerLine=241,
  │    │                         totalScanlines=312, 無 dot skip
  │    └─ ppu_step_dendy()    ← hardcode: preRenderLine=311, nmiTriggerLine=291,
  │                              totalScanlines=312, 無 dot skip
  ├─ catchUpAPU()
  └─ processStrobeWrite()
```

#### 具體實作

**Step 1: 定義三個 ppu_step 版本**

```csharp
// PPU.cs — 三個版本，所有 region 參數改為 literal 常數
static void ppu_step_ntsc()
{
    // preRenderLine = 261, nmiTriggerLine = 241, totalScanlines = 262
    // 包含 odd frame dot skip 邏輯
    ...
}

static void ppu_step_pal()
{
    // preRenderLine = 311, nmiTriggerLine = 241, totalScanlines = 312
    // 無 dot skip
    ...
}

static void ppu_step_dendy()
{
    // preRenderLine = 311, nmiTriggerLine = 291, totalScanlines = 312
    // 無 dot skip
    ...
}
```

**Step 2: 定義三個 catchUpPPU 版本**

```csharp
// MEM.cs
static void catchUpPPU_ntsc()   // 固定 3 步, ppuClock += 4
{
    ppuClock += 4; ppu_step_ntsc(); /* NMI edge */ ...
    ppuClock += 4; ppu_step_ntsc(); /* NMI edge */ ...
    ppuClock += 4; ppu_step_ntsc(); /* NMI edge */ ...
}

static void catchUpPPU_pal()    // 3 或 4 步, ppuClock += 5
{
    ppuClock += 5; ppu_step_pal(); /* NMI edge */ ...
    ppuClock += 5; ppu_step_pal(); /* NMI edge */ ...
    ppuClock += 5; ppu_step_pal(); /* NMI edge */ ...
    if (ppuClock < masterClock)
    { ppuClock += 5; ppu_step_pal(); /* NMI edge */ ... }
}

static void catchUpPPU_dendy()  // 固定 3 步, ppuClock += 5
{
    ppuClock += 5; ppu_step_dendy(); /* NMI edge */ ...
    ppuClock += 5; ppu_step_dendy(); /* NMI edge */ ...
    ppuClock += 5; ppu_step_dendy(); /* NMI edge */ ...
}
```

**Step 3: 函式指標在 ApplyRegionProfile() 設定**

```csharp
// Main.cs
delegate void CatchUpPPUDelegate();
static CatchUpPPUDelegate catchUpPPU_fn;

static void ApplyRegionProfile()
{
    if (Region == RegionType.PAL)
    {
        catchUpPPU_fn = catchUpPPU_pal;
        ...
    }
    else if (Region == RegionType.Dendy)
    {
        catchUpPPU_fn = catchUpPPU_dendy;
        ...
    }
    else
    {
        catchUpPPU_fn = catchUpPPU_ntsc;
        ...
    }
}
```

**Step 4: StartCpuCycle() 使用函式指標**

```csharp
static void StartCpuCycle()
{
    irqLinePrev = irqLineCurrent;
    masterClock += masterPerCpu;  // masterPerCpu 仍用靜態欄位（每 cycle 1 次讀取，影響極小）
    cpuCycleCount++;
    m2PhaseIsWrite = (cpuCycleCount & 1) != 0;

    if (nmi_delay_cycle >= 0 && cpuCycleCount > nmi_delay_cycle)
    { nmi_pending = true; nmi_delay_cycle = -1; }

    catchUpPPU_fn();    // ← 函式指標呼叫，JIT 無法 inline
    catchUpAPU();
    if (strobeWritePending > 0) processStrobeWrite();
}
```

---

## ⚠️ 需要注意的問題

### 1. delegate 的 JIT inline 限制

**問題**: C# delegate 呼叫 (`catchUpPPU_fn()`) 是間接呼叫（類似 virtual call），.NET JIT **無法 inline** delegate target。這意味著：
- `catchUpPPU_ntsc()` 本身不會被 inline 進 `StartCpuCycle()`
- 但 `catchUpPPU_ntsc()` 內部呼叫的 `ppu_step_ntsc()` **可以被 inline**（因為是直接 static 呼叫）

**對策**: 這仍然比現行方案好，因為：
- 現行: 每 dot 讀取 4~5 個靜態欄位 + 1 個 Region 比較
- 分版: 1 次 delegate 間接呼叫（per CPU cycle），內部全是常數

### 2. 替代方案：if-else 在 StartCpuCycle 層級

如果不想用 delegate（避免間接呼叫開銷），可直接在 `StartCpuCycle()` 用 if-else：

```csharp
static void StartCpuCycle()
{
    ...
    if (regionMode == 0)      catchUpPPU_ntsc();
    else if (regionMode == 1) catchUpPPU_pal();
    else                      catchUpPPU_dendy();
    ...
}
```

其中 `regionMode` 是 int（0/1/2），在 `ApplyRegionProfile()` 設定一次。

**優點**: JIT 看到的是直接 static 呼叫，有機會 inline `catchUpPPU_ntsc()` 進 `StartCpuCycle()`
**缺點**: 每 CPU cycle 多一個 int 比較 + branch（但分支預測率 ~100%，成本極低）

### 3. ppu_rendering_tick() 內的 preRenderLine

`ppu_rendering_tick()` 也用了 `preRenderLine`，需要同步分版或改為參數傳入。

### 4. 程式碼重複

三個版本的 ppu_step 有 95% 的程式碼相同，只差幾個常數。維護風險：改一處忘了改另外兩處。

**緩解方案**:
- 用 `#region` 標記差異處，加註解 `// REGION-SPECIFIC`
- 或用 T4 template / 原始碼產生器（過度工程，不建議）
- 最務實：三份程式碼，差異處加 `// ★ REGION` 標記，修改時搜尋此 tag

---

## 建議方案

| 層級 | 方案 | 理由 |
|------|------|------|
| `StartCpuCycle()` | **if-else + regionMode int** | 避免 delegate 開銷，JIT 可 inline，分支預測 ~100% |
| `catchUpPPU_*()` | **三版本** | hardcode `masterPerPpu` + 步數（NTSC/Dendy=3, PAL=3~4） |
| `ppu_step_*()` | **三版本** | hardcode `preRenderLine`, `nmiTriggerLine`, `totalScanlines`, dot skip |
| `ppu_rendering_tick()` | **傳入 preRenderLine 參數** 或 **三版本** | 只有 1 處用到，傳參數更簡潔 |
| `ppu_r_2002()` | **保持現狀** | 不在 per-dot hot path，讀取觸發，無需分版 |
| `ppu_w_2001()` | **保持現狀** | 寫入觸發，每幀 0~1 次 |

---

## 預期效能收益

| 項目 | 現行 | 分版後 | 節省 |
|------|------|--------|------|
| per-dot 靜態欄位讀取 | ~6 次/dot × 89K dots = ~534K 次/frame | 0 | 消除 |
| per-dot Region 比較 | 1 次/dot × 89K dots = ~89K 次/frame | 0 | 消除 |
| per-cycle if-else | 0 | 1 次/cycle × 30K cycles（~100% 預測） | 可忽略 |
| 程式碼大小 | ~220 行 × 1 | ~220 行 × 3 = ~660 行 | +440 行 |

**預估**: NTSC 模式下效能提升 **1~3%**（主要來自 JIT 常數折疊 + 消除分支）。
PAL/Dendy 收益類似。

---

## 實作步驟

1. 在 Main.cs 新增 `static int regionMode` (0=NTSC, 1=PAL, 2=Dendy)，在 `ApplyRegionProfile()` 設定
2. 複製 `ppu_step_new()` → `ppu_step_ntsc()` / `ppu_step_pal()` / `ppu_step_dendy()`，替換常數
3. 複製 `catchUpPPU()` → 三版本，hardcode `masterPerPpu` 和步數
4. `StartCpuCycle()` 改用 `if (regionMode == 0) ... else if ...`
5. `ppu_rendering_tick()` 改為接收 `int preRenderLine` 參數
6. 跑 174 blargg + 136 AC 確認無回歸
7. Benchmark 比較分版前後 FPS
