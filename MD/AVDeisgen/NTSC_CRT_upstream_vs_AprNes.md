# NTSC + CRT 實作比較：LMP88959 NTSC-CRT (upstream) vs AprNes

**研究日期**: 2026-04-30
**範圍**: 純 read-only 程式碼分析；不修改任何一邊的程式碼
**對照路徑**:
- Upstream: `C:\ai_project\AprNes\temp\NTSC-CRT\` （EMMIR / LMP88959, 2018-2023, v2.3.2）
- 我們: `C:\ai_project\AprNes\AprNes\NesCore\NTSC_CRT\` （AprNes / AprNesAvalonia 共用）

兩邊都做同一件事：把 NES PPU 輸出（或泛用 RGB）變成「像 1980-90 年代電視在訊號劣化下顯示」的畫面。但走的路、目的、平台、效能策略完全不同。下面把所有差異拆開列出來，方便日後決定要不要 port 哪些功能、或者反過來把我們的東西貢獻回 upstream。

---

## 1. TL;DR — 兩邊體質一覽

| 維度 | Upstream NTSC-CRT | AprNes (Ntsc.cs + CrtScreen.\*.cs) |
|------|-------------------|------------------------------------|
| 語言 / 平台 | C89, 跨平台 (Linux/macOS/Windows) | C# .NET Framework 4.8.1 + .NET 10, Windows 中心 |
| 數值精度 | **整數定點全程** (`signed char` analog, 14-bit sin/cos, EXP_P=11 fixed-point) | **`float` 為主 + 部分定點 16.16** (e.g. `ResampleH_Bilinear` 用 fixed-point；YIQ→RGB 走 `gammaLUT[4096]` 12-bit 定點查表) |
| 執行緒 | **嚴格 single-threaded** (專案守則) | **`Parallel.For`** 240 列同時 demod；CRT post 也分列 parallel |
| SIMD | **明確禁止** (專案守則 #5) | `Vector<T>` (Vector256/AVX2)，第三方再多一條 `Avx2.GatherVector256` SIMD path |
| 取樣率 | NES: `CRT_HRES = 2273*4/10 ≈ 909`/line；NTSC: `2275*4/10 ≈ 910`/line | `kOutW = 1024` (預設) 或 **HD_NTSC `2048`** (12× Fsc oversample, .NET 10 only) |
| 取樣 per dot | NES dot ≈ 909/256 ≈ 3.55 sample | `kSampDot = 4` (1024) 或 `8` (HD_NTSC) — 整除、SIMD-friendly |
| FIR 解碼 | `EQF` 三段 IIR filter (low-pass) 走 4-stage cascade (`fL[4]/fH[4]`) | **Hann window FIR**，`kWinY=6, kWinI=18, kWinQ=54`（HD: 12/36/108）；I/Q 模式可切 1953 vs 1960s symmetric |
| CRT post 效果 | **scanlines + bloom + blend** (3 個 knob) | scanlines + bloom + shadow-mask/aperture-grille + curvature + phosphor decay + convergence + vignette + interlace jitter + horizontal beam spread (~9 個 knob) |
| 分發模型 | C lib：`crt_init / crt_modulate / crt_demodulate / crt_draw` 全域 struct API | `partial class NesCore` static fields + `Crt_Init / Crt_ApplyConfig / Crt_Render` + 後端 `Crt_SetBackend(Scalar/Simd/Gpu)` |
| GPU 支援 | 無 | `CrtScreen.Gpu.cs` SkSL runtime effect (D3D11/Metal/GL via SkiaSharp) |
| LOC（核心）| crt_core 666 + crt_nes 310 + crt_ntsc 331 = ~1300 行 (整個 lib 含 main 約 2300) | Ntsc 1129 + CrtScreen 624 + Simd 1005 + Gpu 203 + Shared 156 = **3117 行** |
| 特殊 NES 功能 | dot-skip-on-odd-frame, NES-specific HBI timing, border colour, 9-bit emphasis | dot crawl phase via `scanPhase6 / scanPhaseBase`, RF herring (聲音→畫面波紋), color-burst jitter, RF/AV/SVideo 三套 profile |

---

## 2. Pipeline 架構走查

兩邊邏輯流程都是：

```
raw RGB / NES palette → NTSC composite signal → (noise/blur) → demod → YIQ → RGB → CRT post → screen
```

但每一步的「資料形態」差異極大。

### 2.1 編碼端 (modulate)

**Upstream (`crt_nes.c:106` `crt_modulate`)**
- 輸入：`unsigned short data[]` 9-bit NES pixel（或 6-bit 不含 emphasis）。
- 對每個 dot 跑 `square_sample(p, phase + 0..3)` 4 次，把 NES 顏色當「方波」加總，得出 IRE 整數電壓。寫入 `signed char analog[CRT_INPUT_SIZE]`，整條訊號就是一條 `signed char` 一維陣列（每行 909 sample × 262 行）。
- `setup_field()` 一次性把垂直/水平 sync 寫進 `analog[]`，往後只覆寫 active video 部分（`crt_nes.c:81-104`）。
- 三條 dot crawl phase 用 `phasetab[CRT_CC_VPER] = { 0, 4, 8 }` 直接指定（`crt_nes.c:116`，CRT_CC_VPER=3 是 NES 特有的 3-line 重複樣式）。

**Ours (`Ntsc.cs:553` `GenerateSignal` 走 fast 路徑；`Ntsc.cs:753` `GenerateWaveform` 走 ultra-analog 路徑)**
- 兩條編碼路徑：
  - **Fast / `_Fast` 路徑** — 跳過完整波形重建，每個 dot 直接從 `yBaseE/iBaseE/qBaseE[64*8]` 預先積分好的 (Y, I, Q) 三元組讀取（`Ntsc.cs:559`）。免去全條 909-sample analog buffer，直接得到 256 個 dot 的解調結果。極快但不模擬 LTI bandlimit。
  - **Physical / `_Physical` 路徑（ultraAnalog=true）** — 對每個 dot × `kSampDot` 組合查 `waveTable[64 * kPhaseEntries * kSampDot]` 預算波形，套 `emphAtten`、herring、xorshift noise，再走 single-pole low-pass `vPrev += vVel * ringDamp + (x - vPrev) * SlewRate`（`Ntsc.cs:873`）。這條才是「真的把訊號繞一遍 LTI 系統」。
- 訊號格式：`float waveBuf[kBufLen]`（kBufLen = 30+1024+30 = 1084 或 HD 雙倍），float 全程，`kLeadPad=30` 兩端做 LTI filter 暖機 padding。
- Dot crawl phase：兩個 counter，`scanPhase6`（fast 用）和 `scanPhaseBase`（physical 用），每 scanline `+= kPhaseStepLine`，HD_NTSC 模式自動 ×2 維持物理一致（`Ntsc.cs:99-110`）。

**結論**：Upstream 是「把每行訊號完整算出來變成 1 byte/sample 的整數陣列」；我們的 fast 路徑是「跳過訊號生成、直接給解調結果」，physical 路徑才是和 upstream 同一層級但精度更高（float + 預計算 LUT + LTI filter）。

### 2.2 解碼端 (demodulate)

**Upstream (`crt_core.c:291` `crt_demodulate`)**
- 整段 `crt_demodulate` 一次跑完 noise→VSYNC→HSYNC→color-burst integration→I/Q wave reconstruction→FIR EQ→YIQ→RGB→寫入 `out[]`，每行 `for (line = CRT_TOP; line < CRT_BOT; line++)`。
- 色彩 burst 偵測用 `ccr[CRT_CC_VPER][CRT_CC_SAMPLES]` 累積取值（`crt_core.c:462-467`）：
  ```c
  ccr[i % CRT_CC_SAMPLES] = p + n;  /* 7/8 prev + 1/8 new */
  ```
  這是訊號式 IIR，會自己 lock 到 burst 相位。
- I/Q 解調：對 4-sample-per-cycle 模式，靠 `wave[0..3]` 4 個固定常數（cos/sin 取 4 點），乘上每一個 sample 後丟進 `eqf()` 三段 IIR low-pass。
- YIQ→RGB 是純整數矩陣（`crt_core.c:573-575`）：
  ```c
  r = (((y + 3879 * i + 2556 * q) >> 12) * v->contrast) >> 8;
  ```

**Ours (`Ntsc.cs:976` `DemodulateRow` → `DemodulateRow_Core`)**
- 完全不偵測 sync — 我們直接知道每個 dot/scanline 的位置（emulator 已經給了精確的 PPU 時序），所以省掉 HSYNC/VSYNC search loop。
- 兩條解調策略：
  - **Composite**：色度從 luma waveBuf 同一條訊號取出，靠 Hann FIR 過濾 (`combinedI / combinedQ` 預算 `hann[n] * cos/sin[(ph+n) % kPhaseEntries]`)，視窗 `kWinI=18 / kWinQ=54`（asymmetric 1953）或 18/18（symmetric 1960s，由 `SymmetricIQ` flag 切換）。
  - **S-Video**：色度從另一條乾淨的 `cBuf` 取出，沒有 luma-chroma cross-talk。
- 內層用 `Vector<float>` SIMD `Vector.MultiplyAddEstimate`（.NET 10 → vfmadd231）做 dot product。
- Y 用更短的 Hann window `kWinY=6` 直接展開：
  ```csharp
  yAcc = hannY[0]*wvY[0] + hannY[1]*wvY[1] + ... + hannY[5]*wvY[5];
  ```
  這是 6-tap symmetric FIR，等效於 boxcar low-pass（`Ntsc.cs:1036`）。
- YIQ→RGB + Gamma：`YiqToRgb()` 走 `gammaLUT[4096]` 12-bit fixed-point 查表（`Ntsc.cs:1122-1128`）；SIMD 路徑直接 inline FMA + `Vector.MultiplyAddEstimate(vGC, R, v1_minus_GC)` 算 gamma。

**結論**：Upstream 的解碼是「完整 RF receiver 模擬」（必須處理 sync drift、burst lock、noise）；我們是「我已經知道每個 dot 在哪、phase 是多少，所以直接卷 FIR 算 I/Q」，是更接近「離散信號處理」而非「類比接收機」的視角。

### 2.3 CRT 後處理 (post-process)

**Upstream**：post 幾乎不存在。`crt_core.c` 在 demod 結尾才做兩件事：
1. `if (v->scanlines)` — 跳過 `end - v->scanlines` 行，留黑（`crt_core.c:662`）。
2. `if (v->blend)` — 和上一格的 pixel 做 50/50 blend（`crt_core.c:584-608`）。
3. （optional） `CRT_DO_BLOOM=1`：靠 `prev_e` 累積能量，調整 `line_w` 改變每行有效寬度。NES mode 是 disable（`crt_core.h:70` 註解 "does not work for NES"）。

**Ours**：`CrtScreen.cs` Render() 是一個獨立的 stage 2，吃 `linearBuffer[3 plane × 1024 × 240]` float RGB，輸出 `crt_analogScreenBuf[Crt_DstW × Crt_DstH]`。流程：
1. `PrecomputeScanlineWeights()` — 算每列 scanline gauss 權重（基於 BeamSigma + interlace jitter, `CrtScreen.cs:95-137`）。
2. `ApplyHorizontalBlur()` — 3-tap source-pixel-space horizontal beam spread（SIMD, `CrtScreen.cs:192-247`）。
3. 主 Parallel.For 列迴圈（`CrtScreen.cs:289`）：upscale + ProcessPixel（含 brightness boost + bloom + gamma + clamp）。
4. `ProcessRowMask_SWAR` / `ProcessRowMaskPhosphor_SWAR` — shadow mask + phosphor decay（SWAR 32-bit pack, `CrtScreen.cs:452-522`）。
5. `ProcessRowConvergence` — R/G/B 水平偏移模擬電子槍未對齊（fixed-point 16.16 累積, `CrtScreen.cs:524-544`）。
6. `ApplyFullFrameCurvatureAndConvergence` — 桶形變形 + convergence（`CrtScreen.cs:546-623`）。

**結論**：Upstream 沒有真正的 CRT post-process，所有「電視感」都是 demod 內部 FIR 軟邊 + scanline + blend。我們把 demod 和 CRT post 完全切開（Stage 1 NTSC demod 寫 linearBuffer，Stage 2 CRT post 從 linearBuffer 算 final pixel），多了非常多 monitor-side 的可調 knob。

---

## 3. NES 專屬處理

### 3.1 Palette → 訊號

| 主題 | Upstream | Ours |
|------|----------|------|
| 顏色定義入口 | `crt_nes.c:21` `square_sample(p, phase)` 動態算每個 sub-sample 的 IRE 值 | `Ntsc.cs:266-274` 一次性算 `yBase/iBase/qBase[64]`；再 `yBaseE/iBaseE/qBaseE[64*8]` 積分含 emphasis |
| Emphasis 處理 | 9-bit pixel：`(p & 0x700)` 三個 emphasis bit 在 `square_sample` 內 mask `active[6]` 表決定哪幾個 phase tier 該被衰減 | 預先把所有 emphasis 0..7 各算一次 LUT，runtime 完全免重算（`Ntsc.cs:294-326`） |
| 黑色處理 | `crt_nes.c:47` 直接 hardcode `if (hue >= 0x0e) return 0` | `Ntsc.cs:270` `if (color == 0) lo = hi; else if (color == 0x0D) hi = lo; else if (color > 0x0D) lo = hi = 0f` |
| IRE 表 | `crt_nes.c:26-35` 16 個 entry, signed int, raw mV 換算後 ×1024 | 隱式 — `loLevels/hiLevels` 4-entry 浮點表 `{-0.12, 0.00, 0.31, 0.72}` / `{0.40, 0.68, 1.00, 1.00}`（`Ntsc.cs:217, 225`） |

兩邊公式來源都是 [NESdev wiki - Brightness Levels](https://www.nesdev.org/wiki/NTSC_video#Brightness_Levels)，但 upstream 走原始 mV 整數，我們走「歸一化到 ±1 的浮點」這條路。

### 3.2 NES 特殊時序

| 主題 | Upstream | Ours |
|------|----------|------|
| Dot-skip-on-odd-frame | 由 caller 控制 `s->xoffset` 偏移；upstream 本身只接受 sample-space x offset | 沒有顯式處理 — 因為我們的 emulator 直接管理 PPU 時序，dot crawl 由 `scanPhaseBase` 進位攜帶 |
| NES-specific HBI | `crt_nes.h:71-104` 把 341 PPU px 拆成 9/25/4/15/5/1/15/256/11，sync_separator 寫到 line ≥ 259 | 完全沒有 — 我們不模擬 HBI/sync。`HbiSimulation` flag 只是「是否在 leftPad 給 left-edge filter ring 一個假的 zero」（`Ntsc.cs:776`） |
| 三行重複的 dot crawl | `CRT_CC_VPER = 3`，phasetab `{0, 4, 8}` (`crt_nes.c:116`) | `kSampDot=4` × 3 行迴圈隱含同樣的 12-phase 重複；NES 物理上 master = 6×Fsc，所以 1 line = 1364 master cycles，1364 mod 6 = 2，3 行才回到 phase 0 |
| Border color | `NES_BORDER` 在 NES_OPTIMIZED 路徑 disable 掉 (`crt_nes.c:69`)，但 macros 還在 | 完全沒有 — 我們的 emulator 自己處理 overscan |

### 3.3 Color burst 與 jitter

- Upstream 的 color burst 是真的寫進 analog `signed char`，再透過 `crt_demodulate` 中那段 `ccr[]` IIR 把它從訊號中找回來。
- 我們完全跳過 color burst — `phase0` 直接從 `scanPhase6` 拿，等於「我已經知道 burst 在哪，不用解」。
- 只有 ultra-analog + RF + `ColorBurstJitter` 開時，會在 `Ntsc.cs:730-734` 偶然（1/32）±1 master tick nudge phase0，模擬訊號劣化造成的相位漂移。

---

## 4. CRT Post 效果矩陣

| 效果 | Upstream | Ours (Scalar/SIMD) | Ours (GPU) | 備註 |
|------|----------|---------------------|------------|------|
| Scanline gap | ✓ 跳行留黑 (`crt_core.c:662`) | ✓ Gauss 權重每列 (`PrecomputeScanlineWeights`) | ✓ uScanlineStrength uniform | upstream 是 binary on/off；我們是漸層 |
| Beam Gauss bloom | ✓ 但 NES disable (`CRT_DO_BLOOM`) | ✓ `BloomStrength` × 列亮度，`bright * constB` | ✓ uBloomStrength | upstream 改寬度模擬，我們是亮度提升 |
| Horizontal beam spread (3-tap blur) | ✗ | ✓ `ApplyHorizontalBlur`, SIMD 8-pixel | ✓ uHBlurAlpha uniform | 我們特有 |
| Shadow mask (RGB stripe / 蜂窩) | ✗ | ✓ `ProcessRowMask_SWAR`, 兩種 mode (ApertureGrille / ShadowMask) | ✓ uMaskType (0=none,1=AG,2=SM) | upstream 完全沒有 |
| Curvature (桶形變形) | ✗ | ✓ `ApplyFullFrameCurvatureAndConvergence`, 預算 `_curvMap[]` reverse map | ✓ uCurvature | 我們特有，map 帶 cache |
| Phosphor decay (frame N + N-1 max) | ✗ | ✓ `ProcessRowPhosphor_SWAR`, max blend with `_prevFrame` | ✓ uPhosphorDecay (ping-pong SKSurface) | 我們特有 |
| Convergence (R/G/B 水平偏移) | ✗ | ✓ `ProcessRowConvergence`, fixed-point 16.16 | ✓ uConvergence | 我們特有 |
| Vignette (邊角壓暗) | ✗ | ✓ 嵌在 `_boostRow[ty]` 中：`bb * (1 - vs4 * vy * vy)` | ✓ uVignetteStrength | 我們特有 |
| Interlace jitter (隔行 sub-pixel 抖動) | 跨欄位的 even/odd phase 切換在 NTSC 模式有 (`crt_ntsc.c:217-228`) | ✓ `InterlaceJitter` flag → `±0.25f` Y offset | (uniforms 有，shader 看版本) | 行為不一樣：upstream 是訊號層的 even/odd field，我們是 monitor-side scanline jitter |
| Dot crawl | ✓ 真的從 `CRT_CC_VPER=3` phase 重複展現 | ✓ 由 `scanPhase6` carry 自然產生 | ✓ 同 SIMD | 兩邊都會 |
| Frame blend (每幀 50/50 blend) | ✓ `crt->blend = 1` (`crt_core.c:584-608`) | ✗ 我們改用 phosphor decay 模擬持續性，沒有 50/50 blend | ✗ | upstream 算是 phosphor decay 的簡單版 |
| Signal noise injection | ✓ `crt_demodulate(noise)` 直接加 `(rn>>16 & 0xff - 0x7f) * noise / 256` 到 inp | ✓ `NoiseIntensity` × xorshift（profile-driven，RF=0.04, AV=0.003, SVideo=0） | (shader 沒有，noise 在 NTSC 階段加了) | 兩邊都做 |
| Monochrome toggle (`as_color=0`) | ✓ `crt_ntsc.c:184-187` 把 ccmodI/Q/burst 全部 memset 0 | ✗ 沒有開關，但 `SVideo` profile 因為 `ChromaBlur` 高 + emphAtten 行為類似 | ✗ | upstream 特有 |
| Raw artifact-color image input (`raw=1`) | ✓ `crt_ntsc.c:148-172` 跳過 luma 縮放，直接餵原圖 | ✗ | ✗ | upstream 特有，用來解 dithered B/W → 彩色 |
| VHS-style 底部噪音抖動 | ✓ `crt_ntscvhs.c` 專門模式 | ✗ | ✗ | upstream 特有 |
| RF herringbone (聲音→畫面) | ✗ | ✓ ultra-analog + RF + `RfAudioLevel` 驅動 1.31683 rad/dot 旋轉複數 (`Ntsc.cs:577-587, 759-770`) | (大概沒實作) | 我們特有；對應現實 RF 訊號音訊洩漏進畫面 |
| Color burst phase jitter | ✗ | ✓ `ColorBurstJitter` 1/32 機率 ±1 master-tick nudge (`Ntsc.cs:730-734`) | ✗ | 我們特有 |

---

## 5. 效能策略

### Upstream
- 嚴格 single-thread。專案守則第 6 條「Single threaded」就直接寫死。
- 嚴格 integer-only。`signed char` analog buffer + 14-bit interpolated sin/cos table (`crt_core.c:19-40`) + `EXP_P=11` fixed-point 算 `expx()` (`crt_ntsc.c:32-83`)。
- 嚴格無 SIMD（守則第 5 條）。
- README 顯式提到「L. Spiro AVX accelerated version」存在於 BeesNES 分支，等於告訴讀者「想加速？fork 出去做」。
- 換言之：upstream 的設計目標是「最大可移植性」，不是最大效能。任何能用 SIMD 加速的迴圈在這邊都是故意不做。

### Ours
三層後端，runtime dispatch（`CrtScreen.Shared.cs:31`）：

1. **Scalar (CrtScreen.cs)** — 預設 net48 build；主要靠 `Vector<T>` 自動 SIMD（已經很多手動展開），`Parallel.For` 240 列分散到 thread pool。
2. **Simd (CrtScreen.Simd.cs)** — .NET 10 + AVX2 explicit (`Vector256<T>`, `Avx2.GatherVector256`, `Vector.MultiplyAddEstimate`, `[SkipLocalsInit]`)。
3. **Gpu (CrtScreen.Gpu.cs)** — SkiaSharp `SKRuntimeEffect` 跑 SkSL shader，可走 D3D11/Metal/GL；目前 raster SKSurface 為主，phase 3 計畫直接 lease Avalonia 的 GPU canvas。

NTSC demod 端 (`Ntsc.cs`) 沒有後端切換，但內建多套手段：
- **HD_NTSC compile-time switch** — `kOutW` 1024 vs 2048，`kSampDot` 4 vs 8，`kPhaseEntries` 6 vs 12。透過 `kSampleRateScale = 0.5f` 把 IIR coef 對應縮，維持物理一致。
- **Generic struct dispatch** (`Scale2/4/6/8`) — JIT 對每個 `analogSize` 特化，`int d = x / N` 變 compile-time const divide，N=power-of-2 變 shift（`Ntsc.cs:23-27, 591-598`）。
- **Code splitting** — `addNoise / herring` 兩個 bool 拆 4 條 branch-hoisted JIT-specialized 路徑（`Ntsc.cs:780-783`）。
- **Branchless modular wrap** — 用符號位元擴展取代 `if`：
  ```csharp
  ph += kPhaseStepOutPx + (((kThreshOutPx - ph) >> 31) & kPhaseWrap);
  ```
  整支檔案到處都是這招（`Ntsc.cs:637, 884, 970...`）。
- **Per-thread scratch via `[ThreadStatic]`** — 取代每幀 stackalloc，省 ~720 個 stackalloc/frame（`Ntsc.cs:459-463`, `CrtScreen.cs:49`）。

**對比定性結論**：Upstream 的數學完全可以 SIMD 化（BeesNES 的 AVX 版證明了），但作者選擇「演算法正確 + 可移植」優先；我們是另一個極端，最大化 .NET 10 + AVX2 + .NET TieredPGO 的效能潛力，代價是程式碼量 ~2.4× 多、Windows-centric、需要 unsafe pointer。

---

## 6. API / 整合方式

### Upstream 用法（C lib，全域 struct）

```c
#include "crt_core.h"

