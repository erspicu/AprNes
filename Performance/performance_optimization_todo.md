# AprNes Performance Optimization TODO

## Benchmark Method

### Tool
```powershell
AprNes.exe --perf "Performance\Mega Man 5 (USA).nes" 20 "description"
```
- ROM: Mega Man 5 (USA).nes（Mapper 004 MMC3，代表性複雜場景）
- Duration: 20 seconds, no FPS cap, no audio, headless mode
- Result: automatically saved to `Performance/YYYY-MM-DD_perf_vN.md`

### Pass/Fail Criteria
- **Improvement ≥ 3%** → keep change, commit, create new perf report
- **Improvement 1–3%** → keep if code stays clean, note in TODO
- **No improvement / regression** → revert, mark as FAILED in TODO

### Test Steps for Each Optimization
1. Record baseline FPS (latest `_perf_vN.md`)
2. Apply code change
3. Build: `MSBuild AprNes.csproj /p:Configuration=Debug /p:Platform=x64`
4. Run: `AprNes.exe --perf "Performance\Mega Man 5 (USA).nes" 20 "description"`
5. Compare FPS delta
6. Update this file with result

---

## Baseline

| Date | Frames (20s) | Avg FPS | Report |
|------|-------------|---------|--------|
| 2026-03-14 | 3634 | 181.70 | [perf_v1](2026-03-14_perf_v1.md) |

---

## Optimization Tasks

### PRIORITY 0 — Remove DMA trace logging code
- **Target**: APU.cs, IO.cs, TestRunner.cs — `dmcTraceEnabled` 條件式 `[DMA-TRACE]` Console.Error.WriteLine 及相關 field
- **Expected gain**: 微小（消除每次 APU write 路徑上的條件判斷）
- **Effort**: 已完成
- **Method**: 移除所有 `if (dmcTraceEnabled)` trace block、`dmcTraceStart` unused field、`--dma-trace` CLI 參數及 `NesCore.dmcTraceEnabled` 設定
- **Risk**: 無 — 純 debug 輸出，不影響模擬邏輯
- **Status**: ✅ DONE (build 0 errors)

---

### PRIORITY 1 — Remove PollInterrupts() calls
- **Target**: CPU.cs — 255+ empty stub calls in instruction dispatch switch
- **Expected gain**: 5–8%
- **Effort**: ~5 minutes
- **Method**: Delete all `PollInterrupts();` calls from each `case 0xXX:` in cpu_step_one_cycle()
- **Risk**: ~~Low~~ → **INVALID** — PollInterrupts() has actual runtime function (interrupt polling timing per-instruction), NOT a pure empty stub
- **Status**: ❌ FAILED — 移除後測試全部失敗，已 revert。**此方法不可行。**

---

### PRIORITY 2 — AudioEnabled early-exit guard in apu_step()
- **Target**: APU.cs — `apu_step()` runs all pulse/noise/triangle/DMC timers even when audio disabled
- **Expected gain**: 3–5% (when running headless/benchmark)
- **Status**: ❌ REMOVED — 邏輯正確性問題，不實作
- **原因**:
  - `apu_step()` 中以下邏輯**必須在 audio disabled 時繼續執行**，無法 guard：
    1. **`clockdmc()`** — DMC timer 驅動 DMA 週期偷取（stolen cycles），影響 CPU timing，與音效無關
    2. **frame counter / IRQ** — `framectrdiv` 倒數、`statusframeint` 設定，影響 IRQ 時序
    3. **`lengthctr_snapshot[]`** — `$4015` read 的 length counter 狀態來源
    4. **envelope/sweep/length 時鐘** — 影響 frame counter 狀態一致性
  - 只有最末段的「sample 輸出」可 guard，但那部分本身已有 `if (AudioEnabled)` 路徑（WaveOut 不播放）
  - 有效 guard 範圍極小，幾乎無收益，且容易遺漏，風險高

---

