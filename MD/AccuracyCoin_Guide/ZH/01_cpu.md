# 第 1 部：CPU 行為（Page 1 / 2–11 / 12 / 20）

> 對應 page：**P1 CPU Behavior**、**P2–P11 Unofficial Opcodes**、**P12 CPU Interrupts**、**P20 CPU Behavior 2**。
> 骨架：① 測試考什麼 → ② 硬體真實行為 → ③ 我們踩的坑 → ④ 怎麼修（含 code）→ ⑤ commit/file:line。
> 前置：先讀 [`00_timing_model.md`](00_timing_model.md)（per-cycle / master-clock 是這一切的前提）。

CPU 頁多半是「邏輯題」—— 不太需要 PPU 那種 dot 精度，但**極度要求 cycle 內的 bus 與中斷取樣時機**。能過 CPU 頁，你的 CPU core 大概就站穩了。

---

## 1. Open Bus（資料匯流排模型）—— 最該先搞定的觀念

**測試（P1 Open Bus, codes 1–9）**：
- `1: 讀 open bus 不應全為 0`
- `2: LDA Absolute 讀 open bus 應回傳 operand 高位元組`
- `6: 讀控制器的上 3 bit 應為 open bus`
- `9: $4015 bit5 應為 open bus`…

**硬體行為**：NES 的資料匯流排是「**最後一次有人驅動上去的值**」。沒有裝置驅動時（讀未對應位址、暫存器的未實作位元），讀到的就是這條 latch 的殘值 —— 這就是 open bus。

**我們的實作**：用一個 `cpubus` latch（`PPU.cs:1032`），每次 CPU read/write 更新它；讀 open bus 區（如 `$4020–$5FFF`、`$4016/$4017` 上 3 bit）就回傳 `cpubus`。

```csharp
// JoyPad.cs：讀控制器，下 1 bit 來自 shift register，上 3 bit 是 open bus
return (byte)((P1_ShiftRegister >> 7) | (cpubus & 0xE0));
```

**踩過的坑**（[BUGFIX29](../../bugfix/2026-03-04_BUGFIX29.md)）：
- `$4020–$5FFF` 原本路由到 mapper 的 `ExpansionROM`（回傳 0）→ open bus code 1 FAIL。改成整段回傳 `cpubus`。
- ZP read/write 當初沒更新 `cpubus` → code 4 FAIL。補上。

> **進階**：open bus 其實有「**internal vs external** 兩條」。`$4015` bit5 的來源跟一般 open bus 不同 —— CPU 讀 `$4015` 走 internal、DMA fetch 走 external。這是最新的坑，獨立成案：[dual data-bus](../../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md)。Open bus 是 CPU 頁的暗線，從第一頁纏到最後一頁。

---

## 2. Dummy read / write cycles

6502 在某些定址模式會做「**多餘的 bus 存取**」—— 它不是無害的空轉，因為那次存取**會更新資料匯流排**，而且可能打到有副作用的位址（如 `$2002`、`$4015`）。

**測試（P1 Dummy read / Dummy write、P20 Branch/Implied Dummy Reads）**重點：
- 索引定址跨頁時的 dummy read（在錯誤高位址讀一次）。
- read-modify-write 指令對 `$2006` 寫兩次（第一次寫舊值、第二次寫新值）。
- taken branch 的額外 dummy read。

**踩過的坑**（BUGFIX29）：分支被採取時的 dummy read 當初只是空 `tick()`、沒真的讀記憶體 → 資料匯流排沒更新 → P20 Branch Dummy Reads FAIL 4/5。修法是把空 tick 換成**真的 read**：
- taken、無跨頁：dummy read 自 `PC+2`。
- 跨頁：dummy read 自「錯頁位址」`(dest_hi 錯成 PC 高位) | dest_lo`。

> 現行 per-cycle 模型下，這些 dummy 存取就是指令 cycle 序列裡的一個 `CpuRead`，自然會更新 `cpubus` —— 不必特別處理「要不要更新 bus」，因為**每個 bus cycle 都是真的存取**。這就是換 per-cycle 模型的紅利（見 [timing model](00_timing_model.md) §2）。

---

## 3. Decimal flag / B flag（容易過、但要懂 quirk）

- **Decimal flag（P1）**：NES 的 2A03 把 6502 的 BCD 拔掉了，`ADC`/`SBC` **不受 D flag 影響**。但 `D` flag 本身仍存在、仍會被 `PHP`/`BRK` 推進堆疊。所以實作上：ADC/SBC 完全忽略 D，但 PHP/BRK 照推 P 暫存器（含 D bit）。
- **B flag（P1）**：6502 沒有真正的「B 暫存器」—— bit 4/5 只在**推進堆疊時**依來源決定：
  - `PHP` / `BRK` 推的 P：bit 4 = 1、bit 5 = 1。
  - `IRQ` / `NMI` 推的 P：bit 4 = 0、bit 5 = 1。
  測試逐一驗這 9 種組合（codes 1–9）。實作就是在推 P 到 stack 時，依「這是 PHP/BRK 還是中斷」決定 bit 4。

這兩題不難，但它們在教你一件事：**P 暫存器的某些 bit 是「推堆疊當下才合成」的，不是真的存在暫存器裡。**