static struct CRT crt;
static struct NTSC_SETTINGS ntsc;

/* init */
crt_init(&crt, screen_width, screen_height, CRT_PIX_FORMAT_BGRA, screen_buffer);
crt.blend = 1;
crt.scanlines = 1;

/* per frame */
ntsc.data = video_buffer;       /* unsigned short[] for NES, unsigned char[] for NTSC */
ntsc.format = CRT_PIX_FORMAT_BGRA;  /* not for NES (NES has fixed format) */
ntsc.w = video_width;
ntsc.h = video_height;
ntsc.as_color = color;
ntsc.field = field & 1;
ntsc.raw = raw;
ntsc.hue = hue;
if (ntsc.field == 0) ntsc.frame ^= 1;
crt_modulate(&crt, &ntsc);
crt_demodulate(&crt, noise);
field ^= 1;
```

整合一個新「系統」 = 在 `crt_core.h` 加一個 `CRT_SYSTEM_*` enum + 寫一份 `crt_<sys>.h/c` 帶 timing 常數和 `crt_modulate`，Done（README §"Writing a port for a certain system"）。

### Ours 用法（partial class static API）

```csharp
// init
NesCore.Crt_SetBackend(NesCore.CrtBackend.Simd);  // or Scalar / Gpu
NesCore.Ntsc_Init();
NesCore.Crt_Init();
NesCore.Ntsc_ApplyConfig(
    analogOutput: (int)AnalogOutputMode.AV,
    ultraAnalog: true,
    analogSize: 4,
    crtEnabled: true,
    analogScreenBuf: screenBuf);
