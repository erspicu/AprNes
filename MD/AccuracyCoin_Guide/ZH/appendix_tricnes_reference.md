# 附錄 B：把 TriCNES 當 ground truth（以及它的已知錯誤）

> AccuracyCoin 的作者 **100thCoin** 自己寫了模擬器 **TriCNES**，對自己的測試基本滿分。所以遇到「硬體到底該怎樣」的爭議，TriCNES 是最直接的對照基準。但它**不是 100% 正確**，有幾項它自己也不過 —— 知道哪些別信，比盲目照抄重要。

---

## 1. 為什麼用 TriCNES，而不是別的模擬器

優先序（本專案鐵律）：**硬體文獻（NESdev wiki）> 測試 ROM 期望 > TriCNES**。

- NESdev wiki 是最高權威，但有些 sub-cycle 行為 wiki 講不清。
- AC 測試 ROM 的期望值是「作者認定的正確」，但 ROM 只告訴你 pass/fail，不告訴你「中間哪個 cycle 算錯」。
- **TriCNES 是作者把那份期望「實作出來」的版本** —— 當你需要看「正確實作長怎樣、中間值該是多少」，trace TriCNES 最快。

> ⚠️ 我們**不參考 BeesNES**（AC 只有 96/136，表現差），Mesen2 可作 mapper 邏輯參考但不保證 100% 正確（原則上 bug fix 不再參考 Mesen2）。

---

## 2. 怎麼用

**路徑**：
- `ref/TriCNES-main-20260521/` —— 最新（本機 gitignored，不進 repo）
- `ref/TriCNES-main-20260410/` —— 前一版（可比 TriCNES 自己的演進）

**結構**：TriCNES 是 C# WinForms，核心全在**單一大檔 `Emulator.cs`**（CPU/PPU/APU/DMA 都在裡面，約 11000+ 行）。mapper 在 `mappers/`。

**用法**：
1. 把**同一張 ROM** 丟進 TriCNES 跑，比對行為 / 中間值。
2. trace `Emulator.cs` 對照我們的 NesCore 實作。常用對照點：
   - `Fetch(ushort)` —— CPU/DMA 共用的 read（含 `$4015`、controller、PPU register 的 bus 行為）。
   - `internalBus` / `dataBus` —— 內外資料匯流排。
   - DMC timer / `APU_PutCycle` / `CannotRunDMCDMARightNow` —— DMA parity 與 cooldown。
3. 新舊版 diff 看 [`MD/notes/TriCNES_20260521_vs_20260410_diff`](../../notes/TriCNES_20260521_vs_20260410_diff.md)（最近 dual-bus 的修法就是從這份 diff 找到的）。

---

## 3. ⚠️ 對齊「語義」，不是對齊「數字」

最容易翻車的一點：**TriCNES 的內部計數節奏跟我們不同，直接照抄它的常數會錯。**

實例（[BUGFIX56](../../bugfix/2026-03-14_BUGFIX56_Implicit_DMA_Abort.md)）：
- TriCNES 的 DMC timer **每 GET cycle 遞減 2**（值恆為偶數）。
- AprNes 的 DMC timer **每 cycle 遞減 1**。
- 而且 pending→active 轉換有 **+3 的 position offset**。

所以 TriCNES 寫 `timer == 10 && !PutCycle`，我們對應的條件是 `dmctimer == 8 && !getCycle` —— 不是 10。**先搞懂兩邊 timer 的遞減語義，再換算位置**，否則 magic number 直接搬一定錯。

> 這也是「禁止錯誤補償」的延伸：照抄數字而不懂語義，等於在猜，遲早回歸。

---

## 4. TriCNES 自己也不過的測試（別拿它當這些項目的真理）

以下幾項 TriCNES 本身就 fail 或行為有爭議 —— 遇到這些**不要**以 TriCNES 為準，回到 NESdev wiki / 實機 / 多方交叉驗證：

| 項目 | 說明 |
|------|------|
| `6-MMC3_alt` | MMC3 alternate 行為 |
| `6-MMC6` | MMC6 |
| `5-MMC3_rev_A` | MMC3 rev A 變體 |
| `read_write_2007` | `$2007` 讀寫的某些 edge case |
| `power_up_palette` | 開機 palette 初值 |

另外有一個**三方解讀分歧**的開放問題：**sprite X-counter 在 forced-blank 期間的行為** —— NESdev wiki / AC 測試 / TriCNES 三邊解讀不一致，仍待 AC 作者確認（見專案 notes）。這類「連 ground truth 都不確定」的題目，別硬湊。

---

## 5. TriCNES 的 mapper 涵蓋範圍（trace 前先確認）

TriCNES **只實作這幾個 mapper**：

```
0 (NROM), 1 (MMC1), 2 (UxROM), 3 (CNROM),
4 (MMC3/MMC6), 7 (AOROM), 9 (MMC2), 69 (FME-7)
```

**不在這清單上的 mapper，不要去 trace TriCNES**（它根本沒實作，trace 了也是白費）。AccuracyCoin 本身是 NROM（mapper 0），所以 AC 攻克過程用到的 TriCNES 對照集中在 mapper 0 + CPU/PPU/APU/DMA 核心。其他 mapper 的實作參考改用 Mesen2（`ref/Mesen2-master/Core/NES/Mappers/`），但要用我們自己的 `IMapper` 風格重寫，不直接搬。

---

## 小結

- TriCNES 是攻克 AC 最實用的 ground truth —— 但**有限度**：mapper 只涵蓋 8 個、有 5+ 項它自己也不過。
- 用它的方式是「對照語義」，不是「照抄數字」（timer 遞減節奏不同）。
- 永遠保留優先序：**硬體文獻 > 測試 ROM > TriCNES**。

回 [指南首頁](README.md)。
