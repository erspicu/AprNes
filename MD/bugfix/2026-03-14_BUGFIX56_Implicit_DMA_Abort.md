# BUGFIX56: P13 Implicit DMA Abort (2026-03-14)

## 問題
AccuracyCoin P13 "Implicit DMA Abort" 測試 FAIL (err=2)。
測試驗證：當 1-byte non-looping DMC sample 即將結束時寫入 $4015=$10 (enable)，
觸發「幽靈」1-cycle DMA，此 DMA 若遇到 write cycle 會被完全取消。

## 根因分析

### Implicit abort 機制（TriCNES 參考）

TriCNES (line 9403) 在 $4015 write 時偵測 timer 接近 fire：
- `APU_ChannelTimer_DMC == 10 && !APU_PutCycle`
- `APU_ChannelTimer_DMC == 8 && APU_PutCycle`

設定 `APU_SetImplicitAbortDMC4015 = true`。Timer fire 時轉為
`APU_ImplicitAbortDMC4015 = true`，觸發 1-cycle phantom DMA。

### Timer 值映射

TriCNES DMC timer 每 GET cycle 遞減 2（值恆為偶數），AprNes 每 cycle 遞減 1。
經驗證確認存在 +3 position offset（pending→active 轉換延遲，bits counter 8 次 fire）。

AprNes 對應偵測條件：
- `dmctimer == 8 && !getCycle`（對應 TriCNES timer==10 && !PutCycle）
- `dmctimer == 9 && getCycle`（對應 TriCNES timer==8 && PutCycle）

### 1-cycle phantom DMA

TriCNES (line 8758-8761) 在每個 CPU cycle 結束後清除 implicit abort flag：
```
if (DoDMCDMA && APU_ImplicitAbortDMC4015)
    APU_ImplicitAbortDMC4015 = false;
```
下一 cycle 的 DMA gate 因 flag 已清失敗，形成僅 1 cycle（halt only）的 phantom DMA。

### Write cycle 取消

TriCNES (line 3974) DMA gate 要求 `CPU_Read`（GET cycle）：
```
DoDMCDMA && (APU_Status_DMC || APU_ImplicitAbortDMC4015) && CPU_Read
```
若 CPU 正在執行 write cycle（如 STA），implicit abort DMA 直接不執行。

## 修復

### 改動 1: APU.cs — Implicit abort 偵測條件

```csharp
// $4015 write 時偵測 timer 接近 fire
if ((dmctimer == 8 && !getCycle) || (dmctimer == 9 && getCycle))
{
    dmcImplicitAbortPending = true;
}
```

### 改動 2: APU.cs — Timer fire 時 pending→active 轉換

clockdmc 中 buffer 清空且 bits counter 歸零時：
```csharp
if (dmcImplicitAbortPending)
{
    dmcImplicitAbortActive = true;
    dmcImplicitAbortPending = false;
}
```

### 改動 3: MEM.cs — Post-halt 1-cycle phantom DMA 取消

ProcessPendingDma 中 halt cycle 執行後：
```csharp
if (dmcImplicitAbortActive)
{
    dmcImplicitAbortActive = false;
    if (dmcDmaRunning && dmcsamplesleft == 0)
    {
        dmcDmaRunning = false;
        dmcNeedDummyRead = false;
        if (!spriteDmaTransfer) return;
    }
}
```
`dmcsamplesleft == 0` 確保只在純 implicit abort DMA 時取消，不影響正常 refill DMA。

### 改動 4: CPU.cs — Write cycle 取消 implicit abort DMA

CpuWrite 中，若 implicit abort active 且 DMA 等待 halt：
```csharp
if (dmcImplicitAbortActive && dmaNeedHalt)
{
    dmcImplicitAbortActive = false;
    dmcDmaRunning = false;
    dmcNeedDummyRead = false;
    dmaNeedHalt = false;
}
```

## 測試結果
- Implicit DMA Abort pre-1990 answer key 完全匹配
- blargg: 174/174 PASS（無回歸）
- AccuracyCoin: 135→136/136（+1，PERFECT）

## 基線
- **174 PASS / 0 FAIL / 174 TOTAL**
- **AccuracyCoin: 136/136 PASS**（全部通過）
