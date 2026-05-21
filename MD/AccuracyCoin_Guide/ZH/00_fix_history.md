# AccuracyCoin 修復編年史（2026-02 → 2026-05）

> 這是 AprNes 攻克 AccuracyCoin 的**完整時間線**，由前到後整理 git 紀錄與 `MD/bugfix/`。
> 教學章節是「依子系統」分的；這份是「依時間」的全紀錄，方便你看清整個演進的因果。
> 每個 commit 後面的 `PASS` 數字：早期是 **blargg**（174 ROM），3 月起標 **AC**（AccuracyCoin）。

## 三個測試版本紀元（先搞懂數字）

| 紀元 | 題數 | 達標時間 | 關鍵 |
|------|------|---------|------|
| AC v1 | 136 | 2026-03-14 達 **136/136** | per-cycle CPU + DMC/DMA 完整時序 |
| AC v2 | 138 | TriCNES PPU core 移植後 **138/138** | $2005/$2001 TriCNES model、SR latch |
| AC 20260521 | 139 | 2026-05-22 達 **139/139** | 內外資料匯流排拆分 |

---

## Phase 0 — 基礎建設與 cycle-accurate PPU（2026-02-19 ~ 02-22）

這一階段主要在把 **blargg 174** 衝上去，順便把 cycle-accurate 的地基打好。沒有精準的 PPU/CPU 時序，後面 AC 根本無從談起。

| commit | 日期 | 問題 → 修復 | blargg |
|--------|------|------------|--------|
| `24687f0` `be3f979` | 02-19 | PPU 從粗略改成 **cycle-accurate 渲染**（3-stage attribute pipeline），修 CHR bank 時序 | — |
| `a289801` `e5c7486` | 02-19 | NMI suppression、NMI edge trigger、sprite 0 hit 時序、VBL suppress、OAM read | — |
| `13ceb89` | 02-20 | **headless test runner**（`$6000` 協定）+ CPU dummy reads + APU open bus | 入門 |
| `7671455` | 02-21 | **PPU VBL/NMI 1-cycle delay model**（最關鍵的一跳）| 154 (+15) |
| `5461fe7` | 02-22 | sprite timing：per-pixel hit + cycle-accurate overflow + 硬體 bug（BUGFIX17）| 165 (+4) |
| `1dd9024` | 02-22 | CPU interrupt timing：**penultimate-cycle IRQ** + NMI deferral + DMA align（BUGFIX18）| 169 (+4) |
| `f3188b9` | 02-22 | **DMC DMA cycle stealing** + TestRunner CRC 偵測（BUGFIX19）| 171 (+2) |
| `7cfef01` | 02-22 | PPU `$2007` read cooldown（6-dot ignoreVramRead，BUGFIX20）| 172 (+1) |

詳見：[BUGFIX4](../../bugfix/2026-02-20_1823_BUGFIX4.md)、[BUGFIX13–20](../../bugfix/)。
> 重點觀念：**VBL/NMI 1-cycle delay model**（rising edge → `nmi_delay` → `nmi_pending`）是過 NMI 系列測試的門票，這時就打好了。

---

## Phase 1 — AccuracyCoin 正面進攻（2026-03-04 ~ 03-08）

blargg 接近滿分後轉向 AC。這一階段是「在既有（指令級 CPU）模型上能修多少修多少」，大量 PPU/OAM/APU 行為被一一補上，最後卡在 **122/136**。

