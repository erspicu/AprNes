# Master Clock Architecture Study

**Purpose**: Analyze the limitations of the existing timing architecture and plan the Master Clock refactoring direction
**Date**: 2026-03-07

---

## Existing Architecture: CPU-Driven Tick Model

The current minimum time unit is the CPU cycle. Each memory access (`Mem_r()`/`Mem_w()`) calls `tick()`, which advances all subsystems at once:

```csharp
// MEM.cs - tick()
static void tick()
{
    // promote NMI delay → nmi_pending
    ppu_step_new();  // PPU dot 1
    ppu_step_new();  // PPU dot 2
    ppu_step_new();  // PPU dot 3
    apu_step();      // 1 APU half-cycle
}
```

**Characteristics**:
- 1 CPU cycle = 3 PPU dots = 1 APU step, all tied together
- The CPU opcode switch executes synchronously; each `Mem_r()`/`Mem_w()` triggers one tick
- No independent clock scheduler; the CPU drives everything

---

## Sub-Cycle Approximation

Many places currently use "approximation" to simulate sub-cycle behavior:

| Behavior | Current approach | Real hardware behavior |
|------|---------|-------------|
| DMC DMA stolen cycles | `dmc_stolen_tick()` runs extra ticks, approximates GET/PUT with `cpuBusIsWrite` | Master Clock precisely schedules the M2 phase of each stolen cycle |
| $2002 flag stagger | sprite flags dot 1, VBL dot 2 (BUGFIX45) | M2 rise reads VBL, M2 fall reads sprite (difference of 7.5 master clocks) |
| DMA halt/alignment | `cpuBusIsWrite` determines read/write cycle | M2 duty cycle 15/24 determines GET(high)/PUT(low) |
| NMI 1-cycle delay | `nmi_delay` → promoted on next tick | NMI edge is detected at a specific master clock edge |
| Load DMA parity | `apucycle & 1` determines delay 2 or 3 | CPU cycle count parity corresponds to M2 phase |
| OAM DMA bus state | `cpuBusAddr`/`cpuBusIsWrite` tracking | Actual M2 phase determines bus direction |

**Limitation**: These approximations are sufficient in most cases, but cannot achieve M2 rise/fall edge precision. Almost all of AccuracyCoin's remaining 17 FAILs are stuck in scenarios that require distinguishing M2 phase.

---

## True NES Clock Relationships

```
Master Clock = 21.477272 MHz (NTSC)

CPU  = Master / 12 = 1.789773 MHz
PPU  = Master / 4  = 5.369318 MHz
APU  = Master / 24 = 0.894886 MHz (= CPU / 2)

1 CPU cycle = 12 master clocks = 3 PPU dots
1 APU cycle = 24 master clocks = 2 CPU cycles = 6 PPU dots
```

### M2 Duty Cycle

The M2 signal of the CPU (similar to a clock enable) has a duty cycle of 15/24 on the RP2A03G:

```
Master clock: |0|1|2|3|4|5|6|7|8|9|A|B|  (12 clocks per CPU cycle)
M2 signal:    |_|_|_|‾|‾|‾|‾|‾|‾|‾|‾|‾|  (low 3, high 9 → ~15/24 over 2 phases)

M2 rise: master clock 3  → CPU read (GET) begins
M2 fall: master clock 12 → CPU write (PUT) begins, register latch
```

- **M2 high (GET phase)**: Data bus driven by external device; CPU reads
- **M2 low (PUT phase)**: CPU drives the data bus; external device latches

### Relationship Between DMA and M2 Phase

The DMA controller (internal to the 2A03) determines behavior at specific M2 phases:
- **Halt**: Waits until the next GET cycle before stopping the CPU
- **Alignment**: If halt occurs during a PUT cycle, waits one extra cycle to align
- **Phantom reads**: The address on the CPU bus during DMA idle periods determines the phantom read target

AccuracyCoin's `DMADMASync_PreTest` precisely tests these phase behaviors; our approximation model is off by 1 cycle.

---

## Target Master Clock Architecture

### Core Concept

```
while (running) {
    master_clock++;

    // PPU: advance 1 dot every 4 master clocks
    if (master_clock % 4 == 0)
        ppu_dot();

    // CPU M2 edges: one complete cycle every 12 master clocks
    if (master_clock % 12 == 3)   // M2 rise
        cpu_m2_rise();            // GET phase begins
    if (master_clock % 12 == 0)   // M2 fall (next cycle boundary)
        cpu_m2_fall();            // PUT phase begins

    // APU: every 24 master clocks
    if (master_clock % 24 == 0)
        apu_tick();
}
```

### CPU State Machine Conversion

The current CPU is a synchronous giant switch:

```csharp
// Current: synchronous execution, each Mem_r/Mem_w triggers tick
case 0xAD: // LDA absolute
    ushort1 = (ushort)(Mem_r(r_PC++) | (Mem_r(r_PC++) << 8));
    r_A = Mem_r(ushort1);
    // ...
    break;
```

Needs to be converted to a pausable/resumable state machine:

```csharp
// Target: advance one step per master clock
enum CpuState { FetchOpcode, FetchLow, FetchHigh, Execute, ... }

void cpu_step() {
    switch (state) {
        case CpuState.FetchOpcode:
            opcode = bus_read(r_PC++);
            state = decode(opcode);
            break;
        case CpuState.FetchLow:
            addr_lo = bus_read(r_PC++);
            state = CpuState.FetchHigh;
            break;
        // ...
    }
}
```

### Scope of Impact

| Component | Change volume | Description |
|------|--------|------|
| CPU.cs | **Extremely large** | ~5000-line giant switch converted to state machine |
| MEM.cs | **Large** | tick() removed, replaced by master clock scheduler |
| PPU.cs | **Medium** | ppu_step_new() mostly unchanged, but call timing changes to independent scheduling |
| APU.cs | **Medium** | apu_step() unchanged; DMC DMA converted to precise master clock scheduling |
| IO.cs | **Small** | Register dispatch unchanged |
| JoyPad.cs | **Small** | Strobe timing naturally precise |

---

## Possible Incremental Refactoring Paths

### Option A: Full Rewrite (High risk, highest reward)

Directly build a master clock scheduler; convert CPU to a state machine.

- **Pros**: One-shot solution; clean architecture
- **Cons**: CPU.cs 5000 lines fully rewritten; extremely high regression risk
- **Effort**: Very long

### Option B: M2 Phase Tracking (Medium risk, highly targeted)

Without changing the tick() structure, distinguish M2 rise/fall phase within each tick:

```csharp
static void tick() {
    m2_phase = M2Phase.Rise;  // GET
    ppu_step_new();
    ppu_step_new();
    m2_phase = M2Phase.Fall;  // PUT (approximately at dot 2)
    ppu_step_new();
    apu_step();
}
```

DMA decisions read `m2_phase` instead of `cpuBusIsWrite`.

- **Pros**: Minimal changes; targeted at DMA issues
- **Cons**: Still an approximation (M2 rise/fall position within 3 PPU dots is hardcoded)
- **Estimated reward**: May resolve P13 preconditions (+6~+12)

### Option C: Micro-Tick Subdivision (Medium-low risk)

Subdivide tick() into sub-ticks without fully converting to master clock:

```csharp
static void tick() {
    // 12 master clocks per CPU cycle
    for (int m = 0; m < 12; m++) {
        if (m % 4 == 0) ppu_step_new();
        if (m == 3) on_m2_rise();
        if (m == 0) on_m2_fall();
    }
    apu_step();
}
```

- **Pros**: Improved internal precision of tick(); external API unchanged
- **Cons**: Performance decrease (12x loop); PPU/APU need adaptation
- **Estimated reward**: Theoretically can resolve all M2-related issues

---

## Expected Rewards

| Fix target | Count | Required precision |
|----------|------|-----------|
| P13 DMA preconditions | 6 | Precise DMA halt/alignment with M2 phase |
| P10 SH* DMA bus conflict | 5 | Precise bus state during DMA |
| P20 Implied Dummy Reads | 1 | Same as P13 preconditions |
| P14 DMC Channel | 1 | Full DMC DMA behavior |
| P14 APU Reg Activation | 1 | DMA bus + $4000 range detection |
| P14 Controller Strobing | 1 | Precise PUT/GET parity |
| P12 IFlagLatency | 1 | Elimination of accumulated DMC DMA error |
| P19 PPU per-dot | 3 | Per-dot OAM evaluation (only Option A can resolve) |
| **Total** | **19** | |

Options B/C are estimated to resolve 12~15 items; Option A can theoretically resolve all 19 (achieving 136/136).

---

## Reference Implementations

### Mesen2 (C++)

- `NesConsole.cpp`: Master Clock scheduler; CPU/PPU/APU each have their own `Run()` method
- `NesCpu.cpp`: CPU is not a state machine; uses `Exec()` + callbacks to yield at each bus access
- `NesPpu.cpp`: Independent dot-by-dot execution
- `NesApu.cpp`: `ClockDmc()` called at precise master clock timing

---

## Conclusion

The current **blargg 100% + AccuracyCoin 87% (118/136)** represents the practical ceiling of the CPU-driven tick architecture.
The root causes of the remaining 17+1 FAILs are concentrated in M2 phase precision, and can only be broken through architectural refactoring.

The recommendation is to start with **Option B (M2 Phase Tracking)** to validate with minimal changes whether the P13 preconditions can pass.
If successful (+6~+12), then decide whether to further pursue Option A's full Master Clock implementation.
