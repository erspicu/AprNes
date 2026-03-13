# DMA 完整重寫計畫

**建立日期**: 2026-03-08
**基線**: 174/174 blargg, 122/136 AccuracyCoin
**目標**: 修復剩餘 13 FAIL + 1 SKIP（全部與 DMA timing 相關）
**潛在收益**: +13~14 PASS
**狀態**: **已完成** — 最終成果 174/174 blargg + 136/136 AC (BUGFIX33-56)

---

## 長期準則

1. **硬體行為(文獻了解) > 測試 ROM 期望 > Mesen2 參考**
2. **禁止錯誤補償**: 不為通過測試添加 hack。正確硬體行為導致回歸時，找出並修正其他不正確部分。
3. **一次性完整修正**: 根因影響多個子系統時，一次全改。短期回歸可接受，基礎必須正確。
4. **研究優先於試錯**: 不確定的硬體行為，先下載文獻研讀再動手。花時間 study 遠勝盲目修改。

---

## 現狀分析

### 已完成的基礎建設

- [x] Asymmetric MCU split: StartCpuCycle(+5/+7) → EndCpuCycle(+7/+5)
- [x] catchUpPPU 在 Start 和 End 各跑一次（2+1 dot 分佈）
- [x] NMI edge detection 在 EndCpuCycle
- [x] IRQ sampling 在 EndCpuCycle
- [x] nmi_delay_cycle 精確到 cycle 的 NMI delay
- [x] Unified DMA state machine (ProcessPendingDma)
- [x] ProcessDmaRead bus conflict 處理
- [x] DMC Load DMA parity countdown (BUGFIX31)

### 目前的核心問題

**ProcessPendingDma 位置錯誤**（compensation chain）：

```
目前 (DMA-after-Start):
  Mem_r(addr):
    StartCpuCycle(true)     ← cpuCycleCount++ 在這裡
    ProcessPendingDma(addr) ← getCycle = (CC & 1) == 0，但 CC 已經比 Mesen2 多 1
    bus read
    EndCpuCycle(true)

Mesen2 (DMA-before-Start):
  MemoryRead(addr):
    ProcessPendingDma(addr) ← getCycle = (CC & 1) == 0，CC 是正確值
    StartCpuCycle(true)     ← CC++ 在 DMA 之後
    bus read
    EndCpuCycle(true)
```

**影響鏈**：
- CC 多 1 → getCycle parity 翻轉 → Reload DMA 產生 3 cycles（應為 4）
- BUGFIX31 的 `(apucycle & 1) != 0 ? 2 : 3` 是為 DMA-after-Start 校準的補償值
- 移動 DMA 到 Start 前 → Load DMA countdown 需要跟著改 → OAM DMA parity 也變 → 多處連鎖反應

### 剩餘 13 FAIL + 1 SKIP 的根因分類

| 根因 | 測試 | 數量 | 說明 |
|------|------|------|------|
| DMA cycle parity | P13×6 + P20×2 | 8 | phantom read 碰錯地址/時機 |
| DMA + SH* RDY | P10×5 | 5 | DMA halt 在 write cycle 延遲不足 |
| DMC 累積偏移 | P12 Test E | 1 SKIP | DMA ~12 cycle drift → hang |

---

## 步驟規劃

### Step 0: 研究準備（每步開始前必做）

**在動手改任何東西之前，必須先完成相關文獻研讀。**

需要確認的硬體行為文獻：

- [x] `ref/DMA - NESdev Wiki.html` — DMA timing 基本規格
- [x] `ref/DMC_DMA timing and quirks - nesdev.org.html` — DMC DMA 進階細節
- [x] `ref/Mesen2-master/Core/NES/NesCpu.cpp` L317-448 — Mesen2 ProcessPendingDma
- [x] `ref/Mesen2-master/Core/NES/NesCpu.h` — DMA state 定義
- [x] `ref/Mesen2-master/Core/NES/APU/DeltaModulationChannel.cpp` — DMC Load/Reload DMA trigger

**研讀後需回答的問題**：

1. **Load DMA** (由 $4015 write 觸發) 的精確 halt timing 是什麼？

   **答案**: Load DMA 排程在 **GET cycle** halt。正常情況 3 cycles (halt+dummy+read)。
   如果被 write cycle 延遲奇數次 → 4 cycles (halt+dummy+alignment+read)。
   $4015 write 後有 2-3 APU cycle 延遲才真正啟動 DMA（Mesen2: `_transferStartDelay`）。
   延遲值: `(CycleCount & 1) == 0 ? 2 : 3`（依 CPU cycle parity）。

