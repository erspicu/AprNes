# PPU Rendering Tick: AprNes vs TriCNES -- Detailed Comparison

This document compares the PPU tile-fetch rendering logic between:
- **AprNes**: `ppu_rendering_tick()` in `AprNes/NesCore/PPU.cs` (line ~527)
- **TriCNES**: `PPU_Render_ShiftRegistersAndBitPlanes()` in `ref/TriCNES-main/Emulator.cs` (line ~3555), sprite evaluation code (line ~2840-2970), and garbage NT fetches (line ~3668)

Both emulators use a "phase" system where an 8-dot tile fetch cycle runs phases 0-7 repeatedly. However, their phase numbering is offset by 1 dot due to different conventions.

---

## Key Structural Difference: Phase Numbering

| Aspect | AprNes | TriCNES |
|--------|--------|---------|
| Phase computation | `phase = cx & 7` | `cycleTick = (byte)(PPU_Dot & 7)` |
| BG fetch range | `cx < 256 \|\| (cx >= 320 && cx < 336)` | `PPU_Dot >= 0 && PPU_Dot < 257 \|\| PPU_Dot > 320 && PPU_Dot <= 336` |
| Address compute | Even phases (0, 2, 4, 6) | Odd phases (1, 3, 5, 7) |
| Data fetch / bus set | Odd phases (1, 3, 5, 7) | Same as address compute (1, 3, 5, 7) |
| Commit timing | Deferred via `PPU_Commit_*` flags | Deferred via `commitCXinc` (AprNes) |

**Critical insight**: TriCNES uses a deferred commit model (`PPU_Commit_*` flags set during fetch, applied at the START of the NEXT full PPU step via `PPU_Render_CommitShiftRegistersAndBitPlanes()`). AprNes computes the address one phase early and fetches the data one phase later, achieving the same net timing without deferred commits for most operations. The exception is `commitCXinc` which defers CXinc to the next dot, matching TriCNES's `PPU_Commit_PatternHighFetch`.

---

## 1. BG Tile Fetch (dots 0-255, 320-335)

### Phase 0 (AprNes) -- idle dot in TriCNES

| Category | AprNes | TriCNES |
|----------|--------|---------|
| Address compute | `ioaddr = 0x2000 \| (vram_addr & 0x0FFF)` (NT address) | case 0: `break` (nothing happens) |
| Bus update | Not yet | Not yet |
| Data fetch | Not yet | Not yet |
| A12 notification | Not yet | Not yet |

**Status**: -- N/A (different phase numbering; AprNes computes address 1 phase ahead)

**Notes**: AprNes pre-computes the nametable address at phase 0 into the local variable `ioaddr`. TriCNES does nothing at case 0 because it computes + fetches simultaneously at case 1. This is a structural difference -- AprNes splits address computation and data fetch into separate phases.

---

### Phase 1 (AprNes) = Case 1 (TriCNES) -- Nametable Fetch

| Category | AprNes | TriCNES |
|----------|--------|---------|
| Address compute | (already done at phase 0) | `PPU_AddressBus = (ushort)(0x2000 + (PPU_ReadWriteAddress & 0x0FFF))` |
| PPU_AddressBus set | `ppuAddressBus = ioaddr` | Inline (same line as compute) |
| Data fetch | `NTVal = ppu_ram[CIRAMAddr(ioaddr)]` (or ntBankPtrs for override) | `PPU_RenderTemp = FetchPPU(PPU_AddressBus)` + sets `PPU_Commit_NametableFetch = true` |
| A12 notification | `if (mapperA12IsMmc3) NotifyMapperA12(ioaddr)` (NT addr has A12=0) | None at this point (A12 detected at end of PPU cycle via `PPU_MapperSpecificFunctions`) |
| MMC5 notification | `mmc5Ref.NotifyVramRead(ioaddr)` | N/A (TriCNES does not implement MMC5) |

**Status**: ✅ Matched (equivalent behavior, structural difference)

**Notes**:
- AprNes sets `ppuAddressBus` and performs the NT read in a single phase (1). TriCNES sets `PPU_AddressBus` and reads `PPU_RenderTemp` in the same case (1), then defers the commit to `PPU_NextCharacter` until the next full step's `PPU_Render_CommitShiftRegistersAndBitPlanes()`.
- AprNes directly writes to `NTVal` (immediate). TriCNES defers via `PPU_Commit_NametableFetch` flag. Since the NT value is only needed at phase 4/5 (CHR address computation), the 1-dot commit delay has no functional effect within the same 8-dot tile group.
- A12 notification differs in mechanism (see Section 5) but produces equivalent timing.

---

### Phase 2 (AprNes) = Case 2 (TriCNES) -- Attribute Address Compute

| Category | AprNes | TriCNES |
|----------|--------|---------|
| Address compute | `ioaddr = 0x23C0 \| (vram_addr & 0x0C00) \| ((vram_addr >> 4) & 0x38) \| ((vram_addr >> 2) & 0x07)` | case 2: `break` (nothing) |
| Bus update | Not yet | Not yet |
| Data fetch | Not yet | Not yet |
| A12 notification | Not yet | Not yet |

