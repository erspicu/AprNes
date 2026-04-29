# 11 Mapper000 / NROM

## 這章要解決什麼問題

Mapper000，也就是 NROM，是最簡單的 NES 卡匣格式。它沒有 bank switching，是學習 mapper 介面的第一個目標。

本章說明 NROM 如何把 PRG ROM 與 CHR ROM/RAM 接到 CPU/PPU bus，並對照 AprNes 的 `Mapper000.cs`。

## NES 硬體觀念

NROM 常見兩種：

- NROM-128：16KB PRG ROM。
- NROM-256：32KB PRG ROM。

CPU cartridge window 是 `$8000-$FFFF`，大小 32KB。

NROM-256：

```text
$8000-$FFFF  32KB PRG ROM
```

NROM-128：

```text
$8000-$BFFF  16KB PRG ROM
$C000-$FFFF  mirror of same 16KB PRG ROM
```

PPU pattern table：

```text
$0000-$1FFF  8KB CHR ROM or CHR RAM
```

NROM 的 mirroring 通常由 iNES header 決定，mapper 本身沒有 register 可改。

## 初學者簡化模型

NROM mapper 可以很簡單：

```csharp
byte ReadPrg(ushort addr)
{
    return prgRom[addr - 0x8000];
}

byte ReadChr(ushort addr)
{
    return chrRomOrRam[addr];
}
```

如果 ROM loader 已經把 16KB PRG mirror 成 32KB，mapper 內就不需要特殊判斷。

## AprNes / NesCore 實作對照

`Mapper000.cs` 實作 `IMapper`。

初始化：

- 保存 `PRG_ROM`。
- 保存 `CHR_ROM`。
- 保存 `ppu_ram`。
- 保存 `CHR_ROM_count`。

CPU PRG read：

```csharp
return PRG_ROM[address - 0x8000];
```

CHR read：

- 若 `CHR_ROM_count == 0`，讀 `ppu_ram[address]`。
- 否則讀 `CHR_ROM[address]`。

CHR write：

- 只有 CHR RAM 時允許寫入。
- 若有 CHR ROM，寫入忽略。

`UpdateCHRBanks()`：

- 把 `NesCore.chrBankPtrs[0..7]` 指向 8 個連續的 1KB CHR bank。
- 這讓 PPU hot path 可以用 bank pointer 快速讀 CHR。

## 常見錯誤

- 16KB PRG 沒鏡像，導致 reset vector 讀不到正確資料。
- CHR ROM count 0 時沒有建立 CHR RAM。
- 把 CHR RAM 寫入忽略掉，導致使用 CHR RAM 的遊戲沒有圖形。
- 以為 NROM 沒有 mapper class。即使沒有 bank switching，也仍然需要 mapper 介面接 CPU/PPU bus。

## 本章重點整理

1. Mapper000 是固定映射，沒有 mapper register。
2. PRG 16KB mirror 可以在 ROM loader 處理，簡化 mapper。
3. CHR ROM 與 CHR RAM 要分開處理。

## 下一章銜接

下一章介紹 Mapper001 / MMC1。它開始有真正的 mapper register，而且是用 5-bit serial write 控制 PRG/CHR bank 與 mirroring。
