# AprNes Mapper 實作狀態清單

參考來源：`ref/mapper/mappers-0.80-summary.md`（31 個，扣除盜版 5 個 = 26 個）+ 補充重要商業 mapper 2 個（020 FDS、085 VRC7）= **目標 28 個**

> 盜版/hack mapper（006 FFE F4xxx、008 FFE F3xxx、015 100-in-1、017 FFE F8xxx、091 HK-SF3）不列入統計與實作目標。

---

## 進度摘要

| 項目 | 數量 |
|------|------|
| 實作目標（扣除盜版 + 補充 020/085） | 28 個 |
| 目標內已實作 | 28 個 |
| 目標外額外實作（NROM、Namco 108 + S 級 8 個） | 10 個 |
| **總計已實作** | **38 個** |
| 目標涵蓋完成率 | **28 / 28 = 100%** |
| S 級 TODO 完成率 | **8 / 8 = 100%** |

---

## 已實作（28 個）

| Mapper | 名稱 | 備註 |
|:------:|------|------|
| **000** | NROM | `Mapper000.cs`（文件外額外支援） |
| **001** | MMC1 | `Mapper001.cs` — 序列寫入、鏡像切換；Legend of Zelda, Metroid, MegaMan 2 |
| **002** | UxROM | `Mapper002.cs` — Castlevania, MegaMan, Ghosts & Goblins |
| **003** | CNROM | `Mapper003.cs` — Solomon's Key, Gradius |
| **004** | MMC3 | `Mapper004.cs` — 掃描線 IRQ、精細分頁；另有 `Mapper004RevA.cs`、`Mapper004MMC6.cs` 變體；SMB2, SMB3, MegaMan 3-6 |
| **005** | MMC5 | `Mapper005.cs` — 部分實作（PRG/CHR 分頁；音效、擴充屬性表未完整）；Castlevania 3 |
| **007** | AxROM | `Mapper007.cs` — Battletoads, Wizards & Warriors |
| **009** | MMC2 | `Mapper009.cs` — PPU Latch 自動換頁；《泰森拳擊》驗證通過 |
| **010** | MMC4 | `Mapper010.cs` — MMC2 演進版（16K PRG）；PPU Latch 延遲更新；Fire Emblem, Famicom Wars 驗證通過 |
| **011** | Color Dreams | `Mapper011.cs` — Crystal Mines, Pesterminator（非授權正規廠商） |
| **016/159** | Bandai FCG / LZ93D50 | `Mapper016.cs` — 16K PRG + 8×1K CHR，16-bit CPU cycle IRQ（BEFORE decrement）；sub4=FCG-1/2（$6000 regs, direct counter）sub5=LZ93D50（$8000 regs, latch IRQ）；Mapper159 alias sub5；EEPROM stub（ACK 模擬）；Dragon Ball（FCG-1/2）、Famicom Jump、Magical Taruruuto-kun 驗證通過。⚠️ Dragon Ball Z - Kyoushuu Saiya Jin (J) 遊戲畫面 CHR bank 交換時序異常（garbled terrain），IRQ timing 問題尚待克服 |
| **018** | Jaleco SS8806 | `Mapper018.cs` — 3×8K PRG + 8×1K CHR（nibble 寫入），可變寬度 IRQ（4/8/12/16-bit）；Ninja Jajamaru, Pizza Pop!, Magic John, Saiyuuki World 2 驗證通過 |
| **021** | VRC4 | `Mapper021.cs` — VRC4a/VRC4c（subMapper 自動偵測）；4×8K PRG switchable (mode 0/1)，8×1K CHR，prescaler IRQ；Wai Wai World 2 (J), Ganbare Goemon Gaiden 2 (J) 驗證通過 |
| **022** | VRC2a | `Mapper022.cs` — TwinBee 3 (J)；8K PRG×2 + 8×1K CHR；CHR index >> 1（低位忽略） |
| **023** | VRC2b | `Mapper023.cs` — Contra (J), Getsufuu Maden (J)；同 VRC2a 但地址線標準配置，CHR index 不右移 |
| **032** | Irem G-101 | `Mapper032.cs` — PRG mode 0/1 切換，8×1K CHR；SubMapper 1 = Major League (mode 0 + single-A)；Image Fight (J), Major League (J) 驗證通過 |
| **033** | Taito TC0190 | `Mapper033.cs` — 2×8K PRG switchable，addr & 0xA003 decode；$8002/$8003 各選 2K CHR，$A000-$A003 選 4×1K CHR；Akira (J), Don Doko Don (J) 驗證通過 |
| **034** | Nina-1 | `Mapper034.cs` — 兩種子變體：CHR-RAM（Deadly Towers/Mashou）用 $8000 PRG 選擇；CHR-ROM（Impossible Mission II）用 $7FFD-$7FFF 寫入暫存器 |
| **064** | Tengen RAMBO-1 | `Mapper064.cs` — 類 MMC3；3×8K PRG switchable (regs 6,7,15) + fixed last；CHR fine mode (regs 8,9)；A12 或 CPU-cycle IRQ ($C001 bit0 切換)；Skull&Crossbones forceClock 修正；Shinobi (Tengen) 驗證通過。⚠️ Klax (Tengen) 進入遊戲後畫面異常（停留在標題畫面循環，無法進入遊戲），尚待克服 |
| **065** | Irem H-3001 | `Mapper065.cs` — 3×8K PRG switchable + 固定末 8K；16-bit CPU cycle IRQ（$9003/$9004/$9005/$9006）。⚠️ Daiku no Gen San 2 (J) intro 捲軸場景畫面異常（條紋），尚待克服 |
| **066** | GxROM | `Mapper066.cs` — DragonBall, Gumshoe |
| **068** | Sunsoft #4 | `Mapper068.cs` — 4×2K CHR，16K PRG 切換，固定末尾 16K；CHR-as-nametable ($C000/$D000/$E000 bit4)；AfterBurner II (J), Maharaja (J) 驗證通過 |
| **069** | FME-7 | `Mapper069.cs` — CPU 週期 IRQ，PRG-RAM 分頁，4 種鏡像；Batman (J), Gimmick! (J) 驗證通過。⚠️ Sunsoft 5B 擴充音效（YM2149）未實作。⚠️ PAL 版（Mr. Gimmick (E)）畫面異常，PAL timing 尚未支援 |
| **071** | Camerica | `Mapper071.cs` — Firehawk, Linus Spacehead（非授權正規廠商） |
| **078** | Irem 74HC161/32 | `Mapper078.cs` — ⚠️ Holy Diver (J) 可動；Uchuusen Cosmo Carrier (J) 有問題（intro 黑畫面），尚待完全克服 |
| **019** | Namco 163 | `Mapper019.cs` — 3×8K PRG + 固定末；8×1K CHR（≥0xE0 映射 CIRAM）；4 NT regs（CHR-as-NT 覆蓋）；15-bit 上計數 IRQ；8 通道波形擴充音效（128-byte 內建 RAM，round-robin 更新，每 15 cycles）；Splatterhouse (J), Rolling Thunder 2 (J) |
| **024/026** | VRC6a / VRC6b | `Mapper024.cs` — Konami VRC6；16K+8K PRG；8×1K CHR；多種 CHR/NT layout（bankingMode）；per-nametable CHR-ROM 映射（ntBankPtrs）；VRC prescaler IRQ；3 通道擴充音效（Pulse1+2 + Sawtooth）；`IsVRC6b` flag 控制地址 bit-swap；Akumajo Dracula 3 (J) |
| **085** | VRC7 | `Mapper085.cs` — Konami VRC7；3×8K PRG + 固定末；8×1K CHR；VRC prescaler IRQ；WRAM enable（controlFlags bit7）；OPLL FM 音效 silent stub（$9010/$9030 接受但不合成）；Lagrange Point (J) |
| **153** | Bandai LZ93D50+WRAM | `Mapper153.cs` — 5-bit PRG bank（chrReg OR bit0 << 4 延伸）；CHR-RAM only；8K WRAM（reg $0D bit5 enable）；latch IRQ；Dragon Ball 3 (J) |
| **206** | Namco 108 | `Mapper206.cs` — MMC3 雛形，無 IRQ；《Karnov》驗證通過（文件外額外支援） |

