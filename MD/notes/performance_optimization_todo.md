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
- **結論**：在 .NET Fx 4.8.1 JIT 下，函數已有足夠 local，多加反而有害
- 已 revert，不採用

---

### ❌ P1（變體）— RenderBGTile left-8 masking 提升至 tile 層級（fast-path/slow-path split）
**實際收益：負（259 → 245 FPS，-5.4%）** — 2026-03-15

- 將 `masked = !bgLeft8 && screenX < 8` 從 per-pixel 提升至 per-tile 層級
- 分成 fast-path（無 masking，99% 情況）+ slow-path（含 masking）兩個 loop body
- 反而大幅下降：程式碼體積膨脹，I-cache miss 增加
- **結論**：JIT 已透過 branch prediction 有效消除 masked 判斷，手動分路適得其反
- 已 revert

---

### ❌ P5 — ppu_rendering_tick switch 拆分
**評估結論：跳過（不測試）**

- JIT 已將 `switch (cx & 7)` 編譯為 jump table，無法再改善
- 理論 overhead 可忽略不計

---

### ❌ P6 — apu_step even-cycle gate 分離
**評估結論：跳過（不測試）**

- `(apucycle & 1) == 0` 每次 1 bitwise AND + 1 branch
- 1.79M/sec × ~1 ns = ~1.8 ms/sec 節省，< 0.1%，不可量測

---

### P3 — EndCpuCycle IRQ dirty flag
**預估收益：小～中（風險高）**

- 目前每 CPU 週期重新計算 `irqLineCurrent` 4-field OR 運算式
- 優化：改為 dirty flag，僅在 APU 寫入或 mapper IRQ 時更新
- Mutation sites 多（APU.cs 8+、IO.cs 2、Mapper 2），漏掉一處會造成 IRQ 行為錯誤
- **建議**：除非其他優化已耗盡，否則暫不實作

---

### ❌ PPU tile fetch 快取 — generation-based per-scanline CHR cache
**實際收益：負（259 → ~256 FPS，-1.2%）** — 2026-03-15

- 設計：`tileFetchTag[8192]`（ushort）+ `tileFetchData[8192]`（byte），以 scanline generation 為有效期
- 邏輯：cases 5/7 先查 tag，hit 直接返回 cached byte，miss 才呼叫 MapperR_CHR
- 失敗原因：
  - 24KB 額外陣列（16KB tag + 8KB data）侵佔 L1 cache（32KB），造成其他熱資料的 eviction
  - `MapperR_CHR`（MMC3）本身已很快：1 interface virtual call + 3-4 branch + 1 array access，被 branch predictor 預測良好
  - 即使 cache hit，tag/data 兩次隨機 array access 的 overhead 幾乎等於省下的 MapperR_CHR cost
  - Cache hit rate 不如預期：Mega Man 5 複雜背景，同 scanline 上重複 NTVal 比例不高
- 已 revert

---

### P1（SIMD）— RenderBGTile Vector128 8-pixel 寫入
**評估：中等複雜度，預估收益 < 1%**

- 每次呼叫 8 個間接 palette lookup（bgPixel 0-3 → uint 顏色）是主要成本
- SIMD 僅能加速最後的 store 階段（8 uint → 2 × Vector128 stores）
- Palette lookup 仍是 scalar（gather 需要 AVX2，.NET Fx 4.8.1 可用但複雜）
- **建議**：與其費工實作，.NET 10 RyuJIT 自動向量化已提供 +34% 收益

---

## 注意事項

- 每項優化完成後必須驗證：`python run_tests.py -j 10`（174/174）+ `bash run_tests_AccuracyCoin_report.sh`（136/136）
- SIMD 優化需保留 `SIMDEnabled = false` fallback（用於 `--benchmark-simd` 對比模式）
- **測試時注意 CPU 熱降頻**：連續多次 20s 測試後需休息 30s，否則 FPS 會虛假偏低
- ProcessPendingDma 的 state machine 正確性敏感，暫不列入優化

## 優化收益總結（.NET Fx 4.8.1 Release）

| 優化 | FPS | 累計 |
|------|-----|------|
| 原始基線 | 241 | — |
| catchUpPPU/APU loop unroll | +10.5 | **252** |
| Sprite 0 hit range check | +7.0 | **259** |
| PPU tile fetch 快取 | -1.2%（~256） | revert |
| 其他嘗試（均 revert） | — | — |
| **當前基線** | **~259** | **+7.5%** |
