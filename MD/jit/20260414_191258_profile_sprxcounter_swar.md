# sprXCounter byte* + pure-SWAR slow path

- **Date**: 2026-04-14 19:12
- **Config**: WinForms Debug, NTSC 1x native, audio-mode 0, no filter
- **ROM**: ny2011
- **Tests**: 184/184 PASS ✅

---

## Change

1. `sprXCounter` declaration: `int*` (32 bytes) → `byte*` (8 bytes)
2. Fast path SWAR check: `(xc[0] | xc[1] | xc[2] | xc[3]) == 0` → `*(ulong*)sprXCounter == 0`
3. Slow path rewritten from 8-iter `for` loop into **pure SWAR** (no loop, no branch inside):

```csharp
ulong dec_mask = ((v | ((v & 0x7F7F7F7F7F7F7F7FUL) + 0x7F7F7F7F7F7F7F7FUL))
                 & 0x8080808080808080UL) >> 7;
*(ulong*)sprXCounter = v - dec_mask;         // decrement all non-zero
ulong mask_0 = ~(dec_mask * 255UL);           // byte-smear: 0xFF where counter==0
*(ulong*)sprShiftL = (((sl << 1) & 0xFEFE...UL) & mask_0) | (sl & ~mask_0);
*(ulong*)sprShiftH = (((sh << 1) & 0xFEFE...UL) & mask_0) | (sh & ~mask_0);
```

Semantic proof:
- `dec_mask[i] = 1` iff byte `v[i] > 0` (standard "byte > 0" SWAR idiom)
- `v - dec_mask` has no cross-byte borrow because `dec_mask[i] ≤ v[i]` by construction
- `dec_mask * 255` ≡ `(x<<8) - x` is byte-wise smear when each byte is 0/1

---

## Results (1x native, no filter, audio 0)

| Variant | Warm FPS | Profile FPS | ppu_step_new |
|---------|----------|-------------|--------------|
| Baseline (`int*`, branchless for loop) | 106.93 | 105.10 | 54.2% |
| `byte*` + 8× unrolled if-else (rejected) | 105.76 | 102.41 | 50.6% |
| `byte*` + original for-loop (rejected) | ~100 | 97.56 | 54.2% |
| **`byte*` + pure SWAR (this)** | **112.05** | **111.32** | **49.9%** |

**SWAR vs baseline: +4.8% warm, +5.9% profile.** ppu_step_new exclusive
drops 4.3pp; work redistributes slightly to Run_NTSC (call-site
inlining pattern).

---

## Why SWAR wins

- Original slow path: 8-iter loop × (load/cmp/sub/store + 2× shift) ≈ 40+ ops
- SWAR slow path: ~10 pure ulong ALU ops, zero branches, no bounds check
- JIT cannot auto-vectorize this (byte comparison + byte-smear multiply
  is too specific), so the manual rewrite captures the remaining win
- Fast path unchanged (already optimal)

## Side benefit

`sprXCounter` storage shrinks 32 → 8 bytes. Minor but aligns with
actual NES hardware register width (8-bit).
