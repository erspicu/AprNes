# OAM Multiplexer SWAR (loopless sprite-pixel pick)

- **Date**: 2026-04-14 19:26
- **Config**: WinForms Debug, NTSC 1x native, audio 0, no filter, ny2011
- **Tests**: 184/184 PASS ✅

---

## Change

Replace the 8-iter for+break loop that picks the lowest-index sprite
with an active pixel, with a pure SWAR pipeline:

1. `has_bits = ((xc & 0x7F...) + 0x7F...) | xc` — bit7-per-byte iff counter > 0
2. `active_mask = ~has_bits & 0x80...` (or all 0x80 when `skippedPreRenderDot341`)
3. `pixel_mask = (H | L) & 0x80...` — bit7 of (shiftH|shiftL) per sprite
4. `valid = active_mask & pixel_mask`
5. Early exit on `valid == 0` (common — most dots have no sprite)
6. `valid & -valid` isolates lowest-bit → lowest-index sprite (little-endian)
7. 3-level binary-tree decode → sprite index 0..7

Equivalence: original finds first i where (counter==0 OR skipped) AND
((h|l) bit7 set) AND breaks. SWAR produces `valid` with bit7-per-sprite
indicating the same predicate; isolate-lowest maps to first match in
little-endian storage order.

---

## Results

| Variant | Warm FPS | Profile FPS | ppu_step_new Excl% |
|---------|----------|-------------|--------------------|
| Baseline (int* + branchless for) | 106.93 | 105.10 | 54.2% |
| sprXCounter byte+SWAR (prior) | 112.05 | 111.32 | 49.9% |
| **+ OAM mux SWAR (this)** | **117.53** | **116.93** | **48.9%** |

**This commit: +4.9% warm / +5.0% profile.**
**Cumulative vs baseline: +9.9% warm / +11.3% profile.**

---

## Why it wins

1. **Super-early exit**: `valid == 0` skips everything when no sprite
   has a visible pixel at this dot (common on empty background areas).
   Original loop had to iterate at least some sprites.
2. **No branch in hot case**: when valid != 0, the index decode is 3
   cmov-friendly compares instead of a variable-trip-count loop.
3. **Only the winning sprite's h/l/attr is loaded** — original loaded
   h/l for every iterated slot before the `(h|l) >= 128` check.

JIT cannot auto-vectorize this kind of data-dependent sprite-priority
pick; hand-written SWAR is the remaining win.
