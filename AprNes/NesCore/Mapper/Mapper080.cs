namespace AprNes
{
    // Taito X1-005 — Mapper 080
    // Registers at $7EF0-$7EFF (in the "RAM" region $6000-$7FFF).
    // PRG: 4×8KB banks ($8000/$A000/$C000 switchable, $E000 fixed last)
    // CHR: 2×2KB + 4×1KB banks
    // Mirror: $7EF6/$7EF7 bit0 (0=H, 1=V) unless alternateMirroring
    // RAM: 128 bytes at $7F00-$7FFF (enabled by $7EF8/$7EF9 == 0xA3)
    unsafe public class Mapper080 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int[] chrReg = new int[8]; // 0-1: 2KB banks, 2-7: 1KB banks (indices in reg array)
        int[] prgBank = new int[3]; // 8KB banks for $8000/$A000/$C000
        byte ramPermission;

        // 128-byte working RAM (mirrored)
        byte[] workRam = new byte[256]; // 256 = 2×128, mirrored as per Mesen2

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
            for (int i = 0; i < 8; i++) chrReg[i] = 0;
            for (int i = 0; i < 3; i++) prgBank[i] = 0;
            ramPermission = 0;
            for (int i = 0; i < workRam.Length; i++) workRam[i] = 0;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }

        public byte MapperR_RAM(ushort address)
        {
            // $7F00-$7FFF: 128-byte RAM (mirrored), only accessible when unlocked
            if (address >= 0x7F00 && ramPermission == 0xA3)
                return workRam[address & 0x7F];
            return NesCore.NES_MEM[address];
        }

        public void MapperW_RAM(ushort address, byte value)
        {
            // Registers at $7EF0-$7EFF
            if (address >= 0x7EF0 && address <= 0x7EFF)
            {
                WriteRegister(address, value);
                return;
            }
            // $7F00-$7FFF: 128-byte RAM (mirrored), only accessible when unlocked
            if (address >= 0x7F00 && ramPermission == 0xA3)
            {
                workRam[address & 0x7F] = value;
                workRam[(address & 0x7F) | 0x80] = value; // mirror
                return;
            }
            NesCore.NES_MEM[address] = value;
        }

        void WriteRegister(ushort addr, byte value)
        {
            switch (addr)
            {
                case 0x7EF0:
                    // 2KB CHR bank for $0000 (slots 0,1)
                    chrReg[0] = value;
                    UpdateCHRBanks();
                    break;
                case 0x7EF1:
                    // 2KB CHR bank for $0800 (slots 2,3)
                    chrReg[1] = value;
                    UpdateCHRBanks();
                    break;
                case 0x7EF2: chrReg[2] = value; UpdateCHRBanks(); break; // 1KB at $1000
                case 0x7EF3: chrReg[3] = value; UpdateCHRBanks(); break; // 1KB at $1400
                case 0x7EF4: chrReg[4] = value; UpdateCHRBanks(); break; // 1KB at $1800
                case 0x7EF5: chrReg[5] = value; UpdateCHRBanks(); break; // 1KB at $1C00

                case 0x7EF6:
                case 0x7EF7:
                    *Vertical = (value & 0x01) != 0 ? 1 : 0; // 1=V, 0=H
                    break;

                case 0x7EF8:
                case 0x7EF9:
                    ramPermission = value;
                    break;

                case 0x7EFA:
                case 0x7EFB:
                    prgBank[0] = value;
                    break;
                case 0x7EFC:
                case 0x7EFD:
                    prgBank[1] = value;
                    break;
                case 0x7EFE:
                case 0x7EFF:
                    prgBank[2] = value;
                    break;
            }
        }

        // Mapper080 has no $8000+ registers
        public void MapperW_PRG(ushort address, byte value) { }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + i * 1024;
                return;
            }
            int n1k = CHR_ROM_count * 8;
            int n2k_pages = CHR_ROM_count * 4;

            // chrReg[0]: 2KB bank at $0000 (slots 0,1)
            int b0 = (chrReg[0] & 0xFE) % n1k;
            NesCore.chrBankPtrs[0] = CHR_ROM + (b0 << 10);
            NesCore.chrBankPtrs[1] = CHR_ROM + ((b0 + 1) << 10);

            // chrReg[1]: 2KB bank at $0800 (slots 2,3)
            int b1 = (chrReg[1] & 0xFE) % n1k;
            NesCore.chrBankPtrs[2] = CHR_ROM + (b1 << 10);
            NesCore.chrBankPtrs[3] = CHR_ROM + ((b1 + 1) << 10);

            // chrReg[2-5]: 1KB banks at $1000-$1C00 (slots 4-7)
            NesCore.chrBankPtrs[4] = CHR_ROM + ((chrReg[2] % n1k) << 10);
            NesCore.chrBankPtrs[5] = CHR_ROM + ((chrReg[3] % n1k) << 10);
            NesCore.chrBankPtrs[6] = CHR_ROM + ((chrReg[4] % n1k) << 10);
            NesCore.chrBankPtrs[7] = CHR_ROM + ((chrReg[5] % n1k) << 10);
        }

        public byte MapperR_RPG(ushort address)
        {
            int n8k = PRG_ROM_count * 2;
            if (address < 0xA000) return PRG_ROM[(address - 0x8000) + ((prgBank[0] % n8k) << 13)];
            if (address < 0xC000) return PRG_ROM[(address - 0xA000) + ((prgBank[1] % n8k) << 13)];
            if (address < 0xE000) return PRG_ROM[(address - 0xC000) + ((prgBank[2] % n8k) << 13)];
            return PRG_ROM[(address - 0xE000) + ((n8k - 1) << 13)];
        }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
            public void Cleanup() { }
}
}
