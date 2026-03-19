# AprNes Mapper 實作狀態清單

參考來源：`ref/mapper/mappers-0.80-summary.md`（31 個，扣除盜版 5 個 = 26 個）+ 補充重要商業 mapper 2 個（020 FDS、085 VRC7）= **目標 28 個**

> 盜版/hack mapper（006 FFE F4xxx、008 FFE F3xxx、015 100-in-1、017 FFE F8xxx、091 HK-SF3）不列入統計與實作目標。

---

## 進度摘要

| 項目 | 數量 |
|------|------|
| 實作目標（扣除盜版 + 補充 020/085） | 28 個 |
| 目標內已實作 | 28 個 |
| 目標外額外實作（NROM、Namco 108 + S 級 8 個 + A 級 6 個 + Mapper152 + B/C/D 級 12 個） | 29 個 |
| **總計已實作** | **57 個** |
| 目標涵蓋完成率 | **28 / 28 = 100%** |
| S 級 TODO 完成率 | **8 / 8 = 100%** |
| A 級 TODO 完成率 | **6 / 6 = 100%** |
| B 級 TODO 完成率 | **7 / 7 = 100%** |
| C 級 TODO 完成率 | **3 / 3 = 100%（含 Mapper152）** |
| D 級 TODO 完成率 | **2 / 2 = 100%** |

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
| **070** | Bandai 74161/32 | `Mapper070.cs` — PRG 16KB + CHR 8KB 一次寫入；$C000 固定末；Reset 強制 Vertical（Kamen Rider header hack）；Kamen Rider Club, Family Trainer 5 驗證通過（文件外額外支援） |
| **075** | Konami VRC1 | `Mapper075.cs` — 4×8K PRG；2×4KB CHR；$9000 bit0=H/V，bit1/2=CHR高位；Ganbare Goemon!, Jajamaru Ninpou Chou 驗證通過（文件外額外支援） |
| **088** | Namco 118 / 634 | `Mapper088.cs` — Namco 108 同架構；R0/R1=2KB CHR，R2-R5=1KB CHR，R6/R7=8KB PRG；Dragon Spirit, Quinty 驗證通過（文件外額外支援） |
| **118** | TxSROM | `Mapper118.cs` — MMC3 全功能 + CHR bit7 NT 控制；ntBankPtrs + ntChrOverrideEnabled；Ys III, Goal! Two, Armadillo, NES Play Action Football 驗證通過（文件外額外支援） |
| **140** | Jaleco JF-11 / JF-14 | `Mapper140.cs` — $6000-$7FFF 寫入選 PRG 32KB + CHR 8KB；Doraemon 驗證通過（文件外額外支援） |
| **152** | Bandai 74161/32 (single-screen) | `Mapper152.cs`（Mapper070 subclass）— bit6 single-screen 鏡像；Arkanoid II (Prototype)（文件外額外支援） |
| **232** | Camerica BF9096 Quattro | `Mapper232.cs` — 外層 $8000-$9FFF + 內層 $C000-$DFFF 二段 PRG；Aladdin variant submapper；Quattro Adventure, Quattro Sports 驗證通過（文件外額外支援） |
| **013** | CPROM | `Mapper013.cs` — 32KB PRG 固定；16KB CHR-RAM；下半 4KB 固定 bank0，上半 4KB switchable bits[1:0] |
| **067** | Sunsoft-3 | `Mapper067.cs` — 16KB PRG + 4×2KB CHR；16-bit IRQ 下計數(0→0xFFFF)；Fantasy Zone 2, Mito Koumon II 驗證通過 |
| **076** | Namco 109 | `Mapper076.cs` — Namco108 架構；4×2KB CHR(reg[2-5])；Megami Tensei 驗證通過 |
| **077** | Napoleon Senki (IremLrog017) | `Mapper077.cs` — 32KB PRG；CHR slot0=ROM 2KB，slots1-3=RAM 6KB；Napoleon Senki 驗證通過 |
| **080** | Taito X1-005 | `Mapper080.cs` — $7EF0-$7EFF regs；3×8KB PRG；2KB+1KB CHR；RAM unlock；Minelvaton Saga, Fudou Myouou Den 驗證通過 |
| **082** | Taito X1-017 | `Mapper082.cs` — $7EF0-$7EFF regs；chrMode switch；SRAM unlock seq；SD Keiji Blader, Harikiri Stadium 驗證通過 |
| **095** | Namco 118 DxROM | `Mapper095.cs` — Namco108 架構；reg[0]/[1] bit5=NT select；Dragon Buster 驗證通過 |
| **097** | Irem TAM-S1 | `Mapper097.cs` — 首 16KB 固定，末 16KB 切換；CHR-RAM；Kaiketsu Yanchamaru 驗證通過 |
| **119** | TQROM | `Mapper119.cs` — MMC3 全功能；CHR 0x40-0x7F → 8KB CHR-RAM；High Speed 驗證通過 |
| **180** | Crazy Climber / UnRom_180 | `Mapper180.cs` — 首 16KB 固定($8000)，末 16KB 切換($C000)；CHR-RAM；Crazy Climber 驗證通過 |
| **210** | Namco 175/340 | `Mapper210.cs` — SubMapper 1=175(無IRQ)，SubMapper 2=340(IRQ+NT)；4×8KB PRG + 8×1KB CHR |
| **228** | Action 52 | `Mapper228.cs` — addr+data 編碼 PRG/CHR/mirror；chipSelect 3→2；16KB/32KB mode；Cheetahmen II 驗證通過 |

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

