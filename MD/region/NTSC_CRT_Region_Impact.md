# PAL/Dendy 導入對 NTSC_CRT 相關程式碼的影響分析

> 2026-03-29

---

## 核心發現

**NTSC_CRT 渲染管線本身（`Ntsc.cs`, `CrtScreen.cs`）不含任何 region 判斷。**
所有 region 差異透過 PPU 層餵入的調色盤索引（`ntscScanBuf`）和 emphasis bits（`ppuEmphasis`）間接影響輸出。

---

## 異動清單

### 1. 調色盤生成 — PPU.cs (初始化, cold path)

| 項目 | NTSC / Dendy | PAL |
|------|-------------|-----|
| 色彩空間 | YIQ→RGB | YUV→RGB |
| R 公式 | `y + 0.956*cb + 0.621*cr` | `y + 1.140*cr` |
| G 公式 | `y - 0.272*cb - 0.647*cr` | `y - 0.395*cb - 0.581*cr` |
| B 公式 | `y - 1.107*cb + 1.704*cr` | `y + 2.032*cb` |

- PAL 呼叫 `generatePaletteFromVoltages()` 使用 2C07 電壓與 YUV 解碼
- NTSC/Dendy 使用硬編碼標準 NES 調色盤
- **影響**: 所有像素的基礎顏色不同，下游 `NesColors[]` 查表全部受影響

### 2. Emphasis 位元交換 — PPU.cs `ppu_w_2001()` (per-$2001 write)

```csharp
ppuEmphasis = (byte)((value >> 5) & 0x7);
if (Region != RegionType.NTSC)
    ppuEmphasis = (byte)((ppuEmphasis & 0x4) | ((ppuEmphasis & 1) << 1) | ((ppuEmphasis >> 1) & 1));
```

| Bit | NTSC | PAL / Dendy |
|-----|------|------------|
| bit 0 | Red | **Green** (swapped) |
| bit 1 | Green | **Red** (swapped) |
| bit 2 | Blue | Blue (不變) |

- **影響**: `DecodeScanline()` 使用 `ppuEmphasis` 索引 emphasis 查表 (`yBaseE[]`, `iBaseE[]`, `qBaseE[]`)，交換後色彩增強效果對應不同色頻

### 3. 奇偶幀跳點 (Odd Frame Dot Skip) — PPU.cs `ppu_step_ntsc()` (per-dot, hot path)

| Region | Dot Skip |
|--------|----------|
| **NTSC** | 有 — pre-render line 261, dot 339 跳過一個 dot |
| **PAL** | 無 |
| **Dendy** | 無 |

- NTSC 奇數幀 338 cycles vs 偶數幀 339 cycles
- PAL/Dendy 固定 341 cycles per scanline
- **對 CRT 的影響**: NTSC 的相位偏移模擬更接近真實硬體的色彩抖動效果

### 4. Scanline 數量與 VBL 時序 — PPU.cs `ppu_step_*()` (per-frame)

| 參數 | NTSC | PAL | Dendy |
|------|------|-----|-------|
| visible lines | 0–239 (240) | 0–239 (240) | 0–239 (240) |
| VBL start (nmiTriggerLine) | 241 | 241 | **291** |
| pre-render line | 261 | 311 | 311 |
| total scanlines | 262 | 312 | 312 |
| VBL 長度 | 20 lines | 71 lines | 20 lines |
| post-render idle | 1 line (240) | 1 line (240) | **51 lines** (240–290) |

- **對 CRT 的影響**: `DecodeScanline(scanline, ...)` 只在 scanline 0–239 呼叫（visible lines），三個 region 相同，不受 VBL 長度影響
- `RenderScreen()` 在 scanline 240 dot 1 觸發，三個 region 相同

### 5. PPU-CPU 時序比 — MEM.cs `catchUpPPU_*()` (per CPU cycle, hot path)

| Region | masterPerCpu | masterPerPpu | PPU dots / CPU cycle |
|--------|-------------|-------------|---------------------|
| **NTSC** | 12 | 4 | 3 (固定) |
| **PAL** | 16 | 5 | 3.2 (3,3,3,3,4 循環) |
| **Dendy** | 15 | 5 | 3 (固定) |

- PAL 每 5 個 CPU cycle 多一個 PPU dot → 影響精靈渲染與 BG tile fetch 的相對時序
- **對 CRT 的影響**: per-scanline 的 DecodeScanline 在所有 PPU dots 完成後呼叫（dot 257），時序差異不影響最終像素資料

### 6. RF 載波頻率 — AudioPlus.cs (初始化, cold path)

```csharp
CMF_RF_PHASE_INC = (Region == RegionType.NTSC ? 59.94 : 50.0) / AP_SAMPLE_RATE;
```

| Region | 頻率 |
|--------|------|
| NTSC | 59.94 Hz |
| PAL / Dendy | 50.0 Hz |

- **影響**: RF 輸出模式的 buzz 頻率不同，與各 region 的實際更新率同步

### 7. 幀率參數 — Main.cs `ApplyRegionProfile()` (cold path)

| Region | cpuFreq (Hz) | FrameSeconds | FPS |
|--------|-------------|-------------|-----|
| NTSC | 1,789,773 | 1/60.0988 | ~60.1 |
| PAL | 1,662,607 | 1/50.0070 | ~50.0 |
| Dendy | 1,773,447 | 1/50.0070 | ~50.0 |

- **對 CRT 的影響**: `Ntsc_SetFrameCount()` / `Crt_SetFrameCount()` 在 `ppu_step_rendering()` scanline 240 dot 1 呼叫，frame_count 的增長速率隨 FPS 不同，但 CRT 效果（閃爍、interlace 模擬）僅依 frame_count 奇偶性，不受絕對時間影響

---

## NTSC_CRT 管線內部 — 無 Region 邏輯

以下檔案**完全不含** `Region`、`PAL`、`Dendy` 等判斷：

| 檔案 | 職責 |
|------|------|
| `NTSC_CRT/Ntsc.cs` | NTSC 訊號編碼/解碼、emphasis 查表、YIQ→RGB |
| `NTSC_CRT/CrtScreen.cs` | CRT 螢幕效果（掃描線、bloom、曲面） |
| `NTSC_CRT/NtscTables.cs` | 預計算 NTSC 濾波器係數 |

這些模組接收的輸入（`ntscScanBuf` 調色盤索引 + `ppuEmphasis` emphasis bits）已經在 PPU 層完成 region 適配，管線本身是 region-agnostic。

---

## 資料流圖

```
PPU Layer (region-aware)                    NTSC_CRT Pipeline (region-agnostic)
┌─────────────────────┐                    ┌──────────────────────────┐
│ ppu_w_2001()        │                    │                          │
│  └─ emphasis swap   │── ppuEmphasis ──→  │  DecodeScanline()        │
│     (PAL/Dendy: R↔G)│                    │   └─ emphasis LUT lookup │
│                     │                    │   └─ YIQ encode/decode   │
│ RenderBGTile()      │                    │   └─ composite filter    │
│ RenderSpritesLine() │── ntscScanBuf ──→  │                          │
│  └─ palette index   │   (per-scanline)   │  CrtScreen.Apply()       │
│     per pixel       │                    │   └─ scanline effect     │
│                     │                    │   └─ bloom / curvature   │
│ generatePalette()   │                    │                          │
│  └─ YIQ (NTSC)      │── NesColors[] ──→  │  (non-Analog path only)  │
│  └─ YUV (PAL)       │                    │                          │
└─────────────────────┘                    └──────────────────────────┘
```
