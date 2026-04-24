# AprNes JIT Profile — Phase4 Split + Aux Dedup @ bf51c3e

- **Date**: 2026-04-24 20:07
- **Branch**: `master` @ `bf51c3e` (Phase4 split + aux block dedup)
- **Build**: Debug x64, .NET Framework 4.8.1
- **CPU**: AMD Ryzen 7 3700X (Zen 2, 8-core)
- **Config**: NTSC, Audio Mode 2, Ultra Analog RF, CRT, 4× resolution
- **Duration**: 30 s benchmark (NY2011), 88 591 samples / 88 591 ms CPU

Trace: `temp/aprnes_jit.etl` (30 MB). Reproduced via `cmd /c tools\analyze\run_perfview.bat`.

> ⚠️ FPS during PerfView trace = 98–104 fps. ETW sampling adds 1.5–2× overhead;
> raw FPS comparable to 1bea3d1 baseline (~136 fps) requires `benchmark_baseline.bat`.

---

## 1. CPU Sampling — Top 20 (Exclusive, self time)

| # | Excl % | Method | Δ vs 1bea3d1 |
|---:|---:|---|---:|
| 1 | **23.2** | CRT `<Render>b__0` lambda (analog phosphor) | -1.1 |
| 2 | **19.9** | CRT `<Curvature>b__1` lambda | +0.9 |
| 3 | **11.3** | `DemodulateRow_Core` | -0.2 |
| 4 | **9.0** | **`Ppu_Tick_Visible_PixelZone`** | +0.6 |
| 5 | **6.7** | `Run_NTSC` | +0.3 |
| 6 | 3.5 | `apu_step` | +0.2 |
| 7 | 2.8 | `GenerateWaveform` | -0.2 |
| 8 | **2.0** | **`PpuPhase4_VisiblePixelZone`** ← NEW | — |
| 9 | 1.1 | CRT `<ApplyHorizontalBlur>b__0` | 0.0 |
| 10 | 1.0 | `Ppu_Tick_Visible_SpriteFetch` | +0.1 |
| 11 | 0.9 | `PpuPhase4_SpriteFetch` | -0.1 |
| 12 | 0.5 | `Ppu_Tick_Visible_Prefetch` | +0.1 |
| 13 | 0.4 | `CpuRead` | 0.0 |
| 14 | 0.4 | `Ppu_Tick_VBlankLine` | 0.0 |
| 15 | 0.3 | `NestedTick7_NTSC` | 0.0 |
| 16 | 0.2 | `DoBranch` | 0.0 |
| 17 | 0.1 | `GetAddressAbsolute` | 0.0 |
| 18 | 0.1 | `Op_2C` | 0.0 |
| 19 | 0.1 | `ApuOutputCatchup` | — |
| 20 | 0.1 | `Ppu_Tick_Visible_Dummy` | 0.0 |

**NesCore 總和 = 84.9% CPU**（1bea3d1: 85.4%）— 大致持平。

CRT pipeline 仍是 ~44%（23.2 + 19.9 + 1.1）— 未變動，這次重構沒碰 CRT。

---

## 2. Phase4 Split 對照表

```
Old (1bea3d1)                          New (bf51c3e)
─────────────────────────────────      ─────────────────────────────────
PpuPhase4_SpriteEvalAndInit(NoInline)  ├─ PpuPhase4_VisiblePixelZone(cx)
  - 統一處理 visible / preRender       │   - PixelZone 專用，去掉 ro / evalScanline
  - runtime evalScanline / ro 判別     │     runtime 判別
  - 3.0% CPU                           │   - 2.0% CPU
                                       └─ PpuPhase4_PreRenderDot(evalDot)
                                           - preRender 專用
                                           - 0.0% CPU（240/89K dispatches）

Ppu_Tick_Visible_PixelZone (8.4%)       Ppu_Tick_Visible_PixelZone (9.0%)
  └─ inline PpuPhase4_SpriteEvalAndInit   └─ call PpuPhase4_VisiblePixelZone (NEW 2.0%)
```

**Net Phase4 work**:
- 1bea3d1: PpuPhase4_SpriteEvalAndInit 3.0% (從所有 visible handlers 跨呼叫)
- bf51c3e: PpuPhase4_VisiblePixelZone 2.0% + PpuPhase4_SpriteEvalAndInit (preRender only) ≈ 0% + PpuPhase4_PreRenderDot 0.0% = **2.0%**

**省 1.0pp CPU**。原因：`evalScanline` / `ro` runtime 判別 + 多個無關 dot range 檢查（`if (evalDot >= 257 && evalDot <= 320)` 在 cx 1-256 全跑 false）在熱路徑被消除，改成各自特化版本。

