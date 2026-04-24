# Why AprNes Uses Very Little Catch-Up and Leans on Structural Optimisation Instead

## Introduction

In emulator design, `catch-up` is often a very natural idea:

- let one component run ahead
- wait until interaction, observation, or synchronization is needed
- then bring the other components forward to the correct point in time

This works well in many coarse-grained emulators, because it avoids the cost of moving every subsystem forward together at every tiny step.

But there is an important reality here:

**the more precise and tightly coupled the timing model becomes, the smaller the safe design space for `catch-up` becomes.**

AprNes is on the high-precision side of that spectrum.  
Its design direction is close to the `TriCNES` style of thinking, not only in the PPU but also in how CPU, PPU, APU, and mapper timing relationships are preserved at a finer level.

That leads to a practical conclusion:

- `catch-up` is not completely forbidden
- but the places where it can be used safely are very limited
- and once it is used too broadly, correctness risk rises quickly

So the core strategy of AprNes is not "use lots of catch-up to save work."  
Instead, it is:

**keep `catch-up` limited to a few places where the boundaries are clear and the hardware meaning is explicit, and spend the rest of the optimisation effort on the structure of the code itself.**

This article explains why that design decision makes sense.

## Catch-Up Is Often Very Useful in Coarser Models

If an emulator is based on:

- `per-frame`
- `per-scanline`
- or an instruction-level CPU-driven model

then `catch-up` is often practical.

That is because such models already tolerate a fair amount of approximation:

- many intermediate states are not preserved
- many side effects do not need to be observed at very fine timing points
- many events only need the larger boundary result to be correct

Under those conditions, you can often let one subsystem lag behind and bring it forward later without breaking too much.

## But in a High-Precision Model, the Catch-Up Space Shrinks Fast

The key difference in a model like AprNes is that more intermediate moments are treated as meaningful.

For example, in this kind of model:

- a register write may not take effect immediately, but only after a few phases
- some PPU or mapper behaviors are only correct at very specific dots, half-steps, or boundaries
- some open-bus, latch, or pipeline states cannot be reconstructed casually afterward
- some IRQ, A12, or OAM corruption logic becomes wrong as soon as the wrong boundary is crossed

That means `catch-up` is no longer just:

- add a few cycles
- advance by some amount of time

It becomes:

- did the delayed advancement preserve all intermediate side effects
- did it preserve the correct event order
- did it avoid crossing boundaries that should never have been crossed

So in a high-precision design, `catch-up` is itself a high-risk technique.

## Catch-Up Has a Cost of Its Own

People often think of `catch-up` as a performance trick, but that is only true in some models.

In a high-precision system, `catch-up` often comes with its own overhead:

- extra synchronization logic
- extra timestamp or counter management
- extra rules for replayed side effects or deferred state commitment
- extra function boundaries and control flow
- extra verification cost

So `catch-up` is not a free shortcut.  
In many highly accurate designs, it may actually:

- increase hot-path complexity
- hurt JIT, inlining, and I-cache behavior
- raise the risk of correctness regressions

Once the model itself is already detailed, those costs can become more painful than the work `catch-up` was supposed to save.

## AprNes Keeps Only a Very Small Amount of Catch-Up

Looking at the current structure, AprNes does not reject `catch-up` completely.  
It still uses a small number of forms that are local, explicit, and tightly constrained by hardware semantics.

The most typical examples are these.

### 1. Small fixed master-clock pushes during PPU register access

In `PPU.cs`, some register handlers trigger a fixed amount of master-clock advancement:

- `$2002` read uses `nestedTick7Fn()`
- `$2007` read and write use `nestedTick7Fn()`
- `$2004` read uses `nestedTick7Fn()`
- `$2000` write uses `nestedTick2Fn()`

This is essentially a **very small, sharply bounded form of catch-up**:

- not "advance the whole system by an arbitrary amount"
- but "this hardware operation is defined to push forward by a fixed number of master clocks"

That kind of local catch-up is much safer because:

- the behavior is clearly defined
- it maps naturally to hardware meaning
- the risk is much lower than broad delayed synchronization across large time ranges

### 2. Deferred state commitment

Another catch-up-like mechanism is delayed commitment rather than immediate effect, for example:

- delayed scroll updates for `$2005`
- delayed `t -> v` copying for `$2006`
- delayed mask and emphasis updates for `$2001`
- the phased `$2007` state machine for reads, writes, and address increment

The point here is not to erase detail.  
It is to:

- record a pending state first
- then commit it at the exact phase, dot, or step where the hardware model says it becomes visible

