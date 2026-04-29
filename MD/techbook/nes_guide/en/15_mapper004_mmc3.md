# 15 Mapper004 / MMC3

## What This Chapter Solves

MMC3 is one of the most important mappers on the NES. It supports fine-grained PRG/CHR bank switching, mirroring control, and scanline IRQ. Understanding MMC3 also illuminates why PPU timing affects cartridge hardware.

This chapter is based on AprNes's `Mapper004.cs`, `Mapper004RevA.cs`, and `Mapper004MMC6.cs`.

## NES Hardware Concepts

**Everyday analogy**: MMC3 is Nintendo's "**Swiss-army mapper**" — practically standard equipment for the second half of the NES era. In one chip it provides:
- **Finer PRG switching**: UNROM swaps 16 KB at a time; MMC3 swaps 8 KB across 4 independent slots.
- **More flexible CHR switching**: CNROM swaps 8 KB at a time; MMC3 can switch two 2 KB banks plus four 1 KB banks across 8 slots.
- **Built-in IRQ counter**: notifies the CPU automatically when "the PPU has scanned to line X."

That last item is revolutionary — **scanline IRQ** lets games trigger events on a precise raster line without spamming sprite-0-hit polling, enabling complex split scrolling, status bars, and effects. **Notable titles**: *Super Mario Bros. 3*, *Mega Man 3-6*, *Kirby's Adventure*, *Crystalis*.

MMC3 features:

- 8 KB PRG bank switching.
- 1 KB / 2 KB CHR bank switching.
- mirroring control.
- IRQ latch / reload / enable.
- **Watches PPU A12 rising edges** to clock the scanline counter.

### PRG bank mode

MMC3's CPU `$8000-$FFFF` is divided into four 8 KB regions:

```text
$8000-$9FFF
$A000-$BFFF
$C000-$DFFF
$E000-$FFFF
```

The last 8 KB is typically pinned to the last bank. PRG mode decides whether `$8000-$9FFF` or `$C000-$DFFF` holds the fixed second-to-last bank, and which one is the switchable bank.

### CHR bank mode

PPU `$0000-$1FFF` is split into 8 × 1 KB slots. MMC3 has:

- two 2 KB CHR banks.
- four 1 KB CHR banks.

CHR mode decides whether the 2 KB banks are in the lower or upper half.

### IRQ

MMC3 scanline IRQ is not "increment one per scanline." **The hardware actually watches the PPU address bus's A12 line**:

- PPU reads `$0000-$0FFF` → A12 = 0 (pattern table 0).
- PPU reads `$1000-$1FFF` → A12 = 1 (pattern table 1).
- Background and sprite pattern fetches read different pattern tables at different times, causing A12 to rise and fall.
- Each A12 0 → 1 transition decrements the mapper's internal counter.
- When the counter hits 0, an IRQ is asserted.

**Everyday analogy**: MMC3 doesn't wear a watch — it tells time by watching "**when the kitchen's gas burner ramps up to high**." When the background and sprite use different pattern tables, the A12 line "flares up" once mid-scanline. MMC3 catches that signal and counts elapsed scanlines.

```text
Standard NES PPU pattern fetches per scanline:
  dot 1-256    background fetch (using BG pattern table)
  dot 257-320  sprite fetch (using sprite pattern table)
  dot 321-336  next scanline's first 2 background tiles

If BG uses $0000 (A12=0) and sprites use $1000 (A12=1):
  during the scanline: A12 rises at dot 256→257 ★ ← MMC3 sees this edge
  later returns to 0
  next scanline another edge ★

Each visible scanline produces about 1 A12 rising edge → MMC3 counter -1
```

**Why is this design so intricate?** Because Nintendo wanted MMC3 to count scanlines accurately without adding extra signal lines to the mapper. **Since the PPU already follows a specific pattern-table access pattern each scanline, the A12 edges become a "free scanline clock"**.

