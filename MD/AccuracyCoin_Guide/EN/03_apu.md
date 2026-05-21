# Part 3: APU (Page 14)

> Maps to: the APU half of **P14 APU Registers and DMA Tests** — Length Counter, Length Table, Frame Counter IRQ, Frame Counter 4-step, Frame Counter 5-step, Delta Modulation Channel, APU Register Activation, Controller Strobing, Controller Clocking.
> (The DMA half of the same page is in [`02_dma.md`](02_dma.md).)
> Prerequisite: [`00_timing_model.md`](00_timing_model.md) (APU step, get/put cycle).

The hidden thread of the APU page is **deferred (delayed effect) + parity (get/put parity)**. Many APU behaviors don't "take effect the moment you write"; they're deferred until the next APU **get cycle** — the same worldview as the CPU page's cycle sampling and the DMA page's parity alignment.

---

## 1. Length Counter / Length Table (warm-up)

**Tests**: a length counter counting down to zero stops the channel; `$4015` lets you read whether each channel's length counter is > 0; the length table is a 32-entry lookup (indexed by the high 5 bits of `$4003` etc.).

Key quirks:
- **halt flag**: the length counter's halt (`$4000` bit5 etc.) freezes the countdown.
- **reload timing**: writing a length (the high part of `$4003`/`$4007`/`$400B`/`$400F`) reloads on the **next half-frame**, not immediately.
- **enable=0 zeros immediately**: writing 0 to the corresponding `$4015` bit clears that channel's length counter at once.

These are clear logic with little cycle-precision demand — the warm-up of P14.

---

## 2. Frame Counter (4-step / 5-step / Frame IRQ) — the poster child for deferred clear

The frame counter is the APU's metronome: 4-step mode produces 4 events per frame (quarter-frame for envelope/linear counter, half-frame for length/sweep), and the last step in 4-step mode raises the **frame interrupt**; 5-step mode has one more step and raises no IRQ.

**The most delicate part is the timing of clearing the Frame Counter IRQ** ([BUGFIX37](../../bugfix/2026-03-07_BUGFIX37.md)). This AccuracyCoin item has **24 sub-tests** and forces out two hardware facts:

1. **Reading `$4015` clears the frame IRQ flag with a "delay"** — not the instant you read, but deferred to the **next APU get cycle**.
   - Our approach: `apu_r_4015()` only sets a pending flag (currently named `clearingFrameInterrupt`); the real clear happens in `apu_step()`'s get cycle (`(cpuCycleCount & 1) == 0`), ordered before the frame counter assertion.
   - Why it matters: the test uses `SLO abs,X` to do a "dummy read + real read" pair on `$4015`, and the get/put parity decides whether the IRQ flag **was or wasn't** cleared before the second read — clearing it immediately gives the wrong answer.

2. **When `apuintflag` (IRQ inhibit) is true, the frame IRQ flag is still "unconditionally set for 2 cycles" and only suppressed on the 3rd cycle**.
   - We originally gated "whether to set" with `!apuintflag` → wrong. Changed to: **set unconditionally**, and on the last cycle clear it if `apuintflag` is true; only the IRQ line additionally checks `!apuintflag`.

> Lesson: APU flags are often "happen unconditionally first, then get corrected with a delay." Understanding "inhibit" as "doesn't happen" is wrong — it's "happens, then is retracted later." This deferred behavior simply can't be tested without a cycle-level model.

---

## 3. Delta Modulation Channel (DMC) — enable delay always set

The DMC plays DPCM samples, fetching them via DMA (DMA details in [`02_dma.md`](02_dma.md)). P14's DMC test has a classic pitfall ([BUGFIX49](../../bugfix/2026-03-08_BUGFIX49_DMC_enable_delay.md), AC 121→122):

**When `$4015` re-enables the DMC, the transfer start delay must be set "unconditionally"** — not only when the buffer is already empty.

```
$4015 write enables DMC → restartdmc() sets dmcsamplesleft
   buffer not yet empty (the shift register is still consuming the previous byte)
   ✗ old code: no countdown set → buffer empties next cycle → DMA fires immediately (too early)
   ✓ fix: set the countdown unconditionally → DMA deferred until the countdown expires (correct)
```

The reference is Mesen2's `SetEnabled(true)`, which always sets `_transferStartDelay`. Tests M/N specifically check the boundary "writing `$4015` 1 or 0 cycles before the DMC timer fires."

> Same motif as §2: **write a register → takes effect N cycles later**, not immediately. The only difference is how many cycles late, and how that delay wraps up when it collides with a timer fire.

---

## 4. APU Register Activation — DMA reading APU registers

This was also mentioned in [`02_dma.md`](02_dma.md) §3, because it straddles DMA and APU. The point: when the **CPU address bus is within `$4000–$401F`**, an OAM DMA reading from page `$40` reads the **APU internal registers**, and they re-map every `$20` bytes (`$4036 → $4016`, etc.).

Two pitfalls we hit ([BUGFIX46](../../bugfix/2026-03-08_BUGFIX46.md), 118→119; plus the recent dual-bus regression):
1. `IO_read()` originally lacked a `$4017` (controller 2) case → it returned `cpubus` instead of controller data. With no controller 2 plugged in, it should be D0–D4=0, D5–D7=open bus.
2. The bit5 open bus of a DMA read of `$4015` takes the **external** bus (not internal) — see [dual data-bus](../../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md); this is the root cause of error code 7 "bus conflicts not properly emulated."

> Expected OAM contents (the README includes them): `... 44 41 40 ...` — `$44` is the result of reading `$4015` (frame IRQ flag + triangle length), where bit5=0 comes from the DMA's `$40` external bus. Being able to print this string means APU + DMA + bus are all aligned.

---

## 5. Controller Strobing / Clocking — parity again

The strobe and shift timing of the controllers (`$4016`/`$4017`) also depend on get/put parity ([BUGFIX39](../../bugfix/2026-03-07_BUGFIX39.md)):

- **Controller Strobing**: writing `$4016` bit0=1 strobes the controllers; but the strobe taking effect relates to the CPU's get→put cycle transition (deferred `$4016` write). The test checks: value `$02` should NOT strobe (only bit0 matters), any value with bit0 set should strobe, and the strobe happens on the get→put transition.
- **Controller Clocking**: reading the same `$4016`/`$4017` on two consecutive cycles does **not** shift the shift register (the edge behavior of strobe/clock in hardware). We model "two consecutive reads don't shift" with `P1_ShiftCounter = 2` (set to 2 after a read, decremented on the APU put cycle).

```csharp
// MEM.cs DmaFetch / IO read: reading $4016
ctrlData = (byte)(((P1_ShiftRegister & 0x80) != 0 ? 1 : 0) | (val & 0xE0));
P1_ShiftCounter = 2;   // two consecutive reads don't shift
```

---

## Summary

The APU page's motif in one sentence: **"writing a register / reading a status" rarely takes effect immediately; it's mostly deferred to the next APU get cycle, and the behavior varies by get/put parity.**

- Frame IRQ: reading `$4015` clears with a delay; inhibit means "happens then is retracted."
- DMC: enable sets the transfer delay unconditionally.
- Controller: strobe/shift depend on the get→put transition and consecutive reads.

These share the same underlying model as the [DMA page](02_dma.md)'s parity alignment and the [CPU page](01_cpu.md)'s cycle sampling — again proving: **with the foundation (master-clock + get/put parity) right, the APU's deferred behaviors become expressible.**

Next: [`04_ppu.md`](04_ppu.md) (PPU: VBlank/NMI, read buffer, palette, sprite eval, sprite 0 hit, OAM corruption, shift registers).
