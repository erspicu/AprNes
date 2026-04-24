# What Catch-Up Means in Emulators, and Why Higher-Level Timing Strategies Make It Harder

## Introduction

When people discuss emulator timing, the conversation often focuses on labels such as:

- `per-frame`
- `per-scanline`
- `per-dot`
- `per-cycle`
- `master clock`

But in real emulator development, one of the ideas that most often makes the architecture complicated is not timing granularity by itself.  
It is another concept that is easy to underestimate:

**catch-up**

In simple terms, `catch-up` means:

> one component has moved forward in simulated time, while another has not fully advanced yet; when interaction, observation, synchronization, or side effects matter, the lagging component is brought forward to the correct point.

This idea appears in almost every emulator that does not move every component forward in perfect lockstep at the smallest possible unit.  
And as timing strategies become more precise and more complex, the design space for `catch-up` usually gets smaller, the cost of being wrong gets higher, and the real challenge becomes a test of **time-model design ability**.

This article is written for two kinds of readers at once:

- people who are interested in computing and software systems, even if they have never built an emulator
- developers who genuinely want to design emulator cores

## A Simple Intuition for Catch-Up

You can think of an emulator as a stage with multiple actors:

- the CPU executes instructions
- the PPU generates the picture
- the APU generates sound
- the mapper watches address lines, counts IRQ conditions, or switches banks

If all actors move forward together on every smallest tick, the model is conceptually clean, but also expensive.  
Many emulators avoid that cost and instead do something cheaper:

- let the CPU run for a while
- then, when the CPU needs to interact with the PPU, check IRQ state, or commit visible output
- advance the PPU, APU, or mapper up to the same logical time

That act of "bringing the lagging component forward" is `catch-up`.

So the essence of `catch-up` is not one specific algorithm.  
It is a synchronization strategy:

- allow different subsystems to drift temporarily
- force them back into alignment at important observation points

## Why Emulators Use Catch-Up

The most direct reason is simple: **cost reduction**.

If every component advances at every smallest unit of time:

- the implementation may be conceptually direct
- the behavior may feel closer to hardware
- but the computational cost is often high

If some components are allowed to update later and only catch up when needed, you may gain:

- fewer function calls
- fewer state transitions
- more batching opportunities
- better performance

That is why many emulators, even when they are not intentionally coarse, still contain some form of `catch-up`.

## A Very Simple Example

Suppose you are building a rough NES emulator and decide:

- the CPU is the main driver
- after each CPU instruction
- the PPU catches up by the corresponding amount of progress

Then the model may look like this:

1. The CPU executes one instruction.
2. You determine how many CPU cycles it consumed.
3. The PPU catches up by the corresponding amount.
4. The APU also catches up.
5. Then you check IRQs, NMIs, frame output, and other events.

That is one of the most classic forms of `catch-up`.

Its strengths are:

- easy to understand
- easy to implement
- already enough to run many games

Its limitations are:

- if some side effects actually happen in the middle of the instruction, you may observe them too late
- if some PPU or mapper events require more exact timing, the model begins to drift

## Catch-Up Is Not Just "Adding Time"; It Is Restoring Observable State

This point is crucial.

When people first hear `catch-up`, they often imagine something simple:

- add some cycles
- increment some counters

But the hard part is that an emulator is not merely counting time.  
It is maintaining **observable behavior**.

When you delay one component and bring it forward later, the real questions are:

- does its internal state rebuild correctly during the catch-up
- were any events that other components should have observed during that interval lost
- after catch-up, does the externally visible result still match the intended ordering of time

So `catch-up` is not only about aligning time. It is also about aligning:

- events
- side effects
- bus and register visibility

## In Coarse Models, Catch-Up Is Often Easier

If your timing model is coarse, for example:

- `per-frame`
- a rough `per-scanline`
- an instruction-level CPU-driven model

then the design space for `catch-up` is usually larger.

Why?

- the model already tolerates approximation
- many intermediate states are not meant to be preserved anyway
- outside observers only care about larger-grain results

In that situation, you can often be much freer with:

- batched advancement
- deferred computation
- merged events
- one-shot corrections at boundaries

That is one reason coarse emulators can often be made very fast.

## But a Coarse Model Is Not Automatically Easy

There is an important misunderstanding here.

Many people think:

> if coarse models leave more room, then catch-up must be simpler.

Not exactly.

A more accurate statement is:

- **you have more freedom to operate**
- but you must be much more careful about deciding which information may be compressed away and which may not

So the real challenge of a coarse model is this:

- you are intentionally not simulating everything precisely
- but you still need to know where that imprecision will break things

That is less a raw implementation problem and more an engineering judgment problem.

## As the Model Gets Finer, the Catch-Up "Space" Gets Smaller

When you move toward more precise timing models, such as:

- finer `per-scanline` plus intra-line events
- `per-dot`
- `per-cycle`
- `master clock`

something important happens:

**the freedom to delay, merge, compress, or approximate work shrinks very quickly.**

The reason is straightforward:

- more intermediate states become meaningful
- more boundary moments become observable
- more side effects can no longer be paid back later

In such a model, `catch-up` does not disappear, but it changes character:

- the valid window becomes shorter
- less work can safely be postponed
- synchronization points become stricter
- once you cross the wrong boundary, the error propagates immediately

So in high-precision models, `catch-up` often looks less like "free delayed advancement" and more like:

- tightly controlled deferred execution
- strict timestamp alignment
- careful side-effect scheduling

## Why More Complex Strategies Make Catch-Up Harder to Design

This is the central point of the whole discussion.

As the emulation strategy becomes more sophisticated, the difficulty does not come only from having more components.  
It comes from the fact that:

- components observe one another more often
- there are more observable time points
- there are more meaningful intermediate states
- there are more ordering constraints on events

