# AccuracyCoin 20260521 vs 20260410 — 版本差異與測試結論

**日期**: 2026-05-21
**比較對象**:
- 新版: `nes-test-roms-master/AccuracyCoin-main-20260521/`
- 舊版: `nes-test-roms-master/AccuracyCoin-main-20260410/`（master 基線當時的版本，138/138 滿分）

---

## 1. 版本差異總覽

| 項目 | 舊版 20260410 | 新版 20260521 |
|------|---------------|---------------|
| PASS/FAIL 測試數 | 138 | **139（+1）** |
| DRAW 資訊項 | 5 | 5 |
| 選單項目（含頁首/DRAW） | 146 | 147 |
| `.nes` 大小 | 40976 bytes | 40976 bytes（內容重編） |
| `.asm` 大小 | 676609 bytes | 668527 bytes（+399 / −207 行） |
| 移除/改名的測試 | — | **無** |

### 1.1 唯一新增測試：`Internal Data Bus`（Page 20，最後一項）

選單定義（`AccuracyCoin.asm:773`）排在最後一頁 CPU edge-case 區（Instruction Timing / Implied Dummy Reads / Branch Dummy Reads / JSR Edge Cases 之後）：

```
table "Internal Data Bus", $FF, result_InternalDataBus, TEST_InternalDataBus
```

測試碼 `TEST_InternalDataBus`（`AccuracyCoin.asm:15688`），三個子項，主題是 **2A03 內部資料匯流排 (internal data bus) 與外部資料匯流排 (external data bus) 在 DMC DMA 期間的區別**：

| 子項 | 標題 | 驗證內容 |
|------|------|----------|
| Test 1 | Verify Open Bus | 跨 page 邊界的 open bus 讀取 + DMC DMA timing 要正確（複製自 `DMA + Open Bus`） |
| Test 2 | External Data Bus 不能改 Internal Data Bus | 讀 `$4015` 時，剛發生的 DMC DMA sample fetch（byte = `$60`，bit5=1）**不應該**讓 `$4015` 的 bit5 變 1 |
| Test 3 | Internal Data Bus 不能改 External Data Bus | 反向：DMC DMA 後從 open bus 讀，bit5 **應該**保留 DMA 之前的值 |

### 1.2 既有測試的「期望值修正」（名稱不變，判定改了）

README 與 asm 都有這些改動，**可能讓原本 pass 的項目在新版翻成 fail**（若我們當初是對著舊的、有時不正確的期望寫的）：

| 測試 | 變更 |
|------|------|
| **Stale Sprite Shift Regs** | error code **3 與 5 的情境對調**（sprite zero in-range / out-of-range 在 shift register 的期望值重新對應） |
| **Sprite overflow behavior** | code 5 從「Secondary OAM 滿時 OAM address +5 而非 +1 再 AND $FC」改為「test 3+4 在同一條 scanline 的組合」 |
| **Sprites On Scanline 0** | F-Blank/H-Blank shift register 測試從 3 個 error code 擴充成 **6 個**（sprite counter 要在 F-Blank 繼續 clocking 等） |
| 屬性位元讀取 | 「missing bits 2 through **5**」→「bits 2 through **4**」（修正） |
| **Arbitrary Sprite zero** | 措辭 "sprite evaluation" → "sprite **fetch**"（stale secondary OAM 來源澄清） |
| **$2004 Stress Test** / **$2007 Stress Test** | 各新增 error code #1「emulator 開機時 test 沒能把 CPU 同步到 VBlank」當守門，原 code 往後挪一位 |

### 1.3 純文字修正
- `caannel` → `channel`
- `teh` → `the`

---

## 2. 測試結論（2026-05-21 實測）

**結果：138/139 通過。唯一 fail = Page 20 最後一項 `Internal Data Bus`，error code = 2。**

- 所有舊有 138 項（含 1.2 那批被修正期望值的 sprite/OAM 測試）**全部仍通過** → 代表我們原本的 sprite/OAM 行為本來就對齊新版（修正後）的期望，沒有踩到回歸。
- 唯一掛掉的就是這次**全新增加**的 `Internal Data Bus` 測試，而且是停在 **Test 2**。

---

## 3. `Internal Data Bus` error code 2 是什麼意思

### 3.1 error code 編號對應
測試每過一個子項就 `INC <ErrorCode`，FAIL handler 回報當下的 `ErrorCode`：
- Test 1 過 → ErrorCode 進到 2
- **Test 2 失敗 → 回報 2** ← 我們卡在這

所以 **fail 2 = Test 2「External Data Bus 不能改 Internal Data Bus」沒過**。

### 3.2 Test 2 在測什麼（`AccuracyCoin.asm:15704-15720`）

```asm
JSR TEST_InternalDataBus_Sync   ; 把 DMC DMA 排在 operand 與 read 之間，sample 取 index $15
NOP × 4
LDA $4015   ; [Opcode][Operand][Operand] {DMC DMA} [Read from $4015]
AND #$20    ; DMC DMA 只更新 internal bus，不更新 external bus
BNE FAIL_InternalDataBus   ; bit5 若為 1 → 失敗
```

