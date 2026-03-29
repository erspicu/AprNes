# PAL / Dendy 區域支援 — 異動影響分析

> 涵蓋 commits: 6f98115, 9c33365, b005068, 9ee7623

---

## 1. 新增的核心型別與欄位 (Main.cs)

### 新增列舉
```csharp
public enum RegionType { NTSC, PAL, Dendy }
static public RegionType Region = RegionType.NTSC;
```

### 新增 Region-dependent 時序參數

| 欄位 | NTSC | PAL | Dendy | 可見度 |
|------|------|-----|-------|--------|
| `totalScanlines` | 262 | 312 | 312 | internal |
| `preRenderLine` | 261 | 311 | 311 | internal |
| `nmiTriggerLine` | 241 | 241 | **291** | internal |
| `masterPerCpu` | 12 | 16 | 15 | internal |
| `masterPerPpu` | 4 | 5 | 5 | internal |
| `cpuFreq` | 1,789,773 | 1,662,607 | 1,773,447 | internal |
| `FrameSeconds` | 1/60.0988 | 1/50.0070 | 1/50.0070 | **public** |

### 新增方法
- **`ApplyRegionProfile()`** — 根據 `Region` 設定所有時序參數，於 `init()` 中在 `HardResetState()` 前呼叫

---

## 2. 各檔案異動明細

### AprNes/NesCore/Main.cs

| 位置 | 異動 | 說明 |
|------|------|------|
| L67-68 | 新增 | `RegionType` enum + `Region` 欄位 |
| L71-77 | 新增 | 6 個 region-dependent 時序參數宣告 |
| L79-112 | 新增 | `ApplyRegionProfile()` 三分支（PAL/Dendy/NTSC） |
| L443 | 修改 | `init()` 中呼叫 `ApplyRegionProfile()` |
| L451 | 修改 | `init()` 中增加 `initPaletteRam()` 呼叫 |

---

### AprNes/NesCore/PPU.cs

| 位置 | 方法/區域 | 異動類型 | 說明 |
|------|-----------|----------|------|
| L16-47 | `initPalette()` | **重構** | 分支：PAL 呼叫 `generatePaletteFromVoltages()`，NTSC/Dendy 用硬編碼 |
| L49-110 | `generatePaletteFromVoltages()` | **新增** | PAL 2C07 DAC 電壓 → YUV→RGB 64 色調色盤演算法 |
| L112-123 | `initPaletteRam()` | **新增** | 從 `initPalette()` 抽出 palette RAM 初始化 |
| L221 | `Increment2007()` | 常數替換 | `261` → `preRenderLine` |
| L454, 545, 553, 585, 631 | tile fetch / sprite eval | 常數替換 | `261` → `preRenderLine` |
| L653 | `ppu_step_new()` VBL start | **參數化** | `scanline == 241` → `scanline == nmiTriggerLine` |
| L665-667 | `ppu_step_new()` flag clear | 常數替換 | `261` → `preRenderLine` |
| L672 | `ppu_step_new()` dot skip | **條件守衛** | 加 `Region == RegionType.NTSC &&` |
| L688 | `ppu_step_new()` scanline wrap | 常數替換 | `262` → `totalScanlines` |
| L1043 | `PrecomputePreRenderSprites()` | 常數替換 | `261 & 255` → `preRenderLine & 255` |
| L1266 | `ppu_r_2002()` VBL suppress | **參數化** | `scanline == 241` → `scanline == nmiTriggerLine` |
| L1329-1332 | `ppu_w_2001()` emphasis | **新增邏輯** | PAL/Dendy Red↔Green bit 交換 |
| L1368 | `ppu_w_2004()` OAM protect | 常數替換 | `261` → `preRenderLine` |

**統計**: 10 處 `261→preRenderLine`，2 處 `241→nmiTriggerLine`，1 處 `262→totalScanlines`，2 個新方法，1 個條件守衛，1 個新邏輯

---

### AprNes/NesCore/MEM.cs

| 位置 | 方法 | 異動類型 | 說明 |
|------|------|----------|------|
| L25-32 | 常數宣告 | **移除常數** | `MASTER_PER_CPU/PPU` 改為引用 Main.cs 的 `masterPerCpu/masterPerPpu` |
| L38-52 | `catchUpPPU()` | **新增邏輯** | PAL 第4步：`if (ppuClock < masterClock)` 條件性多執行一步 PPU |
| L60 | `catchUpAPU()` | 常數替換 | 使用 `masterPerCpu` |
| L74 | `StartCpuCycle()` | 常數替換 | `masterClock += masterPerCpu` |

