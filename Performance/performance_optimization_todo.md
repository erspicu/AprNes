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

### PRIORITY 8 — Instruction dispatch table (defer)
- **Target**: CPU.cs — 256-case switch in cpu_step_one_cycle()
- **Expected gain**: 2–4%
- **Effort**: 3–4 hours
- **Method**: Replace switch with delegate array `opFuncs[opcode]()`
- **Risk**: High — large refactor, JIT already compiles switch as jump table
- **Note**: Current implementation already near-optimal; defer unless priorities 1–7 exhausted
- **Status**: 🔲 DEFERRED

---

## Results Log

| # | Optimization | Before FPS | After FPS | Delta | Result | Report |
|---|-------------|-----------|----------|-------|--------|--------|
| 1 | Baseline | — | 181.70 | — | — | [v1](2026-03-14_perf_v1.md) |

---

## Failed / Ineffective Attempts

*(None yet)*

---

## Notes

- All tests use the same ROM, same machine, same duration (20s)
- Build must be **Debug x64** to match baseline (same JIT optimization level)
- If machine load varies, re-run baseline before comparing
- AccuracyCoin 136/136 and blargg 174/174 must still pass after each change
