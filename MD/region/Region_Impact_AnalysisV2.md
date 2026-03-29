# Region Impact Analysis V2 — 重構後架構

> 基於 PPU region split 重構後的現況分析（2026-03-29）

---

## 重構摘要

將原本單一的 `ppu_step_new()` + `catchUpPPU()` 拆分為 NTSC/PAL/Dendy 三個專版，region-dependent 的變數（`preRenderLine`, `nmiTriggerLine`, `totalScanlines`, `masterPerPpu`）全部改為 hardcode 常數，讓 JIT 能進行常數折疊與分支消除。

---

## 新架構：呼叫圖

```
StartCpuCycle()                           ← MEM.cs, per CPU cycle
  ├─ if (regionMode == 0)
  │    └─ catchUpPPU_ntsc()               ← MEM.cs, hardcode ppuClock+=4, 固定 3 步
  │         └─ ppu_step_ntsc()            ← PPU.cs, hardcode 261/241/262, 有 dot skip
  │              ├─ ppu_step_common()      ← PPU.cs, AggressiveInlining, 共用前導
  │              └─ ppu_step_rendering()   ← PPU.cs, AggressiveInlining, PRE_RENDER_LINE 參數
  │                   └─ ppu_rendering_tick(cx, 261)  ← PPU.cs, preRenderLn 參數
  │
  ├─ else if (regionMode == 1)
  │    └─ catchUpPPU_pal()                ← MEM.cs, hardcode ppuClock+=5, 3~4 步
  │         └─ ppu_step_pal()             ← PPU.cs, hardcode 311/241/312, 無 dot skip
  │              ├─ ppu_step_common()
  │              └─ ppu_step_rendering(cx, re, 311)
  │                   └─ ppu_rendering_tick(cx, 311)
  │
  └─ else
       └─ catchUpPPU_dendy()              ← MEM.cs, hardcode ppuClock+=5, 固定 3 步
            └─ ppu_step_dendy()           ← PPU.cs, hardcode 311/291/312, 無 dot skip
                 ├─ ppu_step_common()
                 └─ ppu_step_rendering(cx, re, 311)
                      └─ ppu_rendering_tick(cx, 311)
```

---

## 各檔案 region-dependent 點清單

### 🔴 Hot Path（per-dot / per-cycle，已完成分版 hardcode）

| 檔案 | 方法 | 變數 | 處理方式 |
|------|------|------|----------|
| **MEM.cs** | `catchUpPPU_ntsc()` | `masterPerPpu=4`, 3 步 | hardcode 常數 |
| **MEM.cs** | `catchUpPPU_pal()` | `masterPerPpu=5`, 3~4 步 | hardcode + `if (ppuClock < masterClock)` |
| **MEM.cs** | `catchUpPPU_dendy()` | `masterPerPpu=5`, 3 步 | hardcode 常數 |
| **MEM.cs** | `StartCpuCycle()` | `regionMode` | `if-else` 分支（~100% 預測命中） |
| **PPU.cs** | `ppu_step_ntsc()` | `preRenderLine=261, nmiTriggerLine=241, totalScanlines=262` | hardcode literal |
| **PPU.cs** | `ppu_step_pal()` | `preRenderLine=311, nmiTriggerLine=241, totalScanlines=312` | hardcode literal |
| **PPU.cs** | `ppu_step_dendy()` | `preRenderLine=311, nmiTriggerLine=291, totalScanlines=312` | hardcode literal |
| **PPU.cs** | `ppu_step_rendering()` | `PRE_RENDER_LINE` 參數 | 由呼叫端傳入 literal，JIT 常數折疊 |
| **PPU.cs** | `ppu_rendering_tick()` | `preRenderLn` 參數 | 由呼叫端傳入 literal，JIT 常數折疊 |
| **PPU.cs** | `ppu_step_ntsc()` | odd frame dot skip | 僅 NTSC 版本包含此邏輯 |

### 🟡 Medium Path（per-frame / per-scanline，未分版，影響小）

| 檔案 | 方法 | 變數 | 頻率 | 說明 |
|------|------|------|------|------|
| **MEM.cs** | `catchUpAPU()` | `masterPerCpu` | per CPU cycle | 每 cycle 1 次欄位讀取，相對 PPU 的 3x 極少 |
| **MEM.cs** | `StartCpuCycle()` | `masterPerCpu` | per CPU cycle | `masterClock += masterPerCpu` |
| **APU.cs** | frame counter IRQ | `Region != Dendy` | 每 7457 APU step | Dendy 禁用 frame counter IRQ |
| **AudioPlus.cs** | `CMF_RF_PHASE_INC` | `Region` | 初始化時 | buzz 頻率計算（50/60Hz） |

