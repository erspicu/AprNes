# Performance Optimization TODO

*建立日期：2026-03-15*
*基線：Debug 242 FPS / Release 241 FPS / .NET 10 RyuJIT 326 FPS*
*最新基線（loop unroll + sprite0 range check 後）：Release ~259 FPS*
*測試基線：174/174 blargg PASS，136/136 AccuracyCoin PASS*

參考：`MD/hotspot_analysis.md`

---

## 已完成

### ✅ catchUpPPU / catchUpAPU loop unroll
**實際收益：+4.3% Release（241.45 → 252 FPS）** — 2026-03-15

- while 迴圈改為固定 3 次展開（PPU）+ 直接呼叫（APU）
- 每 CPU cycle 省掉 4 次迴圈條件判斷（1.79M × 4 = 716 萬次/秒）
- commit: `3d967e6`

---

## 待辦優化項目

### P1 — RenderBGTile SIMD 8-pixel 寫入
**預估收益：高**

- 目前：8 pixel 展開迴圈，每 pixel 個別寫入 `ScreenBuf1x` 和 `Buffer_BG_array`
- 優化：使用 `Vector128<uint>` / `Vector256<uint>` 一次寫入 4 或 8 pixels
- 需要：.NET 8+ `System.Runtime.Intrinsics`（或已有 `SIMDEnabled` flag 可整合）
- 注意：palette lookup 仍需逐 pixel，僅 store 可 SIMD 化
- 影響範圍：PPU.cs `RenderBGTile()`

---

### P2 — StartCpuCycle / EndCpuCycle local field 快照
**預估收益：中**

- 目前：`masterClock`, `cpuCycleCount`, `m2PhaseIsWrite`, `nmi_pending` 等每次都從 static field 讀寫
- 優化：在函數頂端快照到 local variable，結束前寫回，讓 JIT 有機會 enregister
- 注意：catchUpPPU / catchUpAPU 呼叫期間若有副作用修改同一欄位，需在 call 前後 flush
- 影響範圍：MEM.cs `StartCpuCycle()`, `EndCpuCycle()`

---

### P3 — EndCpuCycle IRQ dirty flag
**預估收益：小～中**

- 目前：每 CPU 週期都重新計算 `irqLineCurrent = (statusframeint && !apuintflag) || statusdmcint || statusmapperint`
- 優化：改為 dirty flag，僅在 APU 寫入（`$4015`, `$4017`）或 mapper IRQ 觸發時更新
- 每秒省下 1.79M 次 4-field OR 運算
- 影響範圍：MEM.cs `EndCpuCycle()`，APU.cs/IO.cs 相關寫入點

---

### ✅ P4 — ppu_step_new Sprite 0 hit range check（條件重排）
**實際收益：+2.8% Release（252 → ~259 FPS）** — 2026-03-15

- 將 `cx >= sprite0_line_x && cx < sprite0_line_x + 8` 提前至第 3 個條件
- 讓 248 個非命中 dot 只做 4 次判斷就跳出，避免 sprCol 計算
- commit: `59be539`

---

### ❌ P2（變體）— ppu_step_new scanline + ppuRenderingEnabled local 快照
**實際收益：負（259 → 252 FPS，-2.7%）** — 2026-03-15

- 新增 `int sl = scanline` + `bool rend = ppuRenderingEnabled` 兩個 local
- 反而增加 register pressure，JIT 生成更差的程式碼
- **結論**：在 .NET Fx 4.6.1 JIT 下，函數已有足夠 local，多加反而有害
- 已 revert，不採用

---

### P5 — ppu_rendering_tick phase 拆成 inlined methods
**預估收益：小**

- 目前：`switch (cx & 7)` 7-way switch，每次都要 dispatch
- 優化：將 8 個 phase 拆成獨立的 `[MethodImpl(AggressiveInlining)]` 函數，或展開為 if/else chain
- 現代 JIT 對已知 modulo 可能已優化為 jump table，實際收益需 benchmark 確認
- 影響範圍：PPU.cs `ppu_rendering_tick()`

---

### P6 — apu_step even-cycle gate 預計算
**預估收益：小**

- 目前：每次 `apu_step()` 都 check `(apucycle & 1) == 0` 來決定是否 clock pulse/noise
- 優化：改為兩個交替的 step 函數（`apu_step_even()` / `apu_step_odd()`），由 caller 依 parity 選擇
- 或：unroll 為 2-step pair，每次執行一次 odd + 一次 even
- 影響範圍：APU.cs `apu_step()`，MEM.cs `catchUpAPU()`

---

## 注意事項

- 每項優化完成後必須驗證：`python run_tests.py -j 10`（174/174）+ `bash run_tests_AccuracyCoin_report.sh`（136/136）
- SIMD 優化需保留 `SIMDEnabled = false` fallback（用於 `--benchmark-simd` 對比模式）
- local field 快照（P2/P3）改動範圍廣，建議先用 `--perf` 20s benchmark 確認收益再 commit
- ProcessPendingDma 的 state machine 正確性敏感，暫不列入優化
