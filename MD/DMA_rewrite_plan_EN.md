# DMA Complete Rewrite Plan

**Created**: 2026-03-08
**Baseline**: 174/174 blargg, 122/136 AccuracyCoin
**Goal**: Fix the remaining 13 FAILs + 1 SKIP (all related to DMA timing)
**Potential gain**: +13~14 PASS
**Status**: **Completed** — Final result 174/174 blargg + 136/136 AC (BUGFIX33-56)

---

## Long-term Guidelines

1. **Hardware behavior (from documentation) > test ROM expectations > Mesen2 reference**
2. **No error compensation**: Do not add hacks just to pass tests. If correct hardware behavior causes a regression, find and fix the other incorrect part.
3. **Fix completely in one pass**: When a root cause affects multiple subsystems, fix all of them at once. Short-term regressions are acceptable; the foundation must be correct.
4. **Research before trial-and-error**: For uncertain hardware behavior, download and read the documentation before making changes. Spending time studying is far better than blind modifications.

---

## Current State Analysis

### Completed Infrastructure

- [x] Asymmetric MCU split: StartCpuCycle(+5/+7) → EndCpuCycle(+7/+5)
- [x] catchUpPPU runs once each in Start and End (2+1 dot distribution)
- [x] NMI edge detection in EndCpuCycle
- [x] IRQ sampling in EndCpuCycle
- [x] nmi_delay_cycle cycle-accurate NMI delay
- [x] Unified DMA state machine (ProcessPendingDma)
- [x] ProcessDmaRead bus conflict handling
- [x] DMC Load DMA parity countdown (BUGFIX31)

### Current Core Issue

**ProcessPendingDma is in the wrong position** (compensation chain):

```
Current (DMA-after-Start):
  Mem_r(addr):
    StartCpuCycle(true)     ← cpuCycleCount++ happens here
    ProcessPendingDma(addr) ← getCycle = (CC & 1) == 0, but CC is already 1 ahead of Mesen2
    bus read
    EndCpuCycle(true)

Mesen2 (DMA-before-Start):
  MemoryRead(addr):
    ProcessPendingDma(addr) ← getCycle = (CC & 1) == 0, CC is the correct value
    StartCpuCycle(true)     ← CC++ happens after DMA
    bus read
    EndCpuCycle(true)
```

**Cascade effects**:
- CC off by 1 → getCycle parity flipped → Reload DMA produces 3 cycles (should be 4)
- BUGFIX31's `(apucycle & 1) != 0 ? 2 : 3` was a compensation value calibrated for DMA-after-Start
- Moving DMA to before-Start → Load DMA countdown must be updated → OAM DMA parity also changes → cascading effects in multiple places

### Root Cause Classification of the Remaining 13 FAILs + 1 SKIP

| Root Cause | Tests | Count | Description |
|------------|-------|-------|-------------|
| DMA cycle parity | P13×6 + P20×2 | 8 | phantom read hits wrong address/timing |
| DMA + SH* RDY | P10×5 | 5 | DMA halt not delayed enough on write cycles |
| DMC cumulative drift | P12 Test E | 1 SKIP | DMA ~12 cycle drift → hang |

---

## Step Plan

### Step 0: Research Preparation (required before each step)

**Before touching anything, the relevant documentation must be read first.**

Hardware behavior documentation to confirm:

- [x] `ref/DMA - NESdev Wiki.html` — DMA timing basic specification
- [x] `ref/DMC_DMA timing and quirks - nesdev.org.html` — DMC DMA advanced details
- [x] `ref/Mesen2-master/Core/NES/NesCpu.cpp` L317-448 — Mesen2 ProcessPendingDma
- [x] `ref/Mesen2-master/Core/NES/NesCpu.h` — DMA state definitions
- [x] `ref/Mesen2-master/Core/NES/APU/DeltaModulationChannel.cpp` — DMC Load/Reload DMA trigger

**Questions to answer after reading:**

1. **Load DMA** (triggered by $4015 write): What is the exact halt timing?

   **Answer**: Load DMA is scheduled to halt on a **GET cycle**. Normally 3 cycles (halt+dummy+read).
   If delayed by an odd number of write cycles → 4 cycles (halt+dummy+alignment+read).
   There is a 2–3 APU cycle delay after the $4015 write before the DMA actually starts (Mesen2: `_transferStartDelay`).
   Delay value: `(CycleCount & 1) == 0 ? 2 : 3` (based on CPU cycle parity).

