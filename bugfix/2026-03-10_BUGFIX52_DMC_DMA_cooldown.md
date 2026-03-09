# BUGFIX52: DMC DMA Cooldown (131→132/136 AC, +1)

**日期**: 2026-03-10 (commit 5af6fdb)
**影響**: AccuracyCoin 131→132 (+1), blargg 174/174 不變

## 問題

DMC DMA 完成後立即允許下一次 DMC DMA 觸發，
但真實硬體有 cooldown 期間防止連續 DMA。
缺少此機制導致 P13 DMC DMA + OAM DMA 測試的 stolen cycle 計數不對。

## 修復

參考 TriCNES 的 `CannotRunDMCDMARightNow` 機制：

1. 在 `ProcessPendingDma` DMC fetch 完成後設 `dmcDmaCooldown = 2`
2. 在 `clockdmc()` 每 APU cycle 遞減 cooldown
3. DMA 觸發條件加入 `dmcDmaCooldown != 2` 檢查

```csharp
// ProcessPendingDma: DMC fetch 完成後
dmcDmaCooldown = 2;

// clockdmc(): 遞減
if (dmcDmaCooldown > 0) dmcDmaCooldown--;

// DMA 觸發條件
if (dmcBufferEmpty && dmcsamplesleft > 0)
{
    if (dmcDmaCooldown != 2) // block if just finished DMA
        dmcStartTransfer();
}
```

## 修復的 AccuracyCoin 測試

- P13 DMC DMA + OAM DMA: stolen cycle 計數正確

## 修改檔案

- `AprNes/NesCore/APU.cs` — dmcDmaCooldown 欄位、clockdmc() 遞減邏輯
- `AprNes/NesCore/MEM.cs` — ProcessPendingDma 設 cooldown
