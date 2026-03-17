namespace AprNes
{
    // MMC3 mapper for Speed Core (simplified scanline-based IRQ)
    // Ref: https://wiki.nesdev.com/w/index.php/MMC3
    unsafe public class Mapper004_S : IMapper_S
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int CHR_ROM_count;
        int PRG_ROM_count;
        int* Vertical;

        // PRG bank registers (8KB units)
        int PRG0_Bankselect = 0; // R6: $8000-$9FFF (or $C000-$DFFF in swap mode)
        int PRG1_Bankselect = 0; // R7: $A000-$BFFF
        int PRG_Bankmode    = 0; // 0=R6 at $8000, 1=R6 at $C000

        // CHR bank registers
        int CHR_Bankmode       = 0; // 0=2KB at $0000, 1KB at $1000; 1=inverted
        int CHR0_Bankselect2k  = 0; // R0: 2KB bank at $0000 (or $1000)
        int CHR1_Bankselect2k  = 0; // R1: 2KB bank at $0800 (or $1800)
        int CHR0_Bankselect1k  = 0; // R2: 1KB bank
        int CHR1_Bankselect1k  = 0; // R3: 1KB bank
        int CHR2_Bankselect1k  = 0; // R4: 1KB bank
        int CHR3_Bankselect1k  = 0; // R5: 1KB bank

        // Bank select register latch (which R register to update on next data write)
        int BankReg = 0;

        // Scanline IRQ
        int  irqCounter = 0;
        int  irqLatch   = 0;
        bool irqEnabled = false;
        bool irqReset   = false;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
                               int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM       = _PRG_ROM;
            CHR_ROM       = _CHR_ROM;
            ppu_ram       = _ppu_ram;
            CHR_ROM_count = _CHR_ROM_count;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical      = _Vertical;
            UpdateChrPtrs();
            UpdatePrgPtrs();
        }

        void UpdatePrgPtrs()
        {
            int total8k = PRG_ROM_count * 2;
            int secondLast = (total8k - 2) << 13;
            int last       = (total8k - 1) << 13;
            if (PRG_Bankmode == 0)
            {
                NesCoreSpeed.prgBankPtrs_S[4] = PRG_ROM + ((PRG0_Bankselect % total8k) << 13);
                NesCoreSpeed.prgBankPtrs_S[5] = PRG_ROM + ((PRG1_Bankselect % total8k) << 13);
                NesCoreSpeed.prgBankPtrs_S[6] = PRG_ROM + secondLast;
                NesCoreSpeed.prgBankPtrs_S[7] = PRG_ROM + last;
            }
            else
            {
                NesCoreSpeed.prgBankPtrs_S[4] = PRG_ROM + secondLast;
                NesCoreSpeed.prgBankPtrs_S[5] = PRG_ROM + ((PRG1_Bankselect % total8k) << 13);
                NesCoreSpeed.prgBankPtrs_S[6] = PRG_ROM + ((PRG0_Bankselect % total8k) << 13);
                NesCoreSpeed.prgBankPtrs_S[7] = PRG_ROM + last;
            }
        }

        void UpdateChrPtrs()
        {
            if (CHR_ROM_count == 0) {
                for (int i = 0; i < 8; i++) NesCoreSpeed.chrBankPtrs_S[i] = ppu_ram + i * 1024;
                return;
            }
            if (CHR_Bankmode == 0) {
                NesCoreSpeed.chrBankPtrs_S[0] = CHR_ROM + ((CHR0_Bankselect2k & 0xFE) << 10);
                NesCoreSpeed.chrBankPtrs_S[1] = CHR_ROM + ((CHR0_Bankselect2k | 1)   << 10);
                NesCoreSpeed.chrBankPtrs_S[2] = CHR_ROM + ((CHR1_Bankselect2k & 0xFE) << 10);
                NesCoreSpeed.chrBankPtrs_S[3] = CHR_ROM + ((CHR1_Bankselect2k | 1)   << 10);
                NesCoreSpeed.chrBankPtrs_S[4] = CHR_ROM + (CHR0_Bankselect1k << 10);
                NesCoreSpeed.chrBankPtrs_S[5] = CHR_ROM + (CHR1_Bankselect1k << 10);
                NesCoreSpeed.chrBankPtrs_S[6] = CHR_ROM + (CHR2_Bankselect1k << 10);
                NesCoreSpeed.chrBankPtrs_S[7] = CHR_ROM + (CHR3_Bankselect1k << 10);
            } else {
                NesCoreSpeed.chrBankPtrs_S[0] = CHR_ROM + (CHR0_Bankselect1k << 10);
                NesCoreSpeed.chrBankPtrs_S[1] = CHR_ROM + (CHR1_Bankselect1k << 10);
                NesCoreSpeed.chrBankPtrs_S[2] = CHR_ROM + (CHR2_Bankselect1k << 10);
                NesCoreSpeed.chrBankPtrs_S[3] = CHR_ROM + (CHR3_Bankselect1k << 10);
                NesCoreSpeed.chrBankPtrs_S[4] = CHR_ROM + ((CHR0_Bankselect2k & 0xFE) << 10);
                NesCoreSpeed.chrBankPtrs_S[5] = CHR_ROM + ((CHR0_Bankselect2k | 1)   << 10);
                NesCoreSpeed.chrBankPtrs_S[6] = CHR_ROM + ((CHR1_Bankselect2k & 0xFE) << 10);
                NesCoreSpeed.chrBankPtrs_S[7] = CHR_ROM + ((CHR1_Bankselect2k | 1)   << 10);
            }
        }

        public byte MapperR_PRG(ushort address)
        {
            int totalBanks8k = PRG_ROM_count * 2; // number of 8KB banks

            if (PRG_Bankmode == 0)
            {
                // R6 swappable at $8000, $C000 fixed to second-last
                if      (address < 0xA000) return PRG_ROM[(address - 0x8000) + ((PRG0_Bankselect % totalBanks8k) << 13)];
                else if (address < 0xC000) return PRG_ROM[(address - 0xA000) + ((PRG1_Bankselect % totalBanks8k) << 13)];
                else if (address < 0xE000) return PRG_ROM[(address - 0xC000) + ((totalBanks8k - 2) << 13)]; // fixed second-last
                else                       return PRG_ROM[(address - 0xE000) + ((totalBanks8k - 1) << 13)]; // fixed last
            }
            else
            {
                // R6 swappable at $C000, $8000 fixed to second-last
                if      (address < 0xA000) return PRG_ROM[(address - 0x8000) + ((totalBanks8k - 2) << 13)]; // fixed second-last
                else if (address < 0xC000) return PRG_ROM[(address - 0xA000) + ((PRG1_Bankselect % totalBanks8k) << 13)];
                else if (address < 0xE000) return PRG_ROM[(address - 0xC000) + ((PRG0_Bankselect % totalBanks8k) << 13)];
                else                       return PRG_ROM[(address - 0xE000) + ((totalBanks8k - 1) << 13)]; // fixed last
            }
        }

        public void MapperW_PRG(ushort address, byte value)
        {
            bool even = (address & 1) == 0;

            if (address < 0xA000)
            {
                if (even)
                {
                    // $8000 (even): Bank select
                    BankReg      = value & 7;
                    int newPrgMode = (value >> 6) & 1;
                    if (newPrgMode != PRG_Bankmode) { PRG_Bankmode = newPrgMode; UpdatePrgPtrs(); }
                    else PRG_Bankmode = newPrgMode;
                    int newChrMode = (value >> 7) & 1;
                    if (newChrMode != CHR_Bankmode) { CHR_Bankmode = newChrMode; UpdateChrPtrs(); }
                }
                else
                {
                    // $8001 (odd): Bank data — update register R[BankReg]
                    switch (BankReg)
                    {
                        case 0: CHR0_Bankselect2k = value; break;
                        case 1: CHR1_Bankselect2k = value; break;
                        case 2: CHR0_Bankselect1k = value; break;
                        case 3: CHR1_Bankselect1k = value; break;
                        case 4: CHR2_Bankselect1k = value; break;
                        case 5: CHR3_Bankselect1k = value; break;
                        case 6: PRG0_Bankselect   = value; UpdatePrgPtrs(); return;
                        case 7: PRG1_Bankselect   = value; UpdatePrgPtrs(); return;
                    }
                    // CHR bank changed (cases 0-5)
                    UpdateChrPtrs();
                }
            }
            else if (address < 0xC000)
            {
                if (even)
                {
                    // $A000 (even): Mirroring (0=vertical, 1=horizontal)
                    *Vertical = ((value & 1) != 0) ? 0 : 1;
                }
                // $A001 (odd): PRG RAM protect — ignored in Speed Core
            }
            else if (address < 0xE000)
            {
                if (even)
                    irqLatch = value;       // $C000 (even): IRQ latch
                else
                    irqReset = true;        // $C001 (odd): IRQ reload on next scanline
            }
            else
            {
                if (even)
                {
                    // $E000 (even): IRQ disable + acknowledge
                    irqEnabled = false;
                    NesCoreSpeed.irq_pending_S = false;
                }
                else
                {
                    // $E001 (odd): IRQ enable
                    irqEnabled = true;
                }
            }
        }

        // Called once per scanline by the Speed Core PPU to clock the MMC3 IRQ counter.
        // Mirrors the logic of Mapper004.Mapper04step_IRQ() but sets irq_pending_S instead.
        public void Mapper04step_IRQ_S()
        {
            if (irqCounter == 0 || irqReset)
            {
                irqCounter = irqLatch;
                irqReset   = false;
            }
            else
            {
                irqCounter--;
            }

            if (irqCounter == 0 && irqEnabled)
            {
                NesCoreSpeed.irq_pending_S = true;
            }
        }

        public byte MapperR_CHR(int address)
        {
            if (CHR_ROM_count == 0) return ppu_ram[address & 0x1FFF];

            if (CHR_Bankmode == 0)
            {
                // Two 2KB banks at $0000-$0FFF, four 1KB banks at $1000-$1FFF
                if (address < 0x1000)
                {
                    if      (address < 0x0400) return CHR_ROM[ address         + ((CHR0_Bankselect2k & 0xFE) << 10)];
                    else if (address < 0x0800) return CHR_ROM[(address - 0x400) + ((CHR0_Bankselect2k | 1)   << 10)];
                    else if (address < 0x0C00) return CHR_ROM[(address - 0x800) + ((CHR1_Bankselect2k & 0xFE) << 10)];
                    else                       return CHR_ROM[(address - 0xC00) + ((CHR1_Bankselect2k | 1)   << 10)];
                }
                else
                {
                    if      (address < 0x1400) return CHR_ROM[(address - 0x1000) + (CHR0_Bankselect1k << 10)];
                    else if (address < 0x1800) return CHR_ROM[(address - 0x1400) + (CHR1_Bankselect1k << 10)];
                    else if (address < 0x1C00) return CHR_ROM[(address - 0x1800) + (CHR2_Bankselect1k << 10)];
                    else                       return CHR_ROM[(address - 0x1C00) + (CHR3_Bankselect1k << 10)];
                }
            }
            else
            {
                // Four 1KB banks at $0000-$0FFF, two 2KB banks at $1000-$1FFF
                if (address < 0x1000)
                {
                    if      (address < 0x0400) return CHR_ROM[ address         + (CHR0_Bankselect1k << 10)];
                    else if (address < 0x0800) return CHR_ROM[(address - 0x400) + (CHR1_Bankselect1k << 10)];
                    else if (address < 0x0C00) return CHR_ROM[(address - 0x800) + (CHR2_Bankselect1k << 10)];
                    else                       return CHR_ROM[(address - 0xC00) + (CHR3_Bankselect1k << 10)];
                }
                else
                {
                    if      (address < 0x1400) return CHR_ROM[(address - 0x1000) + ((CHR0_Bankselect2k & 0xFE) << 10)];
                    else if (address < 0x1800) return CHR_ROM[(address - 0x1400) + ((CHR0_Bankselect2k | 1)   << 10)];
                    else if (address < 0x1C00) return CHR_ROM[(address - 0x1800) + ((CHR1_Bankselect2k & 0xFE) << 10)];
                    else                       return CHR_ROM[(address - 0x1C00) + ((CHR1_Bankselect2k | 1)   << 10)];
                }
            }
        }

        public byte MapperR_RAM(ushort address)              { return NesCoreSpeed.NES_MEM_S[address]; }
        public void MapperW_RAM(ushort address, byte value)  { NesCoreSpeed.NES_MEM_S[address] = value; }
        public byte MapperR_EXP(ushort address)              { return 0; }
        public void MapperW_EXP(ushort address, byte value)  { }
    }
}