2. **Reload DMA** (triggered by auto-empty buffer): What is the exact halt timing?

   **Answer**: Reload DMA is scheduled to halt on a **PUT cycle** (opposite of Load DMA!).
   Normally 4 cycles (halt+dummy+alignment+read).
   If delayed by an odd number of write cycles → 3 cycles (halt+dummy+read).
   Reload DMA is triggered directly by the DMC output unit when `_bitsRemaining` reaches zero via
   `StartDmcTransfer()`, with no additional delay (unless `_transferStartDelay > 0`,
   meaning it was just enabled via $4015, in which case ProcessClock delays the trigger).

3. **OAM DMA** halt + alignment rules? Conditions for 513 vs 514 cycles?

   **Answer**:
   - OAM DMA attempts to halt on the first CPU cycle after the $4014 write (can only halt on a read cycle)
   - **513 cycles**: halt falls on a PUT cycle → next cycle is GET → can begin reading immediately
     = 1 halt + 256 get/put pairs
   - **514 cycles**: halt falls on a GET cycle → next cycle is PUT → alignment needed
     = 1 halt + 1 alignment + 256 get/put pairs
   - Mesen2: uses `getCycle = (CycleCount & 1) == 0` in the ProcessPendingDma main loop
     to determine whether alignment is needed

4. **What happens when DMA halt occurs during a write cycle?**

   **Answer**: The CPU uses the RDY input for halting. RDY only takes effect on read cycles.
   If the CPU is in a write cycle, the halt request is ignored and the DMA unit tries again next cycle.
   Maximum delay is 3 cycles (RMW has 2 consecutive writes, interrupt sequence has 3).
   **DMA is only triggered from Mem_r/ZP_r, never from Mem_w/ZP_w.**

5. **Does the phantom read occur on all halt/dummy/alignment cycles?**

   **Answer**: **Yes, all three types of no-op cycles execute a phantom read**.
   After the CPU is halted by RDY, it repeats the address of its last read. All no-op cycles
   (halt, dummy, alignment) are visible bus reads with full side effects:
   - `$2002`: clears the VBL flag
   - `$4015`: clears the frame counter IRQ flag
   - `$4016/$4017`: on NES-001 multiple consecutive reads count as only 1 shift (joypad /OE stays asserted),
     but on RF Famicom each cycle clocks independently
   - Address mixing quirk: if the CPU halts while reading $4000-$401F, bits 4-0 of the DMA address
     mix with bits 4-0 of the CPU address, potentially accidentally triggering 2A03 internal registers

### Key Finding: GET/PUT Difference Between Load and Reload DMA

| Type | Scheduled halt phase | Normal cycles | After odd-count delay |
|------|----------------------|---------------|-----------------------|
| Load DMA ($4015 write) | GET cycle | 3 (H+D+R) | 4 (H+D+A+R) |
| Reload DMA (buffer empty) | PUT cycle | 4 (H+D+A+R) | 3 (H+D+R) |
| OAM DMA ($4014 write) | Next read cycle | 513 or 514 | N/A |

### Key Finding: Mesen2 Load DMA Parity Formula

```cpp
// DeltaModulationChannel.cpp SetEnabled():
if((_console->GetCpu()->GetCycleCount() & 0x01) == 0) {
    _transferStartDelay = 2;
} else {
    _transferStartDelay = 3;
}
```

Our BUGFIX31 formula:
```csharp
dmcLoadDmaCountdown = (apucycle & 1) != 0 ? 2 : 3;
```

**Potential issue**: We use `apucycle & 1`; Mesen2 uses `CycleCount & 1`.
If the two parities are inconsistent, the delay value will be wrong. After moving DMA to before-Start,
this should be changed to `cpuCycleCount & 1` to fully match Mesen2.

### Key Finding: ProcessPendingDma Position and getCycle Parity

**DMA-after-Start (current)**:
```
Mem_r → StartCpuCycle(CC++) → ProcessPendingDma
  halt: StartCpuCycle(CC++) → EndCpuCycle
  loop: getCycle = (CC & 1) == 0  ← CC is 1 more than Mesen2 (the main Mem_r CC++)
```
→ getCycle parity is **opposite** to Mesen2

