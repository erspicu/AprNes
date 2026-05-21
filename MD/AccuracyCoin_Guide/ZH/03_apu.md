# 第 3 部：APU（Page 14）

> 對應 page：**P14 APU Registers and DMA Tests** 的 APU 半邊 —— Length Counter、Length Table、Frame Counter IRQ、Frame Counter 4-step、Frame Counter 5-step、Delta Modulation Channel、APU Register Activation、Controller Strobing、Controller Clocking。
> （同頁的 DMA 半邊在 [`02_dma.md`](02_dma.md)。）
> 前置：[`00_timing_model.md`](00_timing_model.md)（APU step、get/put cycle）。

APU 頁的暗線是 **deferred（延遲生效）+ parity（get/put 奇偶）**。很多 APU 行為不是「寫下去就立刻生效」，而是延遲到下一個 APU **get cycle** 才結算 —— 這跟 CPU 頁的 cycle 取樣、DMA 頁的 parity 對齊是同一套世界觀。

---

## 1. Length Counter / Length Table（暖身）

**測試**：length counter 倒數歸零會停聲道；`$4015` 可讀各聲道 length counter 是否 > 0；length table 是 32 個 entry 的查表（`$4003` 等的高 5 bit 索引）。

重點 quirk：
- **halt flag**：length counter 的 halt（`$4000` bit5 等）會凍結倒數。
- **reload timing**：寫 length（`$4003`/`$4007`/`$400B`/`$400F` 高位）會在**下一個 half-frame** reload，不是立刻。
- **enable=0 立即歸零**：`$4015` 對應 bit 寫 0，該聲道 length counter 立刻清 0。

這幾項邏輯清楚、不太吃 cycle 精度，是 P14 的暖身。

---

## 2. Frame Counter（4-step / 5-step / Frame IRQ）—— deferred clear 的代表

frame counter 是 APU 的節拍器：4-step 模式每幀產生 4 個事件（envelope/linear counter 的 quarter-frame、length/sweep 的 half-frame），最後一步在 4-step 模式會拉 **frame interrupt**；5-step 模式多一步、不拉 IRQ。

**最精緻的是 Frame Counter IRQ 的清除時機**（[BUGFIX37](../../bugfix/2026-03-07_BUGFIX37.md)）。AccuracyCoin 這項有 **24 個 sub-test**，逼出兩個硬體事實：

1. **讀 `$4015` 清 frame IRQ flag 是「延遲」的** —— 不是讀的當下清，而是延到**下一個 APU get cycle** 才生效。
   - 我們的做法：`apu_r_4015()` 只設一個 pending flag（現行命名 `clearingFrameInterrupt`），真正的 clear 在 `apu_step()` 的 get cycle（`(cpuCycleCount & 1) == 0`）執行，且排在 frame counter assertion 之前。
   - 為什麼重要？測試用 `SLO abs,X` 對 `$4015` 做「dummy read + real read」兩次存取，依 get/put parity 決定 IRQ flag 在第二次 read 前**有沒有**被清掉 —— 立即清就會答錯。

2. **`apuintflag`（IRQ inhibit）為真時，frame IRQ flag 仍會被「無條件 set 2 個 cycle」，只在第 3 個 cycle 才被 suppress**。
   - 原本我們用 `!apuintflag` 門控「要不要 set」→ 錯。改成：**無條件 set**，最後一個 cycle 若 `apuintflag` 為真才 clear；IRQ line 才額外判 `!apuintflag`。

> 教訓：APU flag 常常是「先無條件發生，再延遲修正」。把「inhibit」理解成「不發生」是錯的 —— 它是「發生了、但稍後撤回」。這種 deferred 行為沒有 cycle 級模型根本測不出來。

---

## 3. Delta Modulation Channel（DMC）—— enable delay always set

DMC 播 DPCM 取樣，靠 DMA 抓 sample（DMA 細節見 [`02_dma.md`](02_dma.md)）。P14 的 DMC 測試裡有個經典坑（[BUGFIX49](../../bugfix/2026-03-08_BUGFIX49_DMC_enable_delay.md)，AC 121→122）：

**`$4015` 重新 enable DMC 時，transfer start delay 必須「無條件」設定**，不能只在 buffer 已空時才設。

