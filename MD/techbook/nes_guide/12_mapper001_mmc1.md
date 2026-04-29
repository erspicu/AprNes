# 12 Mapper001 / MMC1

## 這章要解決什麼問題

MMC1 是初學 mapper 時很重要的一步。它不像簡單 mapper 一次寫入一個完整 register，而是用 5 次寫入累積出 5-bit control value。

本章說明 MMC1 的 serial register、PRG mode、CHR mode、mirroring，並對照 AprNes 的 `Mapper001.cs`。

## NES 硬體觀念

MMC1 有 4 個主要 register：

```text
$8000-$9FFF  Control
$A000-$BFFF  CHR bank 0
$C000-$DFFF  CHR bank 1
$E000-$FFFF  PRG bank
```

CPU 寫入 `$8000-$FFFF` 時，MMC1 不是直接使用整個 byte，而是：

- 若 bit 7 為 1：reset shift register。
- 否則取 bit 0，依序放入 5-bit shift register。
- 累積 5 次後，依 address range 寫入對應 register。

這表示 CPU 對 mapper 的寫入是一種硬體序列通訊。

## Control Register

Control register 包含：

- mirroring type。
- PRG bank mode。
- CHR bank mode。

Mirroring：

```text
0  one-screen, lower bank
1  one-screen, upper bank
2  vertical
3  horizontal
```

PRG bank mode：

```text
0/1  switch 32KB at $8000
2    fix first 16KB at $8000, switch 16KB at $C000
3    switch 16KB at $8000, fix last 16KB at $C000
```

CHR bank mode：

```text
0  switch 8KB CHR
1  switch two independent 4KB CHR banks
```

## 初學者簡化模型

MMC1 可以先用兩層狀態：

```text
shiftBuffer
shiftCount

if write bit7:
    reset shift
else:
    shiftBuffer |= (value & 1) << shiftCount
    shiftCount++
    if shiftCount == 5:
        commit to target register
        reset shift
```

接著再根據 control register 決定 PRG/CHR mapping。

## AprNes / NesCore 實作對照

`Mapper001.cs` 重要欄位：

- `PRG_Bankmode`。
- `CHR_Bankmode`。
- `Mirroring_type`。
- `CHR0_Bankselect`。
- `CHR1_Bankselect`。
- `PRG_Bankselect`。
- `MapperShiftCount`。
- `MapperRegBuffer`。

`MapperW_PRG()`：

1. 若 `value & 0x80` 非 0：
   - 清 `MapperShiftCount`。
   - 清 `MapperRegBuffer`。
   - `PRG_Bankmode = 3`。
2. 否則把 `value & 1` 放入 `MapperRegBuffer`。
3. 累積未滿 5 bit 就 return。
4. 依 address range 寫入 control、CHR0、CHR1 或 PRG register。
5. 清 shift buffer。

`MapperR_RPG()`：

- mode 0/1：32KB PRG bank。
- mode 2：固定 `$8000` 第一 bank，切 `$C000`。
- mode 3：切 `$8000`，固定 `$C000` 最後 bank。

`UpdateCHRBanks()`：

- CHR RAM：直接指向 `ppu_ram`。
- 4KB mode：`CHR0_Bankselect` 控 `$0000-$0FFF`，`CHR1_Bankselect` 控 `$1000-$1FFF`。
- 8KB mode：使用 `CHR0_Bankselect >> 1` 選 8KB bank。

AprNes 也使用 `chrCountMask` 與 `banks4kMask`，假設 CHR ROM count 是 power-of-two，以 mask 取代 modulo。

## 常見錯誤

- 把 CPU 寫入整個 byte 當成 MMC1 register value。
- 忘記 bit 7 reset 會把 PRG mode 設回 3。
- 32KB PRG mode 沒忽略 bank number low bit。
- 8KB CHR mode 仍錯誤使用 CHR1 register。
- mirroring type 與 AprNes `Vertical` mode 對應錯。

## 本章重點整理

1. MMC1 的核心是 5-bit serial load register。
2. Control register 同時決定 mirroring、PRG mode、CHR mode。
3. MMC1 展示了 mapper 其實是卡匣上的狀態機，而不是單純 offset 函式。

## 下一章銜接

下一章介紹 Mapper002 / UNROM，重點放在最簡單的 PRG 16KB bank switching。
