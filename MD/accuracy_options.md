# AprNes 精確度選項設計規劃

*撰寫日期：2026-03-15*
*背景：基線 174/174 blargg PASS，136/136 AccuracyCoin PASS，Release ~273.8 FPS*

---

## 一、動機

AprNes 為了通過 AccuracyCoin 136/136 及 blargg 174/174，實作了數項高精確度但計算成本較高的硬體模擬功能。這些功能對於執行測試 ROM 和追求最高模擬精準度是必要的，但對於一般遊戲遊玩，部分功能可以在效能受限的機器上選擇性停用，換取更高的 FPS。

**設計目標**：提供一組可於執行期切換的精確度選項（`AccuracyProfile`），讓用戶依據自身硬體和需求選擇適合的設定。

---

## 二、可選精確度功能清單

以下各項按效能影響由高至低排列。

---

### OPT-A：Per-dot Secondary OAM Evaluation FSM

| 項目 | 內容 |
|------|------|
| **實作檔案** | `PPU.cs` — `ppu_step_new()` 內，dots 1–256 |
| **修復目標** | AC P19 SprSL0（Sprite Scanline 0 精確評估）、AC $2004 Stress |
| **呼叫頻率** | ~45,840 calls/frame（SpriteEvalTick × 191 dots × 240 scanlines）|
| **效能成本** | 🔴 **最高** — 佔 ppu_step_new 可見 scanline 段的主要額外負擔 |
| **停用影響** | 退回 `PrecomputeOverflow()` 簡化模型；sprite overflow flag 時序略有誤差；$2004 在 rendering 期間讀值不精確 |
| **建議停用條件** | 低端 CPU（< ~1.5GHz 單核等效），或僅需遊玩商業遊戲不需通過測試 ROM |

**實作細節**：
- Dots 1–64：逐 dot 清零 secondary OAM（32 writes/scanline）
- Dot 65：`SpriteEvalInit()`（初始化 FSM 指標）
- Dots 66–256：`SpriteEvalTick()`（per-dot 評估，含 overflow hardware bug 模擬）
- 停用時：`PrecomputeSprites()` 在 scanline 開始前一次性完成（原有簡化模型）

---

### OPT-B：irqLinePrev / irqLineCurrent 每 CPU cycle 追蹤

| 項目 | 內容 |
|------|------|
| **實作檔案** | `MEM.cs` — `EndCpuCycle()` |
| **修復目標** | BUGFIX18 CPU interrupt timing（cpu_interrupts_v2 5/5）、AC IRQ 相關測試 |
| **呼叫頻率** | 1,789,773 次/frame（每 CPU cycle 一次） |
| **效能成本** | 🔴 **高** — 每次 6 個 static field read（`statusframeint`、`apuintflag`、`statusdmcint`、`statusmapperint`、兩次 assign）= ~1070 萬次 memory access/秒 |
| **停用影響** | IRQ polling 精度降至「opcode fetch 時刻快照」；多數遊戲不受影響，但 IRQ 密集的 APU/Mapper 遊戲可能有極罕見的音效或中斷時序誤差 |
| **建議停用條件** | 不需要精確 IRQ timing 的一般遊玩場景 |

**潛在優化方向（不停用情況下）**：
改為 dirty flag 架構——`irqLineCurrent` 僅在 APU 寫入或 Mapper IRQ 狀態改變時重算，而非每 CPU cycle 無條件重算。mutation sites：APU frame counter（8+）、IO $4015 write（1）、Mapper（MMC3 等 2+）。實作風險中等。

---

### OPT-C：ppuRenderingEnabled 延遲一 dot 生效

| 項目 | 內容 |
|------|------|
| **實作檔案** | `PPU.cs` — `ppu_step_new()` 結尾 |
| **修復目標** | AC P19 BGSerialIn（$2001 寫入後 BG rendering 延遲一 dot 才啟用） |
| **呼叫頻率** | 5,369,318 次/frame（每 PPU dot 一次） |
| **效能成本** | 🟡 **低** — 每次只做 1 OR + 1 bool write，但 × 5.37M/sec 累積可見 |
| **停用影響** | 回退至直接使用 `ShowBackGround`/`ShowSprites`；絕大多數遊戲不受影響，僅極少數依賴 mid-frame rendering enable/disable 精確時序的遊戲 |
| **建議停用條件** | 效能優先模式，或已知遊戲不使用 mid-frame PPU mask 切換 |

---

### OPT-D：ppu2007ReadCooldown（$2007 連續讀取抑制）

| 項目 | 內容 |
|------|------|
| **實作檔案** | `PPU.cs` — `ppu_step_new()` 開頭、`ppu_r_2007()` |
| **修復目標** | dmc_dma_during_read4/double_2007_read（blargg + AC） |
| **呼叫頻率** | 5,369,318 次/frame（每 dot 一次 compare）|
| **效能成本** | 🟢 **極低** — 單一 compare + conditional decrement |
| **停用影響** | 極罕見情況下 $2007 快速連讀結果略有誤差；幾乎不影響商業遊戲 |
| **建議停用條件** | 成本太低，通常不值得停用 |

---

### OPT-E：DMC DMA 精確 state machine（Implicit/Explicit Abort、Bus Conflict）

