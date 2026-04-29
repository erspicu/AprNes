# 09 APU 與 AudioMode 0

## 這章要解決什麼問題

NES 聲音不是播放 WAV，而是 APU 依 register 狀態即時產生波形。AprNes 有多種音訊模式，本系列只聚焦 `AudioMode = 0`，也就是 Pure Digital 路徑。

本章介紹 APU 五聲道、frame counter，以及 AprNes 如何產生 44100Hz audio samples。

## NES 硬體觀念

NES APU 主要聲道：

- Pulse 1。
- Pulse 2。
- Triangle。
- Noise。
- DMC。

### Pulse

Pulse channel 有：

- duty sequence。
- timer period。
- envelope。
- sweep。
- length counter。

Pulse 常用於旋律與音效。

### Triangle

Triangle 使用 32-step 序列。它沒有 envelope，而是透過 linear counter 與 length counter 控制是否發聲。

Triangle 常用於 bass line。

### Noise

Noise 使用 LFSR 產生 pseudo-random waveform。它有 mode bit 與 period table，常用於鼓聲、爆炸、雜訊音效。

### DMC

DMC 從 CPU memory 讀 sample bytes，透過 delta modulation 改變 7-bit output value。DMC 重要的不只是聲音，還會發動 DMA，影響 CPU bus。

### Frame counter

APU frame counter 會產生 quarter frame 與 half frame：

- quarter frame：更新 envelope、triangle linear counter。
- half frame：更新 length counter、sweep。

## 初學者簡化模型

第一版可以這樣做：

1. 每個 CPU cycle 更新 APU channel timer。
2. 以固定 sample rate 累積時間。
3. 到達 sample 時，把目前 channel output 混成一個 sample。
4. 先做 Pulse、Triangle、Noise，再做 DMC。

不用一開始就做高品質重採樣或類比濾波。

## AprNes / NesCore 實作對照

`APU.cs` 是主體。

重要設定：

- `APU_SAMPLE_RATE = 44100`。
- `_sampleAccum`：sample rate accumulator。
- `_cpuFreqInt`：依 region 設定的 CPU frequency。
- `AudioMode = 0` 時使用 `ApuOutputCatchup()`。

`apu_step()` 每次 APU cycle 做：

- controller shift processing。
- GET cycle 更新 Pulse/Noise timer、DMC clock。
- PUT cycle 處理 frame interrupt clear 與 DMC load DMA countdown。
- 更新 DMC `$4015` deferred status。
- 更新 Triangle timer。
- 執行 `ApuFrameCounterStep()`。
- 更新 length halt flags。
- 呼叫 `apuOutputFn()`。

`ApuRefreshOutputFn()`：

```csharp
apuOutputFn = AudioMode > 0 ? &ApuOutputPushPlus : &ApuOutputCatchup;
```

所以 `AudioMode = 0` 會走：

- `ApuOutputCatchup()`。
- 累積 `_sampleAccum += APU_SAMPLE_RATE`。
- 若還沒到 `_cpuFreqInt` 就 return。
- 到 sample 時呼叫 `generateSample(...)`。

`generateSample()`：

- Pulse 走 `SQUARELOOKUP`。
- Triangle/Noise/DMC 走 `TNDLOOKUP`。
- 加上 mapper expansion audio。
- 套用 DC killer。
- 套用 `Volume`。
- clamp 到 `short`。
- 呼叫 `AudioSampleReady?.Invoke((short)clamped, (short)clamped)`。

## Register 對照

AprNes 的 `IO.cs` 把 `$4000-$4017` 寫入分派到：

- `apu_4000` 到 `apu_4003`：Pulse 1。
- `apu_4004` 到 `apu_4007`：Pulse 2。
- `apu_4008` 到 `apu_400b`：Triangle。
- `apu_400c` 到 `apu_400f`：Noise。
- `apu_4010` 到 `apu_4013`：DMC。
- `apu_4015`：channel enable / status。
- `$4017`：frame counter mode。

## 常見錯誤

- 用 frame 為單位產生聲音，導致延遲與音高不準。
- Pulse sweep 與 envelope 只在 sample 時更新，而不是依 APU timing。
- Triangle 忽略 linear counter。
- DMC 只當聲音資料，不處理 DMA 與 IRQ。
- 混音直接線性相加，沒有使用 NES 常見非線性 lookup table。

## 本章重點整理

1. APU 是五個硬體聲道加 frame counter 的同步系統。
2. `AudioMode = 0` 用 sample accumulator 在 CPU/APU cycle 中定期產生 sample。
3. DMC 會影響 CPU bus timing，因此聲音模組也會反過來影響整機模擬。

## 下一章銜接

下一章會把 DMA 與 controller 放在一起介紹，說明 OAM DMA、DMC DMA 與 JoyPad serial read 如何透過 CPU bus 工作。
