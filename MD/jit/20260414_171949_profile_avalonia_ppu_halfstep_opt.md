# AprNesAvalonia Release — JIT Profile (PPU half-step opt)

- **Date**: 2026-04-14 17:19
- **Change**: `ppu_half_step_new` — `else if` mutex on commit flags,
  unsigned range tricks, `byte atRaw` elimination
- **Config**: NTSC, 4x Ultra Analog RF, CRT, ny2011
- **Tests**: 184/184 PASS (zero regression)
- **Profile FPS**: 77.38 (2322 frames / 30.01s)
- **Samples**: 64,030

---

## Before/After (4x profile)

| Method | Before | **After** | Δ |
|--------|--------|-----------|---|
| `Crt_Render` lambda | 24.2% | 23.9% | −0.3pp |
| `Parallel.ForWorker` inner | 16.6% | 17.0% | +0.4pp |
| **`ppu_step_new`** | **16.6%** | **15.7%** | **−0.9pp** |
| `DemodulateRow_Core` | 8.5% | 8.8% | +0.3pp |
| **`Run_NTSC`** | **6.0%** | **5.4%** | **−0.6pp** |
| `PpuPhase4_SpriteEvalAndInit` | 3.3% | 3.2% | −0.1pp |
| `ApplyHorizontalBlur` lambda | 2.6% | 2.6% | 0 |
| `apu_step` | 2.3% | 2.3% | 0 |
| **Profile FPS** | **74.67** | **77.38** | **+3.6%** |

Emulation core (ppu_step_new + Run_NTSC) exclusive: **22.6% → 21.1%** (−1.5pp).
CRT pipeline relative share unchanged (~52% of CPU).

---

## 解讀

- **PPU 核心確有降低**：ppu_half_step_new 被 inline 進 ppu_step_new，
  `else if` 短路 + range magic 把 4 個獨立 bool 檢查與範圍比對合併，
  讓 RyuJIT 產生更精簡的 x86 碼
- **FPS 提升 +3.6% 超出純 emulation 比例**：emulation 下降 1.5pp 單看
  應只換到 ~2% FPS；多出的部分來自**執行時間縮短讓 CRT 執行緒更
  有機會平行**（原本 emulation 佔滿 main thread，可能阻擋 CRT
  Parallel worker 的調度）
- **Zero regression**：全 184 blargg 測試通過，硬體語意 100% 保留
- **風險低**：`else if` 對應硬體 fetch 排程嚴格序列，不會多吃或漏吃 flag

---

## 結論

合理的微優化。原本預估 +0.6 FPS，實測 +2.7 FPS，比預期好（估計是
間接讓 CRT 平行性更充分）。投報比意外地高。
