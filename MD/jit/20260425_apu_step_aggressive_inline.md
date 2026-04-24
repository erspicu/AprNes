# AprNes JIT + PMU — `apu_step` AggressiveInlining

- **Date**: 2026-04-25 03:40
- **Branch**: `master` + `APU.cs` 未 commit 變更
- **Change**: `apu_step()` 加上 `[MethodImpl(MethodImplOptions.AggressiveInlining)]`
- **Build**: Debug x64, .NET Framework 4.8.1
- **CPU**: AMD Ryzen 7 3700X (Zen 2, 8-core)
- **Config**: NTSC, Audio Mode 2, Ultra Analog RF, CRT, 4× resolution
- **Duration**: 30 s benchmark (NY2011)

## 1. JIT Inline 行為變化

| Method | bf51c3e | 現在 | 變化 |
|---|---|---|---|
| `apu_step` | NO (standalone, 3.5% Excl) | **YES × 4 inlines** | 成功 inline |
| `ppu_half_step_new` | 隱式 inline（0% Excl）| NO (standalone, **4.2% Excl**) | 被擠出 inline budget |
| Total inline successes | 1 740 | 1 731 | -9 |
| Total inline failures | 0 | 0 | 0 |

**JIT inline 預算 trade-off**：加上 AggressiveInlining 後 JIT 確實把 apu_step（680 IL）inline 到 4 個 call site，但代價是 `ppu_half_step_new` 失去之前隱式 inline 待遇，變成 standalone。兩者此消彼長。

## 2. CPU Exclusive 前後對比

| Method | bf51c3e Excl% | 現在 Excl% | Δ |
|---|---:|---:|---:|
| `Ppu_Tick_Visible_PixelZone` | 9.0 | **8.8** | -0.2 |
| `Run_NTSC` | 6.7 | **4.6** | **-2.1** |
| **`apu_step`** | **3.5** | — | inlined out |
| **`ppu_half_step_new`** | — | **4.2** | now standalone |
| `DemodulateRow_Core` | 11.3 | 11.8 | +0.5 |
| `PpuPhase4_VisiblePixelZone` | 2.0 | 2.0 | 0 |
| `GenerateWaveform` | 2.8 | 3.2 | +0.4 |
| **Total NesCore** | **84.9%** | **84.9%** | **0** |

**重點**：Total NesCore 完全沒變（84.9%）— apu_step inline 的工作量被重新分配到 caller，但 ppu_half_step_new 變 standalone 抵銷了。**JIT 層面零收益**。

## 3. PMU I-cache Miss Rate 變化

| Metric | bf51c3e | 現在 | Δ |
|---|---:|---:|---:|
| **Global miss rate** | **0.54%** | **0.53%** | -0.01 ✓ |
| `PixelZone` | 0.49% | 0.72% | **+0.23** ↓ |
| `Run_NTSC` | 0.45% | 0.88% | **+0.43** ↓ |
| `ppu_half_step_new` | (inlined, n/a) | 0.73% | NEW |
| `PpuPhase4_VisiblePixelZone` | 0.60% | 0.78% | +0.18 ↓ |
| `DemodulateRow_Core` | 1.13% | 0.94% | **-0.19** ↑ |
| `Visible_SpriteFetch` | 0.48% | ~0.7% | +0.2 ↓ |
| CRT `<Render>` | 0.94% | 1.03% | +0.09 |

**Global 持平**（0.54% → 0.53%），但**個別熱方法 miss rate 變動加劇**。原因跟 JIT 一樣：inline layout 重組後，有些方法的 machine code 變大（吞 L1 空間）、有些變小（釋放 L1）。個別熱方法看似退步，但全域被其他方法的改善抵銷。

## 4. FPS 驗證

`benchmark_baseline.bat`（NetFx Debug, pure-core）：

| 版本 | Run 2 | Run 3 | Avg |
|---|---:|---:|---:|
| bf51c3e | 144.22 | 144.49 | **144.36** |
| 現在 | 142.17 | 146.28 | **144.22** |

**FPS 零變化**（-0.14 在誤差內）。

## 5. 結論

`apu_step` 加 AggressiveInlining 這個改動：
- ✅ JIT 真的成功 inline 了（4 次）— attribute 有效
- ❌ JIT inline budget 被擠壓，`ppu_half_step_new` 從隱式 inline 變 standalone
- ❌ 個別熱方法 I-cache miss rate 加劇（PixelZone +0.23pp, Run_NTSC +0.43pp）
- ✓ 全域 miss rate 持平（0.54% → 0.53%）
- ✓ FPS 持平（144.36 → 144.22）
- ✓ Total NesCore CPU 持平（84.9% → 84.9%）

**Net 評估：中性變動**。沒壞處也沒好處。JIT 做了不同的 inline 決策組合，總工作量不變。

**建議**：保留或移除都可以。保留的理由是 JIT 成功接受了 hint；移除的理由是此處的 inline 沒換到 FPS / 全域 cache 的實質改善。

## 6. 觀察學到的事

此實驗展示了 **JIT inline budget** 的實際存在 — 單一 function 加 AggressiveInlining 可以**擠掉**另一個原本會被 inline 的 function。這在評估其他 `AggressiveInlining` 候選時要記住：不要把它當免費午餐，它可能會搬動其他方法的 inline 狀態。
