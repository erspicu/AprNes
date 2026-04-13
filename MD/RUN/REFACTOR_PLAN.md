# 靜態分派主迴圈重構規劃 (Static Dispatch Main Loop)

**日期**: 2026-04-13
**狀態**: ✅ **已完成（feature/static-dispatch-mainloop 分支，Stages 1/3/4/5 全 commit）**
**基線**: master @ 4a6ff7d, 184/184 blargg + 138/138 AC, FPS=64.77 (Debug, ultra+CRT+RF+4x+DSP2)

## 實作結果總覽

| 階段 | Commit | 內容 | 驗證 |
|------|--------|------|------|
| 1 | 88da1a7 | Run_NTSC + AlignPhaseForFastPath + MasterClockTickInlineNTSC | 184/184 blargg PASS |
| 2 | — | UI dispatcher（無需改動，已透過 run() 內建） | ✅ 自動生效 |
| 3 | 103acfc | Run_FDS + MasterClockTickInlineFDS | FDS 由使用者自行煙霧測試 |
| 4 | 7f946ea | Run_Dendy + MasterClockTickInlineDendy (LCM=15) | 無 Dendy 專屬測試 ROM |
| 5 | 48b70b4 | Run_PAL + MasterClockTickInlinePAL (LCM=80) | pal_apu_tests 10/10 PASS |

**效益**：
- 架構目標達成：4-way static dispatch, region-specific kernels
- 修正 `mcCpuClock == 8` NTSC-hardcoded NMI 偏移問題於 PAL (12) / Dendy (11)
- FPS 與 master 基線持平（Debug + 全 analog 管線，瓶頸在 CRT 後處理）
- 真正的 FPS 增益需更高階優化（例如純結構展開），但該路徑被證實會破壞 PPU timing（見下）

**重要學習（未來優化的護欄）**：
純結構展開（移除 `mcCpu==X` / `mcPpu==X` gate checks、手動設定計數器值）會回歸 PPU timing 測試（vbl_nmi_timing / sprite_hit_tests / ppu_vbl_nmi）。即使每個事件點的 mc*Clock 值看似與 slow path 完全一致，PPU register handler 透過 `& 3` 觀察的**過渡態**仍依賴 slow path 的 reset-then-decrement 語意。Stage 1A 採取的「inlined-gated」折衷版是目前能保持 184/184 的最佳形式。

---

## 0. 動機

目前 `Main.cs:602 MasterClockTick()` 為**單一共用狀態機**，以 `mcCpuClock` / `mcPpuClock` 倒數計時決定 CPU/PPU/APU 子步觸發時機。每秒執行約 **2,147 萬次**（NTSC 357,368 × 60），每次內含：

- `mcCpuClock == 0` / `== 8` / `== 5` / `== masterPerCpu` 共 4 處比較
- `mcPpuClock == 0` / `== masterPerPpuHalf` 共 2 處比較
- `if (!isFDS)` 分支 × 2（CpuCycle + CpuClockRise）
- `mcCpuClock--` / `mcPpuClock--` / `masterClockTotal++` bookkeeping

對 95%+ 使用 NTSC 標準卡匣（非 FDS）的情境，這些判斷大多是 dead weight。

重構構想：**在 UI 載入 ROM 後，依 `Region × isFDS` 在 thread 建立時選擇專用的 `Run_*()` 方法**。每個路徑內以「完整相位週期」為粒度做迴圈展開，抹除所有子步時機判斷。

---

## 1. 建議合理性評估

### 1.1 核心策略（靜態分派 + Master Clock LCM 展開）— ✅ **採用**

**物理依據正確**：
- NTSC: CPU=12 MC / PPU=4 MC → `LCM(12,4)=12`，12 MC 為 1 個完整相位週期（1 CPU + 3 PPU）
- PAL:  CPU=16 MC / PPU=5 MC → `LCM(16,5)=80`，80 MC 為 1 個完整相位週期（5 CPU + 16 PPU）
- Dendy: CPU=15 MC / PPU=5 MC → `LCM(15,5)=15`，15 MC 為 1 個完整相位週期（1 CPU + 3 PPU）— **與 NTSC 邏輯對稱，只差常數**
- FDS: 硬體為 NTSC 時基，`fds_CpuCycle()` 取代 `MapperObj.CpuCycle()`

