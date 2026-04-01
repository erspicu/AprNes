# AprNes Mapper 實作狀態

**已實作：65 個　　預備實作：16 個　　最後更新：2026-04-02**

結果說明：✅ 正常　⚠️ 部分問題　❌ 有問題　❓ 待確認／不明

---

## 校驗摘要

| 結果 | 數量 | Mapper 列表 |
|:----:|:----:|------------|
| ✅ 正常 | 60 | 000, 001, 002, 003, 004, 005, 007, 009, 010, 011, 013, 016, 018, 019, 020, 021, 022, 023, 024, 025, 026, 029, 032, 033, 034, 065, 066, 067, 068, 069, 070, 071, 072, 075, 076, 077, 078, 079, 080, 082, 085, 087, 088, 089, 090, 093, 095, 097, 118, 119, 140, 152, 154, 159, 180, 184, 185, 206, 228, 232 |
| ⚠️ 部分問題 | 1 | 064 |
| ❓ 待確認 | 4 | 153, 209, 210, 211 |
| **合計已實作** | **65** | |

---

## 已實作 — 人工校驗紀錄

校驗日期：**2026-03-27**（部分 2026-03-30 補驗）

| Mapper | 名稱 / 晶片 | 校驗結果 | 測試遊戲 | 說明 |
|:------:|------------|:--------:|---------|------|
| **000** | NROM | ✅ | — | 固定 32KB PRG，8KB CHR |
| **001** | MMC1 | ✅ | Legend of Zelda, Metroid, MegaMan 2 | 序列寫入；16KB×2 PRG，4KB×2 CHR；4種鏡像 |
| **002** | UxROM | ✅ | Castlevania, MegaMan, Ghosts & Goblins | 16KB PRG 切換 + 固定末，CHR-RAM |
| **003** | CNROM | ✅ | Solomon's Key, Gradius | 固定 32KB PRG，8KB CHR 切換 |
| **004** | MMC3 / MMC3 RevA / MMC6 | ✅ | SMB2, SMB3, MegaMan 3–6 | 8KB×4 PRG，1KB×8 CHR；A12 掃描線 IRQ |
| **005** | MMC5 | ✅ | CV3, Gemfire, L'Empereur, ROTK | PRG/CHR banking(4 modes)、scanline IRQ、extended attribute、nametable mapping。缺：vertical split、MMC5 audio |
| **007** | AxROM | ✅ | Battletoads, Wizards & Warriors | 32KB PRG 切換，CHR-RAM，single-screen |
| **009** | MMC2 | ✅ | Punch-Out!! (U) | PPU Latch 自動換頁。Gradius II (J)(VC) 為 iNES header 錯誤（實為 mapper 25），已透過 RomDatabase 修正 |
| **010** | MMC4 | ✅ | Fire Emblem, Famicom Wars | MMC2 演進版；PPU Latch 延遲更新 |
| **011** | Color Dreams | ✅ | Crystal Mines, Pesterminator | 32KB PRG + 8KB CHR（非授權） |
| **013** | CPROM | ✅ | Videomation | 固定 32KB PRG；16KB CHR-RAM（上半 4KB 切換）。Glider Expansion 為 mapper 29（RomDB 修正） |
| **016** | Bandai FCG-1/2 / LZ93D50 | ✅ | Dragon Ball (J), DBZ Kyoushuu Saiya Jin | $6000/$8000 regs；CPU cycle / latch IRQ；EEPROM stub |
| **018** | Jaleco SS8806 | ✅ | Ninja Jajamaru, Pizza Pop!, Magic John | 3×8KB PRG + 8×1KB CHR；nibble 寫入；可變寬度 IRQ |
| **019** | Namco 163 | ✅ | Splatterhouse (J), Rolling Thunder 2 (J) | 8ch 波形音效；≥0xE0 映射 CIRAM；15-bit 上計數 IRQ |
| **020** | FDS 磁碟機 | ✅ | DK, SMB, Bubble Bobble, Dracula II 等 10+ | BIOS + PRG-RAM + CHR-RAM；磁碟 I/O；IRQ timer；wavetable + FM |
| **021** | Konami VRC4a/c | ✅ | Wai Wai World 2, Goemon Gaiden 2 | 4×8KB PRG；8×1KB CHR；prescaler IRQ |
| **022** | Konami VRC2a | ✅ | TwinBee 3 (J) | CHR index >>1 |
| **023** | Konami VRC2b | ✅ | Contra (J), Getsufuu Maden (J) | CHR index 不右移 |
| **024** | Konami VRC6a | ✅ | Akumajou Densetsu (J) | 3ch 擴充音效；prescaler IRQ |
| **025** | Konami VRC4b/d | ✅ | Gradius II (J), TMNT (J) | VRC4 A0/A1 對調 |
| **026** | Konami VRC6b | ✅ | Esper Dream 2 (J), Madara (J) | VRC6 A0/A1 對調 |
| **029** | Sealie Computing | ✅ | Glider Expansion - Mad House (PD) | 16KB switchable + 16KB fixed；32KB CHR-RAM；8KB WRAM |
| **032** | Irem G-101 | ✅ | Image Fight (J), Major League (J) | SubMapper1=Major League |
| **033** | Taito TC0190 | ✅ | Akira (J), Don Doko Don (J) | addr&0xA003 decode |
| **034** | Nina-1 | ✅ | Deadly Towers (U), Impossible Mission II | CHR-RAM/ROM 變體 |
| **064** | Tengen RAMBO-1 | ⚠️ | Shinobi ✅ / Klax ⚠️ | Klax 畫面異常（停留標題循環） |
| **065** | Irem H-3001 | ✅ | Daiku no Gen San 2 (J) | 16-bit CPU cycle IRQ；鏡像邏輯修復 |
| **066** | GxROM | ✅ | DragonBall (J), Gumshoe (U) | PRG modulo 修復 |
| **067** | Sunsoft-3 | ✅ | Fantasy Zone 2 (J), Mito Koumon II (J) | 16-bit 下計數 IRQ |
| **068** | Sunsoft #4 | ✅ | AfterBurner II (J), Maharaja (J) | CHR-as-nametable |
| **069** | Sunsoft FME-7 / 5B | ✅ | Batman (J), Gimmick! (J) | YM2149 3ch 擴展音效；CPU cycle IRQ |
| **070** | Bandai 74161/32 | ✅ | Kamen Rider Club (J), Arkanoid II (J) | bit7 啟發式偵測 mislabeled ROM |
| **071** | Camerica / BF909x | ✅ | Firehawk (U), Linus Spacehead (U) | BF9097 variant 自動偵測+單屏鏡像 |
| **072** | Jaleco JF-17 | ✅ | Pinball Quest (J), Moero!! Juudou Warriors (J) | Latch 機制（prgFlag/chrFlag） |
| **075** | Konami VRC1 | ✅ | Ganbare Goemon! (J) | $9000 bit0=H/V |
| **076** | Namco 109 | ✅ | Battle City Hack V4, Megami Tensei (J) | A15 全域解碼；PRG 指標快取 |
| **077** | IremLrog017 | ✅ | Napoleon Senki (J) | slot0=CHR-ROM 2KB，slots1-3=CHR-RAM 6KB |
| **078** | Irem 74HC161/32 | ✅ | Holy Diver (J), Uchuusen Cosmo Carrier (J) | subMapper 鏡像差異修復 |
| **079** | NINA-03/06 (AVE) | ✅ | Blackjack (AVE), Deathbots (AVE) | (addr&0xE100)==0x4100 |
| **080** | Taito X1-005 | ✅ | Minelvaton Saga (J), Fudou Myouou Den (J) | RAM unlock；mirroring 反轉修復 |
| **082** | Taito X1-017 | ✅ | SD Keiji Blader (J), Harikiri Stadium (J) | SRAM unlock seq；mirroring 反轉修復 |
| **085** | Konami VRC7 | ✅ | Lagrange Point (J) | OPLL (YM2413) FM 合成音效；CHR-RAM banking 修復 |
| **087** | Jaleco JF-09/10/18 | ✅ | Argus (J), City Connection (J), Goonies (J) | D0/D1 bit-swap |
| **088** | Namco 118 / 634 | ✅ | Dragon Spirit (J), Quinty (J) | R0/R1=2KB CHR(low 64KB)，R2-R5=1KB CHR(high 64KB) |
| **089** | Sunsoft-2 (Ikki) | ✅ | Tenka no Goikenban (J) | bit3=single-screen |
| **090** | JY Company | ✅ | Mortal Kombat 2 (Unl) | 4 PRG/CHR modes；CPU/A12 IRQ；multiply reg；NT control |
| **093** | Sunsoft-2 (FZ2) | ✅ | Fantasy Zone (J), Shanghai (J) | CHR-RAM |
| **095** | Namco 118 DxROM | ✅ | Dragon Buster (J) | reg[0][1] bit5=NT select |
| **097** | Irem TAM-S1 | ✅ | Kaiketsu Yanchamaru (J) | 首 16KB 固定，末 16KB 切換 |
| **118** | TxSROM | ✅ | Ys III (J), Armadillo (J), NES Play Action Football | CHR bit7 控制 nametable |
| **119** | TQROM | ✅ | High Speed (U) | CHR bank 0x40–0x7F 映射 CHR-RAM |
| **140** | Jaleco JF-11/14 | ✅ | Doraemon (J), Bio Senshi Dan (J) | bits[5:4]=PRG，bits[3:0]=CHR；PRG/CHR bits 修正 |
| **152** | Bandai single-screen | ✅ | Arkanoid II Prototype (J) | bit7→single-screen（bit6→bit7 修復） |
| **153** | Bandai LZ93D50+WRAM | ❓ | — | 已實作，ROM 庫中無對應遊戲 |
| **154** | Namco 129 | ✅ | Devil Man (J) | Mapper088 + bit6 動態單屏鏡像 |
| **159** | Bandai LZ93D50 alias | ✅ | — | 016 sub5 別名，隨 016 驗證通過 |
| **180** | Crazy Climber | ✅ | Crazy Climber (J) | 首 16KB 固定，末 16KB 切換 |
| **184** | Sunsoft-1 / FC-08 | ✅ | Wing of Madoola (J), Atlantis no Nazo (J) | 下 4KB + 上 4KB（bit7 常設）CHR |
| **185** | CNROM + copy-protect | ✅ | B-Wings (J), Bird Week (J), Mighty Bomb Jack (J) | nibble 保護 heuristic |
| **206** | Namco 108 | ✅ | Karnov (J) | MMC3 雛形；無 IRQ |
| **209** | JY Company (209) | ❓ | Mike Tyson's Punch-Out!! (Unl) [!] 等 | Mapper 090 + CHR latch（待人工驗證） |
| **210** | Namco 175/340 | ❓ | ⚠️ MK2 為 header 誤標（實為 090） | SubMapper1=175（無IRQ）；SubMapper2=340（IRQ+NT） |
| **211** | JY Company (211) | ❓ | DKC4 + Jungle Book 2 (Unl) [!] 等 | Mapper 090 + extended NT（待人工驗證） |
| **228** | Action 52 | ✅ | Cheetahmen II (U) | addr+data 編碼；chipSelect；16/32KB mode |
| **232** | Camerica BF9096 | ✅ | Quattro Adventure (U), Quattro Sports (U) | 外層+內層二段 PRG；Aladdin variant |

