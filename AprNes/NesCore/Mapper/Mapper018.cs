namespace AprNes
{
    // Jaleco SS8806 — Mapper 018
    // Games: Ninja Jajamaru, Pizza Pop!, Magic John, Saiyuuki World 2, etc.
    //
    // PRG: 3×8K switchable + fixed last 8K at $E000
    //   Registers use nibble-write scheme: addr bit0=0 → low nibble, bit0=1 → high nibble
    //   $8000/$8001 = bank[0], $8002/$8003 = bank[1], $9000/$9001 = bank[2]
    //
    // CHR: 8×1K banks
    //   $A000/$A001 = chr[0], $A002/$A003 = chr[1]
    //   $B000/$B001 = chr[2], $B002/$B003 = chr[3]
    //   $C000/$C001 = chr[4], $C002/$C003 = chr[5]
    //   $D000/$D001 = chr[6], $D002/$D003 = chr[7]
    //
    // IRQ: 16-bit CPU-cycle counter, variable width (4/8/12/16-bit)
    //   $E000-$E003 = reload nibbles [0..3] (lo→hi)
    //   $F000 = clear IRQ + reload counter
    //   $F001 = clear IRQ + control (bit0=enable, bit1=12-bit, bit2=8-bit, bit3=4-bit)
    //   $F002 = mirroring (0=H, 1=V, 2=single-A, 3=single-B)
    //
    // Only the active-width bits count; upper bits preserved on each clock.

    unsafe public class Mapper018 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        byte[] prgBanks = new byte[3];
        byte[] chrBanks = new byte[8];

        // IRQ
        byte[] irqReloadNibble = new byte[4];  // $E000-$E003
        ushort irqCounter;
        int  irqCounterSize;  // 0=16bit, 1=12bit, 2=8bit, 3=4bit
        bool irqEnabled;

        static readonly ushort[] irqMask = { 0xFFFF, 0x0FFF, 0x00FF, 0x000F };

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
            for (int i = 0; i < 3; i++) prgBanks[i] = 0;
            for (int i = 0; i < 8; i++) chrBanks[i] = 0;
            for (int i = 0; i < 4; i++) irqReloadNibble[i] = 0;
            irqCounter = 0;
            irqCounterSize = 0;
            irqEnabled = false;
            UpdateCHRBanks();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }

        public void MapperW_PRG(ushort address, byte value)
        {
            bool hi = (address & 0x01) != 0;
            value &= 0x0F;

            switch (address & 0xF003)
            {
                // PRG banks (nibble-addressed)
                case 0x8000: case 0x8001: UpdatePrgNibble(0, value, hi); break;
                case 0x8002: case 0x8003: UpdatePrgNibble(1, value, hi); break;
                case 0x9000: case 0x9001: UpdatePrgNibble(2, value, hi); break;

                // CHR banks (nibble-addressed)
                case 0xA000: case 0xA001: UpdateChrNibble(0, value, hi); break;
                case 0xA002: case 0xA003: UpdateChrNibble(1, value, hi); break;
                case 0xB000: case 0xB001: UpdateChrNibble(2, value, hi); break;
                case 0xB002: case 0xB003: UpdateChrNibble(3, value, hi); break;
                case 0xC000: case 0xC001: UpdateChrNibble(4, value, hi); break;
                case 0xC002: case 0xC003: UpdateChrNibble(5, value, hi); break;
                case 0xD000: case 0xD001: UpdateChrNibble(6, value, hi); break;
                case 0xD002: case 0xD003: UpdateChrNibble(7, value, hi); break;

                // IRQ reload nibbles
                case 0xE000: irqReloadNibble[0] = value; break;
                case 0xE001: irqReloadNibble[1] = value; break;
                case 0xE002: irqReloadNibble[2] = value; break;
                case 0xE003: irqReloadNibble[3] = value; break;

                // $F000: clear IRQ + reload counter
                case 0xF000:
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    irqCounter = (ushort)(
                        irqReloadNibble[0]       |
                        (irqReloadNibble[1] << 4) |
                        (irqReloadNibble[2] << 8) |
                        (irqReloadNibble[3] << 12));
                    break;

                // $F001: clear IRQ + set enable + counter width
                case 0xF001:
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    irqEnabled = (value & 0x01) != 0;
                    if      ((value & 0x08) != 0) irqCounterSize = 3;  // 4-bit
                    else if ((value & 0x04) != 0) irqCounterSize = 2;  // 8-bit
                    else if ((value & 0x02) != 0) irqCounterSize = 1;  // 12-bit
                    else                           irqCounterSize = 0;  // 16-bit
                    break;

                // $F002: mirroring (0=H, 1=V, 2=single-A, 3=single-B)
                // AprNes: *Vertical=0=H, 1=V, ≥2=one-screen
                case 0xF002:
                    *Vertical = value & 0x03;
                    break;

                // $F003: expansion audio (not implemented)
            }
        }

        void UpdatePrgNibble(int bank, byte nibble, bool hi)
        {
            if (hi) prgBanks[bank] = (byte)((prgBanks[bank] & 0x0F) | (nibble << 4));
            else    prgBanks[bank] = (byte)((prgBanks[bank] & 0xF0) | nibble);
        }

        void UpdateChrNibble(int bank, byte nibble, bool hi)
        {
            if (hi) chrBanks[bank] = (byte)((chrBanks[bank] & 0x0F) | (nibble << 4));
            else    chrBanks[bank] = (byte)((chrBanks[bank] & 0xF0) | nibble);
            UpdateCHRBanks();
        }

        public byte MapperR_RPG(ushort address)
        {
            int n = PRG_ROM_count * 2;  // total 8K banks
            int bank;
            if      (address < 0xA000) bank = prgBanks[0] % n;
            else if (address < 0xC000) bank = prgBanks[1] % n;
            else if (address < 0xE000) bank = prgBanks[2] % n;
            else                       bank = n - 1;
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
                NesCore.chrBankPtrs[i] = CHR_ROM + ((chrBanks[i] % total1k) << 10);
        }

        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }
        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }

        public void CpuCycle()
        {
            if (!irqEnabled) return;

            ushort mask = irqMask[irqCounterSize];
            ushort masked = (ushort)(irqCounter & mask);
            masked--;
            if (masked == 0)
            {
                NesCore.statusmapperint = true;
                NesCore.UpdateIRQLine();
            }
            irqCounter = (ushort)((irqCounter & ~mask) | (masked & mask));
        }

        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void CpuClockRise() { }
            public void Cleanup() { }
}
}
