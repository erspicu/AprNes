# read_write_2007 研究報告

## 測試內容

`dmc_dma_during_read4/read_write_2007.nes` 測試 **PPU $2007 (PPUDATA) 的連續 read→write 行為**，特別是 `STA $2007,X`（X=0）這種 read-modify-write 指令的 CPU-PPU 時序。

### 測試結構
1. 填入 VRAM 模式：`$11 $22 $33 $44 $55 $66 $77`
2. Test Case 1：手動 `LDX $2007` + `STA $2007`（分離的 read 和 write）
3. Test Case 2：`STA $2007,X`（X=0，CPU 在同一指令內先 read 再 write）
4. 兩個 case 預期相同結果：`33 11 22 33 09 55 66 77`
5. CRC 驗證：`$0F877C4B`

### 核心機制：$2007 State Machine
PPU 的 $2007 讀寫需要多個 PPU cycle 才能完成（state 0→1→3→4→8→9）。當 read 和 write 在 1-2 cycle 內連續發生時，state machine 出現特殊的 "interrupted read-to-write" 行為，包括 mystery write、delayed buffer update、early VRAM address increment 等。

## TriCNES 為何失敗

TriCNES 作者在 `Emulator.cs` line 1333 **明確承認** $2007 state machine 有 bug：

```csharp
// TODO: Something is going wrong with the timing of STA $2007, X (where X = 0). 
// Gotta figure that out, and probably re-do this entire function. 
// I have no idea how inaccurate this is.
```

這正是 `read_write_2007` 測試的指令模式！TriCNES 將此列為已知限制，錯誤碼 `66F9FCAB`（與 AprNes 相同）。

## 硬體修訂版差異

AccuracyCoin README 明確指出：
> "This ROM was designed for an NTSC console with an **RP2A03G CPU and RP2C02G PPU**. Some tests might fail on hardware with a different revision."

同系列其他測試有 **多個有效 CRC**（不同 CPU-PPU alignment 產生不同結果）：
- `dma_2007_read`：2 個有效 CRC
- `double_2007_read`：4 個有效 CRC
- `read_write_2007`：只有 1 個有效 CRC（`$0F877C4B`）

但這不代表不同修訂版硬體無法通過 — 只是 alignment 不同可能需要不同的 state machine timing。

## AprNes 現況分析

| Branch | Blargg | AC | read_write_2007 |
|--------|--------|-----|-----------------|
| **master**（PPU 移植前） | 174/174 | 136/136 | PASS |
| **feature/fetch-port**（TriCNES PPU 移植後） | 173/174 | 136/136 | FAIL |

**關鍵事實**：master 分支 **已經同時通過 174/174 + 136/136**。`read_write_2007` 是在 TriCNES PPU 移植過程中回歸的 — 因為 $2007 state machine 被改寫成 TriCNES 模型（含其已知 bug）。

## 可行方案

### 方案 A：恢復 master 的 $2007 state machine（推薦）
master 的 `ppu_r_2007` / `ppu_w_2007` 已正確處理 `STA $2007,X` 時序。將 master 版本的 $2007 handler 合併回 feature branch，保留其他 TriCNES 移植成果。

- **優點**：已驗證可同時通過 174 + 136
- **風險**：低（$2007 handler 相對獨立）
- **工作量**：小（cherry-pick 或 diff merge）

### 方案 B：修復 TriCNES 模型的 $2007 bug
分析 TriCNES state machine 中 `STA $2007,X` 的具體錯誤，在 AprNes 的 TriCNES 移植版本上修正。

- **優點**：保持完整的 TriCNES 架構一致性
- **風險**：中（需要精確理解 state machine timing，TriCNES 作者自己也沒解決）
- **工作量**：大（需要對照硬體行為逐 state 驗證）

### 方案 C：混合方案
保留 TriCNES 的整體 PPU pipeline，但 $2007 read/write handler 使用 master 的實作。兩者的接口（vram_addr increment、buffer update）是相容的。

- **優點**：取兩者之長
- **風險**：低（handler 接口明確）
- **工作量**：小

## 結論

`read_write_2007` 的失敗不是「不同硬體修訂版」的問題，而是 **TriCNES $2007 state machine 的已知 bug** 被移植到了 AprNes。master 分支已證明 174/174 + 136/136 是可達成的。

**建議**：採用方案 A 或 C，將 master 的 $2007 handler 恢復，即可達成 **174/174 blargg + 136/136 AC** 的完美成績。