---

## 4. Unofficial opcodes（P2–P11）

整批未官方 opcode 都要實作對 —— 大多數（`NOP` 各定址、`LAX`/`SAX`/`DCP`/`ISC`/`SLO`/`RLA`/`SRE`/`RRA`）只是官方指令的組合，照 cycle 數與 dummy read 補齊即可。

**真正難的是 SH\* 家族**（`SHA $93/$9F`、`SHX $9E`、`SHY $9C`、`SHS $9B`）。

**硬體行為**：SH* 寫入的值是 `暫存器 & (位址高位元組 + 1)`。但有個惡名昭彰的 quirk：**當 DMA/中斷打斷在 write cycle 之前時，那個 `& (H+1)` 的 high-byte masking 會被「取消」**（變成 `& 0xFF`）。

**怎麼修**（[BUGFIX51](../../bugfix/2026-03-10_BUGFIX51_SH_opcodes.md)，commit `3a3d728`，AC 126→131 +5）：偵測 SH* 在關鍵 cycle 是否被 DMA 插入，設一個 `ignoreH` flag；為真時 write 用 `H = 0xFF`：

```csharp
// DMA 插入點偵測 SH* 的關鍵 cycle（參考 TriCNES IgnoreH）
if ((opcode == 0x93 && operationCycle == 4) ||
    (opcode == 0x9B && operationCycle == 3) ||
    (opcode == 0x9C && operationCycle == 3) ||
    (opcode == 0x9E && operationCycle == 3) ||
    (opcode == 0x9F && operationCycle == 3))
{
    ignoreH = true;   // SH* write 改用 H = 0xFF，消除 high-byte masking
}
```

> 這題是「per-cycle 模型」的試金石：要知道 DMA **精準插在指令的哪個 cycle**，才能決定 ignoreH。指令級模型根本表達不出來 —— 這也是當初非換模型不可的原因之一。`ignoreH` 在 hard reset 時清零（`Main.cs:285`）。

---

## 5. 中斷時序（P12）—— penultimate-cycle 取樣 + 中斷序列不 poll NMI

這是 CPU 頁最精緻的部分。三個關鍵硬體事實：

1. **IRQ/NMI 在指令的「倒數第二個 cycle」取樣**（penultimate-cycle polling）。也就是說，指令最後一個 cycle 之前的 line 狀態才算數。
2. **中斷序列（BRK/IRQ/NMI 的 7 個 cycle）本身不做 NMI polling** —— NMI 要等到 handler 的第一條指令才可能觸發。否則會出現「NMI 搶在 IRQ handler 第一條指令（SEC）之前」的錯誤。
3. **NMI 是邊緣觸發 + 1-cycle 延遲**（見 [timing model](00_timing_model.md) §4）；IRQ 是電平觸發。

**踩過的坑**（[BUGFIX18](../../bugfix/2026-02-22_BUGFIX18.md)，165→169）：早期用 `irqLineAtFetch` 在 opcode fetch 後取樣，對 2-cycle 指令對、但對 3-cycle JMP 與 OAM DMA（500+ cycle）都偏早 → IRQ 在錯的指令觸發。

**現行做法（master-clock 模型）**：line 狀態在 master clock 的精確位置取樣，自然涵蓋所有指令長度 —— **NMI 在 MC 4、IRQ 在 MC 7** 取樣（`Main.cs`）：

```csharp
// MasterClockTickUnrolledNTSC：一個 CPU cycle = 12 master clock
mcCpuClock = 8; mcPpuClock = 0;
NMILine |= NMIable && isVblank;                       // ← MC 4：NMI 取樣
if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
...
mcCpuClock = 5;
IRQLine = irqLineCurrent;                              // ← MC 7：IRQ 取樣
if (statusframeint && !apuintflag) irqLineCurrent = true;
```

P12 還有 **NMI Overlap BRK / IRQ**（中斷序列中又來一個中斷的搶占行為，俗稱 interrupt hijacking）與 **Interrupt flag latency**（`SEI`/`CLI`/`PLP` 改 I flag 的延遲一個 cycle 生效）。這些全靠「精準到 cycle 的 line 取樣 + 不在中斷序列裡 poll NMI」才過得了。

---

## 6. P20 CPU Behavior 2（綜合）

P20 把前面的觀念綜合驗收：Instruction Timing、Implied Dummy Reads、Branch Dummy Reads（§2）、JSR Edge Cases、**Internal Data Bus**（§1 的進階，[dual data-bus](../../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md)）。能過 P20，CPU core 基本畢業。

---

## 小結

CPU 頁的核心其實就兩條暗線：
1. **資料匯流排**（open bus / dummy 存取更新 bus / internal vs external）—— 從 P1 纏到 P20。
2. **cycle 內的取樣時機**（penultimate IRQ、NMI 1-cycle delay、中斷序列不 poll、ignoreH 看 DMA 插哪 cycle）。

兩條都要求「**指令內部可定位到 cycle / master clock**」。所以 CPU 頁真正的門檻不是某個 opcode，而是 [timing model](00_timing_model.md) 對不對。

下一篇：[`02_dma.md`](02_dma.md)（DMA：OAM/DMC 時序、bus conflict、abort）。
