namespace AprNes
{
    // Sunsoft FME-7 (Sunsoft 5B) — Batman (J/U), Mr. Gimmick, etc.
    // $8000 write: command select (bits 0-3)
    // $A000 write: execute command
    //   0-7:  1K CHR bank at PPU $0000/$0400/$0800/$0C00/$1000/$1400/$1800/$1C00
    //   8:    $6000-$7FFF control (bit6=0:ROM, bit6=1+bit7=1:WRAM, bit6=1+bit7=0:no-access)
    //   9:    8K PRG at $8000
    //   10:   8K PRG at $A000
    //   11:   8K PRG at $C000
    //   12:   mirroring (0=V, 1=H, 2=single-A, 3=single-B)
    //   13:   IRQ control (bit0=irqEnable, bit7=counterEnable, clears pending IRQ)
    //   14:   IRQ counter low byte
    //   15:   IRQ counter high byte
    // Last 8K PRG fixed at $E000. CPU-cycle counter IRQ.
    //
    // Audio: Sunsoft 5B (YM2149-compatible, 3 tone channels)
    //   $C000: audio register select
    //   $E000: audio register write
    //   Registers 0-5: channel A/B/C period (12-bit, low/high pairs)
    //   Register 6: noise period (unused by most games)
    //   Register 7: enable flags (bit0-2=tone disable, bit3-5=noise disable)
    //   Registers 8-10: channel A/B/C volume (4-bit, +1.5dB×2 per step)
    //   Registers 11-12: envelope period (not emulated)
    //   Register 13: envelope shape (not emulated)
    //   Clocked at CPU/2 (every other CPU cycle)
    unsafe public class Mapper069 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int CHR_ROM_count;
        int PRG_ROM_count;
        int* Vertical;

        int cmdReg;
        int[] chrBank = new int[8];
        int prgBank8000, prgBankA000, prgBankC000;
        byte wramCtrl;          // command 8 value: bit6=useRAM, bit7=ramWritable, bits0-5=ROM bank
        bool irqEnabled;        // bit0 of cmd 13
        bool counterEnabled;    // bit7 of cmd 13
        ushort irqCounter;

        // ── Sunsoft 5B audio (YM2149-compatible) ──────────────────────────
        byte audioRegSelect;
        byte[] audioRegs = new byte[16];
        short[] audioTimer = new short[3];
        byte[] toneStep = new byte[3];
        bool audioProcessTick;     // CPU/2 divider
        static byte[] volumeLut;   // logarithmic volume lookup (+1.5dB×2 per step)

