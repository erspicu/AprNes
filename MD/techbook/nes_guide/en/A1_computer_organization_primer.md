# A1 Computer Organization Primer: What Programmers Without Hardware Background Need to Know

## What This Chapter Solves

Many people who can write Python, JavaScript, or even C still get stuck when asked things like "what's a register?", "what's a bus?", or "why do CPU and PPU compete for memory?" This is the hardware-foundation supplement to read **before the emulator chapters**. Every abstract term is grounded with everyday analogies.

After reading this, going back to chapters 02–17 will be much smoother.

---

## Master Analogy: Picture the Computer as a Kitchen

Almost every term below maps to this kitchen analogy:

```
Kitchen = a computer

Head chef (CPU)         = the person who reads the recipe and follows steps
Recipe book (ROM)       = unwritable source of instructions
Counter (RAM)           = surface for temporarily holding ingredients
Fridge (large storage)  = slow but big (NES has almost none; modern PCs do)
Conveyor belt (Bus)     = how ingredients/tools move to and from the chef
Metronome (Clock)       = the kitchen's beat; each tick decides who acts
Doorbell/fire alarm (Interrupt) = signals that interrupt the chef
Assistant (DMA)         = someone who fetches things without bothering the chef
Special faucet/gas valve (I/O) = certain "spots" wired directly to outside equipment
Pastry chef (PPU)       = works in parallel with the head chef on a different task
Music conductor (APU)   = plays music while the cooking happens
Cartridge               = a guest's "outside toolkit" (recipe + their own utensils)
```

Whenever a term below feels fuzzy, this table usually clears it up.

---

## 1. Bit, Byte, Word: Units of Food

- **Bit**: a grain of rice. 0 or 1.
- **Byte (8 bits)**: a bag of rice. The smallest meaningful unit; expresses 0–255 or -128–127.
- **Word**: a box of rice. Meaning differs by machine — 16-bit machines call it a 16-bit box; 64-bit machines call it 64-bit. **NES is an 8-bit machine**, so on the NES, "word" usually means 16-bit (composed of two bytes).

The NES CPU can only carry one bag of rice at a time (one byte). Larger items (like a 16-bit address) require two trips.

### Endian: Which end of the bag do you pour from?

Storing the 16-bit value `0x1234` into two memory locations:
- **Little-endian**: low byte first → `34, 12` (small grains first).
- **Big-endian**: high byte first → `12, 34` (big grains first).

**6502 / NES is Little-endian**. People porting from PowerPC (GameCube/Wii) to x86 are the ones who complain about endianness. NES development just remembers "low first, high second".

---

## 2. Register, RAM, ROM: In-Hand, On-Counter, On-Shelf

The three storage tiers most often confused by emulator beginners:

| Name | Kitchen analogy | Speed | Capacity | Writable? |
|---|---|---|---|---|
| **Register** | Ingredients held in the chef's hands | Fastest | Tiny (NES 6502 has only 6) | Yes |
| **RAM** | The counter | Fast | Medium (NES system 2 KB + cartridge SRAM optional) | Yes |
| **ROM** | The recipe book | Fast but read-only | Large (NES cartridge can hit MB) | **No** (or via mapper banking) |

For each recipe step, the chef:
1. Glances at the recipe (read instruction from ROM)
2. Picks up / puts down ingredients on the counter (read/write RAM)
3. Adjusts what's in the hands (operate registers)

The NES's 6 registers: **A** (Accumulator, the working hand), **X**, **Y** (two index fingers), **SP** (stack pointer), **PC** (recipe bookmark), **P** (status flags — covered later).

---

## 3. Bus: The Kitchen Conveyor

The chef doesn't run to the counter to grab things — he writes a slip ("give me the bag from cell #`$0042`"), the slip rides the conveyor, and the counter side fulfils the request and sends back rice.

The NES CPU bus is two conveyors:

- **Address bus**: 16 lines, can specify any of `$0000`–`$FFFF` (65,536 cells).
- **Data bus**: 8 lines, transports one byte at a time.

Each CPU cycle, the chef does exactly one of:
- "**Read**: hand me cell `$1234`" (address bus emits 1234, data bus returns one byte)
- "**Write**: put this byte at cell `$5678`" (both buses send)

One cycle, one action. **That's why cycle accuracy matters** — the order of the chef's slips and the timing of returns must be aligned across the system.

---

## 4. Clock and Cycle: The Metronome

The kitchen has a metronome ticking 1,789,773 times per second (NES NTSC CPU clock ~1.79 MHz).

Each tick is one **clock cycle**. Every action by the chef takes at least one cycle:
- Read a byte from the recipe → 1 cycle.
- Write data to the counter → 1 cycle.
- A 6502 instruction usually takes 2–7 cycles.

