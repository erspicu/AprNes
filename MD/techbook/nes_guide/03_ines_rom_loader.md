# 03 iNES ROM 載入與 Header 解析

## 這章要解決什麼問題

`.nes` 檔不是一整塊可以直接丟進 CPU memory 的資料。它包含 header、PRG ROM、CHR ROM，並用 header 告訴 emulator 這片卡匣使用哪種 mapper、多少 ROM bank、哪種 mirroring。

本章說明如何從 `.nes` 檔建立 AprNes 需要的 PRG、CHR 與 Mapper 狀態。

## NES 硬體觀念

NES 卡匣通常包含兩種主要資料：

- PRG ROM：CPU 執行的程式碼與資料。
- CHR ROM 或 CHR RAM：PPU 讀取的 tile pattern。

iNES 檔案常見排列：

```text
16-byte header | optional trainer | PRG ROM | CHR ROM
```

Header 重要欄位：

```text
byte 0-3  magic: "NES" + 0x1A
byte 4    PRG ROM count, unit = 16KB
byte 5    CHR ROM count, unit = 8KB
byte 6    mirroring, battery, trainer, mapper low nibble
byte 7    mapper high nibble, NES 2.0 marker
byte 8    PRG RAM size or NES 2.0 extension field
```

如果 CHR ROM count 是 0，通常代表卡匣使用 CHR RAM。此時 PPU pattern table 的資料不是從 ROM 來，而是遊戲執行時寫入 RAM。

## 初學者簡化模型

最小 ROM loader 可以先做：

1. 檢查前四個 byte 是否為 `NES\x1A`。
2. 讀 PRG bank count。
3. 讀 CHR bank count。
4. 計算 mapper number。
5. 配置 PRG ROM。
6. 若只有一個 PRG bank，就鏡像成 32KB。
7. 若 CHR bank count 大於 0，就配置 CHR ROM。
8. 根據 mapper number 建立 mapper。

簡化版可以先只支援 mapper 0，之後再加 1、2、3、4。

## AprNes / NesCore 實作對照

AprNes 的 ROM 載入在 `Main.cs init(byte[] rom_bytes)`。

主要流程：

```text
檢查 magic number
讀 PRG_ROM_count / CHR_ROM_count
配置 PRG_ROM / CHR_ROM
解析 ROM_Control_1 / ROM_Control_2
判斷 mirroring / battery / trainer / four-screen
計算 mapper number
查 RomDatabase 修正特殊 ROM
MapperRegistry.Create(...)
MapperObj.MapperInit(...)
MapperObj.Reset()
MapperObj.UpdateCHRBanks()
配置 CPU RAM / PPU RAM / OAM / palette / audio buffer
初始化 CPU / PPU / APU / dispatch table
```

AprNes 特別處理：

- 16KB PRG ROM 會複製一份到後 16KB，讓 CPU `$8000-$FFFF` 都有資料。
- CHR ROM count 會依實際檔案長度 clamp，避免壞 header 造成越界。
- `RomDatabase` 用 PRG+CHR CRC32 修正 header 錯誤的 ROM。
- `MapperRegistry` 會依 mapper id 與 submapper 建立正確 mapper instance。

## 重要程式碼觀念

### PRG ROM 鏡像

NROM-128 只有 16KB PRG，但 CPU cartridge window 是 32KB：

```text
$8000-$BFFF  PRG bank 0
$C000-$FFFF  mirror of PRG bank 0
```

AprNes 在載入時把 16KB 複製到第二個 16KB，讓 mapper 讀取時可以用簡單 offset。

### Mapper number

Mapper 編號來自 header byte 6 與 byte 7 的高 nibble 組合：

```text
mapper = (flag6 >> 4) | (flag7 & 0xF0)
```

NES 2.0 header 會有額外判斷。AprNes 也處理部分 old-style mapper 資訊。

### Mirroring

Header bit 0 決定 vertical 或 horizontal mirroring。bit 3 代表 four-screen。

AprNes 用 `Vertical` 指標保存 mirroring mode：

- `0`：horizontal。
- `1`：vertical。
- `2` / `3`：one-screen lower / upper。
- `4`：four-screen。

## 常見錯誤

- 忘記處理 16KB PRG mirror，導致 reset vector 讀錯。
- 把 CHR count 0 當成沒有圖形資料，實際上應該提供 CHR RAM。
- 忽略 trainer offset，導致 PRG/CHR 起始位置錯誤。
- 完全相信 header，不處理錯誤 mapper 或特殊 ROM。
- 在 mapper 初始化前就讓 PPU 讀 CHR bank pointer。

## 本章重點整理

1. `.nes` 檔案需要先解析 header，不能直接當 CPU memory。
2. PRG 是 CPU 程式資料，CHR 是 PPU pattern 資料。
3. Mapper number 是 ROM loader 與後續 bus mapping 的橋樑。

## 下一章銜接

下一章會介紹 CPU 看到的 64KB 記憶體地圖，以及 AprNes 如何把不同地址分派給 RAM、PPU、APU、JoyPad 與 Mapper。
