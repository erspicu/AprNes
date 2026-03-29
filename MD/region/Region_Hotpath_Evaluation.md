# Region 設計整體效能評估 — Hot Path 分析

> 2026-03-29

---

## 已優化的最熱路徑 (per-dot / per-cycle) ✅

| 路徑 | 頻率 | 方式 |
|------|------|------|
| `catchUpPPU_ntsc/pal/dendy()` | per CPU cycle (~30K/frame) | hardcode `masterPerPpu` (4 or 5) |
| `ppu_step_ntsc/pal/dendy()` | per dot (~89K/frame) | hardcode scanline 常數 + packed const guard |
| `ppu_step_rendering()` | per dot | `PRE_RENDER_LINE` 參數傳入 literal，JIT 常數折疊 |
| `ppu_rendering_tick()` | per dot | `preRenderLn` 參數傳入 literal，JIT 常數折疊 |
| `StartCpuCycle()` 分派 | per CPU cycle | `regionMode` int if-else (~100% 分支預測) |

### 優化手段說明

- **三版本分割**: `ppu_step_new()` → `ppu_step_ntsc/pal/dendy()`，`catchUpPPU()` → `catchUpPPU_ntsc/pal/dendy()`
- **參數傳入取代欄位讀取**: `ppu_step_rendering(cx, re, 261)` / `ppu_rendering_tick(cx, 261)` — JIT 將 literal 參數視為常數
- **Packed constant guard**: `if (cx <= 2) { L = (scanline<<9)|cx; if (L == CONST) ... }` — 339/341 dots 跳過所有 scanline event 檢查
- **regionMode int**: 避免 Region enum 比較，if-else 取代 delegate（JIT 可 inline）

---

## 殘留的 runtime field 讀取（warm path）

| 檔案 | 方法 | 行號 | 變數 | 頻率 | 影響評估 |
|------|------|------|------|------|----------|
| PPU.cs | `Increment2007()` | L221 | `preRenderLine` | per $2007 write | 低 — CPU 讀寫 VRAM 時觸發，非 per-dot |
| PPU.cs | `ppu_r_2002()` | L1337 | `nmiTriggerLine` | per $2002 read | 低 — VBL flag 讀取，每幀 0~數次 |
| PPU.cs | `ppu_w_2004()` | L1439 | `preRenderLine` | per OAM write | 低 — 渲染期間寫 OAM 才觸發 |
| PPU.cs | `PrecomputePreRenderSprites()` | L1120 | `preRenderLine` | per frame (1次) | 可忽略 |
| APU.cs | `apu_step()` frame IRQ | L591 | `Region != Dendy` | per frame counter step 3 | 可忽略 — 每幀 1 次 |

### 為何不需要進一步優化

- `$2002` / `$2007` / `$2004` 是 **CPU IO 觸發**，不是 per-dot 迴圈內的操作
- 典型遊戲每幀讀 `$2002` 約 1~3 次，讀寫 `$2007` 在 VBL 期間集中（~幾百次/frame）
- 單次 static field 讀取成本 ≈ 1 cycle，相比 per-dot 的 ~89K 次，這些路徑的總成本 < 0.1%
- 拆分這些函式需要 3 版本 × 多個函式，維護成本遠大於效能收益

---

## Cold path（init / reset）— 無需優化

| 檔案 | 方法 | 時機 |
|------|------|------|
| Main.cs | `ApplyRegionProfile()` | ROM 載入 / HardReset |
| PPU.cs | `initPalette()` | ROM 載入 |
| APU.cs | `initAPU()` | ROM 載入 |
| APU.cs | `apuSoftReset()` | Reset |
| AudioPlus.cs | `AudioPlus_ApplyRegion()` | ROM 載入 |

這些都只在初始化時執行一次，使用 `Region` enum 直接判斷，無需優化。

---

## 量化評估

### Per-frame 操作次數比較

| 操作 | 次數/frame | 重構前 region field 讀取 | 重構後 |
|------|-----------|------------------------|--------|
| PPU dot (ppu_step) | ~89,342 (NTSC) | ~6 次/dot = **536K** | **0** (hardcode) |
| CPU cycle (StartCpuCycle) | ~29,781 | 1 次 `masterPerPpu` = **30K** | **0** (hardcode) + 1 次 `regionMode` int |
| $2002 read | ~1-3 | 1 次 `nmiTriggerLine` | 1 次（不變） |
| $2007 read/write | ~200-500 (VBL) | 1 次 `preRenderLine` | 1 次（不變） |
| Frame counter IRQ | 1 | 1 次 `Region` enum | 1 次（不變） |

**消除的 region field 讀取**: ~566K 次/frame → 剩餘 ~數百次/frame

---

## 結論

per-dot 和 per-cycle 的最熱路徑已全部以 hardcode 常數或參數傳入處理，JIT 可進行常數折疊與分支消除。殘留的 5 處 runtime field 讀取都在 IO 觸發的 warm/cold path，對效能影響趨近於零（< 0.1%），不需要再拆分版本。

**目前的 region 架構設計已達最佳效能/維護平衡點。**
