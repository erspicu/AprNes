# A Practical Guide to NES Emulator Timing Models

## What This Article Tries to Answer

If you want to build an emulator, or if you are simply curious about how emulators work, you quickly run into one central question:

> How precisely should an emulator model time?

At first this sounds like a performance problem, but it is also:

- an architecture problem
- a correctness problem
- an engineering cost problem
- a maintenance problem

For systems like the NES, many bugs are not caused by "wrong logic" in the abstract.
They happen because the logic happens at the wrong moment.

Examples include:

- when VBlank begins
- when sprite 0 hit becomes visible
- when MMC3 IRQ counting advances
- when `$2005/$2006/$2007` writes actually take effect
- when open bus, OAM corruption, or palette corruption appears

So the real design question is not only "how fast should my emulator be?"
It is:

> At what level should I model hardware time?

This article walks from the coarsest and fastest timing approaches to very fine-grained hardware-oriented timing, and ends with a more extreme future direction: `Visual6502`-style netlist simulation.

It is written for two kinds of readers:

- people with general technical interest and some basic computing background
- actual emulator developers who need to evaluate tradeoffs before choosing an architecture

## One Core Idea to Keep in Mind

An emulator is not just "logically correct" or "logically incorrect."

There is another axis:

- does the logic happen at the correct time?

The coarser the timing model:

- the faster it usually is
- the easier it usually is to write
- the easier it usually is to maintain
- but the harder it is to reproduce edge-case hardware behavior

The finer the timing model:

- the slower it usually is
- the harder it is to write
- the harder it is to optimize
- but the more capable it is of reproducing real hardware details

So choosing a timing model really means choosing:

- what level of correctness you want
- what engineering cost you are willing to pay

## A Quick Overview Table

| Level | Typical Time Unit | Compatibility Potential | Runtime Speed | Development Difficulty | Typical Use |
|---|---|---|---|---|---|
| 1 | per frame / per scanline | low | very high | very low | teaching, prototypes |
| 2 | per CPU instruction | low to medium | high | low | early prototypes, basic playability |
| 3 | per CPU cycle | medium | medium-high | medium | practical compatibility-focused emulators |
| 4 | CPU cycle + PPU dot | medium-high | medium | medium-high | serious NES compatibility work |
| 5 | master clock / signal / delayed-effect model | very high | low | very high | high-fidelity emulation, hardware study |
| 6 | transistor / netlist | extremely high | extremely low | extremely high | research, verification, preservation |

Now let us go through these one by one.

---

## 1. The Coarsest Timing: Per Frame / Per Scanline

### What It Is

This is the most intuitive approach.

Instead of simulating what happens at each hardware step, you say:

- a frame has passed, update the screen
- a scanline has passed, produce one line of background and sprites

This model focuses more on:

- the result

than on:

- the process

### Advantages

- very fast
- easy to understand
- good for teaching
- good for a first proof of concept

### Weaknesses

It will usually fail on many NES edge cases, because a lot of NES behavior does not happen "at the end of a scanline."
It happens at:

- a specific dot
- a specific CPU cycle
- or a delayed effect a few clocks later

This model tends to handle these badly:

- sprite 0 hit
- sprite overflow
- scanline IRQs
- VBlank edge behavior
- `$2007` read buffering
- open bus
- mid-scanline register effects

### When It Makes Sense

It is reasonable if your goal is:

- to learn emulator structure
- to get something on screen quickly
- to validate basic CPU / memory / rendering flow

But if your goal is a mature NES emulator, this is usually only a starting point.

---

## 2. Instruction-Level Timing: Per CPU Instruction

### What It Is

This is a very common early-stage emulator design.

The CPU executes one instruction, then the rest of the system advances by a matching amount of time:

- CPU executes one instruction taking `N` cycles
- PPU advances by about `N * 3`
- APU advances at instruction boundaries or in chunks

### Advantages

- still very fast
- simple architecture
- enough for many basic ROMs
- relatively easy to debug

### Weaknesses

Many NES events occur in the middle of an instruction, not only after the instruction completes.

That leads to issues like:

- shifted event timing
- incorrect NMI / IRQ edges
- inaccurate PPU register side effects
- inaccurate DMA or bus interaction timing

### Developer Perspective

If you only want:

- ROM booting
- games entering gameplay
- roughly correct graphics

this is a very reasonable first version.

But you should expect that moving from here to finer timing later often requires scheduler refactoring.

---

## 3. CPU Cycle-Accurate: CPU as the Main Clock

### What It Is

This is one of the most practical and common NES emulator approaches.

The core idea is usually:

- advance the CPU by 1 cycle
- advance the PPU by 3 dots
- update the APU relative to CPU cycles
- let mappers synchronize on CPU / PPU boundaries

This is still fundamentally:

- a CPU-centered timing model

but its time resolution is already much finer.

### Advantages

- much better compatibility than instruction-level timing
- still reasonably understandable
- still manageable as a project
- common enough to be a good engineering compromise

### Weaknesses

It still struggles with behaviors that happen at finer PPU sub-phases or with delayed internal hardware effects:

- some events happen within a PPU dot sequence, not just at dot boundaries
- some state changes do not apply immediately
- some mappers need very exact A12 edge visibility

### When It Makes Sense

If your goal is:

- a practical emulator
- good compatibility
- without immediately entering extreme timing complexity

this is often the best balance.

---

## 4. CPU Cycle + PPU Dot: Entering Real PPU Timing

### What It Is

At this level, you are no longer satisfied with:

- "CPU advances, then PPU catches up in a chunk"

Instead, you explicitly model:

- what happens on each PPU dot
- what each scanline region does

You start thinking in terms like:

- visible line
- pre-render line
- vblank line
- background fetch
- sprite evaluation
- sprite fetch
- dummy fetch

### Advantages

- much better handling of NES-specific timing behavior
- easier to map to nesdev documentation and test ROMs
- good foundation for scanline IRQs, sprite hit, and VBlank correctness

### Weaknesses

- PPU code grows quickly
- timing begins to dominate the design
- if you are not careful, you end up with one giant `step()` function full of conditionals

This is also where emulator designs usually start to split into two different styles:

- keep a single large state machine
- or move toward table-driven / specialized handlers

---

## 5. Hardware-Oriented Timing: Master Clock, Delayed Effects, Signal-Style Modeling

This is much closer to what `AprNes` is doing now.

More specifically, `AprNes` eventually adopted an approach close to `TriCNES`.

### What It Is

At this level, you are no longer asking only:

- how many CPU cycles passed?
- what PPU dot am I on?

You start asking much more hardware-like questions:

- does this register write take effect immediately, or a few clocks later?
- is this value currently on a bus, in a latch, or in a pending pipeline stage?
- does this event happen on a full step or a half step?
- does this corruption flag only appear under certain alignment conditions?
- when exactly does the mapper observe the A12 edge?

At this point, your code starts to contain many concepts like:

- delayed update
- pending flag
- latch chain
- phase 1 / phase 2 / phase 3
- full-step / half-step
- bus state machine
- corruption timing

### Why Go This Far

Because some NES behaviors are very hard to model correctly with only a coarse dot-based mental model.

Examples:

- `$2002` read edge behavior
- `$2005/$2006/$2007` delayed effects
- OAM corruption
- palette corruption
- open bus
- MMC3 scanline counter / A12 timing
- sprite evaluation bug behavior

With a coarser model, many of these end up "almost correct."
But on the NES, "almost correct" is often where bugs live.

### The Real Cost of the AprNes / TriCNES Style

This approach gives real benefits, but the price is large.

Once `AprNes` moved in this direction, the practical result was:

- accuracy increased
- performance dropped hard

That is not unusual.
It is exactly what this kind of model tends to do.

Because now you are simulating:

- more states
- more intermediate phases
- more delayed effects
- more details that coarse models normally flatten away

And this cost is not merely linear.

Often you are not just "doing one more check."
You are changing:

- hot-path shape
- cache behavior
- JIT inline shape
- branch predictability

### Why Dispatch Specialization Becomes Natural

Once you choose such a fine timing model, the next question becomes:

> How do I avoid making the emulator unbearably slow?

That is exactly where your current `dispatch table + dot specialization` approach makes sense.

What it does is:

- keep the timing fidelity
- while trying to make each dot handler carry only the logic that can actually happen in that region

For example:

- separate tables for `visible / pre-render / vblank`
- visible split into `PixelZone / SpriteFetch / Prefetch / Dummy / Tail`
- impossible branches removed from each handler

This is a very important idea:

- take a hardware-oriented fine-grained timing model
- then reshape it into something friendlier to JIT and CPU execution

### Who This Model Is For

This is appropriate for:

- developers who truly want high-fidelity NES emulation
- people willing to live in timing-sensitive debugging
- people willing to do profiling, hot-path optimization, and code-shape tuning

It is usually not appropriate for:

- a first emulator project
- people who only want to run most games
- people without time for heavy verification

### One Important Line for Developers

If you choose this route, you are not only writing an emulator.

You are really building:

- a hardware behavior model
- plus a performance engineering project

---

## 6. The More Extreme Future Direction: Visual6502 / Netlist Simulation

### What It Is

At this level, you are no longer modeling "CPU specification" or even "PPU behavior" at a high level.
You are modeling:

- circuit netlists
- transistor / gate / node level behavior

`Visual6502` is the best-known example of taking the 6502 down to transistor/netlist-level simulation.

For emulator developers, this represents an extreme philosophy:

- do not hand-write a high-level timing model
- let the netlist itself define the behavior

### Why It Is Worth Respecting

Because this is the closest you can get to:

- actual original hardware behavior

Its value is not only that it can be "more accurate."
It also matters for:

- verifying undocumented behavior
- studying real hardware internals
- preserving historical systems
- cross-checking higher-level models

### Why It Is Not the Mainstream Path

Because the cost is enormous.

And not only in runtime speed.
Also in:

- modeling difficulty
- data preparation difficulty
- debugging difficulty
- tooling difficulty
- long-term maintainability

