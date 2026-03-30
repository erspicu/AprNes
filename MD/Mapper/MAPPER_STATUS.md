# AprNes Mapper 實作狀態

**總計已實作：65 個　　最後更新：2026-03-30**

結果說明：✅ 正常　⚠️ 部分問題　❌ 有問題　❓ 待確認／不明

---

## 校驗摘要

| 結果 | 數量 | Mapper 列表 |
|:----:|:----:|------------|
| ✅ 正常 | 55 | 000, 001, 002, 003, 004, 005, 007, 009, 010, 011, 013, 016, 018, 019, 020, 021, 022, 023, 024, 025, 026, 029, 032, 033, 034, 065, 066, 067, 068, 069, 070, 071, 072, 075, 077, 078, 079, 080, 082, 085, 087, 088, 089, 090, 093, 097, 118, 119, 140, 152, 180, 184, 185, 206, 228, 232 |
| ⚠️ 部分問題 | 1 | 064 |
| ❌ 有問題 | 1 | 076 |
| ❓ 待確認 | 5 | 095, 153, 209, 210, 211 |
| **合計校驗** | **62** | |



---

## 人工校驗紀錄

校驗日期：**2026-03-27**

| Mapper | 校驗結果 | 說明 |
|:------:|:--------:|------|
| **000** | ✅ | |
| **001** | ✅ | |
| **002** | ✅ | |
| **003** | ✅ | |
| **004** | ✅ | |
| **005** | ✅ | 完整重寫：PRG/CHR banking、nametable mapping、scanline IRQ、extended attribute mode、Mesen2 風格 VRAM read notification。人工驗證通過（2026-03-25）：CV3 標題/遊戲正常、mmc5test split screen 正確 |
| **007** | ✅ | |
| **009** | ✅ | 人工驗證通過（2026-03-30）：Punch-Out!! 正常。Gradius II (J)(VC) 綠屏為 iNES header 錯誤（header 標 mapper 9，實際為 mapper 25 VRC4b），已透過 RomDatabase MapperOverride 修正 |
| **010** | ✅ | |
| **011** | ✅ | |
| **013** | ✅ | 人工驗證通過（2026-03-30）：Videomation 正常。Glider Expansion 系列為 iNES header 錯誤（實為 mapper 29），已透過 RomDatabase MapperOverride 修正；其中 The House / Plato's Bath House 為 bad dump（Mesen2 也綠屏） |
| **020** | ✅ | FDS 磁碟機支援（2026-03-28）：獨立於 IMapper 架構，NesCore partial class。BIOS 驗證（SHA-256）、gap-inserted 磁碟映像、IRQ timer、wavetable + FM 音效。人工驗證 10+ 遊戲（DK, SMB, Bubble Bobble, Dracula II, Galaga, Xevious 等） |
| **016** | ✅ | 人工驗證通過（含 Dragon Ball Z - Kyoushuu Saiya Jin） |
| **018** | ✅ | |
| **019** | ✅ | 人工驗證通過（2026-03-27）：Splatterhouse (J)、Chibi Maruko-Chan (J) 正常，含 Namco 163 8ch 波形音效、CIRAM nametable/CHR 映射修復 |
| **021** | ✅ | |
| **022** | ✅ | |
| **023** | ✅ | |
| **024** | ✅ | 人工驗證通過（2026-03-25）：Akumajou Densetsu (J) 正常，含 VRC6 擴展音效 |
| **025** | ✅ | 人工驗證通過（2026-03-27）：TMNT (J) 正常，VRC4d 地址線 A0/A1 修復 |
| **026** | ✅ | Esper Dream 2 - Aratanaru Tatakai (J) 已修復（2026-03-24） |
| **029** | ✅ | 新增實作（2026-03-30）：Sealie Computing。Glider Expansion - Mad House (PD) 正常。16KB switchable + 16KB fixed PRG、32KB CHR-RAM、8KB WRAM |
| **032** | ✅ | |
| **033** | ✅ | |
| **034** | ✅ | 人工驗證通過 |
| **064** | ⚠️ | Klax 畫面有問題 |
| **065** | ✅ | 人工驗證通過（2026-03-27）：Daiku no Gen San 2 (J) 正常，鏡像邏輯修復 |
| **066** | ✅ | 人工驗證通過（2026-03-27）：PRG bank modulo 修復 |
| **067** | ✅ | 人工驗證通過（2026-03-30） |
| **068** | ✅ | |
| **069** | ✅ | 人工驗證通過（2026-03-25）：含 Sunsoft 5B (YM2149) 3ch 擴展音效 |
| **070** | ✅ | 人工驗證通過（2026-03-28）：bit7 啟發式偵測修復，Arkanoid II 標題正常 |
| **071** | ✅ | 人工驗證通過（2026-03-28）：基於 Mesen2 BF909x 重寫，PRG banking + BF9097 鏡像控制 |
| **072** | ✅ | |
| **075** | ✅ | |
| **076** | ❌ | Battle City - Zen Ge Hack V4 遊戲運行問題（hack ROM） |
| **077** | ✅ | |
| **078** | ✅ | Uchuusen - Cosmo Carrier (J) 已修復（2026-03-24） |
| **079** | ✅ | |
| **080** | ✅ | 人工驗證通過（2026-03-27）：mirroring 反轉修復 |
| **082** | ✅ | 人工驗證通過（2026-03-27）：mirroring 反轉修復 |
| **085** | ✅ | 人工驗證通過（2026-03-27）：Lagrange Point (J) 正常，含 OPLL (YM2413) FM 合成音效、CHR-RAM banking 修復 |
| **087** | ✅ | |
| **088** | ✅ | 人工驗證通過（2026-03-30） |
| **089** | ✅ | 人工驗證通過（2026-03-27） |
| **090** | ✅ | 人工驗證通過（2026-03-30）：Mortal Kombat 2 (Unl) 正常（iNES header mapper 210→RomDatabase 修正為 090） |
| **093** | ✅ | |
| **095** | ❓ | 待確認 |
| **097** | ✅ | |
| **118** | ✅ | 人工驗證通過（2026-03-25） |
| **119** | ✅ | |
| **140** | ✅ | 人工驗證通過（2026-03-30）：PRG/CHR bank bits 修正（原本反了） |
| **152** | ✅ | 人工驗證通過（2026-03-27）：mirroring bit 修復（bit6→bit7） |
| **153** | ❓ | 不明 |
| **180** | ✅ | |
| **184** | ✅ | |
| **185** | ✅ | |
| **206** | ✅ | 人工驗證通過（2026-03-27）：CHR/PRG bank modulo 修復 |
| **209** | ❓ | 暫時找不到測試 ROM（2026-03-30） |
| **210** | ❓ | 暫時找不到測試 ROM（2026-03-30）；先前綠屏為 MK2 ROM（實為 mapper 090，已透過 RomDatabase 修正） |
| **211** | ❓ | 暫時找不到測試 ROM（2026-03-30） |
| **228** | ✅ | |
| **232** | ✅ | |