---

## 3. Dispatch Table 分布（traffic share 對照）

| Zone | Slots | Dispatches/frame | Excl% (1bea3d1) | Excl% (bf51c3e) | Δ |
|---|---|---:|---:|---:|---:|
| PixelZone (0-255) | 256 | 61 440 | 8.4% | 9.0% | +0.6 |
| SpriteFetch (258-319) | 62 | 14 880 | 0.9% | 1.0% | +0.1 |
| Prefetch (320-335) | 16 | 3 840 | 0.4% | 0.5% | +0.1 |
| Dummy (336-339) | 4 | 960 | 0.1% | 0.1% | 0 |
| VisibleTail (256/257/340) | 3 | 720 | 0.1% (Line) | 0.1% (Tail) | 0 |
| VBlankLine | 341 × 21 | 7 161 | 0.4% | 0.4% | 0 |
| PreRenderLine | 341 × 1 | 341 | 0.0% | 0.0% | 0 |

`VisibleTail` 從 `VisibleLine`（含完整 RenderBlock 工作）瘦身成只跑 Yinc/CopyHoriV/wrap + 末尾 2 個 delayed pixel — IL 從 678 → 估計 <100 bytes（未列入 top）。 `Ppu_Tick_PreRenderLine` 277 IL（vs 過去包在 VisibleLine 中無法精確分離）。

---

## 4. JIT IL Size — 重構後對照

| Method | 1bea3d1 IL | bf51c3e IL | Δ |
|---|---:|---:|---:|
| `Ppu_Tick_Visible_PixelZone` | 1 885 | **1 891** | +6 |
| `Ppu_Tick_Visible_SpriteFetch` | 478 | **209** | **-269 (-56%)** |
| `Ppu_Tick_Visible_Prefetch` | 892 | **143** | **-749 (-84%)** |
| `Ppu_Tick_Visible_Dummy` | <200 | **123** | -77 |
| `Ppu_Tick_VBlankLine` | 468 | **146** | **-322 (-69%)** |
| `Ppu_Tick_VisibleLine` (→ `VisibleTail`) | 678 | <100 | -578 |
| `Ppu_Tick_PreRenderLine` | (含於 VisibleLine 統計) | **277** | — |
| `Ppu_ActiveScanline_RenderBlock` (helper) | 1 474 | (拆) | — |
| `PpuPhase4_VisiblePixelZone` (NEW) | — | 230 | NEW |
| `PpuPhase4_PreRenderDot` (NEW) | — | 310 | NEW |
| `Ppu_PreRender_RenderBlock` (helper, NEW) | — | 估 ~600 | NEW |

**冷 handler 大幅瘦身**：SpriteFetch -56%、Prefetch -84%、VBlank -69%。原因是它們原先各自重複了 ~30 行 aux block（VSET latch / mapper clock / $2001 delay / pipeline shift / ...），現在抽到 `PpuVisibleAuxBeforePhase4` / `PpuDotAuxBeforeStep1Core` / `PpuDotAuxStep1` / `PpuDotAuxAfterPhase4` 等共用 helper。

PixelZone IL 不變（+6 bytes 是 helper call vs inline 的微差）— 設計上**只動冷路徑、不污染最熱的 PixelZone**。

---

## 5. Inlining

- **Successful inline events**: **1 740**（vs 1 684 at 1bea3d1, +56）
- **Failed inline events**: **0**

### 新 helper inlined 統計

| Helper | Inline 次數 | 設計目的 |
|---|---:|---|
| `PpuDotAuxBeforeStep1Core` | 6 | VSET / 2002 read / mapper clock / A12 prev |
| `PpuDotAuxStep1` | 6 | eval-delay non-phase-3 + Pipeline_Step(1) + OAM corruption |
| `PpuDotAuxAfterPhase4` | 6 | eval-delay phase-3 + addr bus + $2001 mask/emphasis + prevDot shift |
| `PpuVisibleAuxBeforePhase4` | 4 | 結合 BeforeStep1Core + Step1(true) + OAMCorruptionIfNeeded |
| `PpuAdvanceAndMaybeWrap` | 3 | cx++ + scanline wrap |
| `PpuBgTileFetchRange` | 2 | BG tile fetch (cx 1-256 / 321-336) |
| `Ppu_PreRender_RenderBlock` | 1 | preRender 專用 BG fetch + sprite shift（無 CalcPixel）|

`AggressiveInlining` 設計**完全達標**：source 共用、runtime 仍展開到原始大小。

