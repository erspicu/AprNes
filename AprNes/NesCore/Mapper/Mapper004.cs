
namespace AprNes
{
    unsafe public class Mapper004 : IMapper
    {
        //MMC3 https://wiki.nesdev.com/w/index.php/MMC3

        byte* PRG_ROM, CHR_ROM, ppu_ram , NES_MEM ;
        int CHR_ROM_count;
        int PRG_ROM_count;
        int* Vertical;

        protected bool IRQ_enable = false, IRQReset = false;
        protected int IRQlatchVal = 0, IRQCounter = 0;
        int BankReg = 0;

        // TriCNES M2 filter model for A12 rising edge detection
        int m2Filter = 0;
        int CHR0_Bankselect1k = 0, CHR1_Bankselect1k = 0, CHR2_Bankselect1k = 0, CHR3_Bankselect1k = 0;
        int CHR0_Bankselect2k = 0, CHR1_Bankselect2k = 0;
        int PRG0_Bankselect = 0, PRG1_Bankselect = 0;
        int PRG_Bankmode;
        int CHR_Bankmode;

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void Reset() { }
        public void CpuCycle() { }
        public void CpuClockRise()
        {
            if ((NesCore.ppuAddressBus & 0x1000) == 0)
            {
                if (m2Filter < 3) m2Filter++;
            }
        }
        public MapperA12Mode A12NotifyMode => MapperA12Mode.MMC3;

        public void NotifyA12(int address, int ppuAbsCycle) { }

        public void PpuClock()
        {
            bool a12Now = (NesCore.ppuAddressBus & 0x1000) != 0;
            if (!NesCore.ppuA12Prev && a12Now && m2Filter == 3)
            {
                if (NesCore.frame_count <= 13)
                    System.Console.Error.WriteLine($"A CLK SL={NesCore.scanline} cx={NesCore.ppu_cycles_x} bus={NesCore.ppuAddressBus:X4} m2={m2Filter} ctr={IRQCounter}");
                Mapper04step_IRQ();
            }
            if (a12Now)
                m2Filter = 0;
        }

        public virtual void Mapper04step_IRQ()
        {
            // TriCNES model: reload only when ReloadIRQCounter flag set
            // Counter is int — 0 decrements to -1 (no underflow to 255)
            if (IRQReset)
            {
                IRQCounter = IRQlatchVal;
                IRQReset = false;
                if (IRQCounter == 0 && IRQ_enable)
                {
                    NesCore.statusmapperint = true;
                    NesCore.UpdateIRQLine();
                }
            }
            else
            {
                // TriCNES: counter is byte, so 0-1 wraps to 255
                IRQCounter = (IRQCounter - 1) & 0xFF;
                if (IRQCounter == 0 && IRQ_enable)
                {
                    NesCore.statusmapperint = true;
                    NesCore.UpdateIRQLine();
                }
                else if (IRQCounter == 255) // byte underflow: was 0
                {
                    IRQCounter = IRQlatchVal;
                    if (IRQCounter == 0 && IRQ_enable)
                    {
                        NesCore.statusmapperint = true;
                        NesCore.UpdateIRQLine();
                    }
                }
            }
        }

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram, int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM;
            CHR_ROM = _CHR_ROM;
            ppu_ram = _ppu_ram;
            CHR_ROM_count = _CHR_ROM_count;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical = _Vertical;


            NES_MEM = NesCore.NES_MEM;
        }

        public byte MapperR_ExpansionROM(ushort address)
        {
            return 0;
          //  throw new NotImplementedException();
        }

        public void MapperW_ExpansionROM(ushort address, byte value)
        {
          //  throw new NotImplementedException();
        }

        public virtual void MapperW_RAM(ushort address, byte value)
        {
            NES_MEM[address] = value;
        }

        public virtual byte MapperR_RAM(ushort address)
        {
            return NES_MEM[address];
        }

