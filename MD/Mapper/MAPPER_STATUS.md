# AprNes Mapper 實作狀態

**已實作：78 個　　預備實作：4 個　　最後更新：2026-04-17**

結果說明：✅ 正常　⚠️ 部分問題　❌ 有問題　❓ 待確認／不明

---

## 校驗摘要

| 結果 | 數量 | Mapper 列表 |
|:----:|:----:|------------|
| ✅ 正常 | 67 | 000, 001, 002, 003, 004, 005, 007, 009, 010, 011, 013, 016, 018, 019, 020, 021, 022, 023, 024, 025, 026, 029, 032, 033, 034, **074**, 064, 065, 066, 067, 068, 069, 070, 071, 072, 075, 076, 077, 078, 079, 080, 082, 085, 087, 088, 089, 090, 093, 095, 097, **112**, 118, 119, 140, 152, 154, 159, **164**, **177**, 180, 184, 185, **191**, 206, 228, 232, **241** |
| ⚠️ 部分問題 | 0 | — |
| ❓ 待確認 | 6 | 153, **192**, **194**, 209, 210, 211 |
| **合計已實作** | **73** | |

### 2026-04-17 session 新增（8 個）
- ✅ 已驗證（4 實裝 ✓ 4 ROM 測過）：074 (MMC3 ChrRam 8-9), 112 (Asder), 164 (Waixing 164), 177 (Henggedianzi), 191 (MMC3 ChrRam 0x80-FF wrap), 241 (BxROM/Subor)
- ❓ 已實裝但無 ROM 驗證：192 (MMC3 ChrRam 8-0xB), 194 (MMC3 ChrRam 0-1)

### 2026-04-17 session 嘗試失敗（暫緩）
| Mapper | 失敗原因 |
|:------:|--------|
| 126 (PowerJoy multicart) | 多層選單跳轉 + 大型 ROM (4MB+) 多級 banking，測試 ROM 僅 1 顆 |

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
| **064** | Tengen RAMBO-1 | ✅ | Shinobi, Klax | TriCNES timing 移植後 Klax 畫面恢復正常（2026-04-07） |
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
| **241** | BxROM / Subor | ✅ | 學生電腦 16-in-1, 12-in-1 Hwang Shinwei, ABM Study Card | 任何 PRG 寫入 → 32KB bank 切換；極簡；實作參考 Mesen2 |
| **112** | Asder (Ntdec) | ✅ | 三國志, Cobra Mission, Fighting Hero III | $8000 reg select, $A000 data, $C000 outer CHR, $E000 mirror |
| **177** | Henggedianzi | ✅ | 恒格电子 5 ROMs (Mei Guo Fu Hao, Xing He Zhan Shi etc.) | 32KB PRG + bit5 mirroring，極簡 |
| **164** | Waixing 164 | ✅ | Darkseed, FF5 hack, 寶可夢水晶版, Digital Dragon | $5000/$5100 兩暫存器組 PRG bank 高低 nibble |
| **074** | MMC3 + 2KB CHR-RAM | ✅ | 足球小將 4 hacks (EverQuest hack 綠屏，ROM 可疑) | 獨立 Mapper004 copy + bank 0x08-0x09 redirect to 2KB CHR-RAM |
| **191** | MMC3 + 2KB CHR-RAM (wrap) | ✅ | Double Dragon III (中), 熱血物語 (中), Mighty Final Fight (中) | 同 074，bank 0x80-0xFF wrap 到 2KB RAM |
| **192** | MMC3 + 4KB CHR-RAM | ❓ | ROM 庫無對應遊戲 | 同 074，bank 0x08-0x0B redirect to 4KB CHR-RAM |
| **194** | MMC3 + 2KB CHR-RAM | ❓ | ROM 庫無對應遊戲 | 同 074，bank 0x00-0x01 redirect to 2KB CHR-RAM |
| **012** | DBDROM | ⚠️ | DBZ 5 (Ch) 標題 OK；DBZ Super/Kirakira 待確認 | MMC3 + $5xxx CHR high-bit；Rev A IRQ |
| **096** | Bandai Oeka Kids | ✅ | Anpanman Hiragana / Oekaki Shiyou | PPU 匯流排 $2xxx 遷移觸發 inner CHR bank latch（無需改 IMapper，沿用 PpuClock）|
| **163** | Nanjing (南晶) | ✅ | FF7 中文、Diablo 暗黑破壞神、大話西遊、Chao Ji Ji Qi Ren | $5000/$5100/$5200/$5300 + $5101 toggle；copy protection read $5100/$5500 |
| **173** | TXC 22211C | ❓ | 無測試 ROM | TXC chip copy-protection；$4100-$4103 accumulator + invert；$8000+ trigger output |
| **176** | FK23C (Waixing) | ✅ | 12-in-1 / 3-in-1 ES-Q800C / 4-in-1 BS-8088 / 4-in-1 FK23Cxxxx S-0210A / 4-in-1 KT-220B 全數進選單 | MMC3 super-set 完整移植 Mesen2：5 PRG modes、MMC3/CNROM CHR、extended MMC3 mode (10 regs)、4-mode mirroring、$A001 WRAM config、IRQ 2-cycle delay、subtype 2 $46/$47 swap |