### 🟠 A 級 — 容易實作、遊戲量多（✅ 全部完成）

| 優先 | Mapper | 名稱 | 難度 | ROMs | 代表遊戲 | 實作要點 |
|:----:|:------:|------|:----:|:----:|---------|---------|
| ~~9~~ | ~~**075**~~ | ~~Konami VRC1~~ | ~~⭐⭐~~ | ~~28~~ | ~~Ganbare Goemon! (J), Jajamaru Ninpou Chou (J)~~ | ✅ `Mapper075.cs` — 4×8K PRG ($8000/$A000/$C000 swappable, $E000 固定末)；$9000 bit0=H/V，bit1=CHR0 high，bit2=CHR1 high；$E000/$F000 各選 4KB CHR；Ganbare Goemon!, Jajamaru Ninpou Chou 驗證通過 |
| ~~10~~ | ~~**118**~~ | ~~TxSROM (MMC3 + NT control)~~ | ~~⭐⭐~~ | ~~28~~ | ~~Ys III (J), NES Play Action Football (U), Armadillo (J)~~ | ✅ `Mapper118.cs` — MMC3 全功能 + CHR reg bit7 控制命名表；mode 0: R0/R1 bit7→NT0+NT1/NT2+NT3；mode 1: R2-R5 各控制 NT0-NT3；ntBankPtrs + ntChrOverrideEnabled；Ys III, Goal! Two, Armadillo, NES Play Action Football 驗證通過 |
| ~~11~~ | ~~**088**~~ | ~~Namco 118 / 634~~ | ~~⭐⭐~~ | ~~18~~ | ~~Dragon Spirit (J), Quinty (J), Dragon Buster II (J)~~ | ✅ `Mapper088.cs` — Namco 108(206) 同架構；R0/R1=2KB CHR ($0000/$0800，清 bit6)；R2-R5=1KB CHR ($1000-$1C00，強制 bit6=1)；R6/R7=8KB PRG；$C000-$DFFF 倒數第二，$E000-$FFFF 固定末；Dragon Spirit, Quinty 驗證通過 |
| ~~12~~ | ~~**232**~~ | ~~Camerica Quattro (BF909x)~~ | ~~⭐⭐~~ | ~~18~~ | ~~Quattro Sports, Quattro Adventure (Camerica)~~ | ✅ `Mapper232.cs` — `$8000–$9FFF` 寫入外層 bank bits[4:3]；`$C000–$DFFF` 寫入內層 bits[1:0]；bank0=(outer<<2)|inner，bank1=(outer<<2)|3；IsAladdinVariant(submapper 1)位元交換；Quattro Adventure, Quattro Sports 驗證通過 |
| ~~13~~ | ~~**140**~~ | ~~Jaleco JF-11 / JF-14~~ | ~~⭐⭐~~ | ~~15~~ | ~~Bio Senshi Dan (J), Mississippi Satsujin Jiken (J)~~ | ✅ `Mapper140.cs` — `$6000–$7FFF` 寫入(MapperW_RAM)；bits[7:4]=CHR 8KB，bits[3:0]=PRG 32KB；total32k=PRG_ROM_count/2；Doraemon 驗證通過。⚠️ Bio Senshi Dan 綠畫面（疑似 ROM 特定問題，非 mapper bug） |
| ~~14~~ | ~~**070**~~ | ~~Bandai 74161/32~~ | ~~⭐⭐~~ | ~~14~~ | ~~Kamen Rider Club (J), Family Trainer 5 (J)~~ | ✅ `Mapper070.cs` — `$8000–$FFFF` 寫入；bits[7:4]=PRG 16KB ($8000)，bits[3:0]=CHR 8KB；$C000 固定末；Reset 強制 *Vertical=1（Mesen2 hack for Kamen Rider bad header）；Mapper152 subclass(enableMirroringControl=true，bit6 single-screen)；Kamen Rider Club, Family Trainer 驗證通過；Arkanoid II (Prototype) 部分正常 |

