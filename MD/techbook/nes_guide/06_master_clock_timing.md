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

簡化理解：

```text
CPU:  C . . C . . C . .
PPU:  P P P P P P P P P
```

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
