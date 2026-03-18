namespace AprNes
{
    // Per-ROM override entry. Only games that require special handling are listed.
    // General fixes (CHR bank clamping, PRG wrapping) stay in code logic and do NOT belong here.
    struct RomDbEntry
    {
        public static readonly RomDbEntry None = default;

        public uint   Crc;            // PRG+CHR CRC32 (Mesen2 style: skip 16-byte iNES header)
        public string Name;           // Human-readable identifier (doc/debug only)
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
        // -----------------------------------------------------------------------
        static readonly RomDbEntry[] Table =
        {
            // --- Mapper 4 (MMC3) sub-variants ---
            // blargg mmc3_irq_accuracy test ROMs; actual game ROMs needing Rev A / MMC6
            // should be added here when discovered.
            new RomDbEntry { Crc = 0xF312D1DE, Name = "[blargg] mmc3_irq_accuracy/5.MMC3_rev_A",  Submapper = 1, MirrorOverride = -1 },
            new RomDbEntry { Crc = 0x633AFE6F, Name = "[blargg] mmc3_irq_accuracy/6-MMC3_alt",    Submapper = 1, MirrorOverride = -1 },
            new RomDbEntry { Crc = 0xA512BDF6, Name = "[blargg] mmc3_irq_accuracy/6-MMC6",        Submapper = 2, MirrorOverride = -1 },

            // --- Mapper 32 (Irem G-101) sub-variants ---
            new RomDbEntry { Crc = 0x243A8735, Name = "Major League (J)",                         Submapper = 1, MirrorOverride = -1 },

            // --- Mapper 78 (Irem 74HC161/32) sub-variants ---
            new RomDbEntry { Crc = 0xBA51AC6F, Name = "Holy Diver (J)",                           Submapper = 3, MirrorOverride = -1 },
        };

        public static RomDbEntry Lookup(uint crc)
        {
            for (int i = 0; i < Table.Length; i++)
                if (Table[i].Crc == crc) return Table[i];
            return RomDbEntry.None;
        }
    }
}
