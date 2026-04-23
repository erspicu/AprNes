# AprNes PPU Dispatch 架構對照 TriCNES 的設計指南

> 記錄 `ppu_dispatch.cs` 當前（commit 1bea3d1）的設計意圖、TriCNES 邏輯對應，以及未來同步 TriCNES 更新的參考。
>
> 目的：讓每次 TriCNES 上游更新都能快速對照到 AprNes 的哪個 handler / helper 需要修改，不用重新推演一遍架構。

---

## 1. 檔案結構 & commit 對照

- **當前 peak 版本**：commit `1bea3d1` —— FPS 136.30 / +11.39% vs master
- **檔案**：`AprNes/NesCore/ppu_dispatch.cs`（1088 行）+ `AprNes/NesCore/ppu_new.cs`（thin dispatcher + phase helpers）
- **實驗完整紀錄**：`feature/ppu-refactor` branch（含 10 次實驗 commit 與回歸分析）
- **peak 分支**：`feature/ppu-refactor-v2`（從 1bea3d1 切出的乾淨 base）

---

## 2. 核心架構：tri-state 341-slot dispatch

TriCNES 的 `_EmulatePPU` 是一條線性單一大函式；AprNes 把它拆成**按 scanline state 的 3 個 dispatch table**，每個 table 341 slot，slot index = 入口 `ppu_cycles_x`：

```
ppu_step_new():
    sl = scanline
    cx = ppu_cycles_x
    if (sl < 240)              → ppuTickVisibleTable[cx]()
    else if (sl == preRenderLine) → ppuTickPreRenderTable[cx]()
    else                          → ppuTickVBlankTable[cx]()
```

### 3 個 table 的 slot 填法（1bea3d1 當前）

| Slot 範圍 | Visible table | PreRender table | VBlank table |
|---|---|---|---|
| 0-255 | `Ppu_Tick_Visible_PixelZone` | `Ppu_Tick_PreRenderLine` | `Ppu_Tick_VBlankLine` |
| 256-257 | `Ppu_Tick_VisibleLine` (generic) | 同上 | 同上 |
| 258-319 | `Ppu_Tick_Visible_SpriteFetch` | 同上 | 同上 |
| 320-335 | `Ppu_Tick_Visible_Prefetch` | 同上 | 同上 |
| 336-339 | `Ppu_Tick_Visible_Dummy` | 同上 | 同上 |
| 340 | `Ppu_Tick_VisibleLine` (generic) | 同上 | 同上 |

Visible table 有 **5 個特化 zone handler + 1 個通用 fallback**，共 7 個 handler 被 dispatch。PreRender / VBlank 全路由到通用 handler（冷路徑，特化測過回歸所以保留通用）。

---

## 3. TriCNES `_EmulatePPU` 執行順序 → AprNes 對應

TriCNES `Emulator.cs` 從 line 1256 開始的 `_EmulatePPU` 線性流程，AprNes 的 handler（不論哪個 zone）內部**嚴格保持同樣順序**：