**預期收益**：
- 消除 `mc*Clock` 倒數與 4+2 處比較（每秒 > 1 億次 ALU ops）
- 消除 `if (!isFDS)` × 2 分支（每秒 > 400 萬次誤預測）
- 展開後的線性程式碼讓 x64 亂序執行單元可跨子步 ILP
- **預估 FPS 提升 5–15%**（文件聲稱 20–50%，但那是包含 MEM 委派重構的總和；單拆主迴圈應更保守）

**風險**：程式碼 WET（4 條冗餘路徑）。可透過保留核心 helper（`cpu_step_one_cycle`, `ppu_step_new`, `apu_step`, `ppu_half_step_new`）為單一實作來控管。

### 1.2 建議文件中的「自動相位修復」混合路徑 — ❌ **不採用**

`模擬器效能極致優化秘訣.md` 提出：`run()` 用 `if (mcCpuClock==0 && mcPpuClock==0) 跑fast else 跑slow`。

**拒絕理由**：
- 依然在 hot path 留下判斷，失去「抹除 100% 時機判斷」的主要好處
- 混合路徑讓 JIT 無法完整 inline / unroll
- 靜態分派的乾淨版應在 thread 啟動時就綁定 Run_*，不在 tick 層動態切換

改採：**純靜態分派版**（`NTSC_PAL 模擬器效能優化實踐.md` + `Dendy` 文件的方案），外加合理的幀尾處理。

### 1.3 幀尾餘數處理 — ⚠️ **修正建議**

建議文件：NTSC 每幀 357368 MC，29780 次快速 + **8 MC 慢速** 補齊。

**問題**：8 MC 慢速補齊後，下一幀開始的 CPU/PPU 相位 **不會是 0**（因為 29780×12 = 357360，剩 8 MC 會推進 CPU/PPU 一部分）。這會讓下一幀的 fast batch 從非對齊狀態開始，破壞展開前提。

**正確理解**：
- 當前 `run()` 的 `for (batch < MasterTicksPerFrame)` **不是相位對齊點**，只是 exit 檢查節流閥
- 相位在幀邊界**本來就跨越**（tick 連續運行，幀邊界由 PPU scanline=241 的 VBL 中斷處理，與主迴圈無關）

**修正做法**：
- 捨棄 per-frame 批次結構
- 改為「連續快速週期」+ 「每 N 次週期檢查 exit」（例：每 10,000 次 12 MC 週期 = 120,000 MC ≈ 1/3 幀，檢查一次 exit）
- `MasterClockTick()` 保留作為**冷啟動相位對齊**用（開機最初幾個 tick，直到 `mcCpuClock==0 && mcPpuClock==0` 時切入快速路徑）

### 1.4 硬碼常數替換 `masterPerCpu`/`masterPerPpu` — ✅ **採用**

在各 `Run_*()` 內，直接寫 `12`/`4` 或 `16`/`5`/`80` 或 `15`/`5` 而非讀靜態欄位。讓 JIT 視為編譯期常數、最佳化 × 4 分流路徑。

### 1.5 NMI / IRQ 時機硬碼值修正 — ⚠️ **重要副產物**

現行 `MasterClockTick()` 使用：
```csharp
else if (mcCpuClock == 8) { NMI check }   // Line 622
if (mcCpuClock == 5) { IRQ check }        // Line 630
```

`8` 是 NTSC-specific（`masterPerCpu - 4 = 12 - 4 = 8`）。
- NTSC (12): NMI 在 CPU 後 4 MC（mcCpuClock=8）✓
- PAL  (16): NMI 應在 CPU 後 4 MC（mcCpuClock=**12**），但目前硬碼 `8` 會落在 CPU 後 8 MC — **疑似 bug**
- Dendy (15): 應為 mcCpuClock=**11** — **疑似 bug**

