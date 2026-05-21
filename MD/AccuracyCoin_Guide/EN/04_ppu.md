# Part 4: PPU (Page 16–19, etc.)

> Maps to: **PPU RAM / Palette RAM / PPU Reset Flag**, **PPU Register Mirroring / Open Bus / Read Buffer / Palette RAM Quirks / Rendering Flag / $2007 read w/ rendering**, the **VBlank/NMI series**, **Sprite Evaluation / Sprite 0 Hit / OAM Corruption / Misaligned OAM / $2004 / Suddenly Resize Sprite / Arbitrary Sprite Zero**, **Attributes As Tiles / t Register Quirks / Stale BG & Sprite Shift Registers / BG Serial In / Sprites On Scanline 0 / $2004 & $2007 Stress**.
> Prerequisite: [`00_timing_model.md`](00_timing_model.md) (**PPU half-step, VBL/NMI 1-cycle delay** are the premise for this whole chapter).

The PPU is the largest part of AC and the most demanding of **dot / half-dot precision**. The CPU/APU/DMA pages mostly resolve at cycle level, but the PPU pages frequently check "the ordering of half a dot within the same PPU dot." This is exactly why our main loop packs `ppu_step_new` (MC 0/4/8) + `ppu_half_step_new` (MC 2/6/10) into a single CPU cycle (12 master clocks) — the half-step exists for these PPU tests.

---

## 1. VBlank / NMI timing (stand this up first)

**Tests**: VBlank beginning / end, NMI Control / Timing / Suppression / at VBlank end / disabled at VBlank.

Three hardware facts:
1. **The VBL flag sets at scanline 241 dot 1, clears at the pre-render line (261) dot 1**. One dot off and a slew of tests fail.
2. **NMI is edge-triggered + 1-cycle delay**: the rising edge of (VBL flag AND `$2000` bit7 (NMI enable)) → `nmi_delay` → next tick promotes to `nmi_pending` → CPU checks.
3. **NMI suppression**: reading `$2002` "around the dot" the VBL flag sets suppresses NMI for that frame (reading `$2002` clears `nmi_delay`, cancellable; but not `nmi_pending`, irreversible).

This 1-cycle delay model was established back in the blargg phase (the key 139→154 jump); details in [`00_timing_model.md`](00_timing_model.md) §4. The PPU page's NMI series pushes it to the limit.

---

## 2. `$2002` Flag Clear Timing Stagger — the showcase of half-dot precision

**Test ($2002 flag timing)**: the sprite flags (sprite 0 hit, sprite overflow) appear to clear **about 2 PPU dots earlier** than the VBL flag.

**Hardware truth** ([BUGFIX45](../../bugfix/2026-03-07_BUGFIX45.md)): when reading `$2002`, **the VBL flag is sampled on the M2 rising edge and the sprite flags on the M2 falling edge**. The RP2A03G's M2 duty cycle is **15/24**, so the sprite flags are read ~1.875 PPU dots later than VBL — viewed the other way, the sprite flags "appear" to clear ~2 dots earlier than VBL.

**Fix**: split the pre-render line's flag clear into two dots:
- **dot 1**: clear `isSprite0hit` + `isSpriteOverflow`
- **dot 2**: clear the VBL flag

> This is the best teaching example for "why you need half-dot precision": the three flags can't all clear on the same dot — the difference is the ~2 dots from the M2 duty cycle. Without a sub-dot model, this one is unsolvable.

---

## 3. `$2007` Read Buffer / access during rendering / `$2006` delayed t→v copy

### Read buffer
Reading `$2007` (the non-palette region) returns the **previous** read's buffered value, while this read's value goes into the buffer first. The palette region returns directly + updates the buffer simultaneously (it reads the nametable mirror beneath).

### Access during rendering
With rendering on, reading/writing `$2007` uses the "rendering v register" and triggers a weird address increment (coarse X + Y both move). The tests `$2007 read w/ rendering` and `$2004 read/write during rendering` specifically check these.

