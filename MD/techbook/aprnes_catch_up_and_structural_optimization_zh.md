# AprNes 為什麼少用 Catch-Up，而改走結構性優化

## 前言

在模擬器設計中，`catch-up` 常常是一個很自然的想法：

- 先讓某個元件跑在前面
- 等到需要互動、觀察或同步時
- 再把其他元件補跑到正確時間點

這個觀念在很多較粗粒度的模擬器裡很好用，因為它能省掉大量「所有元件每拍一起前進」的成本。

但這裡有一個很重要的現實：

**越高精度、越高耦合的 timing 模型，`catch-up` 可以運作的空間越小。**

而 `AprNes` 的方向，正是偏向高精度、硬體行為導向的 timing 模型。  
它最後採用接近 `TriCNES` 的設計思路，不只是 PPU，連 CPU / PPU / APU / mapper 的時間關係都盡量維持在更細的層級上。

這代表一件事：

- `catch-up` 不是完全不能用
- 但它能用的地方很少
- 而且一旦用得太多，正確性風險會快速上升

因此 `AprNes` 的核心策略不是「大量依賴 catch-up 省成本」，而是：

**只在少數、邊界清楚、語意很明確的地方有限度使用 catch-up，其他地方則轉向程式本身的結構性優化。**

這篇文章要介紹的，就是這個設計判斷背後的原因。

## Catch-Up 在粗模型裡通常很好用

如果模擬器使用的是：

- `per-frame`
- `per-scanline`
- instruction-level CPU 主導模型

那麼 `catch-up` 往往很實用。

原因是這些模型本來就容忍比較多近似：

- 很多中間狀態不保留
- 很多副作用不要求在極小時間點被觀察
- 很多事件只要在較大邊界前後「結果正確」即可

在這種情況下，你可以讓某些元件延後處理，再在需要時補跑，通常仍然維持不錯的效果。

## 但在高精度模型裡，Catch-Up 的空間會迅速縮小

AprNes 這類模型和粗模型最大的差異，在於它保留了更多「中間時刻也有意義」的狀態。

舉例來說，在這種模型裡：

- register write 可能不是立即生效，而是延遲幾個 phase 才提交
- 某些 `PPU` / `mapper` 行為要在非常特定的 dot / 半步 / 邊界才算正確
- 某些 `open bus`、`latch`、`pipeline` 狀態不能被粗暴地事後補帳
- 某些 IRQ / A12 / OAM corruption 相關邏輯，一旦越過錯的邊界就會整段失真

這使得 `catch-up` 不再只是：

- 補一點 cycle
- 補一段時間

而是變成：

- 補跑時是否保住了所有中間 side effect
- 補跑時是否維持了正確的事件順序
- 補跑時是否沒有跨過不可跨的觀察邊界

所以對高精度模型來說，`catch-up` 本身就是一種高風險技術。

## Catch-Up 本身也有代價

很多人會把 `catch-up` 視為效能優化手段，但這只對一部分模型成立。

在高精度模型中，`catch-up` 常常也自帶成本：

- 額外的同步邏輯
- 額外的 timestamp / counter 維護
- 額外的 side effect 重播或延遲提交規則
- 額外的函式邊界與判斷
- 額外的驗證成本

也就是說，`catch-up` 並不是「免費的省事捷徑」。  
在很多高擬真設計裡，它可能反而會：

- 增加熱路徑複雜度
- 傷害 JIT / inline / I-cache 形狀
- 提高 correctness regression 的風險

當模型本身已經很精細時，這些代價往往比它節省的東西還麻煩。

## AprNes 的結論：只保留極少部分 Catch-Up

從目前的程式結構來看，AprNes 並不是完全排斥 `catch-up`。  
它仍然保留了少數「局部、硬體語意明確、可以被嚴格界定」的做法。

比較典型的例子有：

### 1. PPU register access 的小範圍 master-clock 推進

在 `PPU.cs` 中，某些 register handler 會觸發固定長度的 master-clock 推進：

- `$2002` read 走 `nestedTick7Fn()`
- `$2007` read / write 走 `nestedTick7Fn()`
- `$2004` read 走 `nestedTick7Fn()`
- `$2000` write 走 `nestedTick2Fn()`