**Status**: -- N/A (phase offset; AprNes pre-computes AT address)

**Notes**: Same pattern as phase 0 -- AprNes computes the attribute table address one phase early.

---

### Phase 3 (AprNes) = Case 3 (TriCNES) -- Attribute Fetch

| Category | AprNes | TriCNES |
|----------|--------|---------|
| Address compute | (already done at phase 2) | `PPU_AddressBus = (ushort)(0x23C0 \| ...)` (same formula) |
| PPU_AddressBus set | `ppuAddressBus = ioaddr` | Inline with address compute |
| Data fetch | `ATVal = (ppu_ram[CIRAMAddr(ioaddr)] >> shift) & 3` (pre-shifted) | `PPU_RenderTemp = FetchPPU(PPU_AddressBus)` + `PPU_Commit_AttributeFetch = true` |
| Attribute extraction | Immediate: `ATVal` is the 2-bit attribute for this tile | Deferred: full byte stored, then shifted in `PPU_Render_CommitShiftRegistersAndBitPlanes()` |
| Attribute latch | `attrLatch = ATVal` (explicit) | `PPU_AttributeLatchRegister = PPU_Attribute` (in commit) |
| A12 notification | None (AT address never has A12=1) | None |
| MMC5 notification | `mmc5Ref.NotifyVramRead(ioaddr)` | N/A |

**Status**: ✅ Matched

**Notes**:
- Address formula is identical: `0x23C0 | (v & 0x0C00) | ((v >> 4) & 0x38) | ((v >> 2) & 0x07)`.
- AprNes pre-shifts the attribute byte inline to extract the 2-bit value; TriCNES defers this to the commit function. Both produce the same 2-bit attribute.
- TriCNES attribute shift uses `(PPU_ReadWriteAddress & 3) >= 2` for right column and `(((PPU_ReadWriteAddress & 0x1F0) >> 5) & 3) >= 2` for bottom row. AprNes uses `((vram_addr >> 4) & 0x04) | (vram_addr & 0x02)` as a combined shift amount. Both are mathematically equivalent.
- AprNes also maintains a `bg_attr_p1/p2/p3` pipeline which is AprNes-specific (not in TriCNES).

---

### Phase 4 (AprNes) = Case 4 (TriCNES) -- CHR Low Address Compute

| Category | AprNes | TriCNES |
|----------|--------|---------|
| Address compute | `ioaddr = BgPatternTableAddr \| (NTVal << 4) \| ((vram_addr >> 12) & 7)` | case 4: `break` (nothing) |
| Bus update | Not yet | Not yet |
| Data fetch | Not yet | Not yet |
| A12 notification | Not yet | Not yet |

**Status**: -- N/A (phase offset)

**Notes**: AprNes pre-computes the CHR low bitplane address. Formula is equivalent to TriCNES's case 5 formula: `(fineY) | NTChar * 16 | patternSelect`. AprNes uses `BgPatternTableAddr` (0 or 0x1000) while TriCNES uses `PPU_PatternSelect_Background ? 0x1000 : 0`.

---

### Phase 5 (AprNes) = Case 5 (TriCNES) -- CHR Low Bitplane Fetch