This is a controlled form of delay, but it is not broad, loose catch-up.  
It is closer to **precise timing-point management**.

### 3. Sample-rate catch-up in the APU Pure Digital output path

There is another explicit use of catch-up in `APU.cs`: `ApuOutputCatchup()`.

It is used only by the `AudioMode == 0` Pure Digital output path. The APU itself still advances every CPU/APU cycle:

- pulse, triangle, noise, and DMC timers
- the frame counter
- DMC DMA delays and status updates
- controller strobe and shift timing
- length, envelope, and sweep timing

What is not computed every APU cycle is the final mix and sample emission.  
`ApuOutputCatchup()` adds `APU_SAMPLE_RATE` to `_sampleAccum` every cycle and only computes `mapperExpansionAudio` and calls `generateSample()` once the accumulator reaches `_cpuFreqInt`.

In other words, the audio hardware state remains fine-grained, but 44.1 kHz sample output is delayed until the next sample boundary.  
On NTSC, with a CPU rate around 1.79 MHz and a 44.1 kHz output rate, that means roughly one output sample every 40 CPU cycles.

This is a relatively safe catch-up boundary because it does not skip APU hardware state advancement.  
It only avoids producing intermediate mixed samples that are not externally observed.

By contrast, `AudioMode > 0` uses `ApuOutputPushPlus()`, which pushes the main APU and expansion audio into AudioPlus on every APU cycle.  
That shows the design boundary clearly: Pure Digital can use sample-rate catch-up, while more detailed audio reconstruction goes back to per-cycle output.

### 4. Internal APU delays are still precise timing events

The APU also contains many delayed effects, but they are closer to hardware event scheduling than broad catch-up:

- `$4015` DMC enable and disable are delayed by `dmcStatusDelay` for 3-4 cycles
- DMC load DMA is scheduled through `dmcLoadDmaCountdown`
- `$4015` reads clear the frame interrupt on the next PUT cycle
- `$4017` frame-counter reset is delayed by 3 or 4 cycles depending on GET / PUT phase
- length-counter reloads are carried as flags and committed at the correct quarter / half-frame boundary

The common idea is this: **the APU is not allowed to lag behind and catch up later; observable state changes are committed on the exact cycle where the hardware model says they become visible.**

## Why AprNes Shifts Its Effort Toward Optimising the Code Structure Itself

For AprNes, the more reliable path is not:

- let more components lag behind and catch them up later

but rather:

- make the correct fine-grained model itself run faster

That shifts the optimisation focus away from `catch-up` and toward:

- main-loop structure
- phase layering
- static dispatch
- region-specific fast paths
- specialised PPU hot paths
- JIT, IL, and I-cache friendliness

In other words, AprNes chooses this philosophy:

> do not loosen the timing model and try to recover correctness later with catch-up;  
> keep the detailed model, and reshape the detailed model into something the machine can execute more efficiently.

## What Is Special About the Handling in Main.cs

If you look at it casually, `Main.cs` may seem like "the main loop."  
From an architecture and performance perspective, though, it is really a deliberately shaped **timing executor**.

Its special qualities mainly come from the following points.

### 1. It does not use one generic tick loop for all regions

`run()` does not route everything through a single generic `MasterClockTick()` and branch every time.  
It dispatches once at the top into:

- `Run_NTSC()`
- `Run_PAL()`
- `Run_Dendy()`
- `Run_FDS()`

The special thing here is that:

- region differences are moved out of the hot path
- the loop does not keep asking `if (Region == ...)`
- each region gets its own timing-friendly shape

This is a classic example of **static dispatch used to remove hot-path branches**.

### 2. NTSC, Dendy, and FDS use structural unrolling instead of a generic countdown scheduler

Take `Run_NTSC()` as the clearest example.  
Its core path is `MasterClockTickUnrolledNTSC()`.

That is not a generic tick function that runs every master clock and repeatedly checks which of CPU, PPU, and APU should fire.  
Instead, it **spells out the 12-master-clock event sequence directly**.

Why is that special?

- the control flow becomes far more stable
- generic scheduling branches are reduced
- JIT can see a more regular execution shape

So AprNes is not mainly reducing cost through "smarter catch-up."  
It is reducing cost by **writing the already-correct work into a structure that is easier for the machine to execute efficiently**.

### 3. Warm-up aligns the phase before entering the fast path

`WarmUpNTSC()`, `WarmUpFDS()`, and `WarmUpDendy()` are not just slow startup sequences.  
Their real purpose is to align `mcCpuClock` and `mcPpuClock` to the exact starting state expected by the fast path.