`TEST_InternalDataBus_Sync`（`asm:15739`）設定 DMC：
- sample 位址 `$EEC0`、長度 33 bytes，內容**全部是 `$60`**（`$60 = 0110_0000`，bit5 = `$20` 是 set 的）。
- 把 DMC DMA 的 sample fetch 精準排在 `LDA $4015` 的兩個 operand fetch 之後、實際讀取之前。

**硬體真實行為（測試期望）**：
- 2A03 內部其實有兩條 data bus：**internal**（CPU 核心內部）與 **external**（對卡帶/APU/PPU 的匯流排）。
- `$4015` 讀取的 **bit 5 是 open bus**，而它反映的是 **internal data bus**。
- 關鍵 quirk：**從 `$4015` 讀取「不會」更新 external data bus**（其他位址的讀取會）。
- DMC DMA 的 sample fetch 讀到 `$60`，這個動作更新的是 **external** bus，**不是** internal bus。
- 所以即使剛剛 DMA 抓到 bit5=1 的 byte，接下來 `$4015` 的 bit5（來自 internal bus）**應該是 0** → `AND #$20` 得 0 → `BNE` 不跳 → PASS。

我們 fail，代表我們的 `$4015` bit5 回傳了 **1**（把 DMC DMA 抓到的 `$60` 當成 open bus 來源了）。

### 3.3 根因：我們只有單一 `cpubus` latch

我們的核心只有一條 open bus 變數 `cpubus`，沒有區分 internal / external：

- `APU.cs:934` — `$4015` 讀取：
  ```csharp
  status |= (byte)(cpubus & 0x20); // bit 5 is open bus (CPU data bus)
  ```
- `CPU.cs:82` — 一般讀取會更新 `cpubus`，但 `$4015` 讀取除外（已正確模擬「$4015 讀不更新 bus」這半邊）：
  ```csharp
  else { val = mem_read_page[addr >> 13](addr); if (addr != 0x4015) cpubus = val; }
  ```
- `MEM.cs:216` — OAM DMA 期間 `cpubus = dmaOamInternalBus;`

問題在於 **DMC DMA 的 sample fetch 會把 `$60` 寫進同一條 `cpubus`**，於是接下來 `$4015` 讀 `cpubus & 0x20` 就拿到 bit5=1。硬體上那個 `$60` 只該落在 **external** bus，而 `$4015` bit5 該讀 **internal** bus —— 兩者在我們的模型裡被合併成同一個 latch，所以無法區分。

這個測試的名字（"Internal Data Bus"）正好點名了我們單 latch 模型塌掉的那個概念。TriCNES 有 internal/external 雙 bus 的區分（`APU_Status_*` 相關），是對應的參考來源。

---

## 4. 狀態與後續

- **✅ 已修復 (2026-05-22)**：dual data-bus 拆分，見 [bugfix/2026-05-22_AC_InternalDataBus_DualDataBus](../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md)。P20 `Internal Data Bus` 現 PASS；blargg 184/184 + AC 受影響頁面（P1/P13/P20）無回歸。下方為當初的根因分析，保留紀錄。
- ~~**現況**：未修。Page 20 `Internal Data Bus` Test 2 fail（code 2）。其餘 138 項全過。~~
- **修正方向**（尚未動工）：把單一 `cpubus` 拆成 **internal data bus** 與 **external data bus** 兩個 latch：
  - 一般 read/write → 兩條都更新
  - `$4015` 讀取 → 只更新 internal，不更新 external（目前我們是「都不更新 external」的近似，但 internal 仍被汙染）
  - DMC DMA / OAM DMA 的 fetch → 只更新 external
  - `$4015` bit5 open bus → 從 **internal** 取
  - Test 3 的反向（從 open bus 讀，期望保留 external 的舊值）也要一起對齊
  - 以 TriCNES 的 internal/external bus 模型為準（符合「以 TriCNES 為唯一準則、禁止自創補償」原則）。
- **基線更新**：master 的 AC 基線 = **139/139 PASS**（dual-bus 修復後，2026-05-22 使用者完整 139 題驗證全數通過；blargg 184/184 無回歸）。
- **測試腳本（已更新 2026-05-21）**：`run_tests_AccuracyCoin_report.sh`（NetFx）與 `run_tests_AccuracyCoin_avalonia.sh`（Avalonia）的 `ROM=` 路徑都已改指向 `AccuracyCoin-main-20260521/`（avalonia 之前甚至還指在更舊的無日期 `AccuracyCoin-main/`），報告內的版本/測試數標籤也一併更新為 2026-05-21 / 139 tests。報告的 TOTAL 數字本來就是動態加總，不受影響。

---

## 附：關鍵 file:line

**新版 ROM**
- `AccuracyCoin.asm:773` — `Internal Data Bus` 選單項
- `AccuracyCoin.asm:15688` — `TEST_InternalDataBus`（Test 1/2/3 本體）
- `AccuracyCoin.asm:15739` — `TEST_InternalDataBus_Sync`（DMC DMA 對齊 + `$60` sample 設定）

**我們的核心**
- `AprNes/NesCore/APU.cs:921` — `apu_r_4015()`，bit5 open bus 來源（`:934`）
- `AprNes/NesCore/CPU.cs:81-82` — read 更新 `cpubus`（`$4015` 除外）
- `AprNes/NesCore/MEM.cs:216, 265` — OAM DMA bus / `Read_OpenBus`
