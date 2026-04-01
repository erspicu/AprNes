# AprNes Mapper 新預備實作清單

**建立日期：2026-04-02　來源：new NES Mapper 實作清單與建議.xlsx**

已實作 mapper 總數：65（含 Mapper 090 JY Company）
本清單新增預備實作：16 個（不含已實作的 090）

---

## 狀態總覽

| 狀態 | 數量 | 說明 |
|:----:|:----:|------|
| 🟢 已備 ROM | 12 | 有測試 ROM，可直接開始實作 |
| 🟡 ROM 不足 | 1 | ROM 數量少或品質不佳 |
| 🔴 無 ROM | 3 | 找不到對應 ROM，暫緩 |
| ⬜ 已實作 | 1 | Mapper 090 已在 AprNes 中 |

---

## 🟢 已備 ROM — 可直接實作

### Mapper 074 — 漢化版 MMC3（CHR-RAM 變體）
- **廠商**：漢化 RPG 專用
- **技術**：在 MMC3 基礎上將特定 Bank 改為 RAM 產生中文字
- **代表作**：《重裝機兵》、《吞食天地 II》
- **實作價值**：高 — 支援經典 RPG 漢化
- **測試 ROM**（5）：
  - `Young Chivalry (Ch) [!].nes`
  - `Dragon Ball Z II - Gekishin Freeza!! (Ch).nes`
  - `Dragon Ball Z II - Gekishin Freeza!! (Ch) [a1].nes`
  - `Dragon Ball Z II - Gekishin Freeza!! (Ch) [a2].nes`
  - `Captain Tsubasa Vol. II - Super Striker (J) [T+Chi20060406_Kanou].nes`

### Mapper 163 — 南晶科技 (Nanjing)
- **廠商**：南晶科技
- **技術**：防寫鎖定序列，需寫入特定序列解鎖 PRG，具備模擬器環境偵測
- **代表作**：《神奇寶貝》系列、《最終幻想 VII》
- **實作價值**：極高 — 中文原創遊戲核心
- **測試 ROM**（5）：
  - `Han Liu Bang (NJ013) (Ch) [!].nes`
  - `Hu Lu Jin Gang (NJ039) (Ch) [!].nes`
  - `Lei Dian Huang Bi Ka Qiu Chuan Shuo (NJ046) (Ch) [!].nes`
  - `Liang Shan Ying Xiong (NJ023) (Ch) [!].nes`
  - `Ne Zha Chuan Qi (NJ036) (Ch) [!].nes`

### Mapper 176 — 外星科技 (Waixing)
- **廠商**：外星科技
- **技術**：混合 Banking（16KB/8KB 混用），特殊存檔致能電路
- **代表作**：《仙劍奇俠傳》、《大富翁》
- **實作價值**：極高 — 華語圈最知名作品
- **測試 ROM**（5）：
  - `12-in-1 Console TV Game Cartridge (Unl) [!].nes`
  - `4-in-1 (BS-8088) [p1][!].nes`
  - `4-in-1 (FK-8008) [p1][!].nes`
  - `4-in-1 (FK-8050) [p1][!].nes`
  - `4-in-1 (FK23C8021) [p1][!].nes`

### Mapper 177 — 外星科技 (Waixing)
- **廠商**：外星科技
- **技術**：SRAM 鎖定狀態機，嚴格的寫入保護序列
- **代表作**：《魔界大空戰》、《新神雕俠侶》
- **實作價值**：高 — 確保 RPG 存檔穩定
- **測試 ROM**（5）：
  - `Mei Guo Fu Hao (Ch).nes`
  - `Shang Gu Shen Jian (Explosion Sangokushi) (Ch).nes`
  - `Wang Zi Fu Chou Ji (Ch).nes`
  - `Xing He Zhan Shi (Ch).nes`
  - `Xing Zhan Qing Yuan (Ch).nes`