---

## 預備實作清單

來源：new NES Mapper 實作清單與建議.xlsx（2026-04-02）

### 建議優先順序

| 優先級 | Mapper | 理由 |
|:------:|:------:|------|
| **P1** | 074, 163, 176 | 極高價值：漢化 RPG、南晶、外星核心 |
| **P2** | 177, 164, 112 | 高價值：外星 RPG、南晶後期、台灣 Asder |
| **P3** | 096, 209, 211 | 技術挑戰：PPU Latch、JY 系列擴充 |
| **P4** | 241, 191, 012 | 中等價值：小霸王、漢化字庫、Bus Conflict |
| **P5** | 126 | 低價值：合卡 |
| **暫緩** | 153, 157, 192, 194 | 無 ROM 或特殊外設需求 |

### 🟢 已備 ROM — 可直接實作

| Mapper | 廠商 / 類別 | 代表作 | 技術要點 | 價值 | 測試 ROM 數 |
|:------:|------------|--------|---------|:----:|:----------:|
| **074** | 漢化版 MMC3 | 重裝機兵、吞食天地 II | CHR-RAM 變體：特定 Bank 改 RAM 產生中文字 | 高 | 5 |
| **163** | 南晶科技 | 神奇寶貝、FF VII | 防寫鎖定序列、模擬器環境偵測 | 極高 | 5 |
| **176** | 外星科技 | 仙劍奇俠傳、大富翁 | 混合 Banking（16KB/8KB）、存檔致能電路 | 極高 | 5 |
| **177** | 外星科技 | 魔界大空戰、新神雕俠侶 | SRAM 鎖定狀態機、嚴格寫入保護序列 | 高 | 5 |
| **164** | 南晶科技（後期） | 幻想水滸傳 | 暫存器位址偏移（與 163 類似） | 中 | 5 |
| **112** | 台灣 Asder | 三國志、封神榜 | 暫存器偏移、IRQ 時序微調 | 高 | 5 |
| **096** | Oeka Kids | 麵包超人繪圖板 | PPU Read Latch：掃描線位置即時變更 CHR Bank | 高 | 2 |
| **241** | 小霸王 (Subor) | 學習卡 1-12 冊 | 多層 WRAM 映射 | 中 | 5 |
| **191** | 中文字庫專用 | 超級機器人大戰中文版 | 細碎 CHR 映射（1KB 以下細分） | 中 | 5 |
| **209** | JY Company | 早期 JY 作品 | Mapper 090 + CHR Latch（MMC2-style） | 中 | 5 |
| **211** | JY Company | 真人快打擴充版 | Mapper 090 + extended NT control | 高 | 4 |
| **012** | DBDROM | 匯流排衝突遊戲 | Bus Conflict 電位變化切換 Bank | 中 | 4 |

### 🟡 ROM 不足

| Mapper | 廠商 / 類別 | 技術要點 | 價值 | 測試 ROM 數 |
|:------:|------------|---------|:----:|:----------:|
| **126** | Power Joy (台灣) | 多層選單跳轉、大型 ROM (4MB+) 多級 Banking | 低 | 1 |

### 🔴 無 ROM — 暫緩實作

| Mapper | 廠商 / 類別 | 技術要點 | 備註 |
|:------:|------------|---------|------|
| **153** | Bandai LZ93D50+WRAM | 5-bit PRG、CHR-RAM、8KB WRAM、latch IRQ | 已實作（❓ 待確認），ROM 庫無對應遊戲 |
| **157** | Bandai Datach | I2C EEPROM + 條碼掃描器 | 需特殊外設模擬，實作門檻高 |
| **192** | 漢化 MMC3 變體 | CHR-ROM/RAM 混合（類似 074） | ROM 庫無對應遊戲 |
| **194** | 漢化 MMC3 變體 | 特殊鏡像控制 | ROM 庫無對應遊戲，價值低 |

---

測試 ROM 位置：`temp/mapper###/`（已實作）、`temp2/mapper###/`（預備實作）。均不納入 git。