        static Mapper069()
        {
            // Build logarithmic volume LUT: +1.5dB per step (×2 per step = ×1.1885^2)
            // Matches Mesen2 Sunsoft5bAudio constructor
            volumeLut = new byte[16];
            volumeLut[0] = 0;
            double output = 1.0;
            for (int i = 1; i < 16; i++)
            {
                output *= 1.1885022274370184377301224648922;
                output *= 1.1885022274370184377301224648922;
                volumeLut[i] = (byte)output;
            }
        }

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM;
            CHR_ROM = _CHR_ROM;
            ppu_ram = _ppu_ram;
            CHR_ROM_count = _CHR_ROM_count;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical = _Vertical;
        }

        public void Reset()
        {
            cmdReg = 0;
            for (int i = 0; i < 8; i++) chrBank[i] = 0;
            prgBank8000 = prgBankA000 = prgBankC000 = 0;
            wramCtrl = 0;
            irqEnabled = false;
            counterEnabled = false;
            irqCounter = 0;

            // Audio reset
            audioRegSelect = 0;
            for (int i = 0; i < 16; i++) audioRegs[i] = 0;
            for (int i = 0; i < 3; i++) { audioTimer[i] = 0; toneStep[i] = 0; }
            audioProcessTick = false;
            NesCore.mapperExpansionAudio = 0;
            NesCore.expansionChipType = NesCore.ExpansionChipType.Sunsoft5B;
            NesCore.expansionChannelCount = 3;
            NesCore.expansionChannels[0] = NesCore.expansionChannels[1] = NesCore.expansionChannels[2] = 0;
            NesCore.mmix_UpdateExpansionGain();
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }

        public void MapperW_RAM(ushort address, byte value)
        {
            // WRAM only if bit6=1 and bit7=1
            if ((wramCtrl & 0xC0) == 0xC0) NesCore.NES_MEM[address] = value;
        }

        public byte MapperR_RAM(ushort address)
        {
            if ((wramCtrl & 0x40) == 0)
            {
                // ROM mapped at $6000: select 8K bank from bits 0-5
                int bank = wramCtrl & 0x3F;
                int total8k = PRG_ROM_count * 2;
                bank = bank % total8k;
                return PRG_ROM[(address - 0x6000) + (bank << 13)];
            }
            if ((wramCtrl & 0x80) != 0)
                return NesCore.NES_MEM[address];  // WRAM readable
            return 0;  // bit6=1, bit7=0: no-access
        }

        public void MapperW_PRG(ushort address, byte value)
        {
            switch (address & 0xE000)
            {
                case 0x8000: cmdReg = value & 0x0F; break;        // command select
                case 0xA000: ExecuteCommand(value); break;          // command data
                case 0xC000: audioRegSelect = value; break;         // 5B audio register select
                case 0xE000:                                        // 5B audio register write
                    if (audioRegSelect <= 0x0F)
                        audioRegs[audioRegSelect] = value;
                    break;
            }
        }

        void ExecuteCommand(byte value)
        {
            if (cmdReg <= 7)
            {
                chrBank[cmdReg] = value;
                UpdateCHRBanks();
            }
            else if (cmdReg == 8)  { wramCtrl = value; }
            else if (cmdReg == 9)  { prgBank8000 = value & 0x3F; }
            else if (cmdReg == 10) { prgBankA000 = value & 0x3F; }
            else if (cmdReg == 11) { prgBankC000 = value & 0x3F; }
            else if (cmdReg == 12)
            {
                int m = value & 3;
                if      (m == 0) *Vertical = 1;  // vertical
                else if (m == 1) *Vertical = 0;  // horizontal
                else if (m == 2) *Vertical = 2;  // single screen A
                else             *Vertical = 3;  // single screen B
            }
            else if (cmdReg == 13)
            {
                irqEnabled  = (value & 0x01) != 0;
                counterEnabled = (value & 0x80) != 0;
                NesCore.statusmapperint = false;
                NesCore.UpdateIRQLine();
            }
            else if (cmdReg == 14) { irqCounter = (ushort)((irqCounter & 0xFF00) | value); }
            else if (cmdReg == 15) { irqCounter = (ushort)((irqCounter & 0x00FF) | (value << 8)); }
        }

        public byte MapperR_RPG(ushort address)
        {
            int total8k = PRG_ROM_count * 2;
            int bank;
            if (address < 0xA000)      bank = prgBank8000 % total8k;
            else if (address < 0xC000) bank = prgBankA000 % total8k;
            else if (address < 0xE000) bank = prgBankC000 % total8k;
            else                       bank = total8k - 1;  // last 8K fixed
            int pageBase = address & 0x1FFF;
            return PRG_ROM[pageBase + (bank << 13)];
        }

        public void UpdateCHRBanks()
        {
            int total1k = CHR_ROM_count * 8;
            for (int i = 0; i < 8; i++)
                NesCore.chrBankPtrs[i] = CHR_ROM + ((chrBank[i] % total1k) << 10);
        }

        public byte MapperR_CHR(int address)
        {
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void CpuCycle()
        {
            // IRQ
            if (counterEnabled)
            {
                irqCounter--;
                if (irqCounter == 0xFFFF && irqEnabled)
                {
                    NesCore.statusmapperint = true;
                    NesCore.UpdateIRQLine();
                }
            }

            // Sunsoft 5B audio — clocked at CPU/2
            if (audioProcessTick)
            {
                for (int ch = 0; ch < 3; ch++)
                {
                    audioTimer[ch]--;
                    if (audioTimer[ch] <= 0)
                    {
                        audioTimer[ch] = (short)(audioRegs[ch * 2] | (audioRegs[ch * 2 + 1] << 8));
                        toneStep[ch] = (byte)((toneStep[ch] + 1) & 0x0F);
                    }
                    // Tone enabled (bit=0 means enabled) and in high half of 16-step cycle
                    bool toneEnabled = ((audioRegs[7] >> ch) & 1) == 0;
                    NesCore.expansionChannels[ch] = (toneEnabled && toneStep[ch] < 8)
                        ? volumeLut[audioRegs[8 + ch] & 0x0F] : 0;
                }
            }
            audioProcessTick = !audioProcessTick;
        }

        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }
        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public MapperA12Mode A12NotifyMode => MapperA12Mode.None;
            public void Cleanup() { }
}
}