### `$2006` delayed t→v copy (affects real games!)
**Hardware truth** ([BUGFIX57](../../bugfix/2026-03-23_BUGFIX57_PPU2006_Delayed_Copy.md)): after the CPU's second write to `$2006`, the `t→v` copy does **not** take effect immediately — it's delayed about **4–5 PPU dots** (the PPU's internal bus needs time to propagate the signal).

We copied immediately at first → **the platform in Mega Man 5's elevator stage shook up/down by 1 scanline every frame**. Switching to a delayed copy made it steady.

> This pitfall is especially worth telling: it **affects not just AC but real games**. Many people think "passing AC means you're done," but it was only after AC 136/136 that the actual on-screen image (Mega Man 5, `scanline-a1`, `colorwin_ntsc`) revealed the PPU timing was still imprecise — which was the trigger for deciding to [replace the whole thing with the TriCNES per-master-clock model](00_timing_model.md#2-aprnes的演進三代-timing-模型). The `$2005` scroll write has a similar 2-dot delay (added later per the TriCNES model).

---

## 4. Palette RAM Quirks

**Test (Palette RAM Quirks)**:
- `$3F10/$3F14/$3F18/$3F1C` are mirrors of `$3F00/$3F04/$3F08/$3F0C` (shared backdrop color).
- the grayscale mask (`$2001` bit0) makes palette reads `& $30`.
- palette RAM's open-bus behavior (a palette read updates only the low 6 bits of the data bus; the upper 2 bits stay open bus).

These are lookup + mask logic with little cycle demand, but the mirror addresses must be right.

---

## 5. Sprite Evaluation / Sprite 0 Hit / OAM Corruption

This is the largest engineering block of the PPU pages, all built on a **secondary OAM + per-dot sprite evaluation FSM**.

- **Sprite evaluation FSM**: on a visible scanline, dots 1–256 do sprite evaluation (scan primary OAM, push in-range entries into secondary OAM), and dots 257–320 fetch sprite tile data. This must run as a **per-dot** state machine; you can't compute it all at once.
- **Sprite 0 hit**: the flag sets only at the **exact dot** sprite 0's opaque pixel collides with an opaque background pixel (with quirks like x=255 not triggering, dot 0 not triggering).
- **OAM Corruption** ([BUGFIX36](../../bugfix/2026-03-07_BUGFIX36.md)): when rendering is enabled/disabled on specific dots, OAM gets corrupted by a hardware bug — you have to emulate the exact "way it breaks."
- **$2004 read during sprite evaluation** ([BUGFIX41](../../bugfix/2026-03-07_BUGFIX41.md)): reading `$2004` during rendering returns the "OAM buffer value evaluation is currently pointing at," not static OAM.
- **Suddenly Resize Sprite** ([BUGFIX42](../../bugfix/2026-03-07_BUGFIX42.md)): sprite size (8x8/8x16) latches only at a specific dot during CHR fetch — changing `$2000` sprite size mid-scanline produces transitional behavior.
- **Sprites On Scanline 0** ([BUGFIX47](../../bugfix/2026-03-08_BUGFIX47.md)): the pre-render line (261)'s dots 257–320 use `(261 & 255) = 5` as the effective scanline for the in-range check; secondary OAM still holds the result from the previous visible line (239). If a sprite falls in scanline 5's range, its tile data is loaded into the shift register and persists into scanline 0.

> All of this requires "secondary OAM is a real buffer, and sprite evaluation is a state machine advancing per-dot." We have an `AccuracyOptA` toggle controlling the per-dot secondary OAM FSM (performance vs accuracy) — forced on for AC validation (headless defaults to on).

---

## 6. Shift Registers (Stale BG / Sprite + Rendering Flag)

- **Stale BG Shift Registers** ([BUGFIX40](../../bugfix/2026-03-07_BUGFIX40.md)) / **Rendering Flag Behavior** ([BUGFIX43](../../bugfix/2026-03-07_BUGFIX43.md)): when rendering is turned off, the BG shift register **freezes** (no more shifting, no more reload), and still holds the old value when re-enabled. Advanced tests like `BG Serial In` and `Attributes As Tiles` create visual tricks by freezing/thawing the shift register at precise rendering-on/off dots.
- **Stale Sprite Shift Regs**: similar, but for the sprite shift register (the recent AC 20260521 even reordered its in-range clear timing, see the [version diff](../../notes/AccuracyCoin_20260521_diff_and_result.md)).
- **t Register Quirks**: the write timing of `$2005`/`$2006`/`$2000` to each bit of the internal `t` register.

---

## Summary

The PPU page's motif: **many events happen at "half a dot" precision, and latch updates are delayed.**

- VBL/NMI: 1-cycle delay + edge trigger + suppression.
- `$2002` flags: sprite flags clear ~2 dots before VBL (M2 duty cycle).
- `$2006`/`$2005`: t→v / scroll update delayed 4–5 / 2 dots.
- Sprite: secondary OAM + per-dot evaluation FSM + the exact sprite 0 hit dot.
- Shift registers: freeze/thaw at precise rendering-on/off dots.

These are exactly why the main loop has `ppu_half_step_new` (half-dot). **When the PPU pages don't pass, it's almost always insufficient timing-model granularity — not a wrong rendering formula.** And so the PPU was the main battlefield in pushing from v1 136/136 to alignment with TriCNES's per-master-clock model and v2 138/138.

Next (appendices): [`appendix_error_code_index.md`](appendix_error_code_index.md) (per-page error-code quick reference), [`appendix_tricnes_reference.md`](appendix_tricnes_reference.md) (TriCNES as ground truth + its known failures).
