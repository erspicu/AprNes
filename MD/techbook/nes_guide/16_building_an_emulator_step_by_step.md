# 16 從零寫 NES 模擬器的實作路線

## 這章要解決什麼問題

前面章節已經介紹硬體與 AprNes 實作。這章整理一條務實的開發路線，讓初學者知道先做什麼、後做什麼，避免一開始就被 cycle-accurate PPU、DMA edge case、MMC3 IRQ 壓垮。

## 階段 1：ROM Loader 與 Mapper000

目標：

- 能載入 `.nes`。
- 能解析 iNES header。
- 能建立 PRG ROM / CHR ROM 或 CHR RAM。
- 只支援 Mapper000。

驗收：

- 能讀 reset vector。
- CPU 可以從 `$8000-$FFFF` 取 opcode。
- PPU 可以讀 CHR pattern data。

## 階段 2：CPU Memory Map

目標：

- `$0000-$1FFF` RAM mirror。
- `$2000-$3FFF` PPU register stub。
- `$4000-$401F` APU/IO stub。
- `$6000-$7FFF` SRAM。
- `$8000-$FFFF` mapper PRG。

驗收：

- CPU 測試 ROM 能正確讀寫 RAM。
- 讀寫 mapper PRG 不會越界。

## 階段 3：6502 CPU Core

目標：

- Register 與 flags。
- Addressing modes。
- 官方 opcodes。
- Stack。
- Branch。
- Interrupt。

建議：

- 先 instruction-level。
- 每條指令回傳 cycle count。
- 先跑 nestest 類 CPU 測試。

後續再改 per-cycle。

## 階段 4：PPU 最小畫面

目標：

- PPU memory map。
- `$2000-$2007` 基本行為。
- background rendering。
- palette。
- VBlank/NMI。

建議：

- 第一版 scanline renderer 即可。
- 先讓畫面能出來。
- 不急著做所有 PPU timing bug。

## 階段 5：Controller

目標：

- `$4016` strobe。
- `$4016/$4017` serial read。
- button order 正確。

驗收：

- 遊戲 title screen 可以按 Start。
- 方向鍵與 A/B 正確。

## 階段 6：Sprite 與 OAM

目標：

- OAM。
- `$2003/$2004`。
- `$4014` OAM DMA 初版。
- sprite rendering。
- sprite 0 hit。

建議：

- Sprite 先做 functional。
- 之後再補 overflow bug 與 per-dot evaluation。

## 階段 7：APU AudioMode 0

目標：

- Pulse。
- Triangle。
- Noise。
- DMC。
- frame counter。
- sample accumulator。
- lookup table mixing。

建議：

- 先輸出 44100Hz mono。
- 不先做進階類比濾波或 stereo effect。

## 階段 8：Mapper001-004

推薦順序：

1. Mapper002 / UNROM：
   - 最小 PRG bank switching。
2. Mapper003 / CNROM：
   - 最小 CHR bank switching。
3. Mapper001 / MMC1：
   - serial register。
   - PRG/CHR mode。
   - mirroring。
4. Mapper004 / MMC3：
   - PRG/CHR bank。
   - A12 IRQ。
   - revision 差異。

這個順序比直接從 MMC3 開始穩定，因為每個 mapper 只新增一兩個概念。

## 階段 9：提高 Timing 精準度

當 functional emulator 能跑不少遊戲後，再開始補：

- CPU per-cycle。
- PPU dot-level rendering。
- `$2005/$2006/$2007` 延遲與 buffer。
- DMA per-cycle。
- DMC DMA。
- open bus。
- sprite evaluation bug。
- MMC3 A12 edge。

AprNes 基本上是在這個階段之後的形態。

## AprNes / NesCore 實作對照

如果以 AprNes 當最終參考，可以把檔案對應到開發階段：

- ROM loader：`Main.cs`。
- CPU bus：`MEM.cs`, `IO.cs`。
- CPU core：`CPU.cs`。
- PPU register：`PPU.cs`。
- PPU dot pipeline：`ppu_new.cs`, `ppu_dispatch.cs`。
- APU：`APU.cs`。
- Controller：`JoyPad.cs`。
- Mapper：`Mapper000.cs` 到 `Mapper004.cs`。

## 常見錯誤

- 一開始就追求 AprNes 等級 timing，導致沒有任何遊戲能先跑起來。
- 只寫 CPU，不寫 PPU 最小輸出，無法觀察結果。
- 忽略 mapper，導致只能跑很少 ROM。
- 在 functional 尚未穩定時就最佳化 hot path。
- 沒有測試 ROM，只靠遊戲畫面猜錯誤。

## 本章重點整理

1. 寫 NES emulator 應該分階段，先 functional，再 timing accurate。
2. Mapper002、003、001、004 是由簡到難的好路線。
3. AprNes 可作為高精準度終點參考，不一定是第一版實作形態。

## 下一章銜接

下一章提供 AprNes 程式碼閱讀地圖，幫你回到 `NesCore` 時知道每個檔案該怎麼讀。
