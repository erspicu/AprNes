# P13 DMA Tests Fix Plan (132→136/136)

**Created**: 2026-03-10
**Baseline**: 174/174 blargg + 132/136 AC (master branch)
**Goal**: 136/136 AC (P13 all PASS)
**Status**: **Completed** — BUGFIX53-56 fixed all P13 tests (136/136 AC)

---

## Remaining 4 FAILs and Error Descriptions

| # | Test | err | Specific Error (err=2) | Required Fix |
|---|------|-----|------------------------|--------------|
| 1 | DMA + $2002 Read | 2 | halt/alignment cycles did not read $2002 correctly | DMA cycle-accurate |
| 2 | DMC DMA Bus Conflicts | 2 | DMC DMA bus conflict with APU registers is incorrect | DMA cycle-accurate |
| 3 | Explicit DMA Abort | 2 | Wrong number of stolen cycles for aborted DMA | DMA cycle-accurate + Abort |
| 4 | Implicit DMA Abort | 2 | Wrong number of stolen cycles for aborted DMA | DMA cycle-accurate + Abort |

---

## Preferred Approach: Option B — DMA State Machine Rewrite (TriCNES Style)

### Design Rationale

Convert AprNes's one-shot batch DMA (`dmcfillbuffer()`) into a per-cycle Get/Put state machine,
similar to TriCNES's approach. Advance the DMA state in each `tick()` call rather than stealing 3-4 cycles at once.

### Core Concept

```
Current AprNes:
  dmcfillbuffer() → calls dmc_stolen_tick() 3-4 times → batch complete

Option B:
  Each tick() call checks DMA state → advances Get/Put FSM cycle-by-cycle
  Use get/put cycle parity (similar to TriCNES APU_PutCycle) to control read/write alternation
```

### Scope of Changes

| File | Changes | Risk |
|------|---------|------|
| `APU.cs` | `dmcfillbuffer()` → DMA FSM (state, parity tracking) | Major |
| `MEM.cs` | tick() adds DMA state machine advancement, remove/refactor `dmc_stolen_tick()` | Medium |
| `IO.cs` | Adjust DMA trigger logic for $4014/$4015 writes | Minor |
| `CPU.cs` | Nearly unchanged (Mem_r/Mem_w interface unchanged) | None |
| `PPU.cs` | Unchanged | None |

### DMA States to Implement

```
OAM DMA:
  - Halt cycle (wait for correct parity)
  - Alignment cycle (if halt lands on put cycle, wait one extra get cycle)
  - 256 byte transfer: alternating Get (read source address) / Put (write $2004)
  - Total: 513-514 cycles

DMC DMA:
  - Halt cycle (1 cycle)
  - Alignment cycle (0-1 cycles, depending on parity)
  - Dummy cycle (1 cycle)
  - Read cycle (1 cycle, reads DMC sample address)
  - Total: 3-4 cycles

Priority (when both DMAs active):
  - GET cycle: DMC takes priority
  - PUT cycle: OAM takes priority
```

### Comparison with Option A

| | Option A: MCU Split | **Option B: DMA State Machine** |
|--|--|--|
| Files changed | 5-6 core files | 2-3 |
| Tests affected | All 310 | ~15 DMA-related |
| Regression risk | Extremely high | Medium |
| Expected gain | +4 AC | +4 AC |
| Additional benefit | Future all sub-cycle issues | DMA only |
| Prior failure history | Yes (2+1 model) | None |

### TriCNES Reference

TriCNES uses the same symmetric 3+3 model as AprNes, but DMA is a per-cycle state machine,
and achieves AC 136/136 across the board. This proves that non-asymmetric MCU split is not required to solve P13.

Key reference locations:
- `ref/TriCNES-main/Emulator.cs`
  - DMA state machine: lines 3836-4095
  - APU_PutCycle alternation: line 705
  - OAM DMA Get/Put: OAMDMA_Get(), OAMDMA_Put()
  - DMC DMA Get/Put: DMCDMA_Get(), DMCDMA_Put()
  - CPU_Read check: line 3974 (DMA only executes on read cycle)

