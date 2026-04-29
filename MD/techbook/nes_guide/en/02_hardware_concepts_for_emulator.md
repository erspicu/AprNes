# 02 Hardware Concepts You Need Before Writing an Emulator

## What This Chapter Solves

NES emulator code is full of masks, shifts, mirroring, open bus, latches, DMA, IRQ, NMI, and cycles. These aren't implementation tricks — they're how the hardware actually works.

This chapter collects the hardware ideas the rest of the series will lean on repeatedly.

## NES Hardware Concepts

### Bit field

NES registers commonly pack independent meanings into the bits of a single byte.

**Everyday analogy**: picture an 8-toggle dashboard. Each toggle is its own function — toggle 1 controls the light, toggle 2 the fan, toggle 3 the AC mode. You change the whole panel (one byte) at once, but each toggle (bit) is independent.

PPU `$2000` (PPUCTRL), for instance, controls 7 unrelated functions in one byte:

```text
bit 7  NMI enable           ← "should VBlank interrupt the CPU?"
bit 6  master/slave         ← grounded on NES, unused
bit 5  sprite size          ← 8x8 or 8x16
bit 4  background pattern table  ← BG uses CHR $0000 or $1000
bit 3  sprite pattern table       ← Sprites use $0000 or $1000
bit 2  VRAM increment       ← after $2007: address +1 or +32
bit 1  base nametable hi bit
bit 0  base nametable lo bit
```

Writing this byte adjusts 7 unrelated things at once. Emulator code is full of bit tests and recombinations:

```csharp
NMIable = (value & 0x80) != 0;            // bit 7
VramaddrIncrement = (value & 0x04) != 0 ? 32 : 1;  // bit 2
SpritePatternTable = (value & 0x08) >> 3;  // bit 3 as 0 or 1
```

**Why design it this way?** The NES CPU only addresses 64 KB. Packing many boolean controls into one byte saves register addresses — a standard practice on 8-bit consoles where register space is precious.

### Address bus and data bus

When the CPU talks to the outside world, it goes:

1. address bus emits an address.
2. read/write pin selects read or write.
3. data bus transfers one byte.

If the address maps to RAM, RAM is read or written. If it maps to a PPU register, the corresponding PPU register behaviour fires.

**Everyday analogy**: imagine a 65536-room building. The chef wants to fetch something from one room:
1. Write the room number on a slip → **address bus** (16 lines pointing to any of rooms 0–65535).
2. Mark the slip ✓ for read or ✗ for write → **R/W control line**.
3. Slip goes out on the conveyor; what comes back is a bag of rice → **data bus** (8 lines, one byte at a time).

Key point: **address and data are two separate sets of lines**. 16 address lines → can specify 0–65535. 8 data lines → moves only one byte per transfer.

```
        16 address lines
CPU ──────────────────────►  AddressDecoder
                                 │
                                 ├── 0x0000-0x1FFF  → RAM chip
                                 ├── 0x2000-0x3FFF  → PPU chip
                                 ├── 0x4000-0x401F  → APU + IO
                                 └── 0x8000-0xFFFF  → cartridge

        8 data lines (bidirectional)
CPU ◄═════════════════════►  whichever chip got selected
```

**Why do emulators care?** Because many NES hardware behaviours (open bus, DMA cycle stealing, controller serial reads) depend on **what the last value on the bus was**. The emulator has to track this "last bus value" (AprNes calls it `cpubus`) — you can't treat the bus as just an abstract function call.

### Memory-mapped I/O

The NES has no separate I/O instructions. The CPU controls hardware through ordinary memory reads and writes:

```text
$2000-$2007  PPU registers
$4000-$4013  APU channel registers
$4014        OAM DMA
$4015        APU status
$4016        Controller strobe / read
$4017        APU frame counter / controller 2 read
```

These addresses have side effects. Reading `$2002` affects VBlank and the write latch; writing `$4014` triggers OAM DMA.

### Mirroring

Mirroring means several addresses pointing at the same physical hardware.

The CPU has only 2 KB of internal RAM, but it appears across `$0000-$1FFF`:

```text
$0000-$07FF  actual RAM
$0800-$0FFF  mirror
$1000-$17FF  mirror
$1800-$1FFF  mirror
```

