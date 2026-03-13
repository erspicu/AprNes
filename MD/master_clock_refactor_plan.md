# Master Clock 重構計畫

**目的**: 從 CPU-driven tick 升級到 M2 phase-aware 架構，解決 AccuracyCoin 剩餘 17 FAIL
**日期**: 2026-03-07
**基線**: blargg 174/174, AccuracyCoin 118/136 (87%)
**狀態**: **未採用** — 最終透過 Per-cycle CPU + DMA timing fixes (BUGFIX50-56) 達成 136/136，無需 Master Clock 重構

---

## 現有基礎設施

MEM.cs 已有 Master Clock 基礎：
- `masterClock` / `cpuCycleCount` / `ppuClock` / `apuClock` 計數器
- `catchUpPPU()` / `catchUpAPU()` catch-up loop
- `tick_pre()` / `tick_post()` 分離（tick_post 為空 placeholder）
- `MASTER_PER_CPU = 12`, `MASTER_PER_PPU = 4` 常數

CPU.cs 2395 行（非之前估計的 5000 行），重構風險比預期低。

---

## 重構階段

### Phase 1: M2 Phase Tracking（低風險，高收益）

**目標**: 在每個 CPU cycle 內追蹤 M2 rise/fall phase，讓 DMA 使用真正的 GET/PUT 判斷

**改動**:

1. **MEM.cs** — 新增 `m2Phase` 欄位
   ```csharp
   enum M2Phase { Rise, Fall }  // Rise = GET (read), Fall = PUT (write)
   static M2Phase m2Phase = M2Phase.Rise;
   ```

2. **MEM.cs** — `Mem_r()` 設 `m2Phase = M2Phase.Rise`（GET cycle）
3. **MEM.cs** — `Mem_w()` 設 `m2Phase = M2Phase.Fall`（PUT cycle）
4. **APU.cs** — `dmcfillbuffer()` 用 `m2Phase` 取代 `cpuBusIsWrite` / `cpuCycleCount & 1`
   - Load DMA: `m2Phase == M2Phase.Fall ? 3 : 2`
   - Reload DMA: `m2Phase == M2Phase.Fall ? 2 : 3`

**預期收益**: 基礎設施準備，不改變行為
**回歸風險**: 無（Load DMA 已用相同 parity，Reload DMA/OAM DMA 保留原 proxy）
**驗證**: blargg 174/174 ✓

**實測結果** (2026-03-07):
- ✅ 已完成：新增 `m2PhaseIsWrite` 欄位，Load DMA 使用
- ⚠️ Reload DMA 改用 m2PhaseIsWrite → 回歸 5 測試（dma_2007_read/write, dma_4016_read, sprdma×2）
- ⚠️ OAM DMA alignment 改用 m2PhaseIsWrite → 回歸（同上 + cpu_interrupts）
- 結論：Reload DMA 和 OAM DMA 的現有 proxy（cpuBusIsWrite / apucycle parity）不可簡單替換

---

### Phase 2: tick_pre/tick_post 分離 PPU dots（中風險）

**目標**: 將 3 PPU dots 從 tick_pre 拆分為 2+1，模擬 M2 rise 在 master clock 3 的行為

**改動**:

1. **MEM.cs** — `tick_pre()` 只推進 master clock 到 M2 rise 位置（8 master clocks = 2 PPU dots）
   ```csharp
   static void tick_pre()
   {
       masterClock += 8;  // M2 rise at master clock 3 → 2 PPU dots
       cpuCycleCount++;
       if (nmi_delay) { nmi_pending = true; nmi_delay = false; }
       catchUpPPU();  // runs 2 PPU dots
       catchUpAPU();
       // IRQ tracking...
   }
   ```

2. **MEM.cs** — `tick_post()` 推進剩餘 4 master clocks（1 PPU dot）
   ```csharp
   static void tick_post()
   {
       masterClock += 4;  // remaining 1 PPU dot
       catchUpPPU();  // runs 1 PPU dot
   }
   ```

3. **PPU.cs** — 驗證 $2002 read timing 是否需要調整（VBL/sprite flag 清除時機可能受影響）

**預期收益**: PPU register 讀寫與 M2 phase 精確對齊，修復 sub-dot timing 問題
**回歸風險**: 中（PPU dot 與 bus access 的相對順序改變，可能影響 VBL/NMI timing）
**驗證**: blargg ppu_vbl_nmi 全套 + AccuracyCoin P17/P18