        public virtual void MapperW_PRG(ushort address, byte value)
        {
            //$8000-$9FFF, $A000-$BFFF, $C000-$DFFF, and $E000-$FFFF
            if ((address & 1) == 0)//even
            {
                if (address < 0xa000)//$8000-$9FFF (Bank select)
                {
                    BankReg = value & 7; // Specify which bank register to update on next write to Bank Data register
                    PRG_Bankmode = (value & 0x40) >> 6;
                    int newCHRMode = (value & 0x80) >> 7;
                    if (newCHRMode != CHR_Bankmode) { CHR_Bankmode = newCHRMode; UpdateCHRBanks(); }
                    else CHR_Bankmode = newCHRMode;
                }
                else if (address < 0xc000) *Vertical = ((value & 1) > 0) ? 0 : 1; //(0: vertical; 1: horizontal) $A000-$BFFF (Mirroring)
                else if (address < 0xe000) IRQlatchVal = value;//$C000-$DFFF (IRQ latch)
                else//$E000-$FFFF IRQ disable + acknowledge
                {
                    IRQ_enable = false;
                    NesCore.statusmapperint = false; // acknowledge: de-assert /IRQ line
                    NesCore.UpdateIRQLine();
                }
            }
            else//odd
            {
                if (address < 0xa000) //$8000-$9FFF (Bank data)
                {
                    bool chrChanged = false;
                    if (BankReg == 0) { CHR0_Bankselect2k = value; chrChanged = true; } //0: Select 2 KB CHR bank at PPU $0000-$07FF (or $1000-$17FF);
                    else if (BankReg == 1) { CHR1_Bankselect2k = value; chrChanged = true; } //1: Select 2 KB CHR bank at PPU $0800-$0FFF (or $1800-$1FFF);
                    else if (BankReg == 2) { CHR0_Bankselect1k = value; chrChanged = true; } //2: Select 1 KB CHR bank at PPU $1000-$13FF (or $0000-$03FF);
                    else if (BankReg == 3) { CHR1_Bankselect1k = value; chrChanged = true; } //3: Select 1 KB CHR bank at PPU $1400-$17FF (or $0400-$07FF);
                    else if (BankReg == 4) { CHR2_Bankselect1k = value; chrChanged = true; } //4: Select 1 KB CHR bank at PPU $1800-$1BFF (or $0800-$0BFF);
                    else if (BankReg == 5) { CHR3_Bankselect1k = value; chrChanged = true; } //5: Select 1 KB CHR bank at PPU $1C00-$1FFF (or $0C00-$0FFF);
                    else if (BankReg == 6) PRG0_Bankselect = value;//6: Select 8 KB PRG ROM bank at $8000-$9FFF (or $C000-$DFFF);
                    else PRG1_Bankselect = value; //7: Select 8 KB PRG ROM bank at $A000-$BFFF
                    if (chrChanged) UpdateCHRBanks();
                }
                else if (address < 0xc000) return; //$A000-$BFFF (PRG RAM protect) nothing do
                else if (address < 0xe000)//$C000-$DFFF (IRQ reload)
                {
                    // TriCNES: $C001 sets counter to 0xFF AND sets reload flag
                    IRQCounter = 0xFF;
                    IRQReset = true;
                }
                else IRQ_enable = true; //$E000-$FFFF (IRQ enable)
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            if (PRG_Bankmode == 0) //0: $8000-$9FFF swappable, $C000-$DFFF fixed to second-last bank;
            {
                if (address < 0xa000) return PRG_ROM[(address - 0x8000) + (PRG0_Bankselect << 13)]; //$8000-$9FFF swap ok
                else if (address < 0xc000) return PRG_ROM[(address - 0xa000) + (PRG1_Bankselect << 13)]; //$A000-$BFFF swap ok
                else if (address < 0xe000) return PRG_ROM[(address - 0xc000) + (((PRG_ROM_count << 1) - 2) << 13)]; //$C000-$DFFF fixed
                else return PRG_ROM[(address - 0xe000) + (((PRG_ROM_count << 1) - 1) << 13)]; ;//$E000-$FFFF fixed

            }
            else //1: $C000-$DFFF swappable, $8000-$9FFF fixed to second-last bank
            {
                if (address < 0xa000) return PRG_ROM[(address - 0x8000) + (((PRG_ROM_count << 1) - 2) << 13)]; //$8000-$9FFF fixed
                else if (address < 0xc000) return PRG_ROM[(address - 0xa000) + (PRG1_Bankselect << 13)]; //$A000-$BFFF swap ok
                else if (address < 0xe000) return PRG_ROM[(address - 0xc000) + (PRG0_Bankselect << 13)]; //$C000-$DFFF swap ok
                else return PRG_ROM[(address - 0xe000) + (((PRG_ROM_count << 1) - 1) << 13)];//$E000-$FFFF fixed
            }
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + i * 1024;
                return;
            }
            if (CHR_Bankmode == 0) // two 2KB at $0000-$0FFF, four 1KB at $1000-$1FFF
            {
                NesCore.chrBankPtrs[0] = CHR_ROM + ((CHR0_Bankselect2k & 0xFE) << 10);
                NesCore.chrBankPtrs[1] = CHR_ROM + ((CHR0_Bankselect2k | 1) << 10);
                NesCore.chrBankPtrs[2] = CHR_ROM + ((CHR1_Bankselect2k & 0xFE) << 10);
                NesCore.chrBankPtrs[3] = CHR_ROM + ((CHR1_Bankselect2k | 1) << 10);
                NesCore.chrBankPtrs[4] = CHR_ROM + (CHR0_Bankselect1k << 10);
                NesCore.chrBankPtrs[5] = CHR_ROM + (CHR1_Bankselect1k << 10);
                NesCore.chrBankPtrs[6] = CHR_ROM + (CHR2_Bankselect1k << 10);
                NesCore.chrBankPtrs[7] = CHR_ROM + (CHR3_Bankselect1k << 10);
            }
            else // four 1KB at $0000-$0FFF, two 2KB at $1000-$1FFF
            {
                NesCore.chrBankPtrs[0] = CHR_ROM + (CHR0_Bankselect1k << 10);
                NesCore.chrBankPtrs[1] = CHR_ROM + (CHR1_Bankselect1k << 10);
                NesCore.chrBankPtrs[2] = CHR_ROM + (CHR2_Bankselect1k << 10);
                NesCore.chrBankPtrs[3] = CHR_ROM + (CHR3_Bankselect1k << 10);
                NesCore.chrBankPtrs[4] = CHR_ROM + ((CHR0_Bankselect2k & 0xFE) << 10);
                NesCore.chrBankPtrs[5] = CHR_ROM + ((CHR0_Bankselect2k | 1) << 10);
                NesCore.chrBankPtrs[6] = CHR_ROM + ((CHR1_Bankselect2k & 0xFE) << 10);
                NesCore.chrBankPtrs[7] = CHR_ROM + ((CHR1_Bankselect2k | 1) << 10);
            }
        }