2. **Reload DMA** (buffer 自動清空觸發) 的精確 halt timing 是什麼？

   **答案**: Reload DMA 排程在 **PUT cycle** halt（與 Load DMA 相反！）。
   正常情況 4 cycles (halt+dummy+alignment+read)。
   如果被 write cycle 延遲奇數次 → 3 cycles (halt+dummy+read)。
   Reload DMA 由 DMC output unit 在 `_bitsRemaining` 歸零時直接觸發
   `StartDmcTransfer()`，無額外延遲（除非 `_transferStartDelay > 0`，
   表示剛被 $4015 啟用，此時由 ProcessClock 延遲觸發）。

3. **OAM DMA** 的 halt + alignment 規則？513 vs 514 cycles 的分界條件？

   **答案**:
   - OAM DMA 在 $4014 write 後的第一個 CPU cycle 嘗試 halt（只能在 read cycle halt）
   - **513 cycles**: halt 落在 PUT cycle → 下一 cycle 是 GET → 可立刻開始讀取
     = 1 halt + 256 get/put pairs
   - **514 cycles**: halt 落在 GET cycle → 下一 cycle 是 PUT → 需 alignment
     = 1 halt + 1 alignment + 256 get/put pairs
   - Mesen2: 在 ProcessPendingDma 主迴圈用 `getCycle = (CycleCount & 1) == 0`
     判斷是否需要 alignment

4. **DMA halt 在 write cycle 時的行為？**

   **答案**: CPU 使用 RDY input halt。RDY 只在 read cycle 生效。
   如果 CPU 正在 write cycle，halt 請求被忽略，DMA unit 下一 cycle 再嘗試。
   最多延遲 3 cycles（RMW 有 2 consecutive writes，interrupt sequence 有 3）。
   **DMA 永遠只從 Mem_r/ZP_r 觸發，不從 Mem_w/ZP_w 觸發。**

5. **Phantom read** 是否在 halt/dummy/alignment cycle 都發生？

   **答案**: **是的，全部三種 no-op cycle 都執行 phantom read**。
   CPU 被 RDY halt 後，重複最後一次 read 的地址。所有 no-op cycles
   （halt, dummy, alignment）都是可見的 bus read，有完整 side effects：
   - `$2002`: 清除 VBL flag
   - `$4015`: 清除 frame counter IRQ flag
   - `$4016/$4017`: NES-001 上多個連續讀只算 1 次 shift（joypad /OE 保持 assert），
     但 RF Famicom 每 cycle 獨立 clock
   - 地址混合怪癖: 如果 CPU halt 時在讀 $4000-$401F，DMA 地址的 bits 4-0
     會與 CPU 地址的 bits 4-0 混合，可能誤觸發 2A03 內部寄存器

### 關鍵發現：Load vs Reload DMA 的 GET/PUT 差異

| 類型 | 排程 halt phase | 正常 cycles | 延遲奇數後 |
|------|----------------|-------------|-----------|
| Load DMA ($4015 write) | GET cycle | 3 (H+D+R) | 4 (H+D+A+R) |
| Reload DMA (buffer empty) | PUT cycle | 4 (H+D+A+R) | 3 (H+D+R) |
| OAM DMA ($4014 write) | 下一 read cycle | 513 or 514 | N/A |

### 關鍵發現：Mesen2 Load DMA parity 公式

```cpp
// DeltaModulationChannel.cpp SetEnabled():
if((_console->GetCpu()->GetCycleCount() & 0x01) == 0) {
    _transferStartDelay = 2;
} else {
    _transferStartDelay = 3;
}
```

我們的 BUGFIX31 公式:
```csharp
dmcLoadDmaCountdown = (apucycle & 1) != 0 ? 2 : 3;
```

**潛在問題**: 我們用 `apucycle & 1`，Mesen2 用 `CycleCount & 1`。
如果兩者 parity 不一致，延遲值會錯。移動 DMA 到 before-Start 後，
應改用 `cpuCycleCount & 1` 以完全匹配 Mesen2。

### 關鍵發現：ProcessPendingDma 位置與 getCycle parity

**DMA-after-Start (目前)**:
```
Mem_r → StartCpuCycle(CC++) → ProcessPendingDma
  halt: StartCpuCycle(CC++) → EndCpuCycle
  loop: getCycle = (CC & 1) == 0  ← CC 比 Mesen2 多 1（主 Mem_r 的 CC++）
```
→ getCycle parity 與 Mesen2 **相反**

