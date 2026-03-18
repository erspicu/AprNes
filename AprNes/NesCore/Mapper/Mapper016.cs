namespace AprNes
{
    // Bandai FCG / LZ93D50 — Mapper 016
    // Games: Dragon Ball series, DBZ series, Famicom Jump, Magical Taruruuto-kun, etc.
    //
    // PRG: 1×16K switchable at $8000-$BFFF + fixed last 16K at $C000-$FFFF
    //   Register 0x08 (low 4 bits): PRG bank select
    //
    // CHR: 8×1K banks
    //   Registers 0x00-0x07 (full 8-bit): CHR bank select
    //
    // Mirror: Register 0x09 bits[1:0] — 0=V, 1=H, 2=single-A, 3=single-B
    //
    // IRQ: 16-bit CPU-cycle down-counter
    //   Register 0x0A: enable/disable + clear IRQ; on non-FCG also copies latch→counter
    //   Register 0x0B: low byte of counter (FCG-1/2) or latch (LZ93D50/heuristic)
    //   Register 0x0C: high byte of counter (FCG-1/2) or latch (LZ93D50/heuristic)
    //   Counter fires IRQ when it hits 0 (checked BEFORE decrement each cycle)
    //
    // Submapper 4 = FCG-1/2: registers at $6000-$7FFF, no latch (counter written directly)
    // Submapper 5 = LZ93D50: registers at $8000-$FFFF, uses latch ($0A copies latch→counter)
    // Submapper 0 = heuristic: both ranges active (handles both FCG and LZ93D50 games)
    //
    // EEPROM (24C01/24C02): not implemented — save games won't persist, gameplay works.

    unsafe public class Mapper016 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        byte   prgBank;
        byte[] chrBanks = new byte[8];

        // IRQ
        ushort irqCounter;
        ushort irqLatch;
        bool   irqEnabled;
        bool   useLatch;   // true for LZ93D50/heuristic; false for FCG-1/2 (submapper 4)

        public int Submapper;

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
        }

        public void Reset()
        {
            prgBank = 0;
            for (int i = 0; i < 8; i++) chrBanks[i] = 0;
            irqCounter = irqLatch = 0;
            irqEnabled = false;
            // Submapper 4 = FCG-1/2: direct counter, no latch
            useLatch = (Submapper != 4);
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return 0; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }

        // $6000-$7FFF: active for submapper 0 (heuristic) and submapper 4 (FCG-1/2)
        public byte MapperR_RAM(ushort address)
        {
            // LZ93D50: bit4 = EEPROM SDA data-out.
            // Return NES_MEM value as-is; default memory content (0x00) makes bit4=0
            // which acts as a permanent ACK, allowing the game to complete EEPROM init
            // with all-zero (blank/default) save data instead of hanging on NACK.
            return NesCore.NES_MEM[address];
        }
        public void MapperW_RAM(ushort address, byte value)
        {
            // Submapper 5 (LZ93D50): registers only at $8000-$FFFF — ignore $6000 writes
            if (Submapper == 5) return;
            WriteReg(address & 0x000F, value);
        }

        // $8000-$FFFF: active for submapper 0 (heuristic) and submapper 5 (LZ93D50)
        public void MapperW_PRG(ushort address, byte value)
        {
            // Submapper 4 (FCG-1/2): registers only at $6000-$7FFF — ignore $8000 writes
            if (Submapper == 4) return;
            WriteReg(address & 0x000F, value);
        }

        void WriteReg(int reg, byte value)
        {
            switch (reg)
            {
                // CHR banks 0-7 (full 8-bit)
                case 0: case 1: case 2: case 3:
                case 4: case 5: case 6: case 7:
                    chrBanks[reg] = value;
                    UpdateCHRBanks();
                    break;

                // PRG bank (low 4 bits)
                case 0x8:
                    prgBank = (byte)(value & 0x0F);
                    break;

                // Mirroring: 0=V, 1=H, 2=single-A, 3=single-B
                // AprNes: *Vertical=0=H, 1=V, ≥2=one-screen
                case 0x9:
                    switch (value & 0x03)
                    {
                        case 0: *Vertical = 1; break;   // Vertical
                        case 1: *Vertical = 0; break;   // Horizontal
                        case 2: *Vertical = 2; break;   // Single-screen A
                        case 3: *Vertical = 3; break;   // Single-screen B
                    }
                    break;

                // IRQ control: enable/disable + clear; on LZ93D50/heuristic also reload
                case 0xA:
                    irqEnabled = (value & 0x01) != 0;
                    if (useLatch)
                        irqCounter = irqLatch;
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    break;

                // IRQ low byte
                case 0xB:
                    if (useLatch) irqLatch   = (ushort)((irqLatch   & 0xFF00) | value);
                    else          irqCounter = (ushort)((irqCounter & 0xFF00) | value);
                    break;

                // IRQ high byte
                case 0xC:
                    if (useLatch) irqLatch   = (ushort)((irqLatch   & 0x00FF) | (value << 8));
                    else          irqCounter = (ushort)((irqCounter & 0x00FF) | (value << 8));
                    break;

                // 0xD: EEPROM control (not implemented)
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            int n = PRG_ROM_count;  // total 16K banks
            int bank = address < 0xC000 ? prgBank % n : n - 1;
            return PRG_ROM[(address & 0x3FFF) + (bank << 14)];
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
                return;
            }
            int total1k = CHR_ROM_count * 8;
            for (int i = 0; i < 8; i++)
                NesCore.chrBankPtrs[i] = CHR_ROM + ((chrBanks[i] % total1k) << 10);
        }

        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }
        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }

        public void CpuCycle()
        {
            if (!irqEnabled) return;
            // Fire IRQ when counter reaches 0 (check BEFORE decrement — Mesen2 behaviour)
            if (irqCounter == 0)
            {
                NesCore.statusmapperint = true;
                NesCore.UpdateIRQLine();
            }
            irqCounter--;
        }

        public void NotifyA12(int addr, int ppuAbsCycle) { }
    }
}
