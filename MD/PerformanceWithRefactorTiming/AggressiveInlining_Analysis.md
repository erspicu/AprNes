# AggressiveInlining 使用分析 — PPU.cs + ppu_new.cs

分析日期：2026-04-07

## .NET Framework 4.8.1 JIT Inlining 規則
- 自動 inline 閾值：~32 IL bytes
- `AggressiveInlining` 強制 inline（無視大小限制）
- 過大的 method 被強制 inline → **膨脹呼叫者 IL** → 呼叫者本身無法被 inline → 效能反降
- Register handler（$2000-$2007 read/write）：CPU 存取時才觸發，頻率低
- Per-dot method（每 PPU dot 呼叫一次）：~89,000 次/frame → 真正的 hot path

## 需要移除 AggressiveInlining（過大或低頻呼叫）

| Method | 檔案:行 | 大小 | 呼叫頻率 | 原因 |
|--------|---------|------|----------|------|
| **ppu_step_new()** | ppu_new.cs:28 | **~600 行** | per-dot | 最大問題：膨脹 MasterClockTick，阻止其被優化 |
| **ppu_r_2007()** | PPU.cs:1048 | ~54 行 | $2007 read（低頻） | 大型 register handler，不值得 inline |
| **ppu_w_2001()** | PPU.cs:1132 | ~58 行 | $2001 write（低頻） | 大型 register handler |
| **ppu_w_2007()** | PPU.cs:1267 | ~30 行 | $2007 write（低頻） | register handler |
| **ppu_r_2002()** | PPU.cs:1024 | ~22 行 | $2002 read（低頻） | register handler |
| **ppu_r_2004()** | PPU.cs:1217 | ~20 行 | $2004 read（低頻） | register handler |
| **ComputeSpritePatternAddr()** | PPU.cs:546 | ~35 行 | 每 sprite fetch（中頻） | 過大，讓 JIT 自行判斷 |
| **PpuBusWrite()** | PPU.cs:345 | ~24 行 | $2007 write path（低頻） | 不在 hot path |

## 可考慮移除（邊界案例，需測試驗證）

| Method | 檔案:行 | 大小 | 呼叫頻率 | 備註 |
|--------|---------|------|----------|------|
| **ppu_half_step_new()** | ppu_new.cs:637 | ~63 行 | per-dot | 與 ppu_step_new 配對，大小接近臨界 |

## 應保留 AggressiveInlining（小型 + 高頻）

| Method | 檔案:行 | 大小 | 呼叫頻率 | 原因 |
|--------|---------|------|----------|------|
| **CXinc()** | PPU.cs:290 | 10 行 | per-dot rendering | 小型 scroll helper，hot path |
| **Yinc()** | PPU.cs:303 | 20 行 | per-dot rendering | 小型 scroll helper |
| **CopyHoriV()** | PPU.cs:464 | 4 行 | per-scanline | 極小 |
| **FlipByte()** | PPU.cs:584 | 7 行 | per-sprite fetch | 小型 bit 操作 |
| **SpriteEvalTick()** | PPU.cs:748 | 16 行 | **per-dot eval（~46K/frame）** | 最高頻 hot path |
| **SpriteEvalInit()** | PPU.cs:733 | 10 行 | per-scanline | 小型初始化 |
| **SpriteEvalEnd()** | PPU.cs:900 | 8 行 | per-scanline | 小型結束處理 |
| **CIRAMAddr()** | PPU.cs:475 | 12 行 | tile fetch path | 小型地址轉換 |
| **PpuBusRead()** | PPU.cs:328 | 15 行 | tile fetch path | 小型，在 rendering loop 裡 |
| **Increment2007()** | PPU.cs:372 | 13 行 | $2007 SM | 小型 |
| **ppu_w_2003()** | PPU.cs:1192 | 5 行 | $2003 write | 極小 setter |
| **ppu_w_2004()** | PPU.cs:1199 | 16 行 | $2004 write | 可接受大小 |

## 缺少 AggressiveInlining 但可考慮加入

| Method | 檔案:行 | 大小 | 呼叫頻率 | 備註 |
|--------|---------|------|----------|------|
| **RenderBGTile()** | PPU.cs:524 | 16 行 | 每 8 dots | 小型，rendering inner loop，可加速 tile fetch |

## 正確地沒有 AggressiveInlining

| Method | 大小 | 原因 |
|--------|------|------|
| SpriteEvalWrite() | ~130 行 | 過大，per-dot 但不適合 inline |
| PrecomputeOverflow() | ~39 行 | per-scanline，頻率不夠高 |
| PrecomputePreRenderSprites() | ~54 行 | per-frame，一幀一次 |
| ProcessOamCorruption() | 12 行 | 低頻（rendering toggle 時才觸發） |
| RenderScreen() | 大型 | per-frame |

## 最關鍵的改善

**#1 移除 `ppu_step_new()` 的 AggressiveInlining**
- 這個 600+ 行的 God Method 如果被強制 inline 到 MasterClockTick，會導致 MasterClockTick 的 IL 體積爆炸
- JIT 無法對如此大的方法做有效的暫存器分配和最佳化
- 移除後 MasterClockTick 可以保持小巧，JIT 更容易優化其 loop

**#2 移除所有 register handler 的 AggressiveInlining**
- $2000/$2001/$2002/$2004/$2007 的 read/write handler 只在 CPU 存取時觸發
- 這些 handler 被 inline 到 IO_read/IO_write → 膨脹 IO dispatch
- IO dispatch 是每次 CPU read/write 都經過的路徑，但大部分走 RAM/ROM 分支
- register handler 過大的 inline 會汙染 instruction cache

## 預期效果
- 移除 ppu_step_new 的 inline：MasterClockTick loop 縮小 → JIT 更佳優化 → 可能 +5-15% FPS
- 移除 register handler inline：IO dispatch 縮小 → icache 命中率提升 → 可能 +2-5%
- 加入 RenderBGTile inline：每 8 dot 省一次 call overhead → 微小改善