**DMA-before-Start (target)**:
```
Mem_r → ProcessPendingDma → StartCpuCycle(CC++)
  halt: StartCpuCycle(CC++) → EndCpuCycle
  loop: getCycle = (CC & 1) == 0  ← CC is exactly consistent with Mesen2
```
→ getCycle parity is **consistent** with Mesen2

---

### Step 1+2: Move ProcessPendingDma + Fix Parity (must be done together)

**Goal**: Make getCycle parity consistent with Mesen2, fixing all formulas that depend on parity

#### Change 1: MEM.cs — Move ProcessPendingDma before StartCpuCycle in Mem_r/ZP_r

```csharp
// MEM.cs — Mem_r
static byte Mem_r(ushort address)
{
    cpuBusAddr = address;
    cpuBusIsWrite = false;
    if (dmaNeedHalt) ProcessPendingDma(address);  // ← moved before Start
    StartCpuCycle(true);
    byte val = mem_read_fun[address](address);
    if (address != 0x4015) cpubus = val;
    EndCpuCycle(true);
    return val;
}

// MEM.cs — ZP_r (same change)
static byte ZP_r(byte addr)
{
    cpuBusAddr = addr; cpuBusIsWrite = false;
    if (dmaNeedHalt) ProcessPendingDma(addr);  // ← moved before Start
    StartCpuCycle(true);
    byte val = NES_MEM[addr]; cpubus = val;
    EndCpuCycle(true);
    return val;
}
```

#### Change 2: APU.cs — Change Load DMA countdown to use cpuCycleCount

BUGFIX31's `(apucycle & 1)` should be changed to `(cpuCycleCount & 1)` to precisely match Mesen2:

```csharp
// APU.cs — DMC enable branch in apu_4015()
// Mesen2: (GetCycleCount() & 0x01) == 0 ? 2 : 3
dmcLoadDmaCountdown = (cpuCycleCount & 1) == 0 ? 2 : 3;
```

**Note**: apu_4015 is called from the write handler in Mem_w.
Mem_w's StartCpuCycle(false) has already incremented CC, so at this point cpuCycleCount is
"the value for the current cycle", consistent with Mesen2's GetCycleCount().

#### Change 3: No change needed — OAM DMA alignment auto-corrects

After moving DMA to before-Start, getCycle parity becomes consistent with Mesen2.
OAM DMA's alignment check `getCycle = (cpuCycleCount & 1) == 0`
will automatically produce the correct 513/514 cycles.

**Key reasoning**: Hardware-wise, OAM DMA halt falls on a PUT cycle → 513 (next is GET, can read).
Under DMA-after-Start, the flipped parity happened to compensate, giving 513/514 correctly.
After fixing the parity, the same values naturally produce the correct result.

#### Verification

```bash
# Build
powershell -NoProfile -Command "& 'C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe' ..."

# Critical DMA tests (most likely to be affected)
./AprNes/bin/Debug/AprNes.exe --wait-result --max-wait 30 --rom nes-test-roms-master/checked/sprdma_and_dmc_dma/sprdma_and_dmc_dma.nes
./AprNes/bin/Debug/AprNes.exe --wait-result --max-wait 30 --rom nes-test-roms-master/checked/cpu_interrupts_v2/4-irq_and_dma.nes
./AprNes/bin/Debug/AprNes.exe --wait-result --max-wait 30 --rom nes-test-roms-master/checked/dmc_dma_during_read4/double_2007_read.nes

# Full regression
python run_tests.py -j 10
```

**Goal**: 174/174 blargg with no regressions

#### Expected Risks

| Test | Risk | Reason |
|------|------|--------|
| 4-irq_and_dma | **High** | OAM DMA cycle count changes (513↔514) |
| sprdma_and_dmc_dma | **High** | DMC+OAM overlap parity changes |
| double_2007_read | **Medium** | Load DMA countdown changes |
| dma_2007_read | **Medium** | DMA phantom read timing changes |

If 4-irq_and_dma fails: OAM alignment may need fine-tuning.
Before adjusting, confirm: is the failure 513→514 or 514→513? Verify with a trace log.

---

### Step 3: Distinguish Load DMA vs Reload DMA Halt Phase

**Prerequisite**: Step 1+2 complete and 174/174 with no regressions

