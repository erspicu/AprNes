using System.Runtime.CompilerServices;

namespace AprNes
{
    // Namco 109 / Namco108_76 — Mapper 076
    // Namco 108 family: same register mechanism as Mapper088/206,
    // but CHR uses four 2KB banks instead of two 2KB + four 1KB.
    // PRG: two switchable 8KB at $8000/$A000, fixed last two 8KB at $C000/$E000
    // CHR: 4×2KB banks (registers R2, R3, R4, R5 select 2KB banks at $0000/$0800/$1000/$1800)
    // No IRQ. Hardwired mirroring (from header).
    // Address decode: only $8000-$9FFF is active (not $A000-$FFFF).
    unsafe public class Mapper076 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int cmdReg;
        int[] reg = new int[8]; // reg[2-5] used for CHR, reg[6-7] for PRG

        // PRG pointer cache: eliminates math in MapperR_RPG
        byte*[] prgBankPtrs = new byte*[4];
        int prgBanks, chrBanks;

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void CpuCycle() { }

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;

            prgBanks = PRG_ROM_count * 2;  // total 8KB banks
            chrBanks = CHR_ROM_count * 8;  // total 1KB banks
        }

        public void Reset()
        {
            cmdReg = 0;
            // Linear boot state: hack ROMs assume sequential bank layout at startup
            reg[0] = 0; reg[1] = 0;
            reg[2] = 0; reg[3] = 1; reg[4] = 2; reg[5] = 3; // CHR linear
            reg[6] = 0; reg[7] = 1;                           // PRG linear
            UpdatePRGBanks();
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void MapperW_PRG(ushort address, byte value)
        {
            // Namco 109 /CE is tied to A15: any write $8000-$FFFF is accepted.
            // Address decode uses only A0: even=command, odd=data.
            if ((address & 1) == 0)
            {
                cmdReg = value & 0x07;
            }
            else
            {
                reg[cmdReg] = value;
                if (cmdReg >= 6) UpdatePRGBanks();
                else if (cmdReg >= 2) UpdateCHRBanks();
            }
        }

        void UpdatePRGBanks()
        {
            if (prgBanks == 0) return;
            prgBankPtrs[0] = PRG_ROM + ((reg[6] % prgBanks) << 13);       // $8000
            prgBankPtrs[1] = PRG_ROM + ((reg[7] % prgBanks) << 13);       // $A000
            prgBankPtrs[2] = PRG_ROM + ((prgBanks - 2) << 13);            // $C000 fixed
            prgBankPtrs[3] = PRG_ROM + ((prgBanks - 1) << 13);            // $E000 fixed
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
                return;
            }
            int b0 = (reg[2] * 2) % chrBanks;
            int b1 = (reg[3] * 2) % chrBanks;
            int b2 = (reg[4] * 2) % chrBanks;
            int b3 = (reg[5] * 2) % chrBanks;
            NesCore.chrBankPtrs[0] = CHR_ROM + (b0 << 10);
            NesCore.chrBankPtrs[1] = CHR_ROM + ((b0 | 1) << 10);
            NesCore.chrBankPtrs[2] = CHR_ROM + (b1 << 10);
            NesCore.chrBankPtrs[3] = CHR_ROM + ((b1 | 1) << 10);
            NesCore.chrBankPtrs[4] = CHR_ROM + (b2 << 10);
            NesCore.chrBankPtrs[5] = CHR_ROM + ((b2 | 1) << 10);
            NesCore.chrBankPtrs[6] = CHR_ROM + (b3 << 10);
            NesCore.chrBankPtrs[7] = CHR_ROM + ((b3 | 1) << 10);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte MapperR_RPG(ushort address)
        {
            return prgBankPtrs[(address - 0x8000) >> 13][address & 0x1FFF];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void Cleanup() { }
    }
}
