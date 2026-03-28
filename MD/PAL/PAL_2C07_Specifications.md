# PAL 2C07 PPU 規格差異文件

> 資料來源：Gemini 知識庫查詢 (2026-03-29)，交叉對照 NESdev Wiki、Mesen2 source

## 1. 基本時序參數

| 參數 | NTSC (2C02) | PAL (2C07) |
|------|-------------|------------|
| Master Clock | 21,477,272.73 Hz | 26,601,712.5 Hz |
| CPU Clock | master ÷ 12 = 1,789,773 Hz | master ÷ 16 = 1,662,607 Hz |
| PPU Clock | master ÷ 4 = 5,369,318 Hz | master ÷ 5 = 5,320,343 Hz |
| PPU/CPU 比 | 3:1 (固定) | 3.2:1 (16÷5, 3+3+3+3+4 pattern) |
| Scanlines/Frame | 262 | 312 |
| Pre-render line | 261 | 311 |
| Visible lines | 240 (0-239) | 240 (0-239) |
| VBlank lines | 20 (241-260) | 70 (241-310) |
| Dots/scanline | 341 | 341 |
| Frame rate | 60.0988 Hz | 50.0070 Hz |
| Colorburst freq | 3,579,545 MHz (master÷6 phase) | 4,433,618.75 MHz (master÷6) |
| Phases/color cycle | 12 (6 MCU × 2 edges) | 12 (6 MCU × 2 edges) |
| Pixels per MCU | 4 master clocks | 5 master clocks |

## 2. 電壓位準 (Normalized Float)

正規化基準：Color $1D (Standard Black) = 0.000, NTSC Color $20 (White) = 1.000

### NTSC 2C02
| 亮度列 | LOW | HIGH |
|--------|-----|------|
| $0x | -0.117 | 0.397 |
| $1x | 0.000 | 0.681 |
| $2x | 0.308 | 1.000 |
| $3x | 0.715 | 1.096 |

### PAL 2C07
| 亮度列 | LOW | HIGH |
|--------|-----|------|
| $0x | -0.117 | 0.306 |
| $1x | 0.000 | 0.543 |
| $2x | 0.223 | 0.741 |
| $3x | 0.490 | 1.000 |

**關鍵差異**：
- PAL 沒有 7.5 IRE setup（Black = Blanking，不像 NTSC 有 setup offset）
- PAL 對比度較低（$3x HIGH = 1.000 vs NTSC 1.096）
- PAL $0x 暗色更正確（LOW 和 blanking 同電壓），NTSC $0x LOW 低於黑色 → 壓黑

## 3. Color Emphasis 紅綠交換

**硬體差異** — $2001 bits 5/6 在 PAL 上對調：

| Bit | NTSC 2C02 | PAL 2C07 |
|-----|-----------|----------|
| Bit 5 | **Red** Emphasis | **Green** Emphasis |
| Bit 6 | **Green** Emphasis | **Red** Emphasis |
| Bit 7 | Blue Emphasis | Blue Emphasis |

→ NTSC 遊戲在 PAL 上跑，Red emphasis 會變成 Green，反之亦然。
→ 模擬器必須根據 region 交換 bit 5/6 的解讀。

## 4. Odd Frame Dot Skip

- **NTSC**: 奇數幀 pre-render line dot 339 跳過（340 dots），用於偏移 colorburst phase 消除 dot crawl
- **PAL**: **不跳過**。每幀每條 scanline 都是固定 341 dots。PAL 的逐行相位反轉機制已天然消除 dot crawl。

## 5. 色彩編碼差異

| 項目 | NTSC | PAL |
|------|------|-----|
| 編碼方式 | YIQ (I/Q 正交) | YUV (U/V 逐行相位反轉) |
| Colorburst 相位 | 固定 | Swinging burst (±45° from U axis，逐行交替) |
| 波形 | 方波 (非正弦) | 方波 (非正弦) |
| Dot crawl | 對角線爬行 | 靜態棋盤紋（不爬行） |

## 6. 開機/Reset 暫存器

- **PPU 暫存器初始值相同**（$2000/$2001 歸零，OAM 隨機衰減）
- **PPU 暖機時間不同**：NTSC ~29,658 CPU cycles, PAL ~27,384 CPU cycles
- **OAM 衰減**：PAL 在 70 行 VBlank 期間主動刷新 OAM，不會腐敗（NTSC 20 行 VBlank 也夠用）

## 7. APU 差異

| 參數 | NTSC | PAL |
|------|------|-----|
| Frame counter step | 7458/7456/7458/7458 | 8314/8314/8312/8314 |
| Noise period table | 4,8,16,32,64,96,128,160,202,254,380,508,762,1016,2034,4068 | 4,8,14,30,60,88,118,148,188,236,354,472,708,944,1890,3778 |
| DMC rate table | 428,380,340,320,286,254,226,214,190,160,142,128,106,84,72,54 | 398,354,316,298,276,236,210,198,176,148,132,118,98,78,66,50 |
| Sample rate 換算 | cpuFreq/44100 ≈ 40.58 clocks/sample | cpuFreq/44100 ≈ 37.70 clocks/sample |