**DMA-before-Start (目標)**:
```
Mem_r → ProcessPendingDma → StartCpuCycle(CC++)
  halt: StartCpuCycle(CC++) → EndCpuCycle
  loop: getCycle = (CC & 1) == 0  ← CC 與 Mesen2 完全一致
```
→ getCycle parity 與 Mesen2 **一致**

---

### Step 1+2: 移動 ProcessPendingDma + 修正 parity（必須同時完成）

**目標**: 使 getCycle parity 與 Mesen2 一致，修正所有依賴 parity 的公式

#### 改動 1: MEM.cs — Mem_r/ZP_r 中 ProcessPendingDma 移到 StartCpuCycle 前

```csharp
// MEM.cs — Mem_r
static byte Mem_r(ushort address)
{
    cpuBusAddr = address;
    cpuBusIsWrite = false;
    if (dmaNeedHalt) ProcessPendingDma(address);  // ← 移到 Start 前
    StartCpuCycle(true);
    byte val = mem_read_fun[address](address);
    if (address != 0x4015) cpubus = val;
    EndCpuCycle(true);
    return val;
}

// MEM.cs — ZP_r (同樣移動)
static byte ZP_r(byte addr)
{
    cpuBusAddr = addr; cpuBusIsWrite = false;
    if (dmaNeedHalt) ProcessPendingDma(addr);  // ← 移到 Start 前
    StartCpuCycle(true);
    byte val = NES_MEM[addr]; cpubus = val;
    EndCpuCycle(true);
    return val;
}
```

#### 改動 2: APU.cs — Load DMA countdown 改用 cpuCycleCount

BUGFIX31 的 `(apucycle & 1)` 應改為 `(cpuCycleCount & 1)` 以精確匹配 Mesen2:

```csharp
// APU.cs — apu_4015() 中 DMC enable 分支
// Mesen2: (GetCycleCount() & 0x01) == 0 ? 2 : 3
dmcLoadDmaCountdown = (cpuCycleCount & 1) == 0 ? 2 : 3;
```

**注意**: apu_4015 在 Mem_w 的 write handler 中被呼叫。
Mem_w 的 StartCpuCycle(false) 已經 CC++，所以此時 cpuCycleCount 已是
「當前 cycle 的值」，與 Mesen2 的 GetCycleCount() 一致。

#### 改動 3: 無需改動 — OAM DMA alignment 自動修正

移動 DMA 到 before-Start 後，getCycle parity 與 Mesen2 一致。
OAM DMA 的 alignment 判斷 `getCycle = (cpuCycleCount & 1) == 0`
自動產生正確的 513/514 cycles。

**關鍵理由**: 硬體上 OAM DMA halt 落在 PUT cycle → 513 (下一是 GET，可讀)。
DMA-after-Start 下 parity 翻轉使 513/514 恰好補償，現在修正 parity 後
同樣的值自然產生正確結果。

#### 驗證

```bash
# 編譯
powershell -NoProfile -Command "& 'C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe' ..."

# 關鍵 DMA 測試（最可能受影響）
./AprNes/bin/Debug/AprNes.exe --wait-result --max-wait 30 --rom nes-test-roms-master/checked/sprdma_and_dmc_dma/sprdma_and_dmc_dma.nes
./AprNes/bin/Debug/AprNes.exe --wait-result --max-wait 30 --rom nes-test-roms-master/checked/cpu_interrupts_v2/4-irq_and_dma.nes
./AprNes/bin/Debug/AprNes.exe --wait-result --max-wait 30 --rom nes-test-roms-master/checked/dmc_dma_during_read4/double_2007_read.nes

# 完整回歸
python run_tests.py -j 10
```

**目標**: 174/174 blargg 無回歸

#### 預期風險

| 測試 | 風險 | 原因 |
|------|------|------|
| 4-irq_and_dma | **高** | OAM DMA cycle count 改變 (513↔514) |
| sprdma_and_dmc_dma | **高** | DMC+OAM 重疊 parity 改變 |
| double_2007_read | **中** | Load DMA countdown 改變 |
| dma_2007_read | **中** | DMA phantom read timing 改變 |

若 4-irq_and_dma 失敗: OAM alignment 可能需要微調。
在修正前先確認: 失敗是 513→514 還是 514→513？用 trace log 確認。

---

### Step 3: 區分 Load DMA vs Reload DMA 的 halt phase

**前置**: Step 1+2 完成且 174/174 無回歸

**硬體行為（NESdev Wiki 文獻）**:
- **Load DMA**: 排程在 GET cycle halt → 正常 3 cycles
- **Reload DMA**: 排程在 PUT cycle halt → 正常 4 cycles
- 目前我們沒有區分兩者的 halt phase

