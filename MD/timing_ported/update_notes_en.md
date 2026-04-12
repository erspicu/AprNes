# AprNes Update Notes — TriCNES Timing Architecture Port & Performance Optimization

## Why This Update

The previous version of AprNes had passed AccuracyCoin (Commit 62ed684) 136/136 and blargg 174/174 tests, and I thought the emulation accuracy was in good shape. However, running test ROMs like scanline-a1 and colorwin_ntsc revealed noticeable visual artifacts. After thorough investigation, the root cause was confirmed: the PPU timing granularity was insufficient, and real timing issues still existed beneath the surface.

## TriCNES and the Porting Process

During the process of resolving these issues, I ultimately decided to raise the timing precision and adopt the timing architecture designed by TriCNES, porting key parts of its approach.

TriCNES is an excellent NES emulator. Its design philosophy leans toward the author's personal academic research interests and the pursuit of absolute correctness for TAS (Tool-Assisted Speedrun) execution — it's not primarily designed with general end-users in mind. From what I can see, TriCNES's development direction has been steadily moving toward real circuit-level behavioral emulation, which is genuinely impressive.

### First Port: Fixing PPU Test ROMs

Before porting TriCNES, AprNes had already achieved a perfect score of 136/136 on AccuracyCoin (Commit 62ed684). The first port of TriCNES's timing structure was primarily aimed at fixing visual artifacts in other PPU test ROMs (such as scanline-a1, colorwin_ntsc, etc.). This port improved overall timing precision, and blargg tests also increased from 174 to 184 items all passing (including 10 newly added PAL APU tests).

### The AccuracyCoin Update Challenge

AccuracyCoin was later updated to a new version (Commit 03385dd), increasing the test count from 136 to 138 items. Under this new test suite, the previous version of AprNes failed 10 items, dropping to 128/138.

### Second Port: Addressing the New AccuracyCoin

To tackle the challenges posed by AccuracyCoin (Commit 03385dd), I performed another round of timing architecture porting. AprNes climbed back to 135/138, and with further fixes reached 137/138. However, one test ($2007 Stress Test) proved impossible to solve through behavioral-level emulation alone, no matter what approach I tried.

Finally, I ported the SR Latch Pipeline design from a newer version of TriCNES. This updated version features even more circuit-level (RTL) design concepts — for example, using a 5-stage NOR gate chain to model the $2007 read/write timing pipeline. It's truly remarkable engineering.

I didn't adopt all of these circuit-level designs. I selectively ported only the parts needed to solve the final failing test. The result: **138/138** perfect score on AccuracyCoin (Commit 03385dd), plus **184/184** blargg tests passing.

## Performance Impact & Optimization

### Performance Hit

Porting TriCNES's timing architecture had a significant impact on overall performance. Circuit-level timing simulation — such as the SR Latch advancing state every PPU dot, and Master Clock-driven sub-cycle-accurate scheduling — introduces non-trivial computational overhead.

### The Catchup Problem

The traditional emulator acceleration technique — catchup (deferring work and batch-processing it when needed) — becomes extremely difficult to implement under this architecture:

- NMI/IRQ firing is precise to the Master Clock level
- The SR Latch pipeline must advance every dot
- APU GET/PUT half-cycle alternation affects DMC DMA timing
- AccuracyCoin (Commit 03385dd) tests directly validate these micro-level behaviors

Completed safe optimizations include: bitwise SR latch pipeline, SWAR 64-bit batch operations, managed array elimination, method inlining, and Mode 0 audio sample catchup. I believe there's still room for further optimization, but it will take more time.

### A Note on Performance Reality

In theory, with modern CPU performance, emulation shouldn't be a problem as long as you're not running something like Visual6502's Gate-level Netlist simulation on the CPU. The issue is that AprNes was designed to make full use of modern CPU capabilities, incorporating substantial audio/video DSP chains (NTSC analog simulation, CRT effects, audio post-processing). With the emulator core's processing cost significantly increased by the high-precision timing model, the DSP chain overhead becomes particularly noticeable.

The base mode currently runs smoothly after optimization, but enabling analog mode (especially Ultra Analog + CRT simulation) may become too demanding for some systems.

## Benchmark Tools

This update includes several benchmark batch files for evaluating performance on your machine:

- **benchmark_baseline.bat** — Baseline test. Pure digital mode (1x resolution, no filters, no analog processing), measuring raw emulator core performance. Uses a JIT warmup → cooldown → two valid measurements protocol, reporting the best result.
- **benchmark_full.bat** — Full pipeline test. Enables NTSC + Audio Mode 2 (Modern Stereo) + Analog + Ultra Analog + CRT, testing at 2x/4x/6x/8x resolutions separately to comprehensively evaluate the audio/video processing pipeline under varying loads.
- **benchmark_analog_full.bat** — Extreme stress test. Runs at maximum load configuration: 8x resolution (2048x1920), Ultra Analog, RF output, CRT simulation, Audio DSP Mode 2. Tests your hardware's limits under the most extreme scenario.

I may spin this off into a standalone benchmark testing tool project in the future.

## Positioning & Recommendations

I want to emphasize: **AprNes exists primarily as a proof-of-concept and personal interest project.** It's not designed with mass-market needs as the ultimate goal. I have my own ideas, interests, and things I want to test, research, and implement.

If what you care about is:

- Mapper support coverage
- UI polish and completeness
- Built-in features (debugger, cheats, save states, etc.)

Then the best choice remains **Mesen2**. These user-facing features are what truly determine whether an emulator is "good to use." Pushing emulation accuracy to the absolute extreme regardless of cost is more of a research or personal challenge endeavor.

**If the previous version of AprNes worked well for you, if you liked its audio and video processing results, and your computer can't handle the computational cost of the new version — there's no need to update.**

## Future Plans

- **Continued optimization** — Improving performance without sacrificing accuracy
- **More CJK-region Mappers** — Better compatibility with Chinese-language games
- **Final release on .NET 10** — The analog mode and CRT portions will likely leverage GPU acceleration
- **Visual6502 research** — After AprNes reaches a stopping point, I plan to shift focus to the Visual6502 project — designing a system that can run Gate-level Netlist simulation in real-time to actually play games. **This is what I truly want to build.**

## License & Acknowledgments

The AprNes project asserts no copyright and is released under the **WTFPL** license. If any of my designs appeal to you, if you see room for improvement, or if you'd like to bring certain design approaches into your own project — everything is welcome.

What I want to promote is just a "concept." The program itself was implemented with the assistance of AI (models trained on publicly available information), so in theory, my only real output is the design concept behind this project.

If anyone appreciates the concept, you're welcome to refine it. In particular, the audio/video DSP chain likely still contains theoretical errors from an academic standpoint that need correction.

---

*AprNes — An experiment in "what happens when you take every clock cycle of NES emulation seriously."*