| 項目 | 內容 |
|------|------|
| **實作檔案** | `MEM.cs` — `ProcessPendingDma()`、`APU.cs` — `dmcfillbuffer()` |
| **修復目標** | AC P13 DMA Tests（BUGFIX53–56）：dmcImplicitAbortPending、dmcDeferredStatus、dmaPrevReadAddress 等 |
| **呼叫頻率** | DMA 執行時高成本，但頻率低（每幾百幀才發生一次 DMC DMA） |
| **效能成本** | 🟠 **中（單次昂貴，頻率低）** — 額外 flag 使 early-exit guard 變複雜（多 1 flag 在 OR chain）|
| **停用影響** | DMC 採樣 DMA 在邊緣案例下時序略有誤差（與 OAM DMA 交互、abort 情況）；商業遊戲中極罕見觸發 |
| **建議停用條件** | 低成本；通常不建議停用，但可列為選項 |

---

### OPT-F：NMI delay 精確 cycle timestamp（long 型別）

| 項目 | 內容 |
|------|------|
| **實作檔案** | `MEM.cs` — `StartCpuCycle()`、`catchUpPPU()` |
| **修復目標** | VBL/NMI 精確時序（BUGFIX 系列） |
| **呼叫頻率** | StartCpuCycle：1.79M/frame；catchUpPPU edge detection：5.37M/frame |
| **效能成本** | 🟢 **低** — 每次 1 long compare |
| **停用影響** | 回退 bool delay 模型；VBL/NMI suppress 邊緣案例可能失敗 |
| **建議停用條件** | 幾乎不值得停用 |

---

## 三、預設 Profile 建議

| Profile 名稱 | 啟用項目 | 適用場景 | 預估 FPS 影響 |
|-------------|---------|---------|-------------|
| **Accurate**（預設） | 全部 OPT-A～F | 測試 ROM、精確模擬 | 基線（~273.8 FPS） |
| **Balanced** | OPT-B～F，停用 OPT-A | 一般遊玩，中高端 CPU | +估計 ~3–6% |
| **Performance** | 停用 OPT-A、OPT-B，其餘保留 | 低端 CPU，效能優先 | +估計 ~5–10% |

> **注意**：以上 FPS 估計尚未實測，需建立 benchmark 後更新。OPT-A（sprite eval FSM）是最大收益來源，OPT-B（irqLinePrev）是第二大。

---

## 四、實作方式設計

### 4.1 靜態布林旗標（推薦最簡方案）

在 `NesCore` 加入 static bool 旗標，在熱路徑加 fast-path branch：

```csharp
// NesCore 靜態選項（在 Main.cs 或 init 前設定）
public static bool AccuracyOptA_SpriteEvalFSM = true;
public static bool AccuracyOptB_IRQPerCycle    = true;
public static bool AccuracyOptC_PPURenderDelay = true;
public static bool AccuracyOptD_2007Cooldown   = true;
public static bool AccuracyOptE_DMCStateMachine = true;
public static bool AccuracyOptF_NMIDelayCycle   = true;
```

在熱路徑：

```csharp
// OPT-A：ppu_step_new() 內
if (AccuracyOptA_SpriteEvalFSM && scanline >= 0 && scanline < 240 && ppuRenderingEnabled)
{
    if (cx >= 1 && cx <= 64) { /* 清零 secondary OAM */ }
    else if (cx == 65) { SpriteEvalInit(); SpriteEvalTick(); }
    else if (cx >= 66 && cx <= 256) { SpriteEvalTick(); ... }
}

// OPT-B：EndCpuCycle()
if (AccuracyOptB_IRQPerCycle)
{
    irqLinePrev = irqLineCurrent;
    irqLineCurrent = (statusframeint && !apuintflag) || statusdmcint || statusmapperint;
}
```

**優點**：實作簡單，切換方便，branch predictor 在固定 profile 下快速學習（旗標幾乎不變）。

**缺點**：每個熱路徑多一次 bool load；但 bool 會被 JIT 最佳化為 register（Release 模式），成本極低。

### 4.2 設定存檔

選項存入 `AprNes.ini`：

```ini
[Accuracy]
SpriteEvalFSM=1
IRQPerCycle=1
PPURenderDelay=1
2007Cooldown=1
DMCStateMachine=1
NMIDelayCycle=1
```

### 4.3 UI 整合

在 `AprNes_ConfigureUI`（設定視窗）新增「精確度」分頁：
- 每個選項一個 CheckBox + 簡短說明
- 顯示「預設 Profile」下拉選單（Accurate / Balanced / Performance）
- 警告文字：「停用精確度選項可能導致部分遊戲行為異常」

---

## 五、實作優先順序

| 優先 | 項目 | 理由 |
|------|------|------|
| P1 | OPT-A（Sprite Eval FSM） | 效能收益最大，停用後仍有 PrecomputeSprites 備用 |
| P2 | OPT-B（irqLinePrev） | 第二大收益；或考慮 dirty flag 優化代替停用 |
| P3 | UI 整合 | 讓用戶能實際切換 |
| P4 | 其餘選項 | 成本低，影響小，非優先 |

---

## 六、注意事項與風險

1. **Profile 切換需重啟 ROM**：部分精確度旗標影響 init 時期的 state，mid-game 切換可能造成狀態不一致。建議在 ROM 載入前套用設定。

2. **Balanced Profile 不通過所有 AC 測試**：停用 OPT-A 後，AC P19 SprSL0 和 $2004 Stress 會 FAIL。這是預期行為，需在 UI 中清楚標示。

3. **irqLinePrev dirty flag 優化**（OPT-B 的替代方案）：若能正確識別所有 mutation sites，dirty flag 可在不犧牲精確度的情況下降低 OPT-B 成本，優先於直接停用該功能。

4. **Branch predictor 考量**：旗標設定後幾乎不改變，branch predictor 能快速學習，額外 bool check 的實際開銷極小（< 0.1%）。

---

*此文件記錄設計規劃，實際實作時依需求展開。*