---

### AprNes/NesCore/APU.cs

| 位置 | 方法/區域 | 異動類型 | 說明 |
|------|-----------|----------|------|
| L186 | `apuSoftReset()` | 參數化 | `apuClock` offset: PAL=5, NTSC/Dendy=4 |
| L201 | `apuSoftReset()` | 參數化 | `framectrdiv`: PAL=8305, NTSC/Dendy=7449 |
| L287-315 | `initAPU()` | **分支** | PAL: 專用 frameReload/noise/DMC tables；NTSC/Dendy: NTSC tables |
| L331 | `initAPU()` | 參數化 | `framectrdiv` 同上 |
| L337 | `initAPU()` | 參數化 | `apuClock` 同上 |
| L591 | `clockframecounter()` | **條件守衛** | Dendy 禁用 frame counter IRQ：`&& Region != RegionType.Dendy` |

**APU tables 差異**:

| Table | NTSC (也用於 Dendy) | PAL |
|-------|---------------------|-----|
| `frameReload4` | 7458, 7456, 7458, 7458 | 8314, 8314, 8312, 8314 |
| `noiseperiod[0..15]` | 4..4068 | 4..3778 |
| `dmcperiods[0..15]` | 428..54 | 398..50 |
| `framectrdiv` (init) | 7449 | 8305 |

---

### AprNes/NesCore/AudioPlus/AudioPlus.cs

| 位置 | 異動類型 | 說明 |
|------|----------|------|
| L53 | `const` → `static` | `AP_CPU_FREQ`（隨 region 變動） |
| L55 | `const` → `static` | `AP_CLOCKS_PER_SAMPLE` |
| L375 | `const` → `static` | `CMF_RF_PHASE_INC` |
| L972, 974 | `const` → `static` | `OSE_CUTOFF_NORM`, `OSE_CLOCKS_PER_SAMPLE_FP` |
| L82 | 新呼叫 | `AudioPlus_Init()` 開頭呼叫 `AudioPlus_ApplyRegion()` |
| L98-106 | **新增方法** | `AudioPlus_ApplyRegion()` — 重算所有頻率相關常數 |

**`AudioPlus_ApplyRegion()` 重算的常數**:
```
AP_CPU_FREQ           = cpuFreq (from Main.cs)
AP_CLOCKS_PER_SAMPLE  = AP_CPU_FREQ / 44100
OSE_CUTOFF_NORM       = 20000.0 / AP_CPU_FREQ
OSE_CLOCKS_PER_SAMPLE_FP = (uint)(AP_CLOCKS_PER_SAMPLE * OSE_ONE_CLOCK_FP)
CMF_RF_PHASE_INC      = (NTSC ? 59.94 : 50.0) / 44100
ap_tablesInitialized  = false  (強制 FIR kernel 重建)
```

---

### AprNes/UI/AprNesUI.Designer.cs

| 位置 | 異動 | 說明 |
|------|------|------|
| L62-65 | 新增 | 4 個 `ToolStripMenuItem` 欄位（Region 子選單） |
| L313 | 修改 | Emulation 選單加入 `_menuEmulationRegion` |
| L340-374 | 新增 | Region 子選單初始化（NTSC✓/PAL/Dendy） |
| L370 | 修改 | Dendy `Visible = true`（原 false，commit 9ee7623 改） |
| L202, 426 | 修改 | Ultra Analog 選項隱藏 (`Visible = false`) |

---

### AprNes/UI/AprNesUI.cs

| 位置 | 方法 | 異動類型 | 說明 |
|------|------|----------|------|
| L148 | 初始化 | 新增 | `_menuEmulationRegion` i18n 文字設定 |
| L503-510 | INI 載入 | **新增** | 從 INI 讀取 Region，呼叫 `UpdateRegionCheckmarks()` |
| L1630 | 常數移除 | **刪除** | `const double NES_FRAME_SECONDS` |
| L1657, 1661 | FPS limiter | **替換** | `NES_FRAME_SECONDS` → `NesCore.FrameSeconds` |
| L2084-2103 | `_menuEmulationRegion_Click()` | **新增** | Region 切換處理 + INI 保存 + HardReset |
| L2104-2108 | `UpdateRegionCheckmarks()` | **新增** | 三選一勾選狀態更新 |