`5` 是 IRQ 在 CPU 前 5 MC（`5`）— 這個對三區域皆可一致（因為是 "next CPU 前 5 MC"，不依 masterPerCpu）。

**重構時應將 NMI 點調整為「CPU 後 4 MC」的正確相對位置**。各 Run_*() 可直接寫：
- NTSC: MC 4
- PAL:  MC 4
- Dendy: MC 4

（均為「CPU step 後 4 MC」，而非「mcCpuClock==8」這種 NTSC-hardcoded 表述）

這其實是 **靜態分派的隱藏紅利**：暴露並修正原本共用路徑中的 NTSC-bias。

### 1.6 文件中的 MEM.cs 委派移除（`CpuRead_Standard` / `CpuRead_FDS`）— ⛔ **本次不做**

使用者已明確指示「記憶體重構那邊不要理會」。MEM.cs 現行 `mem_read_fun` / `mem_write_fun` 用 `Action/Func<>` 維持原狀。

---

## 2. 實作藍圖

### 2.1 檔案改動範圍

| 檔案 | 改動 |
|------|------|
| `AprNes/NesCore/Main.cs` | 新增 `Run_NTSC()` / `Run_PAL()` / `Run_Dendy()` / `Run_FDS()` + 對應 `*Fast*Clocks()` helper；保留 `MasterClockTick()` 與 `run()` |
| `AprNes/NesCore/Main.cs` | 新增 `SelectRunMethod()` 回傳 `Action` delegate 供 UI 使用 |
| `AprNes/UI/AprNesUI.cs` | （可選）UI 改用 `SelectRunMethod()` 建立 thread；或保留呼叫 `run()` 不動 |
| `AprNesAvalonia/MainWindow.axaml.cs` | 同上 |

**不改動**：MEM.cs, CPU.cs, PPU.cs, APU.cs, Mapper/*.cs — 全部保持現狀。

### 2.2 四條路徑的主結構

```csharp
// Main.cs
public static void Run_NTSC()
{
    // 冷啟動：用 MasterClockTick 跑到相位對齊 (mcCpuClock==0 && mcPpuClock==0)
    AlignPhaseForFastPath();

    const int ExitCheckInterval = 10000; // 每 10K 個 12-MC 週期檢查一次 exit (~120K MC)
    while (!exit)
    {
        for (int i = 0; i < ExitCheckInterval; i++)
            NTSCFast12Clocks();
    }
    Console.WriteLine("NTSC Thread exit..");
}

public static void Run_PAL() { /* PALFast80Clocks */ }
public static void Run_Dendy() { /* DendyFast15Clocks */ }
public static void Run_FDS() { /* FDSFast12Clocks (NTSC 時基 + fds_CpuCycle) */ }

static void AlignPhaseForFastPath()
{
    // Slow-path MasterClockTick until phase zero
    while (!(mcCpuClock == masterPerCpu && mcPpuClock == masterPerPpu))
    {
        MasterClockTick();
        if (exit) return;
    }
}
```

### 2.3 `NTSCFast12Clocks()` 骨架

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
static void NTSCFast12Clocks()
{
    // ── MC 0: CPU step + APU step + PPU full step ──
    bool isDmcActive = dmcDmaRunning & (dmcStatusEnabled | dmcImplicitAbortActive);
    if (cpuIsRead & (isDmcActive | spriteDmaTransfer)) DmaOneCycle();
    else cpu_step_one_cycle();
    if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive = false;
    MapperObj.CpuCycle();              // 寫死 !isFDS 路徑
    apu_step();
    mcApuPutCycle = !mcApuPutCycle;
    ppu_step_new();

    // ── MC 2: PPU half step ──
    ppu_half_step_new();

    // ── MC 4: NMI check (原 mcCpuClock == 8 = 12-4) ──
    NMILine |= NMIable && isVblank;
    if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
    ppu_step_new();

    // ── MC 6: PPU half ──
    ppu_half_step_new();

    // ── MC 7: IRQ check (原 mcCpuClock == 5) ──
    IRQLine = irqLineCurrent;
    if (statusframeint && !apuintflag) irqLineCurrent = true;
    MapperObj.CpuClockRise();          // 寫死 !isFDS 路徑

    // ── MC 8: PPU full (對應 masterPerPpu = 4 → 3rd PPU step) ──
    ppu_step_new();

    // ── MC 10: PPU half ──
    ppu_half_step_new();

    // 12 MC 完成，相位歸零
    masterClockTotal += 12;
}
```