NesCore.Crt_ApplyConfig(
    analogOutput: (int)AnalogOutputMode.AV,
    analogSize: 4,
    analogScreenBuf: screenBuf);

// per scanline (called from PPU at sl × cx==260)
NesCore.Ntsc_CaptureScanline(sl, emphasisBits);

// per frame end
NesCore.Ntsc_FlushPendingRows();   // parallel demod 240 rows → linearBuffer
NesCore.Crt_Render();              // CRT post → analogScreenBuf
```

整合方式完全是「寫進我們的 partial class、共用所有 NesCore static state、走 `[ThreadStatic]` per-worker scratch」。沒有可拔插性，但反過來代表整合零阻抗：`palBuf = ntsc_rowPalettes + sl * 256` 是 PPU 寫的 buffer 直接拿來用，不需要 copy。

---

## 7. Upstream 有、我們沒有的東西（潛在 port / 靈感）

| 功能 | Upstream 位置 | 為什麼可能值得抄 |
|------|---------------|------------------|
| **Monochrome toggle (`as_color=0`)** | `crt_ntsc.c:184-187` | 一行 memset，可給 user 一個「看 B/W 老電視」選項 |
| **Raw image / artifact color input** | `crt_ntsc.c:148-172` `s->raw` 路徑 | 拿來解 dithered B/W → 出彩色（rainbow.png 那種藝術），是個有趣的 demo 模式 |
| **VHS mode** | `crt_ntscvhs.c` 整檔 + `CRT_VHS_NOISE` | 三段 bandwidth (SP/LP/EP) + 底部 noise 條紋，給「翻錄錄影帶」觀感 |
| **Programmable hue offset (整體 `crt.hue`)** | `crt_core.c:318-321` | 我們有 `iPhase[]/qPhase[]` table 但沒有 user-tunable 整體 hue rotation |
| **真的 VSYNC/HSYNC search** | `crt_core.c:369-397` (VSYNC), `:434-451` (HSYNC) | 我們現在「emu 知道一切」所以省掉了 — 但如果想做「訊號從外部餵進來」（e.g. Avalonia capture 別的視窗），就得補 |
| **Interlaced even/odd field** | `crt_ntsc.c:197-200, 217-228` | NTSC 模式有真正的 field-alternating equalizing pulses；我們的 `InterlaceJitter` 是 monitor side 的視覺 hack，而非訊號層 |
| **Signal noise (`crt_demodulate(noise)` 整體 knob)** | `crt_core.c:362` 一個 user-facing noise scalar | 我們有 `RF_NoiseIntensity / AV_NoiseIntensity / SV_NoiseIntensity` 但綁在 profile，缺 user override |
| **3-band EQ (USE_CONVOLUTION=0 path)** | `crt_core.c:158-233` `EQF` 結構 | 我們的 Hann window FIR 是固定形狀；upstream 的 IIR 3-band 可在 runtime 改 gain，視訊感不同。倒不見得要抄，但理解 trade-off 有用 |
| **Bloom 改 line width 而非 line brightness** | `crt_core.c:399-402, 512-526` | 模擬「亮場景下行掃描整條變寬」的物理現象，我們是用亮度 boost 近似 |

## 8. 我們有、Upstream 沒有的東西

| 功能 | 我們的位置 |
|------|-----------|
| **HD_NTSC 12× Fsc oversampling (2048 sample/scanline)** | `Ntsc.cs:73-85` 整段 `#if HD_NTSC` ladder — `kPhaseEntries=12`、Hann window 也對應放大 12/36/108 |
| **完整 CRT post-process pipeline (scanline+mask+curvature+phosphor+convergence+vignette)** | `CrtScreen.cs` 全檔 |
| **GPU SkSL shader path** | `CrtScreen.Gpu.cs` + `crt_core_v1.sksl` (referenced) |
| **Runtime backend dispatch (Scalar/Simd/Gpu)** | `CrtScreen.Shared.cs:31-61` |
| **三套 terminal profile (RF/AV/SVideo)** | `CrtScreen.Shared.cs:81-94` (Beam / Bloom / Brightness) + `Ntsc.cs:129-138` (Noise / Slew / ChromaBlur) |
| **RF herringbone (audio-driven visual buzz)** | `Ntsc.cs:577-587, 759-770` — `RfAudioLevel * 0.06f * sin(line/240+phase)` 驅動 |
| **Color burst jitter (相位漂移)** | `Ntsc.cs:730-734`，1/32 機率 ±1 master-tick |
| **Symmetric vs asymmetric I/Q (1953 vs 1960s NTSC standard)** | `Ntsc.cs:210, 374-384` `SymmetricIQ` flag |
| **Color temperature warm/cool** | `Ntsc.cs:194-203, 338-350` `ColorTempR/G/B` 三軸 multiplier |
| **JIT generic-struct specialization for `analogSize`** | `Ntsc.cs:23-27, 591-598` `Scale2/4/6/8` 介面 |
| **`[ThreadStatic]` per-worker scratch (avoid per-frame stackalloc)** | `Ntsc.cs:459-463`, `CrtScreen.cs:49-56` |
| **Parallel demod (240 rows in worker pool)** | `Ntsc.cs:509-522` `Ntsc_FlushPendingRows` |
| **Pre-integrated palette LUT (yBaseE/iBaseE/qBaseE for 64×8)** | `Ntsc.cs:311-326` 跳過 runtime sub-sample summation |
| **Branchless modular wrap (`(threshold - x) >> 31) & wrap`)** | `Ntsc.cs` 整支檔案 |
| **SWAR 32-bit pixel processing (mask/phosphor/convergence)** | `CrtScreen.cs:452-544` |

