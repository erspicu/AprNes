# 06 Master Clock 與整機同步

## 這章要解決什麼問題

如果 CPU、PPU、APU 各自用自己的 loop 執行，很快會遇到 timing 錯誤。NES 的畫面、聲音、中斷、DMA、Mapper IRQ 都依賴硬體事件發生的相對時間。

本章說明 AprNes 如何用 master clock 把整台機器放到同一條時間線。

## NES 硬體觀念

NTSC NES 有共同的高頻 master clock。不同晶片從這個基準分頻：

- CPU 約 master clock 除以 12。
- PPU 約 master clock 除以 4。
- PPU dot rate 約 CPU cycle 的 3 倍。
- APU 與 CPU cycle 同步，但有 GET/PUT phase 差異。

**生活比喻**：想像廚房裡掛了一個高頻節拍器（master clock），每秒滴答 21,477,272 次。
- **CPU** 是 12 拍滴答動一下（每秒 1,789,773 次 = 1.79 MHz）
- **PPU** 是 4 拍滴答動一下（每秒 5,369,318 次 = 5.37 MHz）
- **APU** 跟 CPU 同步，但內部分成 GET（奇數 cycle）跟 PUT（偶數 cycle）兩種事件

```
master clock 拍   1   2   3   4   5   6   7   8   9   10  11  12  13  14  15...
CPU              [─────────────────── 1 cycle ──────────────────][...
PPU              [─ 1 dot ─][─ 1 dot ─][─ 1 dot ─][─ 1 dot ─][─...
                    ▲          ▲          ▲          ▲
                  PPU 動       PPU 動    PPU 動     PPU 動
```

PPU 每跑 3 dot，CPU 才跑 1 cycle。所以 PPU **不是 CPU 的子函式**，而是另一個獨立、跑得比 CPU 快 3 倍的處理器。

簡化理解：

```text
CPU:  C . . C . . C . .       (每 12 master clock 動一次)
PPU:  P P P P P P P P P       (每 4 master clock 動一次)
```

**為什麼這個比例（CPU:PPU = 1:3）這麼方便？** 因為 NES 的螢幕是 256 像素寬，每條 scanline 共 341 dot，CPU 每條 scanline 跑剛好 113.667 cycles。這個比例讓**遊戲程式設計師可以用「CPU 跑了幾條指令」估算「PPU 跑到了第幾個 dot」**，是 NES 上「scanline IRQ」「split scroll」之類的時序技巧成立的基礎。

但 AprNes 的模型更細，會在指定 master clock phase 執行：

- CPU step 或 DMA step。
- APU step。
- PPU full step。
- PPU half step。
- NMI line sampling。
- IRQ line sampling。
- Mapper clock rise。

## 初學者簡化模型

常見第一版：

```text
cycles = ExecuteOneCpuInstruction()
RunPpu(cycles * 3)
RunApu(cycles)
```

這適合起步，但有問題：

- CPU 指令中途寫 PPU register 的 timing 不精準。
- OAM DMA 插入點不精準。
- DMC DMA 可能錯過特定 CPU read cycle。
- MMC3 scanline IRQ 可能偏移。
- PPU status split timing 測試容易失敗。

進階版可以變成：

```text
for each CPU cycle:
    CPU step one cycle
    PPU step three dots
    APU step one cycle
```

AprNes 則更接近 per-master-clock gate。

## AprNes / NesCore 實作對照

`Main.cs` 中有區域 timing：

- `RegionType.NTSC`
- `RegionType.PAL`
- `RegionType.Dendy`

`ApplyRegionProfile()` 設定：

- `preRenderLine`
- `nmiTriggerLine`
- `masterPerCpu`
- `masterPerPpu`
- `cpuFreq`
- `FrameSeconds`

主時脈狀態：

- `mcCpuClock`
- `mcPpuClock`
- `mcApuPutCycle`

NTSC fast path 會把固定 phase 展開，避免每個 master clock 都跑大量 if。PAL 與 Dendy 有不同的分頻與 unrolled kernel。

CPU gate 內的關鍵邏輯：

```text
if CPU bus 是 read 且 DMA active:
    DmaOneCycle()
else:
    cpu_step_one_cycle()

MapperObj.CpuCycle()
```

其他 phase 會呼叫：

- `apu_step()`
- `ppu_step_new()`
- `ppu_half_step_new()`
- NMI / IRQ line update
- `MapperObj.CpuClockRise()`

## PPU full step 與 half step

AprNes 把 PPU 動作拆成：

- `ppu_step_new()`：dot 開始與主要 phase。
- `ppu_half_step_new()`：背景 shifter、fetch commit、VBlank latch、sprite0 pipeline、`$2007` 第二階段。

這樣能表達 PPU 裡一些半 dot 或 latch 類 timing。

## 常見錯誤

- 用 frame 為單位同步 CPU 與 PPU。
- CPU 指令跑完才處理 PPU register 副作用。
- NMI 在 VBlank 當下立即觸發 CPU handler。
- DMA 寫成瞬間完成。
- 把 PAL/Dendy 套用 NTSC timing。

## 本章重點整理

1. NES 模擬器的核心難點是多硬體同步，不是單一 CPU 速度。
2. AprNes 用 master clock gate 控制 CPU、PPU、APU、DMA、Mapper 的執行順序。
3. Timing 精準度會直接影響 PPU register、DMA、DMC、MMC3 IRQ 與 NMI 行為。

## 下一章銜接

下一章進入 PPU memory 與 register，說明 CPU 如何透過 `$2000-$2007` 控制畫面晶片。
