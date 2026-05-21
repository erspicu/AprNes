# Timing 模型：為什麼非得做到 sub-cycle，以及 AprNes 的 master-clock 架構

> 對應：所有 page 的底層前提。這篇講「地基」—— 沒有對的 timing 模型，後面任何 PPU/APU/DMA 修復都是在錯的基礎上疊補償。

---

## 1. 為什麼 frame/scanline 級過不了 AC

一般模擬器分這幾級精度：

| 精度級 | 一次推進 | 能跑商業遊戲？ | 能過 AC？ |
|--------|----------|----------------|-----------|
| frame 級 | 一整張畫面 | 大多可以 | ❌ |
| scanline 級 | 一條掃描線 | 幾乎都可以 | ❌ |
| **cycle / dot 級** | 1 CPU cycle / 1 PPU dot | 可以 | 大致可以 |
| **sub-cycle / master-clock 級** | 1 master clock（CPU cycle 的 1/12）| 可以 | ✅ 必須 |

AccuracyCoin 大量測試在驗「同一個 CPU cycle 內，PPU 走到第幾個 dot 時某 flag 才變」這種事 —— 例如 `$2002` VBL flag 的 set/clear 時機、NMI 邊緣、sprite 0 hit 的精準 dot。這些**在 CPU cycle 邊界才結算的模型上根本表達不出來**，必須把 CPU cycle 再切細。

---

## 2. AprNes 的演進（三代 timing 模型）

這段是[修復編年史](00_fix_history.md)的濃縮，但從「模型」角度看：

1. **第一代：tick-on-access**（早期）。每次 `Mem_r`/`Mem_w` 呼叫 `tick()`，推進 3 PPU dots + 1 APU cycle。簡單、夠快，blargg 衝到 174 沒問題。但 **DMA 只能在指令邊界插入** → DMC stolen cycle 時序不準，AC 卡在 122/136。
2. **第二代：per-cycle CPU**（[BUGFIX50](../../bugfix/2026-03-10_BUGFIX50_per_cycle_CPU.md)）。CPU 改成「每 cycle 獨立步進」（`cpu_step_one_cycle()`），DMA 能在**任意 read cycle 邊界**插入。AC 122→136 滿分（v1）。
3. **第三代：per-master-clock**（移植 TriCNES，現行）。連 CPU cycle 內部都切成 12 個 master clock，PPU 在 cycle 內多個 sub-點推進。這才能對齊 `$2002`/`$2005`/`$2001` 那些 sub-cycle flag 行為，AC 推到 v2 138 / 20260521 的 139。

> **核心教訓**：每次卡關的根因都是「模型粒度不夠細」，而不是「某個值算錯」。換對模型 > 打補丁。

---

## 3. 現行 master-clock 模型怎麼跑

NTSC 的時脈關係：**master clock 21.477 MHz**，CPU = master / 12，PPU = master / 4。所以 **1 CPU cycle = 12 master clock = 3 PPU dots**。

主迴圈 `MasterClockTickUnrolledNTSC()`（`Main.cs:712`）把這 12 個 master clock 攤平展開，事件落在精確的 MC 位置：

```
MC 0 :  CPU gate（cpu_step_one_cycle 或 DmaOneCycle）+ MapperObj.CpuCycle()
        apu_step() + APU put/get 翻面
        ppu_step_new()        ← PPU dot #1（整步）
MC 2 :  ppu_half_step_new()   ← 半步（sub-dot 精度）
MC 4 :  NMI line 取樣 + ppu_step_new()  ← PPU dot #2
MC 6 :  ppu_half_step_new()   ← 半步
MC 7 :  IRQ line 取樣 + MapperObj.CpuClockRise()
MC 8 :  ppu_step_new()        ← PPU dot #3
MC 10:  ppu_half_step_new()   ← 半步
```

幾個關鍵設計：
- **CPU 在 MC 0 跑一個 cycle**，但它對 bus 的影響、與 PPU 的相對位置，靠後面 MC 2/4/6/8/10 的 PPU (半)步精準對齊。
- **NMI 在 MC 4 取樣、IRQ 在 MC 7 取樣**（`Main.cs:736, 745`）—— 中斷的「倒數第二 cycle 取樣」這類行為就是靠這種 sub-cycle 位置表達。
- **half-step**（MC 2/6/10）讓需要「半個 dot」精度的 flag（如 `$2002` 的 VBL/sprite 0/overflow 在 dot 內 staggered 清除）有地方落點。
- **DMA gate**：`if (cpuIsRead & (isDmcActive | spriteDmaTransfer)) DmaOneCycle();`（`Main.cs:717`）—— DMA 只在 CPU 處於 read cycle 時偷一個 cycle，精準對齊 GET/PUT。

> CPU 本身是 `cpu_step_one_cycle()`（`CPU.cs:593`）逐 cycle 步進，bus 存取走 `CpuRead`/`CpuWrite`（`CPU.cs:77+`），不再是老的 `Mem_r/tick`。

---

## 4. 兩個一定要懂的子模型

### VBL/NMI 1-cycle delay
NMI 不是 flag 一變就觸發，而是：**rising edge → 設 `nmi_delay` → 下一個 tick 升級成 `nmi_pending` → CPU 檢查 `nmi_pending`**。`$2002` 讀取會清 `nmi_delay`（可取消）但不清 `nmi_pending`（不可逆）。這個 1-cycle 延遲模型是過 NMI control/timing/suppression 整批測試的門票（早在 blargg 階段就靠它從 139→154，見編年史 Phase 0）。

### address bus vs data bus（以及 internal vs external）
NES 的匯流排行為是 AC 後段的重災區：
- **address bus**：CPU 當前定址的位址（PC 或存取目標）。
- **data bus / open bus**：最後一次在 bus 上的位元組；沒有裝置驅動時讀到的就是它（open bus）。
- 更細的：**internal data bus vs external data bus** —— `$4015` bit5 的 open bus 來源（CPU 讀走 internal、DMA 讀走 external），這是最新 [dual data-bus 修復](../../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md) 的主題。

這些都要在對的 timing 點更新對的 bus latch，模型不夠細就分不開。

---

## 5. catch-up vs global tick（取捨）

我們走的是 **global tick（master clock 統一驅動所有子系統）**，不是 catch-up（各子系統各自記時、需要時再追上）。

- **global tick**：每個 master clock 推進所有東西，子系統間永遠同步 → 精度最高、最好推理，但每 tick 都要碰所有子系統，較吃效能。
- **catch-up**：PPU/APU 平常不動，CPU 要讀它們時才「追上」到當前時間 → 省效能，但跨子系統的精準互動（DMA、bus conflict、flag stagger）很難寫對。

AC 滿分需要的精準互動太多，catch-up 的心智負擔會爆炸，所以我們選 global tick，再用 .NET 10 的 JIT/SIMD/PGO 把效能吃回來。

> 想深入 timing 分級、catch-up 概念，見 [`MD/techbook/`](../../techbook/) 既有長文（NES Emulator Timing Models、Catch-Up Concept、AprNes Catch-Up and Structural Optimization）。

---

下一篇開始進子系統：[`01_cpu.md`](01_cpu.md)（CPU：open bus / dummy cycles / unofficial opcodes / 中斷時序）。
