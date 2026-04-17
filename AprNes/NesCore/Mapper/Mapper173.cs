namespace AprNes
{
    // Mapper 173 — TXC 22211C (Chinese educational / mahjong pirate carts)
    //
    // PRG: 32KB fixed at bank 0 (variant C always selects bank 0 regardless of TXC output).
    // CHR:
    //   If CHR ROM > 8KB: bank = (txc.output & 1) | (txc.y ? 2 : 0) | ((txc.output & 2) << 1)
    //   If CHR ROM <= 8KB:
    //     if txc.y is set → bank 0 enabled
    //     else → CHR disabled (Mesen2: RemovePpuMemoryMapping → all reads return open bus,
    //            approximated here by pointing banks to ppu_ram null-ish zone).
    //
    // TXC chip registers at $4100-$4103:
    //   $4100 (any, masked 0xE103): trigger accumulator update
    //   $4101 (masked 0xE103): bit 0 = invert flag
    //   $4102 (masked 0xE103): staging (low nibble) + inverter (high nibble)
    //   $4103 (masked 0xE103): bit 0 = increase flag
    //
    // PRG writes ($8000+) trigger TXC output update:
    //   output = (accumulator & 0x0F) | ((inverter & 0x08) << 1)
    //
    // Register reads at $4100-$5FFF (addr & 0x103) == 0x100:
    //   return (openBus & 0xF0) | (txc.Read() & 0x0F)
    //   txc.Read(): (accum & mask) | ((inv ^ (invert?0xFF:0)) & ~mask), sets yFlag
    //
    // mask = 0x07 (TXC22211), invert init = false (not JV-001).
    //
    // Ref: Mesen2 Txc22211C.h + Txc22211A.h + TxcChip.h
    unsafe public class Mapper173 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        // TXC chip state (non-JV-001 init: mask=0x07, invert=false)
        const byte MASK = 0x07;
        byte accumulator, inverter, staging, output;
        bool increase, yFlag, invert;

        // Mapper-specific
        int chrBank;
        bool chrEnabled = true;  // when false, reads return open bus approximation

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
            UpdateState();
        }

        public void Reset()
        {
            accumulator = inverter = staging = output = 0;
            increase = yFlag = invert = false;
            chrBank = 0;
            chrEnabled = true;
            UpdateState();
        }

        public byte MapperR_ExpansionROM(ushort address)
        {
            // $4100-$5FFF: (addr & 0x103) == 0x100 → return (openBus & 0xF0) | (txcRead & 0x0F)
            if ((address & 0x103) == 0x100)
            {
                byte v = TxcRead();
                UpdateState();
                return (byte)((NesCore.cpubus & 0xF0) | (v & 0x0F));
            }
            return NesCore.cpubus;
        }

        public void MapperW_ExpansionROM(ushort address, byte value)
        {
            // TXC registers at $4100-$4103 (masked 0xE103)
            if (address >= 0x4100 && address <= 0x5FFF)
            {
                TxcWrite(address, (byte)(value & 0x0F));
                UpdateState();
            }
        }

        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }

        public void MapperW_PRG(ushort address, byte value)
        {
            // $8000+: TXC output update (non-JV-001: output = accum.low4 | ((inv & 0x08) << 1))
            output = (byte)((accumulator & 0x0F) | ((inverter & 0x08) << 1));
            yFlag = !invert || ((value & 0x10) != 0);
            UpdateState();
        }

        public byte MapperR_RPG(ushort address)
        {
            // 22211C: PRG always bank 0 (32KB fixed)
            return PRG_ROM[(address - 0x8000) % (PRG_ROM_count * 0x4000)];
        }

        // TXC chip operations
        byte TxcRead()
        {
            byte value = (byte)((accumulator & MASK) | ((inverter ^ (invert ? 0xFF : 0)) & ~MASK));
            yFlag = !invert || ((value & 0x10) != 0);
            return value;
        }

        void TxcWrite(int addr, byte value)
        {
            switch (addr & 0xE103)
            {
                case 0x4100:
                    if (increase) accumulator++;
                    else accumulator = (byte)(((accumulator & ~MASK) | (staging & MASK)) ^ (invert ? 0xFF : 0));
                    break;
                case 0x4101:
                    invert = (value & 0x01) != 0;
                    break;
                case 0x4102:
                    staging  = (byte)(value & MASK);
                    inverter = (byte)(value & ~MASK);
                    break;
                case 0x4103:
                    increase = (value & 0x01) != 0;
                    break;
            }
            yFlag = !invert || ((value & 0x10) != 0);
        }

        void UpdateState()
        {
            // 22211C UpdateState — Mesen2 logic
            if (CHR_ROM_count > 1)  // CHR_ROM > 8KB (count is 8KB units)
            {
                chrBank = (output & 0x01) | (yFlag ? 0x02 : 0) | ((output & 0x02) << 1);
                chrEnabled = true;
            }
            else
            {
                if (yFlag)
                {
                    chrBank = 0;
                    chrEnabled = true;
                }
                else
                {
                    chrEnabled = false;
                }
            }
            UpdateCHRBanks();
        }

        public void UpdateCHRBanks()
        {
            if (!chrEnabled || CHR_ROM_count == 0)
            {
                // Point to ppu_ram low area (approximation of "open bus / disabled")
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
                return;
            }
            int total8k = CHR_ROM_count;
            int bank8k = chrBank % total8k;
            for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = CHR_ROM + (bank8k << 13) + (i << 10);
        }

        public byte MapperR_CHR(int address)
        {
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void CpuCycle() { }
        public void CpuClockRise() { }
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void PpuClock() { }
        public void Cleanup() { }
    }
}
