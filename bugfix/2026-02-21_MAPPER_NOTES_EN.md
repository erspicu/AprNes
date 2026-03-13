# NES Mapper Sub-Variant Identification Notes

Compiled from the 2026-02-21 development session, for reference when extending mapper support in the future.

---

## Core Issue

The same iNES mapper number may correspond to multiple different hardware variants with behavioral differences. The iNES 1.0 header **cannot distinguish** these sub-variants. NES 2.0 added a submapper field, but most ROM dumps remain in iNES 1.0 format.

---

## MMC3 (Mapper 004) Sub-Variants

### Rev A vs Rev B

| Item | Rev A (early) | Rev B (later, more common) |
|------|---------------|----------------------------|
| When counter==0 | reload from latch | reload from latch |
| When reload flag is set | marks "reload from latch on next zero" | **immediately** reload from latch |
| Common games | Star Wars, very few | vast majority of MMC3 games |

**AprNes current**: implements Rev B behavior. `mmc3_irq_tests/5.MMC3_rev_A` expected to FAIL.

### MMC6

- Shares mapper 004 with MMC3, but different chip
- Additional feature: PRG-RAM has per-1KB bank read/write protection mechanism
- Very few games use it (StarTropics series, etc.)
- `mmc3_test/6-MMC6` expected to FAIL

### Detection Method

- **Filename unreliable**: "Rev A" in filenames refers to the game software revision, not the chip revision
- **Header insufficient**: iNES 1.0 only contains the mapper number, no sub-type
- **Only reliable method**: CRC32/SHA1 lookup table (NesCartDB, No-Intro DAT)

---

## Other Mappers with Sub-Variant Issues

### Critical (Affects Game Correctness)

| Mapper | Name | Variants | Issue Description |
|--------|------|----------|-------------------|
| 021/023/025 | VRC2/VRC4 | 7 | VRC2a, VRC2b, VRC4a–VRC4e. Differences in address line A0/A1 wiring. NES 2.0 resolves with submapper |
| 024/026 | VRC6 | 2 | VRC6a vs VRC6b, A0/A1 swapped |
| 085 | VRC7 | 2 | Similar to VRC6, address line swap |
| 016 | Bandai FCG | 4 | FCG-1/2, LZ93D50, LZ93D50+EEPROM, 24C02. Later split into 016/153/157/159, but old dumps still labeled 016 |
| 019 | Namco 163/175/340 | 3 | Large functional differences (175/340 no expansion audio, different RAM layout) |

### Moderate (Specific Features Affected)

| Mapper | Name | Issue Description |
|--------|------|-------------------|
| 001 | MMC1 | SNROM/SOROM/SUROM/SXROM board variants; PRG-RAM size and banking differ |
| 069 | Sunsoft FME-7/5B | Same mapper 069, but Sunsoft 5B adds 3 expansion audio channels |

### Currently Implemented Mappers in AprNes

| Mapper | Name | Sub-Variant Risk |
|--------|------|-----------------|
| 000 | NROM | None |
| 001 | MMC1 | Low (board variant affects PRG-RAM) |
| 002 | UxROM | None |
| 003 | CNROM | None |
| **004** | **MMC3** | **Yes (Rev A/B, MMC6)** |
| 005 | MMC5 | Stub, not fully implemented |
| 007 | AxROM | None |
| 011 | Color Dreams | None |
| 066 | GxROM | None |
| 071 | Camerica | None |

---

## Suggested CRC32 Lookup Mechanism (Future Implementation)

Minimal cost approach:

1. Compute CRC32 when ROM loads (data already in memory, nearly zero cost)
2. Use `Dictionary<uint, MapperSubType>` to look up known special ROMs
3. Lookup result affects mapper initialization parameters (e.g., Rev A/B flag)

Known MMC3 Rev A games are very few (approximately 3–5 titles); hard-coding suffices:

```
Game                           CRC32       Sub-type
Star Wars (USA)               xxxxxxxx    MMC3 Rev A
Startropics (USA)             xxxxxxxx    MMC6
Startropics II (USA)          xxxxxxxx    MMC6
```

> Exact CRC32 values need to be confirmed from NesCartDB or No-Intro DAT.

---

*Last updated: 2026-02-21*