---

## 未實作（目標 28 個中剩餘 1 個）

| Mapper | 名稱 | 代表作品 | 技術重點 |
|:------:|------|---------|---------|
| **020** | FDS（磁碟機系統） | 銀河戰士(日)、薩爾達(日)、惡魔城(日) | BIOS + 磁碟流模擬，複雜度最高 |

---

## TODO — PTT 全 138 名未實作（非盜版）優先順序

來源：`temp/nes-mapper-ptt-summary-full-updated.md`，扣除多合一、盜版、已實作後剩餘 **27 個**。
測試 ROM 已複製至 `temp/mapperNNN/`（各最多 5 款，[!] 優先）。

評分說明：**難度**（⭐=極簡可在1hr內完成 / ⭐⭐=簡單 / ⭐⭐⭐=中等 / ⭐⭐⭐⭐=困難）；**ROMs** = GoodNES V5.00 中的數量。

---

### 🔴 S 級 — 投入少、回報高（✅ 全部完成）

| 優先 | Mapper | 名稱 | 難度 | ROMs | 代表遊戲 | 實作要點 |
|:----:|:------:|------|:----:|:----:|---------|---------|
| ~~1~~ | ~~**025**~~ | ~~Konami VRC4b/d~~ | ~~⭐~~ | ~~66~~ | ~~Gradius II (J), TMNT (J), Bio Miracle Bokutte Upa (J)~~ | ✅ `Mapper025.cs` — VRC4b(sub1)/VRC4d(sub2)/heuristic；A0/A1 swap vs Mapper021；Gradius II, Bio Miracle, TMNT 驗證通過 |
| ~~2~~ | ~~**079**~~ | ~~NINA-03 / NINA-06 (AVE)~~ | ~~⭐~~ | ~~69~~ | ~~Deathbots (U), Krazy Kreatures, Bible Adventures~~ | ✅ `Mapper079.cs` — `(addr & 0xE100)==0x4100` 過濾；bit3=PRG 32KB，bits[2:0]=CHR 8KB；Blackjack, Deathbots 驗證通過 |
| ~~3~~ | ~~**087**~~ | ~~Jaleco JF-09/10/18~~ | ~~⭐~~ | ~~52~~ | ~~The Goonies (J), City Connection (J), Choplifter (J), Argus (J)~~ | ✅ `Mapper087.cs` — `$6000–$7FFF` 寫入；D0/D1 bit-swap 選 CHR 8KB；Argus, City Connection, Goonies 驗證通過 |
| ~~4~~ | ~~**185**~~ | ~~CNROM + CHR copy-protect~~ | ~~⭐~~ | ~~33~~ | ~~B-Wings (J), Mighty Bomb Jack (J), Spy vs Spy (U)~~ | ✅ `Mapper185.cs` — heuristic 保護：`(v&0x0F)!=0 && v!=0x13` 才啟用 CHR；否則讀 0xFF；B-Wings, Bird Week, Mighty Bomb Jack 驗證通過 |
| ~~5~~ | ~~**184**~~ | ~~Sunsoft-1 (FC-08)~~ | ~~⭐~~ | ~~19~~ | ~~Atlantis no Nazo (J), Wing of Madoola (J)~~ | ✅ `Mapper184.cs` — `$6000–$7FFF` 寫入；bits[2:0]=下 4KB，`0x80\|bits[6:4]`=上 4KB（高位常設）；Wing of Madoola, Kantarou 驗證通過 |
| ~~6~~ | ~~**072**~~ | ~~Jaleco JF-17~~ | ~~⭐~~ | ~~6~~ | ~~Pinball Quest (J), Moero!! Juudou Warriors (J)~~ | ✅ `Mapper072.cs` — latch 機制（prgFlag/chrFlag）；bit7→PRG bits[2:0]，bit6→CHR bits[3:0]；$C000 固定末；Pinball Quest, Juudou Warriors 驗證通過 |
| ~~7~~ | ~~**093**~~ | ~~Sunsoft-2 (Fantasy Zone II)~~ | ~~⭐~~ | ~~3~~ | ~~Fantasy Zone (J), Shanghai (J)~~ | ✅ `Mapper093.cs` — `$8000–$FFFF` 寫入；bits[6:4]=PRG 16KB ($8000)；$C000 固定末；CHR-RAM；Fantasy Zone, Shanghai 驗證通過 |
| ~~8~~ | ~~**089**~~ | ~~Sunsoft-2 (Ikki variant)~~ | ~~⭐~~ | ~~3~~ | ~~Tenka no Goikenban - Mito Koumon (J), Ikki (J)~~ | ✅ `Mapper089.cs` — bits[6:4]=PRG($8000)，`(v&7)\|((v&0x80)>>4)`=CHR 4-bit，bit3=single-screen(A/B)；$C000 固定末；Mito Koumon 驗證通過 |