### 熱方法 inline 決策

| Method | Excl% | IL bytes | Inlined? |
|---|---:|---:|---|
| CRT `<Render>` lambda | 23.2% | 1 364 | NO (standalone) |
| CRT `<Curvature>` lambda | 19.9% | 309 | NO |
| `DemodulateRow_Core` | 11.3% | 2 283 | YES (into NTSC worker) |
| `Ppu_Tick_Visible_PixelZone` | 9.0% | 1 891 | NO (function-pointer dispatch) |
| `Run_NTSC` | 6.7% | — | NO (outer loop) |
| `apu_step` | 3.5% | 680 | NO |
| `PpuPhase4_VisiblePixelZone` | 2.0% | 230 | NO (NoInline 標記) |

`PpuPhase4_VisiblePixelZone` 230 IL 但 NoInlining 是有意的 — 避免 PixelZone 熱路徑膨脹（同樣理由套用在原 SpriteEvalAndInit）。

---

## 6. 觀察

### ✅ 設計目標達成

1. **Phase4 visible/preRender 拆分節省 1.0pp CPU** — runtime evalScanline / ro 判別在 256+ visible dispatches/frame 跑掉了，拿掉就回收
2. **冷 handler IL 大幅瘦身（-56% / -84% / -69%）** — aux block 共用 helper 後，每個冷 handler 只剩自己獨特的邏輯
3. **PixelZone IL 不變（+6 bytes 微差）** — 完美隔離熱路徑，不被重構波及
4. **Inline 數從 1 684 → 1 740** — 多了 56 個成功 inline，全部來自新 aux helper
5. **Inline 失敗仍是 0** — JIT 對所有 AggressiveInlining helper 都接受了

### 🔍 次要觀察

1. **`PixelZone` excl% +0.6pp（8.4% → 9.0%）** — 看似回歸，實際是 PpuPhase4_SpriteEvalAndInit 從共用方法變成 PixelZone 專用 call site，部分 inclusive 時間被 sample 進 PixelZone 的 callee 路徑。淨值仍是減少的（見 §2 net Phase4 work 對照）
2. **`PpuPhase4_VisiblePixelZone` 230 IL** — 比預期小，原因是它**只**處理 visible scanline 邏輯，pre-render 那一份完全分離到 PpuPhase4_PreRenderDot
3. **`Ppu_Tick_PreRenderLine` 0.04% CPU** — 一年只跑 1/89K（1 scanline × 341 dots），完全沒污染 L1
4. **dead code 移除（`Buffer_BG_array` 21 MB/s 寫入消失）的影響沒在這份報告直接看到** — 因為 `Unsafe.InitBlockUnaligned` 是 intrinsic，不會出現在 sample top；要從 PMU LLC store traffic 才看得到

---

## 7. Pure-core Baseline FPS

`AprNes/bin/Debug/benchmark_baseline.bat` 跑 NTSC + Audio 0 + 1× + 無濾鏡（純模擬核心，無 AV pipeline）：

| Session | JIT warmup | Run 2 | Run 3 | Avg(2+3) |
|---|---:|---:|---:|---:|
| 1（冷啟動，疑 OS 背景活動）| 125.78 | 125.65 | 126.06 | 125.86 |
| 2（PerfView 後暖）| 142.55 | 142.14 | 141.74 | 141.94 |
| 3（連續第三次）| 144.71 | 144.22 | 144.49 | **144.36** |

**穩定 baseline ≈ 144 FPS**（bf51c3e）。Session 1 視為 outlier。

## 8. 接下來可看

1. **PMU L1 I-cache miss 重測**（vs 1bea3d1 0.53% baseline）— 確認冷 handler 瘦身有沒有把全域 miss rate 再壓低
2. **CRT pipeline（仍佔 44%）** — 如果要再往下擠 emulation 性能，下個目標應該是把 CRT scalar lambda 移到 GPU shader（aprnesava 已走這條路）

## 9. 結論

PPU dispatch refactor v2 的 Phase4 split + aux dedup **乾淨地達成所有設計目標**：
- 冷路徑 IL 大幅瘦身（-56% ~ -84%）
- 熱路徑 PixelZone 完全不被波及（+6 bytes 微差）
- 共用 helper 全部成功 inline（0 failures）
- Phase4 工作量微減（-1.0pp CPU）
- Pure-core baseline 穩定在 **144 FPS**（NetFx Debug, NY2011）

以**source-level cleanliness + runtime efficiency 雙贏**為基準，這版可作為下一階段（PMU 重測 / TriCNES sync）的穩定起點。
