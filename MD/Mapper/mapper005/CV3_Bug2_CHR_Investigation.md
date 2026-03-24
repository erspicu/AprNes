# CV3 Bug 2: Wrong CHR Tiles on 2F — Investigation Status

**Date**: 2026-03-24
**Game**: 悪魔城伝説 (Castlevania III, Japanese MMC5 version)
**Bug**: After climbing stairs from 1F to 2F, CHR tiles are garbled (wrong HUD text, wrong gameplay tile patterns). Layout/scroll is correct, only tile graphics are from wrong CHR banks.
**Bug 1** (periodic background flickering on 1F): Already FIXED — replaced A12-based scanline counting with direct PPU scanline tracking in CpuCycle().

---

## Confirmed Facts (from debug log analysis)

### CV3 2F Frame Structure
```
NMI (sl=241):
  - NMI vector read → ppuInFrame=false, scanlineCounter=0
  - NMI log: B=[040,045,047,043] (gameplay CHR from prev frame's IRQ handler)

NMI Handler (sl=248-250):
  - W$5204=$80 → enable scanline IRQ
  - W$5203=46  → IRQ target = scanline 46
  - W$5129=041 → B[1] = HUD CHR page
  - W$512A=042 → B[2] = HUD CHR page

Rendering sl=0-45: HUD area
  - B-set = [040,041,042,043] (HUD CHR)

IRQ_MATCH at sl=46:
  - confirmed B=[040,041,042,043] at match point

IRQ Handler (sl=47):
  - W$5129=045 → B[1] = gameplay CHR page
  - W$512A=047 → B[2] = gameplay CHR page
  - W$5204=$00 → disable IRQ

Rendering sl=47-239: Gameplay area
  - B-set = [040,045,047,043] (gameplay CHR)
```

### Key Parameters (2F)
- `chrMode=3` (1KB × 8 banks)
- `8x16=True` (8×16 sprites → A/B set switching active)
- `extRM=0`, `extAttr=False` (no extended attributes)
- `nt=$44` (vertical mirroring: nt0=CIRAM-A, nt1=CIRAM-B)
- `lastChr=$512A` (B-set register → useA=false → BG uses B-set)
- `BgPatternTableAddr=0x1000` (confirmed from TILE log on US version)

### 1F Parameters (working)
- `nt=$55` (single-screen B)
- `B=[041,03E,03F,06F]`
- `irqT=0`, no scanline IRQ split

---

## Eliminated Theories

| Theory | Why Eliminated |
|--------|---------------|
| ppuInFrame oscillation between scanlines | ppuIdleCounter gap is only ~2 CPU cycles; dot 336 garbage NT fetch resets it |
| lastChrReg causing A-set for BG | lastChrReg=$512A consistently (B-set), never ≤$5127 |
| Extended attribute mode bug | extRM=0, extAttr=False in 2F |
| IRQ not firing | IRQ_MATCH confirmed at sl=46 every frame with correct B-set values |
| CHR bank calculation wrong | Verified mathematically: bank*1024 % chrRomSize correct |
| FillCHRBankPtrs B-set mirroring | chrMode=3 B-set: dst[i+4]=dst[i] matches Mesen2 |
| Nametable mirroring (CIRAMAddr) | V-mirror `addr & 0x27FF` correct; ntChrOverrideEnabled=false for nt=$44 |
| ApplyCHRBanksDynamic not called | Called on every CHR register write ($5120-$512B), confirmed by log |

---

## Remaining Investigation Leads

### 1. Per-tile CHR pointer verification (HIGHEST PRIORITY)
The register-level logs look correct, but we haven't verified what chrBankPtrs the PPU ACTUALLY uses during tile fetch at render time on the Japanese version's 2F. Need per-tile debug dump at sl=10 (HUD) and sl=50 (gameplay) during 2F frames.

