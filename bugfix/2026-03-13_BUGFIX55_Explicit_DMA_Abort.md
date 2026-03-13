# BUGFIX55: P13 Explicit DMA Abort (2026-03-13)

## 問題
AccuracyCoin P13 "Explicit DMA Abort" 測試 FAIL (err=2)。
測試驗證：當 DMC DMA 正在進行時，寫 $4015=#$00 (disable) 的 deferred status delay 在 timer fire boundary 附近需要延長。

## 根因分析

### 1. Explicit abort 只偵測到 "剛觸發" 的情況
原本條件 `dmctimer == dmcrate` 只抓到 timer 剛 fire（reload 到 rate）的 cycle。
但 TriCNES 覆蓋 **兩個** cycle 的 fire window：
- `timer == Rate && PutCycle`（剛 reload）
- `timer == 2 && !PutCycle`（即將 fire）

在 AprNes 中（clockdmc 每 cycle decrement 1，且在 CPU write 前已執行）：
- `dmctimer == dmcrate`：timer 本 cycle 剛 fire 並 reload
- `dmctimer == 1`：timer 下個 cycle 將 fire

### 2. Normal delay 未考慮 timer/deferred 同 cycle 衝突
當 `dmctimer == delay` 時，timer fire 和 deferred status 會在同一 cycle 觸發。
clockdmc 中 timer fire（DMA trigger）在 deferred status 之前執行，
但同 cycle 的 dmcStopTransfer() 會立即取消剛啟動的 DMA。

需要 parity-dependent delay 避免衝突：getCycle=true 時 delay=4，getCycle=false 時 delay=3。

## 修復 (APU.cs)

### 改動 1: Normal delay 改為 parity-dependent
```csharp
// Before
dmcStatusDelay = 3;
// After
dmcStatusDelay = getCycle ? 4 : 3;
```

### 改動 2: Explicit abort 覆蓋 2-cycle fire window
```csharp
// Before: 只偵測 timer==dmcrate
bool timerJustFired = (dmctimer == dmcrate);
if (timerJustFired) dmcStatusDelay = 5;

// After: 覆蓋 "剛觸發" 和 "即將觸發" 兩種情況
if (dmctimer == dmcrate)
    dmcStatusDelay = 4;  // 剛 fire (TriCNES Rate&&PUT)
else if (dmctimer == 1)
    dmcStatusDelay = 5;  // 即將 fire (TriCNES 2&&GET)
```

## 測試結果
- Explicit DMA Abort answer key 完全匹配：
  `$04,$04,$04,$04,$04,$04,$03,$04,$01,$01,$00,$00,$00,$00,$00,$00`
- blargg: 174/174 PASS（無回歸）
- AccuracyCoin: 134→135/136（+1）

## 基線
- **174 PASS / 0 FAIL / 174 TOTAL**
- **AccuracyCoin: 135/136 PASS**（剩餘 1 FAIL: P13 Implicit DMA Abort）
