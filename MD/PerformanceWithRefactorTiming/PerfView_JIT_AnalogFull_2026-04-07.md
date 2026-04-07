# PerfView JIT & Inlining Analysis — Analog Full Mode (8x / Ultra / RF / CRT / DSP Mode 2)

- **日期**: 2026-04-07
- **Branch**: feature/performance-optimization @ 7517c96
- **Build**: Debug x64, .NET Framework 4.8.1
- **ROM**: ny2011.nes (Mapper 0)
- **Config**: NTSC / Ultra Analog / RF Output / CRT / 8x / Audio DSP Mode 2
- **Benchmark**: 56.53 FPS best-of-3 (vs 60.10 realtime = **-6%**)
- **PerfView run**: 40s, 52.06 FPS (PerfView overhead)
- **ETL**: `temp/aprnes_jit.etl`

---

## 1. Native Code Size — Top 30

| # | Method | Native Size | 子系統 |
|---|--------|-------------|--------|
| 1 | **apu_step** | **6,685 bytes** (6.5KB) | APU |
| 2 | **ppu_step_new** | **6,355 bytes** (6.2KB) | PPU |
| 3 | **PpuPhase4_SpriteEvalAndInit** | **5,986 bytes** (5.8KB) | PPU |
| 4 | .cctor | 5,821 bytes | Init |
| 5 | initAPU | 4,522 bytes | Init |
| 6 | InitOpHandlers | 4,373 bytes | Init |
| 7 | **GenerateWaveform** | **3,639 bytes** (3.6KB) | **NTSC Analog** |
| 8 | **DemodulateRow** | **3,083 bytes** (3.0KB) | **NTSC Analog** |
| 9 | init | 2,904 bytes | Init |
| 10 | **Ntsc_Init** | **2,222 bytes** | **NTSC Analog** |
| 11 | ppu_r_2002 | 1,706 bytes | PPU |
| 12 | init_function | 1,699 bytes | Init |
| 13 | Process2007StateMachine | 1,683 bytes | PPU |
| 14 | run | 1,634 bytes | Main |
| 15 | IO_write | 1,495 bytes | IO |
| 16 | initPalette | 1,135 bytes | Init |
| 17 | Op_00 (BRK) | 1,090 bytes | CPU |
| 18 | HardResetState | 983 bytes | Init |
| 19 | **Crt_Render** | **765 bytes** | **CRT** |
| 20 | GetAddressIndOffY | 747 bytes | CPU |
| 21 | **UpdateColorTemp** | **720 bytes** | **CRT** |

### I-Cache Working Set (hot-path)

| 子系統 | 方法 | Size |
|--------|------|------|
| Core | ppu_step_new + apu_step + PpuPhase4 + run | 20.8KB |
| **Analog** | **GenerateWaveform + DemodulateRow** | **6.7KB** |
| **CRT** | **Crt_Render + UpdateColorTemp** | **1.5KB** |
| **合計** | | **~29.0KB** |

**接近 L1 I-Cache 上限 (32KB)**。Analog mode 增加了 ~9KB 的 hot native code，壓縮了 I-Cache 餘裕從 12KB 降到 ~3KB。

---

## 2. Inlining 分析 — Analog/CRT/DSP 相關

### 成功 Inline ✅

| Method | 被 Inline 進 | 說明 |
|--------|-------------|------|
| **DemodulateRow** | DecodeScanline | NTSC 解調（每掃描線） |
| **GenerateWaveform** | — | NTSC 波形產生 |
| **Crt_Render** | RenderScreen | CRT 後處理 |
| **ProcessPixelScalar** | Crt_Render | 逐像素 CRT 處理 |
| **ProcessPixelVector** | Crt_Render | 向量化 CRT 處理 |
| **ProcessRowMask_SWAR** | Crt_Render | SWAR 遮罩處理 |
| **ProcessRowPhosphor_SWAR** | Crt_Render | SWAR 磷光處理 |
| **ProcessRowMaskPhosphor_SWAR** | Crt_Render | SWAR 合併處理 |
| **ProcessRowConvergence** | Crt_Render | 收斂處理 |
| **RunWaveformLoop** | GenerateWaveform | 波形迴圈核心 |
| Ntsc_Init | init | NTSC 初始化 |
| Ntsc_ApplyConfig | — | NTSC 配置 |
| Crt_ApplyConfig | — | CRT 配置 |
| Ntsc_SetFrameCount | ppu_step_new | 幀計數 |
| Crt_SetFrameCount | ppu_step_new | 幀計數 |
| **AudioPlus_PushApuCycle** | apu_step | DSP 音效推送 |
| authMix_GetVoltage | apu_step | 混音計算 |
| cmf_Process | generateSample | 濾波器 |
| mmix_PushChannels / TryGetStereoSample | apu_step | 混音管線 |
| ose_PushSample / TryGetSample / Convolve | apu_step | 過取樣引擎 |
| mfx_ProcessSample | apu_step | 後處理效果 |

