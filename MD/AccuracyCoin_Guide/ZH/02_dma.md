# 第 2 部：DMA（Page 13）

> 對應 page：**P13 APU Registers and DMA Tests** —— DMA + Open Bus / $2002 / $2007 Read / $2007 Write / $4015 Read / $4016 Read、DMC DMA Bus Conflicts、DMC DMA + OAM DMA、Explicit DMA Abort、Implicit DMA Abort。
> 前置：[`00_timing_model.md`](00_timing_model.md)（GET/PUT、master-clock）、[`01_cpu.md`](01_cpu.md)（open bus / 資料匯流排）。

DMA 是整個 AC 最吃「cycle 對齊」的一頁。NES 有兩種 DMA，都會**偷 CPU 的 cycle**，而測試專門驗「偷在哪個 cycle、那個 cycle 在 bus 上留下什麼、被打斷時怎麼收尾」。沒有 per-cycle/master-clock 模型，這頁一題都過不了。

---

## 1. 兩種 DMA

| | OAM DMA | DMC DMA |
|---|---------|---------|
| 觸發 | 寫 `$4014`（page）| DMC 取樣計時器 fire（播 DPCM 時自動）|
| 動作 | 把一整頁 256 bytes 複製到 OAM（經 `$2004`）| 抓 1 個 sample byte 餵 DMC shifter |
| 偷幾個 cycle | 513 或 514（看對齊）| 1~4（看對齊 + 是否撞 OAM DMA）|
| 我們的實作 | `OamDmaGet`/`OamDmaPut`（`MEM.cs:201/210`）| `DmcDmaGet`（`MEM.cs:231`）|

兩者都只在 **CPU 處於 read cycle** 時才能偷（write cycle 不能被 halt）。這個 gate 在主迴圈：

```csharp
// Main.cs MasterClockTickUnrolledNTSC — CPU gate
bool isDmcActive = dmcDmaRunning & (dmcStatusEnabled | dmcImplicitAbortActive);
if (cpuIsRead & (isDmcActive | spriteDmaTransfer)) DmaOneCycle();   // 偷一個 cycle
else cpu_step_one_cycle();                                          // 正常跑 CPU
```

---

## 2. GET / PUT cycle parity（對齊模型）

DMA 在 bus 上分 **GET（讀）** 與 **PUT（寫）** 兩種 cycle，交替進行。哪個 cycle 是 GET、哪個是 PUT，取決於 **CPU cycle 的奇偶（parity）**。對齊不對，整個 DMA 偷的 cycle 數、撞到的位址全錯。

我們用 `cpuCycleCount` 的奇偶判定 GET/PUT phase（[BUGFIX31/32](../../bugfix/2026-03-06_BUGFIX31.md)，171→174 blargg）。OAM DMA 還可能需要一個 **alignment cycle**（513→514）讓它對齊到 PUT。

> 重點：DMA 的「偷幾個 cycle」不是固定值，而是**看它在哪個 parity 啟動**。`OamDmaPut` 裡 `if (OAMDMA_Aligned)` 就是在處理這件事 —— 沒對齊時先 `DmaFetch(addressBus)` 燒一個 alignment cycle。

---

## 3. DMA + 暫存器讀（bus conflict）—— 這頁的核心

DMA 偷 cycle 時還是要對 bus 做存取（GET 是 read）。如果那次 read 剛好打到**有副作用的暫存器**（`$2002`/`$2007`/`$4015`/`$4016`），就會產生「bus 衝突」—— 暫存器的副作用照樣發生，回傳值也照 open bus 規則合成。

測試逐一驗：
- **DMA + $2002 Read**：DMA 讀到 `$2002` 會清 VBL flag、reset address latch（副作用照發）。
- **DMA + $2007 Read**：會推進 PPU address、更新 read buffer。
- **DMA + $4015 Read**：清 frame interrupt flag —— 而且 **bit5 open bus 來源是 external bus，不是 internal**（CPU 讀才走 internal）。這是 [dual data-bus](../../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md) 修復的另一半。
- **DMA + $4016 Read**：clock 控制器 shift register。

我們把這段做在 `DmaFetch`（`MEM.cs:125`）裡 —— DMA 的 read 不是繞過暫存器，而是真的走暫存器邏輯，只是 `$4015` 的 bit5 取 `cpubus`（external）而非 `internalBus`：