這本質上是一種**非常小範圍、邊界明確的 catch-up**：

- 不是「整個系統想補多久就補多久」
- 而是「這個硬體操作依規則必須推進固定幾個 master clocks」

這種局部 catch-up 的好處是：

- 行為定義清楚
- 容易和硬體語意對齊
- 風險比大範圍延後同步小得多

### 2. 延遲提交型狀態更新

另一種比較像 `catch-up` 的做法，是延遲提交而不是立即生效，例如：

- `$2005` 延遲 scroll 更新
- `$2006` 延遲 `t -> v` copy
- `$2001` 延遲 mask / emphasis 更新
- `$2007` 用 state machine 分 phase 完成讀寫與遞增

這裡的設計重點不是「省略細節」，而是：

- 先把狀態記成 pending
- 再在正確的 phase / dot / step 上提交

這也算是一種受控的延後處理，但它並不是寬鬆的大範圍 catch-up，反而更像**硬體時序的精確落點管理**。

### 3. APU Pure Digital 輸出的 sample-rate catch-up

`APU.cs` 裡還有一個很明確的 catch-up 使用點：`ApuOutputCatchup()`。

它只用在 `AudioMode == 0` 的 Pure Digital 輸出路徑。APU 本體仍然每個 CPU/APU cycle 執行：

- pulse / triangle / noise / DMC timer
- frame counter
- DMC DMA 延遲與狀態更新
- controller strobe / shift
- length / envelope / sweep 的精確時序

但最後的混音與 sample 輸出不是每個 APU cycle 都算。  
`ApuOutputCatchup()` 會每 cycle 累加 `_sampleAccum += APU_SAMPLE_RATE`，只有當累積值達到 `_cpuFreqInt` 時才真正計算 `mapperExpansionAudio` 並呼叫 `generateSample()`。

這等於把「音訊波形狀態」維持細粒度，但把「送出 44.1 kHz sample」延後到 sample 邊界才處理。  
以 NTSC CPU 約 1.79 MHz、輸出 44.1 kHz 來看，約每 40 個 CPU cycle 產生一次 sample。

這種 catch-up 的邊界很安全，因為它沒有跳過 APU 的硬體狀態推進；它只省略中間 cycle 不會被外部觀察到的最終混音輸出。

相對地，`AudioMode > 0` 會走 `ApuOutputPushPlus()`，每個 APU cycle 都把主 APU 與 expansion audio 推進 AudioPlus。  
這也反映出 AprNes 的判斷：只有 Pure Digital 這種輸出模型可以接受 sample-rate catch-up；需要更細緻音訊重建時，就回到 per-cycle 推送。

### 4. APU 內部仍以延遲事件維持精確時序

APU 內還有很多「延後生效」的狀態，但它們更接近硬體事件排程，而不是寬鬆的 catch-up：

- `$4015` DMC enable / disable 透過 `dmcStatusDelay` 延遲 3-4 cycle 生效
- DMC load DMA 透過 `dmcLoadDmaCountdown` 延後觸發
- `$4015` read 對 frame interrupt 的清除延到下一個 PUT cycle
- `$4017` frame counter reset 依 GET / PUT phase 延遲 3 或 4 cycle
- length counter reload 以 flag 方式延到正確 quarter / half-frame 邊界

這些設計的共同點是：**不是把 APU 晚點整批補跑，而是把會被硬體觀察到的狀態放在正確 cycle 才提交。**

## 為什麼 AprNes 最後把重心放在「程式本身的優化」

因為對 AprNes 這條路線來說，真正比較可靠的作法不是：

- 讓更多元件晚點再追

而是：

- 讓正確的細粒度模型本身跑得更快

這就把優化重心從 `catch-up` 轉成：

- 主迴圈結構優化
- phase 分層
- static dispatch
- region-specific fast path
- PPU hot path 專門化
- JIT / IL / I-cache 友善化

也就是說，AprNes 選擇的是：

> 不用大量放寬模型，再靠 catch-up 補回來；  
> 而是盡量維持精細模型，然後把精細模型本身整理成比較能跑的形狀。

