# 04 CPU Bus 與記憶體地圖

## 這章要解決什麼問題

CPU 有 16-bit address bus，所以能發出 `$0000-$FFFF` 共 64KB 的地址。但這 64KB 不是一整塊 RAM，而是被不同硬體區塊共用。

本章說明 NES CPU memory map，以及 AprNes 如何用 dispatch table 把讀寫分派到正確硬體。

## NES 硬體觀念

CPU 位址空間：

```text
$0000-$07FF  2KB RAM (主機內建)         ┐
$0800-$0FFF  RAM mirror                  ├ 同一塊 2KB 重複 4 次
$1000-$17FF  RAM mirror                  │
$1800-$1FFF  RAM mirror                  ┘
$2000-$2007  PPU registers (8 個)        ┐
$2008-$3FFF  PPU register mirror         ┘ 那 8 個重複 1024 次
$4000-$4017  APU / controller / DMA registers
$4018-$401F  CPU 測試模式 (NES 用不到)
$4020-$5FFF  卡匣擴充區 (大部分 mapper 不用)
$6000-$7FFF  卡匣 PRG RAM / SRAM (有電池的話 = 存檔)
$8000-$FFFF  卡匣 PRG ROM / mapper banks
```

這張表是寫 NES emulator 的中心。

**生活比喻**：把 64 KB 想成一棟 65536 房間的大樓平面圖：
- **0–8191 號**：主機自己的小儲藏室（2 KB RAM 重複貼了四次門牌）。為什麼重複？省晶片！1980 年代 address decoder 只接 11 條線，剩下的線忽略，「`$0042` 跟 `$0842` 通到同一間」就是這個結果。
- **8192–16383 號**：8 個 PPU 控制台，但門牌號被多印了 1023 次。
- **16384–16407 號**：APU、手把、DMA 觸發器。
- **16415–24575 號**：卡匣的擴充區，大部分卡匣這裡是「空房間」（讀會回 open bus）。
- **24576–32767 號**：卡匣的 PRG RAM（有電池的卡匣的存檔位置）。
- **32768–65535 號**：卡匣的 PRG ROM。**主廚 90% 的時間都在這個區域看食譜**，所以這裡的讀取速度直接決定遊戲跑多快。

**為什麼模擬器不能把 64 KB 當一個 byte 陣列？** 因為**寫入相同位址，行為可能完全不同**：
- 寫 `$0042` → 真的存進 RAM，下次讀回來
- 寫 `$2000` → 觸發 PPU 控制設定，不會有 byte 留下來
- 寫 `$4014` → 觸發 OAM DMA，CPU stall 513 cycles
- 寫 `$8000` → 對 NROM 是無效操作，對 MMC1 是「累積一個 bit 到 mapper register」

模擬器的 `Mem_w(addr, val)` 函式必須先看 addr 落在哪個區段，分派給對應的 handler，這就是 **bus dispatch**。

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