### 🟢 Cold Path（per-frame 以下頻率，無需分版）

| 檔案 | 方法 | 變數 | 頻率 | 說明 |
|------|------|------|------|------|
| **PPU.cs** | `ppu_r_2002()` | `nmiTriggerLine` | CPU 讀取觸發 | VBL flag suppress timing |
| **PPU.cs** | `ppu_w_2001()` | `Region` | CPU 寫入觸發 | PAL/Dendy emphasis bit swap (R↔G) |
| **Main.cs** | `ApplyRegionProfile()` | 全部 region 參數 | ROM 載入時 | 設定 `regionMode`, timing 參數 |
| **Main.cs** | `init()` | `masterPerCpu` | 啟動時 | clock 初始值校準 |
| **IO.cs** | `ppu_w_4014()` | 無 region 差異 | OAM DMA | DMA 透過 tick() → StartCpuCycle() 間接使用分版 |

---

## 共用 vs 分版的決策

| 層級 | 決策 | 理由 |
|------|------|------|
| `catchUpPPU` | **三版本** | hardcode `masterPerPpu` + 步數，消除 per-cycle 欄位讀取 |
| `ppu_step` 尾段 | **三版本** | hardcode 3 個 scanline 常數 + dot skip 邏輯差異 |
| `ppu_step_common()` | **共用 (AggressiveInlining)** | 前導邏輯無 region 差異，JIT inline 進呼叫端 |
| `ppu_step_rendering()` | **共用 (AggressiveInlining + 參數)** | `PRE_RENDER_LINE` 以參數傳入，JIT 從 literal 呼叫端常數折疊 |
| `ppu_rendering_tick()` | **共用 (參數)** | 僅 1 處 `preRenderLn` 使用，參數傳入比三版本簡潔 |
| `catchUpAPU()` | **保持現狀** | 仍讀 `masterPerCpu` 欄位，per-cycle 1 次讀取影響極小 |
| `ppu_r_2002` / `ppu_w_2001` | **保持現狀** | 非 hot path，CPU 觸發 |

---

## 新增欄位

| 檔案 | 欄位 | 型別 | 說明 |
|------|------|------|------|
| **Main.cs** | `regionMode` | `static int` | 0=NTSC, 1=PAL, 2=Dendy，在 `ApplyRegionProfile()` 設定 |

### 保留但 hot path 不再讀取的欄位

| 欄位 | 說明 | 仍有用途 |
|------|------|----------|
| `preRenderLine` | PPU pre-render scanline | `ppu_r_2002()` cold path |
| `nmiTriggerLine` | NMI 觸發 scanline | `ppu_r_2002()` cold path |
| `totalScanlines` | frame 總 scanline 數 | 未使用（compiler warning CS0414） |
| `masterPerPpu` | master clock per PPU dot | 未使用（compiler warning CS0414） |
| `masterPerCpu` | master clock per CPU cycle | `StartCpuCycle()` + `catchUpAPU()` 仍使用 |

---

## 預期效能收益

| 項目 | 重構前 | 重構後 | 變化 |
|------|--------|--------|------|
| per-dot `preRenderLine` 欄位讀取 | ~6 次/dot × 89K dots ≈ 534K/frame | 0（literal 常數） | **消除** |
| per-dot `nmiTriggerLine` 欄位讀取 | 1 次/dot | 0（literal 常數） | **消除** |
| per-dot `totalScanlines` 欄位讀取 | 1 次/dot | 0（literal 常數） | **消除** |
| per-dot `Region` 比較（dot skip） | 1 次/dot | 0（僅 NTSC 版含此段） | **消除** |
| per-cycle `masterPerPpu` 欄位讀取 | 3~4 次/cycle | 0（hardcode 4 或 5） | **消除** |
| per-cycle `regionMode` 分支 | 0 | 1 次/cycle（~100% 預測） | +可忽略 |
| 程式碼行數 | ~220 行 × 1 | common ~100 + rendering ~100 + 3×tail ~30 + 3×catchUp ~10 ≈ 290 行 | +70 行 |

---

## 測試驗證

- **blargg 174**: 174/174 PASS ✓
- **AccuracyCoin 136**: 136/136 PASS ✓
- **零回歸**