## Main.cs 的特殊處理：它特別在哪裡

如果你只看觀念，AprNes 的 `Main.cs` 會像「主迴圈」。  
但如果你從效能與架構角度看，它其實是一個很有企圖心的**時序執行器**。

它的特殊性主要在下面幾點。

### 1. 不是單一 generic tick loop，而是 region / FDS 靜態分派

`run()` 不走一個統一 `MasterClockTick()` 然後每次判斷區域。  
它一開始就依條件直接分派到：

- `Run_NTSC()`
- `Run_PAL()`
- `Run_Dendy()`
- `Run_FDS()`

這個設計的特殊點在於：

- 把「區域差異」從熱路徑裡提前搬出去
- 避免每個 tick 都做 `if (Region == ...)`
- 讓每個區域都可以有自己的最佳化節奏

這是典型的 **static dispatch 換 branch 消除**。

### 2. NTSC / Dendy / FDS 走結構性 unroll，不靠一般化計數器迴圈硬跑

以 `Run_NTSC()` 為例，主體是 `MasterClockTickUnrolledNTSC()`。  
它不是每拍都去跑一個 generic tick，再讓內部用 countdown 判斷 CPU / PPU / APU 哪個該動，而是把 12 個 master clocks 的事件序列**直接展開**。

這種作法的特殊點在於：

- 熱路徑中的控制流更固定
- 少掉 generic scheduler 的分支與函式層
- JIT 更容易看到穩定的執行形狀

換句話說，AprNes 不是靠「更聰明的 catch-up」減少成本，而是靠「把本來就要做的事情寫成更容易被機器執行的結構」來減少成本。

### 3. WarmUp 先把 phase 對齊，再進 fast path

`WarmUpNTSC()`、`WarmUpFDS()`、`WarmUpDendy()` 的目的，不是跑一個完整慢速前奏，而是把 `mcCpuClock` / `mcPpuClock` 對齊到 fast path 想要的起始狀態。

這個技巧的價值在於：

- 冷啟動不必一直掛著 generic phase-align 邏輯
- 主迴圈可以假設自己從乾淨狀態開始
- 後面的 unrolled kernel 就能維持更固定的形狀

這其實也是一種很工程化的思路：  
**把麻煩集中到冷路徑，換熱路徑更乾淨。**

### 4. NestedTick 專門化，避免 register access 反向打亂主迴圈

`nestedTick7Fn` / `nestedTick2Fn` 這個設計非常關鍵。

因為某些 PPU register 操作需要在 CPU cycle 中間額外推進固定數量的 master clocks。  
如果做法很粗糙，這些 handler 很容易又反過來呼叫 generic `mcTickFn`，造成：

- recursion
- 難預測的計數器狀態
- 熱路徑與冷路徑互相污染

AprNes 的做法是：

- 在 `Run_X()` 入口先綁好對應區域的 `nestedTick` 版本
- 讓 register handler 用專門化版本完成那段固定推進
- 讓主迴圈與小範圍 catch-up 保持可預測的交界

這使得「有限 catch-up」不會破壞主 fast path 的整體形狀。

### 5. PAL / Dendy / FDS 不是被 NTSC 順手兼容，而是各自特化

這點很重要。  
很多模擬器雖然宣稱支援多區域，但實作上仍讓 NTSC 的假設滲進全系統。

AprNes 在這裡的特殊性是：

- `masterPerCpu`
- `masterPerPpu`
- warm-up
- nested tick
- outer unroll

都依區域或 FDS 特性做了顯式處理。  
這樣做的成本比較高，但好處是：

- 不必在熱路徑一直背著「NTSC 特判修補 PAL」的歷史包袱
- timing 行為比較容易維持自洽

## PPU 結構：AprNes 怎麼把高精度模型整理成可執行的形狀

AprNes 的 PPU 不是單一大函式，而是分成三層。

### 1. PPU.cs：狀態、匯流排、暫存器語意、sprite evaluation 基礎

`PPU.cs` 比較像「PPU 的語意層」。

它主要放：