### PRIORITY 3 — Early-exit in ProcessPendingDma()
- **Target**: MEM.cs — `ProcessPendingDma()` called on every CpuRead even when no DMA active
- **Expected gain**: 3–5%
- **Method**: Add fast-path guard at entry（**修正版**，原版不完整）：
  ```csharp
  if (!dmaNeedHalt && !dmcDmaRunning && !dmcNeedDummyRead && !spriteDmaTransfer && !dmcImplicitAbortPending) return;
  ```
  - 原版 guard 漏掉 `!dmcNeedDummyRead`（dummy read 進行中會跳過）和 `!dmcImplicitAbortPending`（1-cycle phantom DMA 會被跳過）
- **Risk**: Low — 確認 5 個 flag 都覆蓋所有 DMA 入口條件
- **Status**: ✅ DONE — **+5.1%** (216.45 → 227.40 FPS)；blargg 174/174 + AC 136/136 驗證通過

---

### PRIORITY 4 — Integer fixed-point sample accumulator in APU
- **Target**: APU.cs — floating-point `_sampleAccum += 1.0` comparison done every CPU cycle
- **Expected gain**: 1–2%
- **Method**: Replace `double _sampleAccum` + `_cycPerSample` with `int _sampleCounter += APU_SAMPLE_RATE`，觸發條件 `>= (int)CPU_FREQ`
- **Risk**: Low — only affects sample timing precision (negligible)
- **Status**: ❌ FAILED — 兩次實測均為負效益
  - v1（與 Priority 5 stackalloc 合併）：188.55 → 187.90（-0.3%）
  - v2（單獨測試）：189.75 → 186.50（**-1.7%**），已 revert
  - 原因：Debug JIT 對 `int += int` 與 `double += double` 的差異不大，反而 `(int)CPU_FREQ` 的 cast overhead 可能更高

---

### PRIORITY 5 — Cache palette lookups in RenderBGTile()
- **Target**: PPU.cs `RenderBGTile()` — redundant ppu_ram[] + NesColors[] lookups per pixel
- **Expected gain**: 1–2%
- **Method**: 用靜態 `palCacheR / palCacheN`（`Marshal.AllocHGlobal(uint*4)` in Main.cs init），每次 RenderBGTile 開頭更新 6 個 entry，loop 內直接 `pal[bgPixel]`
- **Risk**: Low — pure refactor, no logic change
- **Status**: ✅ DONE — **+0.6%** (188.55 → 189.75 FPS)
  - ❌ v1（stackalloc）: -0.3%（每次 tile 都 allocate，開銷更大）
  - ✅ v2（static pre-alloc）: +0.6%（一次分配，重複使用）

---

### PRIORITY 6 — Remove redundant screenX > 255 check in RenderBGTile()
- **Target**: PPU.cs `RenderBGTile()` line ~173 — bounds check every iteration when only last tile pixel can overflow
- **Expected gain**: <1%（實際遠超預期）
- **Effort**: ~30 minutes
- **Method**: 確認呼叫方已有 `ppu_cycles_x < 256` guard，直接移除 `if (screenX > 255) break;`
- **Risk**: Low
- **Status**: ✅ DONE — **+6.1%** (204.10 → 216.45 FPS)；移除 inner loop branch 後 JIT 能更好 unroll/最佳化，遠超預期

---

### PRIORITY 7 — Memory write dispatch inlining
- **Target**: MEM.cs — `mem_write_fun[addr](addr, val)` function pointer call on every write
- **Expected gain**: 1–2%
- **Status**: ❌ REMOVED — 無效且有正確性風險，不實作
- **原因**:
  - `mem_write_fun[]` 已是 O(1) array 索引，dispatch overhead 本身幾乎為零
  - 真正的成本在 **handler 內部**（`IO_write` 21-case switch、mapper bank switching），inline dispatch 層無法改善
  - Mapper handler（`MapperW_PRG` 等）為虛擬方法，**runtime polymorphism 無法 inline**（10+ mapper 類型，JIT 無法確定單一目標）
  - 若自行複製 mapper dispatch 邏輯，mapper 新增/修改時極易產生不一致 → 靜默錯誤（bank switch 失效、MMC3 IRQ 錯誤等）
  - 結論：此最佳化在架構上無效，且正確性風險不對等

