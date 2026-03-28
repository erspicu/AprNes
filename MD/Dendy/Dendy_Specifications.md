# Dendy (UMC Famiclone) 規格文件

> 資料來源：Gemini 知識庫查詢 (2026-03-29)
> 晶片：UMC UM6527P (CPU) + UMC UM6538 (PPU)

## 設計理念

Dendy 是俄羅斯/中國的 Famiclone，目標是**在 PAL 電視上跑 NTSC 遊戲而不崩潰**。
本質上是「塞進 PAL 訊號的 NTSC 主機」。

## 1. 核心時序

| 參數 | NTSC | PAL | **Dendy** |
|------|------|-----|-----------|
| Master Clock | 21.477272 MHz | 26.601712 MHz | **26.601712 MHz** (PAL) |
| CPU Clock | 1.789773 MHz (÷12) | 1.662607 MHz (÷16) | **1.773447 MHz (÷15)** |
| PPU Clock | 5.369318 MHz (÷4) | 5.320342 MHz (÷5) | **5.320342 MHz (÷5)** |
| PPU/CPU 比 | **3:1** | 3.2:1 | **3:1** (同 NTSC!) |
| Frame rate | 60.09 Hz | 50.00 Hz | **50.00 Hz** (PAL) |
| Total scanlines | 262 | 312 | **312** (PAL) |

**關鍵洞察**：Dendy 用 PAL master clock 但除以 15（而非 16）得到接近 NTSC 的 CPU 速度，
且維持 3:1 PPU/CPU 比率，讓 NTSC 遊戲的 cycle-accurate timing 不會壞掉。

## 2. VBlank 與 NMI — 最大特色

Dendy 的 312 scanlines 分配方式與 PAL 完全不同：

| | NTSC | PAL | **Dendy** |
|---|------|-----|-----------|
| Visible | 0-239 (240) | 0-239 (240) | 0-239 (240) |
| Post-render (idle) | 240 (1) | 240 (1) | **240-290 (51!)** |
| VBlank NMI trigger | scanline 241 | scanline 241 | **scanline 291** |
| VBlank length | 20 lines | 70 lines | **20 lines** (291-310) |
| Pre-render | 261 (1) | 311 (1) | 311 (1) |

**51 行 post-render idle**：PPU 在 240-290 什麼都不做，不觸發 NMI。
CPU 只看到 20 行 VBlank（跟 NTSC 一樣），所以 NTSC 遊戲的 VBlank 邏輯不會溢出。

## 3. APU

| 參數 | NTSC | PAL | **Dendy** |
|------|------|-----|-----------|
| Noise period table | NTSC values | PAL values | **NTSC values** |
| DMC rate table | NTSC values | PAL values | **NTSC values** |
| Frame counter | NTSC intervals | PAL intervals | **NTSC intervals** |
| Frame counter IRQ | 正常 | 正常 | **壞掉/不存在** |

- CPU 跑近似 NTSC 速度 → APU 用 NTSC table 音高才正確
- Frame counter IRQ 在 UMC 晶片上硬體 bug，完全不觸發
- 音樂因 50 Hz NMI → 播放速度慢 16.6%，但音高正確

## 4. Color / Emphasis

- **色彩編碼**：PAL composite（非 NTSC）
- **調色盤**：UM6538 自己的內建 palette，偏接近 NTSC look（不像正式 PAL NES 那��暗/飽和）
- **Emphasis bits**：Bit 5/6 紅綠交換（同 PAL NES）
- **Dot skip**：無（同 PAL NES）
- **Duty cycle 交換**：25% 和 75% pulse duty cycle 在硬體上反轉（聽起來一樣但示波器上不同）

## 5. 三方比較總表

| 規格 | NTSC | PAL | Dendy |
|------|------|-----|-------|
| Master Clock | 21.477 MHz | 26.602 MHz | 26.602 MHz |
| CPU ÷ | 12 | 16 | **15** |
| PPU ÷ | 4 | 5 | 5 |
| PPU/CPU | 3:1 | 3.2:1 | **3:1** |
| FPS | 60 Hz | 50 Hz | 50 Hz |
| Scanlines | 262 | 312 | 312 |
| NMI line | 241 | 241 | **291** |
| VBlank len | 20 | 70 | **20** |
| Pre-render | 261 | 311 | 311 |
| APU tables | NTSC | PAL | **NTSC** |
| Frame IRQ | Yes | Yes | **No** |
| Emphasis R/G | Normal | Swapped | Swapped |
| Odd dot skip | Yes | No | No |
| Palette | NTSC | PAL | **接近 NTSC** |