**Hardware behavior (NESdev Wiki documentation)**:
- **Load DMA**: scheduled to halt on GET cycle → normally 3 cycles
- **Reload DMA**: scheduled to halt on PUT cycle → normally 4 cycles
- Currently we do not distinguish between the two halt phases

**Analysis**:
Currently `dmcStartTransfer()` uniformly sets `dmaNeedHalt = true`.
In ProcessPendingDma the halt cycle does not distinguish Load/Reload — both halt at the next read cycle.
This may already naturally match Load DMA (GET cycle halt), but Reload DMA should halt on a PUT cycle.

**Possible changes**:
```csharp
// APU.cs: distinguish Load and Reload
static bool dmcIsReloadDma = false;  // true = Reload (PUT halt), false = Load (GET halt)

// In clockdmc when buffer empty triggers:
dmcIsReloadDma = true;
dmcStartTransfer();

// In apu_4015 when $4015 write triggers (after Load countdown expires):
dmcIsReloadDma = false;
dmcStartTransfer();
```

```csharp
// MEM.cs ProcessPendingDma: Reload DMA halts on PUT cycle
// If dmcIsReloadDma && getCycle: this is a GET cycle, need to wait for PUT
// Specific implementation requires adding parity check in the halt cycle logic
```

**Note**: This step may be complex and requires careful study of how ProcessPendingDma's halt logic
can distinguish the different halt phases for Load vs Reload. Mesen2 does not explicitly distinguish
the two (the processCycle lambda handles both uniformly), possibly relying on `_transferStartDelay` timing
to naturally cause Load DMA to trigger on a GET cycle.

**Verification**: 174/174 + AccuracyCoin P13 DMADMASync_PreTest

---

### Step 4: P13 Phantom Read Side Effects

**Prerequisite**: Steps 1–3 complete

P13 tests require DMA phantom reads to trigger side effects at the correct timing:
- `$2002` read clears VBL flag (DMA + $2002 Read)
- `$4015` read clears frame counter IRQ flag (DMA + $4015 Read)
- All no-op cycles (halt, dummy, alignment) execute phantom reads

**Already implemented**: The phantom reads in ProcessPendingDma already use
`mem_read_fun[readAddress](readAddress)` — halt/dummy/alignment all execute them.

**Key**: After Steps 1–3 fix the parity, phantom reads land on the correct cycles,
and the observation timing of side effects should be automatically corrected.

**If issues remain**: Check that phantom reads are correctly executed between StartCpuCycle and EndCpuCycle
(not before Start).

**Verification**: P13 DMA + $2002 Read (err=2→PASS), DMA + $4015 Read (err=2→PASS)

---

### Step 5: DMC DMA + OAM DMA Overlap / DMA Abort

**Prerequisite**: Step 4 complete

**Hardware behavior (NESdev Wiki)**:
- DMC DMA and OAM DMA are independent; they only conflict when both need bus access on the same cycle
- **Conflict: DMC has priority**: OAM is paused, realigns on the next cycle → overlap typically adds +2 cycles
- **Special cases at end of OAM**:
  - DMC on the second-to-last PUT: +1 cycle
  - DMC on the last PUT: +3 cycles
- **Aborted DMA**: $4015 stops a reload DMA 1 APU cycle before it's scheduled → DMA aborts immediately after starting
  - Only 1 cycle (halt only); if blocked by a write cycle, 0 cycles

**To verify**: Whether the DMC/OAM interaction logic in ProcessPendingDma correctly handles the above cases.

**Abort mechanism**: We already have a `dmcAbortDma` flag. Confirm:
- Whether abort correctly consumes only the halt cycle
- Whether DMC overlap at the end of OAM DMA correctly handles the +1/+3 cases

**Verification**: P13 remaining 6 items

---

### Step 6: SH* Instruction DMA Interaction (P10)

**Prerequisite**: Step 5 complete

**Hardware behavior (NESdev Wiki)**:
> DMA can only halt on CPU read cycles. Write cycles delay halt up to 3 cycles.

**P10 SH* tests (err=7)**: 5 SH* instructions (SHA/SHX/SHY/SHS) write `A & X & (H+1)`.
Tests expect: the DMA phantom read **between** SH*'s write cycles (delayed by writes)
changes the bus state, causing the H AND masking to be eliminated.