---

| Mapper | 名稱 / 晶片 | PRG / CHR 架構 | 測試遊戲 | 結果 |
|:------:|------------|----------------|---------|:----:|
| **000** | NROM | 固定 32KB PRG，8KB CHR | — | ✅ |
| **001** | MMC1 | 序列寫入；16KB×2 PRG，4KB×2 CHR；4種鏡像 | Legend of Zelda, Metroid, MegaMan 2 | ✅ |
| **002** | UxROM | 16KB PRG 切換 + 固定末，CHR-RAM | Castlevania, MegaMan, Ghosts & Goblins | ✅ |
| **003** | CNROM | 固定 32KB PRG，8KB CHR 切換 | Solomon's Key, Gradius | ✅ |
| **004** | MMC3 | 8KB×4 PRG，1KB×8 CHR；A12 掃描線 IRQ | SMB2, SMB3, MegaMan 3–6 | ✅ |
| **004** | MMC3 RevA | MMC3 變體（RevA 行為差異） | — | ✅ |
| **004** | MMC6 | MMC3 + 1KB PRG-RAM | — | ✅ |
| **005** | MMC5 | 8KB×4 PRG，1KB×8 CHR；PRG/CHR banking(4 modes)、scanline IRQ、extended attribute mode、nametable mapping、Mesen2 風格 VRAM read notification。缺：vertical split、MMC5 audio | Castlevania III, Gemfire, L'Empereur, ROTK | ✅ |
| **007** | AxROM | 32KB PRG 切換，CHR-RAM，single-screen | Battletoads, Wizards & Warriors | ✅ |
| **009** | MMC2 | PPU Latch 自動換頁；16KB×2 PRG，4KB×2 CHR | Punch-Out!! (U) | ✅ |
| **010** | MMC4 | MMC2 演進版；16KB PRG；PPU Latch 延遲更新 | Fire Emblem, Famicom Wars | ✅ |
| **011** | Color Dreams | 32KB PRG 切換，8KB CHR 切換（非授權） | Crystal Mines, Pesterminator | ✅ |
| **013** | CPROM | 固定 32KB PRG；16KB CHR-RAM（上半 4KB 切換） | Videomation | ✅ |
| **016** | Bandai FCG-1/2 | 16KB PRG，1KB×8 CHR；$6000 regs；CPU cycle IRQ | Dragon Ball (J), Famicom Jump (J) | ✅ |
| **016** | Bandai LZ93D50 | 同上；$8000 regs；latch IRQ；EEPROM stub | Magical Taruruuto-kun (J) | ✅ |
| **016** | Dragon Ball Z - Kyoushuu Saiya Jin | CIRAM read-time mirroring 修復後正常 | Dragon Ball Z - Kyoushuu Saiya Jin (J) | ✅ |
| **018** | Jaleco SS8806 | 3×8KB PRG + 8×1KB CHR；nibble 寫入；可變寬度 IRQ | Ninja Jajamaru, Pizza Pop!, Magic John, Saiyuuki World 2 | ✅ |
| **019** | Namco 163 | 3×8KB PRG；8×1KB CHR（≥0xE0 映射 CIRAM）；15-bit 上計數 IRQ；8ch 波形音效 | Splatterhouse (J), Rolling Thunder 2 (J) | ✅ |
| **020** | FDS 磁碟機 | BIOS($E000) + PRG-RAM(32KB) + CHR-RAM；磁碟 I/O 狀態機；IRQ timer；wavetable 音效 + FM 調變 | Donkey Kong, SMB, Bubble Bobble, Dracula II 等 10+ 遊戲 | ✅ |
| **021** | Konami VRC4a/c | 4×8KB PRG switchable；8×1KB CHR；prescaler IRQ | Wai Wai World 2 (J), Ganbare Goemon Gaiden 2 (J) | ✅ |
| **022** | Konami VRC2a | 8KB×2 PRG + 8×1KB CHR；CHR index >>1 | TwinBee 3 (J) | ✅ |
| **023** | Konami VRC2b | 同 VRC2a 但 CHR index 不右移 | Contra (J), Getsufuu Maden (J) | ✅ |
| **024** | Konami VRC6a | 16KB+8KB PRG；8×1KB CHR；prescaler IRQ；3ch 擴充音效 | Akumajou Densetsu (J) | ✅ |
| **025** | Konami VRC4b/d | VRC4 A0/A1 地址線對調 | Gradius II (J), Bio Miracle Bokutte Upa (J), TMNT (J) | ✅ |
| **026** | Konami VRC6b | VRC6 A0/A1 地址線對調 | Madara (J) | ✅ |
| **029** | Sealie Computing | 16KB switchable + 16KB fixed PRG；32KB CHR-RAM（8KB×4）；8KB WRAM | Glider Expansion - Mad House (PD) | ✅ |
| **032** | Irem G-101 | 8KB×3 PRG switchable；8×1KB CHR；SubMapper1=Major League | Image Fight (J), Major League (J) | ✅ |
| **033** | Taito TC0190 | 2×8KB PRG；2KB×2+1KB×4 CHR；addr&0xA003 decode | Akira (J), Don Doko Don (J) | ✅ |
| **034** | Nina-1 | CHR-RAM 變體（$8000 PRG）或 CHR-ROM 變體（$7FFD-$7FFF） | Deadly Towers (U), Impossible Mission II | ✅ |
| **064** | Tengen RAMBO-1 | 類 MMC3；3×8KB PRG；A12/CPU-cycle IRQ 可切換 | Shinobi (Tengen) | ✅ |
| **064** | Klax | 進入遊戲後畫面異常（停留標題循環） | Klax (Tengen) | ⚠️ |
| **065** | Irem H-3001 | 3×8KB PRG switchable；16-bit CPU cycle IRQ | Daiku no Gen San 2 — intro 捲軸條紋 | ⚠️ |
| **066** | GxROM | 32KB PRG × 8KB CHR 一次寫入；PRG modulo 修復 | DragonBall (J), Gumshoe (U) | ✅ |
| **067** | Sunsoft-3 | 16KB PRG；4×2KB CHR；16-bit 下計數 IRQ | Fantasy Zone 2 (J), Mito Koumon II (J) | ✅ |
| **068** | Sunsoft #4 | 16KB PRG（固定末）；4×2KB CHR；CHR-as-nametable | AfterBurner II (J), Maharaja (J) | ✅ |
| **069** | Sunsoft FME-7 / 5B | CPU cycle IRQ；PRG-RAM 分頁；4種鏡像；YM2149 3ch 擴展音效（對數音量 LUT、CPU/2 時脈） | Batman (J), Gimmick! (J) | ✅ |
| **070** | Bandai 74161/32 | 16KB PRG + 8KB CHR 一次寫入；$C000 固定末；bit7 啟發式偵測 mislabeled ROM 鏡像 | Kamen Rider Club (J), Arkanoid II (J) | ✅ |
| **071** | Camerica / BF909x | 16KB PRG 切換；CHR-RAM；BF9097 variant $9000 自動偵測+單屏鏡像 | Firehawk (U), Linus Spacehead (U) | ✅ |
| **072** | Jaleco JF-17 | Latch 機制（prgFlag/chrFlag）；16KB PRG + 8KB CHR | Pinball Quest (J), Moero!! Juudou Warriors (J) | ✅ |
| **075** | Konami VRC1 | 4×8KB PRG；2×4KB CHR；$9000 bit0=H/V | Ganbare Goemon! (J), Jajamaru Ninpou Chou (J) | ✅ |
| **076** | Namco 109 | Namco108 架構；reg[2-5]=2KB CHR | Digital Devil Monogatari - Megami Tensei (J) | ✅ |
| **077** | Napoleon Senki / IremLrog017 | 32KB PRG；slot0=CHR-ROM 2KB，slots1-3=CHR-RAM 6KB | Napoleon Senki (J) | ✅ |
| **078** | Irem 74HC161/32 | 16KB PRG 切換；4KB×2 CHR；subMapper 鏡像差異 | Holy Diver (J) | ✅ |
| **078** | Uchuusen Cosmo Carrier | submapper 鏡像修復，正常運作 | Uchuusen Cosmo Carrier (J) | ✅ |
| **079** | NINA-03 / NINA-06 (AVE) | (addr&0xE100)==0x4100；32KB PRG + 8KB CHR | Blackjack (AVE), Deathbots (AVE) | ✅ |
| **080** | Taito X1-005 | $7EF0–$7EFF regs；3×8KB PRG；2KB+1KB×4 CHR；RAM unlock | Minelvaton Saga (J), Fudou Myouou Den (J) | ✅ |
| **082** | Taito X1-017 | $7EF0–$7EFF regs；chrMode 1KB/2KB；SRAM unlock seq | SD Keiji Blader (J), Harikiri Stadium (J) | ✅ |
| **085** | Konami VRC7 | 3×8KB PRG；8×1KB CHR；prescaler IRQ；OPLL (YM2413) FM 合成音效 | Lagrange Point (J) | ✅ |
| **087** | Jaleco JF-09/10/18 | $6000–$7FFF 寫入；D0/D1 bit-swap 選 8KB CHR | Argus (J), City Connection (J), The Goonies (J) | ✅ |
| **088** | Namco 118 / 634 | Namco108 架構；R0/R1=2KB CHR，R2-R5=1KB CHR | Dragon Spirit (J), Quinty (J) | ✅ |
| **089** | Sunsoft-2 (Ikki variant) | bits[6:4]=PRG；(v&7)\|((v&0x80)>>4)=CHR；bit3=single-screen | Tenka no Goikenban - Mito Koumon (J) | ✅ |
| **090** | JY Company | 4 PRG modes；4 CHR modes；CPU/A12 IRQ；multiply reg；NT control | Mortal Kombat 2 (Unl) | ✅ |
| **093** | Sunsoft-2 (Fantasy Zone II) | bits[6:4]=PRG 16KB；$C000 固定末；CHR-RAM | Fantasy Zone (J), Shanghai (J) | ✅ |
| **095** | Namco 118 DxROM | Namco108 架構；reg[0][1] bit5=NT select | Dragon Buster (J) | ✅ |
| **097** | Irem TAM-S1 | 首 16KB 固定，末 16KB 切換；bits[7:6]=mirror；CHR-RAM | Kaiketsu Yanchamaru (J) | ✅ |
| **118** | TxSROM | MMC3 全功能 + CHR bit7 控制 nametable（ntBankPtrs） | Ys III (J), Armadillo (J), NES Play Action Football (U) | ✅ |
| **119** | TQROM | MMC3 全功能；CHR bank 0x40–0x7F 映射 CHR-RAM | High Speed (U) | ✅ |
| **140** | Jaleco JF-11 / JF-14 | $6000–$7FFF 寫入；bits[5:4]=PRG 32KB，bits[3:0]=CHR 8KB | Doraemon (J), Bio Senshi Dan (J), Mississippi Satsujin Jiken (J) | ✅ |
| **152** | Bandai 74161/32 single-screen | Mapper070 subclass；bit7→single-screen 鏡像（bit6→bit7 修復） | Arkanoid II Prototype (J) | ✅ |
| **153** | Bandai LZ93D50 + WRAM | 5-bit PRG bank；CHR-RAM；8KB WRAM；latch IRQ | Dragon Ball 3 (J) | ✅ |
| **159** | Bandai LZ93D50 alias | Mapper016 sub5 的別名 | — | ✅ |
| **180** | Crazy Climber / UnRom-180 | 首 16KB 固定($8000)，末 16KB 切換($C000)；CHR-RAM | Crazy Climber (J) | ✅ |
| **184** | Sunsoft-1 / FC-08 | $6000–$7FFF 寫入；下 4KB + 上 4KB（bit7 常設）CHR | Wing of Madoola (J), Atlantis no Nazo (J) | ✅ |
| **185** | CNROM + CHR copy-protect | nibble 保護 heuristic；不符合則讀 0xFF | B-Wings (J), Bird Week (J), Mighty Bomb Jack (J) | ✅ |
| **206** | Namco 108 | MMC3 雛形；無 IRQ；固定鏡像 | Karnov (J) | ✅ |
| **209** | JY Company (209) | Mapper 090 + CHR latch（MMC2-style） | — | ❓ |
| **210** | Namco 175 / Namco 340 | SubMapper1=175（無IRQ）；SubMapper2=340（IRQ+NT控制） | Famista '92 (J), Wagyan Land 2 (J) | ❓ |
| **211** | JY Company (211) | Mapper 090 + extended NT control | — | ❓ |
| **228** | Action 52 | addr+data 編碼 PRG/CHR/mirror；chipSelect；16/32KB mode | Cheetahmen II (U) | ✅ |
| **232** | Camerica BF9096 Quattro | 外層+內層二段 PRG；Aladdin variant submapper | Quattro Adventure (U), Quattro Sports (U) | ✅ |
