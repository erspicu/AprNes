namespace AprNes
{
    // Konami VRC6 — Mapper 024 (VRC6a), Mapper 026 (VRC6b)
    // PRG: 2×8K at $8000-$BFFF (16K bank via reg $8000), 1×8K at $C000-$DFFF, fixed last 8K
    // CHR: 8×1K (layout by $B003 bits[1:0]: 0=8×1K, 1=4×2K, 2/3=4×1K+2×2K)
    // IRQ: VRC prescaler (341, -3/cycle, counter $F000/$F001/$F002)
    // Audio: VRC6 expansion (Pulse1 $9000, Pulse2 $A000, Sawtooth $B000; halt $9003)
    //   NesCore.mapperExpansionAudio updated each CPU cycle
    // Mirroring: $B003 bits[5:4]+[2:0]; CHR-ROM nametable mode when bit4=1
    // WRAM: $6000-$7FFF, enabled by $B003 bit7
    //
    // VRC6a (mapper 024): standard address lines
    // VRC6b (mapper 026): bits 0 and 1 swapped; set IsVRC6b=true
    //
    // Games VRC6a: Akumajo Densetsu (Castlevania 3 JP), Madara (J)
    // Games VRC6b: Esper Dream 2 (J), Mouryou Senki Madara (J)

    unsafe public class Mapper024 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        // PRG
        int prgBank16k;    // bits[3:0]: 16K block at $8000-$BFFF (= 2 8K pages at bank*2, bank*2+1)
        int prgBank8k;     // bits[4:0]: 8K page at $C000-$DFFF

        // CHR
        byte[] chrRegs = new byte[8];
        byte bankingMode;    // $B003

        // IRQ (VRC style, same as VRC4/VRC7)
        int  irqPrescaler;
        byte irqCounter;
        byte irqReload;
        bool irqEnabled;
        bool irqEnabledAfterAck;
        bool irqCycleMode;

        // Audio: VRC6 Pulse channels (2)
        int[] pVol       = new int[2];
        int[] pDuty      = new int[2];
        bool[] pIgnDuty  = new bool[2];
        int[] pFreq      = new int[2];   // 12-bit period
        bool[] pEnable   = new bool[2];
        int[] pTimer     = new int[2];
        int[] pStep      = new int[2];   // 0-15
        int[] pFShift    = new int[2];   // 0, 4, or 8

        // Audio: VRC6 Sawtooth
        int  sawRate;        // 6-bit accumulator rate
        int  sawAccum;       // 8-bit
        int  sawFreq;        // 12-bit
        bool sawEnable;
        int  sawTimer;
        int  sawStep;        // 0-13 (14-step cycle)
        int  sawFShift;      // 0, 4, or 8

        bool haltAudio;

        public bool IsVRC6b = false;  // set true for Mapper026

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
            prgBank16k = prgBank8k = 0;
            for (int i = 0; i < 8; i++) chrRegs[i] = 0;
            bankingMode = 0;
            irqPrescaler = 341; irqCounter = irqReload = 0;
            irqEnabled = irqEnabledAfterAck = irqCycleMode = false;
            haltAudio = false;
            for (int i = 0; i < 2; i++)
            {
                pVol[i] = pDuty[i] = pFreq[i] = pTimer[i] = pStep[i] = pFShift[i] = 0;
                pEnable[i] = pIgnDuty[i] = false;
            }
            sawRate = sawAccum = sawFreq = sawTimer = sawStep = sawFShift = 0;
            sawEnable = false;
            NesCore.mapperExpansionAudio = 0;
            NesCore.expansionChipType = NesCore.ExpansionChipType.VRC6;
            NesCore.expansionChannelCount = 3;
            NesCore.expansionChannels[0] = NesCore.expansionChannels[1] = NesCore.expansionChannels[2] = 0;
            NesCore.mmix_UpdateChannelGains();
            UpdateCHRBanks();
        }

        int TranslateAddr(ushort addr)
        {
            if (IsVRC6b)
                return (addr & 0xFFFC) | ((addr & 0x01) << 1) | ((addr & 0x02) >> 1);
            return addr;
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }

        public byte MapperR_RAM(ushort address)
        {
            return (bankingMode & 0x80) != 0 ? NesCore.NES_MEM[address] : (byte)0;
        }
        public void MapperW_RAM(ushort address, byte value)
        {
            if ((bankingMode & 0x80) != 0) NesCore.NES_MEM[address] = value;
        }

        public void MapperW_PRG(ushort address, byte value)
        {
            int addr = TranslateAddr(address);
            switch (addr & 0xF003)
            {
                // PRG: 16K block at $8000-$BFFF
                case 0x8000: case 0x8001: case 0x8002: case 0x8003:
                    prgBank16k = value & 0x0F;
                    break;

                // Pulse 1
                case 0x9000:
                    pVol[0] = value & 0x0F;
                    pDuty[0] = (value >> 4) & 0x07;
                    pIgnDuty[0] = (value & 0x80) != 0;
                    break;
                case 0x9001:
                    pFreq[0] = (pFreq[0] & 0x0F00) | value;
                    break;
                case 0x9002:
                    pFreq[0] = (pFreq[0] & 0x00FF) | ((value & 0x0F) << 8);
                    pEnable[0] = (value & 0x80) != 0;
                    if (!pEnable[0]) pStep[0] = 0;
                    break;

                // Audio halt + global frequency shift
                case 0x9003:
                    haltAudio = (value & 0x01) != 0;
                    int sh = (value & 0x04) != 0 ? 8 : ((value & 0x02) != 0 ? 4 : 0);
                    pFShift[0] = pFShift[1] = sawFShift = sh;
                    break;

                // Pulse 2
                case 0xA000:
                    pVol[1] = value & 0x0F;
                    pDuty[1] = (value >> 4) & 0x07;
                    pIgnDuty[1] = (value & 0x80) != 0;
                    break;
                case 0xA001:
                    pFreq[1] = (pFreq[1] & 0x0F00) | value;
                    break;
                case 0xA002:
                    pFreq[1] = (pFreq[1] & 0x00FF) | ((value & 0x0F) << 8);
                    pEnable[1] = (value & 0x80) != 0;
                    if (!pEnable[1]) pStep[1] = 0;
                    break;

                // Sawtooth
                case 0xB000:
                    sawRate = value & 0x3F;
                    break;
                case 0xB001:
                    sawFreq = (sawFreq & 0x0F00) | value;
                    break;
                case 0xB002:
                    sawFreq = (sawFreq & 0x00FF) | ((value & 0x0F) << 8);
                    sawEnable = (value & 0x80) != 0;
                    if (!sawEnable) { sawAccum = 0; sawStep = 0; }
                    break;

                // Banking mode: CHR layout + mirroring + WRAM enable
                case 0xB003:
                    bankingMode = value;
                    UpdateMirroring();
                    UpdateCHRBanks();
                    break;

                // PRG 8K at $C000-$DFFF
                case 0xC000: case 0xC001: case 0xC002: case 0xC003:
                    prgBank8k = value & 0x1F;
                    break;

                // CHR banks 0-3
                case 0xD000: case 0xD001: case 0xD002: case 0xD003:
                    chrRegs[addr & 0x03] = value;
                    UpdateCHRBanks();
                    break;

                // CHR banks 4-7
                case 0xE000: case 0xE001: case 0xE002: case 0xE003:
                    chrRegs[4 + (addr & 0x03)] = value;
                    UpdateCHRBanks();
                    break;

                // IRQ
                case 0xF000: irqReload = value; break;
                case 0xF001:
                    irqEnabledAfterAck = (value & 0x01) != 0;
                    irqEnabled = (value & 0x02) != 0;
                    irqCycleMode = (value & 0x04) != 0;
                    if (irqEnabled) { irqCounter = irqReload; irqPrescaler = 341; }
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    break;
                case 0xF002:
                    irqEnabled = irqEnabledAfterAck;
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    break;
            }
        }

        unsafe void UpdateMirroring()
        {
            int bits = bankingMode & 0x2F;
            if ((bankingMode & 0x10) == 0)
            {
                // CIRAM mode
                switch (bits)
                {
                    case 0x20: case 0x27: *Vertical = 1; NesCore.ntChrOverrideEnabled = false; break;
                    case 0x23: case 0x24: *Vertical = 0; NesCore.ntChrOverrideEnabled = false; break;
                    case 0x28: case 0x2F: *Vertical = 2; NesCore.ntChrOverrideEnabled = false; break;
                    case 0x2B: case 0x2C: *Vertical = 3; NesCore.ntChrOverrideEnabled = false; break;
                    default:
                        // Per-nametable from CHR register bit0 — use ntBankPtrs pointing to ppu_ram
                        int m0 = bankingMode & 0x07;
                        if (m0 == 0 || m0 == 6 || m0 == 7)
                        {
                            // NT0/NT1 = chrRegs[6] bit0; NT2/NT3 = chrRegs[7] bit0
                            NesCore.ntBankPtrs[0] = NesCore.ntBankPtrs[1] = ppu_ram + ((chrRegs[6] & 1) << 10);
                            NesCore.ntBankPtrs[2] = NesCore.ntBankPtrs[3] = ppu_ram + ((chrRegs[7] & 1) << 10);
                        }
                        else if (m0 == 1 || m0 == 5)
                        {
                            // NT0=4, NT1=5, NT2=6, NT3=7
                            NesCore.ntBankPtrs[0] = ppu_ram + ((chrRegs[4] & 1) << 10);
                            NesCore.ntBankPtrs[1] = ppu_ram + ((chrRegs[5] & 1) << 10);
                            NesCore.ntBankPtrs[2] = ppu_ram + ((chrRegs[6] & 1) << 10);
                            NesCore.ntBankPtrs[3] = ppu_ram + ((chrRegs[7] & 1) << 10);
                        }
                        else // 2,3,4: NT0/NT2 = reg6, NT1/NT3 = reg7
                        {
                            NesCore.ntBankPtrs[0] = NesCore.ntBankPtrs[2] = ppu_ram + ((chrRegs[6] & 1) << 10);
                            NesCore.ntBankPtrs[1] = NesCore.ntBankPtrs[3] = ppu_ram + ((chrRegs[7] & 1) << 10);
                        }
                        NesCore.ntChrOverrideEnabled = true;
                        break;
                }
            }
            else
            {
                UpdateCHRNametables();
            }
        }

        unsafe void UpdateCHRNametables()
        {
            if (CHR_ROM_count == 0) { NesCore.ntChrOverrideEnabled = false; return; }
            int total1k = CHR_ROM_count * 8;
            int bits = bankingMode & 0x2F;
            byte* nt0, nt1, nt2, nt3;
            switch (bits)
            {
                case 0x20: case 0x27:
                    nt0 = CHR_ROM + ((chrRegs[6] & 0xFE) % total1k << 10);
                    nt1 = CHR_ROM + (((chrRegs[6] & 0xFE) | 1) % total1k << 10);
                    nt2 = CHR_ROM + ((chrRegs[7] & 0xFE) % total1k << 10);
                    nt3 = CHR_ROM + (((chrRegs[7] & 0xFE) | 1) % total1k << 10);
                    break;
                case 0x23: case 0x24:
                    nt0 = CHR_ROM + ((chrRegs[6] & 0xFE) % total1k << 10);
                    nt1 = CHR_ROM + ((chrRegs[7] & 0xFE) % total1k << 10);
                    nt2 = CHR_ROM + (((chrRegs[6] & 0xFE) | 1) % total1k << 10);
                    nt3 = CHR_ROM + (((chrRegs[7] & 0xFE) | 1) % total1k << 10);
                    break;
                case 0x28: case 0x2F:
                    nt0 = nt1 = CHR_ROM + ((chrRegs[6] & 0xFE) % total1k << 10);
                    nt2 = nt3 = CHR_ROM + ((chrRegs[7] & 0xFE) % total1k << 10);
                    break;
                case 0x2B: case 0x2C:
                    nt0 = nt1 = CHR_ROM + (((chrRegs[6] & 0xFE) | 1) % total1k << 10);
                    nt2 = nt3 = CHR_ROM + (((chrRegs[7] & 0xFE) | 1) % total1k << 10);
                    break;
                default:
                    int m0 = bankingMode & 0x07;
                    if (m0 == 0 || m0 == 6 || m0 == 7)
                    {
                        nt0 = nt1 = CHR_ROM + (chrRegs[6] % total1k << 10);
                        nt2 = nt3 = CHR_ROM + (chrRegs[7] % total1k << 10);
                    }
                    else if (m0 == 1 || m0 == 5)
                    {
                        nt0 = CHR_ROM + (chrRegs[4] % total1k << 10);
                        nt1 = CHR_ROM + (chrRegs[5] % total1k << 10);
                        nt2 = CHR_ROM + (chrRegs[6] % total1k << 10);
                        nt3 = CHR_ROM + (chrRegs[7] % total1k << 10);
                    }
                    else
                    {
                        nt0 = nt2 = CHR_ROM + (chrRegs[6] % total1k << 10);
                        nt1 = nt3 = CHR_ROM + (chrRegs[7] % total1k << 10);
                    }
                    break;
            }
            NesCore.ntBankPtrs[0] = nt0; NesCore.ntBankPtrs[1] = nt1;
            NesCore.ntBankPtrs[2] = nt2; NesCore.ntBankPtrs[3] = nt3;
            NesCore.ntChrOverrideEnabled = true;
        }

        public byte MapperR_RPG(ushort address)
        {
            int n8k = PRG_ROM_count * 2;
            int b0 = (prgBank16k * 2) % n8k;
            int b1 = (prgBank16k * 2 + 1) % n8k;
            if (address < 0xA000) return PRG_ROM[(address - 0x8000) + b0 * 0x2000];
            if (address < 0xC000) return PRG_ROM[(address - 0xA000) + b1 * 0x2000];
            if (address < 0xE000) return PRG_ROM[(address - 0xC000) + (prgBank8k % n8k) * 0x2000];
            return PRG_ROM[(address - 0xE000) + (n8k - 1) * 0x2000];
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
                if ((bankingMode & 0x10) != 0) UpdateCHRNametables();
                return;
            }
            int total1k = CHR_ROM_count * 8;
            int mask   = (bankingMode & 0x20) != 0 ? 0xFE : 0xFF;
            int orMask = (bankingMode & 0x20) != 0 ? 1    : 0;

            switch (bankingMode & 0x03)
            {
                case 0:
                    for (int i = 0; i < 8; i++)
                        NesCore.chrBankPtrs[i] = CHR_ROM + ((chrRegs[i] % total1k) << 10);
                    break;
                case 1:
                    for (int i = 0; i < 4; i++)
                    {
                        NesCore.chrBankPtrs[i * 2]     = CHR_ROM + (((chrRegs[i] & mask) % total1k) << 10);
                        NesCore.chrBankPtrs[i * 2 + 1] = CHR_ROM + ((((chrRegs[i] & mask) | orMask) % total1k) << 10);
                    }
                    break;
                case 2: case 3:
                    for (int i = 0; i < 4; i++)
                        NesCore.chrBankPtrs[i] = CHR_ROM + ((chrRegs[i] % total1k) << 10);
                    NesCore.chrBankPtrs[4] = CHR_ROM + (((chrRegs[4] & mask) % total1k) << 10);
                    NesCore.chrBankPtrs[5] = CHR_ROM + ((((chrRegs[4] & mask) | orMask) % total1k) << 10);
                    NesCore.chrBankPtrs[6] = CHR_ROM + (((chrRegs[5] & mask) % total1k) << 10);
                    NesCore.chrBankPtrs[7] = CHR_ROM + ((((chrRegs[5] & mask) | orMask) % total1k) << 10);
                    break;
            }
            if ((bankingMode & 0x10) != 0) UpdateCHRNametables();
        }

        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }
        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }

        public void CpuCycle()
        {
            // IRQ
            if (irqEnabled)
            {
                if (irqCycleMode)
                {
                    // Cycle mode: tick counter every CPU cycle
                    if (irqCounter == 0xFF)
                    {
                        irqCounter = irqReload;
                        NesCore.statusmapperint = true;
                        NesCore.UpdateIRQLine();
                    }
                    else irqCounter++;
                }
                else
                {
                    // Scanline mode: prescaler divides CPU cycles (~341/3 per scanline)
                    irqPrescaler -= 3;
                    if (irqPrescaler <= 0)
                    {
                        if (irqCounter == 0xFF)
                        {
                            irqCounter = irqReload;
                            NesCore.statusmapperint = true;
                            NesCore.UpdateIRQLine();
                        }
                        else irqCounter++;
                        irqPrescaler += 341;
                    }
                }
            }

            // Audio: Pulse channels
            if (!haltAudio)
            {
                for (int i = 0; i < 2; i++)
                {
                    if (pEnable[i])
                    {
                        if (--pTimer[i] <= 0)
                        {
                            pStep[i] = (pStep[i] + 1) & 0x0F;
                            pTimer[i] = (pFreq[i] >> pFShift[i]) + 1;
                        }
                    }
                }

                // Sawtooth
                if (sawEnable)
                {
                    if (--sawTimer <= 0)
                    {
                        sawStep = (sawStep + 1) % 14;
                        sawTimer = (sawFreq >> sawFShift) + 1;
                        if (sawStep == 0) sawAccum = 0;
                        else if ((sawStep & 1) == 0) sawAccum = (sawAccum + sawRate) & 0xFF;
                    }
                }
            }

            // Update expansion audio — per-channel raw output
            int p0out = (pEnable[0] && !haltAudio) ? (pIgnDuty[0] ? pVol[0] : (pStep[0] <= pDuty[0] ? pVol[0] : 0)) : 0;
            int p1out = (pEnable[1] && !haltAudio) ? (pIgnDuty[1] ? pVol[1] : (pStep[1] <= pDuty[1] ? pVol[1] : 0)) : 0;
            int sawout = (sawEnable && !haltAudio) ? (sawAccum >> 3) : 0;
            NesCore.expansionChannels[0] = p0out;
            NesCore.expansionChannels[1] = p1out;
            NesCore.expansionChannels[2] = sawout;
        }

        public void NotifyA12(int addr, int ppuAbsCycle) { }
            public void Cleanup() { }
}
}