### 失敗 Inline ❌ (Analog/CRT/DSP 相關)

| Method | 失敗原因 | 說明 |
|--------|----------|------|
| **DecodeScanline** | unprofitable | NTSC 掃描線解碼（每掃描線呼叫，但 JIT 判定太大） |
| DecodeScanline_Fast | — | 快速模式解碼 |
| DecodeScanline_Physical | — | 物理模式解碼 |
| DemodulateRow_SVideo | — | S-Video 解調 |
| GenerateWaveform_SVideo | — | S-Video 波形 |
| **Ntsc_Init** | too many il bytes | NTSC 初始化（僅啟動時） |
| **Crt_Init** | — | CRT 初始化（僅啟動時） |
| ApplyFullFrameCurvatureAndConvergence | — | CRT 曲面（每幀） |
| ApplyHorizontalBlur | — | CRT 水平模糊（每幀） |
| ResampleH_Bilinear | — | 縮放（每幀） |
| VerticalFillRows | — | 垂直填充（每幀） |
| PrecomputeScanlineWeights | — | 掃描線權重（配置變更時） |
| UpdateColorTemp | — | 色溫更新 |
| UpdateGammaLUT | — | Gamma LUT |
| UpdateIQMode | — | IQ 模式 |
| ComputeHann | — | Hann 窗函數 |

### Core 方法 Inline 狀態（與 Baseline 相同）

| Method | Inline | 原因 |
|--------|--------|------|
| ppu_step_new → caller | ❌ | too many il bytes |
| ppu_half_step_new → ppu_step_new | ✅ | 成功 |
| PpuPhase2/3/4 | ❌ (預期) | [NoInlining] |
| apu_step → caller | ❌ | too many il bytes |
| ApuFrameCounterStep | ❌ (預期) | [NoInlining] |
| clockdmc | ❌ (預期) | [NoInlining] |
| cpu_step_one_cycle → caller | ❌ | too many il bytes |
| MasterClockTick → run | ✅ | AggressiveInlining |

---

## 3. Analog vs Baseline 比較

| 指標 | Baseline (1x/Digital) | Analog Full (8x/RF/CRT/DSP2) |
|------|----------------------|-------------------------------|
| JIT 方法數 | ~140 | **~202** (+44%) |
| Hot working set | 20.1KB | **~29.0KB** (+44%) |
| L1 I-Cache 餘裕 | 12KB | **~3KB** |
| Best FPS | 104.31 | **56.53** (-46%) |
| GenerateWaveform | N/A | 3.6KB |
| DemodulateRow | N/A | 3.0KB |
| Crt_Render | N/A | 765 bytes |
| DSP audio methods | N/A | ~20+ methods inline 進 apu_step |

### I-Cache 瓶頸分析

Analog mode 的 hot-path 增加了 NTSC decode (GenerateWaveform + DemodulateRow = 6.7KB) 和 CRT render pipeline。合計 working set 接近 32KB L1 I-Cache 上限。

**這意味著 Analog mode 下的效能瓶頸主要是 I-Cache 壓力，而非計算量。** 進一步優化應聚焦：
1. 縮減 GenerateWaveform / DemodulateRow 的 native code size
2. 確保 CRT render 的 pixel processing 方法保持小巧（目前已成功 inline）
3. DSP 音效管線已全部 inline 進 apu_step，無進一步空間

---

## 4. 優化建議（Analog Full 模式）

| 優先級 | 方向 | 預期效果 |
|--------|------|----------|
| 高 | GenerateWaveform 拆分冷路徑 | 縮減 3.6KB native size → 改善 I-Cache |
| 高 | DemodulateRow 拆分 | 縮減 3.0KB → 改善 I-Cache |
| 中 | 8x upscale 用 SIMD (AVX2) | 大幅加速像素處理 |
| 中 | CRT per-frame effects 平行化 | 利用多核 |
| 低 | 遷移 .NET 8/10 | 更好的 JIT + SIMD intrinsics |
