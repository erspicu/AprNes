# 13 Mapper002 / UNROM

## 這章要解決什麼問題

UNROM 是學習 PRG bank switching 的好例子。它沒有 MMC1 的 serial register，也沒有 MMC3 的 IRQ，只展示 CPU 程式空間如何換頁。

本章說明 Mapper002 / UNROM 的 PRG bank switching 與 CHR RAM，並對照 AprNes 的 `Mapper002.cs`。

## NES 硬體觀念

CPU 可直接看到的 PRG ROM window 是 32KB：

```text
$8000-$FFFF
```

但遊戲可能有超過 32KB 的 PRG ROM。UNROM 的做法：

```text
$8000-$BFFF  switchable 16KB PRG bank
$C000-$FFFF  fixed last 16KB PRG bank
```

固定最後 bank 很重要，因為 interrupt vectors 在：

```text
$FFFA-$FFFB  NMI vector
$FFFC-$FFFD  Reset vector
$FFFE-$FFFF  IRQ/BRK vector
```

如果最後 bank 可以被任意切走，CPU reset 或 interrupt 可能找不到正確入口。

UNROM 通常使用 CHR RAM。圖形資料由 CPU 程式在執行時寫入 PPU pattern table。

## 初學者簡化模型

UNROM mapper 狀態只有一個 PRG bank number：

```csharp
int bank;

void WritePrg(ushort addr, byte value)
{
    bank = value & 7;
}

byte ReadPrg(ushort addr)
{
    if (addr < 0xC000)
        return prg[(bank * 0x4000) + (addr - 0x8000)];
    return prg[lastBankOffset + (addr - 0xC000)];
}
```

CHR 則直接讀寫 8KB RAM。

## AprNes / NesCore 實作對照

`Mapper002.cs` 重要欄位：

- `PRG_ROM`。
- `ppu_ram`。
- `PRG_ROM_count`。
- `PRG_Bankselect`。
- `Rom_offset`。

初始化：

```text
Rom_offset = (PRG_ROM_count - 1) * 0x4000
```

這代表最後一個 16KB PRG bank 的起始 offset。

`MapperW_PRG()`：

```csharp
PRG_Bankselect = value & 7;
```

`MapperR_RPG()`：

- `< $C000`：讀 switchable bank。
- `>= $C000`：讀 fixed last bank。

CHR：

- `MapperR_CHR()` 直接讀 `ppu_ram[address]`。
- `MapperW_CHR()` 直接寫 `ppu_ram[addr]`。
- `UpdateCHRBanks()` 把 8 個 1KB pointer 指向 `ppu_ram`。

## 常見錯誤

- 把 `$C000-$FFFF` 也做成可切換，導致 vectors 不穩。
- 忘記 UNROM 通常是 CHR RAM。
- PRG bank number 沒依實際 ROM 大小 mask 或檢查。
- 把 CPU 寫 mapper register 當成寫 PRG ROM。

## 本章重點整理

1. UNROM 展示最小 PRG bank switching。
2. `$C000-$FFFF` 固定最後 bank 是為了 vectors 與常駐程式。
3. CHR RAM 讓 CPU 可以在執行時更新圖形 pattern。

## 下一章銜接

下一章介紹 Mapper003 / CNROM，主題改成 CHR bank switching，也就是 CPU 寫 mapper 後改變 PPU 看到的圖形資料。
