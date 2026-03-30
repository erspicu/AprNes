namespace AprNes
{
    // Per-ROM override entry. Only games that require special handling are listed.
    // General fixes (CHR bank clamping, PRG wrapping) stay in code logic and do NOT belong here.
    struct RomDbEntry
    {
        public static readonly RomDbEntry None = default;

        public uint   Crc;            // PRG+CHR CRC32 (Mesen2 style: skip 16-byte iNES header)
        public string Name;           // Human-readable identifier (doc/debug only)
        public int    MapperOverride; // -1 = no override; >= 0 = force this mapper ID (iNES header wrong)
        public int    Submapper;      // -1 = default; mapper-specific sub-variant ID
        public int    MirrorOverride; // -1 = no override; 0=H, 1=V, 2=single-A, 3=single-B

        public bool IsNone => Name == null;
    }

    static class RomDatabase
    {
        // -----------------------------------------------------------------------
        // Entries: add one line per special-case ROM.
        // Submapper semantics per mapper:
        //   Mapper 4  (MMC3):  1=Rev A,  2=MMC6
        //   Mapper 32 (G-101): 1=Major League (lock PRG mode 0 + single-A mirror)
        //   Mapper 78 (Irem):  3=Holy Diver (V/H mirroring)
        //   Mapper 16 (Bandai):4=FCG-1/2 ($6000 regs, direct counter)
        //                       5=LZ93D50 ($8000 regs, latch IRQ)
        //   Mapper 25 (VRC4b/d): 1=VRC4b (A0/A1 standard)
        //   Note: some iNES ROMs claim mapper 16 but are actually mapper 159 (LZ93D50+24C01);
        //         use Submapper=5 to force LZ93D50 behaviour for these misidentified ROMs.
        //   Note: MapperOverride >= 0 replaces the iNES header mapper entirely.
        // -----------------------------------------------------------------------
        static readonly RomDbEntry[] Table =
        {
            // --- Mapper 4 (MMC3) sub-variants ---
            new RomDbEntry { Crc = 0xF312D1DE, Name = "[blargg] mmc3_irq_accuracy/5.MMC3_rev_A",  MapperOverride = -1, Submapper = 1, MirrorOverride = -1 },
            new RomDbEntry { Crc = 0x633AFE6F, Name = "[blargg] mmc3_irq_accuracy/6-MMC3_alt",    MapperOverride = -1, Submapper = 1, MirrorOverride = -1 },
            new RomDbEntry { Crc = 0xA512BDF6, Name = "[blargg] mmc3_irq_accuracy/6-MMC6",        MapperOverride = -1, Submapper = 2, MirrorOverride = -1 },

            // --- Mapper 32 (Irem G-101) sub-variants ---
            new RomDbEntry { Crc = 0x243A8735, Name = "Major League (J)",                         MapperOverride = -1, Submapper = 1, MirrorOverride = -1 },

            // --- Mapper 78 (Irem 74HC161/32) sub-variants ---
            new RomDbEntry { Crc = 0xBA51AC6F, Name = "Holy Diver (J)",                           MapperOverride = -1, Submapper = 3, MirrorOverride = -1 },

            // --- Mapper 16 (Bandai FCG) sub-variants ---
            // FCG-1/2 games: registers at $6000-$7FFF, direct IRQ counter (no latch)
            new RomDbEntry { Crc = 0x33B899C9, Name = "Dragon Ball - Dai Maou Fukkatsu (J)",       MapperOverride = -1, Submapper = 4, MirrorOverride = -1 },
            new RomDbEntry { Crc = 0x6E68E31A, Name = "Dragon Ball 3 - Gokuu Den (J)",             MapperOverride = -1, Submapper = 4, MirrorOverride = -1 },
            new RomDbEntry { Crc = 0xD343C66A, Name = "Famicom Jump - Eiyuu Retsuden (J)",         MapperOverride = -1, Submapper = 4, MirrorOverride = -1 },
            // LZ93D50+24C01 games stored as mapper 16 in old iNES (should be mapper 159)
            // Use Submapper=5 to force LZ93D50 behaviour ($8000 regs, latch IRQ)
            new RomDbEntry { Crc = 0x183859D2, Name = "Dragon Ball Z - Kyoushuu Saiya Jin (J)",    MapperOverride = -1, Submapper = 5, MirrorOverride = -1 },
            new RomDbEntry { Crc = 0xDCB972CE, Name = "Magical Taruruuto-kun (J)",                 MapperOverride = -1, Submapper = 5, MirrorOverride = -1 },

            // --- Wrong mapper in iNES header (DB overrides mapper ID) ---
            // Gradius II (J) (VC): header says mapper 9 (MMC2), actually VRC4b/d (mapper 25 sub 1)
            new RomDbEntry { Crc = 0x7F7AB2E2, Name = "Gradius II (J) (VC)",                       MapperOverride = 25, Submapper = 1, MirrorOverride = 0 },
            // Glider Expansion: header says mapper 13 (CPROM), actually mapper 29 (Sealie Computing)
            // Note: The House / Plato's Bath House are bad dumps (16KB PRG, green screen even on Mesen2)
            new RomDbEntry { Crc = 0x98D60CF2, Name = "Glider Expansion - Mad House (PD)",         MapperOverride = 29, Submapper = -1, MirrorOverride = -1 },
            // Mortal Kombat 2 (Unl): header says mapper 210, actually mapper 90 (JY Company)
            new RomDbEntry { Crc = 0x60BFEB0C, Name = "Mortal Kombat 2 (Unl)",                     MapperOverride = 90, Submapper = -1, MirrorOverride = -1 },
            // Devil Man (J): header says mapper 88 + fourscreen, actually mapper 154 (Namco 129) + horizontal
            new RomDbEntry { Crc = 0xD1691028, Name = "Devil Man (J)",                              MapperOverride = 154, Submapper = -1, MirrorOverride = 0 },
        };

        public static RomDbEntry Lookup(uint crc)
        {
            for (int i = 0; i < Table.Length; i++)
                if (Table[i].Crc == crc) return Table[i];
            return RomDbEntry.None;
        }
    }
}