A side effect: **the game must use different pattern tables for BG and sprites** (e.g., BG at `$0000`, sprites at `$1000`); otherwise A12 never changes and scanline IRQ never works. `$2000` bit 3 and bit 4 control these selections.

To prevent brief pulses from being misread, the next rising edge only counts after A12 has stayed low for a while. Emulators usually maintain a counter for "how many PPU cycles A12 has been low" and only accept the next rising edge after a threshold (typically around 8 cycles). MMC3 also has different revisions (Sharp vs IRQ A vs IRQ B) with subtly different behaviour.

## Beginner-Friendly Simplification

A staged approach:

1. Implement PRG bank mapping.
2. Implement CHR bank mapping.
3. Implement IRQ counter.

A first IRQ implementation can be scanline-based, but be aware that's not the correct final model. To get close to AprNes you need the mapper to see the PPU address bus / A12 edges.

## AprNes / NesCore Implementation Mapping

### Register write

`Mapper004.cs`'s `MapperW_PRG()` dispatches by address and odd/even:

```text
$8000 even  bank select, PRG mode, CHR mode
$8001 odd   bank data
$A000 even  mirroring
$A001 odd   PRG RAM protect, ignored on base Mapper004
$C000 even  IRQ latch
$C001 odd   IRQ reload
$E000 even  IRQ disable and acknowledge
$E001 odd   IRQ enable
```

`BankReg` decides which bank register the `$8001` write updates.

### PRG read

`MapperR_RPG()` per `PRG_Bankmode`:

- mode 0: `$8000` switchable, `$C000` fixed second-to-last.
- mode 1: `$8000` fixed second-to-last, `$C000` switchable.
- `$A000` uses `PRG1_Bankselect`.
- `$E000` fixed to the last bank.

### CHR bank pointer

`UpdateCHRBanks()` translates MMC3 CHR registers into `NesCore.chrBankPtrs[0..7]`.

mode 0:

- two 2 KB banks at `$0000-$0FFF`.
- four 1 KB banks at `$1000-$1FFF`.

mode 1:

- four 1 KB banks at `$0000-$0FFF`.
- two 2 KB banks at `$1000-$1FFF`.

### A12 and IRQ

`PpuClock()`:

- read bit 12 of `NesCore.ppuAddressBus`.
- if A12 is low, accumulate `m2Filter`.
- on a low → high transition with the filter past threshold, call `Mapper04step_IRQ()`.

`Mapper04step_IRQ()`:

- handle `IRQReset`.
- decrement or reload `IRQCounter`.
- when counter hits 0 and `IRQ_enable`, set `NesCore.statusmapperint`.
- call `NesCore.UpdateIRQLine()`.

### Rev A and MMC6

`Mapper004RevA.cs`:

- inherits `Mapper004`.
- overrides only the IRQ step.
- Rev A's trigger condition when the counter reaches 0 differs.

`Mapper004MMC6.cs`:

- IRQ behaviour matches Rev A.
- Adds `$A001` PRG-RAM protection.
- `MapperR_RAM()` and `MapperW_RAM()` enable read/write for the lower / upper 1 KB RAM via bits.

## Common Mistakes

- Clocking MMC3 IRQ directly with CPU cycles or scanline numbers.
- Ignoring PPU A12 filtering.
- Forgetting to force CHR 2 KB bank's low bit even.
- Swapping the fixed-bank position between PRG mode 0 and 1.
- Not acknowledging the IRQ line on `$E000` disable.
- Ignoring MMC3 revision differences, causing specific test ROMs to fail.

## Chapter Recap

1. MMC3 is an advanced mapper combining PRG, CHR, mirroring, and IRQ.
2. MMC3 IRQ is driven by PPU A12 edges, not a plain scanline counter.
3. AprNes lets the mapper read the PPU address bus, so cartridge hardware can interact with PPU timing.

## Bridge to the Next Chapter

The next chapter assembles a recommended implementation order for an NES emulator from scratch, turning the previous chapters into a concrete development path.