### Mapper 164 — 南晶科技 (Nanjing) 後期
- **廠商**：南晶科技
- **技術**：暫存器位址偏移，與 163 邏輯相似但暫存器定址不同
- **代表作**：《幻想水滸傳》、早期合卡
- **實作價值**：中 — 補完南晶後期作品
- **測試 ROM**（5）：
  - `Digital Dragon (Ch) [!].nes`
  - `Pocket Monsters Red (Ch) [!].nes`
  - `Sangokushi II - Haou no Tairiku (J) [T+ChT][a1].nes`
  - `Sangokushi II - Haou no Tairiku (J) [T+ChT][b1].nes`
  - `Darkseed (Unl).nes`

### Mapper 112 — 台灣版 MMC3 (Asder)
- **廠商**：台灣 Asder
- **技術**：暫存器偏移，IRQ 計數器時序微調
- **代表作**：《三國志》、《封神榜》
- **實作價值**：高 — 支援早期中文化大作
- **測試 ROM**（5）：
  - `Chik Bik Ji Jin - Saam Gwok Ji (CN-20) (Asder) [!].nes`
  - `Cobra Mission (CN-27) (Asder) [!].nes`
  - `Fighting Hero III (Unl) [!].nes`
  - `Master Shooter (CN-26) (Unl) [!].nes`
  - `Zhen Ben Xi You Ji (Asder) [!].nes`

### Mapper 096 — Oeka Kids
- **廠商**：Oeka Kids
- **技術**：PPU Read Latch — 根據 PPU 讀取的掃描線位置即時變更 CHR Bank
- **代表作**：《麵包超人繪圖板》
- **實作價值**：高 — 挑戰週期級 PPU 模擬
- **測試 ROM**（2）：
  - `Oeka Kids - Anpanman to Oekaki Shiyou!! (J) [!].nes`
  - `Oeka Kids - Anpanman no Hiragana Daisuki (J).nes`

### Mapper 241 — 小霸王 (Subor)
- **廠商**：小霸王
- **技術**：多層 WRAM 映射，用於儲存學習進度
- **代表作**：小霸王學習卡 (1-12 冊)
- **實作價值**：中 — 文化代表性強
- **測試 ROM**（5）：
  - `12-in-1 (Hwang Shinwei) [p1][!].nes`
  - `Education Computer 26-in-1 (R) [!].nes`
  - `Portable FC-LCD 14-in-1 Game (Ch) [!].nes`
  - `Portable FC-LCD 14-in-1 Study (Ch) [!].nes`
  - `Portable FC-LCD S1 (SB01) Eng-Chi Dictionary 1 (Ch) [!].nes`

### Mapper 191 — 中文字庫專用
- **廠商**：漢化專用
- **技術**：細碎 CHR 映射，專為漢字設計的 1KB 以下細分 CHR Banking
- **代表作**：《超級機器人大戰》中文版
- **實作價值**：中 — 提升漢化字體支援
- **測試 ROM**（5）：
  - `Downtown Special - Kunio-kun no Jidaigeki Dayo Zenin Shuugou! (J) [T+ChS_axi,ahe].nes`
  - `Downtown Special - Kunio-kun no Jidaigeki Dayo Zenin Shuugou! (J) [T+ChT_axi,ahe].nes`
  - 及其他 3 個漢化版本

### Mapper 209 — JY Company (CHR Latch)
- **廠商**：JY Company
- **技術**：Mapper 090 邏輯 + 類似 MMC2 的自動換頁功能
- **代表作**：早期 JY Company 作品
- **實作價值**：中 — JY 系列技術分支
- **備註**：已實作 Mapper 090 基礎架構，需擴充 CHR Latch
- **測試 ROM**（5）：
  - `Mike Tyson's Punch-Out!! (Unl) [!].nes`
  - `Power Rangers III (Unl) [!].nes`
  - `Power Rangers IV (Unl) [!].nes`
  - `Shin Samurai Spirits 2 - Haoumaru Jigoku Hen (Ch) [b1].nes`
  - `Shin Samurai Spirits 2 - Haoumaru Jigoku Hen (Ch) [b2].nes`