**分析**:
目前 dmcStartTransfer() 統一設定 `dmaNeedHalt = true`。
ProcessPendingDma 中的 halt cycle 不分 Load/Reload，都在下一個 read cycle halt。
這可能已經自然符合 Load DMA（GET cycle halt），但 Reload DMA 應在 PUT cycle halt。

**可能的改動**:
```csharp
// APU.cs: 區分 Load 和 Reload
static bool dmcIsReloadDma = false;  // true = Reload (PUT halt), false = Load (GET halt)

// clockdmc 中 buffer empty 觸發:
dmcIsReloadDma = true;
dmcStartTransfer();

// apu_4015 中 $4015 write 觸發 (Load countdown 到期後):
dmcIsReloadDma = false;
dmcStartTransfer();
```

```csharp
// MEM.cs ProcessPendingDma: Reload DMA 的 halt 在 PUT cycle
// 如果 dmcIsReloadDma && getCycle: 這是 GET cycle，需要等到 PUT
// 具體實作需要在 halt cycle 中增加 parity 判斷
```

**注意**: 這步可能較複雜，需要仔細研究 ProcessPendingDma 的 halt 邏輯
如何區分 Load/Reload 的不同 halt phase。Mesen2 沒有顯式區分這兩者
（processCycle lambda 統一處理），可能靠 _transferStartDelay 的 timing
自然使 Load DMA 在 GET cycle 觸發。

**驗證**: 174/174 + AccuracyCoin P13 DMADMASync_PreTest

---

### Step 4: P13 Phantom Read Side Effects

**前置**: Step 1-3 完成

P13 測試要求 DMA phantom read 在正確時機觸發 side effects：
- `$2002` 讀取清除 VBL flag（DMA + $2002 Read）
- `$4015` 讀取清除 frame counter IRQ flag（DMA + $4015 Read）
- 所有 no-op cycles (halt, dummy, alignment) 都執行 phantom read

**已實作**: ProcessPendingDma 中的 phantom read 已使用
`mem_read_fun[readAddress](readAddress)`，halt/dummy/alignment 全部執行。

**關鍵**: Step 1-3 修正 parity 後，phantom read 落在正確的 cycle，
side effects 的觀測時機應自動修正。

**如仍有問題**: 檢查 phantom read 是否在 StartCpuCycle 和 EndCpuCycle
之間正確執行（而非在 Start 之前）。

**驗證**: P13 DMA + $2002 Read (err=2→PASS), DMA + $4015 Read (err=2→PASS)

---

### Step 5: DMC DMA + OAM DMA 重疊 / DMA Abort

**前置**: Step 4 完成

**硬體行為（NESdev Wiki）**:
- DMC DMA 和 OAM DMA 各自獨立，僅在同一 cycle 都需要 access 時衝突
- **衝突時 DMC 優先**: OAM 被暫停，下一 cycle 重新對齊 → 中間重疊通常 +2 cycles
- **OAM 結尾特殊情況**:
  - DMC 在倒數第二個 PUT: +1 cycle
  - DMC 在最後一個 PUT: +3 cycles
- **Aborted DMA**: $4015 在 reload DMA 排程前 1 APU cycle 停止 → DMA 啟動後立刻中止
  - 只花 1 cycle (halt only)，如果被 write cycle 擋住則 0 cycles

**需要驗證**: ProcessPendingDma 中 DMC/OAM 的交互邏輯是否正確處理上述情況。

**Abort 機制**: 我們已有 `dmcAbortDma` flag。需要確認:
- abort 後是否正確只消耗 halt cycle
- OAM DMA 結尾的 DMC 重疊是否正確 (+1/+3 cases)

**驗證**: P13 剩餘 6 項

---

### Step 6: SH* 指令 DMA 互動 (P10)

**前置**: Step 5 完成

**硬體行為（NESdev Wiki）**:
> DMA can only halt on CPU read cycles. Write cycles delay halt up to 3 cycles.

**P10 SH* 測試 (err=7)**: 5 個 SH* 指令 (SHA/SHX/SHY/SHS) 寫入 `A & X & (H+1)`。
測試期望: DMA phantom read 在 SH* 的 write cycle **之間**（被 write 延遲後）
改變了 bus state，使 H 的 AND masking 被消除。