| Category | AprNes | TriCNES |
|----------|--------|---------|
| Address formula | (from phase 4): `BgPatternTableAddr \| (NTVal << 4) \| fineY` | `((v & 0x7000) >> 12) \| PPU_NextCharacter * 16 \| (BgSelect ? 0x1000 : 0)` |
| PPU_AddressBus set | `ppuAddressBus = ioaddr` | `PPU_AddressBus = (ushort)(formula)` |
| Data fetch | `lowTile = chrBankPtrs[(ioaddr >> 10) & 7][ioaddr & 0x3FF]` | `PPU_RenderTemp = FetchPPU((ushort)(PPU_AddressBus & 0x1FFF))` + `PPU_Commit_PatternLowFetch = true` |
| ppuChrFetchA12 | `ppuChrFetchA12 = (ioaddr >> 12) & 1` | N/A (TriCNES doesn't track separately) |
| A12 notification | `if (mapperNeedsA12) NotifyMapperA12(ioaddr)` | End-of-cycle detection via `PPU_MapperSpecificFunctions()` |
| MMC5 notification | `mmc5Ref.NotifyVramRead(ioaddr)` | N/A |

**Status**: ✅ Matched

**Notes**:
- Address formulas produce identical values. TriCNES applies `& 0x1FFF` mask to the fetch address (preventing reads beyond CHR range); AprNes relies on the bank pointer structure for equivalent masking.
- AprNes tracks `ppuChrFetchA12` separately for the MMC3 M2 filter model; TriCNES uses the unified `PPU_AddressBus` bit 12 check.
- Both set the bus address at this phase, allowing A12 to be detected.

---

### Phase 6 (AprNes) = Case 6 (TriCNES) -- CHR High Address Compute

| Category | AprNes | TriCNES |
|----------|--------|---------|
| Address compute | `ioaddr = BgPatternTableAddr \| (NTVal << 4) \| fineY \| 8` | case 6: `break` (nothing) |
| Bus update | Not yet | Not yet |

**Status**: -- N/A (phase offset)

**Notes**: AprNes adds `| 8` to get the high bitplane address. TriCNES does this at case 7.

---

### Phase 7 (AprNes) = Case 7 (TriCNES) -- CHR High Bitplane Fetch + Reload

| Category | AprNes | TriCNES |
|----------|--------|---------|
| Address formula | (from phase 6): `...base \| 8` | `(fineY \| NTChar*16 \| BgSelect) + 8` |
| PPU_AddressBus set | `ppuAddressBus = ioaddr` | `PPU_AddressBus = (ushort)(formula)` |
| Data fetch | `highTile = chrBankPtrs[(ioaddr >> 10) & 7][ioaddr & 0x3FF]` | `PPU_RenderTemp = FetchPPU(PPU_AddressBus & 0x1FFF)` + `PPU_Commit_PatternHighFetch = true` |
| A12 notification | `if (mapperNeedsA12 && !mapperA12IsMmc3) NotifyMapperA12(ioaddr)` (MMC2/MMC4 only) | End-of-cycle A12 detection |
| Shift register reload | `lowshift = (lowshift << 8) \| lowTile`, `highshift = (highshift << 8) \| highTile` | Deferred: `PPU_Commit_PatternHighFetch` triggers `PPU_IncrementScrollX()` at next step's commit |
| Render register reload | `renderLow = (renderLow & 0xFF00) \| lowTile` | Deferred: `PPU_Commit_LoadShiftRegisters` at half-dot step |
| CXinc | `commitCXinc = true` (deferred to next dot) | `PPU_IncrementScrollX()` called inside commit handler (also next dot) |
| RenderBGTile | Called inline for visible pixels (`cx < 256`) | N/A (TriCNES renders per-dot via shift register) |

**Status**: ✅ Matched (CXinc deferred timing matches)

**Notes**:
- **CXinc timing is identical**: Both defer the coarse X increment to the following dot. AprNes uses an explicit `commitCXinc` flag checked at the start of `ppu_step_rendering()`. TriCNES uses `PPU_Commit_PatternHighFetch` which fires `PPU_IncrementScrollX()` inside `PPU_Render_CommitShiftRegistersAndBitPlanes()`, called at the beginning of the next `_EmulatePPU()`.
- **Shift register reload**: AprNes loads shift registers immediately at phase 7. TriCNES splits this: the high bitplane data is committed at the next full step (setting `PPU_HighBitPlane`), while the shift register load (`PPU_LoadShiftRegisters`) happens at the next half-step via `PPU_Commit_LoadShiftRegisters`. This half-dot deferral is an important timing nuance.
- AprNes calls `RenderBGTile()` at phase 7 for visible pixels to commit the current tile's palette cache. TriCNES renders per-dot from shift registers in `PPU_Render_CalculatePixel()`.
- AprNes skips MMC3 A12 notification for CHR high (only MMC2/MMC4 get notified). This is correct because MMC3 needs only one A12 rising edge per tile, already detected at phase 5 (CHR low).

---

## 2. Sprite Tile Fetch (dots 257-320)

TriCNES uses `SpriteEvaluationTick` (0-7) inside `PPU_Render_SpriteEvaluation()` for dots 257-320. AprNes uses `sprPhase = (cx - 257) & 7` and `slot = (cx - 257) >> 3`.

**Phase numbering alignment**: TriCNES's `SpriteEvaluationTick` cases 0-7 correspond to dots 257-264 for sprite slot 0, etc. AprNes's `sprPhase` 0 maps to dot 257, sprPhase 1 to dot 258, etc.

However, there is a **1-dot offset** between the two systems:
- TriCNES case 0 starts at dot 257; case 3 is dot 260 (X position), case 4 is dot 261 (CHR address), case 5 is dot 262 (CHR low fetch)
- AprNes sprPhase 0 starts at dot 257; phase 3 is dot 260 (X position), phase 4 is dot 261 (CHR address), phase 5 is dot 262 (CHR low fetch)

These actually align correctly since both use `(cx - 257) & 7` or `SpriteEvaluationTick++` from the same starting point.

---

### sprPhase 0 (dot 257, 265, 273, ...) = TriCNES case 0

| Category | AprNes | TriCNES |
|----------|--------|---------|
| PPU_AddressBus | `ppuAddressBus = 0x2000 \| (vram_addr & 0x0FFF)` (dummy NT) | calls `PPU_Render_ShiftRegistersAndBitPlanes()` which at case 1 also sets NT address (but SpriteEvaluationTick 0 is at a dot where the BG fetch function uses a *different* case via `PPU_Dot & 7`) |
| OAM read | None | `PPU_OAMLatch = OAM2[OAM2Address]`; `PPU_SpriteYposition[slot] = PPU_OAMLatch`; `OAM2Address++` |
| CopyHoriV | At dot 257 only: `CopyHoriV(); spr_ram_add = 0` | At dot 257: `PPU_ResetXScroll()` (in main loop, line 1507) |
| A12 notification | Not at this phase | Not at this phase |

**Status**: ⚠️ Partial

**Notes**:
- **Dummy NT bus set**: AprNes directly sets `ppuAddressBus` to the dummy NT address. TriCNES calls the BG tile fetch function, which runs at `PPU_Dot & 7`; for dots 257+, this still computes `PPU_Dot & 7` but the BG function's case may not align. However, TriCNES also explicitly calls `PPU_Render_ShiftRegistersAndBitPlanes()` inside each sprite eval case 0-3 for the dummy NT fetch behavior.
- **OAM read difference**: TriCNES reads Y position from secondary OAM at case 0 and stores it into `PPU_SpriteYposition[]`. AprNes does not explicitly read Y at sprPhase 0; instead, `ComputeSpritePatternAddr()` reads directly from `secondaryOAM[slot * 4]` at sprPhase 4. Functionally equivalent since the secondary OAM data is stable.
- **OAM2Address tracking**: TriCNES increments `OAM2Address` per case (0,1,2 increment; 3 does not; 7 increments). AprNes does not track OAM2Address during sprite fetch (uses `slot * 4 + offset` direct indexing).

---

### sprPhase 1 (dot 258, 266, ...) = TriCNES case 1

| Category | AprNes | TriCNES |
|----------|--------|---------|
| A12 notification | `if (mapperNeedsA12) NotifyMapperA12(ppuAddressBus)` | `PPU_Render_ShiftRegistersAndBitPlanes()` (dummy NT fetch); A12 detected at end-of-cycle |
| OAM read | None | `PPU_SpritePattern[slot] = OAM2[OAM2Address]`; `OAM2Address++` |

**Status**: ✅ Matched (A12 timing equivalent)

**Notes**: AprNes notifies A12 one dot after the bus was set (phase 0 set → phase 1 notify), matching TriCNES's end-of-cycle detection model.

---

### sprPhase 2 (dot 259, 267, ...) = TriCNES case 2

| Category | AprNes | TriCNES |
|----------|--------|---------|
| PPU_AddressBus | `ppuAddressBus = AT address formula` (dummy AT) | `PPU_Render_ShiftRegistersAndBitPlanes()` (dummy NT -- note: still calls BG fetch, not AT) |
| Attribute read | `sprFetchAttr[slot] = secondaryOAM[slot * 4 + 2]` | `PPU_SpriteAttribute[slot] = OAM2[OAM2Address]`; `OAM2Address++` |

**Status**: ✅ Matched

**Notes**:
- AprNes sets the dummy AT address on the bus. TriCNES calls `PPU_Render_ShiftRegistersAndBitPlanes()` which depending on `PPU_Dot & 7` may set a different address. The exact bus address during sprite fetches matters primarily for A12 detection (and the address at sprPhase 2 has A12=0 regardless).
- Both read the attribute byte from secondary OAM (byte 2 of each 4-byte sprite entry). AprNes uses direct indexing `slot * 4 + 2`; TriCNES uses sequential `OAM2Address`.

---

### sprPhase 3 (dot 260, 268, ...) = TriCNES case 3

| Category | AprNes | TriCNES |
|----------|--------|---------|
| X position read | `sprXPos[slot] = secondaryOAM[slot * 4 + 3]` | `PPU_SpriteXposition[slot] = OAM2[OAM2Address]` (no OAM2Address increment) |

**Status**: ✅ Matched

**Notes**: TriCNES explicitly notes "the secondary OAM address does not get incremented until case 7". AprNes uses direct indexing so this doesn't matter.

---

### sprPhase 4 (dot 261, 269, ...) = TriCNES case 4

| Category | AprNes | TriCNES |
|----------|--------|---------|
| CHR address compute | `ppuAddressBus = ComputeSpritePatternAddr(slot)` | `PPU_SpriteEvaluation_GetSpriteAddress(slot)` sets `PPU_AddressBus` |
| ppuChrFetchA12 | `(ppuAddressBus >> 12) & 1` | N/A |
| Sprite size latch | At dot 261 specifically: `spriteSizeLatchedForFetch = Spritesize8x16` | Not present (TriCNES uses live `PPU_Spritex16`) |
| X position read (again) | No | Yes: `PPU_SpriteXposition[slot] = OAM2[OAM2Address]` |

**Status**: ✅ Matched (address computation equivalent)

**Notes**:
- **Address computation**: Both implementations produce identical CHR addresses for both 8x8 and 8x16 sprites. The formulas account for vertical flip, pattern table selection, and 8x16 half-tile selection.
- AprNes latches `spriteSizeLatchedForFetch` at dot 261 (first sprite CHR address compute). This prevents a mid-frame $2000 write from affecting sprite fetch sizing. TriCNES reads `PPU_Spritex16` live at each call to `GetSpriteAddress()`, but since $2000 writes are delayed by 1-2 PPU cycles in TriCNES's model, the effective behavior is similar.
- TriCNES re-reads X position from OAM2 at cases 4-7; AprNes only reads it once at phase 3.

---

### sprPhase 5 (dot 262, 270, ...) = TriCNES case 5

| Category | AprNes | TriCNES |
|----------|--------|---------|
| A12 notification | `if (mapperNeedsA12) NotifyMapperA12(ppuAddressBus)` | End-of-cycle detection |
| CHR low fetch | `tile = chrBankPtrs[(addr >> 10) & 7][addr & 0x3FF]` | `PPU_SpritePatternL = FetchPPU(PPU_AddressBus)` |
| Horizontal flip | `sprShiftL[slot] = flipH ? FlipByte(tile) : tile` | `if (attr bit 6) PPU_SpritePatternL = Flip(PPU_SpritePatternL)` |
| In-range check | No (handled at slot boundary) | Yes: clears shift register if sprite not in Y range |

**Status**: ⚠️ Partial

**Notes**:
- **CHR fetch**: Both fetch the low bitplane and apply horizontal flip. `FlipByte()` and `Flip()` use identical bit-reversal algorithms.
- **In-range check**: TriCNES performs an explicit Y-range check at case 5 and clears the shift register if the sprite is out of range (defensive, handles edge cases with mid-scanline OAM changes). AprNes handles this differently: at phase 7, `if (slot >= sprSlotCount) { sprShiftL/H[slot] = 0; }` clears empty slots. The `sprSlotCount` comes from the sprite evaluation phase which already determined valid sprites.
- **A12 notification**: AprNes notifies 1 dot after address set (phase 4 set → phase 5 notify), matching TriCNES's end-of-cycle model.

---

### sprPhase 6 (dot 263, 271, ...) = TriCNES case 6

| Category | AprNes | TriCNES |
|----------|--------|---------|
| CHR high address | `ppuAddressBus = ComputeSpritePatternAddr(slot) + 8` | `PPU_SpriteEvaluation_GetSpriteAddress(slot)` then `PPU_AddressBus += 8` |
| ppuChrFetchA12 | `(ppuAddressBus >> 12) & 1` | N/A |

**Status**: ✅ Matched

**Notes**: Both recompute the sprite CHR address and add 8 for the high bitplane. TriCNES comments: "at this point, the address couldn't possibly overflow, so there's no need to worry about that."

---

### sprPhase 7 (dot 264, 272, ...) = TriCNES case 7

| Category | AprNes | TriCNES |
|----------|--------|---------|
| A12 notification | `if (mapperNeedsA12 && !mapperA12IsMmc3) NotifyMapperA12(ppuAddressBus)` (MMC2/MMC4 only) | End-of-cycle detection |
| CHR high fetch | `tile = chrBankPtrs[(addr >> 10) & 7][addr & 0x3FF]` | `PPU_SpritePatternH = FetchPPU(PPU_AddressBus)` |
| Horizontal flip | `sprShiftH[slot] = flipH ? FlipByte(tile) : tile` | `if (attr bit 6) PPU_SpritePatternH = Flip(PPU_SpritePatternH)` |
| In-range check | `if (slot >= sprSlotCount) { sprShiftL[slot] = 0; sprShiftH[slot] = 0; }` | Clears shift register H if sprite not in Y range |
| OAM2Address | N/A | `OAM2Address++` (only incremented here out of cases 3-7) |

**Status**: ✅ Matched

**Notes**:
- Both fetch the high bitplane and apply horizontal flip.
- AprNes skips MMC3 A12 notification for sprite CHR high (same reasoning as BG: only one A12 edge per sprite tile is needed, already detected at phase 5).
- Empty slot clearing: AprNes clears both L and H at phase 7 for slots beyond `sprSlotCount`. TriCNES clears L at case 5 and H at case 7 individually via Y-range check.

---

## 3. Garbage NT Fetches (dots 336-340)

### TriCNES: `PPU_Render_ShiftRegistersAndBitPlanes_DummyNT()`

TriCNES uses a dedicated function for dots 336-340 (`cycleTick = PPU_Dot - 336`):

| Dot | TriCNES (cycleTick) | AprNes |
|-----|---------------------|--------|
| 336 | case 0: `PPU_AddressBus = NT addr`; `PPU_RenderTemp = FetchPPU(addr)` | `ppuAddressBus = 0x2000 \| (vram_addr & 0x0FFF)` |
| 337 | case 1: `PPU_NextCharacter = PPU_RenderTemp` (commit) | A12 notify: `if (mapperNeedsA12) NotifyMapperA12(ppuAddressBus)` |
| 338 | case 2: `PPU_AddressBus = NT addr`; `PPU_RenderTemp = FetchPPU(addr)` | `ppuAddressBus = 0x2000 \| (vram_addr & 0x0FFF)` |
| 339 | case 3: `PPU_NextCharacter = PPU_RenderTemp` (commit) | A12 notify + sprite X counter init |
| 340 | case 4: `PPU_AddressBus = CHR pattern addr` (address set only, no fetch) | `ppuAddressBus = BgPatternTableAddr \| (NTVal << 4) \| fineY`; `ppuChrFetchA12 = bit 12` |

### Dot-by-dot comparison:

#### Dot 336

| Category | AprNes | TriCNES |
|----------|--------|---------|
| PPU_AddressBus | `0x2000 \| (vram_addr & 0x0FFF)` | `0x2000 + (PPU_ReadWriteAddress & 0x0FFF)` |
| Data fetch | None | `FetchPPU(PPU_AddressBus)` into `PPU_RenderTemp` |

**Status**: ⚠️ Partial

**Notes**: AprNes sets the bus address but does not perform an actual NT read. TriCNES fetches the NT byte (though it is a garbage fetch, the value is stored in `PPU_RenderTemp`). The fetch may matter for memory-mapped cartridge features that track reads, but for standard mappers the difference is cosmetic.

#### Dot 337

| Category | AprNes | TriCNES |
|----------|--------|---------|
| NT commit | None | `PPU_NextCharacter = PPU_RenderTemp` |
| A12 notification | `NotifyMapperA12(ppuAddressBus)` | End-of-cycle detection at 336 |
| MMC5 notification | `mmc5Ref.NotifyVramRead(0x2000 \| ...)` | N/A |

**Status**: ✅ Matched (A12 timing equivalent; NT commit is harmless)

#### Dot 338

| Category | AprNes | TriCNES |
|----------|--------|---------|
| PPU_AddressBus | `0x2000 \| (vram_addr & 0x0FFF)` (identical to dot 336) | Same |
| Data fetch | None | `FetchPPU(PPU_AddressBus)` into `PPU_RenderTemp` |

**Status**: ⚠️ Partial (same as dot 336 -- AprNes skips the actual fetch)

#### Dot 339

| Category | AprNes | TriCNES |
|----------|--------|---------|
| NT commit | None | `PPU_NextCharacter = PPU_RenderTemp` |
| A12 notification | `NotifyMapperA12(ppuAddressBus)` | End-of-cycle detection at 338 |
| Sprite X counter init | `sprXCounter[i] = sprXPos[i]` for all 8 sprites | `PPU_SpriteShifterCounter[i] = PPU_SpriteXposition[i]` for all 8 sprites |
| MMC5 notification | `mmc5Ref.NotifyVramRead(...)` | N/A |

**Status**: ✅ Matched

**Notes**: Both initialize sprite X counters at dot 339. TriCNES wraps this in a rendering-enabled check; AprNes runs unconditionally (the function is only called when rendering is enabled).

#### Dot 340

| Category | AprNes | TriCNES |
|----------|--------|---------|
| PPU_AddressBus | `BgPatternTableAddr \| (NTVal << 4) \| fineY` (CHR pattern address) | Same formula |
| ppuChrFetchA12 | `(ppuAddressBus >> 12) & 1` | N/A |
| Data fetch | None | None (address set only) |

**Status**: ✅ Matched

**Notes**: Both set the PPU address bus to the CHR pattern address (using the NT value from the previous garbage fetch) without performing an actual read. This is important for A12 detection -- it places a potentially high A12 on the bus going into the idle/VBL period.

---

## 4. Key Timing Differences

### 4.1 PPU_AddressBus Update Timing

| Aspect | AprNes | TriCNES |
|--------|--------|---------|
| BG NT fetch (phase 1) | Immediate at phase 1 | Immediate at case 1 |
| BG AT fetch (phase 3) | Immediate at phase 3 | Immediate at case 3 |
| BG CHR low (phase 5) | Immediate at phase 5 | Immediate at case 5 |
| BG CHR high (phase 7) | Immediate at phase 7 | Immediate at case 7 |
| Even phases (0,2,4,6) | Address computed into `ioaddr` (bus not yet updated) | `break` (nothing) |

Both update `PPU_AddressBus` on the same dots. AprNes splits "compute address" and "set bus + fetch" across two consecutive phases; TriCNES does both in a single phase. The observable bus timing is identical.

### 4.2 A12 Notification Timing

| Aspect | AprNes | TriCNES |
|--------|--------|---------|
| Model | Explicit `NotifyMapperA12(addr)` calls at data-fetch phases | Implicit: `PPU_MapperSpecificFunctions()` called at end of every PPU cycle; compares `PPU_A12_Prev` vs current `PPU_AddressBus` bit 12 |
| When A12 is detected | Phase 1 (NT, A12=0), Phase 5 (CHR low, may be A12=1) | At the END of the PPU cycle where `PPU_AddressBus` was set |
| Sprite A12 | Phase 1 and 5 (1 dot after bus set at phases 0 and 4) | End-of-cycle detection (same effective timing) |
| Garbage NT A12 | Dots 337, 339 (1 dot after bus set at 336, 338) | End of dots 336, 338 |

**The +1 alignment in `NotifyMapperA12`**: AprNes passes `scanline * 341 + ppu_cycles_x + 1` as the ppuAbsCycle. The `+1` compensates for the fact that AprNes fires the notification during the rendering tick (before `ppu_cycles_x` is incremented), while TriCNES's `PPU_MapperSpecificFunctions()` runs after `PPU_Dot++`.

**Net result**: Both detect A12 rising edges at equivalent timing points.

### 4.3 ppuAbsCycle Calculation

```csharp
// AprNes (PPU.cs line 809):
MapperObj.NotifyA12(address, scanline * 341 + ppu_cycles_x + 1);
```

The `+1` offset ensures the notification timestamp matches TriCNES's end-of-cycle detection. Without it, the elapsed-time filter (`A12_FILTER = 16`) might incorrectly reject valid edges that are exactly 16 dots apart.

### 4.4 CXinc Timing (Deferred vs Immediate)

| Aspect | AprNes | TriCNES |
|--------|--------|---------|
| When CXinc fires | Next dot after phase 7 (via `commitCXinc` flag, checked at start of `ppu_step_rendering()`) | Next full step after case 7 (via `PPU_Commit_PatternHighFetch`, executed in `PPU_Render_CommitShiftRegistersAndBitPlanes()`) |
| Effective timing | Phase 0 of next tile (1 dot after CHR high fetch) | Beginning of next PPU cycle (1 dot after case 7) |

Both are equivalent: CXinc fires 1 dot after the CHR high bitplane is fetched, at the start of the next tile's 8-dot cycle.

### 4.5 Yinc Timing

| Aspect | AprNes | TriCNES |
|--------|--------|---------|
| When | `cx == 256`: `Yinc()` called directly | `PPU_Dot == 256`: `PPU_IncrementScrollY()` called in main loop (line 1503-1506) |
| In rendering tick? | Yes, inside `ppu_rendering_tick()` | No, in `_EmulatePPU()` after commit/fetch |

Both fire Yinc at dot 256, which is correct per NESdev documentation.

### 4.6 CopyHoriV Timing

| Aspect | AprNes | TriCNES |
|--------|--------|---------|
| When | `cx == 257`: `CopyHoriV()` | `PPU_Dot == 257`: `PPU_ResetXScroll()` |
| Location | Inside `ppu_rendering_tick()` | In `_EmulatePPU()` main loop (line 1507) |

Both fire the horizontal scroll copy at dot 257.

### 4.7 Shift Register Update at Half-Dot

| Aspect | AprNes | TriCNES |
|--------|--------|---------|
| Half-step function | `ppu_half_step()` | `_EmulateHalfPPU()` |
| Shift register shift | Per-dot in `ppu_step_rendering()`: `lowshift_s0 <<= 1; highshift_s0 <<= 1 \| 1` | `PPU_UpdateShiftRegisters()` at half-dot for dots 1-257 and 321-336 |
| Shift register reload | Phase 7 immediate | Half-dot case 7: `PPU_Commit_LoadShiftRegisters = true` (executed at next half-dot commit) |
| Attribute latch feed | Per-dot in half-step | `PPU_UpdateShiftRegisters()` shifts attribute from `PPU_AttributeLatchRegister` |

**Status**: ⚠️ Partial (different implementation structure, same observable behavior)

**Notes**: TriCNES shifts BG shift registers at every half-PPU-cycle (`_EmulateHalfPPU` calls `PPU_UpdateShiftRegisters()`). AprNes shifts the sprite-0 shadow registers per dot in the rendering step and uses a separate per-dot render pipeline. The pixel output should be equivalent for correctly rendered frames.

---

## 5. MMC3 A12 Detection Model

### AprNes Model

```
NotifyMapperA12(address, ppuAbsCycle)
  ├── Tracks lastA12 (0 or 1) and a12LowSince (cycle when A12 went low)
  ├── Rising edge (0→1): check if elapsed since a12LowSince >= A12_FILTER (16 dots)
  │   └── If yes: clock IRQ counter via Mapper04step_IRQ()
  └── Falling edge (1→0): record a12LowSince = current cycle
      └── Only if sinceLast < 341 (skip VBL gap resets)
```

- **A12_FILTER = 16**: The MMC3 A12 low-time filter is modeled as a minimum elapsed-time threshold (16 PPU dots).
- **Notification call sites** (during rendering):
  - BG Phase 1: NT address (A12=0)
  - BG Phase 5: CHR low address (A12 = BG pattern table bit 12)
  - BG Phase 7: CHR high address (MMC2/MMC4 only, not MMC3)
  - Sprite Phase 1: 1 dot after dummy NT (A12=0)
  - Sprite Phase 5: 1 dot after CHR address (A12 = sprite pattern table bit 12)
  - Sprite Phase 7: MMC2/MMC4 only
  - Garbage NT: dots 337, 339 (1 dot after bus set)
  - Dot 340: bus set to CHR addr (A12 may be 1)

### TriCNES Model

```
_EmulatePPU():
  ├── ... rendering/sprite evaluation/commit ...
  ├── PPU_MapperSpecificFunctions()  ←── calls Cart.MapperChip.PPUClock()
  ├── PPU_A12_Prev = (PPU_AddressBus & 0x1000) != 0  ←── snapshot for next cycle

Mapper_MMC3.PPUClock():
  ├── Check: !PPU_A12_Prev && (PPU_AddressBus & 0x1000) != 0  ←── 0→1 rising edge
  │   └── AND Mapper_4_M2Filter == 3  ←── A12 was low for >= 3 M2 rising edges
  │       └── Clock IRQ counter
  └── If A12 is high: reset Mapper_4_M2Filter = 0

Mapper_MMC3.CPUClockRise():
  ├── If A12 is low: increment Mapper_4_M2Filter (up to 3)
  └── Called at CPUClock == 5 (M2 rising edge)
```

- **M2 filter**: Instead of a PPU-dot-based elapsed time, TriCNES counts CPU clock rising edges while A12 is low. The filter value must reach 3 before a rising edge is recognized.
- **Detection point**: `PPU_A12_Prev` is sampled at the END of each PPU cycle. The rising edge is detected at the START of the NEXT cycle (comparing old vs new `PPU_AddressBus`).

### Comparison

| Aspect | AprNes | TriCNES |
|--------|--------|---------|
| A12 low-time model | Elapsed PPU dots >= 16 | M2 rising edges >= 3 while A12 low |
| Filter threshold | 16 PPU dots | 3 CPU cycles (~9 PPU dots at 3:1 ratio) |
| Detection timing | At explicit NotifyMapperA12 calls | End of every PPU cycle |
| Edge state tracking | `lastA12` (explicit 0/1) | `PPU_A12_Prev` (bool, sampled per cycle) |
| Continuous tracking | Only at notification points | Every PPU cycle (full bus visibility) |

**Status**: ⚠️ Partial -- Different filtering models

**Notes**:
- The AprNes `A12_FILTER = 16` threshold is a reasonable approximation of the real hardware's behavior, which involves the cartridge's physical A12 line being filtered by the MMC3's internal circuitry using M2 (CPU clock) edges.
- TriCNES's M2-filter model (`MMC3_M2Filter` counts CPU clock rises while A12 is low, threshold = 3) is closer to the actual hardware mechanism. On real MMC3 hardware, the chip uses M2 rising edges as a clock for its internal counter that filters A12 noise.
- Both models produce correct results for the vast majority of games. The difference would only matter for extremely precise A12 manipulation that games don't typically perform.
- A key practical difference: TriCNES checks A12 at EVERY PPU dot (via `PPU_MapperSpecificFunctions`), while AprNes only checks at specific fetch phases. This means TriCNES could detect A12 transitions caused by non-fetch bus activity (e.g., $2006/$2007 writes during rendering), while AprNes handles those via separate `NotifyMapperA12` calls in `Increment2007()` and the `$2006` delay handler.

---

## 6. Remaining Porting Gaps

### 6.1 Garbage NT Actual Fetch

AprNes sets the bus address at dots 336/338 but does not perform an actual VRAM read. TriCNES calls `FetchPPU()` which would trigger mapper-specific read callbacks. For most mappers this is irrelevant, but mappers that track read patterns (e.g., MMC5 scanline detection) may depend on it. AprNes compensates via explicit `mmc5Ref.NotifyVramRead()` calls at dots 337/339.

### 6.2 Sprite Secondary OAM Address Tracking

TriCNES carefully tracks `OAM2Address` throughout the sprite fetch phase (cases 0-7), incrementing it per sub-step. AprNes uses direct indexing (`slot * 4 + offset`) which is simpler but doesn't expose the address bus state. This could matter for edge cases involving mid-frame OAM address reads.

### 6.3 Sprite In-Range Check Location

TriCNES performs Y-range checks at cases 5 and 7 (CHR fetch phases) and clears shift registers for out-of-range sprites individually. AprNes performs the clear at phase 7 based on `sprSlotCount` from the evaluation phase. Both approaches should produce identical visible output, but TriCNES's model is more robust against edge cases where sprite evaluation results change mid-fetch.

### 6.4 Sprite Fetch BG Function Calls

During sprite evaluation (dots 257-320), TriCNES calls `PPU_Render_ShiftRegistersAndBitPlanes()` at cases 0-3. This means the BG fetch logic also runs during sprite fetch phases, setting `PPU_AddressBus` to BG-style addresses. AprNes does not call the BG fetch path during sprite phases; instead, it directly sets `ppuAddressBus` to the appropriate dummy/sprite addresses. The TriCNES approach may be more hardware-accurate (the real PPU reuses the same fetch circuitry), but the observable difference is minimal since no BG data is actually used from these fetches.

### 6.5 OAM Corruption Model

TriCNES implements detailed OAM corruption (`PPU_OAMCorruptionRenderingDisabledOutOfVBlank`, alignment-dependent `PPUClock` checks, `CorruptOAM()`, etc.) triggered by disabling rendering mid-frame. AprNes does not implement this OAM corruption model. This is a low-priority gap since no standard games rely on OAM corruption behavior.

### 6.6 Palette Corruption from V Register Change

TriCNES implements `PPU_PaletteCorruptionRenderingDisabledOutOfVBlank` and `PPU_VRegisterChangedOutOfVBlank` for palette corruption when the V register transitions from palette space ($3F00+) to non-palette space during visible rendering. AprNes has a partial implementation (`paletteCorruptFromVAddr` flag in the $2006 delay handler) but may not cover all edge cases.
