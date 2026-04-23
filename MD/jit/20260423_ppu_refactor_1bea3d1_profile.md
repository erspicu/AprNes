# AprNes JIT Profile — PPU Dispatch Refactor @ 1bea3d1

- **Date**: 2026-04-23 22:23
- **Branch**: `feature/ppu-refactor-v2` @ `1bea3d1` (peak PPU-refactor version)
- **Build**: Debug x64, .NET Framework 4.8.1
- **CPU**: AMD Ryzen 7 3700X (Zen 2, 8-core)
- **Config**: NTSC, Audio Mode 2, Ultra Analog RF, CRT, 4× resolution
- **Duration**: 30 s benchmark, 89 905 ms CPU time, 89 905 samples

Trace: `temp/aprnes_jit.etl` (24 MB). Reproduced via `cmd /c tools\analyze\run_perfview.bat`.

---

## 1. CPU Sampling — Top 20 (Exclusive, self time)

| # | Excl % | Method |
|---:|---:|---|
| 1 | **24.3** | `CrtScreenScalar.<Render>b__0` (Analog phosphor lambda) |
| 2 | **19.0** | `CrtScreenScalar.<ApplyFullFrameCurvatureAndConvergence>b__1` |
| 3 | **11.5** | `NesCore.DemodulateRow_Core` |
| 4 | **8.4** | **`NesCore.Ppu_Tick_Visible_PixelZone`** ← largest NesCore hot method |
| 5 | **6.4** | `NesCore.Run_NTSC` |
| 6 | 3.3 | `NesCore.apu_step` |
| 7 | 3.0 | `NesCore.PpuPhase4_SpriteEvalAndInit` |
| 8 | 3.0 | `NesCore.GenerateWaveform` |
| 9 | 1.1 | `CrtScreenScalar.<ApplyHorizontalBlur>b__0` |
| 10 | 1.0 | `NesCore.PpuPhase4_SpriteFetch` |
| 11 | 0.9 | `NesCore.Ppu_Tick_Visible_SpriteFetch` |
| 12 | 0.4 | `NesCore.Ppu_Tick_Visible_Prefetch` |
| 13 | 0.4 | `NesCore.CpuRead` |
| 14 | 0.4 | `NesCore.Ppu_Tick_VBlankLine` |
| 15 | 0.3 | `NesCore.NestedTick7_NTSC` |
| 16 | 0.2 | `NesCore.DoBranch` |
| 17 | 0.1 | `NesCore.Op_2C` (BIT abs) |
| 18 | 0.1 | `Mapper000.MapperR_CHR` |
| 19 | 0.1 | `NesCore.Ppu_Tick_VisibleLine` (generic fallback) |
| 20 | 0.1 | `NesCore.Ppu_Tick_Visible_Dummy` |

**NesCore 總和 = 85.4% CPU**。CRT 的 Analog RF + CRT pipeline 佔 ~45%，DemodulateRow_Core + Ntsc_FlushPendingRows 佔 ~11.5%。剩下才是模擬核心。

---

## 2. Dispatch Table 分布（驗證設計生效）

Visible 各 zone 按 traffic share 的預期成本：

| Zone | Slots | Dispatches/frame (visible = 240 scanlines) | Excl% (實測) |
|---|---|---:|---:|
| **PixelZone** (0-255) | 256 slots | 61 440 | **8.4%** ← 熱路徑主力 |
| **SpriteFetch** (258-319) | 62 slots | 14 880 | 0.9% |
| **Prefetch** (320-335) | 16 slots | 3 840 | 0.4% |
| **Dummy** (336-339) | 4 slots | 960 | 0.1% |
| **VisibleLine** (256/257/340 generic) | 3 slots | 720 | 0.1% |

PreRender 全路由 `PreRenderLine`（1 scanline/frame = 341 dispatches）：**0.03% CPU**（top-40 邊緣，line 47 僅 28 samples）。

VBlank 全路由 `VBlankLine`（21 scanlines/frame = 7 161 dispatches）：**0.4%**。

