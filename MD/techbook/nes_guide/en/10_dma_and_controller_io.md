# 10 DMA and Controller I/O

## What This Chapter Solves

OAM DMA, DMC DMA, and controller I/O all operate through the CPU bus. They look like peripheral features, but they directly affect CPU cycles and bus values.

This chapter covers DMA and JoyPad hardware behaviour and AprNes's implementation in `MEM.cs` and `JoyPad.cs`.

## NES Hardware Concepts

### OAM DMA

Writing `$4014` from the CPU starts an OAM DMA. The written value is the source page:

```text
write $4014 = XX
source = $XX00-$XXFF
destination = PPU OAM 256 bytes
```

During DMA the CPU is halted while DMA alternates source-read and OAM-write cycles.

### DMC DMA

When DMC needs a sample byte, it reads one byte from CPU memory. This too inserts itself into CPU bus timing and can interact with OAM DMA.

### Controller serial read

**Everyday analogy**: a controller does not "tell you the state of all 8 buttons at once." Think of it as an **old-fashioned punch-card reader** — pull the lever (**strobe**) to load the current card; then each button press only reveals one bit at a time, one slot per turn. After 8 reads you finally know the state of every button.

NES controllers don't return an 8-bit button state in a single read. The flow is:

```
1. CPU writes $4016 = 1   ←  strobe high; shift register continuously loads current input
2. CPU writes $4016 = 0   ←  strobe low; lock register contents
3. read $4016 once        ←  returns bit 0 (A button); next read returns B
   read $4016 once        ←  returns bit 0 (B)
   ...                       (internal shift register shifts right after each read)
   read 9th onward         ←  returns 1 (official controller, idle)
```

In assembly:

```assembly
read_controller:
    LDA  #$01           ; strobe = 1
    STA  $4016
    LDA  #$00
    STA  $4016          ; strobe = 0, latch buttons
    LDX  #$08           ; read 8 times
loop:
    LDA  $4016          ; read one bit (in bit 0)
    LSR                 ; push bit 0 into carry
    ROL  buttons        ; push carry into buttons
    DEX
    BNE  loop
    RTS
```

Button order (in read sequence):

```text
read 1  → A
read 2  → B
read 3  → Select
read 4  → Start
read 5  → Up
read 6  → Down
read 7  → Left
read 8  → Right
read 9+ → 1 (official controllers); non-official (Famicom microphone, etc.) may differ
```

**Why this design?** The 1983 controller port had only 5 pins (power / ground / strobe / data1 / data2). Squeezing 8 buttons through that few wires required serial transmission. **Modern (USB) gamepads don't have this issue**, but NES emulators must still faithfully simulate the shift-register behaviour, or some games won't detect input.

## Beginner-Friendly Simplification

OAM DMA first version:

- On `$4014` write, copy 256 bytes immediately.
- Add 513 or 514 CPU cycles.

Controller first version:

- Store buttons in one byte.
- After strobe transitions 1 → 0, each read shifts one bit.

Later, add AprNes-like per-cycle DMA and controller shift delays.

## AprNes / NesCore Implementation Mapping

### DMA

`MEM.cs` state:

- `spriteDmaTransfer`: OAM DMA in progress.
- `spriteDmaOffset`: source page.
- `dmaOamHalt`.
- `dmaOamAligned`.
- `dmaOamAddr`.
- `dmcDmaRunning`.
- `dmcDmaHalt`.

`DmaOneCycle()` runs one DMA cycle at a time and dispatches by GET/PUT phase:

- OAM DMA get.
- OAM DMA put.
- DMC DMA get.
- halted fetch.

Important functions:

- `DmaFetch()`: DMA bus read with open bus and `$4015/$4016/$4017` corner cases.
- `OamDmaGet()`: read source byte.
- `OamDmaPut()`: write OAM.
- `DmcDmaGet()`: read DMC sample byte.

`ppu_w_4014()` in `PPU.cs` only sets DMA state; the actual byte movement happens cycle by cycle in the master-clock CPU gate.

### Controller

`JoyPad.cs`:

- `P1_Port`, `P2_Port`: current button state.
- `P1_ShiftRegister`, `P2_ShiftRegister`: shift registers for serial read.
- `P1_ShiftCounter`, `P2_ShiftCounter`: post-read shift delay.
- `controllerStrobing`, `controllerStrobed`.

Reads:

- `gamepad_r_4016()` returns player 1's current bit.
- `gamepad_r_4017()` returns player 2's current bit.
- High bits preserve `cpubus & 0xE0`.

Writes:

- `gamepad_w_4016()` sets the strobe flag.

Shift and strobe reload don't happen inside the read function — they happen inside `apu_step()`:

- `ProcessControllerShift()`.
- `ProcessControllerStrobe()`.

## Common Mistakes

- OAM DMA copies data without halting the CPU.
- DMC DMA ignores CPU bus impact.
- Controller reads return a live keyboard query rather than the shift register.
- During strobe-high, button state isn't continuously reloaded.
- Ignoring `$4016/$4017` high-bit open bus.

## Chapter Recap

1. DMA is hardware taking over the CPU bus, not an ordinary memory copy.
2. Controllers are serial shift reads, not a one-shot byte read.
3. AprNes places DMA and controller behaviour inside cycle timing, so peripherals don't break system timing.

## Bridge to the Next Chapter

The next chapter starts the mapper series with the simplest one (Mapper000 / NROM), introducing how cartridge hardware connects to the CPU and PPU buses.
