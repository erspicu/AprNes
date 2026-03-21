namespace AprNes
{
    // Irem H-3001 — Daiku no Gen San 2 (J), Kaiou - Wrath of the Black Dragon (J)
    // PRG: 3×8K switchable ($8000/$A000/$C000 regs), 1×8K fixed last bank at $E000
    // CHR: 8×1K banks via $B000-$B007
    // Mirror: $9001 bit 7 (0=Vertical, 1=Horizontal)
    // IRQ: 16-bit down-counter, per CPU cycle when enabled
    //   $9003 bit7: enable + clear IRQ
    //   $9004: reload counter from irqReload + clear IRQ
    //   $9005: irqReload high byte
    //   $9006: irqReload low byte
    //   Counter fires at 0 → set IRQ, auto-disable
    unsafe public class Mapper065 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int prgBank0, prgBank1, prgBank2;  // 8K banks at $8000/$A000/$C000
        byte[] chrBank = new byte[8];      // 1K CHR bank selectors

        bool irqEnabled;
        ushort irqCounter;
        ushort irqReload;

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
            prgBank0 = 0; prgBank1 = 1; prgBank2 = PRG_ROM_count * 2 - 2;
            for (int i = 0; i < 8; i++) chrBank[i] = 0;
            irqEnabled = false;
            irqCounter = irqReload = 0;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }

        public void MapperW_PRG(ushort address, byte value)
        {
            switch (address)
            {
                case 0x8000: prgBank0 = value; break;

                case 0x9001: *Vertical = (value & 0x80) != 0 ? 1 : 0; break; // bit7: 1=H, 0=V
                case 0x9003:
                    irqEnabled = (value & 0x80) != 0;
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    break;
                case 0x9004:
                    irqCounter = irqReload;
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    break;
                case 0x9005: irqReload = (ushort)((irqReload & 0x00FF) | (value << 8)); break;
                case 0x9006: irqReload = (ushort)((irqReload & 0xFF00) | value); break;

                case 0xA000: prgBank1 = value; break;

                case 0xB000: chrBank[0] = value; UpdateCHRBanks(); break;
                case 0xB001: chrBank[1] = value; UpdateCHRBanks(); break;
                case 0xB002: chrBank[2] = value; UpdateCHRBanks(); break;
                case 0xB003: chrBank[3] = value; UpdateCHRBanks(); break;
                case 0xB004: chrBank[4] = value; UpdateCHRBanks(); break;
                case 0xB005: chrBank[5] = value; UpdateCHRBanks(); break;
                case 0xB006: chrBank[6] = value; UpdateCHRBanks(); break;
                case 0xB007: chrBank[7] = value; UpdateCHRBanks(); break;

                case 0xC000: prgBank2 = value; break;
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            int total8k = PRG_ROM_count * 2;
            int bank;
            if      (address < 0xA000) bank = prgBank0 % total8k;
            else if (address < 0xC000) bank = prgBank1 % total8k;
            else if (address < 0xE000) bank = prgBank2 % total8k;
            else                       bank = total8k - 1;  // fixed last 8K
            return PRG_ROM[(address & 0x1FFF) + (bank << 13)];
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
                NesCore.chrBankPtrs[i] = CHR_ROM + ((chrBank[i] % total1k) << 10);
        }

        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }
        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }

        public void CpuCycle()
        {
            if (!irqEnabled) return;
            irqCounter--;
            if (irqCounter == 0)
            {
                irqEnabled = false;
                NesCore.statusmapperint = true;
                NesCore.UpdateIRQLine();
            }
        }

        public void NotifyA12(int addr, int ppuAbsCycle) { }
    }
}
