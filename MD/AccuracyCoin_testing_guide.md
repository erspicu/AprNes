# AccuracyCoin 測試指南

## 目錄

1. [概述](#1-概述)
2. [ROM 操作方式](#2-rom-操作方式)
3. [測試工具使用方式](#3-測試工具使用方式)
4. [測試技術框架](#4-測試技術框架)
5. [結果解讀](#5-結果解讀)
6. [已知問題與限制](#6-已知問題與限制)
7. [分頁內容一覽](#7-分頁內容一覽)

---

## 1. 概述

**AccuracyCoin** 是一個 NES 精確度測試 ROM，包含 **136 個測試** + **5 個 DRAW 資訊頁**，分佈在 20 個分頁中。測試涵蓋 CPU 指令、非官方指令、中斷時序、DMA、APU、PPU 行為等。

- ROM 路徑：`nes-test-roms-master/AccuracyCoin-main/AccuracyCoin.nes`
- Mapper：0 (NROM)
- 參考文件：`nes-test-roms-master/AccuracyCoin-main/README.md`
- 組語原始碼：`nes-test-roms-master/AccuracyCoin-main/AccuracyCoin.asm`

---

## 2. ROM 操作方式

### 2.1 啟動

ROM 啟動後約 **3 秒**顯示 Page 1/20 的測試項目清單。

### 2.2 分頁導覽

焦點在頁次標題（PAGE X/20）時：

| 按鍵 | 動作 |
|------|------|
| **右鍵** | 下一頁（Page 20 → 右鍵回到 Page 1） |
| **左鍵** | 上一頁（Page 1 → 左鍵跳到 Page 20） |
| **A** | 執行該頁所有測試 |
| **B** | 標記該頁所有測試為 SKIP |
| **Start** | 連續執行 ROM 內所有測試（完成後顯示總結表格） |
| **下鍵** | 焦點移到該頁第一個測試項目 |

每次切換分頁後建議等待 **0.5～1 秒**（載入/顯示時間）。

### 2.3 單項測試導覽

焦點在個別測試項目時：

| 按鍵 | 動作 |
|------|------|
| **下鍵** | 移到下一個測試項目（最後一項再按下不會跳頁） |
| **上鍵** | 移到上一個測試項目（第一項再按上回到頁次標題） |
| **A** | 執行該單項測試 |
| **B** | 標記/取消標記為 SKIP（跳過） |
| **Select** | 顯示 Debug Menu（記憶體值檢視） |

### 2.4 兩種測試方式

#### 連續測試（Start 鍵）

1. 啟動後等 3 秒
2. 按 **Start**
3. ROM 自動依序執行所有 136 個測試
4. 完成後顯示總結表格截圖
5. **缺點**：若某項測試卡住（如 test 82），後續所有測試無法執行

#### 分頁測試（A 鍵）

1. 導覽到目標分頁
2. 按 **A** 執行該頁所有測試
3. 若該頁有會卡住的測試，可先用 **下鍵 + B** 標記為 SKIP，再回到頁首按 **A**
4. **優點**：可跳過卡住的測試，逐頁收集所有結果

### 2.5 測試時間參考

| 類型 | 預估時間 |
|------|----------|
| 整頁測試 | 一般 < 30 秒 |
| 單項測試 | 一般 < 10 秒（NMI/IRQ/BRK/中斷類可能需要 ~10 秒） |
| 分頁切換 | 0.5～1 秒 |

---

## 3. 測試工具使用方式

### 3.1 自動化測試腳本

```bash
# 完整執行（編譯 + 分頁測試 + 截圖 + 報告）
bash run_tests_AccuracyCoin_report.sh

# 跳過編譯
bash run_tests_AccuracyCoin_report.sh --no-build

# 跳過截圖（僅收集結果數據）
bash run_tests_AccuracyCoin_report.sh --no-screenshots
```

輸出：
- HTML 報告：`report/AccuracyCoin_report.html`
- 分頁截圖：`report/screenshots-ac/ac_page_XX.png`
- 中間結果：`temp/ac_results/page_XX.hex`

### 3.2 手動執行單頁測試

```bash
# 範例：測試 Page 3（需 2 次右鍵導覽）
AprNes/bin/Debug/AprNes.exe \
    --rom nes-test-roms-master/AccuracyCoin-main/AccuracyCoin.nes \
    --time 40 \
    --input "Right:3.5,Right:4.0,A:5.0" \
    --screenshot result/ac_page03.png \
    --dump-ac-results
```

### 3.3 手動跳過特定項目

```bash
# 範例：Page 12，跳過第 1 項（IFlagLatency）
# 導覽到 Page 12（9 次左鍵），Down 選第 1 項，B 標記跳過，Up 回頁首，A 執行
AprNes/bin/Debug/AprNes.exe \
    --rom nes-test-roms-master/AccuracyCoin-main/AccuracyCoin.nes \
    --time 45 \
    --input "Left:3.5,Left:4.0,Left:4.5,Left:5.0,Left:5.5,Left:6.0,Left:6.5,Left:7.0,Left:7.5,Down:8.5,B:9.0,Up:9.5,A:10.0" \
    --screenshot result/ac_page12.png \
    --dump-ac-results
```

### 3.4 相關 CLI 參數

| 參數 | 說明 |
|------|------|
| `--rom <path>` | ROM 檔案路徑 |
| `--time <seconds>` | 執行時間（NES 秒數） |
| `--input <spec>` | 模擬手把輸入，格式：`Button:time,Button:time,...` |
| `--screenshot <path>` | 結束時截圖 |
| `--timed-screenshots <spec>` | 定時截圖，格式：`path1:time1,path2:time2,...` |
| `--dump-ac-results` | 結束時印出 `AC_RESULTS_HEX:` 記憶體傾印（$0300-$04FF） |

按鍵名稱：`A`, `B`, `Select`, `Start`, `Up`, `Down`, `Left`, `Right`

---

## 4. 測試技術框架

### 4.1 分頁獨立測試架構

由於 Page 12 第 1 項（Interrupt Flag Latency）會卡住，無法使用 Start 連續測試。因此採用**分頁獨立測試**架構：

```
每頁獨立啟動模擬器 → 導覽到目標頁 → 按 A 執行 → 等待完成 → 傾印結果 → 合併
```

流程：

1. **逐頁啟動**：每頁各啟動一次模擬器（共 20 次），互不干擾
2. **導覽最佳化**：
   - Page 1～10：從 Page 1 按右鍵（0～9 次）
   - Page 11～20：從 Page 1 按左鍵（10～1 次，繞行更快）
3. **Page 12 特殊處理**：導覽到頁 → Down 選第 1 項 → B 標記跳過 → Up 回頁首 → A 執行其餘
4. **Page 15 特殊處理**：DRAW 測試不產生 PASS/FAIL，僅擷取截圖
5. **結果傾印**：每頁結束後 `--dump-ac-results` 輸出 $0300-$04FF 的 hex 資料
6. **結果合併**：將 20 頁的 hex 資料合併（非零值覆蓋零值），產生完整結果

### 4.2 時序計算

每頁的 `--input` 和 `--time` 計算方式：

```
ROM 載入等待：3.0 秒
導覽間隔：每次按鍵 0.5 秒
導覽完成到按 A：1.0 秒
測試等待：35 秒（最大 30 秒 + 緩衝）

Page N 的總時間 ≈ 3.0 + nav_presses × 0.5 + 1.0 + 35.0
```

### 4.3 結果記憶體位址

AccuracyCoin 將每個測試的結果存放在固定記憶體位址（$0400-$048D 區域）。每個測試佔 1 byte：

```
0x01                = PASS（bit 0 = 1）
(ErrorCode << 2) | 0x02 = FAIL（bit 1 = 1，bits 2+ = 錯誤碼）
0xFF                = SKIP
0x00                = 尚未執行
```

`--dump-ac-results` 傾印 $0300-$04FF（512 bytes = 1024 hex chars），包含所有測試結果。

### 4.4 報告生成

Python 腳本讀取合併後的 hex 資料，對照測試位址表（SUITES），產生 HTML 報告：
- 每頁一個區塊，含截圖和結果表格
- 自動統計 PASS/FAIL/SKIP/N/A
- DRAW 頁（Page 15）以紫色卡片排版，顯示子項截圖

---

## 5. 結果解讀

### 5.1 結果 byte 格式

| 值 | 意義 | 解讀方式 |
|----|------|----------|
| `0x01` | PASS | bit 0 = 1 |
| `0x02` | FAIL, error code 0 | (0 << 2) \| 0x02 |
| `0x06` | FAIL, error code 1 | (1 << 2) \| 0x02 |
| `0x0A` | FAIL, error code 2 | (2 << 2) \| 0x02 |
| `0x1E` | FAIL, error code 7 | (7 << 2) \| 0x02 |
| `0xFF` | SKIP（被 B 鍵標記跳過） | |
| `0x00` | 尚未執行 | |

通用公式：`error_code = result_byte >> 2`（當 bit 1 = 1 時）

### 5.2 錯誤碼含義

每個測試的錯誤碼含義不同，詳見 `nes-test-roms-master/AccuracyCoin-main/README.md` 的 "Error Codes" 章節。例如非官方指令測試的常見錯誤碼：

- 1: 目標位址錯誤
- 2: A 暫存器值錯誤
- 3: X 暫存器值錯誤
- 5: CPU flags 錯誤
- 7: RDY line 低電位時的目標位址錯誤（SHA/SHX/SHY/SHS）

### 5.3 Debug Menu

測試完成後按 **Select** 可顯示 Debug Menu，印出以下記憶體區域：
- $20-$2F：非官方指令測試用值
- $50-$6F：部分測試用值
- $500-$5FF：測試工作區（8 行 × 32 bytes）

---

## 6. 已知問題與限制

### 6.1 卡住的測試

| 測試 | 分頁 | 問題 | 根因 |
|------|------|------|------|
| Interrupt Flag Latency (Test E) | Page 12, item 1 | 執行後卡住不返回 | DMC DMA 時序偏移 ~12 cycles，需 Master Clock scheduler |

### 6.2 已知 FAIL 的測試類別

| 類別 | 說明 |
|------|------|
| SH* 指令 (Page 10) | SHA/SHS/SHY/SHX 需要 RDY line + DMA bus contention 精確模擬 |
| DMA 相關 (Page 13) | 部分測試需要 sub-cycle 精確的 DMA 時序 |

### 6.3 分頁獨立測試的限制

- 每頁獨立啟動，Power-On State（Page 15）的結果可能與連續測試不同
- 沒有 Start 鍵的總結表格截圖（改由 HTML 報告取代）
- 測試間無交互影響（這通常是優點，但某些 timing 測試可能依賴前置狀態）

---

## 7. 分頁內容一覽

| 頁 | 名稱 | 測試數 | 備註 |
|----|------|--------|------|
| 1 | CPU Behavior | 9 | 基本 CPU 行為 |
| 2 | Addressing mode wraparound | 6 | 位址模式邊界 |
| 3 | Unofficial: SLO | 7 | |
| 4 | Unofficial: RLA | 7 | |
| 5 | Unofficial: SRE | 7 | |
| 6 | Unofficial: RRA | 7 | |
| 7 | Unofficial: *AX | 10 | SAX + LAX |
| 8 | Unofficial: DCP | 7 | |
| 9 | Unofficial: ISC | 7 | |
| 10 | Unofficial: SH* | 6 | SHA/SHS/SHY/SHX/LAE |
| 11 | Unofficial Immediates | 8 | ANC/ASR/ARR/ANE/LXA/AXS/SBC |
| 12 | CPU Interrupts | 3 | **item 1 會卡住，需 SKIP** |
| 13 | APU Registers and DMA | 10 | DMA 相關測試 |
| 14 | APU Tests | 9 | 長度計數器、Frame Counter、DMC |
| 15 | Power On State | 5 | **DRAW 測試，僅顯示資訊** |
| 16 | PPU Behavior | 7 | CHR/Register/Palette |
| 17 | PPU VBlank Timing | 7 | VBL/NMI 時序 |
| 18 | Sprite Evaluation | 9 | Overflow/Hit/OAM |
| 19 | PPU Misc. | 6 | Attributes/Shift Register |
| 20 | CPU Behavior 2 | 4 | Timing/Dummy Reads/Branch |

**合計**：136 個測試 + 5 個 DRAW = 141 項

---

*最後更新：2026-03-06*