**實測結果** (2026-03-07):
- ❌ 2+1 split（tick_pre: MC+=8 跑 2 dots, tick_post: MC+=4 跑 1 dot）→ **10 regressions**
  - ppu_vbl_nmi: 05-nmi_timing, 06-suppression, 08-nmi_off_timing
  - vbl_nmi_timing: 5-nmi_suppression, 6-nmi_disable, 7-nmi_timing
  - cpu_interrupts_v2: cpu_interrupts, 2-nmi_and_brk
  - sprite_overflow_tests: 5.Emulator
- 根因：CPU 看到 PPU 狀態早了 1 dot，NMI edge detection 時機偏移
- **需要 PPU 事件重新校準才能採用此分離**，非簡單改動

---

### Phase 3: DMA 精確排程（中風險，針對性強）

**目標**: DMA halt/alignment 改用 M2 phase 精確判斷，消除 parity proxy

**改動**:

1. **APU.cs** — `dmcfillbuffer()` 重寫 halt/alignment 邏輯
   - Halt: 等到下一個 GET cycle（`m2Phase == M2Phase.Rise`）
   - Alignment: 如果 halt 在 PUT cycle，多等 1 cycle
   - Phantom reads: 每個 halt cycle 都觸發，但只在 GET phase

2. **PPU.cs** — `oamDmaExecute()` 用 m2Phase 判斷 alignment
   - 取代 `cpuBusIsWrite` 的 read/write cycle 判斷

3. **MEM.cs** — `tick()` (DMA 用) 也設定 m2Phase

**預期收益**: +3~+6（P10 SH* bus conflict、P14 部分）
**回歸風險**: 中（DMA cycle count 變化可能影響現有通過的測試）
**驗證**: blargg dmc_dma 全套 + sprdma_and_dmc_dma + AccuracyCoin P10/P13/P14

---

### Phase 4: PPU Per-Dot 精確化（低風險，獨立）

**目標**: OAM evaluation 改為逐 dot 執行，而非批次

**改動**:

1. **PPU.cs** — sprite evaluation 從 scanline-batch 改為 dot-by-dot state machine
   - Dot 1: 清空 secondary OAM
   - Dot 65-256: 逐個評估 OAM entries（每 2 dots 一個 entry）
   - Dot 257-320: sprite fetch

**預期收益**: +3（P19 BG serial / sprites SL0 / $2004 stress）
**回歸風險**: 低（sprite evaluation 是獨立子系統）
**驗證**: blargg sprite 全套 + AccuracyCoin P19

---

### Phase 5: Master Clock 完整化（高風險，最終目標）

**目標**: 如果 Phase 1-4 仍有無法解決的 timing 問題，將 CPU 改為可暫停 state machine

**改動**:

1. **CPU.cs** — giant switch 改為 microcode-driven state machine
2. **MEM.cs** — main loop 改為 master clock 驅動
3. 所有子系統獨立排程

**預期收益**: 理論上 136/136
**回歸風險**: 極高（全部重寫）
**決策點**: Phase 1-4 完成後評估是否需要

---

## 執行順序與里程碑

| 階段 | 改動檔案 | 預估工時 | 里程碑 |
|------|----------|---------|--------|
| Phase 1 | MEM.cs, APU.cs | 小 | blargg 174 + AC P13 改善 |
| Phase 2 | MEM.cs | 小~中 | blargg 174 + VBL timing 不回歸 |
| Phase 3 | APU.cs, PPU.cs, MEM.cs | 中 | blargg 174 + AC P10/P14 改善 |
| Phase 4 | PPU.cs | 中 | blargg 174 + AC P19 改善 |
| Phase 5 | CPU.cs, MEM.cs, 全部 | 大 | 評估後決定 |

**策略**: 每個 Phase 完成後跑完整回歸測試，確認不回歸再進入下一階段。Phase 1 最安全且收益最明確，從這裡開始。

---

## 風險控管

1. **每個 Phase 獨立 commit** — 出問題可 revert
2. **blargg 174/174 為硬性底線** — 任何回歸立即修復
3. **Phase 5 為選擇性** — 只在 Phase 1-4 不足時才考慮
4. **AccuracyCoin 分頁測試** — 用 `run_ac_test.sh` 快速驗證特定頁面