**分析**: 我們的 ProcessPendingDma 只在 Mem_r/ZP_r 中呼叫。
如果 DMA request 在 write cycle 的 apu_step() 中產生（因為 StartCpuCycle
在 Mem_w 中也呼叫 apu_step），dmaNeedHalt 會設定但要等到下一個 Mem_r
才觸發。這已經自然實作了 "write cycle 不 halt" 的行為。

**問題**: SH* 指令在 CPU.cs 中的 write cycle 是否正確使用 Mem_w？
如果 SH* 的 page-cross dummy read 也用 Mem_r，DMA 可能在那裡觸發
（太早）。需要檢查 CPU.cs 中 SH* 指令的實作。

**驗證**: P10 5 項 SH* 測試

---

### Step 7: P20 CPU Behavior 2

**前置**: Step 1-5 完成

P20 失敗的 2 個測試都使用 DMA sync 作為前置條件:
- Instruction Timing (err=2): DMADMASync 前置條件
- Implied Dummy Reads (err=3): DMADMASync 前置條件

如果 Step 1-5 修正了 DMA timing，前置條件自動通過，
真正測試的指令行為（我們已經正確實作）應該 PASS。

**驗證**: P20 4/4 PASS

---

### Step 8: P12 IRQ Flag Latency Test E

**前置**: Step 1-7 完成

Test E 使用 DMC DMA sync loop 精確對齊 CPU timing。
每次 DMC DMA steal 3-4 cycles，如果 Load/Reload cycle count 不正確，
累積偏移會在多次 DMA 後 desync (~12 cycles)。

如果 Step 1-3 的 DMA timing 完全正確（Load=3, Reload=4），
累積偏移應該消除。

**驗證**: P12 Test E 不再 hang（SKIP → PASS）

---

## 工作流程

### 每步開始前

1. 確認在最新 commit 上（`git status` 乾淨）
2. 研讀相關文獻（Step 0 的問題清單）
3. 理解當前 code 的行為和改動影響

### 每步完成後

1. 編譯: `powershell -NoProfile -Command "& 'C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe' ..."`
2. 舊測試: `python run_tests.py -j 10` → 174/174
3. AC 測試: `bash run_tests_AccuracyCoin_report.sh` → 記錄分數
4. 如有進步（AC +1 以上且無 blargg 回歸）:
   - 更新 `MD/AccuracyCoin_TODO.md`
   - 新增 `bugfix/` 文件
   - `git commit` + `git push`
5. 在本文件更新進度（打勾 checkbox）

### 回歸處理

- **blargg 回歸 ≤ 3 項**: 分析是否為補償 hack 暴露的其他 bug，修正後繼續
- **blargg 回歸 > 3 項**: 暫停，回到 Step 0 重新研讀文獻，可能理解有誤
- **AC 回歸**: 如果 blargg 無回歸，AC 回歸通常可接受（暫時性），繼續下一步

---

## 進度追蹤

| Step | 狀態 | blargg | AC | 日期 | 備註 |
|------|------|--------|-----|------|------|
| 0 研究 | 未開始 | — | — | | |
| 1 DMA 移位 | 未開始 | — | — | | 必須與 Step 2 同時 |
| 2 Parity 修正 | 未開始 | — | — | | 與 Step 1 一起 |
| 3 Reload DMA | 未開始 | — | — | | |
| 4 Phantom Read | 未開始 | — | — | | |
| 5 DMC+OAM 重疊 | 未開始 | — | — | | |
| 6 SH* RDY | 未開始 | — | — | | |
| 7 P20 CPU | 未開始 | — | — | | |
| 8 P12 Test E | 未開始 | — | — | | |

---

## 參考資料清單

| 路徑 | 內容 | 優先級 |
|------|------|--------|
| `ref/DMA - NESdev Wiki.html` | DMA timing 主要規格 | 必讀 |
| `ref/DMC_DMA timing and quirks - nesdev.org.html` | DMC DMA 進階 | 必讀 |
| `ref/APU DMC - NESdev Wiki.html` | DMC 行為 | 必讀 |
| `ref/Mesen2-master/Core/NES/NesCpu.cpp` L317-448 | Mesen2 DMA 引擎 | 參考 |
| `ref/Mesen2-master/Core/NES/NesCpu.h` | DMA state 定義 | 參考 |
| `ref/Mesen2-master/Core/NES/APU/NesApu.cpp` | DMC trigger | 參考 |
| `AprNes/NesCore/MEM.cs` | 目前 DMA 引擎 | 工作檔案 |
| `AprNes/NesCore/APU.cs` | DMC control | 工作檔案 |
| `MD/AccuracyCoin_TODO.md` | 測試狀態追蹤 | 進度記錄 |
