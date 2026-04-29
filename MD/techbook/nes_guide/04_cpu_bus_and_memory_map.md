# 04 CPU Bus 與記憶體地圖

## 這章要解決什麼問題

CPU 有 16-bit address bus，所以能發出 `$0000-$FFFF` 共 64KB 的地址。但這 64KB 不是一整塊 RAM，而是被不同硬體區塊共用。

本章說明 NES CPU memory map，以及 AprNes 如何用 dispatch table 把讀寫分派到正確硬體。

## NES 硬體觀念

CPU 位址空間：

```text
$0000-$1FFF  2KB internal RAM, mirrored every $0800
$2000-$3FFF  PPU registers, mirrored every 8 bytes
$4000-$401F  APU / controller / DMA registers
$4020-$5FFF  cartridge expansion area
$6000-$7FFF  cartridge PRG RAM / SRAM
$8000-$FFFF  cartridge PRG ROM / mapper banks
```

這張表是寫 NES emulator 的中心。

CPU 讀某個地址可能是：

- 讀 RAM。
- 讀 PPU status。
- 讀 APU status。
- 讀 controller bit。
- 讀 mapper 提供的 PRG ROM。
- 讀 open bus。

CPU 寫某個地址可能是：

- 寫 RAM。
- 改 PPU scroll。
- 啟動 OAM DMA。
- 改 APU 聲道參數。
- 改 mapper bank register。

## 初學者簡化模型

可以先寫：

```csharp
byte CpuRead(ushort addr)
{
    if (addr < 0x2000) return ram[addr & 0x7FF];
    if (addr < 0x4000) return PpuReadRegister(0x2000 | (addr & 7));
    if (addr < 0x4020) return ApuIoRead(addr);
    if (addr < 0x6000) return openBus;
    if (addr < 0x8000) return sram[addr - 0x6000];
    return mapper.ReadPrg(addr);
}
```

這個模型足以建立觀念。之後再把 open bus、DMA、DMC bus conflict、register 延遲補上。

## AprNes / NesCore 實作對照

AprNes 在 `MEM.cs` 用 8 個 8KB page handler：

```text
addr >> 13 = 0  $0000-$1FFF
addr >> 13 = 1  $2000-$3FFF
addr >> 13 = 2  $4000-$5FFF
addr >> 13 = 3  $6000-$7FFF
addr >> 13 = 4  $8000-$9FFF
addr >> 13 = 5  $A000-$BFFF
addr >> 13 = 6  $C000-$DFFF
addr >> 13 = 7  $E000-$FFFF
```

讀取時：

- page 0：`Read_NesRam`。
- page 1：`IO_read`，處理 PPU register mirror。
- page 2：`Read_Page2`，再分派 APU/IO 或 mapper expansion。
- page 3：`MapperObj.MapperR_RAM`。
- page 4-7：`MapperObj.MapperR_RPG`。

寫入時：

- page 0：`Write_NesRam`。
- page 1：`IO_write`。
- page 2：`Write_Page2`。
- page 3：`MapperObj.MapperW_RAM`。
- page 4-7：`MapperObj.MapperW_PRG`。

這種設計比 65536 個 handler 的大表更小，也比每次都寫長串 if 更接近 hot path 需求。

## Bus 副作用

AprNes 的 `CpuRead()` 與 `CpuWrite()` 不只是取得或寫入 byte：

- 設定 `cpuBusAddr`。
- 設定 `cpuIsRead`。
- 更新 `cpubus`。
- 在 write cycle 中處理 DMC implicit abort。
- 呼叫對應硬體 handler。

這些狀態會被 DMA、open bus、controller read 等邏輯使用。

## 常見錯誤

- 把 `$2000-$3FFF` 當成 PPU VRAM。它其實是 PPU registers 的 CPU 入口。
- 忘記 PPU register mirror，導致 `$2008` 以後行為錯誤。
- 把 `$8000-$FFFF` 寫入忽略掉。對 ROM 本身不可寫，但對 mapper register 可能有效。
- 在 CPU bus map 裡直接讀 CHR ROM。CHR 是 PPU address space，不是 CPU 直接可見。

## 本章重點整理

1. CPU 64KB 位址空間是多個硬體區塊的映射。
2. Memory read/write 可能帶副作用，不能全部視為 byte array 存取。
3. AprNes 用 8-page dispatch table 把 CPU bus 熱路徑壓得很短。

## 下一章銜接

下一章進入 CPU core，說明 6502 register、flag、addressing mode、opcode dispatch 與 AprNes 的 per-cycle 指令模型。
