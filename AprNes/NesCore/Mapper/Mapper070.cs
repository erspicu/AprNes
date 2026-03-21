namespace AprNes
{
    // Bandai 74161/32 — Mapper 070 and Mapper 152
    // Write to $8000-$FFFF:
    //   bits[7:4] = PRG 16KB bank at $8000-$BFFF
    //   bits[3:0] = CHR 8KB bank at PPU $0000-$1FFF
    //   $C000-$FFFF fixed to last 16KB
    //
    // Mapper 070: mirroring fixed from header (no register control)
    // Mapper 152: bit6 of write value controls single-screen mirroring
    //   bit6=0 → Screen A only ($2000), bit6=1 → Screen B only ($2400)
    //
    // Mesen2 Bandai74161_7432 constructor takes enableMirroringControl:
    //   Mapper070 = false (fixed mirroring), Mapper152 = true (bit6 controls mirroring)
    // Additionally, Mesen2 uses a heuristic: if any game sets bit6=1, assume mirroring control.
    // We implement Mapper152 with explicit mirroring control flag.
    unsafe public class Mapper070 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int prgBank;  // 16KB bank for $8000-$BFFF
        int chrBank;  // 8KB bank for CHR

        // For Mapper152: mirroring control enabled
        protected bool enableMirroringControl;
        // Track if mirroring control has been activated
        private bool mirroringControlActivated;

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void CpuCycle() { }

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
        }

        public void Reset()
        {
            prgBank = 0; chrBank = 0;
            mirroringControlActivated = enableMirroringControl;
            // Mesen2 hack: force Vertical mirroring initially for Kamen Rider Club (bad header)
            if (!enableMirroringControl)
                *Vertical = 1;  // Vertical
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void MapperW_PRG(ushort address, byte value)
        {
            bool mirrorBit = (value & 0x80) != 0;

            // Mesen2 heuristic: if any write has bit7=1, enable mirroring control
            if (mirrorBit) mirroringControlActivated = true;

            if (mirroringControlActivated)
            {
                // bit6: 0=ScreenA, 1=ScreenB
                // Use *Vertical >= 2 for single-screen (MEM.cs handles writes; PPU reads from ppu_ram directly)
                // *Vertical = 2: single-screen-A (all writes to page 0 = $2000)
                // *Vertical = 3: single-screen-B (all writes to page 1 = $2400)
                *Vertical = ((value & 0x40) != 0) ? 3 : 2;
                NesCore.ntChrOverrideEnabled = false;
            }

            prgBank = (value >> 4) & 0x07;
            chrBank = value & 0x0F;
            UpdateCHRBanks();
        }

        public byte MapperR_RPG(ushort address)
        {
            int total16k = PRG_ROM_count;  // PRG_ROM_count is already number of 16KB pages
            if (address < 0xC000) return PRG_ROM[(address - 0x8000) + ((prgBank % total16k) << 14)];
            // $C000-$FFFF fixed to last 16KB
            return PRG_ROM[(address - 0xC000) + ((total16k - 1) << 14)];
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + i * 1024;
                return;
            }
            int total8k = CHR_ROM_count;
            byte* b = CHR_ROM + ((chrBank % total8k) << 13);
            for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = b + i * 1024;
        }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
    }

    // Mapper 152 = Bandai 74161/32 with mirroring control
    unsafe public class Mapper152 : Mapper070
    {
        public Mapper152()
        {
            enableMirroringControl = true;
        }
    }
}
