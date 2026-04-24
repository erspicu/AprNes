# Why a "High-Quality Per-Scanline NES Emulator" Is Actually Hard

## Introduction

When people first learn about emulators, they often assume the difficulty ladder looks like this:

- `per-frame` is the simplest
- `per-scanline` comes next
- `per-dot`, `per-cycle`, and `master clock` are the hard, precise approaches

That ordering is not wrong, but it often creates a misleading impression:  
that `per-scanline` is merely a rougher, less accurate, and easier version of emulator timing.

In practice, **if the goal is not a teaching prototype but a fast emulator that still runs a large portion of commercial games under a `per-scanline` timing model, that is already a serious engineering challenge.**

The hard part is not fully rebuilding the hardware.  
The hard part is this:

- you know your model is too coarse
- you still need it to behave as if it were much finer
- and you must avoid turning the whole system into an unmaintainable pile of special cases

This article explains why that path is difficult, why it is still worth doing, what kind of developer it suits, and how to think about it in a disciplined way.

## What Per-Scanline Timing Means

The NES PPU runs many scanlines per frame, and each scanline contains many dots.  
In a fine-grained emulator, you may advance the system dot by dot, cycle by cycle, or even at a master-clock level while coordinating CPU, PPU, APU, mapper logic, and bus side effects.

A `per-scanline` model works differently:

- the main update unit is an entire scanline
- after each line, the emulator processes rendering, IRQ behavior, scrolling, sprites, audio, or other events
- only especially important timing points are refined with local events or special handling

Its core idea is not "recreate every hardware moment precisely."  
Its core idea is:

**use a coarser time model to gain speed and lower complexity, then add a small number of high-value corrections to recover compatibility.**

## Why It Looks Simpler Than It Really Is

### 1. The NES cannot be explained by "update once per line"

Many NES behaviors are not things you can postpone until the end of a scanline.

Examples include:

- `sprite 0 hit`
- MMC3-style IRQ behavior tied to PPU A12 edges
- mid-frame scroll splits
- timing-sensitive PPU register side effects
- DMC / APU interactions and CPU-side timing effects

These are often not just about "which scanline" something happens on.  
They are about "which part of the scanline" something happens in.

That means **the scanline is inherently too coarse as a full explanation of the hardware.**

So the moment you choose `per-scanline`, you are already accepting an important fact:

> your primary timing model cannot fully express the hardware truth.

The real challenge is deciding how to add back the missing information without collapsing into a full `per-dot` architecture.

### 2. The hardest part is deciding what must be restored

The biggest engineering challenge in a `per-scanline` emulator is usually not writing fixes.  
It is deciding:

- which hardware effects are truly essential
- which ones matter only for a few ROMs
- which ones can be approximated safely
- which ones will break an entire class of games if approximated too loosely

This is an architectural judgment problem.

If you judge badly, two failure modes are common:

- you restore too little, and compatibility stays poor
- you restore too much, and the system becomes a hidden `per-dot` emulator with the complexity of both worlds

### 3. Clean architecture can easily decay into patch forest

A lot of `per-scanline` emulators do not fail because the idea is bad.  
They fail because maintainability collapses over time.

Common patterns of decay look like this:

- one game breaks, so a mapper hack is added
- one split timing is wrong, so a line-local special case is added
- one IRQ is slightly off, so a timing offset is introduced
- one status flag is wrong, so extra correction logic is inserted before or after rendering

If there is no deliberate design for an "exception layer" or a "timing correction layer," the result becomes:

- nominally a `per-scanline` emulator
- practically held together by scattered local fixes
- increasingly fragile whenever a new bug appears

That is one of the most real risks of this approach.

## Why This Approach Is Still Worth Pursuing

Even with those risks, `per-scanline` remains valuable.

It is not chasing maximum hardware fidelity.  
It is chasing a different, equally serious objective:

- high execution speed
- strong practical compatibility
- lower total compute cost
- easier deployment on weaker hardware or broad platform targets

This is not simply a compromise. It is a different optimization direction.

If a `master clock` design asks:

> Can I reconstruct the hardware closely enough that its behavior naturally emerges?

Then a high-quality `per-scanline` design asks:

> Can I make a structurally limited model produce acceptable results for most real games at minimal cost?

That is also a hard question, and a very useful one.

## The Real Core Skills Required

If you want to build a good `per-scanline` NES emulator, the most important capabilities are usually these five.

### 1. Layering discipline

You cannot dump all timing problems into one layer.

A healthier structure usually separates:

- the main model: scanline-driven progression
- a critical-event layer: intra-line events, IRQ points, split points
- component-specific refinement: mapper timing, PPU status behavior, sprite hit behavior, DMC details
- ROM-specific exceptions: only when clearly justified

Without that separation, the system tends to blur into one giant timing blob.

### 2. The ability to find the minimum necessary precision

Not every hardware detail deserves full reconstruction inside a `per-scanline` model.

What matters is identifying:

- which missing information breaks broad classes of games
- which issues appear only in rare test ROMs
- which behaviors can be approximated acceptably

For many practical projects, the goal is not to reproduce every single PPU dot.  
The goal is to capture the few timing points that actually determine visible correctness:

- important state transitions around scanline boundaries
- observable events that affect IRQ behavior
- the specific moments that affect split timing or sprite-related decisions

### 3. The ability to control patch growth

You should assume from the start:

> a `per-scanline` emulator will need patches, but those patches must be institutionalized.

That means:

- patches need fixed insertion points
- patches need names and ownership boundaries
- patches need to be testable and removable
- patches need to state what they are fixing, not hide behind mystery constants

This matters enormously for long-term maintenance.

### 4. Strong regression-testing habits

One dangerous property of `per-scanline` systems is that bugs often do not explode immediately.

Instead, you may see:

- a title screen that looks fine
- gameplay that later shows broken splits
- occasional flicker in a specific level
- mapper IRQ timing that slowly drifts under certain conditions

That is why this style depends heavily on:

- test ROMs
- real commercial game regression testing
- issue classification records
- before/after behavior comparison

Without those, you can easily believe a problem is solved when it has only moved somewhere else.

### 5. The ability to say no

This is critical.

If your actual project goal is:

- high speed
- most commercial games working well
- reasonable maintainability

Then you must accept that:

- some extreme timing cases may remain unsupported
- some ROMs are not worth damaging the architecture for

That is not laziness. It is part of the product definition.

## The Three Most Common Traps

### Trap 1: believing "once per scanline" is enough

That may be enough to achieve:

- roughly visible output
- some simple games running

But the moment you meet games that depend on finer timing, common failures appear:

- broken split positions
- inaccurate IRQ timing
- incorrect sprite hit behavior
- missing or unstable effects

### Trap 2: refusing to add intra-line events at all

Some developers want to preserve the aesthetic purity of a "pure scanline model," and resist the idea of line-internal events.  
That often locks compatibility below a practical threshold.

A more mature compromise is:

- keep the main architecture `per-scanline`
- allow a small number of explicit, justified, testable mid-line events

This is usually much healthier than pretending the line is always enough.

### Trap 3: adding so many events that the model becomes a broken pseudo-`per-dot`

The opposite failure is also common:

- one event here
- one pseudo-dot there
- one mapper-specific half-line correction elsewhere

The result can become:

- harder to understand than a real `per-dot` design
- less systematic than a real `per-dot` design
- but without the structural clarity that a proper fine-grained timing model gives you

That is a dangerous place to end up.

## A Healthier Development Strategy

If you really want to go down this road, a staged strategy is usually safer.

### Stage 1: build a clean and stable scanline core

The first goal is not universal compatibility.  
The first goal is to make the model itself coherent.

This stage should emphasize:

- clear CPU/PPU/APU/mapper driving relationships
- consistent frame and scanline boundary behavior
- stable basic rendering and basic mapper support

### Stage 2: add only high-value timing events

Then start restoring the timing effects that matter to many real games, such as:

- common mapper IRQ needs
- common split-scrolling behavior
- necessary `sprite 0 hit`-type boundaries

The rule here should be:

> every new event must clearly correspond to a known compatibility problem class.

### Stage 3: formalize exception handling

As patches begin to accumulate, they need structure.

For example, categorize them by:

- event type
- mapper family
- whether they affect global timing or only local behavior

This prevents the project from turning into a bug-hack landfill.

### Stage 4: use profiling to decide whether refinement is worth it

At this stage, do not automatically push toward finer timing.  
Ask first:

- where is the actual performance bottleneck
- what kinds of incompatibility remain dominant
- which problems truly justify model refinement

Sometimes you will discover that:

- the bottleneck is not timing granularity at all
- the real issue is mapper behavior
- or the rendering pipeline matters more than the timing model

## Advice for Different Readers

### If you are a general technical enthusiast

You can think of `per-scanline` as an intelligent approximation strategy.

It does not try to imitate every hardware instant.  
Instead, it:

- captures the larger time structure first
- then repairs the important details selectively

Its engineering beauty lies in practical effectiveness rather than maximum realism.

### If you are new to emulator development

`per-scanline` can teach you a lot, but do not mistake it for an "easy path."

You should first decide whether you want:

- a teaching project
- or a practical emulator that runs many commercial games

The first can stay much simpler.  
The second will inevitably face timing-correction design problems.

### If you are a serious emulator core developer

The most important questions are not "can I do it?" but:

- what is the product goal
- what is the compatibility target
- how much maintenance effort is available
- and whether the architecture may later need to evolve toward finer timing

If future migration toward `per-dot` or finer timing is likely, then the current `per-scanline` structure should leave room for that evolution.  
If the project is intentionally staying on the practical high-speed path, then controlled exception architecture must be treated as a first-class design concern.

## How This Differs from High-Fidelity Timing Designs

High-fidelity approaches such as `per-dot`, `per-cycle`, or `master clock` usually aim for:

- stronger reconstruction of the real hardware time structure
- side effects that emerge naturally from the model

A high-quality `per-scanline` design aims for something different:

- reconstructing observable results accurately on top of an incomplete model

The first philosophy is closer to:

> I make the world realistic enough that many effects will come out correctly by themselves.

The second is closer to:

> I know the model is not fully realistic, so I must choose very carefully where to restore realism.

Both are difficult. The difficulty is simply of a different kind.

## Conclusion

`per-scanline` is not obsolete, and it is not just a beginner's shortcut.  
If your goals are:

- high speed
- most commercial games being playable
- reasonable engineering cost

then it can be a serious, practical, and technically rich development path.

But the real difficulty of this path is not that it simulates too little.  
It is that you must:

- know where the model is too coarse
- know exactly which parts must be repaired
- preserve architectural control while improving compatibility
- and do all of that without destroying speed or maintainability

From an engineering point of view, **a high-quality `per-scanline` NES emulator is not the "easy option." It is a different kind of hard option.**