---

## 9. 最終評估

兩邊**目標不同**，沒有「誰比較好」這種比較：

- **Upstream 贏在**：可移植性（C89 + integer-only 跨平台）、簡單性（總共 ~1300 LOC 核心，整個 lib 4 個 `.c` 就能讀完）、自包含（無依賴）、訊號模擬完整性（真的有 sync detection / VHS mode / monochrome）。**它是一個 library**。
- **我們贏在**：解碼精度（HD_NTSC 12× oversampling、Hann FIR、SIMD FMA）、效能（parallel + SIMD + GPU 三層）、CRT post 效果豐富度（9+ 個獨立 knob vs upstream 的 ~3 個）、emulator 整合零阻抗（`partial class NesCore` 直接共享 PPU buffer）。**它是一個 emulator 內建 stage**。

**何時該用哪一邊**：
- 想做一個 generic NTSC filter （e.g. video effect plugin、其他 emulator 想要一個能 plug 的選項）→ 用 upstream，整套 1300 LOC 包好就 ship。
- 想做一個 NES 模擬器專用、能跟 PPU 共享 buffer、能跑 GPU shader、能在 RF/AV/SVideo 之間切的 high-end 訊號鏈 → 走我們這條路。

**互相借鑑的方向**：
1. 我們可以從 upstream 拿 `monochrome toggle` 和 `raw artifact-color path`（小工作量、有趣）。
2. 我們可以參考 upstream 的「bloom 改 line width」做為 `BloomStrength` 的另一種模型。
3. 反過來，upstream 從 BeesNES 已經 fork 了 AVX 版；如果 EMMIR 想要進一步多執行緒 / SkSL GPU path，我們的 `Ntsc.cs` parallel demod / `CrtScreen.Gpu.cs` 都是現成的設計範本。