---

### PRIORITY 8 — If/else branch reordering (high-frequency path first)
- **Target**: PPU.cs `RenderBGTile()`、MEM.cs `init_function()` 讀寫 dispatch 設定
- **Expected gain**: 1–3%
- **Effort**: ~1 hour
- **Method**:
  - **PPU.cs RenderBGTile()（已實作）**：pre-compute `bgColor = NesColors[ppu_ram[0x3f00]&0x3f]` 移至 loop 外；合併重複 `(!ShowBgLeft8 && inLeft8)` 雙重判斷為單一 `masked` 變數；cases 1+2 合流為 `(masked || bgPixel == 0) ? bgColor : ...`
  - **MEM.cs init_function() mem_read_fun / VRAM dispatch**：這兩個是 init 時期的 if/else，執行時已是 O(1) function pointer 陣列索引，重排無效
- **Risk**: Low
- **Status**: ✅ DONE — **+0.4%** (187.70 → 188.55 FPS) — 效果微小但程式碼更簡潔，保留

---

### PRIORITY 9 — CPU opcode dispatch: function pointer table
- **Target**: CPU.cs `cpu_step_one_cycle()` — 目前外層 `switch(opcode)` 共 256 個 case，每個 case 內再用 `if/switch(operationCycle)` 判斷執行週期
- **Expected gain**: 2–5%（取決於 JIT 是否已最佳化 switch；function pointer table 可減少 branch miss）
- **Effort**: 4–6 hours（大型重構）
- **Method**:
  ```csharp
  // 建立 256 個 delegate，每個對應一個 opcode 的所有週期邏輯
  static Action[] opHandlers = new Action[256];

  static void InitOpHandlers()
  {
      opHandlers[0x09] = () => { GetImmediate(); Op_ORA(dl); CompleteOperation(); };
      opHandlers[0x05] = () => {
          if (operationCycle == 1) GetAddressZeroPage();
          else { Op_ORA(CpuRead(addressBus)); CompleteOperation(); }
      };
      // ... 依此類推
  }

  // cpu_step_one_cycle() 改為：
  opHandlers[opcode]();
  ```
  - 外層 switch 改為單一 array index，消除 JIT switch jump table 的間接跳躍
  - 每個 handler 仍保有內層 operationCycle 邏輯
- **Risk**: High — 256 個 opcode 全部重構，容易遺漏；需要完整跑過 AccuracyCoin 136/136 + blargg 174/174 驗證
- **Note**: JIT 本身會將 switch 編譯為 jump table，實際增益需實測才能確認；可先實作 10-20 個高頻 opcode（LDA/STA/BNE/JMP/JSR/RTS）做對比測試再決定是否全面替換
- **Status**: ✅ DONE — **+7.6%** (189.75 → 204.10 FPS)；Debug JIT 對 switch 未完全 jump table 最佳化，delegate array 明顯更快

---

### PRIORITY 10 — Instruction dispatch table (full replace, defer)
- **Target**: CPU.cs — 完整替換 256-case switch（PRIORITY 9 的延伸）
- **Expected gain**: 2–4%（與 Priority 9 合併後確認）
- **Risk**: High — large refactor
- **Note**: Priority 9 已全面替換，Priority 10 視為已完成
- **Status**: ✅ DONE (合併到 Priority 9)

---

