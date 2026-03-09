# BUGFIX50: Per-cycle CPU Rewrite (122→126/136 AC, +4)

**日期**: 2026-03-10 (commit 533d1d4)
**影響**: AccuracyCoin 122→126 (+4), blargg 174/174 不變

## 問題

舊 CPU 模型一次性執行整條指令的所有 cycles，DMA 只能在指令邊界插入。
這導致 DMA stolen cycle 時序不精確，多個 AccuracyCoin 測試失敗。

## 修復

將 CPU 從「每指令一次性執行」改為「每 cycle 獨立步進」模型：

- 新增 `cpu_step_one_cycle()` 函數，每次只執行一個 CPU cycle
- 每個 cycle 獨立的 `StartCpuCycle → bus op → EndCpuCycle`
- DMA (`ProcessPendingDma`) 可在任意 read cycle 邊界插入
- 使用 `operationCycle` 狀態機追蹤指令執行進度
- `CpuRead`/`CpuWrite` 取代原有的 `Mem_r`/`Mem_w`

## 修復的 AccuracyCoin 測試

- P12 IFlagLatency: Test E 不再 hang（DMC DMA cycle drift 消除）
- P20 Instruction Timing + Implied Dummy Reads
- P13 DMA + $4015 Read / DMC DMA + OAM DMA

## 修改檔案

- `AprNes/NesCore/CPU.cs` — 全面重寫（per-cycle state machine）
- `AprNes/NesCore/MEM.cs` — StartCpuCycle/EndCpuCycle/ProcessPendingDma
- `AprNes/NesCore/Main.cs` — run() loop 改用 cpu_step()