That matters because:

- cold-start logic does not have to stay mixed into the hot loop
- the main loop can assume a clean phase alignment
- the unrolled kernel can keep a more stable shape

This is a very practical engineering pattern:

**push awkward work into the cold path so the hot path stays cleaner.**

### 4. NestedTick specialisation prevents register access from destabilising the main loop

The `nestedTick7Fn` and `nestedTick2Fn` mechanism is one of the most important details.

Some PPU register operations need to advance a fixed number of master clocks in the middle of CPU activity.  
If handled badly, those register handlers would call back into a generic `mcTickFn`, which tends to create:

- recursion
- hard-to-predict counter states
- hot-path and cold-path interference

AprNes instead:

- binds the correct region-specific `nestedTick` variants at `Run_X()` entry
- lets register handlers use those specialised variants for fixed advancement
- keeps the boundary between the fast path and the small catch-up windows predictable

This is exactly what allows limited catch-up to exist **without wrecking the shape of the main fast path**.

### 5. PAL, Dendy, and FDS are not treated as NTSC leftovers

This point matters a lot.  
Many emulators support multiple regions, but still let NTSC assumptions leak throughout the core.

AprNes is special here because:

- `masterPerCpu`
- `masterPerPpu`
- warm-up
- nested ticks
- outer unrolling

are all treated explicitly for each region or for FDS.

That is more expensive to engineer, but it avoids carrying a long-term burden where PAL or Dendy correctness depends on patching around NTSC assumptions in the hot path.

## The PPU Structure: How AprNes Turns a Fine-Grained Model into Something Runnable

AprNes does not keep the entire PPU in one giant function.  
Instead, it separates the PPU into three layers.

### 1. `PPU.cs`: state, bus logic, register semantics, and sprite-evaluation foundations

`PPU.cs` is the closest thing to the PPU's semantic layer.

It mainly contains:

- `PpuBusRead()` and `PpuBusWrite()`
- `CIRAMAddr()`, palette cache logic, and PPU RAM behavior
- the `$2000-$2007` register handlers
- `SpriteEvalInit()`, `SpriteEvalTick()`, and `SpriteEvalEnd()`
- `RenderScreen()`

This layer matters because:

- it defines observable PPU behavior
- without forcing every single dot-level hot-path concern into the same body

### 2. `ppu_new.cs`: phase layering, so the question of "when" is separated cleanly

`ppu_new.cs` is the timing-phase layer.

Its main responsibilities include:

- `ppu_step_new()`: choosing the correct dispatch table by scanline state
- `PpuPhase2_DeferredUpdates()`: committing delayed register updates
- `PpuPhase3_Events()`: VBlank, odd-frame, and pre-render events
- the `PpuPhase4_*()` family: sprite evaluation, OAM corruption, sprite fetch, dummy fetch
- `PPU_DATA_Pipeline_Step()`: the `$2007` bus, latch, read/write pipeline
- `ppu_half_step_new()`: shift-register advance, fetch-result commit, sprite-0-hit pipeline, and phase-3 state machine completion

The value of this layer is that it separates kinds of complexity that otherwise become tangled in a fine-grained PPU:

- delayed state commitment
- scanline events
- sprite and OAM logic
- half-step-only commit behavior

So this file is not reducing timing complexity.  
It is **organising timing complexity into manageable phases**.

### 3. `ppu_dispatch.cs`: making dot specialisation real through dispatch tables

`ppu_dispatch.cs` is the most performance-oriented layer.

Its core idea is:

- first split into `visible`, `pre-render`, and `vblank` tables
- then split the visible line further into:
  - `PixelZone`
  - `VisibleTail`
  - `SpriteFetch`
  - `Prefetch`
  - `Dummy`

The goal is not just code readability.  
The goal is:

- let the `0..255` pixel hot zone remove as many impossible branches as possible
- let `256/257/340` keep only scroll, wrap, and delayed final-pixel behavior
- let `258..319`, `320..335`, and `336..339` avoid carrying pixel-composition logic they never need

#### Why `PixelZone` matters so much

`Ppu_Tick_Visible_PixelZone()` is one of the hottest parts of the PPU.

Its key characteristics are:

- it does not over-extract into helpers
- it keeps a large amount of logic inline
- conditions that can never be true in that range are removed entirely

For this region, for example:

- no visible-tail scroll handling is needed
- no scanline wrap is possible
- no VBlank event logic is needed
- tile-fetch, pixel, and sprite-shift gates can be simplified heavily

