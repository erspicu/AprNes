# 2026-03-26 ~ 2026-03-28 更新日誌

---

## 1. AudioPlus DSP 音效引擎（全新功能）

### 架構與實作
- **三種音效模式**：Mode 0（Raw 原始輸出）、Mode 1（Classic 經典）、Mode 2（Modern Digital 現代數位）
- **AudioPlus v2 引擎**：所有 DSP class 扁平化為 `partial class NesCore`，陣列全部改為 `Marshal.AllocHGlobal` 非託管記憶體
- **Mode 2 DSP 鏈**：DC Blocker → 4-tap FIR LPF → Stereo Widener → Comb Reverb → Biquad HPF → Soft Limiter
- **Per-channel expansion audio**：擴展音效（VRC6/VRC7/5B/N163/FDS）改為逐聲道混音，支援 DSP 獨立處理

### 效能優化
- FIR 2x unroll、Comb Reverb unroll、Transposed Biquad：Mode 2 提升 +20~28%
- 扁平化重構：+22~27% 效能提升（消除虛擬呼叫與物件間接層）

### 其他
- Memory leak 修復：ROM reload 時正確釋放 DSP 非託管記憶體
- 完整中英文設計文件與 Benchmark 報告

---

## 2. NTSC / CRT 後處理優化

### NTSC 編解碼
- **Symmetric I/Q demodulation mode**：新增對稱 I/Q 解調模式切換
- **Q FIR dual-accumulator**：FIR 濾波雙累加器 SIMD 優化
- **Bilinear batch SIMD**：雙線性插值批次 SIMD 化
- **Curvature modulo elimination**：曲面變形取模運算消除

### CRT 效果
- PhosphorDecay 預設值 0.6 → 0.15（降低殘影）
- DC Blocker gain loss 修復
- Interlace jitter line-snapping 修正

---

## 3. 畫面縮放與濾鏡重構

- **Two-stage resize pipeline**：新增兩階段縮放管線，修復 memory leak
- **xBRZ 重寫**：修復 2X/3X blend 錯誤，移除無用程式碼，重新命名 GBEMU 殘留
- **ScaleX / LibScanline 重寫**：效能導向重構
- 畫面大小與 Scanline rendering flow 設計文件

---

## 4. Mapper 大批修復與驗證（3/26~3/27）

### VRC7 OPLL FM 合成音效（Mapper 085）
- 完整實作 OPLL (YM2413) FM 合成引擎
- CHR-RAM banking 修正
- 人工驗證：Lagrange Point (J) ✅

### Namco 163 修復（Mapper 019）
- CIRAM nametable/CHR mapping 修正：base offset 與 write protection
- 人工驗證：Splatterhouse (J)、Chibi Maruko-Chan (J) ✅

### VRC4d 地址線修復（Mapper 025）
- A0/A1 地址線對調修正 + heuristic OR 修復
- 人工驗證：TMNT (J) ✅

### Irem H-3001 鏡像修復（Mapper 065）
- 反轉鏡像邏輯（bit7=1 → Horizontal）
- 人工驗證：Daiku no Gen San 2 (J) ✅

### Taito X1-005 / X1-017 鏡像修復（Mapper 080/082）
- mirroring 反轉修復
- 人工驗證通過 ✅

### 其他 Mapper 批次修復
- **Mapper 009**：部分修正
- **Mapper 066**：PRG bank modulo 修復 ✅
- **Mapper 070**：bit7 啟發式偵測 mislabeled ROM 鏡像修復，Arkanoid II 標題正常 ✅
- **Mapper 071**：基於 Mesen2 BF909x 重寫，PRG banking + BF9097 鏡像控制 ✅
- **Mapper 089**：人工驗證通過 ✅
- **Mapper 152**：mirroring bit 修復（bit6→bit7）✅
- **Mapper 206**：CHR/PRG bank modulo 修復 ✅
- **Mapper 067 / 210**：部分修正，仍有問題

---

## 5. 輸入系統

- **P2 手把支援**：新增 Player 2 gamepad 支援
- **穩定裝置識別**：改用裝置 GUID 識別手把，避免重新插拔後錯亂

---

## 6. 系統品質

- **IMapper.Cleanup()**：新增 mapper 清理介面方法，修復 4 個 mapper 的 memory leak
- **xBRZ / ScaleX / LibScanline**：移除 dead code、重新命名 GBEMU 殘留

---

## 7. FDS 磁碟機支援（Famicom Disk System）— 全新功能

### 架構
- **獨立於 IMapper**：FDS 以 `partial class NesCore`（`FDS.cs`）實作，不使用 mapper 架構
- **FdsChrMapper shim**：最小化 IMapper 實作，僅提供 CHR-RAM 給 PPU function pointer 初始化
- 共用 CPU/PPU/APU/MEM 核心子系統，最小化對現有程式碼的干擾

### 核心功能
- **BIOS 驗證**：從 `{exe}/FDSBIOS/DISKSYS.ROM` 載入，SHA-256 校驗
- **Gap-inserted 磁碟映像**：基於 Mesen2 FdsLoader 演算法，插入初始間隙（28300 bits）+ 區塊間間隙（976 bits）+ fake CRC
- **磁碟 I/O 狀態機**：馬達控制、磁頭延遲（50000 cycles）、逐位元組傳輸（149 cycles/byte）、CRC-16 驗算
- **雙 IRQ 來源**：Timer IRQ ($4020-$4022) + Disk transfer IRQ
- **FDS 音效**：64-sample 6-bit wavetable + FM 調變（modulation channel + mod table）
- **自動換碟**：BIOS 例程偵測（$E18C game start、$E445 disk side matching）、自動彈出/插入

### UI 整合
- 檔案對話框支援 `.fds` 副檔名
- Hard Reset 正確路由至 FDS 初始化
- ZIP 解壓縮支援 `.fds` 檔案
- TestRunner headless 模式自動偵測 FDS

### 修復記錄
- **ERR.27 修復**：CRC 校驗以 fake CRC bytes 永遠失敗 → 改為不報告 CRC 錯誤（匹配 FCEUX/Nestopia 行為）
- **Battery status**：`fdsExtConWriteReg` 初始化為 0x80（bit7=1 表示電池正常）
- **雙 IRQ 分離**：`fdsTimerIrqPending` + `fdsDiskIrqPending` 獨立管理

### 驗證結果
- 10+ 遊戲人工測試通過：Donkey Kong、Super Mario Bros.、Bubble Bobble、TwinBee、Galaga、Xevious、Ice Climber、Pac-Man、Zanac、Dracula II
- 多磁碟面遊戲正常（Bubble Bobble 2面、Dracula II 2面）
- 174/174 blargg 測試無回歸 ✅
- AccuracyCoin 136/136 無回歸 ✅

---

## 統計

- **Commits**: 31（3/26~3/28）
- **新增 Mapper 驗證**: 12 個（019/025/065/066/070/071/080/082/085/089/152/206）
- **總計 Mapper**: 61 個已實作（48 ✅ + 5 ⚠️ + 4 ❌ + 2 ❓）
- **測試基線**: 174/174 blargg PASS, 136/136 AccuracyCoin PASS
