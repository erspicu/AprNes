# 01 導讀與 NES 硬體總覽

## 這章要解決什麼問題

很多人第一次寫模擬器時，會把問題想成「讀 ROM，解 6502 指令，跑出結果」。這個想法只對了一小部分。NES 遊戲不是只跑在 CPU 上，而是跑在一整台由 CPU、PPU、APU、Controller、Cartridge Mapper 共同構成的硬體系統上。

本章先建立全局地圖。後面每章會把其中一塊硬體拆開，並對照 AprNes 的 `NesCore` 實作。

> **給對硬體名詞還不熟悉的讀者**：如果讀到 register、bus、interrupt、memory-mapped I/O 這些名詞會卡住，建議先看 [A1 計算機組織小複習](A1_computer_organization_primer.md) —— 那篇全程用「廚房 / 主廚 / 工作檯」這類生活比喻把這些抽象概念講清楚，再回來讀本系列會輕鬆很多。
>
> 之後想查 6502 指令的具體規則，看 [A2 6502 完整 256 Opcode 實作參考](A2_6502_opcode_reference.md)。

## NES 硬體觀念

NES 可以粗略分成幾個主要元件：

```text
              +------------------+
              |    Cartridge     |
              | PRG / CHR / SRAM |
              |      Mapper      |
              +---------+--------+
                        |
+---------+      +------+-------+      +---------+
| JoyPad  | <--> |  CPU 2A03   | <--> |   APU   |
+---------+      +------+-------+      +---------+
                        |
                  memory-mapped I/O
                        |
                 +------+-------+
                 |     PPU      |
                 | background   |
                 | sprites      |
                 | palette      |
                 +------+-------+
                        |
                    video output
```

CPU 是主程式執行者。遊戲邏輯、關卡流程、碰撞判定、寫入 PPU register、讀取手把，多數都由 CPU 執行。

PPU 是畫面晶片。它不會等待 CPU 把一整張圖畫好才輸出，而是按照 scanline 與 dot 的節奏，一邊讀取 pattern table、name table、attribute table、sprite OAM，一邊產生像素。

APU 是聲音晶片。它包含 Pulse、Triangle、Noise、DMC 等聲道，並且有自己的 frame counter。DMC 甚至會發動 DMA 讀取 CPU 記憶體，影響 CPU bus timing。

Cartridge 不只是 ROM。很多卡匣含有 Mapper，用來切換 PRG/CHR bank、控制 mirroring、產生 IRQ，甚至提供額外音源。

## 初學者簡化模型

第一版模擬器可以先把硬體想成四條線：

```text
ROM loader -> CPU memory map -> CPU executes instructions
                         |
                         +-> PPU registers -> frame buffer
                         |
                         +-> APU registers -> audio samples
                         |
                         +-> Mapper -> PRG/CHR bank mapping
```

這個模型可以跑一些早期或簡單測試，但不夠精準。真正的 NES 行為取決於每個硬體事件發生在什麼 clock phase。

## AprNes / NesCore 實作對照

AprNes 把核心放在 `NesCore` partial class 中：

- `Main.cs`：初始化、ROM 載入、Mapper 建立、主時脈 loop。
- `MEM.cs`：CPU bus dispatch、DMA、IRQ line。
- `CPU.cs`：6502 register、flag、addressing mode、opcode handler。
- `PPU.cs` / `ppu_new.cs`：PPU register、scroll、OAM、pixel pipeline。
- `APU.cs`：聲道狀態、frame counter、AudioMode 0 sample 輸出。
- `IO.cs` / `JoyPad.cs`：memory-mapped I/O 與 controller serial read。
- `Mapper000.cs` 到 `Mapper004.cs`：逐步展示卡匣硬體如何擴充主機。

AprNes 的重點不是只把結果做對，而是盡量把硬體時序做對。因此它的主 loop 不是「CPU 跑完一條指令，再 PPU 補三倍 cycle」這種簡化做法，而是透過 master clock gate 交錯推進 CPU、PPU、APU、DMA、Mapper。

## 常見錯誤

- 以為 CPU memory 是單純 64KB byte array。實際上很多地址是硬體 register。
- 以為 PPU 是 CPU 呼叫的繪圖 API。實際上 PPU 自己按 dot 前進。
- 以為 Mapper 只是 ROM offset 計算。實際上 Mapper 是卡匣上的硬體狀態機。
- 以為音訊只要最後混出聲音即可。DMC DMA 會反過來影響 CPU timing。

## 本章重點整理

1. NES 模擬器是在模擬一整台硬體系統，不是只模擬 CPU。
2. CPU、PPU、APU、DMA、Mapper 之間的互動大多透過 clock、bus、register 完成。
3. AprNes 的設計重點是把這些互動放回同一條 master clock 時間線。

## 下一章銜接

下一章會先補齊硬體基本觀念：bit field、bus、memory-mapped I/O、mirroring、latch、open bus、clock、IRQ/NMI 與 DMA。