| commit | 日期 | 問題 → 修復 | AC |
|--------|------|------------|-----|
| `7e4c1b2` | 03-04 | branch dummy reads、CPU/PPU/controller **open bus**（BUGFIX29）| — |
| `8a04051` `afe3e17` | 03-06 | **Load DMA parity-dependent countdown**（GET/PUT phase 用 cpuCycleCount 奇偶，BUGFIX31/32）| 174 blargg |
| `86743fe` | 03-06 | 加 **Master Clock 基礎建設**（為日後 sub-cycle timing 鋪路）| — |
| `24328e9` | 03-06 | controller strobe、OAMADDR reset、S0H rendering flags（BUGFIX33）| — |
| `ab42f68` | 03-07 | `$2007` rendering increment、`$2004` read/write during rendering（BUGFIX34）| — |
| `43d34a9` | 03-07 | arbitrary sprite zero、misaligned OAM（BUGFIX35）| — |
| `09599c1` | 03-07 | **OAM corruption** on rendering enable/disable（BUGFIX36）| — |
| `c9fd77e` | 03-07 | Frame Counter IRQ：deferred clear + unconditional flag set（BUGFIX37）| — |
| `3edef15` | 03-07 | **INC `$4014`**：defer OAM DMA 到下一個 read cycle（BUGFIX38）| — |
| `a35646b` | 03-07 | controller strobing put/get cycle parity（deferred `$4016` write，BUGFIX39）| — |
| `0acad44` | 03-07 | **stale BG shift registers** + deferred Load DMA model（BUGFIX40）| — |
| `56cfcd0` | 03-07 | `$2004` read during sprite evaluation 回傳 evaluation position（BUGFIX41）| — |
| `b789e95` | 03-07 | **suddenly resize sprite**：sprite size latch at dot 261（BUGFIX42）| — |
| `c5895d0` | 03-07 | rendering flag：rendering off 時 freeze BG shift registers（BUGFIX43）+ OAM DMA APU activation（BUGFIX44）| — |
| `da92e7e` | 03-07 | `$2002` flag clear timing stagger（M2 duty cycle，BUGFIX45）| — |
| `ce904d0` | 03-08 | P19 **Sprites On Scanline 0**：secondary OAM + per-dot sprite eval FSM（BUGFIX47）| — |
| `5a7d56f` | 03-08 | P19 **`$2004` Stress Test**：per-dot read accuracy（BUGFIX48）| — |
| `a991af3` | 03-08 | **里程碑：174/174 blargg + 122/136 AC**（換模型前的最佳狀態）| **122** |

詳見：[BUGFIX29–49](../../bugfix/)。
> 重點觀念：很多 PPU 行為（OAM corruption、sprite eval、shift register freeze）都是 **per-dot** 才模擬得出來。這階段把「指令級 CPU」模型推到極限 —— 然後撞牆。

---

## Phase 2 — 換 timing 模型：per-cycle CPU（2026-03-09 ~ 03-14）⭐

**整段攻克的轉折點。** 指令級 CPU 讓 DMA 只能在指令邊界插入，DMC stolen cycle 時序對不準，一整類測試怎麼修都過不了。於是把 CPU 整個改成「每 cycle 獨立步進」，DMA 能在任意 read cycle 邊界插入。從此一路衝到 v1 滿分。

| commit | 日期 | 問題 → 修復 | AC |
|--------|------|------------|-----|
| `533d1d4` | 03-09 | **per-cycle CPU rewrite**：`cpu_step_one_cycle()`，每 cycle 獨立 `StartCpuCycle→bus→EndCpuCycle`，DMA 可任意插入（BUGFIX50）| **126** (+4) |
| `3a3d728` | 03-09 | **SH\* unofficial opcodes**（SHA/SHX/SHY/SHS 的 `&(H+1)` 與 page-cross 行為，BUGFIX51）| **131** (+5) |
| `5af6fdb` | 03-09 | **DMC DMA cooldown**（TriCNES `CannotRunDMCDMARightNow`，BUGFIX52）| **132** (+1) |
| `38368d9` | 03-13 | **DMC Load DMA countdown** timing（TriCNES-style，BUGFIX53）| **133** (+1) |
| `bb0f231` | 03-13 | **DMC DMA bus conflict** + deferred `$4015` status（BUGFIX54）| **134** (+1) |
| `7f83583` | 03-13 | P13 **Explicit DMA Abort**（BUGFIX55）| **135** (+1) |
| `f94fd51` | 03-14 | P13 **Implicit DMA Abort**（BUGFIX56）→ **136/136 PERFECT** 🎉 | **136** (+1) |

