# Dendy 實作待辦事項

> 優先順序：先完成 PAL，再做 Dendy

## 與 PAL 共用部分
- 312 scanlines, pre-render line 311
- 50 Hz frame rate
- 無 odd frame dot skip
- Emphasis bit 5/6 紅綠交換

## Dendy 獨有需求

### D1: CPU 除頻器 = 15 (非 PAL 的 16)
- masterPerCpu = 15 (不是 12 也不是 16)
- masterPerPpu = 5 (同 PAL)
- PPU/CPU = 15/5 = **3:1**（同 NTSC！catchUpPPU 固定 3 步，不需要 PAL 的 4th step）
- cpuFreq = 26601712 / 15 = **1,773,447 Hz**

### D2: VBlank NMI 延遲到 scanline 291
- NTSC/PAL 都在 scanline 241 觸發 NMI
- Dendy 有 51 行 post-render idle (240-290)，NMI 到 scanline 291 才觸發
- VBlank 仍然只有 20 行 (291-310)，與 NTSC 相同
- 需要新增 `nmiTriggerLine` 參數（NTSC/PAL = 241, Dendy = 291）

### D3: APU 使用 NTSC tables
- Noise period: NTSC values
- DMC rate: NTSC values
- Frame counter reload: NTSC values
- 但 cpuFreq 不同 → _cycPerSample 需要用 Dendy 的 1,773,447 Hz

### D4: Frame Counter IRQ 禁用
- Dendy 硬體 bug：APU frame counter IRQ 完全不觸發
- 需要 region check：`if (Region == RegionType.Dendy) { /* skip IRQ */ }`
- 影響 APU.cs 的 `framectr == 3 && ctrmode == 4` IRQ 邏輯

### D5: Pulse Duty Cycle 25%/75% 交換
- 硬體上 duty 0 (12.5%) 和 duty 2 (50%) 不變
- duty 1 (25%) 和 duty 3 (75%) 互換
- 聽感完全相同（波形互為反轉），純粹是硬體差異
- **可選實作**：音感無差異，只有示波器看得出來

### D6: 調色盤
- UM6538 PPU 有自己的 palette，偏接近 NTSC
- 可暫時使用 NTSC palette（視覺上接近）
- 精確實作需要 UMC 晶片的實測 RGB 數據

## RegionProfile 預估值

```
Dendy:
  totalScanlines = 312
  preRenderLine  = 311
  nmiTriggerLine = 291  // 新欄位！
  vblankLength   = 20
  masterPerCpu   = 15
  masterPerPpu   = 5
  cpuFreq        = 1773447.0  (26601712 / 15)
  FrameSeconds   = 1.0 / 50.00
  useNtscApuTables = true
  frameCounterIrqEnabled = false
  emphasisSwapped = true   // 同 PAL
  oddFrameDotSkip = false  // 同 PAL
```

## 注���事項

- Dendy 是 NTSC/PAL 的混合體，不能簡單歸類為其中之一
- PPU/CPU 3:1 比率意味著 catchUpPPU 走 NTSC 路徑（固定 3 步）
- NMI trigger line 是唯一需要新增的參數（NTSC/PAL 都不需要）
- 少數���戲依賴 frame counter IRQ 會在 Dendy 上掛掉（已知限制）
