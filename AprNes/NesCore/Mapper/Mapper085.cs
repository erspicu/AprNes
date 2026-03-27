namespace AprNes
{
    // Konami VRC7 — Mapper 085
    // PRG: 3×8K switchable ($8000/$A000/$C000) + fixed last 8K ($E000-$FFFF)
    // CHR: 8×1K banks
    // IRQ: VRC prescaler (341, -3/cycle, 8-bit counter counts up to 0xFF)
    // Audio: YM2413/OPLL FM synthesis ($9010/$9030) — 6 FM channels via emu2413
    // Control: $E000 (mirroring bits[1:0], WRAM enable bit7, audio mute bit6)
    //
    // Games: Lagrange Point (J)
    //
    // Address translation: if addr & 0x10 (and not $9010), swap: addr |= 0x08, addr &= ~0x10
    // Then decode by addr & 0xF038

    unsafe public class Mapper085 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        int[] prgBank = new int[3];   // 3×8K banks at $8000/$A000/$C000
        byte[] chrRegs = new byte[8]; // 8×1K CHR banks
        byte controlFlags;            // $E000

        // VRC IRQ (same as VRC4/VRC6)
        int  irqPrescaler;
        byte irqCounter;
        byte irqReload;
        bool irqEnabled;
        bool irqEnabledAfterAck;
        bool irqCycleMode;

        // OPLL FM synthesis audio
        Emu2413 opll;
        byte currentReg;
        short previousOutput;
        double clockTimer;
        bool audioMuted;

        // OPLL clock: 49716 Hz sample rate, clock = 49716 * 72
        const int OpllSampleRate = 49716;
        const int OpllClockRate = OpllSampleRate * 72;
        // NES NTSC master clock = 21477272 Hz, CPU clock = master/12 = 1789772.67 Hz
        // clockTimer counts down from (masterClock / opllSampleRate) CPU cycles per OPLL sample
        // = 1789772.67 / 49716 ≈ 36.0 CPU cycles per OPLL sample
        const double CpuCyclesPerOpllSample = 1789772.667 / OpllSampleRate;

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
            prgBank[0] = prgBank[1] = prgBank[2] = 0;
            for (int i = 0; i < 8; i++) chrRegs[i] = 0;
            controlFlags = 0;
            irqPrescaler = 341; irqCounter = irqReload = 0;
            irqEnabled = irqEnabledAfterAck = irqCycleMode = false;
            UpdateCHRBanks();
            UpdateMirroring();

            // Init OPLL
            opll = new Emu2413((uint)OpllClockRate, (uint)OpllSampleRate);
            currentReg = 0;
            previousOutput = 0;
            clockTimer = 0;
            audioMuted = false;
            NesCore.mapperExpansionAudio = 0;
        }

        static int TranslateAddr(int addr)
        {
            // VRC7 address translation: swap bits 3 and 4 for most registers
            if ((addr & 0x10) != 0 && (addr & 0xF010) != 0x9010)
            {
                addr |= 0x08;
                addr &= ~0x10;
            }
            return addr & 0xF038;
        }

        public byte MapperR_ExpansionROM(ushort address) { return NesCore.cpubus; }
        public void MapperW_ExpansionROM(ushort address, byte value) { }

        public byte MapperR_RAM(ushort address)
        {
            return (controlFlags & 0x80) != 0 ? NesCore.NES_MEM[address] : (byte)0;
        }
        public void MapperW_RAM(ushort address, byte value)
        {
            if ((controlFlags & 0x80) != 0) NesCore.NES_MEM[address] = value;
        }

        public void MapperW_PRG(ushort address, byte value)
        {
            switch (TranslateAddr(address))
            {
                case 0x8000: prgBank[0] = value & 0x3F; break;
                case 0x8008: prgBank[1] = value & 0x3F; break;
                case 0x9000: prgBank[2] = value & 0x3F; break;

                // OPLL audio registers
                case 0x9010:
                    if (!audioMuted) currentReg = value;
                    break;
                case 0x9030:
                    if (!audioMuted) opll.WriteReg(currentReg, value);
                    break;

                case 0xA000: chrRegs[0] = value; UpdateCHRBanks(); break;
                case 0xA008: chrRegs[1] = value; UpdateCHRBanks(); break;
                case 0xB000: chrRegs[2] = value; UpdateCHRBanks(); break;
                case 0xB008: chrRegs[3] = value; UpdateCHRBanks(); break;
                case 0xC000: chrRegs[4] = value; UpdateCHRBanks(); break;
                case 0xC008: chrRegs[5] = value; UpdateCHRBanks(); break;
                case 0xD000: chrRegs[6] = value; UpdateCHRBanks(); break;
                case 0xD008: chrRegs[7] = value; UpdateCHRBanks(); break;

                case 0xE000:
                    controlFlags = value;
                    audioMuted = (value & 0x40) != 0;
                    UpdateMirroring();
                    break;

                case 0xE008:
                    irqReload = value;
                    break;
                case 0xF000:
                    irqEnabledAfterAck = (value & 0x01) != 0;
                    irqEnabled = (value & 0x02) != 0;
                    irqCycleMode = (value & 0x04) != 0;
                    if (irqEnabled) { irqCounter = irqReload; irqPrescaler = 341; }
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    break;
                case 0xF008:
                    irqEnabled = irqEnabledAfterAck;
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    break;
            }
        }

        void UpdateMirroring()
        {
            switch (controlFlags & 0x03)
            {
                case 0: *Vertical = 1; break;  // Vertical
                case 1: *Vertical = 0; break;  // Horizontal
                case 2: *Vertical = 2; break;  // Single-A
                case 3: *Vertical = 3; break;  // Single-B
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
            if (CHR_ROM_count == 0)
            {
                // CHR-RAM: bank-switch into ppu_ram (8KB = 8 × 1K pages)
                for (int i = 0; i < 8; i++)
                    NesCore.chrBankPtrs[i] = ppu_ram + ((chrRegs[i] & 7) << 10);
                return;
            }
            int total1k = CHR_ROM_count * 8;
            for (int i = 0; i < 8; i++)
                NesCore.chrBankPtrs[i] = CHR_ROM + ((chrRegs[i] % total1k) << 10);
        }

        public byte MapperR_CHR(int address) { return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF]; }
        public void MapperW_CHR(int addr, byte val)
        {
            if (CHR_ROM_count == 0)
                NesCore.chrBankPtrs[(addr >> 10) & 7][addr & 0x3FF] = val;
        }

        public void CpuCycle()
        {
            // IRQ
            if (irqEnabled)
            {
                if (irqCycleMode)
                {
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

            // OPLL audio: generate samples at ~49716 Hz
            clockTimer--;
            if (clockTimer <= 0)
            {
                clockTimer += CpuCyclesPerOpllSample;
                short output = opll.Calc();
                // Delta-based output like Mesen2, scaled for AprNes mixer
                // OPLL output range is roughly ±4095*6/2 = ±12285
                // Scale to match other expansion audio levels
                // OPLL output range: ~±12285 (6 ch × ~2048)
                // Scale to match NES APU range (~0..98302) and other expansion audio
                // VRC6 max ~28000, Namco163 ~15000; use factor 3 for comparable level
                NesCore.mapperExpansionAudio = audioMuted ? 0 : output * 3;
                previousOutput = output;
            }
        }

        public void NotifyA12(int addr, int ppuAbsCycle) { }
    }
}
