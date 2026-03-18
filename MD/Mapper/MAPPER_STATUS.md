# AprNes Mapper 實作狀態清單

參考來源：`ref/mapper/mappers-0.80-summary.md`（31 個，扣除盜版 5 個 = 26 個）+ 補充重要商業 mapper 2 個（020 FDS、085 VRC7）= **目標 28 個**

> 盜版/hack mapper（006 FFE F4xxx、008 FFE F3xxx、015 100-in-1、017 FFE F8xxx、091 HK-SF3）不列入統計與實作目標。

---

## 進度摘要

| 項目 | 數量 |
|------|------|
| 實作目標（扣除盜版 + 補充 020/085） | 28 個 |
| 目標內已實作 | 18 個 |
| 目標外額外實作（NROM、Namco 108） | 2 個 |
| **總計已實作** | **20 個** |
| 目標涵蓋完成率 | **18 / 28 = 64.3%** |
| 整體完成率（含額外） | **20 / 30 = 66.7%** |

---

## 已實作（18 個）

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
| **022** | VRC2a | `Mapper022.cs` — TwinBee 3 (J)；8K PRG×2 + 8×1K CHR；CHR index >> 1（低位忽略） |
| **023** | VRC2b | `Mapper023.cs` — Contra (J), Getsufuu Maden (J)；同 VRC2a 但地址線標準配置，CHR index 不右移 |
| **032** | Irem G-101 | `Mapper032.cs` — PRG mode 0/1 切換，8×1K CHR；SubMapper 1 = Major League (mode 0 + single-A)；Image Fight (J), Major League (J) 驗證通過 |
| **034** | Nina-1 | `Mapper034.cs` — 兩種子變體：CHR-RAM（Deadly Towers/Mashou）用 $8000 PRG 選擇；CHR-ROM（Impossible Mission II）用 $7FFD-$7FFF 寫入暫存器 |
| **066** | GxROM | `Mapper066.cs` — DragonBall, Gumshoe |
| **068** | Sunsoft #4 | `Mapper068.cs` — 4×2K CHR，16K PRG 切換，固定末尾 16K；CHR-as-nametable ($C000/$D000/$E000 bit4)；AfterBurner II (J), Maharaja (J) 驗證通過 |
| **069** | FME-7 | `Mapper069.cs` — CPU 週期 IRQ，PRG-RAM 分頁，4 種鏡像；Batman (J), Gimmick! (J) 驗證通過。⚠️ Sunsoft 5B 擴充音效（YM2149）未實作。⚠️ PAL 版（Mr. Gimmick (E)）畫面異常，PAL timing 尚未支援 |
| **071** | Camerica | `Mapper071.cs` — Firehawk, Linus Spacehead（非授權正規廠商） |
| **078** | Irem 74HC161/32 | `Mapper078.cs` — ⚠️ Holy Diver (J) 可動；Uchuusen Cosmo Carrier (J) 有問題（intro 黑畫面），尚待完全克服 |
| **206** | Namco 108 | `Mapper206.cs` — MMC3 雛形，無 IRQ；《Karnov》驗證通過（文件外額外支援） |

---

## 未實作（目標 28 個中剩餘 10 個）

| Mapper | 名稱 | 代表作品 | 技術重點 |
|:------:|------|---------|---------|
| **020** | FDS（磁碟機系統） | 銀河戰士(日)、薩爾達(日)、惡魔城(日) | BIOS + 磁碟流模擬，複雜度最高 |
| **016/153/159** | Bandai（含 EEPROM 變體） | DragonBall Z 系列、龍珠Z、聖鬥士星矢 | EEPROM 存檔 (24C01)；153/159 為 EEPROM 變體 |
| **018** | Jaleco SS8806 | 忍者龍牙、Baseball 3 | 精密 IRQ + 多 CHR Bank |
| **019** | Namcot 106 | Splatterhouse, Family Stadium '90'、女神轉生II | 最多 8 通道波形音效，內建 RAM |
| **021** | Konami VRC4 | Wai Wai World 2, Gradius 2、大盜五右衛門 | 掃描線 IRQ，多地址線變體 |
| **024** | Konami VRC6 | Akumajo Dracula 3（惡魔城傳說日） | 額外 3 通道音效（方波×2、鋸齒波） |
| **033** | Taito TC0190 | Pon Poko Pon、影之傳說、泡泡龍2 | 類 MMC3 |
| **064** | Tengen RAMBO-1 | Shinobi, Klax, Skull & Crossbones | 類 MMC3，不同 IRQ 機制 |
| **065** | Irem H-3001 | Daiku no Gensan 2、開路先鋒 | 16-bit IRQ |
| **085** | Konami VRC7 | 《拉格朗日點》 | FM 合成音效 (OPLL/YM2413)，複雜度最高 |

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
| ★★★ | **021** VRC4 | 涵蓋多款 Konami 大作，最重要 |
| ★★★ | **064** Tengen RAMBO-1 | Shinobi 等知名作品 |
| ★★ | **024** VRC6 | 需擴充音效 mixer（惡魔城傳說日） |
| ★★ | **019** Namcot 106 | 需擴充音效 mixer，最多 8 通道 |
| ★★ | **016** Bandai | DragonBall Z、聖鬥士等人氣作品 |
| ★★ | **018** Jaleco SS8806 | 忍者龍牙系列 |
| ★ | **033** Taito TC0190 | 影之傳說、泡泡龍2 |
| ★ | **065** Irem H-3001 | 開路先鋒 |
| ✗ | **020** FDS | 磁碟機模擬，最高複雜度，需 BIOS |
| ✗ | **085** VRC7 | 需 OPLL FM 合成，最高複雜度 |

---

*最後更新：2026-03-19*