- `PpuBusRead()` / `PpuBusWrite()`
- `CIRAMAddr()`、palette cache、PPU RAM 邏輯
- `$2000-$2007` register handler
- `SpriteEvalInit()` / `SpriteEvalTick()` / `SpriteEvalEnd()`
- `RenderScreen()`

這一層的重要性在於：

- 它定義了 PPU 對外可觀察行為
- 但不把每一個 dot 的熱路徑全擠在一起

### 2. ppu_new.cs：phase 分層，把「什麼時候做」拆清楚

`ppu_new.cs` 是時間相位層。

這裡主要做：

- `ppu_step_new()`：依 scanline 狀態選 table
- `PpuPhase2_DeferredUpdates()`：延遲 register update 提交
- `PpuPhase3_Events()`：VBlank / odd frame / pre-render 事件
- `PpuPhase4_*()`：sprite eval、OAM corruption、sprite fetch、dummy fetch
- `PPU_DATA_Pipeline_Step()`：`$2007` 的 bus / latch / read-write pipeline
- `ppu_half_step_new()`：shift register、commit fetch 結果、sprite0 hit pipeline、phase 3 提交

這層的價值是把高精度模型裡很容易糊在一起的東西拆開：

- 哪些是延遲提交
- 哪些是 scanline event
- 哪些是 sprite / OAM 邏輯
- 哪些是 half-step 才完成的 commit

也就是說，它不是簡化 timing，而是把 timing 的複雜度**分層管理**。

### 3. ppu_dispatch.cs：用 dispatch table 把 dot specialization 真的落地

`ppu_dispatch.cs` 是最效能導向的一層。

它的核心做法是：

- 先分成 `visible` / `pre-render` / `vblank` 三張表
- `visible` 再依 dot range 切成：
  - `PixelZone`
  - `VisibleTail`
  - `SpriteFetch`
  - `Prefetch`
  - `Dummy`

這個設計的目的，不只是好看，而是：

- 讓 `0..255` 的像素熱區可以最大幅度去掉不會成立的判斷
- 讓 `256/257/340` 尾端只保留真正需要的 scroll / wrap / delayed draw
- 讓 `258..319`、`320..335`、`336..339` 這些區域不用再背像素合成的負擔

#### PixelZone 為什麼特別重要

`Ppu_Tick_Visible_PixelZone()` 是 PPU 最熱的地方之一。

它的特徵是：

- 不把 body 抽成太多 helper
- 保持很多邏輯 inline
- 對該 range 不可能成立的條件直接刪掉

例如在這個區段內：

- 不需要 scroll 尾端處理
- 不需要 scanline wrap
- 不需要 VBlank event
- tile fetch / pixel gate / sprite shift gate 可以大幅簡化

這就是 **slot-aware specialization**：  
不是在 runtime 問「現在是不是某種 dot」，而是直接讓這個 handler 只服務那種 dot。

#### 非像素路徑則共用骨架，但不去污染 PixelZone

另一個關鍵判斷是：

- `SpriteFetch`
- `Prefetch`
- `Dummy`
- `VisibleTail`

會共用一些骨架 helper，例如：

- `PpuVisibleAuxBeforePhase4()`
- `PpuDotAuxBeforeStep1Core()`
- `PpuDotAuxStep1()`
- `PpuDotAuxAfterPhase4()`

但 `PixelZone` 自己保留 inline，避免最熱路徑被 generic helper 形狀拖累。  
這是一個很典型、也很成熟的效能工程決策：

- 冷一些的路徑可共用
- 最熱的路徑維持特化

#### PreRender / VBlank 也不是完全 generic

`Ppu_Tick_PreRenderLine()` 與 `Ppu_Tick_VBlankLine()` 雖然比 visible pixel 冷得多，但仍保留了各自該有的行為邊界：

- pre-render 仍有特殊 scroll reset、odd-frame skip、BG fetch、sprite shifter
- vblank 則只保留該區段真正會發生的 universal per-dot state update 與 frame render 觸發

這讓 PPU 不需要靠大量 catch-up 把「後面應該發生的事情」一次補回來，而是盡量在正確的 dot 類別上直接執行。

## 其他核心檔案裡的架構亮點