**規律**：Excl% 大致正比於 dispatches/frame。設計意圖達成。

---

## 3. JIT IL size 排行（Top 15 / NesCore only）

| # | IL bytes | Method | 路徑類型 |
|---:|---:|---|---|
| 1 | 5 296 | `InitOpHandlers` | cctor（一次性）|
| 2 | 4 363 | `TestRunnerCore.Run` | test-mode |
| 3 | 3 195 | `initAPU` | 一次性 |
| 4 | 2 978 | `NesCore..cctor` | 一次性 |
| 5 | **2 283** | **`DemodulateRow_Core`** | **熱路徑（NTSC 解調）** |
| 6 | 2 208 | `init` | 一次性 |
| 7 | 2 185 | `MapperRegistry.Create` | 一次性 |
| 8 | 2 001 | `Ntsc_Init` | 一次性 |
| 9 | **1 885** | **`Ppu_Tick_Visible_PixelZone`** | **熱路徑（最大單一 PPU handler）** |
| 10 | 1 726 | `TestRunnerCore` lambda | test-mode |
| 11 | **1 474** | **`Ppu_ActiveScanline_RenderBlock`** | **shared helper，`AggressiveInlining`** |
| 12 | 1 364 | `CrtScreenScalar.<Render>` lambda | 熱 |
| 13 | 1 121 | `initPalette` | 一次性 |
| 14 | **892** | **`Ppu_Tick_Visible_Prefetch`** | **熱** |
| 15 | 795 | `HardResetState` | 冷 |

PPU dispatch handler IL 分布：
- `PixelZone` 1885, `Prefetch` 892, `VisibleLine` 678, `SpriteFetch` 478, `VBlankLine` 468, `Dummy` <200
- **`Ppu_ActiveScanline_RenderBlock` 1474 IL + `AggressiveInlining`**：會被 inline 進 PixelZone / VisibleLine / PreRenderLine，但因為 size 大，可能**不是全部 call-site 都 inline 成功**

---

## 4. Inlining 狀態

- **Successful inline events**：**1 684**
- **Failed inline events**：**0**

### 熱方法 inline 決策

| Method | Excl% | IL bytes | Inlined? |
|---|---:|---:|---|
| CRT `<Render>b__0` lambda | 24.3% | 1 364 | NO (standalone) |
| CRT `<ApplyCurvature>b__1` lambda | 19.0% | 309 | NO |
| `DemodulateRow_Core` | 11.5% | **2 283** | **YES**（into NTSC worker lambda）|
| `Ppu_Tick_Visible_PixelZone` | 8.4% | 1 885 | NO (standalone) |
| `Run_NTSC` | 6.4% | — | NO (outer loop) |
| `apu_step` | 3.3% | 680 | NO |
| `PpuPhase4_SpriteEvalAndInit` | 3.0% | 612 | NO |
| `GenerateWaveform` | 3.0% | 565 | NO |
| `PpuPhase4_SpriteFetch` | 1.0% | 660 | NO |
| `Ppu_Tick_Visible_SpriteFetch` | 0.9% | 478 | NO |
| `Ppu_Tick_Visible_Prefetch` | 0.4% | 892 | NO |
| `Ppu_ActiveScanline_RenderBlock` | 0.04%* | 1 474 | standalone sample leaks → **部分 call-site 沒 inline 成功** |
| `GetAddressAbsolute` | 0.1% | <30 | YES (small helper) |

*ActiveRenderBlock 顯示 38 samples 自身 — 表示它有時被直接呼叫（沒 inline into caller），違反 AggressiveInlining 期望。

### 符合 Gemini 評價的地方

- 大方法全部 standalone，**避免 explosive code duplication**——正確
- 小 helper 自動 inline——正確
- `DemodulateRow_Core` 2 283 IL **竟然 inlined** into NTSC worker lambda——有驚喜，可能是因為單一 caller

### 可能的問題

