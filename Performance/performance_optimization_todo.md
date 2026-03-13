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
- **Risk**: Low — function is confirmed empty stub
- **Status**: 🔲 TODO

---

### PRIORITY 2 — AudioEnabled early-exit guard in apu_step()
- **Target**: APU.cs — `apu_step()` runs all pulse/noise/triangle/DMC timers even when audio disabled
- **Expected gain**: 3–5% (when running headless/benchmark)
- **Effort**: ~30 minutes
- **Method**: Wrap timer/sample logic with `if (AudioEnabled)` guard; keep only frame counter and length counter snapshot outside guard
- **Risk**: Medium — must not break $4015 status reads or IRQ timing
- **Status**: 🔲 TODO

---

### PRIORITY 3 — Early-exit in ProcessPendingDma()
- **Target**: MEM.cs — `ProcessPendingDma()` called on every CpuRead even when no DMA active
- **Expected gain**: 3–5%
- **Effort**: ~1 hour
- **Method**: Add fast-path guard at entry: `if (!dmaNeedHalt && !dmcDmaRunning && !spriteDmaTransfer) return;`
- **Risk**: Low — guard only skips when no DMA in progress
- **Status**: 🔲 TODO

---

### PRIORITY 4 — Integer fixed-point sample accumulator in APU
- **Target**: APU.cs — floating-point `_sampleAccum += 1.0` comparison done every CPU cycle
- **Expected gain**: 1–2%
- **Effort**: ~1 hour
- **Method**: Replace double accumulator with integer Q20 fixed-point counter
- **Risk**: Low — only affects sample timing precision (negligible)
- **Status**: 🔲 TODO

---

### PRIORITY 5 — Cache palette lookups in RenderBGTile()
- **Target**: PPU.cs `RenderBGTile()` — redundant ppu_ram[] + NesColors[] lookups per pixel
- **Expected gain**: 1–2%
- **Effort**: ~30 minutes
- **Method**: Pre-compute `bgColor` and `paletteColor` before inner loop; use ternary assignment
- **Risk**: Low — pure refactor, no logic change
- **Status**: 🔲 TODO

---

### PRIORITY 6 — Remove redundant screenX > 255 check in RenderBGTile()
- **Target**: PPU.cs `RenderBGTile()` line ~173 — bounds check every iteration when only last tile pixel can overflow
- **Expected gain**: <1%
- **Effort**: ~30 minutes
- **Method**: Move break condition outside loop, or restructure loop to only run valid iterations
- **Risk**: Low
- **Status**: 🔲 TODO

---

### PRIORITY 7 — Memory write dispatch inlining
- **Target**: MEM.cs — `mem_write_fun[addr](addr, val)` function pointer call on every write
- **Expected gain**: 1–2%
- **Effort**: ~1 hour
- **Method**: Replace function pointer with inline range-based dispatch (if/else chain with AggressiveInlining)
- **Risk**: Medium — must cover all mapper variants correctly
- **Status**: 🔲 TODO

---

### PRIORITY 8 — If/else branch reordering (high-frequency path first)
- **Target**: PPU.cs `RenderBGTile()`、MEM.cs `init_function()` 讀寫 dispatch 設定
- **Expected gain**: 1–3%
- **Effort**: ~1 hour
- **Method**:
  - **PPU.cs RenderBGTile() 像素繪製（~line 181-188）**：透明/遮罩像素（~80-85%）移到第一個 if，彩色像素（稀少）移到 else；消除重複的 `(!ShowBgLeft8 && inLeft8)` 判斷
  - **MEM.cs init_function() mem_read_fun（~line 383-388）**：PRG ROM 讀取（~50% of all reads）目前排在最後，改成先判斷 `>= 0x8000` 最快到達最常見路徑
  - **MEM.cs VRAM read dispatch（~line 396-435）**：Palette（0x3F00, 僅 ~1%）目前排第一，改成 pattern table（~35-40%）優先
- **Risk**: Low — 純順序調整，邏輯不變；改前需確認每個 else-if 的 address range 沒有重疊
- **Status**: 🔲 TODO

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
- **Status**: 🔲 TODO

---

### PRIORITY 10 — Instruction dispatch table (full replace, defer)
- **Target**: CPU.cs — 完整替換 256-case switch（PRIORITY 9 的延伸）
- **Expected gain**: 2–4%（與 Priority 9 合併後確認）
- **Risk**: High — large refactor
- **Note**: 先完成 Priority 9 的局部實驗，確認有效後再全面替換
- **Status**: 🔲 DEFERRED

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

---

## Failed / Ineffective Attempts

*(None yet)*

---

## Notes

- All tests use the same ROM, same machine, same duration (20s)
- Build must be **Debug x64** to match baseline (same JIT optimization level)
- If machine load varies, re-run baseline before comparing
- AccuracyCoin 136/136 and blargg 174/174 must still pass after each change