---

### AprNes/TestRunner.cs

| 位置 | 異動類型 | 說明 |
|------|----------|------|
| L112-115 | **新增邏輯** | 驗證測試強制 `Region = NTSC`（防 INI 汙染） |
| L256-262 | **新增參數** | `--region <NTSC|PAL|Dendy>` CLI 參數 |

---

## 3. 影響範圍矩陣

| 子系統 | NTSC→PAL 差異 | NTSC→Dendy 差異 |
|--------|---------------|-----------------|
| **PPU 時序** | 312 scanlines, 3.2:1 ratio, no dot skip | 312 scanlines, 3:1 ratio, NMI@291, no dot skip |
| **PPU 調色盤** | YUV 動態生成 | 使用 NTSC 硬編碼 |
| **PPU Emphasis** | Red↔Green 交換 | Red↔Green 交換 |
| **APU Tables** | PAL 專用 noise/DMC/frame counter | 使用 NTSC tables |
| **APU IRQ** | 正常 | **完全禁用** |
| **APU 頻率** | cpuFreq=1,662,607 | cpuFreq=1,773,447 |
| **AudioPlus** | 50Hz buzz, 重算 FIR | 50Hz buzz, 重算 FIR |
| **MEM 同步** | catchUpPPU 4th step | 無（3:1 同 NTSC） |
| **UI** | FrameSeconds=1/50 | FrameSeconds=1/50 |
| **TestRunner** | --region PAL 可用 | --region DENDY 可用 |

---

## 4. Region 條件判斷彙整

| 判斷式 | 出現位置 | 用途 |
|--------|----------|------|
| `Region == RegionType.PAL` | APU.cs ×5 | PAL 專用 tables/timing |
| `Region == RegionType.NTSC` | PPU.cs ×1, AudioPlus.cs ×1 | NTSC only dot skip, 59.94Hz buzz |
| `Region != RegionType.NTSC` | PPU.cs ×1 | PAL/Dendy emphasis swap |
| `Region != RegionType.Dendy` | APU.cs ×1 | Dendy IRQ disable |
| `Region == RegionType.Dendy` | Main.cs ×1 | Dendy profile branch |
| `ppuClock < masterClock` | MEM.cs ×1 | PAL 4th PPU step（隱式 region 判斷） |

---

## 5. Hot Path / Loop 內異動標註

> 以下標註位於 **每幀執行數萬~數十萬次** 的 hot path 中的異動，對效能影響最敏感。

### 🔴 極高頻率 (per PPU dot, ~89,342 次/frame NTSC, ~106,392 PAL)

| 異動 | 檔案:方法 | 原始碼 | 影響 |
|------|-----------|--------|------|
| `nmiTriggerLine` 比較 | PPU.cs:`ppu_step_new()` | `scanline == nmiTriggerLine && cx == 1` | 原 `241` 常數→靜態欄位讀取，每 dot 執行 |
| `preRenderLine` 比較 ×2 | PPU.cs:`ppu_step_new()` | `scanline == preRenderLine && cx == 1/2` | 原 `261` 常數→靜態欄位讀取，每 dot 執行 |
| `totalScanlines` 比較 | PPU.cs:`ppu_step_new()` | `scanline == totalScanlines` | 原 `262` 常數→靜態欄位，每 dot 但只在 cx==341 時進入 |
| NTSC dot skip guard | PPU.cs:`ppu_step_new()` | `Region == RegionType.NTSC && scanline == preRenderLine && cx == 339` | **新增** `Region` 比較（每 dot 評估前兩個條件） |
| `ppuRenderingEnabled` 延遲 | PPU.cs:`ppu_step_new()` | tile fetch/shift 使用 `ppuRenderingEnabled` | 已存在，PAL 無額外變動 |

**效能評估**: 常數→靜態欄位在 JIT 後通常被 inline 為暫存器或 L1 快取，影響極小。`Region == RegionType.NTSC` 是 int 比較，且 short-circuit 在 `scanline != preRenderLine` 時跳過。

### 🟠 高頻率 (per CPU cycle, ~29,781 次/frame NTSC)

