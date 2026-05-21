# AccuracyCoin Guide — 攻克 AccuracyCoin 全測試教學

> 目標讀者：想讓自己的 NES 模擬器通過 [AccuracyCoin](https://github.com/100thCoin/AccuracyCoin) 全部測試的人。
> 以 AprNes 實際攻克的過程為主線，把每一頁測試「考什麼硬體行為、為什麼難、怎麼實作對」講清楚。

---

## 這份指南是什麼

AccuracyCoin 是 NES 測試 ROM 裡最硬核的一份 —— 單一 NROM 卡帶塞進 **139 項**精度測試（`20260521` 版），涵蓋 CPU / PPU / APU / DMA / 匯流排 的 sub-cycle 級行為。很多測試在一般「frame 級」或「scanline 級」模擬器上根本過不了，必須做到 **dot 級 / master-clock 級**精度。

AprNes 從零到 **139/139 滿分**的過程踩過大量坑。這份指南把那些坑整理成「可教學的攻略」，而不是流水帳 bug log（那些在 [`MD/bugfix/`](../../bugfix/) 裡）。

**目前基線**：AccuracyCoin `20260521` = **139/139 PASS**（blargg 184/184 無回歸）。

---

## 先說清楚：攻克 AC 的代價

在你決定追 AC 滿分之前，這份指南要誠實地先講代價。AprNes 從開始到 139/139 大約付出了：

- **時間／人力**：跨度約 3 個月、**57+ 個獨立 bugfix**（`MD/bugfix/` 從 `BUGFIX1`（2026-02-19）到 dual data-bus（2026-05-22））。每一個幾乎都得先讀 NESdev wiki / 硬體文獻、再對照 TriCNES，才動手 —— 研究時間遠多於寫 code 的時間。
- **效能**：要過後段測試必須做到 **sub-cycle / per-master-clock** 精度，那是一個涵蓋 CPU/PPU/APU/DMA 每個子週期的有限狀態機，**很貴**。在 .NET Framework 4.8.1 上，開類比管線（Ultra NTSC + CRT）跑 6×/8× 解析度會掉到 60 FPS 以下 —— 這直接逼出整個 **.NET 10 遷移**（aprnesava），靠 TieredPGO / OSR 把開銷吃回來。
- **複雜度**：高精度 timing model 的程式可讀性遠低於一般模擬器；一個看似無關的改動可能牽動很多地方。
- **邊際效益遞減**：最後那幾 % 的測試在模擬「幾乎沒有商業遊戲會踩到」的冷門 edge case —— internal/external open bus、DMA explicit/implicit abort、stale sprite shift register、$4015 bit5 來源…。投入與「能多跑幾款遊戲」完全不成比例。
- **回歸風險（補償鏈）**：每修一個可能弄壞另一個。本專案鐵律「**禁止錯誤補償**」就是這樣來的 —— 絕不為了過測試而加 hack/調參數繞過正確行為，否則會形成互相衝突的補償鏈，最後無法收斂。（最近 dual data-bus 那次，P20 修好卻回歸 P14，就是活生生的例子。）

> **一句實話**：如果你的目標只是「跑商業遊戲」，**scanline 級**精度其實就夠了，投資報酬率高得多。AC 滿分是**研究級 / 硬體考古級**的目標 —— 為了理解真實硬體到 dot/cycle，而不是為了玩遊戲。想清楚再上路。

---

## 為何我們最後「直接更換」timing 模型

這是整段攻克過程最關鍵的轉折，值得單獨講，因為它能幫你避開我們走過的冤枉路。

**起點（粗模型）**：最初 CPU 是「指令級」執行 —— 一次把整條指令的所有 cycle 跑完，DMA 只能在**指令邊界**插入。PPU timing 也較粗。靠這個模型 + 增量補丁（`BUGFIX1`～`BUGFIX49`）能推進到一定程度，但很快撞牆：DMA stolen cycle 的時序對不準，一整類測試（DMC DMA、IFlagLatency…）怎麼調都過不了。

**轉折一 — per-cycle CPU 重寫**（`BUGFIX50`, 2026-03-10, commit `533d1d4`）：把 CPU 從「每指令一次跑完」改成「**每 cycle 獨立步進**」，DMA 因此能在任意 read cycle 邊界插入。AccuracyCoin 122 → 126。這是第一次「換模型而非打補丁」。

**轉折二 — 整套移植 TriCNES timing 模型**：即使後來衝到 136/136，**實機畫面**仍有 PPU 渲染不準（`scanline-a1`、`colorwin_ntsc.nes`）。追下去發現根因是 **PPU timing 模型精度不足**，而且在舊架構上「補不動」—— 再怎麼 patch 都是在錯的基礎上疊補償。於是決定**整套換掉**：移植 TriCNES 的 **per-master-clock 執行模型 + 精細 PPU 子週期狀態機**（等價重寫，非搬程式碼）。代價就是上一節說的效能問題，連帶逼出 .NET 10 遷移。

> **教訓（這份指南最想傳達的一點）**：到某個精度門檻之後，「在粗模型上繼續打補丁」的累積成本會**超過**「直接換成正確的 timing 模型」。早一點認清基礎不對、果斷重寫，比一路 patch-then-regress 省得多。我們是繞了一圈才學到 —— 你可以直接從正確的 timing 模型起步。
>
> 背景知識（timing model 分級、catch-up vs global tick）見 [`MD/techbook/`](../../techbook/) 既有長文。

---

## 章節安排（系統化，依子系統分，非依 git 時間）

> 刻意**不按 git 時間順序**（那樣太跳、太亂）。改成依**子系統**分部，每一部把相關測試的硬體行為一次講透。
> 每一篇章節都照同一個骨架走：**① 測試考什麼（對應 page / error code）→ ② 硬體真實行為 → ③ 我們踩的坑 → ④ 怎麼修（含關鍵 code 片段 + 註解）→ ⑤ 對照 commit / file:line**。
> 以下是預計收錄的大綱，實際檔案隨撰寫陸續補上。

### 第 0 部：方法論與全紀錄
- ✅ [`00_fix_history.md`](00_fix_history.md) — **修復編年史（2026-02 → 05）**：由前到後的完整 git 時間線，分 4 個 phase（基礎 cycle-accurate → AC 正面進攻 → 換 per-cycle CPU 模型 → PPU 對齊 TriCNES → dual data-bus），每筆含 commit、問題、修復、PASS 數字。**想看「整段怎麼走過來」先看這篇。**
- `00_methodology.md` — 怎麼跑 AC、怎麼讀 error code、debug menu、page-by-page headless 測試流程、如何用 TriCNES 當對照基準。（待寫）
- `00_timing_model.md` — 為什麼需要 per-master-clock / dot 級 timing model；catch-up vs global tick 的取捨（可連到 [`MD/techbook/`](../../techbook/) 既有長文）。（待寫）

### 第 1 部：CPU（Page 1, 10–12, 20）
- 開機狀態、RAM mirroring、PC wraparound、decimal flag、B flag。
- Dummy read / dummy write cycles、open bus。
- **內部 vs 外部資料匯流排**（`$4015` bit5 open bus）→ 已有完整案例：[dual data-bus fix](../../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md)。
- 全部 unofficial opcodes（NOP/SH*/ANC/ARR/ANE/LXA…）。
- Interrupt flag latency、NMI/IRQ overlap、per-cycle CPU 重寫。

### 第 2 部：DMA（Page 13）
- OAM DMA、DMC DMA 時序、DMA + open bus、DMA 命中各暫存器的 bus conflict。
- Explicit / Implicit DMA abort。
- DMC DMA 與 CPU read/write cycle 對齊（get/put cycle）。

### 第 3 部：APU（Page 14）
- Length counter / length table、frame counter 4-step/5-step、frame IRQ 時序。
- DMC、APU register activation（含 OAM DMA 讀 APU 暫存器的 bus 行為）。
- Controller strobing / clocking。

### 第 4 部：PPU（Page 16–19）
- VBlank 開始/結束時序、NMI control/timing/suppression、1-cycle delay model。
- PPU read buffer、palette RAM quirks、$2007 w/ rendering、rendering flag 行為。
- Sprite evaluation、sprite 0 hit、OAM corruption、shift register（stale BG/sprite）、$2004/$2007 stress。

### 附錄
- `appendix_error_code_index.md` — 各頁 error code 速查（對應 README 的官方說明 + 我們的修法）。
- `appendix_tricnes_reference.md` — 怎麼把 TriCNES 當 ground truth、它的已知錯誤（哪些測試 TriCNES 自己也不過）。

---

## 與其他目錄的關係

| 目錄 | 內容 | 與本指南的關係 |
|------|------|----------------|
| [`MD/bugfix/`](../../bugfix/) | 逐個 bug 的修復紀錄（含根因）| 本指南的「原始素材」；教學文會引用 |
| [`MD/notes/AccuracyCoin_TODO.md`](../../notes/AccuracyCoin_TODO.md) | 各頁通關狀態追蹤 | 進度表 |
| [`MD/notes/AccuracyCoin_20260521_diff_and_result.md`](../../notes/AccuracyCoin_20260521_diff_and_result.md) | ROM 版本差異 | 版本沿革 |
| [`MD/techbook/`](../../techbook/) | NES 模擬器通用教學長文 | timing model / catch-up 等背景知識 |
| `ref/TriCNES-main-*/` | AC 作者自己的模擬器（滿分基準）| 對照 ground truth |

---

## 撰寫原則

1. **以硬體行為為主軸**，不是「為了過測試而調參數」。每篇講清楚真實硬體怎麼運作，再講測試怎麼驗、我們怎麼實作。
2. **可獨立閱讀**：每篇開頭交代它對應哪一頁 / 哪些 error code。
3. **引用實際 commit 與 file:line**，讓讀者能跳到 AprNes 原始碼對照。
4. **中文為主**，技術術語保留英文。
