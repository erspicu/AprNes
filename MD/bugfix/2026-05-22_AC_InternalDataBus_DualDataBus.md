# 2026-05-22 — `$4015` bit5 內外資料匯流排拆分（AccuracyCoin `Internal Data Bus`）

## 症狀

升級到 AccuracyCoin `20260521`（138→139 題）後，唯一新增的測試 **Page 20 `Internal Data Bus`** fail，error code **2**。其餘 138 題與 blargg 184/184 全過。

- error code 2 = 該測試 Test 2「External Data Bus 不能改 Internal Data Bus」。
- 測試手法（`AccuracyCoin.asm:15704-15720`）：把一個 DMC DMA sample fetch（sample 全為 `$60`，bit5=1）排在 `LDA $4015` 的 operand fetch 之後、實際讀取之前，然後檢查 `$4015` 的 bit5（open bus）。硬體期望 bit5 = 0。

## 根因

2A03 有兩條資料匯流排：
- **external data bus**：對卡帶/APU/PPU 的匯流排，由 CPU 讀寫**與 DMA fetch** 更新。
- **internal data bus**：CPU 核心內部，`$4015` 讀取的 bit5（open bus）取自這條；DMC DMA 的 sample fetch 只動 external，**不動 internal**。

我們原本只有單一 `cpubus` latch（`PPU.cs`），`apu_r_4015()` 用 `cpubus & 0x20` 取 bit5。DMC DMA 的 `DmaFetch` 把 `$60` 寫進 `cpubus` → 緊接著 `LDA $4015` 讀 `cpubus & 0x20` = bit5=1 → fail。兩條本該分開的匯流排被我們合併成一條。

## 修法（對齊 TriCNES 20260521 `internalBus` 模型）

新增獨立 `internalBus` 欄位，與 external `cpubus` 分離：

| 事件 | `cpubus`（external）| `internalBus`（internal）|
|------|------|------|
| CPU 一般 read（非 `$4015`）| 更新 | 更新 |
| CPU `$4015` read | 不更新 | 不更新 |
| CPU write | 更新 | 更新 |
| **DMA fetch（OAM/DMC）** | **更新** | **不更新** ← 關鍵 |

`$4015` 讀取的 bit5（open bus）改從 `internalBus` 取。這樣 DMC DMA 的 `$60` 只進 `cpubus`，`$4015` bit5 仍是 DMA 前最後一次 CPU access 的值（測試裡是 operand high `$40`，bit5=0）→ pass。Test 3（反向）讀的是一般 open bus（external `cpubus`），DMA 的 `$60` 在那裡，bit5=1 → pass。

> open bus 的其他消費者（`$4016/$4017` 上位元、unmapped read、FDS）維持讀 external `cpubus`，不變。

## 改動檔案

| 檔案 | 改動 |
|------|------|
| `NesCore/PPU.cs` | 新增 `static public byte internalBus`（緊鄰 `cpubus`）|
| `NesCore/CPU.cs` | `CpuRead`/`CpuReadZP`/`CpuWrite`/`CpuWriteZP` 在更新 `cpubus` 時同步 `internalBus`（`$4015` read 兩者都不更新）|
| `NesCore/APU.cs` | `apu_r_4015()` bit5 改 `internalBus & 0x20` |
| `NesCore/MEM.cs` | `DmaFetch` 的 `$4015` bus-conflict 路徑 bit5 改 `internalBus & 0x20`；`DmaFetch` 一般路徑維持只更新 `cpubus`、**不**碰 `internalBus` |
| `NesCore/Main.cs` | reset 時 `internalBus = 0` |

> 共用 NesCore，AprNes（NetFx）與 AprNesAvalonia 同步受惠。

## 驗證

- blargg：**184/184 PASS**（`python run_tests.py -j 10`，46s，無回歸）
- AccuracyCoin 20260521 受影響頁面（`run_ac_test.sh`）：
  - P1 Open Bus（`$4015` bit5 + databus 規則）：全 PASS
  - P13 全 DMA（含 DMA + `$4015` Read、DMC DMA Bus Conflicts、Explicit/Implicit DMA Abort）：全 PASS
  - **P20 `Internal Data Bus`：PASS**（先前 error code 2）
- 完整 139 題 AC 報告由使用者自行驗證。

## 參考

- AC 版本差異：[AccuracyCoin_20260521_diff_and_result](../notes/AccuracyCoin_20260521_diff_and_result.md)
- TriCNES 新舊版 diff（fix 來源）：[TriCNES_20260521_vs_20260410_diff](../notes/TriCNES_20260521_vs_20260410_diff.md)
- TriCNES 20260521 `Emulator.cs:496`（`internalBus` 欄位）、`:9254-9274`（`$4015` 讀）、`:9305`（一般 read 同步）
