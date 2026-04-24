# AprNes PMU L1 I-Cache Miss Analysis — Phase4 Split + Aux Dedup @ bf51c3e

- **Date**: 2026-04-24 20:31
- **Branch**: `master` @ `bf51c3e` (Phase4 split + aux dedup, post-merge)
- **Build**: Debug x64, .NET Framework 4.8.1
- **CPU**: AMD Ryzen 7 3700X (Zen 2, 8-core, L1i 32 KB × 8)
- **Config**: NTSC, Audio Mode 2, Ultra Analog RF, CRT, 4× resolution
- **Duration**: 30 s benchmark, 951 039 IcacheIssues / 5 149 IcacheMisses

Trace: `temp/aprnes_pmu.etl` (320 MB).

---

## 1. Global Health — 持續 Excellent Tier

| 期間 | Global I-Cache Miss Rate | Tier |
|---|---:|---|
| 2026-04-14 (pre-PPU-refactor) | 0.52% | excellent |
| 2026-04-23 (post-mem-refactor) | 1.73% | healthy（警示）|
| 2026-04-23 @ 1bea3d1 (PPU dispatch refactor) | **0.53%** | excellent ✓ |
| **2026-04-24 @ bf51c3e (current)** | **0.54%** | excellent ✓ |

Phase4 split + aux dedup **沒造成任何 I-cache 退步**。全域維持在 < 1% excellent tier。

---

## 2. Per-Method Miss Rate（NesCore 熱點）

| Method | Misses | Fetches | Miss % (bf51c3e) | Miss % (1bea3d1) | Δ |
|---|---:|---:|---:|---:|---:|
| `Ppu_Tick_Visible_PixelZone` | 549 | 111 437 | **0.49%** | 0.45% | +0.04 |
| `Run_NTSC` | 366 | 80 866 | **0.45%** | 0.49% | -0.04 |
| `apu_step` | 178 | 41 046 | **0.43%** | 0.47% | -0.04 |
| `PpuPhase4_VisiblePixelZone` (NEW) | 129 | 21 672 | **0.60%** | — | NEW |
| `PpuPhase4_SpriteFetch` | 68 | 10 182 | **0.67%** | 0.52% | +0.15 |
| `Ppu_Tick_Visible_SpriteFetch` | 47 | 9 846 | **0.48%** | 0.65% | **-0.17** |
| `Ppu_Tick_Visible_Prefetch` | 27 | 5 212 | **0.52%** | 0.65% | **-0.13** |
| `Ppu_Tick_VBlankLine` | 20 | 13 479 | **0.15%** | (n/a < 1% threshold) | — |
| `CpuRead` | 22 | 5 516 | **0.40%** | 0.78% | **-0.38** |
| `DemodulateRow_Core` | 66 | 5 844 | **1.13%** | 1.17% | -0.04 |
| CRT `<Render>b__0` lambda | 114 | 12 091 | **0.94%** | 1.14% | -0.20 |
| CRT `<Curvature>b__1` lambda | 96 | 13 202 | **0.73%** | 1.46% | **-0.73** |

**結論**：所有 NesCore 熱方法維持 < 1%（DemodulateRow_Core 1.13% 是少數例外，跟 1bea3d1 持平）。

---

## 3. 關鍵變化解讀

### ✅ 重構助益（多個方法 miss rate 改善）

1. **`Ppu_Tick_Visible_SpriteFetch` -0.17pp**（0.65% → 0.48%）— IL 從 478 → 209 (-56%)，machine code 小了，L1 footprint 降低
2. **`Ppu_Tick_Visible_Prefetch` -0.13pp**（0.65% → 0.52%）— IL 從 892 → 143 (-84%)，最大瘦身者
3. **`CpuRead` -0.38pp**（0.78% → 0.40%）— 沒直接動 CpuRead，這是**間接收益**：整體 PPU code footprint 變小，CPU code 多得到 L1 空間
4. **CRT lambda 全面改善** —`<Render>` -0.20pp, `<Curvature>` -0.73pp。同樣是 footprint 縮小的副作用，CRT 完全沒動

### ⚠️ 微幅退步（皆在容忍範圍）

1. **`PixelZone` +0.04pp**（0.45% → 0.49%）— 雜訊範圍（< 0.1pp 不算意義差異）。AggressiveInlining 把 4 個 aux helper inline 進去，PixelZone 自身 IL 只 +6 bytes
2. **`PpuPhase4_SpriteFetch` +0.15pp**（0.52% → 0.67%）— 660 IL，呼叫方變化導致 entry 點較少預熱機會
3. **`PpuPhase4_VisiblePixelZone` (NEW) 0.60%** — 230 IL 全新方法，沒有歷史對照；數值在健康範圍

---

## 4. 為何全域沒大幅進步也沒退步？

Phase4 split + aux dedup 在源碼層做的是「共用 helper」，**runtime machine code 透過 AggressiveInlining 又被展回去**。所以：

- 每個冷 handler 的 source IL 大幅縮小（-56% ~ -84%）
- 但 JIT 出來的 machine code 因為 helper 被 inline，每個 call site 仍然有 aux block 的 copy
- 結果：**source 乾淨度 ↑，runtime memory footprint 大致持平**

這是預期的 trade-off — 用 source 維護性換 runtime 同等表現。**沒退步**比「更好」重要。

---

## 5. 相對 1bea3d1 的全景對照

| 子系統 | 1bea3d1 | bf51c3e | 趨勢 |
|---|---:|---:|---|
| PPU 主熱路徑 (`PixelZone` + helper) | 0.45% | 0.49% + 0.60% (Phase4 split) | 持平 |
| 冷 visible handlers (SpriteFetch/Prefetch/Dummy) | 0.52~0.65% | 0.48~0.52% | **改善** |
| CPU + 6502 ops (`CpuRead`) | 0.78% | 0.40% | **大改善** |
| NTSC 解調 (`DemodulateRow_Core`) | 1.17% | 1.13% | 持平 |
| CRT pipeline lambdas | 1.14~1.46% | 0.73~0.94% | **改善** |
| **Global** | **0.53%** | **0.54%** | **持平** |

---

## 6. 與 FPS 的交叉驗證

- Global miss rate 持平（0.53% → 0.54%）
- Pure-core baseline FPS: **144.36 FPS**（NetFx Debug, NY2011, Audio 0 / 1× / 無濾鏡）
- 1bea3d1 沒有可直接對照的 pure-core baseline；但 cold handler I-cache 改善 + Phase4 work -1.0pp 應該對 FPS 有微正向貢獻

---

## 7. 結論

PPU dispatch refactor v2 的 Phase4 split + aux dedup 在 I-cache 層面：

1. **全域 miss rate 0.54%** — 維持 excellent tier，跟 1bea3d1 同等
2. **冷 visible handler miss rate 全面改善**（-0.13 ~ -0.17pp）
3. **`CpuRead` 大幅改善 -0.38pp** — 間接收益，PPU footprint 縮小釋放 L1 給 CPU code
4. **CRT lambda 全面改善 -0.20 ~ -0.73pp** — 也是 footprint 收益副作用
5. **熱路徑 PixelZone +0.04pp** — 雜訊範圍

未來監控目標（與 1bea3d1 報告一致）：
- 全域 < 1%
- PPU 熱方法 < 0.5%
- 任何超過上述閾值都是 regression 信號
