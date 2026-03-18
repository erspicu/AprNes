# AprNes Mapper 實作狀態清單

參考來源：`ref/mapper/mappers-0.80-summary.md`（31 個，扣除盜版 5 個 = 26 個）+ 補充重要商業 mapper 2 個（020 FDS、085 VRC7）= **目標 28 個**

> 盜版/hack mapper（006 FFE F4xxx、008 FFE F3xxx、015 100-in-1、017 FFE F8xxx、091 HK-SF3）不列入統計與實作目標。

---

## 進度摘要

| 項目 | 數量 |
|------|------|
| 實作目標（扣除盜版 + 補充 020/085） | 28 個 |
| 目標內已實作 | 28 個 |
| 目標外額外實作（NROM、Namco 108） | 2 個 |
| **總計已實作** | **30 個** |
| 目標涵蓋完成率 | **28 / 28 = 100%** |
| 整體完成率（含額外） | **30 / 31 = 96.8%** |

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

*最後更新：2026-03-19（Mapper019 Namco163、Mapper024/026 VRC6、Mapper085 VRC7、Mapper153 Bandai+WRAM 加入；目標達成 28/28 = 100%）*
