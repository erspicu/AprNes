namespace AprNes
{
    // Bandai LZ93D50 + 8KiB WRAM — Mapper 153
    // Same layout as Mapper016 but:
    //   - CHR regs bit0 extends PRG bank to 5 bits (for 512K PRG-ROM carts)
    //   - CHR-RAM only (no CHR-ROM)
    //   - Battery-backed WRAM at $6000-$7FFF; reg $0D bit5 = enable
    //   - Registers only at $8000-$FFFF (same as Mapper016 sub5)
    //   - IRQ: latch-reload style (same as LZ93D50)
    //
    // Games: Famicom Jump II - Saikyou no 7 Nin (J)

    unsafe public class Mapper153 : IMapper
    {
        byte* PRG_ROM, ppu_ram;
        int PRG_ROM_count;
        int* Vertical;

        byte   prgReg;             // bits[3:0] from reg $08
        byte[] chrBanks = new byte[8]; // bit0 = PRG extension bit
        bool   wramEnabled;

        ushort irqCounter;
        ushort irqLatch;
        bool   irqEnabled;

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical = _Vertical;
        }

        public void Reset()
        {
            prgReg = 0;
            for (int i = 0; i < 8; i++) chrBanks[i] = 0;
            irqCounter = irqLatch = 0;
            irqEnabled = false;
            wramEnabled = false;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }

        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value)
        {
            if (wramEnabled) NesCore.NES_MEM[address] = value;
        }

        public void MapperW_PRG(ushort address, byte value)
        {
            WriteReg(address & 0x000F, value);
        }

        void WriteReg(int reg, byte value)
        {
            switch (reg)
            {
                case 0: case 1: case 2: case 3:
                case 4: case 5: case 6: case 7:
                    chrBanks[reg] = value;
                    // bit0 of CHR regs extends PRG bank — no CHR banking (CHR-RAM only)
                    break;

                case 0x8:
                    prgReg = (byte)(value & 0x0F);
                    break;

                case 0x9:
                    switch (value & 0x03)
                    {
                        case 0: *Vertical = 1; break;  // Vertical
                        case 1: *Vertical = 0; break;  // Horizontal
                        case 2: *Vertical = 2; break;  // Single-A
                        case 3: *Vertical = 3; break;  // Single-B
                    }
                    break;

                case 0xA:
                    irqEnabled = (value & 0x01) != 0;
                    irqCounter = irqLatch;
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    break;

                case 0xB:
                    irqLatch = (ushort)((irqLatch & 0xFF00) | value);
                    break;

                case 0xC:
                    irqLatch = (ushort)((irqLatch & 0x00FF) | (value << 8));
                    break;

                case 0xD:
                    wramEnabled = (value & 0x20) != 0;
                    break;
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            // PRG bank 5-bit: extBit(from chrBanks) | prgReg[3:0]
            int extBit = 0;
            for (int i = 0; i < 8; i++) extBit |= (chrBanks[i] & 1);
            int n = PRG_ROM_count;
            int bank;
            if (address < 0xC000)
                bank = ((extBit << 4) | prgReg) % n;
            else
                bank = ((extBit << 4) | 0x0F) % n;
            return PRG_ROM[(address & 0x3FFF) + (bank << 14)];
        }

        public void UpdateCHRBanks()
        {
            // Always CHR-RAM: 8×1K from ppu_ram
            for (int i = 0; i < 8; i++)
                NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
        }

        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }
        public void MapperW_CHR(int addr, byte val) { ppu_ram[addr] = val; }

        public void CpuCycle()
        {
            if (!irqEnabled) return;
            if (irqCounter == 0)
            {
                NesCore.statusmapperint = true;
                NesCore.UpdateIRQLine();
            }
            irqCounter--;
        }

        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void PpuClock() { }
        public void CpuClockRise() { }
            public void Cleanup() { }
}
}