---

### 🟡 B 級 — 中等難度或遊戲量適中（✅ 全部完成）

| 優先 | Mapper | 名稱 | 難度 | ROMs | 代表遊戲 | 實作要點 |
|:----:|:------:|------|:----:|:----:|---------|---------|
| ~~15~~ | ~~**119**~~ | ~~TQROM (MMC3 + mixed CHR)~~ | ~~⭐⭐~~ | ~~15~~ | ~~PinBot (U), High Speed (U)~~ | ✅ `Mapper119.cs` — MMC3 全功能；CHR bank 值 0x40–0x7F 映射 8KB CHR-RAM（unmanaged Marshal.AllocHGlobal），其他映射 CHR-ROM；High Speed 驗證通過 |
| ~~16~~ | ~~**080**~~ | ~~Taito X1-005~~ | ~~⭐⭐⭐~~ | ~~24~~ | ~~Minelvaton Saga (J), Kyonshiizu 2 (J), Fudou Myouou Den (J)~~ | ✅ `Mapper080.cs` — $7EF0–$7EFF 寫入(MapperW_RAM)；reg[0]/[1]=2KB CHR(slots 0-3), reg[2-5]=1KB CHR(slots 4-7), reg[6-8]=8KB PRG；ramPermission==0xA3 解鎖 $7F00 工作 RAM；Minelvaton Saga, Fudou Myouou Den 驗證通過 |
| ~~17~~ | ~~**067**~~ | ~~Sunsoft-3~~ | ~~⭐⭐~~ | ~~6~~ | ~~Fantasy Zone 2 (J), Mito Koumon II (J)~~ | ✅ `Mapper067.cs` — PRG 16KB 切換($8000)；CHR 2KB×4($8800/$9800/$A800/$B800)；$C800=IRQ latch(alt lo/hi)，$D800=IRQ enable/ack；$E800=mirror；Fantasy Zone 2 驗證通過 |
| ~~18~~ | ~~**228**~~ | ~~Action 52 (Active Enterprises)~~ | ~~⭐⭐~~ | ~~10~~ | ~~Action 52 (U), Cheetahmen II (U)~~ | ✅ `Mapper228.cs` — $8000–$FFFF write；chipSelect=addr bits[12:11](clamp 3→2)；prgPage=((addr>>6)&0x1F)|(chipSelect<<5)；bit5=16KB/32KB mode；chrBank=((addr&0xF)<<2)|(data&3)；Cheetahmen II 驗證通過 |
| ~~19~~ | ~~**095**~~ | ~~Namco 118 (DxROM)~~ | ~~⭐⭐~~ | ~~7~~ | ~~Dragon Buster (J)~~ | ✅ `Mapper095.cs` — Namco108 架構(addr&0x8001)；reg[0]/[1] bit5=NT select(SetNametables→ntBankPtrs+ntChrOverrideEnabled)；reg[2-5]強制bit6=1(上半)；Dragon Buster 驗證通過 |
| ~~20~~ | ~~**076**~~ | ~~Namco 109~~ | ~~⭐⭐~~ | ~~7~~ | ~~Digital Devil Monogatari - Megami Tensei (J)~~ | ✅ `Mapper076.cs` — Namco108 架構(addr&0x8001)；reg[2-5]=2KB CHR bank(index*2→1KB)；reg[6]/[7]=8KB PRG；Megami Tensei 驗證通過 |
| ~~21~~ | ~~**013**~~ | ~~CPROM (NES-CPROM)~~ | ~~⭐~~ | ~~7~~ | ~~Videomation (U)~~ | ✅ `Mapper013.cs` — PRG 32KB 固定；16KB CHR-RAM(unmanaged)；下半 4KB 固定 bank0，上半 4KB 由寫入 bits[1:0] 切換(0-3)；MapperW_CHR 可寫整個 CHR-RAM |

