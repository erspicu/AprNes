# 13 Mapper002 / UNROM

## 這章要解決什麼問題

UNROM 是學習 PRG bank switching 的好例子。它沒有 MMC1 的 serial register，也沒有 MMC3 的 IRQ，只展示 CPU 程式空間如何換頁。

本章說明 Mapper002 / UNROM 的 PRG bank switching 與 CHR RAM，並對照 AprNes 的 `Mapper002.cs`。

## NES 硬體觀念

**生活比喻**：UNROM 像**書桌只能放兩本書 —— 一本上半部、一本下半部**。下半部那本永遠不換（因為食譜目錄在最後一頁），上半部那本可以隨時換成書架上其他冊。

CPU 可直接看到的 PRG ROM window 是 32KB：

```text
$8000-$FFFF
```

但遊戲可能有超過 32KB 的 PRG ROM。UNROM 的做法：

```
$8000 ┌─────────────────────────┐
      │  可切換 16 KB PRG bank   │  ← 寫 $8000-$FFFF 改變
      │  (最多 8 個或 16 個 bank)│     遊戲程式邏輯主體放這裡
$BFFF ├─────────────────────────┤
$C000 │  固定 16 KB PRG bank     │  ← 永遠是最後一個 bank
      │  (最後一個 bank)         │     vector + 共用副程式放這裡
$FFFF └─────────────────────────┘     例如 reset/NMI/IRQ handler
```

固定最後 bank 很重要，因為 interrupt vectors 在：

```text
$FFFA-$FFFB  NMI vector
$FFFC-$FFFD  Reset vector
$FFFE-$FFFF  IRQ/BRK vector
```

如果最後 bank 可以被任意切走，CPU reset 或 interrupt 可能找不到正確入口。

**為什麼這個設計很普及？** 因為「**遊戲共用程式 (NMI handler、輸入處理、共用副程式) 放固定 bank，關卡資料 / 不同畫面放可切換 bank**」是寫 NES 遊戲最自然的架構。代表作品：《Mega Man》（每一關各占一個 bank）、《Castlevania》、《Contra》、《DuckTales》。

UNROM 通常使用 **CHR RAM**（不是 CHR ROM）。為什麼？因為 UNROM 卡匣硬體沒提供 CHR bank switching，但遊戲又想動態變圖形（例如不同關卡用不同 sprite）。解法：**在卡匣裝 8 KB SRAM 給 PPU 用**，CPU 透過 PPU `$2007` 把當前要用的圖案寫進去。圖形資料由 CPU 程式在執行時寫入 PPU pattern table。

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
