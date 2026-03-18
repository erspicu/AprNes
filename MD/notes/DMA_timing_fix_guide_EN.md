# DMA Timing Fix Guide

**Goal**: Fix the remaining 14 FAILs + 1 SKIP in AccuracyCoin (all DMA timing related)
**Potential gain**: +14 PASS (121/136 → 135/136), plus +1 from SKIP to PASS (→ 136/136)
**Difficulty**: Extremely high (requires structural changes involving MEM.cs / APU.cs / CPU.cs core timing)
**Status**: **Completed** — Final result 136/136 AC (BUGFIX33-56); Plan A (MCU Split) was not needed

---

## I. Problem Overview

| Root Cause | Tests | Count | Description |
|------------|-------|-------|-------------|
| A: DMA sub-cycle precision | P13×6 + P10×5 + P20×2 | 13 | DMADMASync_PreTest precondition failure |
| B: DMC/APU interaction | P14 DMC + P14 Strobe | 2 | DMC sub-test + OAM DMA parity |
| D: DMC DMA cumulative drift | P12 Test E | 1 SKIP | DMC DMA ~12 cycle drift → hang |

The common root cause of all problems: **our DMA engine uses the entire CPU cycle as its minimum unit, lacking the Start/End half-cycle precision of Mesen2**.

---

## II. Core Difference: AprNes vs Mesen2

### 2.1 CPU Cycle Start/End Separation

**Mesen2's model** (NesCpu.cpp):

```
MemoryRead(addr):
    ProcessPendingDma(addr)     ← DMA triggers before bus access
    StartCpuCycle(forRead=true) ← First half cycle: masterClock += 5 MCU, CC++, PPU catch-up, APU
    value = Read(addr)          ← Actual bus read
    EndCpuCycle(forRead=true)   ← Second half cycle: masterClock += 7 MCU, PPU catch-up, NMI edge, IRQ
    return value
```

**Key details**:
- `StartCpuCycle(forRead=true)`: masterClock += `_startClockCount - 1` = **5 MCU**
- `EndCpuCycle(forRead=true)`: masterClock += `_endClockCount + 1` = **7 MCU**
- Total 5 + 7 = **12 MCU** = 1 CPU cycle = 3 PPU dots
- Read/write asymmetry: read = (5, 7), write = (7, 5)
- NMI edge detection is in **EndCpuCycle** (φ2 phase)
- IRQ state is also sampled in **EndCpuCycle**

**AprNes's current model** (MEM.cs):

```
Mem_r(addr):
    StartCpuCycle()              ← Full 12 MCU: CC++, NMI promote, PPU×3, APU, IRQ
    ProcessPendingDma(addr)      ← DMA triggers after StartCpuCycle
    value = read(addr)           ← bus read
    EndCpuCycle()                ← empty (placeholder)
    return value
```

**Problems**:
1. **All 12 MCU concentrated in StartCpuCycle**: PPU 3 dots + APU + NMI + IRQ all complete before bus access
2. **Cannot express half-cycle precision**: Side effects of DMA dummy reads (e.g., $2002 clearing VBL flag) are not accurately timed
3. **ProcessPendingDma is after StartCpuCycle**: cpuCycleCount is already +1, requiring a parity flip compensation hack

### 2.2 Phantom Read Side Effects in DMA

**P13 test requirements**: Phantom reads during DMA halt/dummy/alignment cycles must have correct side effects:

- `$2002` read: must clear VBL flag (DMA + $2002 Read test)
- `$4015` read: must clear frame counter IRQ flag (DMA + $4015 Read test)
- `$4016/$4017`: must advance the controller shift register (though behavior varies by NES model)

**Current problem**: Phantom reads use `mem_read_fun[readAddress](readAddress)` directly,
but the timing is not at the correct half-cycle position, causing side effects to be observed at the wrong time.

### 2.3 Bus Conflict Merging

**P13 DMC DMA Bus Conflicts test**: When the address read by DMC DMA falls in the $4000-$401F range,
the internal APU register and external bus simultaneously drive the data bus; the result is AND-merged.

Formula: `result = Read(dmcSampleAddr) AND Read($4000 | (dmcSampleAddr & 0x1F))`

**Current status**: ProcessDmaRead already implements bus conflict, but incorrect timing causes the wrong value to be read.

---

## III. Fix Approaches

### Plan A: Start/End Half-Cycle Separation (Recommended)

Split the 12 MCU in StartCpuCycle into 5 + 7 (read) or 7 + 5 (write), aligning with Mesen2.

#### Step 1: Split StartCpuCycle / EndCpuCycle