---

## 預備實作清單（現況，2026-04-17 更新）

來源：new NES Mapper 實作清單與建議.xlsx（2026-04-02）

### 狀態分類

| 狀態 | 數量 | Mapper | 備註 |
|:----:|:----:|--------|------|
| ✅ 已實作 + 已驗證 | 6 | 074, 112, 164, 177, 191, 241 | 本 session 完成並 ROM 測過 |
| ❓ 已實作、無 ROM 驗證 | 2 | 192, 194 | 註冊於 MapperRegistry，邏輯同 074；待 ROM 確認 |
| ⚠️ 嘗試過、待架構改動 | 4 | 012, 096, 163, 176 | 需 Mapper004 virtual 或 VRAM hook |
| 🟡 低價值、待處理 | 1 | 126 | PowerJoy 合卡，測試 ROM 僅 1 顆 |
| 🔴 無 ROM / 外設需求 | 1 | 157 | Bandai Datach I2C+條碼機 |

### 建議優先順序（剩餘項）

| 優先級 | Mapper | 理由 |
|:------:|:------:|------|
| **P1** | 012 | MMC3 + CHR bank 擴展；獨立 impl 可參考 074 程式（簡單）|
| **P2** | 163, 176 | 極高價值：南晶寶可夢、外星 FK23C。需架構改動 |
| **P3** | 096 | Oeka Kids；需新增 IMapper.NotifyVramAddressChange hook |
| **P5** | 126 | 低價值合卡 |
| **暫緩** | 157 | 特殊外設 |

### ⚠️ 本 session 嘗試過但暫緩

| Mapper | 廠商 / 類別 | 嘗試結果 | 阻礙 |
|:------:|------------|---------|------|
| **012** | DBDROM (MMC3 + CHR ext) | 未做 | 時間用盡；結構與 074 類似，可獨立 impl |
| **096** | Oeka Kids | 未建檔 | 需 PPU VRAM hook，IMapper 介面不支援 |
| **163** | 南晶 (Nanjing) | 未建檔 | VRAM hook + scanline read + copy protection reads |
| **176** | 外星 (FK23C) | 未建檔 | 400+ 行 Mesen2；多 banking mode、WRAM banking、extended MMC3 mode |
| **126** | PowerJoy 合卡 | 未建檔 | 多層選單跳轉、4MB+ 多級 banking；測試 ROM 僅 1 顆 |

### 🔴 無 ROM — 暫緩實作

| Mapper | 廠商 / 類別 | 技術要點 | 備註 |
|:------:|------------|---------|------|
| **153** | Bandai LZ93D50+WRAM | 5-bit PRG、CHR-RAM、8KB WRAM、latch IRQ | 已實作（❓ 待確認），ROM 庫無對應遊戲 |
| **157** | Bandai Datach | I2C EEPROM + 條碼掃描器 | 需特殊外設模擬，實作門檻高 |
| **192** | 漢化 MMC3 變體 | 同 074（bank 8-0xB）| **已實作 2026-04-17**，待 ROM 驗證 |
| **194** | 漢化 MMC3 變體 | 同 074（bank 0-1）| **已實作 2026-04-17**，待 ROM 驗證 |

### 下一次可做方向

1. **Mapper 012**：複製 Mapper074.cs 邏輯 + 加 $4020-$5FFF 的 CHR bank extension register，獨立 impl 解決。
2. **IMapper 介面擴充**：新增 `NotifyVramAddressChange(int addr)`，由 PPU 在 vram_addr 改變時觸發。解鎖 096/163 + 其他未來 mapper。
3. **Mapper 176 (FK23C)**：最複雜但高價值。建議獨立 session 全神貫注，先用 Mesen2 為 bit-for-bit 參考。
4. **Mapper004 refactor**（可選）：若覺得 074/191/192/194 的複製過多，可把 Mapper004 改為 `virtual` 讓四個 CHR-RAM variant 繼承。但獨立 impl 目前已 work，不急。

---

測試 ROM 位置：`temp/mapper###/`（已實作）、`temp2/mapper###/`（預備實作）。均不納入 git。