But the NES has more than one metronome — there's a faster **master clock** (NTSC: 21.477 MHz), with the CPU running at 1/12 of it and the PPU at 1/4. So:

- For each chef tick, the PPU has ticked 3 times.
- Three workers (CPU, PPU, APU) share one metronome but each follows its own beat.

The emulator's central problem: **how to align the three beats correctly in software**. AprNes's master-clock loop is doing exactly that.

---

## 5. Memory Map: Floor Plan of a Building

The CPU's 64 KB address space (`$0000`–`$FFFF`) is **not 64 KB of physical memory**. It's a building floor plan:

```
$0000-$07FF  RAM           (2 KB physical memory)
$0800-$1FFF  RAM mirror    (mirror of the block above, 4 copies total)
$2000-$2007  PPU registers (8 registers)
$2008-$3FFF  PPU mirror    (those 8 registers, repeated)
$4000-$4017  APU + I/O     (sound, joypads, OAM DMA)
$4018-$401F  test mode     (NES doesn't use)
$4020-$FFFF  cartridge     (PRG ROM, SRAM, mapper registers)
```

Picture a 65,536-room building:

- Rooms 0–2047: actual 2 KB RAM.
- Rooms 2048–8191: **mirror** (knocking these doorbells leads to one of the 2,048 RAM rooms).
- Rooms 8192–8199: **special rooms** wired to the PPU chip.
- Rooms 16,384–16,407: APU and joypads.
- Rooms 16,415+: cartridge PRG ROM and other hardware.

That's **memory-mapped I/O** — the chef thinks he's writing to the counter, but that location is wired straight to "some PPU signal." Writing `$2000` doesn't store in RAM; it sets the PPU control register.

### Mirroring: same room, multiple door numbers

Why does the NES mirror 2 KB RAM to 8 KB? **To save chips**. Period decoders only wired up 11 address lines (enough to address 2,048 rooms), and the rest were ignored. So `$0042` and `$0842` lead to the same room.

In emulators, the simplest mirror handling is `addr & 0x07FF` (drop the high bit).

---

## 6. What is Memory-Mapped I/O? Special Rooms with Outside Lines

A normal RAM room: write a byte; reading later returns that byte.

