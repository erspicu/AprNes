# BUGFIX53: DMC Load DMA Countdown Timing (TriCNES-style)

**日期**: 2026-03-13
**基線**: 174/174 blargg + 132/136 AC
**結果**: 174/174 blargg + 133/136 AC (+1)
**修復項目**: P13 "DMA + $2002 Read"

---

## 問題描述

AccuracyCoin P13 "DMA + $2002 Read" 測試失敗 (err=2)。
測試在 VBL 期間觸發 DMC DMA，期望 DMA halt cycle 讀取 $2002 清除 VBL flag。
但 AprNes 的 DMA 觸發時間早了 1 個 cycle，導致 halt cycle 讀取的是 ROM 地址而非 $2002。

## 根因分析

### TriCNES vs AprNes 的 APU/CPU 執行順序差異

| | TriCNES | AprNes |
|--|---------|--------|
| APU/CPU 順序 | CPU 先，APU 後（同一 tick） | APU 先（StartCpuCycle），CPU 後 |
| DMCDMADelay | 固定 2，僅 PUT cycle 遞減 | 2 或 3（parity-dependent），每 cycle 遞減 |
| APU 設 flag | 本 tick 的 APU 設的 flag，CPU 下一 tick 才看到 | 本 tick 的 APU 設的 flag，CPU 同 tick 立刻看到 |

### 遞減時機的影響

TriCNES 在 PUT cycle 遞減 DMCDMADelay。由於 APU 在 CPU 之後執行，flag 延遲 1 tick 生效：
- 寫入在 GET cycle → 實際延遲 4 cycles
- 寫入在 PUT cycle → 實際延遲 3 cycles

AprNes 的 APU 在 CPU 之前執行（StartCpuCycle），flag 同 tick 生效。
若直接用 PUT-cycle 遞減，結果會反轉（GET→3, PUT→4）。

### 解法：反轉遞減 parity

在 AprNes 中用 **GET cycle 遞減**（而非 PUT），可抵消 APU/CPU 順序差異：
- 寫入在 GET cycle → 下一 tick 是 PUT（不遞減）→ 再下一 tick GET 遞減 → ... → 4 cycles ✓
- 寫入在 PUT cycle → 下一 tick 是 GET（遞減）→ ... → 3 cycles ✓

## 修改內容

### APU.cs

1. **apu_4015()**: dmcLoadDmaCountdown 改為固定 2（TriCNES: `DMCDMADelay = 2`）
   ```csharp
   // 舊: dmcLoadDmaCountdown = (apucycle & 1) != 0 ? 2 : 3;
   dmcLoadDmaCountdown = 2;
   ```

2. **apu_step()**: dmcLoadDmaCountdown 只在 GET cycle 遞減
   ```csharp
   if (dmcLoadDmaCountdown > 0)
   {
       bool getCycle = (cpuCycleCount & 1) == 0;
       if (getCycle)
       {
           --dmcLoadDmaCountdown;
           if (dmcLoadDmaCountdown == 0 && dmcBufferEmpty && dmcsamplesleft > 0)
               dmcStartTransfer();
       }
       return;
   }
   ```

## 驗證結果

| 測試 | 結果 |
|------|------|
| blargg 174 | 174/174 PASS (無回歸) |
| AC P13 DMA + $2002 Read | PASS (原 FAIL err=2) |
| AC 總分 | 133/136 (+1) |

## 參考

- TriCNES: `Emulator.cs` line 9384 (`DMCDMADelay = 2`)
- TriCNES: `Emulator.cs` lines 971-980 (DMCDMADelay decrement in PUT branch)
- TriCNES: `Emulator.cs` lines 652-715 (CPU→APU execution order)