除了 `Main.cs` 與 PPU 三層結構外，根目錄其他核心 `.cs` 檔與 `Mapper/Mapper004.cs` 也延續同一個方向：保留細粒度 timing，但把熱路徑整理成更穩定的形狀。

### CPU.cs：cycle-level CPU，不讓 instruction-level catch-up 接管時序

`CPU.cs` 不是「一個 opcode 一次跑完」的 instruction-level 模型，而是用 `operationCycle` 保存 6502 指令內部狀態，由 master clock 在 CPU gate 每次推進一個 CPU cycle。

這讓 CPU 能和 DMA、NMI、IRQ、PPU register side effect 對齊在 cycle 層級，而不是事後補帳。  
同時它又用 256-entry function-pointer opcode table 降低 dispatch 成本：

- `.NET 10` 路徑使用 `delegate* unmanaged<void>` 搭配 `UnmanagedCallersOnly`
- opcode table 放在 unmanaged memory，避免一般 managed delegate array 的形狀
- `InitOpHandlers()` 用 `stackalloc` 保留 16x16 opcode matrix，再一次 copy 進 native table

這是很典型的 AprNes 取向：**CPU timing 不放寬，但 opcode dispatch 形狀盡量壓平。**

### MEM.cs / IO.cs：匯流排與 DMA 不是旁路，而是 cycle model 的一部分

`MEM.cs` 的重點不是單純記憶體讀寫，而是把 CPU bus、OAM DMA、DMC DMA、open bus、controller bus conflict 都納入 per-cycle 模型。

比較值得注意的設計有：

- `DmaOneCycle()` 每次只執行一個 DMA cycle，和 master clock 的 CPU gate 對齊
- OAM DMA / DMC DMA 依 GET / PUT cycle 做不同優先權處理
- DMC implicit abort、phantom read、DMA halt 都保留在匯流排層
- `DmaFetch()` 模擬 `$4000-$401F` open bus 與 `$4015/$4016/$4017` bus conflict
- CPU memory dispatch 用 8-page table 取代 65536-entry table，讓常用 dispatch table 壓在一個 cache line 內

`IO.cs` 則把 PPU register mirror 正規化後，再分派到 PPU/APU/controller handler。  
這讓 CPU bus handler 保持「只做 bus 語意」，不把時間推進藏在讀寫函式之外；真正需要的固定推進則由 PPU register handler 透過 `nestedTick` 明確執行。

### APU.cs / JoyPad.cs：APU timing 與 controller timing 綁在同一個節奏

`APU.cs` 的 `apu_step()` 很明顯沿著 TriCNES 的 GET / PUT cycle 拆開：

- GET cycle 處理 pulse/noise timer、DMC clock、DMC cooldown、controller strobe reload
- PUT cycle 處理 frame interrupt clear 與 DMC load DMA countdown
- both cycle 處理 DMC `$4015` delay、triangle timer、frame counter

這裡有兩個架構亮點。  
第一，冷門但必要的事件透過 function pointer helper 拆出去，避免把少見分支直接撐大 `apu_step()`。  
第二，`apuRegister` 是連續 16-byte buffer，halt flags 用兩個 `ulong` load 做 SWAR 更新，減少每 cycle 多次分散讀取。

`JoyPad.cs` 也不是立即 shift 的簡化模型，而是把 controller read 後的 2-cycle deferred shift 放進 APU step 裡處理。  
UI / input thread 對按鍵狀態的更新則用 `Interlocked.Or/And` 或 `CompareExchange` fallback，讓輸入更新是 lock-free atomic，避免鍵盤與手把事件互相覆蓋。

### FDS.cs：FDS 不是普通 mapper，而是獨立硬體模式

`FDS.cs` 的設計亮點在於它沒有硬塞進一般 cartridge mapper 流程，而是作為 FDS mode 接管特定記憶體頁：

- `$4020-$40FF` 走 FDS register dispatcher
- `$6000-$DFFF` 走 32KB FDS PRG-RAM
- `$E000-$FFFF` 走 BIOS ROM
- FDS fast path 在 `Main.cs` 裡用 `fds_CpuCycle()` 取代 `MapperObj.CpuCycle()`