| # | TriCNES 階段 | TriCNES 行號 | AprNes 呼叫 | 備註 |
|---|---|---|---|---|
| 1 | Deferred $2006/$2005 updates | 1263-1496 | `PpuPhase2_DeferredUpdates(cx)` | `ppu2006UpdateDelay` / `ppu2005UpdateDelay` gate，> 99% 不跑 |
| 2 | Open bus decay | - | `open_bus_decay_timer--` inline | 每 dot 都跑 |
| 3 | Scroll increments (pre-inc cx) | 1498-1516 | `Yinc()` / `CopyHoriV()` / vert reset | **PRE-increment**（讀 OLD cx） |
| 4 | `ppu_cycles_x = ++cx` + scanline wrap | 1518-1530 | inline `++cx` + 分支式 wrap | **mid-function 遞增** |
| 5 | Phase 3 events | 1532-1606 | `PpuPhase3_Events(cx)` | gated `scanline >= nmiTriggerLine` |
| 6 | VSET latch pipeline | 1608-1618 | inline `ppuVSET_Latch1 / isVblank / ppu2002ReadPending` | 每 dot |
| 7 | Sprite overflow delayed | 1619 | `isSpriteOverflow_Delayed = isSpriteOverflow` | 每 dot |
| 8 | Mapper + A12 prev | 1478-1479 (early in SM) | `MapperObj.PpuClock()` / `ppuA12Prev` | 每 dot，cold |
| 9 | Odd-frame dot skip | - | `PpuPhase_DoOddFrameSkip(ref cx)` | preRender cx==340 only（極冷）|
| 10 | Eval delay non-phase-3 | 1506 | inline `ShowBG_EvalDelay / ShowSpr_EvalDelay` | (mcCpuClock & 3) != 3 |
| 11 | PPU_DATA State Machine 1 | 1513 | `PPU_DATA_Pipeline_Step(1)` | 每 dot |
| 12 | Delayed OAM corruption | 1695-1711 | `PpuPhase_HandleDelayedOamCorruption(isActive)` | `oamCorruptDelay` gate |
| 13 | Sprite evaluation | 1664 | `PpuPhase4_SpriteEvalAndInit()` | 內部自己 gate active scanline |
| 14 | Eval delay phase-3 | 1667-1673 | 同 #10 但 `& 3 == 3` | **順序關鍵，不可跟 #10 合併** |
| 15 | ppuAddressBus fallback | 1530-1535 | `if (!BG && !Spr) ppuAddressBus = vram_addr` | rendering disabled only |
| 16 | $2001 mask / emphasis delays | - | `PpuPhase_Apply2001Mask/Emphasis` | `oamCorruptDelay` gate |
| 17 | Pipeline shift (prev color) | 1724 | inline 6 行 `prevPrev... = prev...` | **ALL scanlines ALL dots**，一定要跑 |
| 18 | Tile fetch (PAR / A12 / ALE) | 1728-1751 (line 3588 shift/bit) | `Ppu_ActiveScanline_RenderBlock(cx)` 內含 | `cx in [1,256] ∪ [321,336]` |
| 19 | CalcPixel | 3073 | 同上 render block 內 | `scanline < 240 && cx in [1,256]` |
| 20 | UpdateSpriteShift | 3718 | 同上 render block 內 | `cx in [1,256]` |
| 21 | PPU_DATA State Machine 2 | 1657 | `PPU_DATA_Pipeline_Step(2)` | 每 dot |
| 22 | Draw to screen | 1764 | inline `cx in [4,259]` | visible scanlines only |
| 23 | NTSC scanline capture | - | `Ntsc_CaptureScanline()` | `cx == 260` only |
| 24 | Frame render trigger | - | `PpuPhase_FrameRender()` | `scanline == 240 && cx == 1` only |
| 25 | ppuRenderingEnabled update | - | inline `= ShowBG_Instant \|\| ShowSpr_Instant` | 每 dot |