---

### 🟠 A 級 — 容易實作、遊戲量多

| 優先 | Mapper | 名稱 | 難度 | ROMs | 代表遊戲 | 實作要點 |
|:----:|:------:|------|:----:|:----:|---------|---------|
| 9 | **075** | Konami VRC1 | ⭐⭐ | 28 | Ganbare Goemon! (J), Jajamaru Ninpou Chou (J) | `$8000` PRG-A，`$A000` PRG-B，`$C000` PRG-C，固定末；`$E000` CHR 4KB 兩組；無 IRQ；鏡像 H/V by $8000 bit0 |
| 10 | **118** | TxSROM (MMC3 + 4-screen) | ⭐⭐ | 28 | Ys III (J), NES Play Action Football (U), Armadillo (J) | MMC3 全功能 + CHR regs 6/7 控制命名表（bit6=1 用 VRAM，bit6=0 用 CHR-ROM page）；可從 Mapper004 繼承 |
| 11 | **088** | Namco 118 / 634 | ⭐⭐ | 18 | Dragon Spirit (J), Quinty (J), Dragon Buster II (J) | Namco 108(206) 同架構；$8000 write = 命令，$8001 = data；CHR: 2KB×2（R0/R1）+ 1KB×4（R2–R5）；無 IRQ |
| 12 | **232** | Camerica Quattro (BF909x) | ⭐⭐ | 18 | Quattro Sports, Quattro Adventure (Camerica) | 與 071 同廠；`$8000–$9FFF` 寫入 = 外層 bank（bits[4:3]）；`$C000–$DFFF` 寫入 = 內層 PRG bank（bits[1:0]） |
| 13 | **140** | Jaleco JF-11 / JF-14 | ⭐⭐ | 15 | Bio Senshi Dan (J), Mississippi Satsujin Jiken (J) | `$6000–$7FFF` 寫入；bits[7:4] = CHR 8KB bank，bits[3:0] = PRG 32KB bank；一次選 PRG+CHR；無 IRQ |
| 14 | **070** | Bandai 74161/32 | ⭐⭐ | 14 | Kamen Rider Club (J), Family Trainer 5 (J) | `$8000–$FFFF` 寫入；bits[3:0] = CHR 8KB，bits[7:4] = PRG 16KB ($8000)；$C000 固定末；H/V 鏡像固定（垂直） |