| 異動 | 檔案:方法 | 原始碼 | 影響 |
|------|-----------|--------|------|
| `masterPerPpu` 使用 ×3~4 | MEM.cs:`catchUpPPU()` | `ppuClock += masterPerPpu` | 原 `MASTER_PER_PPU` 常數→靜態欄位，3 次展開 |
| PAL 第4步條件 | MEM.cs:`catchUpPPU()` | `if (ppuClock < masterClock)` | **新增** 分支：NTSC 永不進入（3×4=12==12），PAL 每5次進入1次 |
| `masterPerCpu` 使用 | MEM.cs:`StartCpuCycle()` | `masterClock += masterPerCpu` | 原常數→靜態欄位 |
| `masterPerCpu` 使用 | MEM.cs:`catchUpAPU()` | `apuClock += masterPerCpu` | 同上 |

**效能評估**: `catchUpPPU()` 是最核心的 hot path。PAL 第4步的 `if` 分支在 NTSC 模式下分支預測器會 100% 預測為 not-taken（永不進入），零成本。PAL 模式下 80% not-taken / 20% taken，分支預測率極高。

### 🟡 中頻率 (per APU step, ~29,781 次/frame)

| 異動 | 檔案:方法 | 原始碼 | 影響 |
|------|-----------|--------|------|
| Dendy IRQ guard | APU.cs:`clockframecounter()` | `&& Region != RegionType.Dendy` | 新增條件，但 `clockframecounter()` 本身低頻（每 ~7458 APU cycles 才呼叫一次） |

**效能評估**: `clockframecounter()` 每幀僅呼叫 4~5 次，額外的 `Region` 比較完全可忽略。

### 🟢 低頻率 (per frame 或更低，不在 hot path)

| 異動 | 檔案:方法 | 頻率 | 影響 |
|------|-----------|------|------|
| Emphasis swap | PPU.cs:`ppu_w_2001()` | 遊戲寫入 $2001 時 | 僅在寫入時執行，大部分遊戲每幀 0~1 次 |
| `nmiTriggerLine` 比較 | PPU.cs:`ppu_r_2002()` | 遊戲讀 $2002 時 | VBL poll 每幀數次 |
| `preRenderLine` 比較 | PPU.cs:各 precompute | 每 scanline 1 次 | 240 次/frame |
| `AudioPlus_ApplyRegion()` | AudioPlus.cs | init 時 1 次 | 完全不影響 |
| `ApplyRegionProfile()` | Main.cs | init 時 1 次 | 完全不影響 |

### Hot Path 總結

| 頻率等級 | 異動數 | 主要類型 | 預估效能影響 |
|----------|--------|----------|-------------|
| 🔴 Per-dot | 5 處 | 常數→靜態欄位、新增 Region 比較 | **< 0.1%**（JIT inline + 分支預測） |
| 🟠 Per-cycle | 4 處 | 常數→靜態欄位、新增 if 分支 | **< 0.1%**（NTSC: 預測 100% not-taken） |
| 🟡 Per-frame counter | 1 處 | 新增 Region 比較 | **0%**（每幀 4~5 次） |
| 🟢 Init/低頻 | 多處 | 新方法、條件邏輯 | **0%** |

> **結論**: 所有 hot path 異動均為常數替換為靜態欄位讀取或簡單 int 比較，在 .NET JIT + 分支預測下對 NTSC 模式效能影響趨近於零。PAL 模式因 `catchUpPPU()` 多一個條件分支和偶爾的第4步執行，理論上每幀多 ~21,000 次 PPU step，但這是 PAL 硬體本身的特性需求。

---

## 6. 新增方法清單

| 方法 | 檔案 | 說明 |
|------|------|------|
| `ApplyRegionProfile()` | Main.cs | 設定所有 region-dependent 參數 |
| `generatePaletteFromVoltages()` | PPU.cs | PAL DAC 電壓 → YUV→RGB 調色盤 |
| `initPaletteRam()` | PPU.cs | 初始化 PPU palette RAM |
| `AudioPlus_ApplyRegion()` | AudioPlus.cs | 重算音效頻率常數 |
| `_menuEmulationRegion_Click()` | AprNesUI.cs | Region 選單切換 |
| `UpdateRegionCheckmarks()` | AprNesUI.cs | 勾選狀態更新 |
