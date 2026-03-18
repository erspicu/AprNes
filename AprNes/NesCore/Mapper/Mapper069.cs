namespace AprNes
{
    // Sunsoft FME-7 (Sunsoft 5B) — Batman (J/U), Mr. Gimmick, etc.
    // $8000 write: command select (bits 0-3)
    // $A000 write: execute command
    //   0-7:  1K CHR bank at PPU $0000/$0400/$0800/$0C00/$1000/$1400/$1800/$1C00
    //   8:    $6000-$7FFF control (bit6=0:ROM, bit6=1+bit7=1:WRAM, bit6=1+bit7=0:no-access)
    //   9:    8K PRG at $8000
    //   10:   8K PRG at $A000
    //   11:   8K PRG at $C000
    //   12:   mirroring (0=V, 1=H, 2=single-A, 3=single-B)
    //   13:   IRQ control (bit0=irqEnable, bit7=counterEnable, clears pending IRQ)
    //   14:   IRQ counter low byte
    //   15:   IRQ counter high byte
    // Last 8K PRG fixed at $E000. CPU-cycle counter IRQ.
    unsafe public class Mapper069 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int CHR_ROM_count;
        int PRG_ROM_count;
        int* Vertical;

        int cmdReg;
        int[] chrBank = new int[8];
        int prgBank8000, prgBankA000, prgBankC000;
        byte wramCtrl;          // command 8 value: bit6=useRAM, bit7=ramWritable, bits0-5=ROM bank
        bool irqEnabled;        // bit0 of cmd 13
        bool counterEnabled;    // bit7 of cmd 13
        ushort irqCounter;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM;
            CHR_ROM = _CHR_ROM;
            ppu_ram = _ppu_ram;
            CHR_ROM_count = _CHR_ROM_count;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical = _Vertical;
        }

        public void Reset()
        {
            cmdReg = 0;
            for (int i = 0; i < 8; i++) chrBank[i] = 0;
            prgBank8000 = prgBankA000 = prgBankC000 = 0;
            wramCtrl = 0;
            irqEnabled = false;
            counterEnabled = false;
            irqCounter = 0;
        }

        public byte MapperR_ExpansionROM(ushort address) { return 0; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }

        public void MapperW_RAM(ushort address, byte value)
        {
            // WRAM only if bit6=1 and bit7=1
            if ((wramCtrl & 0xC0) == 0xC0) NesCore.NES_MEM[address] = value;
        }

        public byte MapperR_RAM(ushort address)
        {
            if ((wramCtrl & 0x40) == 0)
            {
                // ROM mapped at $6000: select 8K bank from bits 0-5
                int bank = wramCtrl & 0x3F;
                int total8k = PRG_ROM_count * 2;
                bank = bank % total8k;
                return PRG_ROM[(address - 0x6000) + (bank << 13)];
            }
            if ((wramCtrl & 0x80) != 0)
                return NesCore.NES_MEM[address];  // WRAM readable
            return 0;  // bit6=1, bit7=0: no-access
        }

        public void MapperW_PRG(ushort address, byte value)
        {
            if ((address & 0xE000) == 0x8000)       // $8000-$9FFF: command
                cmdReg = value & 0x0F;
            else if ((address & 0xE000) == 0xA000)  // $A000-$BFFF: data
                ExecuteCommand(value);
            // $C000-$FFFF: Sunsoft 5B audio (not implemented, ignore)
        }

        void ExecuteCommand(byte value)
        {
            if (cmdReg <= 7)
            {
                chrBank[cmdReg] = value;
                UpdateCHRBanks();
            }
            else if (cmdReg == 8)  { wramCtrl = value; }
            else if (cmdReg == 9)  { prgBank8000 = value & 0x3F; }
            else if (cmdReg == 10) { prgBankA000 = value & 0x3F; }
            else if (cmdReg == 11) { prgBankC000 = value & 0x3F; }
            else if (cmdReg == 12)
            {
                int m = value & 3;
                if      (m == 0) *Vertical = 1;  // vertical
                else if (m == 1) *Vertical = 0;  // horizontal
                else if (m == 2) *Vertical = 2;  // single screen A
                else             *Vertical = 3;  // single screen B
            }
            else if (cmdReg == 13)
            {
                irqEnabled  = (value & 0x01) != 0;
                counterEnabled = (value & 0x80) != 0;
                NesCore.statusmapperint = false;
                NesCore.UpdateIRQLine();
            }
            else if (cmdReg == 14) { irqCounter = (ushort)((irqCounter & 0xFF00) | value); }
            else if (cmdReg == 15) { irqCounter = (ushort)((irqCounter & 0x00FF) | (value << 8)); }
        }

        public byte MapperR_RPG(ushort address)
        {
            int total8k = PRG_ROM_count * 2;
            int bank;
            if (address < 0xA000)      bank = prgBank8000 % total8k;
            else if (address < 0xC000) bank = prgBankA000 % total8k;
            else if (address < 0xE000) bank = prgBankC000 % total8k;
            else                       bank = total8k - 1;  // last 8K fixed
            int pageBase = address & 0x1FFF;
            return PRG_ROM[pageBase + (bank << 13)];
        }

        public void UpdateCHRBanks()
        {
            int total1k = CHR_ROM_count * 8;
            for (int i = 0; i < 8; i++)
                NesCore.chrBankPtrs[i] = CHR_ROM + ((chrBank[i] % total1k) << 10);
        }

        public byte MapperR_CHR(int address)
        {
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void CpuCycle()
        {
            if (!counterEnabled) return;
            irqCounter--;
            if (irqCounter == 0xFFFF && irqEnabled)
            {
                NesCore.statusmapperint = true;
                NesCore.UpdateIRQLine();
            }
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void NotifyA12(int addr, int ppuAbsCycle) { }
    }
}