```csharp
// MEM.cs
static void StartCpuCycle(bool forRead)
{
    masterClock += forRead ? (MASTER_PER_CPU / 2 - 1) : (MASTER_PER_CPU / 2 + 1);
    // = forRead ? 5 : 7 (MCU)
    cpuCycleCount++;
    m2PhaseIsWrite = (cpuCycleCount & 1) != 0;
    catchUpPPU();  // PPU runs ~1.25 or ~1.75 dots
    catchUpAPU();  // APU may fire here
    if (strobeWritePending > 0) processStrobeWrite();
}

static void EndCpuCycle(bool forRead)
{
    masterClock += forRead ? (MASTER_PER_CPU / 2 + 1) : (MASTER_PER_CPU / 2 - 1);
    // = forRead ? 7 : 5 (MCU)
    catchUpPPU();  // PPU processes remaining ~1.75 or ~1.25 dots

    // NMI edge detection (φ2 phase, per Mesen2)
    if (nmi_delay_cycle >= 0 && cpuCycleCount > nmi_delay_cycle)
    { nmi_pending = true; nmi_delay_cycle = -1; }

    // IRQ sampling (penultimate cycle)
    irqLinePrev = irqLineCurrent;
    irqLineCurrent = (statusframeint && !apuintflag) || statusdmcint || statusmapperint;
}
```

#### Step 2: Update Mem_r / Mem_w / ZP_r

```csharp
static byte Mem_r(ushort addr)
{
    ProcessPendingDma(addr);   // DMA before cycle starts
    StartCpuCycle(true);       // First half cycle (5 MCU)
    byte val = mem_read_fun[addr](addr);  // bus read
    EndCpuCycle(true);         // Second half cycle (7 MCU)
    return val;
}

static void Mem_w(ushort addr, byte val)
{
    StartCpuCycle(false);      // First half cycle (7 MCU)
    mem_write_fun[addr](addr, val);  // bus write
    EndCpuCycle(false);        // Second half cycle (5 MCU)
}
```

#### Step 3: Update tick() and the DMA Engine

tick() in DMA also needs Start+End:

```csharp
static void tick()
{
    StartCpuCycle(true);   // DMA cycles are reads
    EndCpuCycle(true);
}
```

Each phantom read in ProcessPendingDma must execute between Start and End:

```csharp
// Halt cycle
StartCpuCycle(true);
if (!skipPhantomRead)
    mem_read_fun[readAddress](readAddress);  // phantom read with side effects
EndCpuCycle(true);
```

#### Step 4: Fix PPU Register Timing

**Greatest risk**: The VBL suppression check in `ppu_r_2002` currently depends on all 3 dots completing before bus access.
After the split, only ~1.25 dots complete in Start; the remaining ~1.75 dots complete in End.

Adjustments needed:
- VBL suppression window for `scanline == 241 && cx == 1` in `ppu_r_2002`
- VBL set timing (currently at sl=241, cx=1 in `ppu_step_new`)
- NMI delay promote (move to EndCpuCycle)

**This is the most difficult part and most likely to cause regressions**, requiring extensive test validation.

#### Step 5: Move ProcessPendingDma Before StartCpuCycle

The current call order is `StartCpuCycle → ProcessPendingDma`. It must change to:

```
ProcessPendingDma → StartCpuCycle → bus access → EndCpuCycle
```

This matches Mesen2's `MemoryRead`. After this change, the parity compensation hack can be removed.

---

### Plan B: Master Clock Scheduler (Full Rewrite)

Rework CPU/PPU/APU as an event-driven master clock scheduler, with each component scheduling independently.

**Pros**: Theoretically solves all timing problems, including DMC DMA cumulative drift
**Cons**: Enormous engineering effort, essentially rewrites the entire timing system, very high regression risk

**Not recommended as a first step**. Plan A is sufficient to resolve most problems.

---

## IV. Fix Sequence

### Phase 1: Start/End Split (Foundation)

1. Implement the 5+7 / 7+5 distribution for `StartCpuCycle(bool forRead)` / `EndCpuCycle(bool forRead)`
2. Update call order in `Mem_r`, `Mem_w`, `ZP_r`
3. Update `tick()` in the DMA engine to use Start+End
4. **Verify**: blargg 174/174 with no regressions (this step is the most likely to break things)

**Key risks**:
- VBL/NMI timing will change (ppu_vbl_nmi 10 tests, vbl_nmi_timing 7 tests)
- Sprite 0 hit timing may shift (sprite_hit_tests 11 tests)
- APU frame counter IRQ timing may shift (blargg_apu 11 tests)

**Mitigation strategy**:
- First use an equal split of `MASTER_PER_CPU/2 = 6` (6+6 instead of 5+7) to confirm the base architecture is correct
- Then gradually adjust to 5+7, fixing regressions introduced at each step
- Or keep NMI/IRQ in StartCpuCycle (does not fully match Mesen2, but reduces regressions)

