# 03 iNES ROM Loading and Header Parsing

## What This Chapter Solves

A `.nes` file isn't a single blob you can drop into CPU memory. It contains a header, the PRG ROM, and the CHR ROM, with the header telling the emulator which mapper, how many ROM banks, and which mirroring the cartridge uses.

This chapter covers how to turn a `.nes` file into the PRG, CHR, and mapper state AprNes needs.

## NES Hardware Concepts

A NES cartridge typically holds two main kinds of data:

- PRG ROM: code and data the CPU executes.
- CHR ROM or CHR RAM: tile patterns the PPU reads.

**Everyday analogy**: think of a NES cartridge as a menu binder containing two kinds of pages:
- **PRG** (recipes): step-by-step instructions executed by the chef (CPU).
- **CHR** (photos): pictures of dishes, used by the wait staff (PPU).

The chef never looks at the photos, and the wait staff never reads the recipes. **The two work on separate conveyor belts (CPU bus and PPU bus) and can run in parallel**. That parallel-bus design is the key reason the NES could deliver 60 fps action games as far back as 1983.

iNES file layout:

```text
+----------------+
| 16-byte header |  ← describes the cartridge "shape" to the emulator
+----------------+
| trainer (512B) |  ← rare; legacy dump artifact, present only when header bit 2 = 1
+----------------+
| PRG ROM        |  ← size = header byte 4 × 16 KB
| (program)      |
+----------------+
| CHR ROM        |  ← size = header byte 5 × 8 KB
| (graphics)     |     byte 5 = 0 means the cartridge uses CHR RAM (8 KB)
+----------------+
```

**iNES (defined by Marc Brouwerd in 1996) vs NES 2.0**: iNES is the most common ROM format; NES 2.0 is an extended version that can describe more mappers / submappers / region info. An emulator at minimum needs iNES to load the bulk of ROM sets; NES 2.0 is an advanced goal.

Important header fields:

```text
byte 0-3  magic: "NES" + 0x1A
byte 4    PRG ROM count, unit = 16 KB
byte 5    CHR ROM count, unit = 8 KB
byte 6    mirroring, battery, trainer, mapper low nibble
byte 7    mapper high nibble, NES 2.0 marker
byte 8    PRG RAM size or NES 2.0 extension field
```

When CHR ROM count is 0, the cartridge typically uses CHR RAM. In that case the PPU pattern table data is not from ROM but written by the running game.

## Beginner-Friendly Simplification

A minimal ROM loader can do:

1. Verify the first four bytes are `NES\x1A`.
2. Read PRG bank count.
3. Read CHR bank count.
4. Compute the mapper number.
5. Allocate PRG ROM.
6. If only one PRG bank, mirror it to fill 32 KB.
7. If CHR bank count is non-zero, allocate CHR ROM.
8. Create a mapper based on the mapper number.

The simplest version supports only mapper 0; add 1, 2, 3, 4 later.

## AprNes / NesCore Implementation Mapping

AprNes loads ROMs in `Main.cs init(byte[] rom_bytes)`.

Main flow:

```text
verify magic number
read PRG_ROM_count / CHR_ROM_count
allocate PRG_ROM / CHR_ROM
parse ROM_Control_1 / ROM_Control_2
determine mirroring / battery / trainer / four-screen
compute mapper number
look up RomDatabase to fix up special ROMs
MapperRegistry.Create(...)
MapperObj.MapperInit(...)
MapperObj.Reset()
MapperObj.UpdateCHRBanks()
allocate CPU RAM / PPU RAM / OAM / palette / audio buffer
initialise CPU / PPU / APU / dispatch table
```

AprNes-specific touches:

- 16 KB PRG ROM is copied into the upper 16 KB so that all of `$8000-$FFFF` has data.
- CHR ROM count is clamped to actual file length, avoiding out-of-bounds from broken headers.
- `RomDatabase` uses PRG+CHR CRC32 to correct ROMs with bad headers.
- `MapperRegistry` creates the right mapper instance based on mapper id and submapper.

## Important Code Concepts

### PRG ROM mirroring

NROM-128 has only 16 KB of PRG, but the CPU cartridge window is 32 KB:

```text
$8000-$BFFF  PRG bank 0
$C000-$FFFF  mirror of PRG bank 0
```

**Why is mirroring necessary?** Because the CPU's reset / NMI / IRQ vectors all live in `$FFFA`–`$FFFF`. If a 16 KB ROM only occupies `$8000-$BFFF`, the CPU jumps into a blank `$C000+` area on power-up and never starts. Mirroring places the 16 KB content **in both halves**, so the boot code can find the reset vector at `$C000+`.

**Everyday analogy**: a restaurant menu fits on one side (16 pages), but the menu binder has two faces (32 pages). To ensure customers see content no matter which face they flip, the owner inserts two identical printed sheets back-to-back.

AprNes copies the 16 KB into the second 16 KB at load time, so mapper code can use a simple offset:

```csharp
if (PRG_ROM_count == 1) {
    // 16 KB ROM → copy to upper 16 KB
    Buffer.MemoryCopy(PRG_ROM, PRG_ROM + 0x4000, 0x4000, 0x4000);
}
```

That way `MapperR_RPG` in the mapper can simply `return PRG_ROM[addr - 0x8000]` without checking for mirroring each time.

### Mapper number

The mapper number is built from the high nibble of header bytes 6 and 7:

```text
mapper = (flag6 >> 4) | (flag7 & 0xF0)
```

NES 2.0 headers have additional checks. AprNes also handles some old-style mapper info.

### Mirroring

Header bit 0 selects vertical or horizontal mirroring. Bit 3 indicates four-screen.

AprNes stores the mode in `Vertical`:

- `0`: horizontal.
- `1`: vertical.
- `2` / `3`: one-screen lower / upper.
- `4`: four-screen.

## Common Mistakes

- Forgetting the 16 KB PRG mirror, so the reset vector returns garbage.
- Treating CHR count 0 as "no graphics data" instead of providing CHR RAM.
- Ignoring the trainer offset, misaligning the PRG/CHR start.
- Trusting the header completely, never accounting for mismatched mappers or odd ROMs.
- Letting the PPU read CHR bank pointers before the mapper is initialised.

## Chapter Recap

1. A `.nes` file must be parsed via the header — it is not raw CPU memory.
2. PRG is CPU program data; CHR is PPU pattern data.
3. The mapper number bridges the ROM loader and the rest of the bus mapping.

## Bridge to the Next Chapter

The next chapter covers the 64 KB memory map the CPU sees, and how AprNes routes addresses to RAM, PPU, APU, JoyPad, and the mapper.
