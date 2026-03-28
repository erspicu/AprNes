# 2026-03-29 更新日誌

---

## 1. PAL 區域支援（完整一般模式）

### 核心時序參數化
- **ApplyRegionProfile()**：所有硬編碼 NTSC 值改為 region-dependent 參數
  - `totalScanlines`: NTSC=262, PAL=312
  - `preRenderLine`: NTSC=261, PAL=311
  - `masterPerCpu`: NTSC=12, PAL=16（3.2:1 PPU/CPU 比）
  - `cpuFreq`: NTSC=1,789,773 Hz, PAL=1,662,607 Hz
  - `FrameSeconds`: NTSC=1/60.0988, PAL=1/50.0070
- **PPU.cs**：所有 `scanline == 261` → `preRenderLine`，`scanline == 262` → `totalScanlines`
- **MEM.cs**：`catchUpPPU()` 支援 PAL 3.2:1（第4步 if guard，自然產生 4,3,3,3,3 pattern）
- **APU.cs**：PAL 專用 noise/DMC/frame counter reload tables
- **AudioPlus.cs**：頻率相關常數改為 static，`AudioPlus_ApplyRegion()` 動態套用

### PAL 硬體差異修正
- **P1: Color Emphasis 紅綠交換**：PPU $2001 寫入時 PAL/Dendy 交換 bit0(R) 和 bit1(G)
- **P2: Odd Frame Dot Skip 禁用**：PAL/Dendy 每幀固定 341 dots
- **P3: PAL 2C07 調色盤**：從 DAC 電壓動態生成 64-color palette（YUV→RGB 解碼）
  - lo levels: [-0.117, 0.000, 0.223, 0.490]
  - hi levels: [0.306, 0.543, 0.741, 1.000]
- **FPS limiter**：改用 `NesCore.FrameSeconds`（PAL 50Hz）
- **PAL 音效修復**：AudioPlus 硬編碼 NTSC CPU 頻率導致 PAL 模式 buffer underrun 破音

### 測試結果
- NTSC 回歸測試：174/174 PASS ✅
- AccuracyCoin：136/136 PASS ✅

---

## 2. Dendy 區域支援（俄羅斯 Famiclone）

### 核心特性
- **CPU ÷15**：masterPerCpu=15（PAL master clock 26.6MHz ÷ 15 = 1,773,447 Hz）
- **PPU/CPU 3:1**：同 NTSC（masterPerPpu=5，15/5=3），catchUpPPU 不需第4步
- **NMI 延遲至 scanline 291**：新增 `nmiTriggerLine` 參數（NTSC/PAL=241, Dendy=291）
  - 51 行 post-render idle（scanline 240-290），VBlank 僅 20 行（同 NTSC）
- **NTSC APU tables**：noise/DMC/frame counter 使用 NTSC 值（音高正確）
- **Frame counter IRQ 禁用**：UMC 晶片硬體 bug，完全不觸發
- **NTSC 調色盤**：使用 NTSC palette（接近原始外觀）
- **PAL 共用特性**：312 scanlines、50Hz、Emphasis 紅綠交換、無 odd dot skip

### UI
- Region 選單新增 Dendy 選項（從 hidden 改為 visible）
- INI 存取支援三種 region

---

## 3. 無頭模式 Region 安全機制

- **驗證測試強制 NTSC**：`--wait-result` / `--dump-ac-results` 時自動設定 Region=NTSC，防止 INI 中 Region=PAL 汙染 NTSC 測試結果
- **--region CLI 參數**：新增 `--region <NTSC|PAL|Dendy>` 支援明確指定區域

---

## 4. UI 重構

- **MenuStrip 取代 Panel 按鈕**：主介面改用選單列，整合所有快捷鍵
- **Region 子選單**：NTSC / PAL / Dendy 三選一，即時切換（需重新載入 ROM）

---

## 5. 音效系統增強

- **Per-channel Volume Control**：每個 APU 聲道獨立音量控制（Pulse1/2、Triangle、Noise、DMC）
- **Soft Limiter**：防止混音溢位的軟限幅器
- **Channel Volume UI**：ConfigureUI 新增聲道音量滑桿 + i18n 支援
- **錄音功能**：音訊錄製、自動停止、INI 記錄路徑、ConfigureUI 品質設定

---

## 6. 文件

- **PAL 規格文件**：`MD/PAL/PAL_2C07_Specifications.md`（電壓、時序、APU 完整規格）
- **PAL 待辦事項**：`MD/PAL/PAL_Implementation_TODO.md`（P1-P3 完成，P4 類比模式待做）
- **PAL 顯示比例**：`MD/PAL/PAL_Display_Aspect_Ratio.md`（PAR、DAR、黑邊計算）
- **Dendy 規格文件**：`MD/Dendy/Dendy_Specifications.md`（三方比較表）
- **Dendy 待辦事項**：`MD/Dendy/Dendy_Implementation_TODO.md`
- **AudioPlus 設計文件**：oversampling 設計理念、blip_buf 比較、英文版規格書

---

## 7. 其他

- **FDS 修復**：自動換碟改用 BIOS header 匹配正確磁碟面
- **Benchmark 模式**：新增圖像濾鏡 benchmark（xBRZ NoTable distance）
- **AccuracyOptA 強制**：無頭驗證模式強制開啟完整精確度

---

## 統計

- **Commits**: 18（3/29）
- **新增區域支援**: PAL + Dendy（共 3 種 region）
- **測試基線**: 174/174 blargg PASS, 136/136 AccuracyCoin PASS（無回歸）