### Phase 2: DMA Phantom Read Side Effects

1. Ensure phantom reads in halt/dummy cycles go through the normal `Mem_r` path (including side effects)
2. Fix bus conflict timing in `ProcessDmaRead`
3. **Verify**: P13 DMADMASync_PreTest passes → unlocks other P13 tests

### Phase 3: DMA Parity and Interleaving

1. Remove the parity compensation hack (ProcessPendingDma is now in the correct position)
2. Fix cycle count when DMC DMA and OAM DMA overlap
3. **Verify**: All 6 P13 tests, P20 Instruction Timing + Implied Dummy Reads

### Phase 4: SH* DMA Interaction

1. Implement RDY line behavior: DMA halt on a write cycle is delayed until the next read cycle
2. SH* instruction write cycles are not interrupted by DMA halt
3. **Verify**: P10 SH* 5 tests (err=7 → PASS)

### Phase 5: DMC Sub-tests

1. Fix precise timing for DMC Load DMA (parity-dependent countdown)
2. Fix 1-byte timing for DMC buffer refill
3. **Verify**: P14 DMC test (err=21 → lower err or PASS)

### Phase 6: Controller Strobe Parity

1. PUT/GET parity after OAM DMA affects controller reads
2. **Verify**: P14 Controller Strobing (err=1 → PASS)

### Phase 7: IRQ Flag Latency Test E

1. Reduce DMC DMA cumulative drift (micro-timing drift per DMA)
2. Requires Start/End split to be correct before this can be solved
3. **Verify**: P12 Test E no longer hangs (SKIP → PASS)

---

## V. Key References

### Files

| Path | Contents |
|------|----------|
| `ref/Mesen2-master/Core/NES/NesCpu.cpp` | Mesen2 DMA implementation (L317-448) |
| `ref/Mesen2-master/Core/NES/NesCpu.h` | DMA state definitions |
| `ref/Mesen2-master/Core/NES/APU/NesApu.cpp` | DMC DMA trigger |
| `ref/DMA - NESdev Wiki.html` | DMA timing spec |
| `ref/APU DMC - NESdev Wiki.html` | DMC behavior |
| `ref/DMC_DMA timing and quirks - nesdev.org.html` | Advanced DMA timing |
| `AprNes/NesCore/MEM.cs` | Current DMA engine |
| `AprNes/NesCore/APU.cs` | DMC control |

### NESdev Wiki Key Points

1. **Get/Put Cycles**: DMA reads can only occur on GET cycles (M2 high); writes can only occur on PUT cycles (M2 low).
   GET/PUT is determined by the APU clock phase, not simply by even/odd CPU cycles.

2. **Halt Timing**: DMA can only halt the CPU on a read cycle. If the CPU is on a write cycle, the halt is delayed until the next read.

3. **DMC DMA Priority**: DMC reads take priority over OAM reads. When they overlap, OAM needs one extra alignment cycle.

4. **Phantom Read**: Halt/dummy cycles repeat the CPU's last bus access.
   If the last access was reading $4015, the phantom read also reads $4015 (with side effects).

### Mesen2 StartCpuCycle Master Clock Formula

```cpp
// _startClockCount = 6 (NTSC), _endClockCount = 6
StartCpuCycle(forRead=true):  masterClock += 6 - 1 = 5
EndCpuCycle(forRead=true):    masterClock += 6 + 1 = 7
// Total: 12 MCU per read cycle

StartCpuCycle(forRead=false): masterClock += 6 + 1 = 7
EndCpuCycle(forRead=false):   masterClock += 6 - 1 = 5
// Total: 12 MCU per write cycle
```

The read/write asymmetry exists because the M2 signal's rising/falling edges are asymmetric within the CPU cycle.
Read operations occur during M2 high (the longer second half), write operations during M2 low (the longer first half).

---

## VI. Risk Assessment

| Phase | Regression Risk | Affected Scope | Mitigation |
|-------|----------------|----------------|------------|
| 1 (Start/End split) | **Extremely high** | All 174 tests | Use 6+6 equal split first, then adjust |
| 2 (Phantom read) | Medium | DMA-related | Limit to ProcessPendingDma |
| 3 (Parity) | High | OAM DMA timing | Adjust incrementally |
| 4 (SH* RDY) | Low | P10 only | Isolated change |
| 5 (DMC sub-tests) | Medium | P14 DMC | Fix one sub-test at a time |
| 6 (Strobe) | Low | P14 Strobe only | Isolated change |
| 7 (IRQ Latency) | Low | P12 only | Depends on Phase 1 being correct |

**Summary**: Phase 1 is the cornerstone and greatest risk of the entire plan. It is recommended to work on a git branch
and run the full 174-test regression after each sub-step. Phase 1 is expected to require multiple iterations to stabilize.
