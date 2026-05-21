# 方法論：怎麼跑 AccuracyCoin、怎麼讀結果、怎麼 debug

> 對應：所有 page。這篇是工具與流程篇 —— 在動手修任何測試前，先把「怎麼跑、怎麼判讀、怎麼定位」搞定，後面才不會瞎忙。

---

## 1. AccuracyCoin 是怎麼運作的

AccuracyCoin 是一張 **NROM（mapper 0）** 卡帶，把 139 項測試塞在一個選單裡。每項測試在畫面上印 `PASS` / `FAIL`，失敗時還給一個 **error code**（1 碼十六進位，對應「第幾個 sub-test 掛了」）。另有 5 項標 `DRAW` 的不判定、只印資訊。

測試結果寫進 RAM 的固定位置（`result_*` label，多半在 `$500`–`$5FF` 區），ROM 自己維護一張結果表。我們的 headless runner 就是去讀這張表 + 截圖。

**手動操作（GUI）**：
- 方向鍵移動游標、`A` 跑當前測試、`B` 標記跳過（再按一次取消）。
- 游標移到頁首（頁碼）時：左右換頁、`A` 跑整頁、`Start` 跑全部並畫總表。
- 跑完一項按 `Select` 開 **debug menu**，印出 `$20`–`$2F`、`$50`–`$6F`、`$500`–`$5FF` 的位元組，debug 個別測試很有用。

---

## 2. headless 怎麼跑（我們的主力流程）

GUI 一頁一頁點太慢。AprNes 有 headless runner，配三支腳本：

### 單頁 / 單項（最常用，快）
```bash
bash run_ac_test.sh <page>            # 跑整頁
bash run_ac_test.sh <page> <item>     # 只跑某一項（1-based）
bash run_ac_test.sh <page> --skip <item>   # 跳過某項、跑其餘
bash run_ac_test.sh <page> --no-build # 跳過編譯
```
跑完會在 `result/ac_p<page>_test.png` 留截圖 —— **直接看截圖判讀 PASS/FAIL 最快**。terminal 也會 dump `AC_RESULTS_HEX:`（結果表的 hex），但人眼看截圖比 parse hex 省事。

> ⚠️ runner 結尾常印 `FAIL(255) | (no $6000 signature)` —— 那只是 headless 的**整體 exit code**判定（AC 不走 blargg 的 `$6000` 協定）。**真正的 per-test 結果看截圖**，每項自己印 PASS/FAIL。

### 完整報告（全 139 題 + 截圖 + HTML）
```bash
bash run_tests_AccuracyCoin_report.sh                 # 完整跑 + 報告
bash run_tests_AccuracyCoin_report.sh --no-build      # 跳過編譯
bash run_tests_AccuracyCoin_report.sh --skip 12:1     # 跳某項
```
輸出在 `reports/report/AccuracyCoin_report.html`。**完整報告很慢**，平常驗單一修復用單頁腳本就好，全套留給最後驗收（或交給人工跑）。

### Avalonia 版
```bash
bash run_tests_AccuracyCoin_avalonia.sh   # 同上，跑 AprNesAvalonia（.NET 10）
```

> 三支腳本的 ROM 路徑都指向 `nes-test-roms-master/AccuracyCoin-main-20260521/`（升級新版時記得一起改）。

---

## 3. 怎麼讀 error code

每項測試的 error code 是「第幾個 sub-test 失敗」。對照來源有兩個：
1. **ROM 的 `README.md`**（`AccuracyCoin-main-20260521/README.md`）—— 官方逐碼說明，例如：
   > Open Bus → `9: Bit 5 of address $4015 should be open bus.`
2. **`.asm` 原始碼** —— 搜 `TEST_<測試名>:`（或 README 提示的 `TestPages:`），看 `INC <ErrorCode` 之間夾的就是各 sub-test。`ErrorCode` 從 1 起算，每過一個 `INC` 一次；FAIL handler 回報當下的值，所以 **fail N = 第 N 個 sub-test 沒過**。

> 範例：[dual data-bus 修復](../../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md) 就是「P20 Internal Data Bus，error code 2」→ 去 `.asm` 找 `TEST_InternalDataBus`，數到第 2 個 sub-test，看它在驗什麼。

---

## 4. 用 TriCNES 當 ground truth

AccuracyCoin 的作者（100thCoin）自己寫了模擬器 **TriCNES**，原則上對自己的測試是滿分。所以遇到「硬體到底該怎樣」的爭議時：

- 把**同一張 ROM** 丟進 TriCNES 跑，看它的行為 / 中間值。
- trace TriCNES 的 `Emulator.cs`（單一大檔，CPU/PPU/APU/DMA 都在裡面）對照我們的實作。
- 參考路徑：`ref/TriCNES-main-20260521/`（最新）、`ref/TriCNES-main-20260410/`（前一版）。

**但 TriCNES 不是 100% 正確**，有幾項它自己也不過（見 [`appendix_tricnes_reference.md`](appendix_tricnes_reference.md)）。優先序永遠是：**硬體文獻（NESdev wiki）> 測試 ROM 期望 > TriCNES**。

---

## 5. 修復紀律（這專案的鐵律）

1. **研究優先於試錯**：硬體行為不確定時，先讀 NESdev wiki / 既有 `ref/` 資料 / TriCNES，弄懂再改。花時間研究遠勝盲目調參。
2. **禁止錯誤補償**：絕不為了過測試加 hack、調 magic number 繞過正確行為。補償會形成互相衝突的鏈，最後無法收斂。若正確行為導致某測試回歸，是**別的地方還不對**，要一起修正，而非在正確行為上打補丁。
3. **一次性完整修正**：發現某個根因影響多個子系統時，一次全改對，而非逐步 patch。短期回歸可接受，但基礎必須正確。
4. **雙測試把關**：每個修復 commit 前，確認 **blargg 184/184** 與 **AC** 都無回歸。改的是共用 NesCore，AprNes（NetFx）+ AprNesAvalonia 兩邊都要顧。

> 為什麼這麼龜毛？因為 AC 後段測試彼此牽連極深（最近 dual data-bus 修 P20 卻回歸 P14 就是教訓）。沒有紀律，修一個壞一個，永遠到不了滿分。

---

## 6. 一個典型修復循環長怎樣

```
1. 跑 run_ac_test.sh <page> → 看截圖，哪項 FAIL、error code 幾號
2. 讀 ROM README + .asm，搞懂該 sub-test 在驗什麼硬體行為
3. 查 NESdev wiki / ref/ / TriCNES，確認真實硬體該怎樣
4. 改 NesCore（CPU/PPU/APU/MEM…），加註解寫清楚「為什麼」
5. 重編 → run_ac_test.sh <page> 再跑，確認該項 PASS
6. 跑「受影響的相鄰頁」+ blargg 184，確認無回歸
7. 更新 AccuracyCoin_TODO + 寫 MD/bugfix/ + commit + push（一個 fix 一個 commit）
```

下一篇：[`00_timing_model.md`](00_timing_model.md) —— 為什麼非得做到 dot/cycle 級精度，以及 AprNes 的 tick 模型長怎樣。
