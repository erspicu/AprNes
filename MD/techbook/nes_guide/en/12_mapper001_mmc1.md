# 12 Mapper001 / MMC1

## What This Chapter Solves

MMC1 is an important step when learning mappers. Unlike simple mappers that take an entire register value in a single write, MMC1 accumulates a 5-bit value across 5 separate writes.

This chapter covers MMC1's serial register, PRG mode, CHR mode, and mirroring, with a cross-reference to AprNes's `Mapper001.cs`.

## NES Hardware Concepts

**Everyday analogy**: MMC1 is a **combination lock**. You can't enter a full number directly — only one click (1 bit) at a time, five clicks to set one register. If you misclick mid-sequence (write bit 7 = 1), the entire lock resets and you start over.

**Why design something so awkward?** Because MMC1 has only 8 input pins (the CPU's 8 data lines), but it must express 5+5+5+5 = 20 bits of configuration. **The chip designer chose serial loading to save chip area** — feed 5 bits via 5 writes. The cost is that game code must write 5 times to change a bank.

MMC1 has 4 main registers:

```text
$8000-$9FFF  Control       (mirror / PRG mode / CHR mode, 5 bits)
$A000-$BFFF  CHR bank 0    (5 bits to pick a 4 KB CHR bank)
$C000-$DFFF  CHR bank 1    (5 bits to pick a 4 KB CHR bank, only used in CHR mode 1)
$E000-$FFFF  PRG bank      (5 bits to pick a 16 KB PRG bank)
```

When the CPU writes `$8000-$FFFF`, MMC1 doesn't take the byte directly. Instead:

```
write $9234, value = 0x80   ─→  bit 7 = 1, reset: clear shift register, PRG mode = 3
write $9234, value = 0x01   ─→  shift = 0b00001, count = 1
write $9234, value = 0x00   ─→  shift = 0b00001 (right-shift, MSB padded 0), count = 2
write $9234, value = 0x00   ─→  shift = 0b00001 (right-shift), count = 3
write $9234, value = 0x00   ─→  shift = 0b00001 (right-shift), count = 4
write $9234, value = 0x01   ─→  shift = 0b10001, count = 5  ← 5 reached
                                commit to Control register (address in $8000-$9FFF)
                                clear shift register and count
```

Note that the address only decides **which register receives the final write**; the first four writes can use any address in `$8000-$FFFF`.

- If bit 7 is 1: reset the shift register.
- Otherwise: take bit 0 and feed it into the 5-bit shift register.
- After 5 accumulated bits, dispatch to the corresponding register based on address range.

So writing to MMC1 is a kind of hardware serial communication.

**How does game code do this?**

```assembly
; Write 0x0E into the control register (mirror=2, PRG mode=3, CHR mode=1)
LDA  #$80         ; reset MMC1
STA  $8000
LDA  #$0E         ; the value (0b01110)
LSR  A            ; bit 0 → carry
PHA
LDA  #$00
ROL  A            ; carry → bit 0
STA  $8000        ; write 1st bit
PLA
... (repeat 5 times) ...
```

In practice this is wrapped into an `mmc1_write_reg` subroutine reused throughout the game.

## Control Register

The control register encodes:

- mirroring type.
- PRG bank mode.
- CHR bank mode.

Mirroring:

```text
0  one-screen, lower bank
1  one-screen, upper bank
2  vertical
3  horizontal
```

PRG bank mode:

```text
0/1  switch 32 KB at $8000
2    fix first 16 KB at $8000, switch 16 KB at $C000
3    switch 16 KB at $8000, fix last 16 KB at $C000
```

CHR bank mode:

```text
0  switch 8 KB CHR
1  switch two independent 4 KB CHR banks
```

## Beginner-Friendly Simplification

MMC1 needs two pieces of state:

```text
shiftBuffer
shiftCount

if write bit7:
    reset shift
else:
    shiftBuffer |= (value & 1) << shiftCount
    shiftCount++
    if shiftCount == 5:
        commit to target register
        reset shift
```

Then use the control register to drive PRG/CHR mapping.

## AprNes / NesCore Implementation Mapping

Important fields in `Mapper001.cs`:

- `PRG_Bankmode`.
- `CHR_Bankmode`.
- `Mirroring_type`.
- `CHR0_Bankselect`.
- `CHR1_Bankselect`.
- `PRG_Bankselect`.
- `MapperShiftCount`.
- `MapperRegBuffer`.

`MapperW_PRG()`:

1. If `value & 0x80` is non-zero:
   - clear `MapperShiftCount`.
   - clear `MapperRegBuffer`.
   - `PRG_Bankmode = 3`.
2. Otherwise put `value & 1` into `MapperRegBuffer`.
3. If we haven't accumulated 5 bits yet, return.
4. Commit to control / CHR0 / CHR1 / PRG by address range.
5. Clear the shift buffer.

`MapperR_RPG()`:

- mode 0/1: 32 KB PRG bank.
- mode 2: fix first bank at `$8000`, switch `$C000`.
- mode 3: switch `$8000`, fix last bank at `$C000`.

`UpdateCHRBanks()`:

- CHR RAM: point directly at `ppu_ram`.
- 4 KB mode: `CHR0_Bankselect` controls `$0000-$0FFF`, `CHR1_Bankselect` controls `$1000-$1FFF`.
- 8 KB mode: use `CHR0_Bankselect >> 1` to pick the 8 KB bank.

AprNes uses `chrCountMask` and `banks4kMask`, assuming CHR ROM count is a power of 2 so masks replace modulo.

## Common Mistakes

- Treating the entire CPU-write byte as the MMC1 register value.
- Forgetting that bit 7 reset also forces PRG mode back to 3.
- 32 KB PRG mode failing to ignore the low bit of the bank number.
- 8 KB CHR mode incorrectly using the CHR1 register.
- Mirroring type misaligned with AprNes's `Vertical` mode.

## Chapter Recap

1. MMC1's core is a 5-bit serial load register.
2. The control register simultaneously sets mirroring, PRG mode, and CHR mode.
3. MMC1 demonstrates that a mapper is really a state machine on the cartridge, not a plain offset function.

## Bridge to the Next Chapter

The next chapter covers Mapper002 / UNROM, focusing on the simplest PRG 16 KB bank switching.
