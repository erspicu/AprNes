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

### 修正方向
**方向 1（推薦）**: 調整舊 SM 的 buffer 更新時機
- 目前 Process2007StateMachine state 1 做部分 buffer 更新
- state 4 做完整 buffer 更新 + v increment
- 需要確認 state 4 是否對應 t4（4 half-cycles after M2 low）
- 檢查 bufferLate flag 是否正確處理 alignment 差異

**方向 2（大工程）**: 移植 D-latch 管線
- 之前嘗試失敗（30 FAIL — Phase1 干擾 rendering）
- 需要先完成 Phase B（rendering fetch 走 bus 模型）
- 工程量大，但是最終正確的做法

### 下一步
1. 讀 Process2007StateMachine 的 state 1 和 state 4
2. 對照 AC test 的 D-latch timeline 表
3. 確認 buffer 更新是否在 t4（4 half-cycles after read end）
4. 如果差 1 half-cycle，調整 state timing

### 關鍵檔案
- `ppu_new.cs` line 648: `Process2007StateMachine()`
- `PPU.cs` line 1038: `ppu_r_2007()` (舊 handler)
- `ref/tricnes_md/emulator-core-diff-analysis.md`: 完整差異分析
- `MD/tricnes-v2/2007_stress_test_analysis.md`: test 詳細分析
- AC ROM source line 2518-3010: test 邏輯和答案
