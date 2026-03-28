# PAL 實作待辦事項

## 已完成 ✅

### 核心時序參數化 (2026-03-29)
- [x] Main.cs: `ApplyRegionProfile()` — totalScanlines, preRenderLine, masterPerCpu, masterPerPpu, cpuFreq, FrameSeconds
- [x] PPU.cs: 所有 `scanline == 261` → `preRenderLine`, `scanline == 262` → `totalScanlines`
- [x] PPU.cs: pre-render sprite eval `261 & 255` → `preRenderLine & 255`
- [x] MEM.cs: `MASTER_PER_CPU/PPU` const → static field, catchUpPPU 支援 PAL 3.2:1 (第4步 if guard)
- [x] APU.cs: Region-dependent noise/DMC/frame counter tables
- [x] APU.cs: `_cycPerSample` 根據 `cpuFreq` 計算
- [x] AprNesUI.cs: FPS limiter 使用 `NesCore.FrameSeconds`
- [x] AudioPlus: `AP_CPU_FREQ`, `AP_CLOCKS_PER_SAMPLE`, `OSE_CUTOFF_NORM`, `OSE_CLOCKS_PER_SAMPLE_FP`, `CMF_RF_PHASE_INC` 全部改為 region-dependent
- [x] AudioPlus: `AudioPlus_ApplyRegion()` 方法，在 init 時套用
- [x] NTSC 回歸測試: 174/174 PASS

## 待實作 🔲

### P1: Color Emphasis 紅綠交換 ✅ (2026-03-29)
- [x] PPU.cs: 寫入 $2001 時，PAL/Dendy 模式下交換 ppuEmphasis bit0(R) 和 bit1(G)
- [x] 使用 bit manipulation: `(emph & 4) | ((emph & 1) << 1) | ((emph >> 1) & 1)`

### P2: Odd Frame Dot Skip 禁用 ✅ (2026-03-29)
- [x] PPU.cs: `Region == RegionType.NTSC` guard 在 odd frame skip 邏輯前
- [x] PAL/Dendy 每幀固定 341 dots，無 dot skip

### P3: PAL 專用調色盤 ✅ (2026-03-29)
- [x] PPU.cs: `generatePaletteFromVoltages()` 從 PAL 2C07 DAC 電壓動態生成 64-color palette
- [x] PAL voltages: lo=[-0.117, 0.000, 0.223, 0.490], hi=[0.306, 0.543, 0.741, 1.000]
- [x] YUV→RGB 解碼（PAL 用 YUV 而非 NTSC 的 YIQ）
- [x] NTSC 保留原 hardcoded palette（已驗證 174+136 tests）

### P4: PAL 類比訊號模擬 (大工程，低優先)
- [ ] Ntsc.cs 的 loLevels/hiLevels 需要 PAL 電壓值
- [ ] 色彩編碼從 YIQ 改為 YUV + 逐行相位反轉
- [ ] Colorburst 從 3.58 MHz 改為 4.43 MHz
- [ ] Swinging burst 實作
- [ ] Filter window 參數重新校準
- [ ] **建議**：可能需要獨立的 Pal.cs 模組而非修改 Ntsc.cs

### P5: Dendy 支援 (未來)
- [ ] Dendy = PAL timing + NTSC palette (俄羅斯 clone)
- [ ] 312 scanlines, 50 Hz, 但使用 NTSC colorburst 和調色盤
- [ ] 目前 UI 已有 Dendy 選項（hidden），待 PAL 穩定後啟用

## 注意事項

- PAL 沒有專用測試 ROM（blargg 174 和 AccuracyCoin 136 都是 NTSC），只能靠人工驗證
- 切換 Region 需要重新載入 ROM（觸發 init()），不是即時切換
- PPU 暖機時間 PAL 較短（~27384 vs ~29658 CPU cycles），但目前未模擬暖機
- OAM 在 PAL 的 70 行 VBlank 中不會衰減（2C07 有主動刷新機制）