This is **slot-aware specialisation**:  
the handler does not ask at runtime what kind of dot it is.  
It already exists only for that kind of dot.

#### Non-pixel paths share a skeleton, but do not contaminate `PixelZone`

Another important design choice is that:

- `SpriteFetch`
- `Prefetch`
- `Dummy`
- `VisibleTail`

share some helper skeletons, such as:

- `PpuVisibleAuxBeforePhase4()`
- `PpuDotAuxBeforeStep1Core()`
- `PpuDotAuxStep1()`
- `PpuDotAuxAfterPhase4()`

But `PixelZone` stays mostly inline, so the hottest path is not shaped by generic helper reuse.  
That is a mature performance-engineering decision:

- colder paths may share more
- the hottest path keeps its specialization

#### `PreRender` and `VBlank` are not blindly generic either

`Ppu_Tick_PreRenderLine()` and `Ppu_Tick_VBlankLine()` are colder than visible pixel work, but they still keep their own behavior boundaries:

- pre-render still keeps scroll reset, odd-frame skip, BG fetch, and sprite shifter behavior
- vblank keeps only the universal per-dot state work and the frame-render trigger that truly belongs there

That means the PPU does not need a broad catch-up layer to reconstruct everything "later."  
It tries to execute the right work in the right dot category directly.

## Architectural Highlights in the Other Core Files

Beyond `Main.cs` and the three-layer PPU structure, the other root-level `.cs` files and `Mapper/Mapper004.cs` follow the same direction: keep fine-grained timing, but reshape hot paths into stable execution forms.

### CPU.cs: cycle-level CPU instead of instruction-level catch-up

`CPU.cs` is not an instruction-level model that runs one opcode to completion at a time.  
It keeps the internal 6502 state in `operationCycle`, and the master clock advances the CPU by one CPU cycle at each CPU gate.

That lets CPU execution line up with DMA, NMI, IRQ, and PPU register side effects at cycle granularity instead of reconstructing those effects afterward.  
At the same time, dispatch cost is reduced through a 256-entry function-pointer opcode table:

- the `.NET 10` path uses `delegate* unmanaged<void>` with `UnmanagedCallersOnly`
- the opcode table lives in unmanaged memory instead of a regular managed delegate-array shape
- `InitOpHandlers()` keeps the 16x16 opcode matrix as a `stackalloc` initializer, then copies it into the native table

This is a typical AprNes tradeoff: **do not loosen CPU timing, but make opcode dispatch as flat as possible.**

### MEM.cs / IO.cs: bus and DMA are part of the cycle model

`MEM.cs` is not just a memory accessor layer.  
It models CPU bus state, OAM DMA, DMC DMA, open bus behavior, and controller bus conflicts inside the per-cycle model.

Notable pieces include:

- `DmaOneCycle()` executes exactly one DMA cycle and is called from the master-clock CPU gate
- OAM DMA and DMC DMA use different priority rules on GET and PUT cycles
- DMC implicit abort, phantom reads, and DMA halt state are kept at the bus layer
- `DmaFetch()` models `$4000-$401F` open bus behavior and `$4015/$4016/$4017` bus conflicts
- CPU memory dispatch uses an 8-page table instead of a 65536-entry table, keeping the common dispatch table within one cache line

`IO.cs` normalizes PPU register mirroring before dispatching to PPU, APU, and controller handlers.  
That keeps CPU bus handlers focused on bus semantics; the few fixed timing pushes that are required are performed explicitly by PPU register handlers through `nestedTick`.

### APU.cs / JoyPad.cs: APU timing and controller timing share the same rhythm

`APU.cs` splits `apu_step()` along the TriCNES GET / PUT cycle model:

- GET cycles handle pulse/noise timers, DMC clocking, DMC cooldown, and controller strobe reload
- PUT cycles handle deferred frame-interrupt clear and DMC load-DMA countdown
- both cycles handle DMC `$4015` delay, triangle timing, and the frame counter

There are two important architectural details here.  
First, rare but necessary delayed events are moved behind function-pointer helpers so `apu_step()` is not expanded by cold logic.  
Second, `apuRegister` is a contiguous 16-byte buffer, so halt flags are updated with two `ulong` loads using a SWAR-style pattern instead of many scattered byte reads every cycle.

`JoyPad.cs` also avoids the simplified "shift immediately on read" model.  
It puts the controller's 2-cycle deferred shift into the APU step, while UI/input-thread updates use `Interlocked.Or/And` or a `CompareExchange` fallback so button updates remain lock-free and atomic.