---

### 🟡 B 級 — 中等難度或遊戲量適中

| 優先 | Mapper | 名稱 | 難度 | ROMs | 代表遊戲 | 實作要點 |
|:----:|:------:|------|:----:|:----:|---------|---------|
| 15 | **119** | TQROM (MMC3 + mixed CHR) | ⭐⭐ | 15 | PinBot (U), High Speed (U) | MMC3 全功能；CHR bank 值 bit6=1 時映射 CHR-RAM（而非 CHR-ROM）；NES-TQROM 板型 |
| 16 | **080** | Taito X1-005 | ⭐⭐⭐ | 24 | Minelvaton Saga (J), Kyonshiizu 2 (J), Fudou Myouou Den (J) | ASIC；PRG 32KB 固定；`$7EF0–$7EFF` 寫入 CHR；2KB×4 或 1KB×8 切換；有帶 NVRAM 的子版型（NES 2.0 細分）；bus conflict timing |
| 17 | **067** | Sunsoft-3 | ⭐⭐ | 6 | Fantasy Zone 2 (J), Mito Koumon II (J) | PRG 16KB 切換（$8000–$BFFF）；CHR 2KB×4（R0–R3）；`$C000–$DFFF` = IRQ latch，`$E000` = IRQ 啟停，`$E001` = ACK |
| 18 | **228** | Action 52 (Active Enterprises) | ⭐⭐ | 10 | Action 52 (U), Cheetahmen II (U) | `$8000–$FFFF` 寫入；bits[9:6]=外層bank，bit5=PRG size(16/32KB)，bits[4:2]=CHR bank，bits[1:0]=鏡像；一次搞定 |
| 19 | **095** | Namco 118 (DxROM) | ⭐⭐ | 7 | Dragon Buster (J) | Namco 108 同架構；CHR: 2KB×2 + 1KB×4；命名表由 CHR bank 控制（bits[5]）；無 IRQ；可從 206/088 改造 |
| 20 | **076** | Namco 109 | ⭐⭐ | 7 | Digital Devil Monogatari - Megami Tensei (J) | Namco 108 同架構；CHR: 2KB×2（R0/R1，低 bank 位置）+ 1KB×4（R2–R5，高 bank 位置）；與 088 的差異在 CHR bank 前後配置相反 |
| 21 | **013** | CPROM (NES-CPROM) | ⭐ | 7 | Videomation (U) | PRG 32KB 固定；`$C000–$FFFF` 寫入 bit1 = 切換 CHR-RAM 上半 4KB（$1000–$1FFF）；下半固定 bank 0 |

---

### 🟢 C 級 — 遊戲少或為邊緣案例

