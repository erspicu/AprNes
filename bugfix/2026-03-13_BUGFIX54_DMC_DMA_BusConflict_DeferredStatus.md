# BUGFIX54: DMC DMA Bus Conflict + Deferred $4015 Status

**日期**: 2026-03-13
**基線**: 174/174 blargg + 133/136 AC
**結果**: 174/174 blargg + 134/136 AC (+1)
**修復項目**: P13 "DMC DMA Bus Conflicts"

---

## 問題描述

AccuracyCoin P13 "DMC DMA Bus Conflicts" 測試失敗 (err=2)。
測試驗證 DMC DMA 讀取時若 CPU address bus 指向 APU 暫存器空間 ($4000-$401F)，
應產生 bus conflict：APU 暫存器和 ROM 同時回應，且 $4015 讀取不影響 data bus。

## 根因分析

### 1. $4016/$4017 Bus Conflict 順序錯誤

舊實作先讀 controller，再讀 ROM，然後做 AND 合併。
但 TriCNES 的做法是：先讀 ROM 設定 data bus，然後 controller 讀取自然使用 cpubus 作為 open bus bits。

### 2. $4015 Bus Conflict 更新 cpubus

TriCNES 明確指出 "$4015 read can not affect the databus" (line 9084)。
舊實作將 $4015 讀取結果設入 cpubus，導致後續 DMA 讀取看到錯誤的 bus value。

### 3. 缺少 Deferred $4015 Status Update

TriCNES 使用 `APU_DelayedDMC4015` countdown 延遲 $4015 寫入的 DMC enable/disable 效果。
$4015 讀取的 bit 4 也基於 delayed status (`APU_Status_DelayedDMC`)。
AprNes 舊實作是立即生效，未使用 deferred mechanism。

## 修改內容

### MEM.cs — ProcessDmaRead()

1. **$4016/$4017**: 改為先讀 ROM → 設 cpubus → 再讀 controller（controller 自然用 cpubus 做 open bus）
2. **$4015**: 加 `dmaReadSkipBusUpdate` flag，讀取 $4015 不更新 cpubus
3. **caller**: `if (!dmaReadSkipBusUpdate) cpubus = val;`

### APU.cs — Deferred Status

1. **dmcStatusDelay / dmcDelayedEnable**: 新欄位，取代舊 dmcDisableDelay
2. **apu_4015()**: $4015 寫入時設 dmcStatusDelay (getCycle?3:2)，disable 不再立即生效
3. **clockdmc()**: dmcStatusDelay 每 cycle 遞減（包含 DMA 期間），到 0 時套用 enable/disable
4. **apu_r_4015()**: bit 4 使用 `dmcsamplesleft > 0 && dmcDelayedEnable`

## 驗證結果

| 測試 | 結果 |
|------|------|
| blargg 174 | 174/174 PASS (無回歸) |
| AC P13 DMC DMA Bus Conflicts | PASS (原 FAIL err=2) |
| AC 總分 | 134/136 (+1) |

## 參考

- TriCNES: `Emulator.cs` lines 9058-9113 (bus conflict handling in Fetch())
- TriCNES: `Emulator.cs` line 9084 ("$4015 read can not affect the databus")
- TriCNES: `Emulator.cs` lines 983-993 (APU_DelayedDMC4015 decrement every cycle)
- TriCNES: `Emulator.cs` lines 9375 (normal delay: PUT?3:4)