### PRIORITY 11 — 將 managed array 改為 unsafe pointer 存取
- **Target**: APU.cs、PPU.cs — 熱路徑中仍使用 managed array 的部分
- **Expected gain**: 1–3%（消除 managed array bounds check 及 2D index 計算）
- **Effort**: ~2 hours
- **Method**:

  **① APU.cs `DUTYLOOKUP[4,8]`（2D → 1D flatten + unsafe pointer）**
  - 目前：`static int[,] DUTYLOOKUP = new int[4,8];`，每次存取 `DUTYLOOKUP[duty, seq]`
  - 改為：`static int* DUTYLOOKUP;`（Marshal.AllocHGlobal 32 ints）
  - 存取：`DUTYLOOKUP[_pulseDuty[i] * 8 + _pulseSeq[i]]`
  - 熱路徑：`apu_step()` 每 CPU cycle 呼叫，約 **3.6M 次/秒**

  **② APU.cs `TRI_SEQ[32]`（managed readonly → unsafe pointer）**
  - 目前：`static readonly int[] TRI_SEQ = { 15,14,...,15 };`
  - 改為：`static int* TRI_SEQ;`（Marshal.AllocHGlobal 32 ints，initAPU 初始化）
  - 熱路徑：`apu_step()` 每 CPU cycle，約 **1.8M 次/秒**

  **③ PPU.cs `secondaryOAM[32]`（managed byte[] → unsafe byte*）**
  - 目前：`static byte[] secondaryOAM = new byte[32];`
  - 改為：`static byte* secondaryOAM;`（Marshal.AllocHGlobal 32 bytes）
  - 熱路徑：sprite evaluation，每 scanline dot 多次存取

  **④ PPU.cs `corruptOamRow[32]`（managed bool[] → unsafe byte*）**
  - 目前：`static bool[] corruptOamRow = new bool[32];`
  - 改為：`static byte* corruptOamRow;`（0=false, 1=true）
  - 判斷由 `if (corruptOamRow[i])` 改為 `if (corruptOamRow[i] != 0)`

- **Risk**: Low — codebase 已大量使用相同的 `Marshal.AllocHGlobal` + unsafe pointer 模式（NES_MEM、ppu_ram、ScreenBuf1x 等），此改法為既有慣例
- **Verify**: 改後跑 AccuracyCoin 136/136 + blargg 174/174
- **Status**: ✅ DONE — **+3.3% improvement** (181.70 → 187.70 FPS, +120 frames/20s)

---

## Results Log

| # | Optimization | Before FPS | After FPS | Delta | Result | Report |
|---|-------------|-----------|----------|-------|--------|--------|
| 1 | Baseline | — | 181.70 | — | — | [v1](2026-03-14_perf_v1.md) |
| 2 | Priority 11: managed array → unsafe pointer (TRI_SEQ, DUTYLOOKUP, secondaryOAM, corruptOamRow) | 181.70 | 187.70 | **+3.3%** | ✅ KEEP | [v2](2026-03-14_perf_v2.md) |
| 3 | Priority 8: RenderBGTile pre-compute bgColor + merge duplicate condition | 187.70 | 188.55 | +0.4% | ✅ KEEP (cleaner code) | [v6](2026-03-14_perf_v6.md) |
| 4 | Priority 4+5 v1: APU int counter + stackalloc palette | 188.55 | 187.90 | -0.3% | ❌ REVERT | [v7](2026-03-14_perf_v7.md) |
| 5 | Priority 5 v2: static palCacheR/N (Marshal.AllocHGlobal, reuse) | 188.55 | 189.75 | **+0.6%** | ✅ KEEP | [v8](2026-03-14_perf_v8.md) |
| 6 | Priority 4 v2: APU int counter (單獨測試) | 189.75 | 186.50 | -1.7% | ❌ REVERT | [v10](2026-03-14_perf_v10.md) |
| 7 | Priority 9: CPU opcode dispatch Action[256] table | 189.75 | 204.10 | **+7.6%** | ✅ KEEP | [v11](2026-03-14_perf_v11.md) |
| 8 | Priority 6: remove redundant screenX > 255 check in RenderBGTile() | 204.10 | 216.45 | **+6.1%** | ✅ KEEP | [v12](2026-03-14_perf_v12.md) |
| 9 | Priority 3: ProcessPendingDma early-exit guard (5-flag) | 216.45 | 227.40 | **+5.1%** | ✅ KEEP | [v14](2026-03-14_perf_v14.md) |

---

## Failed / Ineffective Attempts

*(None yet)*

---

## Notes

- All tests use the same ROM, same machine, same duration (20s)
- Build must be **Debug x64** to match baseline (same JIT optimization level)
- If machine load varies, re-run baseline before comparing
- AccuracyCoin 136/136 and blargg 174/174 must still pass after each change
