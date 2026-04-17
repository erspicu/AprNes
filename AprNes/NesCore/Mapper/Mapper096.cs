namespace AprNes
{
    // Mapper 096 — Bandai Oeka Kids (麵包超人繪圖板)
    //
    // PRG: 32KB switchable at $8000-$FFFF.
    // CHR: 2 × 4KB banks
    //   Slot 0 ($0000-$0FFF): outerChrBank | innerChrBank
    //   Slot 1 ($1000-$1FFF): outerChrBank | 0x03 (fixed to 3)
    //
    // Bus-driven inner-bank latch: whenever PPU bus address transitions from
    // non-NT ($0000-$1FFF or $3000+) to NT ($2000-$2FFF), innerChrBank is
    // updated to bits 8-9 of the new address. This is the Oeka Kids tablet
    // "scanline-aware" CHR bank selection mechanism.
    //
    // Register write ($8000-$FFFF, bus-conflict):
    //   value & 3 → PRG bank (0..3)
    //   value & 4 → outerChrBank bit 2 (0x04 shift)
    //
    // Ref: Mesen2/Bandai/OekaKids.h
    unsafe public class Mapper096 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int prgBank;
        byte outerChrBank;   // 0 or 4 (bit 2)
        byte innerChrBank;   // 0..3
        int lastAddress;

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
            UpdateCHRBanks();
        }

        public void Reset()
        {
            prgBank = 0;
            outerChrBank = 0;
            innerChrBank = 0;
            lastAddress = 0;
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }
        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }

        public void MapperW_PRG(ushort address, byte value)
        {
            // $8000-$FFFF: any write selects PRG + outer CHR bank (bus-conflict ignored)
            prgBank = value & 3;
            outerChrBank = (byte)(value & 4);
            UpdateCHRBanks();
        }

        public byte MapperR_RPG(ushort address)
        {
            int total32k = PRG_ROM_count / 2;
            if (total32k < 1) total32k = 1;
            int bank = prgBank % total32k;
            return PRG_ROM[(address - 0x8000) + (bank << 15)];
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
                return;
            }
            // Each 4KB slot needs 4 × 1KB pointers in chrBankPtrs[]
            int total4k = CHR_ROM_count * 2;
            int slot0_4k = (outerChrBank | innerChrBank) % total4k;
            int slot1_4k = (outerChrBank | 0x03) % total4k;
            byte* b0 = CHR_ROM + (slot0_4k << 12);
            byte* b1 = CHR_ROM + (slot1_4k << 12);
            NesCore.chrBankPtrs[0] = b0;
            NesCore.chrBankPtrs[1] = b0 + 1024;
            NesCore.chrBankPtrs[2] = b0 + 2048;
            NesCore.chrBankPtrs[3] = b0 + 3072;
            NesCore.chrBankPtrs[4] = b1;
            NesCore.chrBankPtrs[5] = b1 + 1024;
            NesCore.chrBankPtrs[6] = b1 + 2048;
            NesCore.chrBankPtrs[7] = b1 + 3072;
        }

        public byte MapperR_CHR(int address)
        {
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void CpuCycle() { }
        public void CpuClockRise() { }
        public void NotifyA12(int addr, int ppuAbsCycle) { }

        // VRAM-address hook: called every PPU dot. Detect transitions from
        // non-NT to NT ($2xxx) and latch the new inner CHR bank from bits 8-9.
        public void PpuClock()
        {
            int addr = NesCore.ppuAddressBus;
            if ((lastAddress & 0x3000) != 0x2000 && (addr & 0x3000) == 0x2000)
            {
                byte newInner = (byte)((addr >> 8) & 3);
                if (newInner != innerChrBank)
                {
                    innerChrBank = newInner;
                    UpdateCHRBanks();
                }
            }
            lastAddress = addr;
        }

        public void Cleanup() { }
    }
}