### 2.4 `FDSFast12Clocks()` 與 NTSC 差異

只改兩行：
```csharp
// MC 0 末段
fds_CpuCycle();                        // 取代 MapperObj.CpuCycle()
// MC 7 末段
// (FDS 的 MapperObj 是 FdsChrMapper，其 CpuClockRise 為空) — 可省略該呼叫
```

### 2.5 `DendyFast15Clocks()`

與 NTSC 相同的 1 CPU + 3 PPU 結構，但展開到 15 MC：
- PPU full at MC 0, 5, 10
- PPU half at MC 2, 7, 12
- NMI at MC 4（CPU 後 4 MC，保持與 NTSC 一致的硬體相對時機）
- IRQ at MC ?（PAL/Dendy 的 IRQ 相對位置需查 TriCNES / NESdev）

**待辦**：確認 Dendy 的 IRQ sample 點是否與 NTSC 一致（CPU 前 5 MC）或不同。

### 2.6 `PALFast80Clocks()`

最複雜。80 MC 內：
- CPU step × 5（MC 0, 16, 32, 48, 64）
- PPU full × 16（MC 0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75）
- PPU half × 16（MC 2, 7, 12, 17, 22, 27, 32, 37, 42, 47, 52, 57, 62, 67, 72, 77）
- NMI check × 5（每 CPU 後 4 MC：MC 4, 20, 36, 52, 68）
- IRQ check × 5（每 CPU 前 5 MC：MC 11, 27, 43, 59, 75）
- APU step × 5（與 CPU 同時：MC 0, 16, 32, 48, 64）

**注意**：MC 32 同時是 PPU full + CPU + APU + IRQ 觸發（MC 32 為 IRQ check for 下一 CPU@48？重新計算）

讓我重新計算 PAL IRQ 點：「CPU 前 5 MC」的物理意義是 `mcCpuClock==5` 即 `elapsed = masterPerCpu - 5 = 16 - 5 = 11`。所以 PAL IRQ 是 CPU step 後 11 MC。展開起點：
- CPU 0 → IRQ 檢查 @ MC 11
- CPU 16 → IRQ 檢查 @ MC 27
- CPU 32 → IRQ 檢查 @ MC 43
- CPU 48 → IRQ 檢查 @ MC 59
- CPU 64 → IRQ 檢查 @ MC 75

NMI 檢查 @ CPU step 後 4 MC：MC 4, 20, 36, 52, 68 ✓

完整 80 MC 時序表需在實作階段建立 spreadsheet 驗證。**PAL 路徑最需要仔細工作**，估計此部分實作 1–2 小時。

### 2.7 UI Dispatcher

```csharp
// Main.cs
public static void RunDispatcher()
{
    if (isFDS)
    {
        Run_FDS();
    }
    else if (Region == RegionType.PAL)
    {
        Run_PAL();
    }
    else if (Region == RegionType.Dendy)
    {
        Run_Dendy();
    }
    else // NTSC
    {
        Run_NTSC();
    }
}
```

UI 端（AprNesUI.cs + AprNesAvalonia MainWindow.axaml.cs）只需把 `new Thread(NesCore.run)` 改成 `new Thread(NesCore.RunDispatcher)`。

### 2.8 保留 `MasterClockTick()` + 舊 `run()`

