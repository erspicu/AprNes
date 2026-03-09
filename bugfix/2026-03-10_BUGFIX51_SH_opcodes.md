# BUGFIX51: SH* Unofficial Opcodes Fix (126→131/136 AC, +5)

**日期**: 2026-03-10 (commit 3a3d728)
**影響**: AccuracyCoin 126→131 (+5), blargg 174/174 不變

## 問題

SHA ($93), SHX ($9E), SHY ($9C), SHS ($9B) 等 SH* unofficial opcodes
在 DMA 發生於 write cycle 前時，應消除 H (high byte) 的 AND masking。
原實作缺少此行為，導致 P10 全 6 項 SH* 相關測試失敗。

## 修復

在 `ProcessPendingDma` 入口偵測 SH* opcodes 的關鍵 cycle：

```csharp
if ((opcode == 0x93 && operationCycle == 4) ||
    (opcode == 0x9B && operationCycle == 3) ||
    (opcode == 0x9C && operationCycle == 3) ||
    (opcode == 0x9E && operationCycle == 3) ||
    (opcode == 0x9F && operationCycle == 3))
{
    ignoreH = true;
}
```

當 `ignoreH = true` 時，SH* write 使用 `H = 0xFF`（等同消除 H masking）。
參考 TriCNES 的 `IgnoreH` 機制。

## 修復的 AccuracyCoin 測試

- P10 全 6 項 SH* 測試（SHA, SHX, SHY, SHS 各 variant）

## 修改檔案

- `AprNes/NesCore/CPU.cs` — ignoreH flag, SH* opcode write 邏輯
- `AprNes/NesCore/MEM.cs` — ProcessPendingDma 入口偵測
