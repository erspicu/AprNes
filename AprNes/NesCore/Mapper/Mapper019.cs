using System.Runtime.CompilerServices;

namespace AprNes
{
    // Namco 163 (also called Namco 129/163) — Mapper 019
    // PRG: 4×8K; bank0=$E000[5:0], bank1=$E800[5:0], bank2=$F000[5:0], bank3=fixed last
    // CHR: 8×1K pattern table banks via $8000-$B800; values >=0xE0 map to CIRAM
    //      4×1K nametable banks via $C000-$D800; values >=0xE0 map to CIRAM, else CHR-ROM
    // IRQ: 15-bit up-counter at $5000(lo)/$5800(hi); fires at 0x7FFF; bit15=enable
    //      Clear IRQ by writing to $5000 or $5800
    // Audio: 128-byte internal RAM (r/w at $4800; address at $F800)
    //   Up to 8 waveform channels; each 8 bytes in RAM at offset 0x40+ch*8
    //   Updated round-robin every 15 CPU cycles
    //   Output: (sample−8)*volume, averaged across active channels
    //   NesCore.mapperExpansionAudio set each update
    //
    // Games: Splatterhouse (J), Erika to Satoru no Yume Bouken (J),
    //        Family Stadium '90, Megami Tensei II (J)

    unsafe public class Mapper019 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        // PRG banks (8K each; bank3 = fixed last)
        int[] prgBank = new int[3];

        // CHR pattern table regs ($8000-$B800, 8 regs, addr step 0x800)
        byte[] chrReg  = new byte[8];
        // Nametable regs ($C000-$D800, 4 regs, addr step 0x800)
        byte[] ntReg   = new byte[4];

        // Additional CHR nametable mode flags (from $E800)
        bool lowChrNtMode;   // bit6: regs 0-3 bypass CHR-ROM NT mode
        bool highChrNtMode;  // bit7: regs 4-7 bypass CHR-ROM NT mode

        // IRQ
        ushort irqCounter;   // bit15 = enable, bits[14:0] = counter