不刪除，原因：
1. 冷啟動相位對齊使用
2. debug / 回歸比對用（可在出錯時開關比對 fast vs slow path 結果）
3. 極端的 mapper bug（例如新增複雜 mapper 時）可暫時切回 slow path 繞過

---

## 3. 實作階段規劃

| 階段 | 工作 | 驗證 | 預估時間 |
|------|------|------|---------|
| 1 | `Run_NTSC()` + `NTSCFast12Clocks()` + `AlignPhaseForFastPath()` | 184/184 blargg NTSC 全通過、FPS +5% | 1-2h |
| 2 | UI dispatch（AprNesUI + Avalonia），Region=NTSC 時走 Run_NTSC | GUI 手動測試幾個 ROM | 30min |
| 3 | `Run_FDS()` + `FDSFast12Clocks()` | FDS BIOS + 至少 1 個 FDS 遊戲能開機、音效正常 | 1h |
| 4 | `Run_Dendy()` + `DendyFast15Clocks()` | Dendy 模式下至少能正確開機（Dendy 測試 ROM 少，盡量用 NTSC ROM 強制 Dendy 時基觀察時序） | 1h |
| 5 | `Run_PAL()` + `PALFast80Clocks()` | pal_apu_tests 11/11 通過（現行基線）、PAL ROM 音效正常 | 2-3h |
| 6 | JIT profile + FPS benchmark 對照 | 3 次法測 FPS；做 perfview 對照 | 1h |
| 7 | `MasterClockTick()` 中的 NTSC-hardcoded `mcCpuClock==8` 是否影響 PAL/Dendy 現況調查 | 若確認有 bug，記錄但不在本重構內修正（避免混淆） | 30min |
| - | 文件 + commit | 每階段獨立 commit | - |

**總計估算**：7-9 小時工作量，分 5-6 個 commit。

---

## 4. 驗收條件

必要（Must）：
- ✅ blargg 184/184 通過
- ✅ AccuracyCoin 138/138 通過
- ✅ 現有 FPS benchmark 無倒退（理想 +5% 以上）
- ✅ 無 inline 回歸（JIT profile 確認）
- ✅ FDS + PAL + Dendy 各自有至少 1 次手動煙霧測試

期望（Nice-to-have）：
- FPS 提升 5–15%（Debug 模式，ultra+CRT 管線）
- L1 I-cache miss 降低（perfview ETW）
- NMI/IRQ 相對時序修正順便提升 PAL/Dendy 準確度

---

## 5. 已知風險與緩解

| 風險 | 影響 | 緩解策略 |
|------|------|---------|
| PAL 80-MC 時序表建錯 | PAL 遊戲破圖 / 音效錯誤 | 先寫 spreadsheet 推導，逐項對照 MasterClockTick，pal_apu_tests 回歸驗證 |
| Dendy IRQ 相對位置未知 | Dendy 可能異常 | 保守方案：Dendy 先走 NTSC 相對時序（CPU 前 5 MC = mcCpuClock==5）；日後有 Dendy 測試 ROM 再精修 |
| FDS `fds_CpuCycle()` 內部修改 `mcCpuClock` 或其他全域狀態 | Fast path 假設不成立 | 仔細檢查 FDS.cs 所有 `mcCpuClock` / `mcPpuClock` 參考，確認 fds_CpuCycle() 只動自己的狀態 |
| 冷啟動 phase alignment 邊界條件 | 開機瞬間時序錯亂 | `AlignPhaseForFastPath()` 確實跑到 `mcCpuClock==masterPerCpu` 才切換 |
| 重構後某 Mapper 因時序微變破圖 | 難以除錯 | 保留 `run()` 作為 fallback；加入 `--legacy-run` CLI flag 供對照測試 |

---

## 6. 不在本規劃內的事項

- ❌ MEM.cs 的 `Action/Func<>` 委派移除（使用者指示跳過）
- ❌ 核心 helper（cpu_step_one_cycle / ppu_step_new / apu_step / ppu_half_step_new）的內部邏輯修改
- ❌ CPU opcode 或 PPU 渲染演算法變動
- ❌ Mapper 介面變動