詳見：[BUGFIX50](../../bugfix/2026-03-10_BUGFIX50_per_cycle_CPU.md)、[BUGFIX51–56](../../bugfix/)。
> **教訓**：到這個精度，「在粗模型上打補丁」的成本已超過「直接換成 per-cycle 模型」。換完之後 122→136 只花了 5 天、7 個 commit —— 證明基礎對了，後面就快。

---

## Phase 3 — PPU 精修 + 全面對齊 TriCNES（2026-03-23 ~ 2026-04）

136/136 之後，**實機畫面**仍有 PPU 渲染瑕疵（`scanline-a1`、`colorwin_ntsc.nes`、Mega Man 5 垂直抖動）。根因是 PPU timing 精度不足，舊架構補不動 → 開始**逐項對齊 TriCNES 的 per-master-clock PPU 模型**，這條線最終把 AC 推到 v2 的 **138/138**。

| commit | 日期 | 問題 → 修復 |
|--------|------|------------|
| `c383e1b` | 03-23 | **`$2006` delayed t→v copy**：修垂直捲動抖動（Mega Man 5 等，BUGFIX57）|
| `898703a` | 03-23 | **read-time CIRAM mirroring**：切 mirror mode 時 nametable 不再壞 |
| `2bdb155`–`97633e2` | 03-24/25 | **MMC5** 完整重寫（PRG/CHR banking、scanline IRQ、pre-sprite-render CHR bank、extended attributes）|
| `14754fe` `7216705` | 04-02 | **PPU / 非-PPU timing 對照文件**（AprNes vs TriCNES）—— 系統性找差異 |
| `6d3ce08` | 04-02 | **`$2005` scroll write delay**（2 PPU dots，TriCNES model）|
| `93086bf` | 04-02 | **`$2001` four-tier flag system**（TriCNES model）|

詳見：[BUGFIX57](../../bugfix/2026-03-23_BUGFIX57_PPU2006_Delayed_Copy.md)、[CIRAM](../../bugfix/2026-03-23_CIRAM_ReadTime_Mirroring.md)、[MMC5](../../bugfix/2026-03-25_MMC5_PreSpriteRender_CHR_Fix.md)。
> 之後 `feature/tricnes-sync` 分支把 **SR latch pipeline + PPU core** 等價移植，cherry-pick 進 master，配合 AC ROM 升級到 v2（138 題）達成 **138/138**。

---

## Phase 4 — 最新進展（2026-05-22）

| commit | 日期 | 問題 → 修復 | AC |
|--------|------|------------|-----|
| `e354371` | 05-22 | AC 升級到 `20260521`（138→**139** 題，新增 P20 `Internal Data Bus`）；**內外資料匯流排拆分**（`internalBus` vs `cpubus`，`$4015` bit5 來源）| 139 |
| `11a16ad` | 05-22 | 回歸修正：**DMA 讀 `$4015` 走 external bus、CPU 走 internal bus**（修好 P20 卻回歸 P14 的活例）| **139/139** ✓ |

詳見：[dual data-bus](../../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md)。

---

## 總結曲線

```
blargg:  ~110 ──(cycle-accurate PPU + VBL 1-cycle delay)──▶ 172 ──▶ 174/174
                                                                  │
AC v1:        122 ──(per-cycle CPU 換模型)──▶ 126 ─31─32─33─34─35─▶ 136/136 PERFECT
                                                                  │
AC v2:        136 ──(TriCNES PPU core / $2005 / $2001 / SR latch)──▶ 138/138
                                                                  │
AC 20260521:  138 ──(internal/external data bus 拆分)──▶ 139/139
```

**兩條主線**：
1. **CPU/DMA 時序** —— 指令級 → per-cycle（BUGFIX50）→ DMC/DMA 完整時序（51–56）。
2. **PPU 精度** —— cycle-accurate 渲染 → per-dot FSM → 對齊 TriCNES per-master-clock。

兩條都印證同一個道理：**地基（timing 模型）對了，補洞才收斂；地基不對，補洞會無限回歸。**