        // Audio internal RAM (128 bytes) and state
        byte[] audioRam  = new byte[128];
        byte   audioAddr = 0;     // current RAM position
        bool   audioAutoInc;      // auto-increment on read/write
        int    audioUpdateCtr;    // counts to 15
        int    audioChannel;      // current channel being updated (7 down to 7-numCh)
        short[] chOutput = new short[8]; // per-channel output (biased around 0)
        bool   audioDisable;

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
            for (int i = 0; i < 3; i++) prgBank[i] = 0;
            for (int i = 0; i < 8; i++) { chrReg[i] = 0; chOutput[i] = 0; }
            // Default nametable regs to CIRAM (>= 0xE0) based on mirroring
            // Vertical: NT0=CIRAM0, NT1=CIRAM1, NT2=CIRAM0, NT3=CIRAM1
            // Horizontal: NT0=CIRAM0, NT1=CIRAM0, NT2=CIRAM1, NT3=CIRAM1
            if (*Vertical == 1) { ntReg[0] = 0xE0; ntReg[1] = 0xE1; ntReg[2] = 0xE0; ntReg[3] = 0xE1; }
            else                { ntReg[0] = 0xE0; ntReg[1] = 0xE0; ntReg[2] = 0xE1; ntReg[3] = 0xE1; }
            lowChrNtMode = highChrNtMode = false;
            irqCounter = 0;
            for (int i = 0; i < 128; i++) audioRam[i] = 0;
            audioAddr = 0; audioAutoInc = false;
            audioUpdateCtr = 0; audioChannel = 7;
            audioDisable = false;
            NesCore.mapperExpansionAudio = 0;
            NesCore.expansionChipType = NesCore.ExpansionChipType.Namco163;
            NesCore.expansionChannelCount = 0; // updated dynamically by audio engine
            for (int i = 0; i < 8; i++) NesCore.expansionChannels[i] = 0;
            NesCore.mmix_UpdateChannelGains();
            UpdateCHRBanks();
        }

        // $4020-$5FFF expansion ROM read
        public byte MapperR_ExpansionROM(ushort address)
        {
            switch (address & 0xF800)
            {
                case 0x4800:
                {
                    byte v = audioRam[audioAddr];
                    if (audioAutoInc) audioAddr = (byte)((audioAddr + 1) & 0x7F);
                    return v;
                }
                case 0x5000: return (byte)(irqCounter & 0xFF);
                case 0x5800: return (byte)(irqCounter >> 8);
                default:     return NesCore.cpubus;
            }
        }

        // $4100-$5FFF expansion ROM write
        public void MapperW_ExpansionROM(ushort address, byte value)
        {
            switch (address & 0xF800)
            {
                case 0x4800:
                    audioRam[audioAddr] = value;
                    if (audioAutoInc) audioAddr = (byte)((audioAddr + 1) & 0x7F);
                    break;
                case 0x5000:
                    irqCounter = (ushort)((irqCounter & 0xFF00) | value);
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    break;
                case 0x5800:
                    irqCounter = (ushort)((irqCounter & 0x00FF) | (value << 8));
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    break;
            }
        }

        public byte MapperR_RAM(ushort address) { return NesCore.NES_MEM[address]; }
        public void MapperW_RAM(ushort address, byte value) { NesCore.NES_MEM[address] = value; }

        public void MapperW_PRG(ushort address, byte value)
        {
            switch (address & 0xF800)
            {
                // CHR pattern table regs 0-3 ($8000-$9800)
                case 0x8000: case 0x8800: case 0x9000: case 0x9800:
                {
                    int idx = (address - 0x8000) >> 11;
                    chrReg[idx] = value;
                    UpdateCHRBanks();
                    break;
                }
                // CHR pattern table regs 4-7 ($A000-$B800)
                case 0xA000: case 0xA800: case 0xB000: case 0xB800:
                {
                    int idx = ((address - 0xA000) >> 11) + 4;
                    chrReg[idx] = value;
                    UpdateCHRBanks();
                    break;
                }
                // Nametable regs 0-3 ($C000-$D800)
                case 0xC000: case 0xC800: case 0xD000: case 0xD800:
                {
                    int idx = (address - 0xC000) >> 11;
                    ntReg[idx] = value;
                    UpdateNametables();
                    break;
                }
                // PRG bank 0 + audio disable
                case 0xE000:
                    prgBank[0] = value & 0x3F;
                    audioDisable = (value & 0x40) != 0;
                    break;
                // PRG bank 1 + CHR NT mode bits
                case 0xE800:
                    prgBank[1] = value & 0x3F;
                    lowChrNtMode  = (value & 0x40) != 0;
                    highChrNtMode = (value & 0x80) != 0;
                    UpdateCHRBanks();
                    break;
                // PRG bank 2
                case 0xF000:
                    prgBank[2] = value & 0x3F;
                    break;
                // Audio RAM address selector
                case 0xF800:
                    audioAddr    = (byte)(value & 0x7F);
                    audioAutoInc = (value & 0x80) != 0;
                    break;
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            int n8k = PRG_ROM_count * 2;
            if (address < 0xA000) return PRG_ROM[(address - 0x8000) + (prgBank[0] % n8k) * 0x2000];
            if (address < 0xC000) return PRG_ROM[(address - 0xA000) + (prgBank[1] % n8k) * 0x2000];
            if (address < 0xE000) return PRG_ROM[(address - 0xC000) + (prgBank[2] % n8k) * 0x2000];
            return PRG_ROM[(address - 0xE000) + (n8k - 1) * 0x2000];
        }

        public void UpdateCHRBanks()
        {
            int total1k = CHR_ROM_count > 0 ? CHR_ROM_count * 8 : 1;
            // Pattern table slots 0-3 (PPU $0000-$0FFF)
            for (int i = 0; i < 4; i++)
            {
                if (!lowChrNtMode && chrReg[i] >= 0xE0 && CHR_ROM_count > 0)
                    NesCore.chrBankPtrs[i] = ppu_ram + 0x2000 + ((chrReg[i] & 0x01) << 10);
                else if (CHR_ROM_count > 0)
                    NesCore.chrBankPtrs[i] = CHR_ROM + ((chrReg[i] % total1k) << 10);
                else
                    NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
            }
            // Pattern table slots 4-7 (PPU $1000-$1FFF)
            for (int i = 4; i < 8; i++)
            {
                if (!highChrNtMode && chrReg[i] >= 0xE0 && CHR_ROM_count > 0)
                    NesCore.chrBankPtrs[i] = ppu_ram + 0x2000 + ((chrReg[i] & 0x01) << 10);
                else if (CHR_ROM_count > 0)
                    NesCore.chrBankPtrs[i] = CHR_ROM + ((chrReg[i] % total1k) << 10);
                else
                    NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
            }
            UpdateNametables();
        }

        void UpdateNametables()
        {
            int total1k = CHR_ROM_count > 0 ? CHR_ROM_count * 8 : 1;
            for (int i = 0; i < 4; i++)
            {
                if (ntReg[i] >= 0xE0)
                {
                    NesCore.ntBankPtrs[i] = ppu_ram + 0x2000 + ((ntReg[i] & 0x01) << 10);
                    NesCore.ntBankWritable[i] = true;
                }
                else if (CHR_ROM_count > 0)
                {
                    NesCore.ntBankPtrs[i] = CHR_ROM + ((ntReg[i] % total1k) << 10);
                    NesCore.ntBankWritable[i] = false; // CHR-ROM is read-only
                }
                else
                {
                    NesCore.ntBankPtrs[i] = ppu_ram + (i << 10);
                    NesCore.ntBankWritable[i] = true;
                }
            }
            NesCore.ntChrOverrideEnabled = true;
        }

        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }
        public void MapperW_CHR(int addr, byte val) { if (CHR_ROM_count == 0) ppu_ram[addr] = val; }

        int GetNumChannels() { return (audioRam[0x7F] >> 4) & 0x07; }

        void UpdateAudioChannel(int ch)
        {
            int baseAddr = 0x40 + ch * 8;
            // 18-bit frequency
            uint freq = (uint)(((audioRam[baseAddr + 4] & 0x03) << 16) |
                                (audioRam[baseAddr + 2] << 8) |
                                 audioRam[baseAddr + 0]);
            // 24-bit phase
            uint phase = (uint)((audioRam[baseAddr + 5] << 16) |
                                (audioRam[baseAddr + 3] << 8) |
                                 audioRam[baseAddr + 1]);
            // Wave length = 256 - (reg4 & 0xFC), as 16-step units
            int waveLength = 256 - (audioRam[baseAddr + 4] & 0xFC);
            byte waveAddr  = audioRam[baseAddr + 6];
            byte volume    = (byte)(audioRam[baseAddr + 7] & 0x0F);

            phase = (phase + freq) % ((uint)waveLength << 16);

            // Store updated phase back
            audioRam[baseAddr + 5] = (byte)((phase >> 16) & 0xFF);
            audioRam[baseAddr + 3] = (byte)((phase >> 8)  & 0xFF);
            audioRam[baseAddr + 1] = (byte)(phase & 0xFF);

            // Read sample nibble
            int samplePos = (int)(((phase >> 16) + waveAddr) & 0xFF);
            int sample;
            if ((samplePos & 1) != 0) sample = audioRam[samplePos / 2] >> 4;
            else                       sample = audioRam[samplePos / 2] & 0x0F;

            chOutput[ch] = (short)((sample - 8) * volume);

            // Update per-channel expansion output
            // N163 hardware: more active channels = each channel gets less time = quieter
            // Divide each channel by (numCh+1) to match averaging behavior
            int numCh = GetNumChannels();
            int activeCh = numCh + 1; // GetNumChannels() returns 0-based count
            NesCore.expansionChannelCount = activeCh;
            for (int i = 0; i < activeCh; i++)
                NesCore.expansionChannels[i] = chOutput[7 - i] / activeCh;
        }

        public void CpuCycle()
        {
            // IRQ: 15-bit up-counter; fires when it reaches 0x7FFF
            if ((irqCounter & 0x8000) != 0 && (irqCounter & 0x7FFF) != 0x7FFF)
            {
                irqCounter++;
                if ((irqCounter & 0x7FFF) == 0x7FFF)
                {
                    NesCore.statusmapperint = true;
                    NesCore.UpdateIRQLine();
                }
            }

            // Audio: update one channel every 15 CPU cycles
            if (!audioDisable)
            {
                audioUpdateCtr++;
                if (audioUpdateCtr >= 15)
                {
                    audioUpdateCtr = 0;
                    UpdateAudioChannel(audioChannel);
                    int minCh = 7 - GetNumChannels();
                    audioChannel--;
                    if (audioChannel < minCh) audioChannel = 7;
                }
            }
        }

        public void NotifyA12(int addr, int ppuAbsCycle) { }
        public void PpuClock() { }
        public void CpuClockRise() { }
            public void Cleanup() { }
}
}