**A memory-mapped I/O room**: write a byte → **trigger a hardware action** (change PPU scroll, start DMA, change volume). Reads similarly return current hardware state (PPU's VBlank flag, current button state).

Examples:

| Address | Not stored in RAM — instead triggers |
|---|---|
| `$2000` | Set PPU control register (NMI on/off, sprite size) |
| `$2005` | Set PPU scroll (two writes: first X, then Y) |
| `$2007` | Push a byte into PPU VRAM |
| `$4014` | **Trigger OAM DMA**: copy 256 bytes from RAM to PPU OAM (stalls CPU 513 cycles) |
| `$4017` | Set APU frame counter mode |

That's why an emulator's "memory write" function can't be just `mem[addr] = value;` — it must inspect which segment `addr` falls in and dispatch to the matching hardware handler. AprNes's `MEM.cs` / `IO.cs` is doing exactly that.

---

## 7. Open Bus: The Echo When No One Picks Up

If the chef sends a slip on the conveyor saying "give me whatever's in `$401F`" and no one is wired to that address, what happens?

**Real hardware doesn't return 0 or an error code — it returns "the last value left on the data bus."** That's **open bus**.

Many old games rely on this — they intentionally read invalid addresses **expecting the byte the chef just touched**. Emulators must reproduce this, otherwise some test ROMs fail.

Implementation: log the "last data bus value" on every legitimate read/write; return it for invalid-address reads.

---

## 8. Interrupts: Doorbells That Interrupt Cooking

Mid-recipe, the chef can be interrupted. There are two kinds:

### NMI (Non-Maskable Interrupt) — fire alarm

- **Cannot be ignored**. When it sounds, the chef must drop the spatula and respond.
- On NES, NMI comes from the PPU — **fires once per frame at VBlank**. Games use this moment to update the screen (the PPU isn't drawing, so VRAM is safe to modify).
- When fired, the CPU:
  1. Saves the current "bookmark `PC`" and "status flags `P`" to the counter (stack).
  2. Jumps to the address stored at `$FFFA-$FFFB` (NMI handler).
  3. After handling, `RTI` returns.

### IRQ (Interrupt Request) — phone call

- **Maskable** (won't be answered if the `I` flag is set in P).
- On NES, IRQ comes from APU frame counter, DMC sample completion, **or cartridge mapper** (e.g., MMC3's scanline IRQ counter).
- Flow similar to NMI but uses `$FFFE-$FFFF`.

### Reset — the building's master reset

Pressing the NES reset button makes the CPU jump to the address at `$FFFC-$FFFD` and start there (RAM isn't cleared, so the title screen sometimes shows leftover content from the last play).

The emulator must implement these three vectors: `$FFFA` (NMI), `$FFFC` (Reset), `$FFFE` (IRQ/BRK).

---

## 9. DMA: Assistant Carries Things Without Disturbing the Chef? Actually Yes

**DMA** (Direct Memory Access) is "the chef stays still; an assistant moves things from A to B."

There are two DMAs on NES:

### OAM DMA (sprite-table DMA)

Triggered by writing `$4014`; copies 256 bytes from the specified page to the PPU OAM (sprite attribute table) in one go.

"Doesn't disturb the chef" is **not entirely true on NES** — the assistant shares the same conveyor as the chef, so during the move the chef must **stop and wait** (513–514 cycles).

Even so, this beats the chef's own "256 `STA` instructions" by a wide margin.

### DMC DMA (audio sample DMA)

When a DMC sample plays, every few hundred cycles it autonomously fetches the next byte from PRG ROM. This DMA **steals 1–4 CPU cycles** (cycle stealing) — the chef gets bumped and DMC briefly uses the bus.

Many timing-sensitive game logic is affected by DMC DMA, which is why it's a focal point of NES emulator accuracy testing.

---

## 10. CPU and PPU Run Together: Chef and Pastry Chef

What's special about NES is that CPU and PPU **really run in parallel**. It's not "CPU runs one instruction, then PPU catches up by 3 dots" — both follow their own metronome ticks.

```
master clock tick   1   2   3   4   5   6   7   8   9   10  11  12
CPU                                       *cycle*               *cycle*
PPU              *dot*  *dot*  *dot*  *dot*  *dot*  *dot*  ...
```

PPU runs at master / 4; CPU at master / 12 — exactly 3:1 (3 PPU dots per CPU cycle).

That's why the emulator's **tick model** looks like:

```
For each CPU read/write:
    1. advance PPU by 3 dots
    2. advance APU by 1 cycle
    3. then perform the actual read/write
```

AprNes's `tick()` in `MEM.cs` does exactly this.

---

## 11. State Machine: Hardware is Just a Self-Ticking Machine

A **state machine** is the mathematical model "given current state + input → go to next state". **Almost all hardware is a state machine**.

The PPU is an obvious example:

```
Mode 0 (HBlank)   → Mode 2 (OAM Search)
Mode 2 (OAM Search) → Mode 3 (Pixel Transfer)
Mode 3 (Pixel Transfer) → Mode 0 (HBlank) → ...
```

Each master clock tick, the PPU follows its logic and moves to the next state. The emulator's job is to faithfully replicate that state machine in software.

The CPU is also a state machine, just more complicated — each instruction is internally several "micro-steps" of one cycle each. For example `LDA $1234` (load A from `$1234`) is internally:

```
cycle 1: read opcode (PC=PC+1)
cycle 2: read low byte 0x34 of 1234 (PC=PC+1)
cycle 3: read high byte 0x12 (PC=PC+1)
cycle 4: read $1234 into A
```

Cycle-accurate emulators decompose every instruction to this granularity.

---

## 12. Latch / Flip-Flop: The Sticky Note Board

A **latch** is the smallest storage unit — just one bit. Physically, two logic gates wired in a loop "remember" the last value written until overwritten.

Kitchen analogy: a sticky note on a board. Write something; it stays visible until someone replaces it.

There are many latches inside the NES:
- PPU's `w` toggle latch (decides whether `$2005`/`$2006` is the 1st or 2nd write).
- The controller's strobe latch.
- Sprite-0-hit and sprite-overflow status latches.
- Bank-select latches inside mappers.

Each latch becomes a boolean or byte variable in the emulator, updated at the right moment.

---

## 13. PRG / CHR: Two Cartridge ROMs

There are two ROMs on a NES cartridge:

- **PRG ROM** (program ROM): instructions and data for the CPU. The "recipe book."
- **CHR ROM** (character ROM): graphic patterns for the PPU. The "sticker templates."

**Why split them?** Because the NES CPU bus and PPU bus are **physically two separate conveyors**! The CPU reads PRG on one bus while the PPU reads CHR on the other — **truly in parallel**. That parallel-bus design is the key reason NES could run 60 fps action games in the 8-bit era.

A few games have no CHR ROM and use **CHR RAM** (cartridge with RAM rather than ROM) — the CPU writes graphics into CHR RAM via the PPU's `$2007` register, allowing dynamic graphics in-game (e.g., the *Zelda* HUD font).

---

## 14. Mapper: The Cartridge's "Extra Hardware"

The NES CPU only addresses 64 KB (only 32 KB of which is for the cartridge), but *Super Mario Bros. 3* is a 384 KB ROM. How does that fit?

A **mapper** is a small chip on the cartridge that "shows the CPU a 32 KB window, but that window can **slide to different positions in the ROM**." In other words, a mapper is a **bank switcher**.

Mapper analogy: a 384-page recipe sits on the shelf, but the chef's desk only fits 32 pages. The mapper is a librarian who can swap those 32 desk pages with another 32 pages on demand.

How does the chef instruct the librarian? **By writing to a specific memory address** (somewhere in `$8000`–`$FFFF`). The write doesn't go to that address — it's a **page-flip command** to the mapper.

Different mappers use different command formats:
- **NROM (Mapper 0)**: no mapper, no flipping. 32 KB sits there.
- **UNROM (Mapper 2)**: write anywhere in `$8000`–`$FFFF` to flip; the value is the page number.
- **CNROM (Mapper 3)**: like UNROM but flips CHR pages, not PRG.
- **MMC1 (Mapper 1)**: feed a 5-bit command across 5 separate writes (serial protocol).
- **MMC3 (Mapper 4)**: includes an IRQ counter that can "ping the chef once per scanline."

Detailed mapper coverage in chapters 11–15.

---

## 15. Why Cycle Accuracy Matters

For an 8-bit console, "exactly how many cycles an instruction took" directly determines behaviour, because:

1. **Race conditions**: when the CPU writes a PPU register, the PPU is already at some dot. One cycle off and you write into a different PPU internal state. *Battletoads*' sprite-0-hit detection is exquisitely timing-sensitive.
2. **DMC cycle stealing**: as noted, DMC steals CPU cycles at unpredictable times. Without precise handling, other logic glitches while music plays.
3. **MMC3 IRQ**: the MMC3 mapper triggers IRQ when the PPU reads a particular pattern table. One dot off and the entire scanline IRQ misfires.

Approximate frame-based or scanline-based emulators can run most games, but to pass **blargg test ROMs** or **AccuracyCoin**-class accuracy tests, cycle-accurate (and even dot-accurate) is required.

For a deeper discussion see [NES Emulator Timing Models — A Comparative Guide](../../nes_emulator_timing_models_guide_en.md).

---

## 16. Why "Just a 6502 Interpreter" Isn't Enough

A pure 6502 interpreter can read all CPU instructions in a ROM and produce correct register / RAM results. But:

- No one paints the screen (no PPU).
- No one plays music (no APU).
- Writing `$2007` does nothing; CHR ROM never reaches the PPU.
- Cartridge banking isn't handled; the game crashes after 32 KB.
- VBlank NMI never fires; the game stalls in its "wait for VBlank" loop.

**Conclusion**: the emulator simulates the *whole machine*, not just a CPU. CPU, PPU, APU, DMA, and mapper must all interact correctly on the same timeline.

---

## 17. Quick Mapping for Programmers

| Concept you know | NES equivalent |
|---|---|
| Function pointer table | CPU bus dispatch (mem read/write function table) |
| Class instance variable | CPU register / PPU register |
| Function call | JSR + RTS |
| OS interrupt handler | NMI / IRQ vector |
| Memory-mapped file | PRG ROM "mapped" from cartridge into CPU addresses |
| Hash-table chaining | Mirroring |
| Multi-thread shared memory | CPU + PPU sharing the OAM DMA bus |
| Mutex | None (NES is single-threaded, but cycle stealing acts as interlocking) |
| Programming-language stack | The 6502's SP indexes into `$0100`–`$01FF` |
| Bit field | The 7 bits of status register P |

---

## 18. What to Read Next

After this primer, returning to chapter 02 onward should feel smoother:

- [02 Hardware Concepts You Need Before Writing an Emulator](02_hardware_concepts_for_emulator.md) — applies these concepts to a concrete emulator architecture.
- [04 CPU Bus and Memory Map](04_cpu_bus_and_memory_map.md) — how to write the dispatch.
- [05 6502 CPU Core](05_6502_cpu_core.md) — registers, flags, addressing modes, per-cycle opcode.

When implementing 6502 decoding and unsure what a particular opcode should do, consult [A2 6502 Complete 256-Opcode Implementation Reference](A2_6502_opcode_reference.md).

---

## Recap

1. Picture the computer as a kitchen — every component has a role.
2. The CPU talks to the outside via a bus made of address + data lines.
3. The NES's 64 KB address space isn't 64 KB of physical memory — it's a floor plan with RAM, I/O, and cartridge regions.
4. Memory-mapped I/O makes the chef *think* he's writing to memory, but he's actually flipping hardware switches.
5. Interrupts (NMI/IRQ) are the kitchen's doorbells and fire alarm — they break the chef's flow.
6. CPU and PPU truly run in parallel; the emulator's biggest job is aligning two timelines.
7. The mapper is the cartridge's bank switcher, letting a small CPU window see large ROMs.
8. Cycle accuracy matters because many NES hardware interactions hinge on which cycle they happen.
