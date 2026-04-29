# 17 AprNes / NesCore 程式碼閱讀地圖

## 這章要解決什麼問題

`NesCore` 檔案不少，而且許多細節是為了 timing accuracy 與 hot path performance。初學者如果直接從 `CPU.cs` 或 `ppu_new.cs` 中間開始讀，很容易迷路。

本章提供閱讀順序與檔案角色總結。

## 建議閱讀順序

### 1. `Main.cs`

先讀初始化流程：

- `init(byte[] rom_bytes)`。
- ROM header 解析。
- PRG/CHR allocation。
- Mapper 建立。
- PPU/APU/CPU 初始化。
- `HardResetState()`。
- region timing。

再讀主 loop：

- `Run_NTSC()`。
- `Run_PAL()`。
- `Run_Dendy()`。
- master clock unrolled kernel。

閱讀目標：理解 AprNes 如何把 ROM 變成一台正在跑的 NES。

### 2. `MEM.cs`

重點：

- CPU bus dispatch table。
- `CpuRead()` / `CpuWrite()` 會呼叫的 memory handler。
- DMA state。
- `DmaOneCycle()`。
- `DmaFetch()`。
- `UpdateIRQLine()`。

閱讀目標：理解 CPU address 如何分派到 RAM、PPU、APU、JoyPad、Mapper。

### 3. `IO.cs`

重點：

- `$2000-$3FFF` mirror 到 PPU register。
- `$4000-$4017` 分派到 APU、OAM DMA、JoyPad。

閱讀目標：建立 CPU register read/write 與硬體 handler 的對照。

### 4. `CPU.cs`

先讀：

- CPU register 與 flags。
- `CpuRead()` / `CpuWrite()` 使用方式。
- addressing mode helper。
- `CompleteOperation()`。
- `PollInterrupts()`。
- `InitOpHandlers()`。

不要一開始就逐行讀 256 個 opcode。先選 `LDA`, `STA`, `ADC`, `BNE`, `BRK` 類代表指令看模式。

閱讀目標：理解 AprNes 的 per-cycle CPU state machine。

### 5. `PPU.cs`

先讀 register 與狀態：

- `ppu_r_2002()`。
- `ppu_w_2000()`。
- `ppu_w_2001()`。
- `ppu_w_2005()`。
- `ppu_w_2006()`。
- `ppu_r_2007()` / `ppu_w_2007()`。
- palette。
- OAM。

閱讀目標：理解 CPU 如何透過 `$2000-$2007` 控制 PPU。

### 6. `ppu_new.cs` 與 `ppu_dispatch.cs`

這是較難的部分。建議先掌握高層：

- `ppu_step_new()`。
- `ppu_half_step_new()`。
- deferred updates。
- sprite evaluation。
- sprite fetch。
- frame render。

`ppu_dispatch.cs` 是為了 hot path dispatch，不需要一開始逐個 handler 讀完。

閱讀目標：理解 AprNes 的 PPU 是 dot pipeline，不是 frame renderer。

### 7. `APU.cs`

先讀：

- channel state。
- `initAPU()`。
- `apu_step()`。
- `ApuFrameCounterStep()`。
- `ApuOutputCatchup()`。
- `generateSample()`。

只看 `AudioMode = 0` 主線即可。`AudioPlus` 進階音訊管線可先略過。

閱讀目標：理解 APU 如何每 cycle 更新聲道，並定期輸出 44100Hz sample。

### 8. `JoyPad.cs`

重點：

- `P1_Port` / `P2_Port`。
- shift register。
- strobe。
- `gamepad_r_4016()` / `gamepad_w_4016()`。
- APU step 中的 delayed shift。

閱讀目標：理解 controller 是 serial device。

### 9. Mapper

建議順序：

1. `IMapper.cs`：先看 mapper 介面。
2. `Mapper000.cs`：固定映射。
3. `Mapper002.cs`：PRG bank switching。
4. `Mapper003.cs`：CHR bank switching。
5. `Mapper001.cs`：MMC1 serial register。
6. `Mapper004.cs`：MMC3 bank 與 A12 IRQ。
7. `Mapper004RevA.cs` / `Mapper004MMC6.cs`：revision 與 MMC6 補充。

閱讀目標：理解卡匣硬體如何插入 CPU/PPU bus。

## AprNes 架構總結

AprNes 可以用這句話理解：

> AprNes 把 NES 視為 CPU、PPU、APU、DMA、Mapper 共享時脈與匯流排的硬體系統，透過 master clock 調度各元件，並用 memory-mapped register 與 mapper 介面重建主機晶片和卡匣硬體的互動。

## 對照表

```text
ROM loader        Main.cs
Master clock      Main.cs
CPU bus           MEM.cs, IO.cs
CPU core          CPU.cs
PPU registers     PPU.cs
PPU pipeline      ppu_new.cs, ppu_dispatch.cs
APU               APU.cs
Controller         JoyPad.cs
Mapper interface  IMapper.cs
Mapper 0-4        Mapper000.cs ... Mapper004.cs
```

## 常見錯誤

- 直接從最佳化 hot path 開始讀，忽略整體資料流。
- 把 partial class 當成無關檔案，其實它們共同組成同一個 `NesCore`。
- 看 PPU 前沒先懂 `$2000-$2007`。
- 看 Mapper004 前沒先看 Mapper000、002、003。
- 看 AudioPlus 前沒先看 `AudioMode = 0`。

## 本章重點整理

1. 先讀初始化與 bus，再讀 CPU/PPU/APU 細節。
2. Mapper 依 0、2、3、1、4 的概念難度閱讀會比較順。
3. AprNes 的複雜度主要來自硬體時序與 hot path performance。

## 下一步

如果要繼續擴寫，本系列可以從第 1 章開始逐篇增加圖解、程式碼片段、測試 ROM 建議與實作練習。