**值得記在心裡的兩條原則差異**：

| 原則 | Upstream | Ours |
|------|----------|------|
| 訊號 vs 解析 | 把訊號完整算出來再解碼，CRT 模擬整個收訊鏈 | 在我們這端「我已經知道每個 dot 在哪 + phase 是多少」，省掉 sync detection，把資源花在更精細的 demod (Hann FIR + HD oversample) |
| 抽象 vs 整合 | 嚴格的 black-box library，多系統共用 `crt_core` | partial class、static field、unsafe pointer，跟 NesCore 緊綁 |

兩種選擇都對，只是不同的工程美學。

---

## 附錄 A：關鍵檔案行號索引（方便日後跳轉）

### Upstream
- `crt_core.h:30-56` — system enum + include dispatch
- `crt_core.h:74-92` — `struct CRT` 主控結構
- `crt_core.c:101-147` — convolution-based EQ (USE_CONVOLUTION=1)
- `crt_core.c:158-233` — IIR 3-band EQ (default)
- `crt_core.c:264-289` — `crt_init` + EQ setup
- `crt_core.c:291-666` — `crt_demodulate` 全境（VSYNC/HSYNC/burst/I-Q wave/EQ/YIQ→RGB/blend）
- `crt_nes.c:21-61` — `square_sample`, NES IRE 表
- `crt_nes.c:82-104` — `setup_field` (vertical sync 一次性寫入)
- `crt_nes.c:106-201` — `crt_modulate` NES_OPTIMIZED 路徑
- `crt_nes.h:65-104` — NES 行 timing 註解 + 常數
- `crt_ntsc.c:32-83` — fixed-point `expx()`
- `crt_ntsc.c:90-126` — IIR low-pass for bandlimit
- `crt_ntsc.c:128-330` — `crt_modulate` NTSC 路徑（含 even/odd field equalizing pulses）
- `crt_ntscvhs.h:102-124` — VHS SP/LP/EP bandwidth modes

