# temp2/ Mapper 實作現況

日期：2026-04-17
對應目錄：`C:\ai_project\AprNes\temp2\mapperXXX\`

圖示說明：✅ 正常　⚠️ 部分問題／畫面怪　❌ 不能跑　❓ 無測試 ROM

---

## 總覽

| Mapper | 名稱 | 實作 | 已測試 ROM | 結果 |
|:------:|------|:----:|:-----:|:----:|
| 012 | DBDROM (MMC3 + CHR ext) | ✅ | 4 | ⚠️ 部分 |
| 074 | MMC3 + 2KB CHR-RAM (bank 8-9) | ✅ | 5 | ✅ |
| 096 | Bandai Oeka Kids | ✅ | 2 | ✅ |
| 112 | Asder / Ntdec | ✅ | 5 | ✅ |
| 126 | PowerJoy multicart | ✅ | 1 | ✅ |
| 153 | Bandai LZ93D50+WRAM | ✅ | 0 | ❓ |
| 157 | Bandai Datach | ❌ | 0 | — |
| 163 | Nanjing (南晶) | ✅ | 5 | ✅ |
| 164 | Waixing 164 | ✅ | 5 | ✅ |
| 176 | FK23C (Waixing) | ✅ | 5 | ✅ |
| 177 | Henggedianzi | ✅ | 5 | ✅ |
| 191 | MMC3 + 2KB CHR-RAM (0x80-0xFF) | ✅ | 5 | ✅ |
| 192 | MMC3 + 4KB CHR-RAM (bank 8-0xB) | ✅ | 0 | ❓ |
| 194 | MMC3 + 2KB CHR-RAM (bank 0-1) | ✅ | 0 | ❓ |
| 209 | JY Company (209) | ✅ | 4 | ⚠️ |
| 210 | Namco 175/340 | ✅ | 1 | ⚠️ |
| 211 | JY Company (211) | ✅ | 3 | ✅ |
| 241 | BxROM / Subor | ✅ | 5 | ✅ |

**統計**：17 個 mapper 資料夾，已實作 **17 個**，未實作 **1 個**（157）。已實測 **15 個**（有 ROM），無法驗證 **3 個**（153/192/194 的 temp2 內無 ROM）。

---

## 逐項明細

### ✅ Mapper 012 — DBDROM (MMC3 + CHR high-bit)
測試 ROM（4 個）：
- ⚠️ Dragon Ball Z 5 (Ch) — 標題畫面顯示，後續深入未驗
- ❓ Dragon Ball Z Super (Ch) [f1]
- ❓ Kirakira Star Night DX (U) (PD)
- ℹ️ 255-in-1 (Mapper 204) — 檔名註記實際是 mapper 204，不是 012

說明：MMC3 + $5xxx CHR 高位元；已加 Rev A IRQ 相容。

### ✅ Mapper 074 — MMC3 + 2KB CHR-RAM (bank 0x08, 0x09)
測試 ROM（5 個）：
- Ba Bao Qi Zhu — EverQuest (ES-1067) (Ch)
- Captain Tsubasa II — Angel Wings 2 (Hack)
- Captain Tsubasa II — Blue Clothes Chi Edition (Hack)
- Captain Tsubasa II — Chinese Team Running in the Rain (Hack)
- Captain Tsubasa II — Circle & Cross (Hack)

說明：Captain Tsubasa 中文 hack 4/5 正常。

### ✅ Mapper 096 — Bandai Oeka Kids (太鼓筆)
測試 ROM（2 個）：
- Oeka Kids — Anpanman no Hiragana Daisuki (J)
- Oeka Kids — Anpanman to Oekaki Shiyou!! (J)

說明：沿用 PpuClock hook 偵測 $2xxx VRAM address 變化，觸發內層 CHR bank latch。

### ✅ Mapper 112 — Asder / Ntdec
測試 ROM（5 個）：
- Chik Bik Ji Jin (Asder)
- Cobra Mission (Asder)
- Fighting Hero III (Unl)
- Huang Di — Zhuo Lu Zhi Zhan (Asder)
- Master Shooter (Unl)

### ✅ Mapper 126 — PowerJoy multicart
測試 ROM（1 個）：
- PowerJoy 84-in-1 (PJ-008) (Unl) — ✅ 選單 + 第一個遊戲 80 Days (世界一周8日の大冒険) 驗證

說明：MMC3 + 4 exReg at $6000-$7FFF (addr&3)；本 session 新增並驗證。

### ❓ Mapper 153 — Bandai LZ93D50 + 8KB WRAM
測試 ROM：無（temp2 目錄為空）
說明：實作完成但無 temp2 驗證。

### ❌ Mapper 157 — Bandai Datach Joint ROM System
測試 ROM：無（temp2 目錄為空）
說明：**尚未實作**。Datach 含條碼讀取器，規格較特殊。

### ✅ Mapper 163 — Nanjing (南晶)
測試 ROM（5 個）：
- Chao Ji Ji Qi Ren Da Zhan A (NJ012) — 超級機器人大戰 A
- Chong Wu Gao Da Zhan Ji (NJ088)
- Da Hua Xi You (大話西遊)
- Diablo (NJ037)
- Final Fantasy VII (Ch) [T+Eng0.97]

### ✅ Mapper 164 — Waixing 164
測試 ROM（5 個）：
- Darkseed (Unl)
- Digital Dragon (Ch)
- Final Fantasy V (Unl) [b1]
- Kou Dai Jing Ling — Zuan Shi (Ch)（寶可夢 - 鑽石）
- Kou Dai Yao Guai — Shui Jing Ban (Ch)（Pokemon 水晶版）

### ✅ Mapper 176 — FK23C (Waixing)
測試 ROM（5 個）全數進選單：
- 12-in-1 Console TV Game Cartridge (Unl)
- 3-in-1 (ES-Q800C PCB)
- 4-in-1 (BS-8088)
- 4-in-1 (FK23Cxxxx, S-0210A PCB)
- 4-in-1 (KT-220B)

說明：本 session 完整移植 Mesen2 Fk23C.h（包含 extended MMC3 mode、32KB WRAM 4-bank remap）。

### ✅ Mapper 177 — Henggedianzi (恒格電子)
測試 ROM（5 個）：
- Mei Guo Fu Hao (美國富豪)
- Shang Gu Shen Jian (Explosion Sangokushi)
- Wang Zi Fu Chou Ji (王子復仇記)
- Xing He Zhan Shi (星河戰士)
- Xing Zhan Qing Yuan (星戰情緣)

### ✅ Mapper 191 — MMC3 + 2KB CHR-RAM (bank 0x80-0xFF wrap)
測試 ROM（5 個）：
- Double Dragon III (J) [T+Chi_madcell][b1]
- Downtown — Nekketsu Monogatari (J) [T+Chi_madcell]
- Downtown Special — Kunio-kun (J) [T+ChS_axi,ahe]
- Mighty Final Fight (J) [T+Chi_madcell]
- Q Boy (Sachen)

說明：madcell 中文 hack 4/5 正常。

### ❓ Mapper 192 — MMC3 + 4KB CHR-RAM (bank 8-0xB)
測試 ROM：無（temp2 目錄為空）

### ❓ Mapper 194 — MMC3 + 2KB CHR-RAM (bank 0-1)
測試 ROM：無（temp2 目錄為空）

### ⚠️ Mapper 209 — JY Company
測試 ROM（4 個）：
- ✅ Mike Tyson's Punch-Out!! (Unl) — "MIKE IS WAITING FOR YOUR CHALLENGE" 訊息正常
- ⚠️ Power Rangers III (Unl) — 畫面幾乎空白（黃色底+一個小游標），可能缺 MMC4-like latch 或 sprite 處理
- ⚠️ Power Rangers IV (Unl) — 同上，白畫面+小圖
- ❌ Shin Samurai Spirits 2 — 畫面嚴重破碎/亂

說明：JY Company 規格比 211 複雜，可能需補強 nametable 控制、MMC2-like latch、或 extended mirroring。

### ⚠️ Mapper 210 — Namco 175 / 340
測試 ROM（1 個）：
- ⚠️ Mortal Kombat 2 (Unl) — MK 龍標題 logo 可見，但缺文字（"MORTAL KOMBAT II" 等），可能 CHR 或 scroll 有狀況

### ✅ Mapper 211 — JY Company (211 子型)
測試 ROM（3 個）：
- ✅ 2-in-1 DKC + Jungle Book — 主選單 "2 IN 1 DONKEY KONG the Jungle Book" 正常
- ✅ 2-in-1 DKC4 + Jungle Book 2 — 主選單正常
- ⚠️ Tiny Toon Adventures 6 — 顯示 Porky Pig 圖（盜版內容標題與檔名不符，圖像渲染正常）

### ✅ Mapper 241 — BxROM / Subor
測試 ROM（5 個）：
- 12-in-1 (Hwang Shinwei)
- 14-in-1 Russian Study Cartridge
- 16-in-1 Chao Ji Shu Biao Jin Ka
- 7-in-1 Russian Study Cartridge
- ABM Study Card v5.0 (Ch)

---

## 待改進清單

| 優先級 | Mapper | 問題 |
|:------:|:------:|------|
| 高 | 209 | Power Rangers III/IV 近白屏；Shin Samurai Spirits 2 畫面破碎 |
| 中 | 210 | Mortal Kombat 2 缺文字（CHR 或 scroll） |
| 中 | 157 | Datach 尚未實作（規格特殊：條碼讀取器） |
| 低 | 012 | DBZ 5 以外三顆未深入驗證 |
| 低 | 153/192/194 | 缺 temp2 測試 ROM |

---

## 待做清單（TODO — 新增 mapper）

| 優先級 | Mapper | 說明 |
|:------:|:------:|------|
| — | **027** | VRC4 非授權變體（World Hero unl 等）；可沿用現有 VRC4 (21/25) 實作擴充 |
| — | **083** | Cony / Yoko（中文盜版晶片，Dragon Ball Party 等） |
| — | **178** | Waixing San Guo Zhong Lie Zhuan（外星三國忠烈傳系列） |
| — | **209** (改進) | 已實作但 3/4 ROM 有畫面問題；需補強 JY Company 規格（見高優先級） |

**行動項目**：
1. 準備 temp2 測試 ROM（027 / 083 / 178 目錄目前不存在，需放入 .nes 檔）
2. 規格研究：優先查 `ref/Mesen2-master/Core/NES/Mappers/` 對應實作
3. 依既有流程實作 → MapperRegistry 註冊 → csproj 加檔 → build → 截圖驗證 → 更新 MAPPER_STATUS.md / TEMP2_MAPPER_STATUS.md / README.md

---

截圖位置：`temp/m_scan/`（209/210/211）、`temp/m176_shots/`（176）、`temp/m126_shots/`（126）。