---

### 🟢 C 級 — 遊戲少或為邊緣案例（✅ 全部完成）

| 優先 | Mapper | 名稱 | 難度 | ROMs | 代表遊戲 | 實作要點 |
|:----:|:------:|------|:----:|:----:|---------|---------|
| ~~22~~ | ~~**152**~~ | ~~Bandai 74161/32 (single-screen)~~ | ~~⭐~~ | ~~4~~ | ~~Arkanoid II (J)~~ | ✅ `Mapper152.cs`（Mapper070 subclass）— enableMirroringControl=true；bit7 heuristic 偵測鏡像控制；bit6→*Vertical 2(A)/3(B) single-screen；Arkanoid II (Prototype) 部分正常 |
| ~~23~~ | ~~**097**~~ | ~~Irem TAM-S1~~ | ~~⭐⭐~~ | ~~3~~ | ~~Kaiketsu Yanchamaru (J)~~ | ✅ `Mapper097.cs` — 固定首 16KB($8000)，切換末 16KB($C000)；bits[7:6]=mirror(0=SingleA,1=H,2=V,3=SingleB)；CHR-RAM via ppu_ram；Kaiketsu Yanchamaru 驗證通過 |
| ~~24~~ | ~~**180**~~ | ~~Crazy Climber~~ | ~~⭐~~ | ~~1~~ | ~~Crazy Climber (J)~~ | ✅ `Mapper180.cs` — 固定首 16KB($8000)，切換末 16KB($C000)；bits[2:0]=bank；CHR-RAM via ppu_ram；Crazy Climber 驗證通過 |
| ~~25~~ | ~~**210**~~ | ~~Namco 175 / Namco 340~~ | ~~⭐⭐⭐~~ | ~~1*~~ | ~~Famista '92/93, Wagyan Land 2, Pac-Attack~~ | ✅ `Mapper210.cs` — SubMapper 1=175(無IRQ,無NT control)，SubMapper 2=340($E000 bits[7:6]=mirror)；4×8KB PRG + 8×1KB CHR；IRQ 15-bit 上計數(340 only)；傳入 db.Submapper |

---

### 🔵 D 級 — 困難或意義有限（✅ 全部完成）

| 優先 | Mapper | 名稱 | 難度 | ROMs | 代表遊戲 | 實作要點 |
|:----:|:------:|------|:----:|:----:|---------|---------|
| ~~26~~ | ~~**082**~~ | ~~Taito X1-017~~ | ~~⭐⭐⭐⭐~~ | ~~12~~ | ~~Kyuukyoku Harikiri Stadium 系列 (J)~~ | ✅ `Mapper082.cs` — $7EF0–$7EFF(MapperW_RAM)；$7EF6 bit1=chrMode(2KB/1KB)；$7EFA-$7EFC=prgBank[0-2]>>2；$7EF8-$7EFA=SRAM unlock(CA/69/84)；SD Keiji Blader, Harikiri Stadium 驗證通過 |
| ~~27~~ | ~~**077**~~ | ~~Napoleon Senki~~ | ~~⭐⭐⭐~~ | ~~2~~ | ~~Napoleon Senki (J)~~ | ✅ `Mapper077.cs` — 32KB PRG switchable；CHR slot0($0000-$07FF)=CHR-ROM 2KB(bits[7:4])，slots1-3($0800-$1FFF)=CHR-RAM 6KB(unmanaged)；Napoleon Senki 驗證通過 |

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

*最後更新：2026-03-19（Mapper013/067/076/077/080/082/095/097/119/180/210/228 加入；B 級 7/7、C 級 3/3（含 Mapper152）、D 級 2/2 全部完成；總計已實作 57 個）*
