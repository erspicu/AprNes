# TriCNES v2 移植 — Session 狀態

**Branch**: `feature/tricnes-v2-port`
**最新 commit**: `bbed3ad`
**基線**: 184/184 blargg PASS

## AC test v2 進度 (138 項)

| 項目 | 狀態 | commit | 修正內容 |
|------|------|--------|---------|
| P16 Palette RAM Quirks | ✅ PASS | `4fda362` | greyscale mode palette read mask (& 0x30) |
| P19 Stale Sprite Shift Regs | ✅ PASS | `d69cfb3` | dot 339 counter 不動 + sprite fetch 設 counter |
| P19 $2007 Stress Test | **FAIL 1** | — | 需要調整 buffer 更新 timing |

**目前成績**: 137/138（剩 1 項）

## 最後一項 FAIL 分析

**P19 $2007 Stress Test FAIL 1**

### 測試做什麼
- 在 visible scanline 每個 dot 讀 $2007
- 記錄 buffer 值
- 和 rendering fetch 的已知結果比對（只比穩定 byte）

### 硬體要求
Buffer 更新在 CPU read 結束後 **4 PPU half-cycles（2 PPU dots）**：
```
t0: CPU read ends (M2 low)
t2: ALE — v 放上 address bus + octal latch
t4: Read — bus data 放入 buffer
```

### 已嘗試的修正（失敗）
- 將 state 1 buffer update 移到 state 2：仍然 FAIL 1，184/184 無回歸但 P19 沒改善

### 根因分析
Process2007StateMachine 在 ppu_step_new **dot 開頭**（Phase 2 deferred updates 內）呼叫。
Buffer refill 和 v increment 都在這一次呼叫內一口氣完成。

但硬體上 D-latch 管線的效果是：
- dot 開頭: 信號建立（ALE）
- **dot 中段（rendering 後）**: buffer refill
- **half step**: v increment + 實際寫入

buffer 在 dot 開頭更新 vs rendering fetch 在 odd dot 讀取 → **timing 不對齊**。
這不是 SM state 偏移能修的，需要把 buffer refill 分散到 rendering 後或 half-step。

### 修正方向
**方向 1 結論**: 舊 SM 架構無法正確處理，因為整個 SM 在 dot 開頭一次跑完，
無法模擬 buffer refill 在 mid-dot 發生的硬體行為。

**方向 2（必要）**: 需要至少拆分 SM 的 buffer refill 到 mid-dot 位置。
不一定要完整 D-latch 管線，但至少需要：
1. SM 的 buffer refill 部分移到 rendering 之後（Phase5 之後）
2. 或加一個 flag 在 SM 中標記，在 rendering 後或 half-step 中執行 refill

### 下一步
### 已嘗試的方向 2 最小改動（失敗）
- Deferred refill flag：SM state 1 設 flag，rendering 之後執行 refill → 仍 FAIL 1
- 原因：refill 用 `PpuBusRead(ppu2007SM_addr)` 讀的是 vram_addr 指向的資料
- 但硬體上 SM Read 和 rendering fetch Read **共用同一條 bus**
- Buffer 應該得到 **rendering fetch 正在讀的 tile/sprite 資料**
- 這需要完整的 bus 模型（OctalLatch + FetchPPU）才能正確實現

### 最終結論
$2007 Stress Test 需要**方向 2（完整 bus 模型移植）**。方向 1 已確認不可行：
1. SM state 偏移（state 1→2）：無效
2. Deferred refill 到 mid-dot：無效（地址不對）
3. 根因：buffer 值必須來自 rendering fetch bus，不是 vram_addr

### Log 對比結果（2026-04-10 確認）

AprNes cx=30 buf=02（重複 vram_addr data）
TriCNES cx=30 buf=C0（AT fetch data — rendering bus）

差異：SM Read 在 even dot 觸發時，TriCNES 用 OctalLatch 讀到 rendering fetch
上一次 latch 的地址的資料。AprNes 用 PpuBusRead(vram_addr) 讀到 v 指向的資料。

SM 的 Read 不一定和 rendering fetch 的 Read 在同一 dot（可以在 even dot 讀）。
lastBusValue 方案不夠，因為 even dot 沒有 rendering fetch read。

### 最終結論
137/138 是舊 SM + 直接 PpuBusRead 架構的硬天花板。
138/138 需要 OctalLatch bus 模型：SM Read 用 (AddressBus high | OctalLatch) 讀取，
和 rendering fetch 共用同一條 multiplexed address bus。

下一步：實作 OctalLatch — 最小改動版本，不需要完整 FetchPPU 重寫

### 關鍵檔案
- `ppu_new.cs` line 648: `Process2007StateMachine()`
- `PPU.cs` line 1038: `ppu_r_2007()` (舊 handler)
- `ref/tricnes_md/emulator-core-diff-analysis.md`: 完整差異分析
- `MD/tricnes-v2/2007_stress_test_analysis.md`: test 詳細分析
- AC ROM source line 2518-3010: test 邏輯和答案