---

## Fallback: Option A — Asymmetric MCU Split (Mesen2 Style)

Consider Option A only if Option B cannot resolve all 4 P13 tests.

### Mesen2 M2 Phase Split

```
NTSC: _startClockCount = 6, _endClockCount = 6
Read:  Start +5 MCU, End +7 MCU (total 12)
Write: Start +7 MCU, End +5 MCU (total 12)
```

### Specific Changes

1. `StartCpuCycle(bool forRead)`: masterClock += forRead ? 5 : 7
2. `EndCpuCycle(bool forRead)`: masterClock += forRead ? 7 : 5
3. CpuRead: ProcessPendingDma → StartCpuCycle(true) → read → EndCpuCycle(true)
4. CpuWrite: StartCpuCycle(false) → write → EndCpuCycle(false)
5. Need to re-calibrate boot CC, PPU offset, APU offset

---

## 2026-03-10 Experiment Results (Old Option A Attempts)

| Method | blargg | AC | Failure Reason |
|--------|--------|-----|----------------|
| Simple reorder (DMA before Start) | 172/174 | 124/136 | getCycle parity flipped |
| Reorder + getCycle flip | 169/174 | — | OAM DMA read/write on wrong cycle |
| Reorder + boot CC=8 | 172/174 | 124/136 | System self-compensated, no effect |
| Steps 2+3 only (no reorder) | 174/174 | 132/136 | No improvement, no regression |
| All 3 steps together | 172/174 | 124/136 | Same reorder issue + P14 regression |

---

## Step 2: Abort Mechanism (integrate after DMA state machine is complete)

**Existing infrastructure**: APU.cs has dmcDisableDelay, dmcImplicitAbortPending, dmcImplicitAbortActive.
clockdmc() already has delay countdown.

**Delayed $4015 disable**: putCycle ? 4 : 3 (normal), putCycle ? 6 : 5 (explicit abort)
**Implicit abort**: (dmctimer == 5 && putCycle) || (dmctimer == 4 && !putCycle)
**Write cycle cancellation**: EndCpuCycle with dmcImplicitAbortActive && cpuBusIsWrite → cancel DMA

**Verified**: Implementing alone does not affect blargg (174/174) and does not improve AC (132/136).

---

## TriCNES Parity Mapping Notes

```
TriCNES APU_PutCycle (post-toggle) = our !putCycle
putCycle = (apucycle & 1) != 0   (odd = PUT in our model)

TriCNES DMC timer decrements by 2 per CPU cycle (ours decrements by 1)
TriCNES timer value / 2 = our timer value

TriCNES formula → our equivalent:
  APU_PutCycle ? 3 : 4        →  putCycle ? 4 : 3
  APU_PutCycle ? 5 : 6        →  putCycle ? 6 : 5
  timer==2 && !APU_PutCycle   →  dmctimer==1 && putCycle
  timer==Rate && APU_PutCycle →  dmctimer==dmcrate && !putCycle
  timer==10 && !APU_PutCycle  →  dmctimer==5 && putCycle
  timer==8 && APU_PutCycle    →  dmctimer==4 && !putCycle
```

---

## Related Files

| File | Changes |
|------|---------|
| `AprNes/NesCore/APU.cs` | dmcfillbuffer → DMA FSM; apu_4015: delayed disable + abort |
| `AprNes/NesCore/MEM.cs` | tick(): DMA state machine advancement; dmc_stolen_tick() refactor/removal |
| `AprNes/NesCore/IO.cs` | $4014/$4015: DMA trigger logic adjustment |

## References

- TriCNES: `ref/TriCNES-main/Emulator.cs` (DMA: lines 3836-4095, $4015: lines 9358-9402)
- Mesen2: `ref/Mesen2-master/Core/NES/NesCpu.cpp` (ProcessPendingDma: 325-448, M2 split: 317-321/294-297)
- TriCNES DMC timer decrement: lines 897-898