For example, in a simple CPU-driven model, you may only need to think in terms of:

- the CPU runs first
- the PPU catches up
- then events are checked

But in a higher-precision model, you may have to consider that:

- a register write does not take effect immediately, but only after a few phases
- an IRQ is not observed at the end of a line, but at a specific boundary transition
- an open-bus or latch state must not be overwritten during deferred advancement
- a mapper needs to see a particular edge transition on a line
- a sprite or background condition must be decided inside a tiny timing window

At that point, `catch-up` is no longer "just add the missing time."  
It becomes:

> can I still preserve every ordering promise of this model while allowing delayed advancement at all?

That question is fundamentally a test of model-design skill.

## Catch-Up Consumes Design Margin

One useful way to think about timing is as a contract.  
Then `catch-up` becomes the question:

> how long does this contract allow me to delay the result before I must pay it back?

In a coarse model, the contract is usually more forgiving.  
You can deliver late because many intermediate moments are not observable anyway.

But the finer the model becomes, the stricter the contract gets.  
A single delayed step may already cross an observable boundary.

So you can say:

- coarse models have more design margin
- fine models have less design margin

And `catch-up` is, in a sense, the act of spending that margin.

That is one reason highly accurate emulators often find it much harder to implement `catch-up` elegantly.

## Common Forms of Catch-Up in Practice

Different emulators vary a lot, but broadly speaking, common `catch-up` styles include the following.

### 1. Pure time-amount catch-up

The simplest form:

- catch up by cycles
- catch up by dots
- catch up by scanlines

This is common in coarse or mid-granularity models.

### 2. Event-driven catch-up

Instead of always catching up every step, the emulator:

- advances to the next important event
- or catches up only when an interaction boundary is reached

This is often more efficient than pure time-amount catch-up, but it depends much more heavily on the event model being correct.

### 3. Deferred state commitment

For example, a write may first become pending:

- the real effect becomes visible only at a specific timing point
- before that point, reads still observe the old state

This is also a form of `catch-up`, except what is being caught up is not just time, but state commitment.

### 4. Partial subsystem catch-up

Not every subsystem catches up together:

- only the PPU catches up
- only the mapper catches up
- only the audio pipeline catches up

This is common in large systems, but it is also where subtle bugs easily appear, because one subsystem may already be aligned while another still lags behind.

## An Intuition for General Technical Readers

If you are not an emulator developer, it may help to think of `catch-up` as a kind of deferred accounting.

Normally the system records what is owed, but does not settle everything immediately.  
When you really need the answer, need to interact, or need to inspect the result, it settles the outstanding balance.

That is fast.  
But it also means:

- the accounting cannot be wrong
- the ordering cannot be wrong
- and nothing that should have been visible in the meantime can be lost

The difficulty in an emulator is that it is not only about whether the final total is right.  
It is also about:

- who saw what first
- when it was seen
- whether the visible value was the old one or the new one
- whether a side effect had already happened

## How Emulator Developers Should Think About Catch-Up

If you are designing an emulator core, I would suggest asking yourself a few questions first.

### 1. What is the primary time anchor

You must decide:

- is the CPU the anchor
- is the PPU the anchor
- or is the whole machine driven by master clock

This choice directly determines the direction of `catch-up`, because the other subsystems usually advance relative to that anchor.

### 2. Which boundaries may not be crossed

Every model has boundaries that cannot be crossed casually, such as:

- register side-effect commit points
- observable IRQ points
- A12 edges
- line and frame transitions
- latch or buffer update points

If a `catch-up` mechanism can jump across those boundaries without correctly splitting the work, it is unsafe.

### 3. Which states may be delayed, and which may not

You need a clear distinction between:

- state that may be advanced later
- state whose commitment may be delayed but whose visibility rules may not
- state that cannot be delayed at all

If this classification is fuzzy, timing bugs tend to multiply later.

### 4. Is catch-up global or local

Some systems define a single global `catch-up` rule.  
Others let each subsystem manage its own.

The global approach is stronger in:

- consistency
- ease of reasoning

But it may:

- cost more performance
- reduce local optimization freedom

The local approach is stronger in:

- flexibility
- subsystem-specific performance opportunities

But it is also where synchronization gaps appear most easily.

### 5. Where future optimization space will come from

Some `catch-up` designs are correct but hard to optimize later.  
Others leave room from the beginning for:

- batching
- event tables
- deferred queues
- dedicated hot paths

If performance work is expected later, that future space is worth designing for early.

## A Hard but Important Conclusion

Many people assume that higher-precision emulation is closer to "just following the hardware," and therefore requires less architectural skill.  
That is not true.

Even in a high-precision model, you still face hard engineering questions:

- how phases should be split
- how deferred state should work
- how event ordering should be enforced
- where batching is still safe
- where strict synchronization is unavoidable

So higher-level timing strategies are not only more computationally expensive.  
They also mean:

- less freedom for `catch-up`
- less error tolerance
- stronger demands for internal model consistency

At that point, what is being tested is no longer just whether you know hardware terminology.  
What is being tested is:

**can you design a time model that is correct, operational, and still optimizable?**

## Conclusion

`catch-up` is not a minor trick at the edges of emulator design.  
It is one of the core ideas inside timing architecture.

In coarse models, it represents:

- performance room
- approximation room
- architectural flexibility

In fine models, it represents:

- synchronization pressure
- boundary control
- event guarantees

So the maturity of an emulator is often measured not only by how fine its timing granularity is, but also by whether its `catch-up` design is clear about:

- when delay is allowed
- how delayed work is repaid
- how observable ordering is preserved
- and which boundaries must never be blurred

If the timing model defines the "resolution" of the simulated world, then `catch-up` design determines this:

**at that resolution, can the whole world still move together in a coherent way?**