        public byte MapperR_CHR(int address)
        {

            if (CHR_ROM_count == 0) return ppu_ram[address];

            if (CHR_Bankmode == 0) //0: two 2 KB banks at $0000-$0FFF,four 1 KB banks at $1000-$1FFF; ok
            {
                if (address < 0x1000)//2k * 2
                {
                    if (address < 0x400) return CHR_ROM[address + ((CHR0_Bankselect2k & 0xfe) << 10)];
                    else if (address < 0x800) return CHR_ROM[(address - 0x400) + ((CHR0_Bankselect2k | 1) << 10)];
                    else if (address < 0xc00) return CHR_ROM[(address - 0x800) + ((CHR1_Bankselect2k & 0xfe) << 10)];
                    else return CHR_ROM[(address - 0xc00) + ((CHR1_Bankselect2k | 1) << 10)];
                }
                else //1k *4
                {
                    if (address < 0x1400) return CHR_ROM[(address - 0x1000) + (CHR0_Bankselect1k << 10)];
                    else if (address < 0x1800) return CHR_ROM[(address - 0x1400) + (CHR1_Bankselect1k << 10)];
                    else if (address < 0x1c00) return CHR_ROM[(address - 0x1800) + (CHR2_Bankselect1k << 10)];
                    else return CHR_ROM[(address - 0x1c00) + (CHR3_Bankselect1k << 10)];
                }
            }
            else //1: two 2 KB banks at $1000-$1FFF,four 1 KB banks at $0000-$0FFF
            {
                if (address < 0x1000) //1k*4
                {
                    if (address < 0x400) return CHR_ROM[address + (CHR0_Bankselect1k << 10)];
                    else if (address < 0x800) return CHR_ROM[(address - 0x400) + (CHR1_Bankselect1k << 10)];
                    else if (address < 0xc00) return CHR_ROM[(address - 0x800) + (CHR2_Bankselect1k << 10)];
                    else return CHR_ROM[(address - 0xc00) + (CHR3_Bankselect1k << 10)];
                }
                else //2k * 2
                {
                    if (address < 0x1400) return CHR_ROM[(address - 0x1000) + ((CHR0_Bankselect2k & 0xfe) << 10)];
                    else if (address < 0x1800) return CHR_ROM[(address - 0x1400) + ((CHR0_Bankselect2k | 1) << 10)];
                    else if (address < 0x1c00) return CHR_ROM[(address - 0x1800) + ((CHR1_Bankselect2k & 0xfe) << 10)];
                    else return CHR_ROM[(address - 0x1c00) + ((CHR1_Bankselect2k | 1) << 10)];
                }
            }
        }
            public void Cleanup() { }
}
}

