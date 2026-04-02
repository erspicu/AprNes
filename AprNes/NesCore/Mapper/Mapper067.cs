namespace AprNes
{
    // Sunsoft-3 — Mapper 067
    // PRG: 16KB switchable at $8000, last 16KB fixed at $C000
    // CHR: 4×2KB banks via $8800/$9800/$A800/$B800
    // IRQ: 16-bit down-counter; latch at $C800 (alt lo/hi); toggle at $D800; ack at $F800
    // Mirror: $E800 bits[1:0] (0=V,1=H,2=ScreenA,3=ScreenB)
    unsafe public class Mapper067 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int prgBank;                 // 16KB PRG bank at $8000
        int[] chrBank = new int[4]; // 2KB CHR banks

        bool irqLatch;              // tracks whether next $C800 write is hi or lo byte
        bool irqEnabled;
        ushort irqCounter;

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
        public void NotifyA12(int addr, int ppuAbsCycle) { }

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
            for (int i = 0; i < 4; i++) chrBank[i] = 0;
            irqLatch = false; irqEnabled = false; irqCounter = 0;
            UpdateCHRBanks();
        }
        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + i * 1024;
                return;
            }
            int n2k = CHR_ROM_count * 4;
            for (int slot = 0; slot < 4; slot++)
            {
                int page = chrBank[slot] % n2k;
                NesCore.chrBankPtrs[slot * 2]     = CHR_ROM + (page << 11);
                NesCore.chrBankPtrs[slot * 2 + 1] = CHR_ROM + (page << 11) + 1024;
            }
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }

        public void CpuCycle()
        {
            if (irqEnabled)
            {
                irqCounter--;
                if (irqCounter == 0xFFFF) // wrapped through 0
                {
                    irqEnabled = false;
                    NesCore.statusmapperint = true;
                    NesCore.UpdateIRQLine();
                }
            }
        }

        public void MapperW_PRG(ushort address, byte value)
        {
            switch (address & 0xF800)
            {
                case 0x8800: chrBank[0] = value; UpdateCHRBanks(); break;
                case 0x9800: chrBank[1] = value; UpdateCHRBanks(); break;
                case 0xA800: chrBank[2] = value; UpdateCHRBanks(); break;
                case 0xB800: chrBank[3] = value; UpdateCHRBanks(); break;

                case 0xC800:
                    // Alternate lo/hi byte of IRQ latch
                    if (!irqLatch)
                        irqCounter = (ushort)((irqCounter & 0x00FF) | (value << 8));
                    else
                        irqCounter = (ushort)((irqCounter & 0xFF00) | value);
                    irqLatch = !irqLatch;
                    break;

                case 0xD800:
                    irqEnabled = (value & 0x10) != 0;
                    irqLatch = false;
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    break;

                case 0xE800:
                    switch (value & 0x03)
                    {
                        case 0: *Vertical = 1; break; // Vertical
                        case 1: *Vertical = 0; break; // Horizontal
                        case 2: *Vertical = 2; break; // Single-screen A
                        case 3: *Vertical = 3; break; // Single-screen B
                    }
                    break;

                case 0xF800:
                    prgBank = value & 0x0F;
                    break;
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            int n16k = PRG_ROM_count; // PRG_ROM_count is already in 16KB units
            if (address < 0xC000)
                return PRG_ROM[(address - 0x8000) + ((prgBank % n16k) << 14)];
            return PRG_ROM[(address - 0xC000) + ((n16k - 1) << 14)];
        }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address];
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void CpuClockRise() { }
            public void Cleanup() { }
}
}
