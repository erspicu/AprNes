namespace AprNes
{
    // Taito X1-017 — Mapper 082
    // Registers at $7EF0-$7EFF (same region as Mapper080).
    // PRG: 3×8KB switchable ($8000/$A000/$C000), fixed last 8KB at $E000
    // CHR: 6×1KB banks; mode bit determines layout
    // RAM: 5×1KB at $6000-$73FF, protected by $7EF7/$7EF8/$7EF9 unlock bytes
    unsafe public class Mapper082 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        byte[] chrReg = new byte[6];      // 6 CHR bank regs
        int[] prgBank = new int[3];       // 3 PRG bank regs
        byte[] ramPermission = new byte[3]; // permission regs
        int chrMode;                       // 0 or 1

        // 5KB save RAM (5×1KB pages at $6000-$73FF)
        byte[] saveRam = new byte[5 * 1024];

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
            for (int i = 0; i < 6; i++) chrReg[i] = 0;
            for (int i = 0; i < 3; i++) { prgBank[i] = 0; ramPermission[i] = 0; }
            chrMode = 0;
            for (int i = 0; i < saveRam.Length; i++) saveRam[i] = 0;
            // Initialize PRG banks to first valid banks
            for (int i = 0; i < 3; i++) prgBank[i] = i;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }

        public byte MapperR_RAM(ushort address)
        {
            // $6000-$63FF: page 0 (unlocked by ramPermission[0]==0xCA)
            if (address < 0x6400) { if (ramPermission[0] == 0xCA) return saveRam[address - 0x6000]; return 0; }
            // $6400-$67FF: page 1 (unlocked by ramPermission[0]==0xCA)
            if (address < 0x6800) { if (ramPermission[0] == 0xCA) return saveRam[0x400 + (address - 0x6400)]; return 0; }
            // $6800-$6BFF: page 2 (unlocked by ramPermission[1]==0x69)
            if (address < 0x6C00) { if (ramPermission[1] == 0x69) return saveRam[0x800 + (address - 0x6800)]; return 0; }
            // $6C00-$6FFF: page 3 (unlocked by ramPermission[1]==0x69)
            if (address < 0x7000) { if (ramPermission[1] == 0x69) return saveRam[0xC00 + (address - 0x6C00)]; return 0; }
            // $7000-$73FF: page 4 (unlocked by ramPermission[2]==0x84)
            if (address < 0x7400) { if (ramPermission[2] == 0x84) return saveRam[0x1000 + (address - 0x7000)]; return 0; }
            // $7EF0-$7EFF: registers (read open bus)
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
            // RAM writes
            if (address < 0x6400) { if (ramPermission[0] == 0xCA) saveRam[address - 0x6000] = value; return; }
            if (address < 0x6800) { if (ramPermission[0] == 0xCA) saveRam[0x400 + (address - 0x6400)] = value; return; }
            if (address < 0x6C00) { if (ramPermission[1] == 0x69) saveRam[0x800 + (address - 0x6800)] = value; return; }
            if (address < 0x7000) { if (ramPermission[1] == 0x69) saveRam[0xC00 + (address - 0x6C00)] = value; return; }
            if (address < 0x7400) { if (ramPermission[2] == 0x84) saveRam[0x1000 + (address - 0x7000)] = value; return; }
            NesCore.NES_MEM[address] = value;
        }

        void WriteRegister(ushort addr, byte value)
        {
            switch (addr)
            {
                case 0x7EF0: case 0x7EF1: case 0x7EF2:
                case 0x7EF3: case 0x7EF4: case 0x7EF5:
                    chrReg[addr & 0x0F] = value;
                    UpdateCHRBanks();
                    break;

                case 0x7EF6:
                    *Vertical = (value & 0x01) != 0 ? 0 : 1; // 1=V, 0=H
                    chrMode = (value >> 1) & 0x01;
                    UpdateCHRBanks();
                    break;

                case 0x7EF7: ramPermission[0] = value; break;
                case 0x7EF8: ramPermission[1] = value; break;
                case 0x7EF9: ramPermission[2] = value; break;

                case 0x7EFA: prgBank[0] = value >> 2; break;
                case 0x7EFB: prgBank[1] = value >> 2; break;
                case 0x7EFC: prgBank[2] = value >> 2; break;
            }
        }

        // No $8000+ PRG registers
        public void MapperW_PRG(ushort address, byte value) { }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + i * 1024;
                return;
            }
            int n1k = CHR_ROM_count * 8;

            if (chrMode == 0)
            {
                // Regs 0&1: 2KB banks (ignore LSB), regs 2-5: 1KB banks
                // $0000-$07FF: reg[0] as 2KB
                int b0 = (chrReg[0] & 0xFE) % n1k;
                NesCore.chrBankPtrs[0] = CHR_ROM + (b0 << 10);
                NesCore.chrBankPtrs[1] = CHR_ROM + ((b0 + 1) << 10);
                // $0800-$0FFF: reg[1] as 2KB
                int b1 = (chrReg[1] & 0xFE) % n1k;
                NesCore.chrBankPtrs[2] = CHR_ROM + (b1 << 10);
                NesCore.chrBankPtrs[3] = CHR_ROM + ((b1 + 1) << 10);
                // $1000-$13FF: reg[2] 1KB
                NesCore.chrBankPtrs[4] = CHR_ROM + ((chrReg[2] % n1k) << 10);
                // $1400-$17FF: reg[3] 1KB
                NesCore.chrBankPtrs[5] = CHR_ROM + ((chrReg[3] % n1k) << 10);
                // $1800-$1BFF: reg[4] 1KB
                NesCore.chrBankPtrs[6] = CHR_ROM + ((chrReg[4] % n1k) << 10);
                // $1C00-$1FFF: reg[5] 1KB
                NesCore.chrBankPtrs[7] = CHR_ROM + ((chrReg[5] % n1k) << 10);
            }
            else
            {
                // Regs 2-5: 1KB at $0000-$0FFF, regs 0&1: 2KB at $1000-$1FFF
                NesCore.chrBankPtrs[0] = CHR_ROM + ((chrReg[2] % n1k) << 10);
                NesCore.chrBankPtrs[1] = CHR_ROM + ((chrReg[3] % n1k) << 10);
                NesCore.chrBankPtrs[2] = CHR_ROM + ((chrReg[4] % n1k) << 10);
                NesCore.chrBankPtrs[3] = CHR_ROM + ((chrReg[5] % n1k) << 10);
                int b0 = (chrReg[0] & 0xFE) % n1k;
                NesCore.chrBankPtrs[4] = CHR_ROM + (b0 << 10);
                NesCore.chrBankPtrs[5] = CHR_ROM + ((b0 + 1) << 10);
                int b1 = (chrReg[1] & 0xFE) % n1k;
                NesCore.chrBankPtrs[6] = CHR_ROM + (b1 << 10);
                NesCore.chrBankPtrs[7] = CHR_ROM + ((b1 + 1) << 10);
            }
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
    }
}