So CPU RAM access usually applies `addr & 0x7FF`.

The PPU registers also mirror every 8 bytes — `$2008` is the same as `$2000`, and addresses up to `$3FFF` repeatedly map back to `$2000-$2007`.

### Latch

A latch is a hardware concept for transient state. Writes to a register don't always update a clean variable in one step.

**Everyday analogy**: think of PPU `$2005` as a "**visitor message board**" at the entrance. It has two sides — the first visitor flips it to face A (X scroll), the second flips it to face B (Y scroll). Who writes doesn't matter; **which face is up right now** matters. That "current face" is decided by the PPU's internal 1-bit `w` toggle.

```text
[w = 0]  CPU writes $2005 ──→ goes to X scroll; w flips to 1
[w = 1]  CPU writes $2005 ──→ goes to Y scroll; w flips to 0
```

PPU `$2005` and `$2006` share this same toggle. Order matters:

- 1st write to `$2005` → horizontal scroll (X)
- 2nd write to `$2005` → vertical scroll (Y)
- 1st write to `$2006` → VRAM address high byte
- 2nd write to `$2006` → low byte and schedules an address update

**Most common pitfall**: reading `$2002` **resets the `w` toggle to 0**! If a game accidentally reads `$2002` between two writes to `$2005`, the next write is treated as the "first write," scrambling scroll.

In code, the emulator represents this toggle as a boolean and updates it correctly across all related register operations:

```csharp
bool w;        // PPU internal toggle
ushort t, v;   // temporary / current VRAM address
byte fineX;

void Write_2005(byte value) {
    if (!w) { t = (t & 0x7FE0) | (value >> 3);  fineX = value & 7;  w = true; }
    else    { t = (t & 0x0C1F) | ((value & 7) << 12) | ((value & 0xF8) << 2);  w = false; }
}

void Read_2002() {
    // ... read status ...
    w = false;  // ★ reset toggle
}
```

### Open bus

Open bus refers to reading the residual data bus value when no new hardware drives it. This affects various test ROMs and a few special game behaviours.

**Everyday analogy**: you call into the phone and say "give me whatever's in room X," but room X's phone doesn't pick up. The line doesn't auto-respond with 0 or an error — you hear the echo of the last sentence on the line (capacitive effects keep voltages on the bus for a brief moment). Until someone actively drives a value, the echo *is* the bus value.

Concrete example:

```
CPU reads $1234 (RAM)        → bus value becomes $42
CPU reads $401F (no chip)    → still returns $42 (residual)
CPU reads $2002 (PPU status) → low 5 bits are open bus,
                                only high 3 bits (VBlank/Sprite0/Overflow)
                                get overwritten
```

**Why do some registers only "partially overwrite"?** Hardware-side, PPU `$2002` only wires 3 bits to the data bus; the other 5 lines are floating — those 5 bits **retain whatever was last on the bus**. The emulator must do:

```csharp
byte Read_2002() {
    byte status = (vblankFlag ? 0x80 : 0) | (spr0Hit ? 0x40 : 0) | (sprOverflow ? 0x20 : 0);
    return (byte)((status & 0xE0) | (cpubus & 0x1F));  // top 3 are real, bottom 5 are residual
}
```

In AprNes you'll see `openbus` and `cpubus`:

- `openbus`: residual on PPU-related buses.
- `cpubus`: most recent CPU data bus value.

**Test ROMs to know**: `ppu_open_bus.nes` (blargg) checks every PPU register's open-bus behaviour. Get it right and you'll see PASS.

### Clocks and cycles

Don't conflate the various "cycle" concepts.

- master clock: the system-wide reference clock.
- CPU cycle: one CPU bus cycle or internal step.
- PPU dot: one pixel-pipeline tick of the PPU.
- APU step: one update step of the audio hardware.

On NTSC NES, the PPU advances roughly 3 dots per CPU cycle. AprNes goes a layer deeper, using master-clock gates to describe which phase each chip operates in.

### IRQ and NMI

Interrupts are how hardware tells the CPU to suspend its current flow and jump into a handler.

**Everyday analogy**:

- **NMI = fire alarm**. When it goes off, the chef must drop the spatula and run downstairs. There's no "I'm busy, later" option.
- **IRQ = phone ringing**. If you've got the "do not disturb" sign up (CPU's `I` flag = 1), you don't pick up. After you remove the sign (`CLI` instruction), the next ring will be answered.

