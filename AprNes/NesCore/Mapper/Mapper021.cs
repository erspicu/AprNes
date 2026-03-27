namespace AprNes
{
    // Konami VRC4 — Mapper 021 (VRC4a / VRC4c)
    // Games: Wai Wai World 2, Ganbare Goemon Gaiden 2, Gradius 2 (J) etc.
    //
    // Address lines (VRC4a, submapper 1): chip-A0 = cpu-bit1, chip-A1 = cpu-bit2
    // Address lines (VRC4c, submapper 2): chip-A0 = cpu-bit6, chip-A1 = cpu-bit7
    // Heuristic (submapper 0): OR both variants
    //
    // PRG: 4×8K slots. Mode 0: $8000=R0, $A000=R1, $C000=second-last, $E000=last
    //                   Mode 1: $8000=second-last, $A000=R1, $C000=R0, $E000=last
    // CHR: 8×1K banks; 9-bit index = loCHR[i] | (hiCHR[i] << 4)
    // Mirror: $9000 bit[1:0] — 0=V, 1=H, 2=screen-A only, 3=screen-B only
    // IRQ: prescaler-based (341 PPU dots ≈ 1 scanline); or CPU-cycle mode

    unsafe public class Mapper021 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        // PRG
        int prgReg0, prgReg1, prgMode;

        // CHR — 8 banks × (lo 4 bits + hi 5 bits) = 9-bit index
        byte[] chrLo = new byte[8];
        byte[] chrHi = new byte[8];

        // IRQ (VRC IRQ — same as VRC3/VRC6/VRC7)
        byte  irqReloadValue;
        byte  irqCounter;
        int   irqPrescaler;      // starts 341, -3 per CPU cycle
        bool  irqEnabled;
        bool  irqEnabledAfterAck;
        bool  irqCycleMode;

        // submapper variant (set via MapperInit data, or from RomDbEntry in Create)
        public int Submapper;   // 0=heuristic, 1=VRC4a, 2=VRC4c

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
            prgReg0 = prgReg1 = prgMode = 0;
            for (int i = 0; i < 8; i++) { chrLo[i] = chrHi[i] = 0; }
            irqReloadValue = irqCounter = 0;
            irqPrescaler = 341;
            irqEnabled = irqEnabledAfterAck = irqCycleMode = false;
            UpdateCHRBanks();
        }

        // Translate CPU address to normalized VRC4 register address
        // Result is masked with 0xF00F so only high nibble + low 4 bits matter
        int TranslateAddr(ushort addr)
        {
            int a0, a1;
            if (Submapper == 1)
            {
                // VRC4a: A0=bit1, A1=bit2
                a0 = (addr >> 1) & 1;
                a1 = (addr >> 2) & 1;
            }
            else if (Submapper == 2)
            {
                // VRC4c: A0=bit6, A1=bit7
                a0 = (addr >> 6) & 1;
                a1 = (addr >> 7) & 1;
            }
            else
            {
                // Heuristic (submapper 0): OR both VRC4a and VRC4c bits
                a0 = ((addr >> 1) | (addr >> 6)) & 1;
                a1 = ((addr >> 2) | (addr >> 7)) & 1;
            }
            return (addr & 0xFF00) | (a1 << 1) | a0;
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }

        public void MapperW_PRG(ushort address, byte value)
        {
            int norm = TranslateAddr(address) & 0xF00F;

            if (norm >= 0x8000 && norm <= 0x8006)
            {
                prgReg0 = value & 0x1F;
            }
            else if (norm >= 0x9000 && norm <= 0x9001)
            {
                // NES: 0=Vertical, 1=Horizontal, 2=single-A, 3=single-B
                // AprNes convention: *Vertical=0=H, 1=V, ≥2=one-screen
                switch (value & 0x03)
                {
                    case 0: *Vertical = 1; break;   // NES Vertical → AprNes V (=1)
                    case 1: *Vertical = 0; break;   // NES Horizontal → AprNes H (=0)
                    case 2: *Vertical = 2; break;   // Single-screen A
                    case 3: *Vertical = 3; break;   // Single-screen B
                }
            }
            else if (norm >= 0x9002 && norm <= 0x9003)
            {
                prgMode = (value >> 1) & 0x01;
            }
            else if (norm >= 0xA000 && norm <= 0xA006)
            {
                prgReg1 = value & 0x1F;
            }
            else if (norm >= 0xB000 && norm <= 0xE006)
            {
                // CHR regs: 8 banks at $B000-$E003 (lo) / $B001-$E004 (hi)
                // regNumber = ((high nibble - 0xB) * 2) + a1; a0=0 → lo nibble, a0=1 → hi nibble
                int high = (norm >> 12) & 0xF;  // B=11, C=12, D=13, E=14
                int a0 = norm & 0x01;
                int a1 = (norm >> 1) & 0x01;
                int reg = (high - 0xB) * 2 + a1;
                if (a0 == 0)
                    chrLo[reg] = (byte)(value & 0x0F);
                else
                    chrHi[reg] = (byte)(value & 0x1F);
                UpdateCHRBanks();
            }
            else if (norm == 0xF000)
            {
                irqReloadValue = (byte)((irqReloadValue & 0xF0) | (value & 0x0F));
            }
            else if (norm == 0xF001)
            {
                irqReloadValue = (byte)((irqReloadValue & 0x0F) | ((value & 0x0F) << 4));
            }
            else if (norm == 0xF002)
            {
                irqEnabledAfterAck = (value & 0x01) != 0;
                irqEnabled         = (value & 0x02) != 0;
                irqCycleMode       = (value & 0x04) != 0;
                if (irqEnabled)
                {
                    irqCounter   = irqReloadValue;
                    irqPrescaler = 341;
                }
                NesCore.statusmapperint = false;
                NesCore.UpdateIRQLine();
            }
            else if (norm == 0xF003)
            {
                irqEnabled = irqEnabledAfterAck;
                NesCore.statusmapperint = false;
                NesCore.UpdateIRQLine();
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            int n = PRG_ROM_count * 2;  // total 8K banks
            int bank;
            if (prgMode == 0)
            {
                if      (address < 0xA000) bank = prgReg0 % n;
                else if (address < 0xC000) bank = prgReg1 % n;
                else if (address < 0xE000) bank = n - 2;
                else                       bank = n - 1;
            }
            else
            {
                if      (address < 0xA000) bank = n - 2;
                else if (address < 0xC000) bank = prgReg1 % n;
                else if (address < 0xE000) bank = prgReg0 % n;
                else                       bank = n - 1;
            }
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
            {
                int page = chrLo[i] | (chrHi[i] << 4);
                NesCore.chrBankPtrs[i] = CHR_ROM + ((page % total1k) << 10);
            }
        }

        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }
        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }

        public void CpuCycle()
        {
            if (!irqEnabled) return;

            irqPrescaler -= 3;
            if (irqCycleMode || irqPrescaler <= 0)
            {
                if (irqCounter == 0xFF)
                {
                    irqCounter = irqReloadValue;
                    NesCore.statusmapperint = true;
                    NesCore.UpdateIRQLine();
                }
                else
                {
                    irqCounter++;
                }
                irqPrescaler += 341;
            }
        }

        public void NotifyA12(int addr, int ppuAbsCycle) { }
            public void Cleanup() { }
}
}