**Analysis**: Our ProcessPendingDma is only called from Mem_r/ZP_r.
If a DMA request is generated in a write cycle's apu_step() (because StartCpuCycle
in Mem_w also calls apu_step), dmaNeedHalt will be set but won't trigger until the next Mem_r.
This naturally implements "no halt during write cycles" behavior.

**Issue**: Do SH* instructions in CPU.cs use Mem_w correctly for write cycles?
If SH*'s page-cross dummy read also uses Mem_r, DMA might trigger there
(too early). The SH* instruction implementation in CPU.cs needs to be checked.

**Verification**: P10 5 SH* tests

---

### Step 7: P20 CPU Behavior 2

**Prerequisite**: Steps 1–5 complete

The 2 failing P20 tests both use DMA sync as a precondition:
- Instruction Timing (err=2): DMADMASync precondition
- Implied Dummy Reads (err=3): DMADMASync precondition

If Steps 1–5 fix DMA timing, the preconditions will pass automatically,
and the actual instruction behavior being tested (which we already implement correctly) should PASS.

**Verification**: P20 4/4 PASS

---

### Step 8: P12 IRQ Flag Latency Test E

**Prerequisite**: Steps 1–7 complete

Test E uses a DMC DMA sync loop to precisely align CPU timing.
Each DMC DMA steal takes 3–4 cycles; if the Load/Reload cycle count is wrong,
the accumulated drift after multiple DMAs will desync (~12 cycles).

If the DMA timing from Steps 1–3 is fully correct (Load=3, Reload=4),
the accumulated drift should be eliminated.

**Verification**: P12 Test E no longer hangs (SKIP → PASS)

---

## Workflow

### Before Each Step

1. Confirm on the latest commit (`git status` is clean)
2. Read relevant documentation (question list from Step 0)
3. Understand the current code behavior and the impact of the changes

### After Each Step

1. Build: `powershell -NoProfile -Command "& 'C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe' ..."`
2. Existing tests: `python run_tests.py -j 10` → 174/174
3. AC tests: `bash run_tests_AccuracyCoin_report.sh` → record score
4. If there is progress (AC +1 or more and no blargg regressions):
   - Update `MD/AccuracyCoin_TODO.md`
   - Add `bugfix/` documentation
   - `git commit` + `git push`
5. Update progress in this document (check off checkboxes)

### Handling Regressions

- **blargg regressions ≤ 3**: Analyze whether a compensation hack has exposed another bug; fix and continue
- **blargg regressions > 3**: Pause, return to Step 0 and re-read documentation; understanding may be wrong
- **AC regressions**: If no blargg regressions, AC regressions are usually acceptable (temporary); continue to next step

---

## Progress Tracking

| Step | Status | blargg | AC | Date | Notes |
|------|--------|--------|-----|------|-------|
| 0 Research | Not started | — | — | | |
| 1 DMA move | Not started | — | — | | Must be done together with Step 2 |
| 2 Parity fix | Not started | — | — | | Together with Step 1 |
| 3 Reload DMA | Not started | — | — | | |
| 4 Phantom Read | Not started | — | — | | |
| 5 DMC+OAM overlap | Not started | — | — | | |
| 6 SH* RDY | Not started | — | — | | |
| 7 P20 CPU | Not started | — | — | | |
| 8 P12 Test E | Not started | — | — | | |

---

## Reference List

| Path | Content | Priority |
|------|---------|----------|
| `ref/DMA - NESdev Wiki.html` | DMA timing main specification | Must read |
| `ref/DMC_DMA timing and quirks - nesdev.org.html` | DMC DMA advanced details | Must read |
| `ref/APU DMC - NESdev Wiki.html` | DMC behavior | Must read |
| `ref/Mesen2-master/Core/NES/NesCpu.cpp` L317-448 | Mesen2 DMA engine | Reference |
| `ref/Mesen2-master/Core/NES/NesCpu.h` | DMA state definitions | Reference |
| `ref/Mesen2-master/Core/NES/APU/NesApu.cpp` | DMC trigger | Reference |
| `AprNes/NesCore/MEM.cs` | Current DMA engine | Working file |
| `AprNes/NesCore/APU.cs` | DMC control | Working file |
| `MD/AccuracyCoin_TODO.md` | Test status tracking | Progress record |