On NES:

| Signal | Source | Maskable? | NES use |
|---|---|---|---|
| **NMI** | PPU once per frame at VBlank | ❌ No | Tells the game "now's a safe moment to update video memory" |
| **IRQ** | APU frame counter / DMC / mapper IRQ counter | ✅ Yes (`I` flag) | Timing, scanline sync, custom events |
| **Reset** | Reset button, power-on | — | Jumps to the boot vector at `$FFFC-$FFFD` |

When an interrupt fires, the CPU does not jump in mid-instruction. It:
1. Pushes current `PC` (return address) and `P` (status flags) onto the stack.
2. Sets `I = 1` (so the IRQ handler isn't itself interrupted).
3. Reads the address from the matching vector and jumps there.
4. After the handler ends, `RTI` restores `PC` and `P`.

```text
NMI vector  : $FFFA-$FFFB
Reset vector: $FFFC-$FFFD
IRQ vector  : $FFFE-$FFFF
```

The CPU doesn't poll interrupts at arbitrary moments — it samples them at specific instruction boundaries. **Precise timing**: the 6502 samples interrupt lines on the **second-to-last cycle of each instruction** (this is **edge sampling**). At cycle-accurate fidelity the emulator must sample on exactly that cycle, or you'll miss or double-fire interrupts.

**Common NMI usage**: games loop in main waiting for NMI. Each NMI updates the screen:
```assembly
main_loop:
    JSR  game_logic     ; run game logic
    LDA  vblank_flag    ; main loop waits for NMI to set this
    BEQ  main_loop      ;
    LDA  #0
    STA  vblank_flag
    JSR  update_screen  ; safely write PPU during VBlank
    JMP  main_loop

nmi_handler:
    LDA  #1
    STA  vblank_flag    ; signal main loop
    RTI
```

### DMA

DMA is hardware taking over the bus to move data. OAM DMA copies 256 bytes from CPU memory to the PPU's OAM. DMC DMA reads sample bytes for audio.

DMA isn't a plain `Array.Copy` — it consumes CPU bus cycles and interacts with CPU read/write phases.

## Beginner-Friendly Simplification

A first pass can use:

- RAM mirroring via `addr & mask`.
- PPU/APU/IO: just the most-used registers first.
- Open bus: return the previous bus value.
- DMA: stall the CPU using a cycle count.
- IRQ/NMI: check at the end of each instruction.

Once games run, you can iterate toward AprNes's per-cycle behaviour.

## AprNes / NesCore Implementation Mapping

- `CPU.cs`
  - `CpuRead()` / `CpuWrite()` set `cpuBusAddr`, `cpuIsRead`, `cpubus`.
  - `PollInterrupts()` polls NMI/IRQ before each instruction completes.
- `MEM.cs`
  - `Read_NesRam()` uses `addr & 0x7FF` for RAM mirror.
  - `DmaOneCycle()` performs one DMA cycle at a time.
  - `DmaFetch()` handles DMA reads, open bus, and APU/joypad bus conflicts.
- `PPU.cs`
  - `vram_latch`, `ppu_2007_buffer`, `openbus`.
  - `$2005/$2006/$2007` all carry pipeline or deferred behaviour.
- `IO.cs`
  - Routes CPU access to `$2000-$4017` to PPU / APU / JoyPad.

## Common Mistakes

- Treating PPU registers as plain arrays.
- Ignoring mirroring, leading to misaligned reads/writes.
- Updating all PPU internal state immediately after a `$2006` write, ignoring the delay.
- Using a plain boolean for IRQ without distinguishing the IRQ line state vs. the CPU's sampled state.
- Implementing DMA as an instant copy that doesn't affect CPU timing.

## Chapter Recap

1. The NES controls hardware via memory-mapped I/O.
2. Bus state, latches, open bus, and DMA all produce observable behaviour.
3. Much of AprNes's complexity exists so these hardware details emerge at the right cycles.

## Bridge to the Next Chapter

The next chapter moves to ROM loading: `.nes` files, the iNES header, PRG/CHR ROM, mapper IDs, and how AprNes initialises everything.