```
$4015 write enable DMC → restartdmc() 設 dmcsamplesleft
   buffer 還沒空（shift register 還在消化上一個 byte）
   ✗ 舊碼：沒設 countdown → 下個 cycle buffer 空 → DMA 立刻 fire（太早）
   ✓ 修法：無條件設 countdown → DMA 延到 countdown 到期才 fire（正確）
```

對應參考是 Mesen2 的 `SetEnabled(true)` 永遠設 `_transferStartDelay`。測試 M/N 專門驗「`$4015` 在 DMC timer fire 前 1 或 0 個 cycle 寫入」這個邊界。

> 跟 §2 同樣的母題：**寫暫存器 → 延遲 N cycle 生效**，而不是立刻。差別只在延遲幾個 cycle、以及那段延遲撞到 timer fire 時怎麼收尾。

---

## 4. APU Register Activation —— DMA 讀 APU 暫存器

這項在 [`02_dma.md`](02_dma.md) §3 也提過，因為它橫跨 DMA 與 APU。重點：當 **CPU 位址匯流排落在 `$4000–$401F`** 時，OAM DMA 從 page `$40` 讀取會讀到 **APU 內部暫存器**，而且每 `$20` bytes 重複映射（`$4036 → $4016` 等）。

兩個我們踩過的坑（[BUGFIX46](../../bugfix/2026-03-08_BUGFIX46.md)，118→119；以及最近的 dual-bus 回歸）：
1. `IO_read()` 當初缺 `$4017`（controller 2）的 case → 回傳 `cpubus` 而非 controller 資料。未接 controller 2 時應 D0–D4=0、D5–D7=open bus。
2. DMA 讀 `$4015` 的 bit5 open bus 走 **external** bus（不是 internal）—— 見 [dual data-bus](../../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md)，這是 error code 7「bus conflicts not properly emulated」的根因。

> 預期的 OAM 內容（README 有附）：`... 44 41 40 ...` —— `$44` 是讀 `$4015` 得到的 frame IRQ flag + triangle length，其中 bit5=0 來自 DMA 的 `$40` external bus。能印出這串，APU + DMA + bus 三者才算對齊。

---

## 5. Controller Strobing / Clocking —— 又是 parity

控制器（`$4016`/`$4017`）的 strobe 與 shift 時機也吃 get/put parity（[BUGFIX39](../../bugfix/2026-03-07_BUGFIX39.md)）：

- **Controller Strobing**：寫 `$4016` bit0=1 strobe 控制器；但 strobe 的生效跟 CPU 的 get→put cycle 轉換有關（deferred `$4016` write）。測試驗：值 `$02` 不該 strobe（只看 bit0）、bit0 set 的任意值都該 strobe、get→put 轉換時才 strobe。
- **Controller Clocking**：連續兩個 cycle 讀同一個 `$4016`/`$4017`，shift register **不會** shift（硬體上 strobe/clock 的邊緣行為）。我們用 `P1_ShiftCounter = 2`（讀後設 2，APU put cycle 才遞減）模擬「兩次連讀不 shift」。

```csharp
// MEM.cs DmaFetch / IO read：讀 $4016
ctrlData = (byte)(((P1_ShiftRegister & 0x80) != 0 ? 1 : 0) | (val & 0xE0));
P1_ShiftCounter = 2;   // 連讀 2 cycle 不 shift
```

---

## 小結

APU 頁的母題就一句：**「寫暫存器/讀狀態」很少立刻生效，多半延遲到下一個 APU get cycle，且行為依 get/put parity 而變。**

- Frame IRQ：讀 `$4015` 延遲清、inhibit 是「發生後撤回」。
- DMC：enable 無條件設 transfer delay。
- Controller：strobe/shift 看 get→put 轉換與連讀。

這些跟 [DMA 頁](02_dma.md) 的 parity 對齊、[CPU 頁](01_cpu.md) 的 cycle 取樣是同一套底層模型 —— 再次印證：**地基（master-clock + get/put parity）對了，APU 這些 deferred 行為才寫得出來。**

下一篇：[`04_ppu.md`](04_ppu.md)（PPU：VBlank/NMI、read buffer、palette、sprite eval、sprite 0 hit、OAM corruption、shift registers）。