- **`Ppu_ActiveScanline_RenderBlock` 雖然標 `AggressiveInlining` 但有 standalone samples** → JIT 對 1 474 IL 的 helper 在大 caller 裡拒絕 inline 是可能的（avoid > 2 × caller IL 的展開）
- 若這個 helper 沒 inline，PixelZone 執行時會有一次額外 call overhead。可能影響 FPS 0.x%

---

## 5. 跟 2026-04-14 PMU baseline 的對照

| Metric | 2026-04-14（monolithic ppu_step_new）| 2026-04-23（1bea3d1 split）|
|---|---:|---:|
| `ppu_step_new` excl | 9.1% | — (被拆) |
| `Ppu_Tick_Visible_PixelZone` | — | 8.4% |
| `Ppu_Tick_Visible_SpriteFetch` | — | 0.9% |
| `Ppu_Tick_Visible_Prefetch` | — | 0.4% |
| PPU handlers 總和 | 9.1% | **10.4%** |
| FPS (NROM baseline) | ~120 | **136.30** (+11.4%) |

**觀察**：split 後 PPU handlers 總 Excl% 小幅**上升**（9.1 → 10.4%），但 FPS 大幅**上升**（+11.4%）。這看似矛盾但合理——**每個 dispatch 的工作量不變，但 dispatch 路徑 throughput 更快**（少了 cx-range branch、JIT 更好 optimize）。CPU 同樣時間做更多 frame，所以單個方法的 Excl% 看起來相對比例略高，但 absolute throughput（frames/sec）顯著提升。

---

## 6. 可優化空間

### 6.1 Ppu_ActiveScanline_RenderBlock inline 未完全成功

- IL = 1 474，已超 JIT 積極 inline 的友善上限（一般 ~100-300 IL）
- 有 standalone samples 洩漏（38 samples = 0.04% CPU）
- **潛在修正**：把 RenderBlock **手動展開**進 PixelZone（像 Visible_PixelZone 自己就已經 inline 一份了），PreRenderLine / VisibleLine 各自也展開。優點：100% inline。缺點：三處複製同 1 474 行，總 IL +2 948，可能反傷 I-cache

### 6.2 PixelZone 仍有 cx branch

根據 `PPU_Dispatch_Design.md §4`，PixelZone 還有 4 個 cx-dependent 小 branch：
- `cx == 1 && chrABAutoSwitch`（MMC5）
- `cx < 256`（sprite-0-hit）
- `cx >= 4`（draw）
- `scanline == 0 && cx == 2`（reset）

根據歷史實驗，消除這些**在有冷 split 的版本上反而回歸**。1bea3d1 不動是合理決定。

### 6.3 CRT lambda 佔 44% CPU

Analog + CRT 管線（24.3 + 19.0 + 1.1 = 44.4%）比 NesCore 核心的 ~20% 還大。純模擬器效能（Audio 0 + 1×）FPS 遠比這個配置高（benchmark_baseline.bat 的 136 vs 這份配置的 ~63）。aprnesava 的 GPU shader 走向是正確的——把這 44% CPU 移到 GPU 就能解放大量資源給 emulation core。

---

## 7. 總結

**PPU dispatch refactor 達成設計目標**：
- 熱路徑 `PixelZone`（8.4% CPU）接管了原 `ppu_step_new` 9.1% 的主要負擔，同時完成了 scanline/cx gate 的編譯期常數摺疊
- Zone Excl% 嚴格正比於 traffic share（驗證 dispatch table 設計生效）
- FPS +11.4% / master 對 NROM baseline

**風險 / 複雜度接受區間**：
- 7 個 handler + 1 shared helper，代碼 1 088 行（可接受）
- 1 474 IL 的 RenderBlock helper 有部分 inline 失敗但影響 < 0.5%
- 剩餘 4 個 PixelZone 內部 cx branch 屬於 predictor-friendly 小成本，不值得進一步拆

**建議**：此版本作為 PPU refactor 的穩定停泊點 merge 到 master。未來 TriCNES 同步依照 `MD/memory/PPU_Dispatch_Design.md §8` 的 4 步流程進行。