If a high-level signal model is already hard to optimize, a netlist model is much harder.

### When It Is Worth Pursuing

This direction is very promising for:

- hardware-research-oriented emulators
- reference engines used to validate higher-level models
- educational hardware visualization platforms
- preservation and verification projects

But if your goal is:

- a practical emulator people actually use day to day

then this is usually not the most pragmatic starting point.

---

## How Should a Real Developer Choose?

### If This Is Your First Emulator

Start with:

- instruction-level timing
- or a CPU-cycle-led model

Because what you first need to learn is:

- CPU / memory / mapper / PPU interaction
- debugging habits
- how compatibility issues are found

Not maximum timing fidelity on day one.

### If Your Goal Is "Playable and Quite Compatible"

Aim for:

- CPU cycle accuracy
- plus sufficiently detailed PPU dot behavior

That is usually the most practical engineering balance.

### If Your Goal Is "High-Fidelity NES"

Then you will likely end up moving toward:

- signal-oriented timing
- delayed effects
- master clock / sub-phase modeling
- something closer to the `TriCNES` style

But accept these three facts first:

1. performance will drop
2. architecture will become much more complex
3. you will spend serious time on optimization later

### If Your Goal Is Hardware Research

Then it makes sense to push toward:

- high-level signal models
- or eventually even netlist / transistor models

But that is beyond the difficulty of a typical emulator project.

---

## A Practical Development Roadmap

If you are seriously building a NES emulator, a sensible progression often looks like this:

### Stage 1: Get Something Running

- basic CPU instruction or cycle execution
- memory map
- basic mappers
- rough PPU flow

### Stage 2: Raise Timing to Practical Quality

- CPU cycle accuracy
- PPU dot-level behavior
- sprite hit / sprite overflow / VBlank timing
- common timing-sensitive paths like MMC3 IRQ

### Stage 3: Start Handling Hardware Edge Cases

- delayed register effects
- `$2007` pipeline behavior
- open bus
- OAM / palette corruption
- finer A12 / bus-state behavior

### Stage 4: If You Truly Want More Fidelity

- master clock / half-step / signal modeling
- dispatch specialization
- extensive profiling
- JIT / code-shape / hot-path optimization

### Stage 5: Research-Grade Direction

- reference-grade signal model
- netlist / transistor-level challenge

---

## How to Evaluate the Cost of a Timing Model

Whenever you choose a timing model, ask five questions:

### 1. What kind of errors am I trying to solve?

If you just have ordinary gameplay bugs, you may not need the finest possible model.

If your problems are things like:

- `$2002` edge behavior
- sprite evaluation bugs
- scanline IRQ timing
- bus glitches

then you probably do need finer timing.

### 2. Can I start coarse and upgrade later?

Often the answer is yes.

And in practice that is usually much more manageable than starting with the finest possible timing model.

### 3. Is My Goal "Play Games" or "Study Hardware"?

Those overlap, but they are not the same goal.

### 4. Can I Actually Afford the Performance Loss?

With very fine timing models, it is dangerous to assume:

- "I will just optimize it later"

Often the slowdown is not small.
It is structural.

### 5. Do I Have a Way to Verify the Result?

The hard part of a high-fidelity model is not only writing it.
It is answering:

- how do I know it is actually more correct?

Without test ROMs, reference comparisons, and profiling, a finer model may not deliver meaningful value.

---

## An Engineering Conclusion for AprNes-Like Projects

A project like `AprNes`, which eventually adopts a `TriCNES`-like approach, is no longer just building a normal compatibility-focused emulator.
It is pursuing:

- high-fidelity timing

That is a valid direction.
But the cost is predictably large.

And practical experience confirms exactly that:

- correctness rises
- performance drops significantly
- then large amounts of work are needed in:
- architectural cleanup
- hot-path specialization
- JIT-friendly tuning
- generic-residue removal

So if someone wants to follow this road, my advice is not "do not do it."
It is:

> Make sure your goal is worth the engineering cost before you commit to this model.

---

## Final Summary

NES emulator timing models are not a straight line from "bad" to "good."

They are a set of tradeoffs:

- the coarser the model, the faster and easier it is
- the finer the model, the more accurate and expensive it becomes

From frame-level and scanline-level approaches, through CPU-cycle and PPU-dot timing, all the way to `TriCNES` / `AprNes`-style hardware-oriented micro-timing, and finally to `Visual6502`-style netlist simulation, every level is answering the same question:

> How much correctness do I want, and what engineering cost am I willing to pay for it?

If you simply want to start building an emulator:

- start with a coarser model

If you want a mature and practical NES emulator:

- CPU cycle accuracy plus PPU dot behavior is often the best overall path

If you want hardware-level fidelity:

- accept that performance, complexity, and verification cost will all rise sharply

If you want to pursue the most extreme future direction:

- netlist / transistor models are deeply valuable
- but they are a research path, not a normal starting point

If this article must be reduced to one line:

> The best timing model is not the one that is finest by default, but the one that matches your goals, your budget, and your ability to verify it.  