```csharp
// DmaFetch：DMA 讀 $4015 的 bus-conflict 路徑
if (reg == 0x15) {
    byte status = (byte)(val & 0x20);   // bit5 來自 EXTERNAL bus（DMA 自己的 bus 值）
    if (statusdmcint)   status |= 0x80;
    ...
    clearingFrameInterrupt = true;       // 副作用：清 frame IRQ flag 照發
    return status;
}
```

> ⚠️ 這正是最近一次回歸的來源：我們一度把這裡也改成 `internalBus`，結果 P14 APU Register Activation 掛了。**CPU 讀 $4015 → internal bus；DMA 讀 $4015 → external bus**，兩條路要分清楚。

---

## 4. DMC DMA cooldown

兩個 DMC DMA 之間有最小間隔，剛跑完 DMA 的下一兩個 cycle 不能馬上再跑（硬體上 RDY line 還沒放開）。我們用 `dmcDmaCooldown`（TriCNES 的 `CannotRunDMCDMARightNow`，[BUGFIX52](../../bugfix/2026-03-10_BUGFIX52_DMC_DMA_cooldown.md)，AC 131→132）。`DmcDmaGet` 跑完設 `dmcDmaCooldown = 2`。

---

## 5. Explicit DMA Abort（寫 $4015=$00）

**測試**：DMC DMA 進行中寫 `$4015 = $00`（disable DMC），DMA 該被「明確中止」。難點在 disable 的 **deferred status delay** 落在 timer fire 邊界附近時要延長，否則 timer fire（觸發新 DMA）和 disable（取消 DMA）會在同 cycle 打架。

**修法**（[BUGFIX55](../../bugfix/2026-03-13_BUGFIX55_Explicit_DMA_Abort.md)，AC 134→135）：
1. deferred status delay 改成 **parity-dependent**：
   ```csharp
   dmcStatusDelay = getCycle ? 4 : 3;   // 避免 timer fire 與 deferred status 同 cycle 衝突
   ```
2. explicit abort 偵測覆蓋 **2 個 cycle 的 fire window**（`dmctimer == dmcrate` 剛 fire、`dmctimer == 1` 下個 cycle 將 fire），而不只「剛 fire」那一個。

---

## 6. Implicit DMA Abort（寫 $4015=$10）—— 幽靈 DMA

**測試**：一個 **1-byte non-looping** 的 DMC sample 即將結束時，寫 `$4015 = $10`（enable），會觸發一個「幽靈」**1-cycle DMA**；這個幽靈 DMA 若撞到 write cycle 會被**完全取消**。

這是整頁最 exotic 的行為。**修法**（[BUGFIX56](../../bugfix/2026-03-14_BUGFIX56_Implicit_DMA_Abort.md)，AC 135→**136 PERFECT** 🎉）：

在 `$4015` write 時偵測 timer 接近 fire，設 `dmcImplicitAbortPending`；timer fire 時轉成 `dmcImplicitAbortActive`，觸發 1-cycle phantom DMA：

```csharp
// 偵測條件（對應 TriCNES timer==10/8，AprNes 有 +3 position offset）
//   dmctimer == 8 && !getCycle   (TriCNES timer==10 && !PutCycle)
//   dmctimer == 9 &&  getCycle   (TriCNES timer==8  &&  PutCycle)
```

而 phantom DMA 撞到 write cycle 時的取消，就在主迴圈 CPU gate 那行：

```csharp
// Main.cs：phantom DMA 遇 write cycle → 取消
if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive = false;
```

> **timer 值映射的坑**：TriCNES 的 DMC timer 每 GET cycle 遞減 **2**（恆為偶數），我們每 cycle 遞減 **1**，而且 pending→active 有 **+3 的 position offset**。直接照抄 TriCNES 的常數會錯 —— 必須先搞懂兩邊 timer 的遞減節奏，再換算。這是「移植參考實作」時最容易翻車的地方：**對齊語義，不是對齊數字**。

---

## 小結

DMA 頁能不能過，取決於三件事：
1. **GET/PUT parity 對齊**（偷幾個 cycle、撞哪個位址）。
2. **DMA 的 read 真的走暫存器副作用**（bus conflict），且 `$4015` bit5 走 external bus。
3. **abort 行為**（explicit：disable 延遲 parity-dependent；implicit：幽靈 1-cycle DMA 遇 write 取消）。

這三件都要求「**DMA 精準插在 CPU cycle 序列的特定 parity/位置**」。P13 最後兩項（explicit/implicit abort）正是當年衝上 **136/136 v1 滿分**的臨門兩腳。

下一篇：[`03_apu.md`](03_apu.md)（APU：length counter、frame counter IRQ、DMC、register activation、controller）。