### FDS.cs: FDS is an independent hardware mode, not just another mapper

`FDS.cs` does not force Famicom Disk System behavior into the normal cartridge mapper path.  
In FDS mode, it takes over the relevant CPU memory pages directly:

- `$4020-$40FF` goes through the FDS register dispatcher
- `$6000-$DFFF` maps to 32KB FDS PRG-RAM
- `$E000-$FFFF` maps to BIOS ROM
- the FDS fast path in `Main.cs` replaces `MapperObj.CpuCycle()` with `fds_CpuCycle()`

`fds_CpuCycle()` advances disk I/O, the IRQ timer, and FDS audio every CPU cycle.  
The disk side keeps head delay, byte delay, gap-inserted disk images, CRC state, and disk IRQ behavior; the audio side advances FDS wavetable, modulation, and envelope state into the expansion audio channel.

So FDS is not catching up mapper state after register reads and writes.  
It has its own per-cycle state machine in the main timing path.

### Mapper004.cs: MMC3 A12 / IRQ timing and CHR bank pointers

`Mapper/Mapper004.cs` is a good mapper-side example of the same philosophy.

MMC3 IRQ behavior is driven by PPU A12 edges, not simply by CPU writes.  
This mapper handles that through:

- `PpuClock()`, which checks A12 on `ppuAddressBus` every PPU dot
- `m2Filter`, which counts how long A12 has been low
- a threshold of 10, filtering short background-fetch gaps and short scanline-boundary gaps
- an effective clock only when the sprite-fetch gap is long enough, matching MMC3 scanline-counter behavior

The CHR hot path also moves mapper-mode complexity out of tile fetches and into bank updates:

- when CHR mode 0/1 changes, `UpdateCHRBanks()` precomputes `NesCore.chrBankPtrs[0..7]`
- in the CHR-ROM case, `MapperR_CHR()` only needs `chrBankPtrs[(address >> 10) & 7][address & 0x3FF]`
- in the CHR-RAM case, reads and writes stay direct to `ppu_ram`, avoiding unnecessary ROM-bank logic

That moves mapper complexity to the time of bank switching instead of forcing the PPU tile-fetch hot path to re-evaluate mapper state on every fetch.

## What This Design Really Means

AprNes did not ultimately choose this path:

- use a looser timing model
- then recover compatibility with lots of catch-up

It chose this path instead:

- accept the cost of a high-precision timing model
- then reshape that cost into something more executable through structure

So its optimisation philosophy is closer to:

- reduce hot-path branching
- reduce generic scheduler cost
- separate hot logic from cold logic
- control how helpers affect JIT, inlining, and I-cache behavior
- let fixed timing follow fixed shapes

From an engineering point of view, this is a harder path.  
It is harder to write, harder to refactor, and more likely to require long-term performance tuning than a design that leans heavily on catch-up.  
But under a high-precision timing model, it is often more controllable than loosening the model and trying to pay correctness back later.

## Final Takeaway

If you are a general technical reader, you can think of AprNes's choice like this:

> instead of cutting corners first and paying back the debt later, it tries to reshape the detailed model itself into something that runs better.

If you are an emulator developer, the most important lesson here is not any single trick, but this judgment:

- once the timing model becomes fine enough
- the freedom to use `catch-up` shrinks
- the cost and correctness risk of `catch-up` rise
- and at that point, the higher-value path is often to reorganize the main loop and hot-path structure itself

That is why AprNes uses only a very small amount of catch-up and spends much more effort on:

- main-loop structural optimisation
- PPU dot specialization
- CPU, DMA, and APU cycle-level state machines
- mapper bank-pointer and A12-timing hot-path organization
- JIT, IL, and I-cache friendliness

## Further Reading

### Chinese

- [AprNes 非 JIT 層優化技巧整理](https://github.com/erspicu/AprNes/blob/master/MD/jit/JIT_ICache_Tutorial.md)
- [C# JIT 與 I-Cache 優化教學](https://github.com/erspicu/AprNes/blob/master/MD/jit/AprNes_Optimization_Techniques.md)

### English

- [AprNes Non-JIT Optimisation Techniques](https://github.com/erspicu/AprNes/blob/master/MD/jit/AprNes_Optimization_Techniques_EN.md)
- [C# JIT and I-Cache Optimisation Tutorial](https://github.com/erspicu/AprNes/blob/master/MD/jit/JIT_ICache_Tutorial_EN.md)