### Mapper 211 — JY Company (Extended NT)
- **廠商**：JY Company
- **技術**：Mapper 090 基礎 + 擴充 NT 控制
- **代表作**：《真人快打》擴充版
- **實作價值**：高 — 090 架構的完全體
- **備註**：已實作 Mapper 090，需擴充 extended NT mapping
- **測試 ROM**（4）：
  - `2-in-1 - Donkey Kong Country 4 + Jungle Book 2 (Unl) [!].nes`
  - `Tiny Toon Adventures 6 (Unl) [!].nes`
  - `2-in-1 - Donkey Kong Country + Jungle Book (Unl).nes`
  - `2-in-1 - Donkey Kong Country 4 + Jungle Book 2 (Unl) [t1].nes`

### Mapper 012 — DBDROM (Bus Conflict)
- **廠商**：DBDROM
- **技術**：利用寫入時匯流排衝突產生的電位變化來切換 Bank
- **代表作**：早期匯流排衝突遊戲
- **實作價值**：中 — 基礎硬體缺陷模擬
- **測試 ROM**（4）：
  - `Kirakira Star Night DX (U) (PD).nes`
  - `Dragon Ball Z 5 (Ch).nes`
  - `Dragon Ball Z Super (Ch) [f1].nes`
  - `255-in-1 (Mapper 204) [p1].nes`

---

## 🟡 ROM 不足

### Mapper 126 — Power Joy (台灣) Multicart
- **廠商**：Power Joy
- **技術**：多層選單跳轉，處理大型 ROM (4MB+) 的多級 Banking 邏輯
- **實作價值**：低 — 支援合卡系列
- **測試 ROM**（1）：
  - `PowerJoy 84-in-1 (PJ-008) (Unl) [!].nes`

---

## 🔴 無 ROM — 暫緩實作

### Mapper 153 — Bandai LZ93D50 + WRAM
- **技術**：5-bit PRG bank、CHR-RAM、8KB WRAM、latch IRQ
- **狀態**：已實作（MAPPER_STATUS 中列為 ❓ 待確認），但 ROM 庫中無對應遊戲
- **備註**：非常稀有，Bandai 專用卡匣

### Mapper 157 — Bandai Datach (條碼機)
- **技術**：I2C EEPROM + 條碼掃描器數據格式
- **代表作**：《七龍珠 Z》條碼機版
- **狀態**：ROM 庫中無對應遊戲
- **備註**：需要特殊外設模擬（條碼掃描器），實作門檻高

### Mapper 192 — 漢化 MMC3 變體
- **技術**：CHR-ROM/RAM 混合，類似 074 但切換位址與硬體連線不同
- **代表作**：《吞食天地 II》漢化變體
- **狀態**：ROM 庫中無對應遊戲

### Mapper 194 — 漢化 MMC3 變體
- **技術**：特殊鏡像控制，針對中文對話框顯示優化
- **代表作**：早期台灣漢化作品
- **狀態**：ROM 庫中無對應遊戲
- **實作價值**：低

---

## 🟠 已實作（MAPPER_STATUS 待確認 → 現有 ROM）

以下 mapper 已在 AprNes 中實作，但 MAPPER_STATUS 列為 ❓。本次已找到測試 ROM：

| Mapper | 測試 ROM | 備註 |
|:------:|---------|------|
| 209 | Mike Tyson's Punch-Out!! (Unl) [!] 等 5 個 | 可進行人工驗證 |
| 211 | DKC 4 + Jungle Book 2 (Unl) [!] 等 4 個 | 可進行人工驗證 |
| 210 | ⚠️ Mortal Kombat 2 (Unl) [!] — header 誤標（實為 090） | 仍需尋找真正的 210 ROM |

---

## 建議實作優先順序

| 優先級 | Mapper | 理由 |
|:------:|:------:|------|
| **P1** | 074, 163, 176 | 極高價值：漢化 RPG、南晶、外星核心 |
| **P2** | 177, 164, 112 | 高價值：外星 RPG、南晶後期、台灣 Asder |
| **P3** | 096, 209, 211 | 技術挑戰：PPU Latch、JY 系列擴充 |
| **P4** | 241, 191, 012 | 中等價值：小霸王、漢化字庫、Bus Conflict |
| **P5** | 126 | 低價值：合卡 |
| **暫緩** | 153, 157, 192, 194 | 無 ROM 或特殊外設需求 |

---

測試 ROM 位置：`temp2/mapper###/`（不納入 git）