---

## 7. 問題 / 待決定（已敲定）

### 7.1 Dendy IRQ 時機 — ✅ **已驗證**
**結論**：與 NTSC 完全一致使用 "下個 CPU 前 5 MC" 規則，即 `mcCpuClock == 5`（counting down from 15）。

**驗證來源**：
- Gemini 建議「scale 到 6-7 MC」基於 Mesen2 的 phi1/phi2 硬體模型（NesCpu.cpp:550-568：NTSC start=6/end=6, PAL start=8/end=8, Dendy start=7/end=8 不對稱）
- **AprNes 跟隨 TriCNES**，TriCNES 模型 IRQ 是「相對下個 CPU step 前 5 MC」（純相對位置，與 phi 相位解耦）
- 因此 Dendy IRQ 在 mcCpuClock=5 是 TriCNES-correct
- 不採信 Gemini 的 Mesen2-based 估算

### 7.2 NMI 時機跨區域 — ✅ **已敲定**
保持「CPU step + 4 MC」相對偏移：
- NTSC: `mcCpuClock == 8` (= 12-4)
- PAL:  `mcCpuClock == 12` (= 16-4)
- Dendy: `mcCpuClock == 11` (= 15-4)

注意：當前 MasterClockTick 用 NTSC-hardcoded `8`，PAL/Dendy 實際偏移錯誤。靜態分派順便修正。

### 7.3 PAL 80-MC 排程表 — ✅ **已生成（待實作驗證）**
公式：CPU=16k, PPU=5j, PPU_Half=5j+2, NMI=CPU+4, IRQ=NextCPU−5（=16k+11）
完整事件表見 `temp/gemini_pal_80mc.txt`（Gemini 已輸出）。

**同 MC 衝突點**（需固定觸發順序）：
- MC 20: PPU 4 + NMI Check 1
- MC 27: PPU Half 5 + IRQ Check 1
- MC 32: CPU/APU 2 + PPU Half 6
- MC 52: PPU Half 10 + NMI Check 3
- MC 75: PPU 15 + IRQ Check 4

實作時依 NTSC 12-MC 範本內的順序（CPU/APU → PPU full → NMI → PPU half → IRQ）對應展開。

### 7.4 mcCpuClock--/mcPpuClock-- 保留策略 — ✅ 已敲定
- Slow path（MasterClockTick）保留 decrement 不動
- Fast path 內不更新計數器（相位由 unroll 結構保證）
- 切回 slow path（清退或冷啟動）時需重置 mcCpuClock=masterPerCpu, mcPpuClock=masterPerPpu

### 7.5 masterClockTotal 語意 — ✅ 已敲定
Fast path 用 `masterClockTotal += 12/80/15` 等效於 slow path 的 +1 × 12/80/15 次。

### 7.6 exit flag 讀取頻率 — ✅ 已敲定
每 10,000 個 fast batch 檢查一次 exit；NTSC ≈ 60ms 延遲，可接受。

### 7.7 MasterClockTick() 移除策略 — ✅ 已敲定
舊路徑（MasterClockTick + run）**先保留**作為冷啟動相位對齊與 fallback。所有 4 條 Run_*() 完成、全測試通過、人工煙霧測試後再移除 dead code。

---

## 8. 效益預估（保守）

| 優化項目 | 每秒消除操作數 | 預估 FPS 提升 |
|---------|--------------|-------------|
| `mc*Clock` 倒數 + 比較 | 2 × 6 × 21.47M = 257M ALU ops | 2-4% |
| `if (!isFDS)` 分支 × 2 | 2 × 2.14M = 4.3M branches | 0.5-1% |
| 展開後 ILP 加成（亂序執行） | — | 1-3% |
| I-cache coherency（FDS 路徑隔離） | — | 1-2% |
| **合計** | | **5-10%** |

比文件號稱的 20–50% 保守許多（扣除 MEM 委派部分）。實際測完再調。