### 2. ppuInFrame=true during VBlank
NotifyA12 is triggered by $2006 writes and $2007 reads during VBlank (NMI handler), which sets ppuInFrame=true. This shouldn't affect B-set selection for CV3 (lastChrReg=$512A > $5127 → useA=false regardless of ppuInFrame), but it's architecturally wrong.

### 3. Mesen2 per-tile UpdateChrBanks vs our dot-based switching
Mesen2 calls `UpdateChrBanks()` on every nametable fetch via `_splitTileNumber`. Our code switches at fixed dots (0, 257, 320). Should be equivalent but hasn't been exhaustively verified for edge cases.

### 4. BgPatternTableAddr during Japanese 2F
Confirmed $1000 on US version title screen. Not confirmed for Japanese 2F gameplay. If different, B-set mirroring (slots [0-3] = [4-7]) should make it irrelevant, but worth verifying.

---

## Architecture Notes (MMC5 CHR A/B Switching)

### Our Implementation (Mapper005.cs + PPU.cs)
```
Dot 0:   chrBankPtrs = Spritesize8x16 ? (chrBGUseASet ? A : B) : A
Dot 257: chrBankPtrs = chrBankPtrsA  (sprite fetch)
Dot 320: chrBankPtrs = Spritesize8x16 ? (chrBGUseASet ? A : B) : A  (BG prefetch)

Mid-frame CHR write → ApplyCHRBanksDynamic():
  1. UpdateCHRBankPtrsAB()  — fills A and B caches from chrBanks[]
  2. Determine useA: !ppuInFrame && lastChrReg <= $5127
  3. Copy selected set to chrBankPtrs[]
```

### Mesen2 (MMC5.h UpdateChrBanks)
```cpp
bool chrA = !largeSprites
         || (_splitTileNumber >= 32 && _splitTileNumber < 40)
         || (!_ppuInFrame && _lastChrReg <= 0x5127);
// Called on EVERY nametable fetch via MapperReadVram
```

### chrMode=3 B-set Bank Mapping
```
chrBanks[8]  ($5128) → chrBankPtrsB[0] and [4]  (mirrored)
chrBanks[9]  ($5129) → chrBankPtrsB[1] and [5]  (mirrored)
chrBanks[10] ($512A) → chrBankPtrsB[2] and [6]  (mirrored)
chrBanks[11] ($512B) → chrBankPtrsB[3] and [7]  (mirrored)
```

---

## Files Modified (debug session)

- `AprNes/NesCore/Mapper/Mapper005.cs` — debug logging infrastructure (dbgLog, dbgFrame, per-register write logs, APPLY dump, IRQ_MATCH dump)
- `AprNes/NesCore/PPU.cs` — per-tile CHR dump at phase 5 (TILE entries)
- `AprNes/NesCore/Main.cs` — ppuTileDbg/ppuTileDbgFrame/ppuTileDbgCHR static fields for PPU↔Mapper debug bridge

---

## Debug Log Format Reference

```
F{frame}: sl=241 8x16={} chrMode={} irqT={target} irqEn={} extRM={} extAttr={} lastChr=${reg} useA={} B=[B0,B1,B2,B3] A=[A0..A7] rendering={} nt=${mapping}
  W$5129={val} up={chrUpperBits} →bank={bank} F{frame} sl={scanline}
  IRQ_MATCH F{frame} sl={} cnt={} target={} enabled={} B=[...]
  APPLY F{frame} sl={} useA={} inFr={} lastR=${} B=[...] ptrs=[offset0..offset7]
    TILE F{frame} sl={} cx={dot} NTVal={tile} ioaddr={addr} slot={} ptrOff={offset} lowTile={byte} BgPTA={patternTableAddr}
```

## Next Steps
1. Rebuild without debug logging (clean state)
2. Re-add targeted debug logging when resuming investigation
3. Key experiment: dump per-tile CHR data during Japanese 2F rendering to compare actual vs expected