| 優先 | Mapper | 名稱 | 難度 | ROMs | 代表遊戲 | 實作要點 |
|:----:|:------:|------|:----:|:----:|---------|---------|
| 22 | **152** | Bandai 74161/32 (single-screen) | ⭐ | 4 | Arkanoid II (J) | Mapper070 直接變體；bit6=1 時 one-screen 鏡像（0=bank A，1=bank B）；其餘 PRG/CHR 邏輯同 070 |
| 23 | **097** | Irem TAM-S1 | ⭐⭐ | 3 | Kaiketsu Yanchamaru (J) | PRG 16KB 切換於 $C000–$FFFF（固定首端 $8000–$BFFF）；與 UxROM 方向相反；無 IRQ；CHR-RAM |
| 24 | **180** | Crazy Climber | ⭐ | 1 | Crazy Climber (J) | 僅切換 $8000–$BFFF 的 16KB PRG；$C000–$FFFF 固定 bank 0；CHR-RAM；`$8000–$FFFF` 任意地址寫入選 bank |
| 25 | **210** | Namco 175 / Namco 340 | ⭐⭐⭐ | 1* | Famista '92/93, Wagyan Land 2, Pac-Attack | 兩種子板型（NES 2.0 分拆）；175=無IRQ；340=有IRQ+命名表控制；CHR 8KB×8；*GoodNES ROM 標記可能不準確 |

---

### 🔵 D 級 — 困難或意義有限（最後考慮）

| 優先 | Mapper | 名稱 | 難度 | ROMs | 代表遊戲 | 實作要點 |
|:----:|:------:|------|:----:|:----:|---------|---------|
| 26 | **082** | Taito X1-017 | ⭐⭐⭐⭐ | 12 | Kyuukyoku Harikiri Stadium 系列 (J) | CHR 1KB×6 + PRG 8KB×3；NVRAM 需先寫解鎖序列（$7EF8–$7EFA）；$7EFC–$7EFF SRAM protect regs；Taito 最複雜 ASIC |
| 27 | **077** | Napoleon Senki | ⭐⭐⭐ | 2 | Napoleon Senki (J) | 2KB CHR-RAM（$0000–$07FF + $1000–$17FF）+ 2KB CHR-ROM（其餘）；`$8000` write bit0 切換；特殊 CHR 空間分割，全 NES 唯一 |

---

## 優先實作建議

依遊戲覆蓋率與難度排序：

| 優先度 | Mapper | 理由 |
|:------:|--------|------|
| ~~★★★~~ | ~~**009** MMC2~~ | ✅ 已完成 |
| ~~★★★~~ | ~~**206** Namco 108~~ | ✅ 已完成 |
| ~~★★★~~ | ~~**069** FME-7~~ | ✅ 已完成 |
| ~~★★~~ | ~~**010** MMC4~~ | ✅ 已完成 |
| ~~★★★~~ | ~~**022/023** VRC2a/VRC2b~~ | ✅ 已完成 |
| ~~★~~ | ~~**032** Irem G-101~~ | ✅ 已完成 |
| ~~★~~ | ~~**068** Sunsoft Mapper #4~~ | ✅ 已完成 |
| ~~★★★~~ | ~~**021** VRC4~~ | ✅ 已完成 |
| ~~★★★~~ | ~~**064** Tengen RAMBO-1~~ | ✅ 已完成 |
| ~~★★~~ | ~~**024** VRC6~~ | ✅ 已完成（Akumajo Dracula 3 日版） |
| ~~★★~~ | ~~**019** Namcot 106~~ | ✅ 已完成（8 通道波形音效） |
| ~~★★~~ | ~~**016** Bandai~~ | ✅ 已完成（⚠️ DBZ1 IRQ timing 待克服） |
| ~~★★~~ | ~~**018** Jaleco SS8806~~ | ✅ 已完成 |
| ~~★~~ | ~~**033** Taito TC0190~~ | ✅ 已完成 |
| ~~★~~ | ~~**065** Irem H-3001~~ | ✅ 已完成 |
| ~~★★~~ | ~~**153** Bandai LZ93D50+WRAM~~ | ✅ 已完成 |
| ~~★★~~ | ~~**085** VRC7~~ | ✅ 已完成（OPLL silent stub） |
| ✗ | **020** FDS | 磁碟機模擬，最高複雜度，需 BIOS |

---

*最後更新：2026-03-19（Mapper025 VRC4b/d、Mapper079 NINA-03/06、Mapper087 JF-09/10/18、Mapper185 CNROM+protect、Mapper184 Sunsoft-1、Mapper072 JF-17、Mapper093 Sunsoft-2 FantasyZone、Mapper089 Sunsoft-2 Ikki 加入；S 級 8/8 = 100% 完成；總計已實作 38 個）*