`fds_CpuCycle()` 每 CPU cycle 推進 disk I/O、IRQ timer、FDS audio。  
Disk 部分保留 head delay、byte delay、gap-inserted disk image、CRC 狀態與 disk IRQ；audio 部分則把 FDS wavetable / modulation / envelope 推到 expansion audio channel。

這讓 FDS 不是「靠 mapper read/write 事後補狀態」，而是在主時序裡有自己的 per-cycle state machine。

### Mapper004.cs：MMC3 A12 / IRQ 與 CHR bank pointer 的熱路徑設計

`Mapper/Mapper004.cs` 是一個很好的 mapper-side 例子。

MMC3 IRQ 不是單純看 CPU write，而是依 PPU A12 邊緣計數。這裡的設計是：

- `PpuClock()` 每 PPU dot 檢查 `ppuAddressBus` 的 A12
- 用 `m2Filter` 計算 A12 low 的持續時間
- threshold 設為 10，過濾 BG fetch 的短 gap 與 scanline 邊界短 gap
- 只讓 sprite fetch 區間形成有效 clock，對應 MMC3 scanline counter 的行為

CHR hot path 則把 mapper bank mode 的分支提前消化在 `UpdateCHRBanks()`：

- CHR mode 0/1 改變時，預先更新 `NesCore.chrBankPtrs[0..7]`
- `MapperR_CHR()` 在 CHR-ROM 情況下只需 `chrBankPtrs[(address >> 10) & 7][address & 0x3FF]`
- CHR-RAM 情況保持直接讀寫 `ppu_ram`，避免多餘 ROM bank 邏輯

這個設計把 mapper 的複雜性移到 bank 更新時處理，而不是讓 PPU tile fetch 熱路徑每次重新判斷 mapper 狀態。

## 這整套設計的真正含義

AprNes 最終走的，不是：

- 用比較鬆的 timing 模型
- 再靠大量 catch-up 修回相容性

而是：

- 直接接受高精度 timing 的成本
- 然後用結構設計把成本壓到可接受

所以它的優化哲學比較像：

- 減少熱路徑 branch
- 減少 generic scheduler 成本
- 分離熱 / 冷邏輯
- 控制 helper 對 JIT / inline / I-cache 的影響
- 讓固定時序走固定形狀

從工程角度看，這是比較「硬」的一條路。  
它比大量 catch-up 更難寫、更難 refactor，也更容易需要長期性能調整；  
但在高精度 timing 模型下，它通常比「放寬模型再補帳」更可控。

## 給讀者的總結

如果你是一般對資訊技術有興趣的人，可以把 AprNes 的選擇理解成：

> 與其先偷懶再補帳，不如把細緻模型本身整理成更容易跑的形狀。

如果你是 emulator developer，那這裡真正值得看的不是單一技巧，而是這個判斷：

- 當 timing 模型夠細時
- `catch-up` 的自由度會縮小
- `catch-up` 的成本與 correctness 風險會上升
- 這時候更高 CP 值的路，往往是回頭整理主迴圈與熱路徑結構

這也是為什麼 AprNes 最後只有極少部分使用 catch-up，而把大量努力投向：

- main loop 結構優化
- PPU dot specialization
- CPU / DMA / APU 的 cycle-level 狀態機
- mapper bank pointer 與 A12 timing 的熱路徑整理
- JIT / IL / I-cache 友善化

## 延伸閱讀

### 中文

- [AprNes 非 JIT 層優化技巧整理](https://github.com/erspicu/AprNes/blob/master/MD/jit/JIT_ICache_Tutorial.md)
- [C# JIT 與 I-Cache 優化教學](https://github.com/erspicu/AprNes/blob/master/MD/jit/AprNes_Optimization_Techniques.md)

### English

- [AprNes Non-JIT Optimisation Techniques](https://github.com/erspicu/AprNes/blob/master/MD/jit/AprNes_Optimization_Techniques_EN.md)
- [C# JIT and I-Cache Optimisation Tutorial](https://github.com/erspicu/AprNes/blob/master/MD/jit/JIT_ICache_Tutorial_EN.md)