### Ours
- `Ntsc.cs:73-85` — HD_NTSC compile switch
- `Ntsc.cs:96-123` — phase step constants (HD scaling)
- `Ntsc.cs:212-336` — `Ntsc_Init` 全部 LUT 預算（64×8 yBaseE/iBaseE/qBaseE、emphAtten、combinedI/Q）
- `Ntsc.cs:490-522` — `Ntsc_CaptureScanline` + `Ntsc_FlushPendingRows`（PPU thread snapshot + parallel demod 入口）
- `Ntsc.cs:535-561` — `DecodeScanline_Fast` (skip-waveform fast path)
- `Ntsc.cs:563-711` — `DecodeAV_Composite` / `DecodeAV_SVideo` + `DispatchDecodeLoop<TScale>` JIT 特化
- `Ntsc.cs:713-892` — `DecodeScanline_Physical` + `GenerateWaveform` (full LTI signal reconstruction)
- `Ntsc.cs:976-1118` — `DemodulateRow_Core` (Hann FIR + Vector<T> SIMD)
- `Ntsc.cs:1122-1128` — `YiqToRgb` (gammaLUT 12-bit fixed-point)
- `CrtScreen.Shared.cs:30-156` — backend dispatch + 共用 config
- `CrtScreen.cs:95-137` — `PrecomputeScanlineWeights` (Gauss + jitter)
- `CrtScreen.cs:139-190` — `PrecomputeCurvature` (reverse map)
- `CrtScreen.cs:192-247` — `ApplyHorizontalBlur` (3-tap SIMD)
- `CrtScreen.cs:249-401` — `Render` (Parallel.For 主迴圈)
- `CrtScreen.cs:452-544` — SWAR mask / phosphor / convergence
- `CrtScreen.cs:546-623` — `ApplyFullFrameCurvatureAndConvergence`
- `CrtScreen.Simd.cs:1-1005` — .NET 10 + AVX2 explicit fork
- `CrtScreen.Gpu.cs:1-203` — SkiaSharp `SKRuntimeEffect` GPU path