**TriCNES sync 要點**：任何 _EmulatePPU 的修改，**先找到對應的階段編號 (#1-#25)**，再到每個 AprNes handler 裡對應的段落改。有些階段（#17, #21）在每個 handler 內都有一份複製，所以要**同步改 7 處**（V_PixelZone, V_SpriteFetch, V_Prefetch, V_Dummy, VisibleLine, PreRenderLine, VBlankLine）。

---

## 4. Zone 特化：哪些 gate 被 bake 掉，哪些保留

這是理解 PPU 特化設計的**關鍵表**。TriCNES sync 時要看某個 gate 是不是該 zone 已經 bake：

### `Ppu_Tick_Visible_PixelZone`（entry cx 0-255, post-inc 1-256）

| Gate | 狀態 | 原因 |
|---|---|---|
| Scroll ops (Yinc/CopyHoriV/vert reset) | **刪除** | entry cx ∉ {256, 257, 280-304} |
| Scanline wrap | **刪除** | entry cx < 340 |
| Events | **刪除** | scanline < 240 < nmiTriggerLine (241) |
| Odd-frame skip | **刪除** | preRender-only |
| `skippedPreRenderDot341` reset | **保留** | 在 entry cx=1 + scanline=0 會 fire |
| Tile fetch gate `cx in [1,256] ∪ [321,336]` | **刪除** | post-inc cx ∈ [1,256] 永遠 true |
| Pixel gate `cx > 0 && cx <= 256` | **刪除** | 同上 |
| Pixel scanline gate `scanline < 240` | **刪除** | 絕對 true |
| Sprite shift gate `cx <= 256` | **刪除** | 絕對 true |
| Draw gate `cx >= 4 && cx <= 259` | **簡化** | 只留 `cx >= 4` |
| NTSC capture `cx == 260` | **刪除** | 絕不 true |
| Frame render `scanline==240 && cx==1` | **刪除** | 絕不 true |
| **剩下的 cx branch** | `cx == 1 && chrABAutoSwitch` (MMC5) / `cx < 256` (sprite-0-hit) / `cx >= 4` (draw) / `scanline==0 && cx==2` (reset) | 4 個小 cx check |

### `Ppu_Tick_Visible_SpriteFetch`（entry cx 258-319, post-inc 259-320）

| Gate | 狀態 | 原因 |
|---|---|---|
| Scroll ops | **刪除** | entry cx ∉ {256, 257, 280-304} |
| Wrap / events / odd-skip | **刪除** | 冷條件 |
| Tile fetch | **刪除** | post-inc ∉ [1,256]∪[321,336] |
| Pixel / sprite shift | **刪除** | post-inc > 256 |
| Draw gate `cx in [4,259]` | **簡化** | 只 cx==259 fires → `if (cx == 259)` |
| NTSC capture `cx == 260` | **保留** | 只 entry 259 fires |

### `Ppu_Tick_Visible_Prefetch`（entry cx 320-335, post-inc 321-336）

| Gate | 狀態 | 原因 |
|---|---|---|
| Scroll / wrap / events / odd-skip | **刪除** | |
| Tile fetch gate | **刪除**（block 一定跑）| post-inc 恆在 [321,336] |
| Pixel / sprite shift / draw / NTSC capture | **刪除** | 全部不 fire |
| MMC5 `cx == 321` check | **保留** | 只 entry 320 fires |

### `Ppu_Tick_Visible_Dummy`（entry cx 336-339）

最精簡：所有 render/draw/NTSC block 全砍，只剩 universal 每 dot 工作。

### `Ppu_Tick_VisibleLine`（通用，走 slot 256/257/340）

保持 TriCNES 原版所有 cx check，沒 bake。因為 slot 256 有 Yinc、slot 257 有 CopyHoriV、slot 340 有 wrap；3 個相對特殊的 dot 流量不夠大到值得拆，直接 fallback 通用 handler。

### `Ppu_Tick_PreRenderLine` / `Ppu_Tick_VBlankLine`（完全通用）

維持跟 TriCNES 一樣的 line-oriented 邏輯，scanline 狀態在 entry 時**已確定**（table dispatch 保證），所以部分 scanline gate 可內部 bake，但 cx gate 全保留。

---

## 5. 不變量（invariants）—— 改 handler 時必須保護

以下任何一項違反，**AccuracyCoin 或 blargg 幾乎必定回歸**：

### 5.1 mid-function `ppu_cycles_x = ++cx` 的位置

TriCNES 的 `_EmulatePPU` 在階段 #4 增加 cx，**而不是函式開頭或結尾**。這意味著：
- **階段 #1-#3 用的是 entry（pre-inc）cx**
- **階段 #5-#25 用的是 post-inc cx**

不要把 `++cx` 移到 handler 最前或最後——AC sprite-hit、odd-frame-skip 時序都靠這個中段遞增。

### 5.2 Pipeline shift (階段 #17) 必須每 dot 跑

`prevPrev... = prev...` 複製鏈 TriCNES 在 line 1724 無條件執行，**不能 gate**。PpuPhase_Dot339 特殊邏輯依賴這個。VBlank 也要跑。

### 5.3 Eval delay 兩個 block 不能合併

階段 #10 (`& 3 != 3`) 跟 #14 (`& 3 == 3`) 中間夾 PPU_DATA_Pipeline_Step(1) 和 sprite eval。TriCNES v2 的 phase-3 alignment 依賴這個切段，合併會破 AC sprite tests。**註解 line 112-116 的 `⚠️ WARNING` 千萬別忽略**。

### 5.4 `skippedPreRenderDot341` 三處互動

- **set**: `PpuPhase_DoOddFrameSkip` (preRender cx==340 NTSC 時 set true)
- **reset**: Visible PixelZone entry cx=1 + scanline=0 + rendering enabled（post-inc cx==2 當下）
- **read**: sprite mux（PixelZone）決定 active_mask、sprite shift 決定 canDecrement

Dispatch 拆完後 set/reset/read 分散在不同 handler，**任何特化時都要確認 reset 還能 fire**。第一次實驗（b0c958f）漏掉這個，7 個 sprite-hit test 壞掉。

### 5.5 Wrap 邊界狀態

`cx == 340` wrap 後 `scanline` 會變（可能變 240 VBlank、或變 0 visible、或變 preRender）。特化的 handler 若 bake `scanline` 狀態，**必須考慮 wrap 後可能已經不是原來的 state**。這是為什麼 slot 340 用通用 handler 而不特化。

---

## 6. 特化三層工具

### 6.1 Per-slot dispatch（硬特化）

Slot index 直接決定 handler。slot 實作內 `int cx = <literal>;` 讓 JIT constant-fold。

**成本**：每個特化 handler 多 ~100-200 IL bytes。
**收益**：JIT 完全消除 cx-dependent branch，最大化 register allocation。
**適用**：熱 zone（每 frame 大量 dispatch），如 PixelZone (240 × 256 = 61k dispatches/frame)。
**不適用**：冷 slot（single-dot 冷路徑已實驗過回歸）。

### 6.2 Zone-level dispatch（軟特化）

Slot 範圍一個 handler（例：SpriteFetch 涵蓋 258-319 共 62 slots）。handler 內 `int cx = ppu_cycles_x;` 是變數，但 scanline state 已是 literal。

**成本**：一個 handler 的 body。
**收益**：部分 cx gate 可 bake（`cx in X range` 永遠 true/false 的那些）；scanline gate 全 bake。
**適用**：中等熱度 zone。

### 6.3 AggressiveInlining helper

`Ppu_ActiveScanline_RenderBlock(cx)` 是唯一現存的 helper，被 PixelZone + VisibleLine + PreRenderLine 共用。

**成本**：helper 來源碼 1 份；inline 後 JIT 會產出 2-3 份機器碼（每個 call-site 一份）。
**收益**：原始碼單一來源，JIT 內聯到各 call site 時對每個 call-site 的 cx/scanline literal 都能 constant-fold。

**未驗證的擴充空間**：Entry / VSetMapper / MidActive / PipelineShift 都還是在 7 個 handler 裡各寫一份。抽成 AggressiveInlining helper 是**純 dedup、零行為改動**，理論上不動 FPS 但瘦代碼 ~200+ 行。目前沒做（1bea3d1 的狀態）。

---

## 7. 雙 runtime 編譯

- **.NET 10 (AprNesAvalonia)**：`delegate* unmanaged<void>*` + `[UnmanagedCallersOnly]`。`calli` 不觸發 GC safe-point poll，省 1-3 cycles/dispatch。
- **.NET Framework 4.8.1 (AprNes NetFx)**：`delegate*<void>*` (managed function pointer)。C# 9.0 語法 + `calli` IL，但沒有 `[UnmanagedCallersOnly]` 屬性。

AllocUnmanaged 兩邊都能用。Table 本身只 64 bytes 一張（341 × 8 × 3 = 8 KB 總），永駐 L1 D-cache。

---

## 8. TriCNES sync 流程建議

當 TriCNES 有更新時（特別是 PPU timing 修正）：

1. **找到 TriCNES 修改的行號**（`ref/TriCNES-main/Emulator.cs`）
2. **對照本文件 §3 的表格**，找到修改落在哪個階段（#1-#25）
3. **找出受影響的 AprNes handler**：
   - 如果改的是**階段 #1-#8（pre-inc 階段）**：改所有 7 個 handler 的對應段落
   - 如果改的是**階段 #9（odd skip）**：只改 VisibleLine（或 PreRenderLine，depending on scanline）
   - 如果改的是**階段 #10-#16（universal middle）**：改 7 個 handler
   - 如果改的是**階段 #17（pipeline shift）**：改 7 個 handler
   - 如果改的是**階段 #18-#20（render block）**：改 `Ppu_ActiveScanline_RenderBlock` 一處（注意 PixelZone 是 inlined 版本，要兩邊都改）
   - 如果改的是**階段 #21-#25（tail）**：改 7 個 handler
4. **驗證**：build → blargg 184/184 → AC 138/138 → benchmark vs 1bea3d1 baseline

### 維護 tip

每次 sync 前先跑 benchmark_baseline.bat 記下「sync 前的 FPS」。sync 完成後對比，如果回歸 > 1%，代表某個 handler 對照漏了或錯了。

### 未來可考慮的 refactor（沒做）

- 把 7 份 universal middle 抽成 `Ppu_MidBlock_Active()` / `Ppu_MidBlock_VBlank()` helper（**實驗過 b46af9b +1.54% FPS，但跟其他 split 綁在一起測，不能獨立確認收益**）
- 本文件 §6.3 提到的 Entry / VSetMapper / PipelineShift 三個純 dedup helper

兩者都是**降低 TriCNES sync 成本**的修改，但**尚未驗證 FPS 影響**。若將來決定做，用 `feature/ppu-refactor-dedup` 分支，完全只抽 helper 不動 dispatch，benchmark 驗證 +/- 1% 之內才 merge。

---

## 9. 歷史實驗紀錄（避免重跑同樣路）

以下是 `feature/ppu-refactor` 分支實際跑過的實驗，**不要再跑一次**：

| 實驗 | 結果 | 教訓 |
|---|---|---|
| Visible Dot256/257/340 單 dot 特化 | -1.52% FPS | 冷 single-dot（traffic < 0.9%）的 I-cache bloat > branch-removal 收益 |
| PreRender 6 zones 全特化 | -0.89% | PreRender 只 1 scanline/frame (0.4% dispatches)，特化淨負 |
| VBlank 3 zones 特化 | -0.47% | 同理 |
| SF_Dot259/260 + Prefetch_Dot321 | +1.55% (噪音) | 可能有效但需重測 |
| PixelZone_Dot1 (MMC5) | +0.57% (噪音) | 可能有效但需重測 |
| PreRender PixelZone_Dot1 + Prefetch_Dot321 | -1.01% | 冷 path 又輸一次 |
| b46af9b（Dot2/Dot3/LastDot + 6 shared helpers 一起） | +1.54% (可能是 helpers 貢獻) | helpers 跟 splits 綁著測，無法獨立驗證誰的貢獻 |

**結論**：冷路徑特化（preRender/VBlank/單 dot < 1%）幾乎都回歸。熱路徑特化（PixelZone/SpriteFetch/Prefetch）是 safe bet。

---

## 10. 快速健康檢查

TriCNES sync 完或任何改動後，跑這三個驗證：

```bash
# 編譯
powershell -NoProfile -Command "& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' 'AprNes/AprNes.csproj' /p:Configuration=Debug /p:Platform=x64"

# blargg 行為正確性（timeout 90s，卡住就代表 hang）
timeout 90 python run_tests.py -j 10

# AccuracyCoin 時序正確性（user 手動）
bash run_tests_AccuracyCoin_report.sh --no-screenshots

# FPS 對照 baseline 136.30
cmd //c "AprNes/bin/Debug/benchmark_baseline.bat"
```

若 blargg 184/184 或 AC 138/138 任一回歸，**立即 `git checkout HEAD -- AprNes/NesCore/ppu_dispatch.cs`** 恢復，不要試圖救。每次 sync 都從 clean 1bea3d1 重開。

---

## 附錄：關鍵檔案地圖

- `AprNes/NesCore/ppu_dispatch.cs` — 3 tables + 7 handlers + `Ppu_ActiveScanline_RenderBlock`
- `AprNes/NesCore/ppu_new.cs` — `ppu_step_new()` thin dispatcher + `PpuPhase2_DeferredUpdates` / `PpuPhase3_Events` / `PpuPhase4_SpriteEvalAndInit` / `PpuPhase4_SpriteFetch` / `PpuPhase4_Dot339` / `PpuPhase_FrameRender` / `PpuPhase_DoOddFrameSkip` / `PpuPhase_HandleDelayedOamCorruption` / `PpuPhase_Apply2001Mask` / `PpuPhase_Apply2001Emphasis` / `PpuPhase4_DummyNTFetch` / `PpuPhase4_VisibleScanlineDot1Init`
- `AprNes/NesCore/PPU.cs` — PPU shared state, `ComputeSpritePatternAddr`, `FlipTable`, `CIRAMAddr`, `PpuBusRead/Write`, helper ops
- `AprNes/NesCore/Main.cs` — `InitPpuDispatchTable()` 的呼叫點（標準 init 流程）
- `AprNes/NesCore/FDS.cs` — FDS 模式也呼叫 `InitPpuDispatchTable()`
- `ref/TriCNES-main/Emulator.cs` — 唯一準則，任何衝突以這個為準

---

**最後更新**：對應 branch `feature/ppu-refactor-v2` @ `1bea3d1`（base + 無後續實驗）
**FPS baseline**：136.30 FPS / +11.39% vs master（NROM / ny2011 Debug 1× Audio 0）
**驗證**：blargg 184/184 PASS, AccuracyCoin 138/138 PASS
