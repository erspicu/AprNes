# 14 Mapper003 / CNROM

## 這章要解決什麼問題

CNROM 是 CHR bank switching 的最小例子。它的 PRG ROM 基本固定，但 CPU 寫 mapper register 後，PPU `$0000-$1FFF` 看到的 CHR ROM bank 會改變。

本章說明 Mapper003 / CNROM，並對照 AprNes 的 `Mapper003.cs`。

## NES 硬體觀念

**生活比喻**：CNROM 跟 UNROM 剛好相反 —— **食譜本身固定不換，但圖片冊可以一頁一頁換**。主廚（CPU）一直看同一本食譜；上菜小弟（PPU）每換一個關卡就翻到不同的圖片冊。

PRG 與 CHR 是兩個不同世界：

- CPU 執行 PRG ROM。
- PPU 讀取 CHR ROM/RAM 作為 pattern table。

CNROM 通常：

```
CPU 看到的：              PPU 看到的：
$8000 ┌──────────┐        $0000 ┌────────────────┐
      │          │              │  可切換 8 KB    │
      │  固定    │              │  CHR ROM bank   │  ← 寫 CPU 的 $8000-$FFFF
      │  PRG ROM │              │                 │     會改變這裡
      │          │        $1FFF └────────────────┘
$FFFF └──────────┘
```

```text
CPU $8000-$FFFF     fixed PRG ROM
PPU $0000-$1FFF     switchable 8KB CHR ROM bank
```

CPU 不能直接執行 CHR，也不能直接把 CHR 當 CPU memory。**CPU 要寫 `$8000-$FFFF` 任何位址**（注意不是寫到那個位址，是給 mapper 一個訊號），mapper 收到後改變 PPU CHR bus 看到的 ROM bank。

```assembly
; 切到 CHR bank 2 (第 3 個 8 KB bank)
LDA  #$02
STA  $8000        ; 寫到任何 $8000-$FFFF 都會被 CNROM 解讀為「換 CHR bank」
                   ; mapper 取 value & 0x03 (CNROM 最多 4 個 bank)
```

**為什麼有 CNROM？** 因為某些遊戲（例如《Solomon's Key》、《Gradius》）的程式邏輯小（< 32 KB），但需要的圖形多（多種 boss、特效）。這時用 CNROM 比 UNROM 划算：PRG 維持 32 KB 不換頁，但 CHR 可以放 32 KB 或 64 KB，每關用不同 8 KB。**代表作品**：《Solomon's Key》、《Gradius》、《Q*bert》、《Spelunker》。

## 初學者簡化模型

CNROM 狀態：

```csharp
int chrBank;

void WritePrg(ushort addr, byte value)
{
    chrBank = value & 3;
}

byte ReadChr(ushort addr)
{
    return chrRom[(chrBank * 0x2000) + addr];
}
```

PRG read 則像 NROM 一樣固定。

## AprNes / NesCore 實作對照

`Mapper003.cs` 重要欄位：

- `PRG_ROM`。
- `CHR_ROM`。
- `ppu_ram`。
- `CHR_ROM_count`。
- `CHR_Bankselect`。

`MapperW_PRG()`：

```csharp
CHR_Bankselect = value & 3;
UpdateCHRBanks();
```

`MapperR_RPG()`：

```csharp
return PRG_ROM[address - 0x8000];
```

`MapperR_CHR()`：

- 若 `CHR_ROM_count == 0`，讀 `ppu_ram[address]`。
- 否則讀 `CHR_ROM[address + (CHR_Bankselect << 13)]`。

`UpdateCHRBanks()`：

- 指向 `CHR_ROM + (CHR_Bankselect << 13)`。
- 每 1KB 填入 `NesCore.chrBankPtrs[0..7]`。

## 與 UNROM 對照

```text
UNROM:
  CPU $8000-$BFFF  switch PRG
  PPU $0000-$1FFF  CHR RAM fixed

CNROM:
  CPU $8000-$FFFF  PRG fixed
  PPU $0000-$1FFF  switch CHR
```

這對照很適合初學者理解 Mapper 同時可以影響 CPU bus 與 PPU bus。

## 常見錯誤

- 在 CPU memory map 中直接映射 CHR ROM。
- CPU 寫 mapper 後沒有更新 PPU 使用的 CHR bank pointer。
- 只改 `MapperR_CHR()`，卻忘記 PPU hot path 使用 `chrBankPtrs`。
- 忽略 CHR ROM count 0 的 CHR RAM fallback。

## 本章重點整理

1. CNROM 是最小 CHR bank switching 範例。
2. CPU 寫 mapper register 可以改變 PPU 看到的 pattern table。
3. AprNes 透過 `UpdateCHRBanks()` 讓 PPU hot path 快速讀取目前 CHR bank。

## 下一章銜接

下一章介紹 Mapper004 / MMC3。它同時支援 PRG/CHR bank switching，並透過 PPU A12 edge 產生 scanline IRQ。