---

## 附錄 B：兩邊的 LOC 統計

| 檔案 | LOC | 角色 |
|------|-----|------|
| **Upstream 核心** | | |
| `crt_core.h` | 145 | 公用 struct + enum + sin/cos API |
| `crt_core.c` | 666 | demod 全境（含 EQ filter 兩種變體） |
| `crt_nes.h` | 149 | NES 系統 timing 常數 |
| `crt_nes.c` | 310 | NES 編碼器 + `square_sample` |
| `crt_ntsc.h` | 130 | 標準 NTSC 系統常數 |
| `crt_ntsc.c` | 331 | 標準 NTSC 編碼器（含 even/odd field） |
| `crt_main.c` | 557 | demo CLI（不算 lib） |
| **upstream 核心 LOC 合計（不含 main）** | **1731** | |
| **Ours** | | |
| `Ntsc.cs` | 1129 | NTSC mod + demod（NES 專用） |
| `CrtScreen.Shared.cs` | 156 | 後端分發 + 共用 config |
| `CrtScreen.cs` | 624 | Scalar 後端 |
| `CrtScreen.Simd.cs` | 1005 | SIMD 後端（.NET 10） |
| `CrtScreen.Gpu.cs` | 203 | GPU 後端（SkSL） |
| **AprNes 核心 LOC 合計** | **3117** | |

LOC ratio ≈ 1.8×，但其中 `CrtScreen.Simd.cs` 是 `CrtScreen.cs` 的 fork（SIMD 重寫）；扣除這 1005 後實際多寫的功能性程式碼是 ~2112 行，比 upstream 多 1.2×（合理：多了 Hann FIR / HD_NTSC / GPU shader / 9-knob CRT post / 三套 profile / parallel + JIT specialization）。
