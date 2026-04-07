using System;
using System.Runtime.CompilerServices;

namespace AprNes
{
    // JY Company — Mapper 090 / 209 / 211
    // Used by unlicensed games: Mortal Kombat 2 (Unl), Aladdin (Unl), etc.
    //
    // PRG: 4 modes (32K/16K/8K/8K-inverted), optional PRG-ROM at $6000
    // CHR: 4 modes (8K/4K/2K/1K), block mode, mirror-CHR
    // IRQ: 4 sources (CPU clock, PPU A12 rise, PPU read, CPU write)
    // Mirroring: V/H/single-A/single-B
    // Special: 8×8 multiply at $5800/$5801, 1-byte RAM at $5803
    //
    // Variant differences:
    //   090: Standard mirroring only, no CHR latch, no advanced NT
    //   209: Advanced NT control (when $D000 bit5 set), CHR latch (PPU address), NT→CHR ROM
    //   211: Always advanced NT control (ignores $D000 bit5), no CHR latch

    unsafe public class Mapper090 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;
        int chrRomSize;

        // Variant: 90, 209, or 211
        public int MapperVariant = 90;

        // PRG
        byte[] prgRegs = new byte[4];
        byte prgMode;
        bool enablePrgAt6000;

        // CHR
        byte[] chrLowRegs  = new byte[8];
        byte[] chrHighRegs = new byte[8];
        byte[] chrLatch = new byte[2]; // used by mapper 209 only
        byte chrMode;
        bool chrBlockMode;
        byte chrBlock;
        bool mirrorChr;

        // Mirroring
        byte mirroringReg;
        bool advancedNtControl;
        bool disableNtRam;

        // NT
        byte[] ntLowRegs  = new byte[4];
        byte[] ntHighRegs = new byte[4];
        byte ntRamSelectBit;

        // IRQ
        bool irqEnabled;
        byte irqSource;        // 0=CPU, 1=A12Rise, 2=PPURead, 3=CPUWrite
        byte irqCountDirection; // 0=off, 1=inc, 2=dec
        bool irqFunkyMode;
        byte irqFunkyModeReg;
        bool irqSmallPrescaler;
        byte irqPrescaler;
        byte irqCounter;
        byte irqXorReg;
        ushort lastPpuAddr;

        // Multiply & RAM
        byte multiplyValue1, multiplyValue2;
        byte regRamValue;

        // Whether advanced NT is effectively active
        bool UseAdvancedNT => MapperVariant != 90 && (advancedNtControl || MapperVariant == 211);

        public MapperA12Mode A12NotifyMode => MapperA12Mode.MMC3;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical;
            chrRomSize = _CHR_ROM_count * 0x2000;
        }

        public void Reset()
        {
            for (int i = 0; i < 4; i++) prgRegs[i] = 0;
            for (int i = 0; i < 8; i++) { chrLowRegs[i] = 0; chrHighRegs[i] = 0; }
            for (int i = 0; i < 4; i++) { ntLowRegs[i] = 0; ntHighRegs[i] = 0; }
            chrLatch[0] = 0; chrLatch[1] = 4;

            prgMode = 0; enablePrgAt6000 = false;
            chrMode = 0; chrBlockMode = false; chrBlock = 0; mirrorChr = false;
            mirroringReg = 0; advancedNtControl = false; disableNtRam = false; ntRamSelectBit = 0;

            irqEnabled = false; irqSource = 0; irqCountDirection = 0;
            irqFunkyMode = false; irqFunkyModeReg = 0; irqSmallPrescaler = false;
            irqPrescaler = 0; irqCounter = 0; irqXorReg = 0; lastPpuAddr = 0;

            multiplyValue1 = 0; multiplyValue2 = 0; regRamValue = 0;

            UpdateCHRBanks();
            UpdateMirroring();
        }

        // ── PRG Banking ──

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        byte InvertPrgBits(byte v)
        {
            return (byte)(
                ((v & 0x01) << 6) | ((v & 0x02) << 4) | ((v & 0x04) << 2) |
                ((v & 0x10) >> 2) | ((v & 0x20) >> 4) | ((v & 0x40) >> 6));
        }

        int GetPrgBank8k(int reg)
        {
            byte v = prgRegs[reg];
            if ((prgMode & 0x03) == 0x03) v = InvertPrgBits(v);
            return v;
        }

        public byte MapperR_RPG(ushort address)
        {
            int total8k = PRG_ROM_count * 2;
            int bank;

            switch (prgMode & 0x03)
            {
                case 0:
                {
                    int b = (prgMode & 0x04) != 0 ? GetPrgBank8k(3) : 0x3C;
                    bank = (b & ~3) | ((address >> 13) & 3);
                    break;
                }
                case 1:
                {
                    if (address < 0xC000)
                    {
                        int b = GetPrgBank8k(1) << 1;
                        bank = b | ((address >> 13) & 1);
                    }
                    else
                    {
                        int b = (prgMode & 0x04) != 0 ? GetPrgBank8k(3) : 0x3E;
                        bank = (b & ~1) | ((address >> 13) & 1);
                    }
                    break;
                }
                default:
                {
                    int slot = (address >> 13) & 3;
                    bank = (slot == 3 && (prgMode & 0x04) == 0) ? 0x3F : GetPrgBank8k(slot);
                    break;
                }
            }
            bank %= total8k;
            return PRG_ROM[(bank << 13) | (address & 0x1FFF)];
        }

        public byte MapperR_RAM(ushort address)
        {
            if (enablePrgAt6000)
            {
                int total8k = PRG_ROM_count * 2;
                int bank;
                switch (prgMode & 0x03)
                {
                    case 0: bank = GetPrgBank8k(3) * 4 + 3; break;
                    case 1: bank = GetPrgBank8k(3) * 2 + 1; break;
                    default: bank = GetPrgBank8k(3); break;
                }
                bank %= total8k;
                return PRG_ROM[(bank << 13) | (address & 0x1FFF)];
            }
            return NesCore.cpubus;
        }

        public void MapperW_RAM(ushort address, byte value) { }

        // ── CHR Banking ──

        int GetChrReg(int index)
        {
            if (chrMode >= 2 && mirrorChr && (index == 2 || index == 3))
                index -= 2;

            if (chrBlockMode)
            {
                int mask, shift;
                switch (chrMode)
                {
                    default:
                    case 0: mask = 0x1F; shift = 5; break;
                    case 1: mask = 0x3F; shift = 6; break;
                    case 2: mask = 0x7F; shift = 7; break;
                    case 3: mask = 0xFF; shift = 8; break;
                }
                return (chrLowRegs[index] & mask) | (chrBlock << shift);
            }
            else
            {
                return chrLowRegs[index] | (chrHighRegs[index] << 8);
            }
        }

        public void UpdateCHRBanks()
        {
            if (CHR_ROM_count == 0)
            {
                for (int i = 0; i < 8; i++) NesCore.chrBankPtrs[i] = ppu_ram + (i << 10);
                return;
            }

            int total1k = CHR_ROM_count * 8;

            switch (chrMode)
            {
                case 0: // 8K
                {
                    int b = (GetChrReg(0) << 3) % total1k;
                    for (int i = 0; i < 8; i++)
                        NesCore.chrBankPtrs[i] = CHR_ROM + (((b + i) % total1k) << 10);
                    break;
                }
                case 1: // 4K × 2 (mapper 209 uses chrLatch, others use fixed 0/4)
                {
                    int loIdx = (MapperVariant == 209) ? chrLatch[0] : 0;
                    int hiIdx = (MapperVariant == 209) ? chrLatch[1] : 4;
                    int lo = (GetChrReg(loIdx) << 2) % total1k;
                    int hi = (GetChrReg(hiIdx) << 2) % total1k;
                    for (int i = 0; i < 4; i++)
                    {
                        NesCore.chrBankPtrs[i]     = CHR_ROM + (((lo + i) % total1k) << 10);
                        NesCore.chrBankPtrs[i + 4] = CHR_ROM + (((hi + i) % total1k) << 10);
                    }
                    break;
                }
                case 2: // 2K × 4
                {
                    for (int p = 0; p < 4; p++)
                    {
                        int b = (GetChrReg(p * 2) << 1) % total1k;
                        NesCore.chrBankPtrs[p * 2]     = CHR_ROM + ((b % total1k) << 10);
                        NesCore.chrBankPtrs[p * 2 + 1] = CHR_ROM + (((b + 1) % total1k) << 10);
                    }
                    break;
                }
                case 3: // 1K × 8
                {
                    for (int i = 0; i < 8; i++)
                        NesCore.chrBankPtrs[i] = CHR_ROM + ((GetChrReg(i) % total1k) << 10);
                    break;
                }
            }
        }

        public byte MapperR_CHR(int address)
        {
            if (irqSource == 2)
                TickIrqCounter();

            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val)
        {
            if (CHR_ROM_count == 0) ppu_ram[addr] = val;
        }

        // ── Mirroring / Nametable ──

        void UpdateMirroring()
        {
            if (UseAdvancedNT)
            {
                // Advanced NT: each NT slot independently selects CIRAM page or CHR ROM
                NesCore.ntChrOverrideEnabled = true;
                for (int i = 0; i < 4; i++)
                {
                    if (disableNtRam || (ntLowRegs[i] & 0x80) != (ntRamSelectBit & 0x80))
                    {
                        // Map to CHR ROM
                        int chrPage = ntLowRegs[i] | (ntHighRegs[i] << 8);
                        int chrOffset = chrPage * 0x400;
                        if (chrOffset < chrRomSize)
                            NesCore.ntBankPtrs[i] = CHR_ROM + chrOffset;
                        else
                            NesCore.ntBankPtrs[i] = ppu_ram; // fallback
                    }
                    else
                    {
                        // Map to CIRAM (normal VRAM page based on low bit)
                        NesCore.ntBankPtrs[i] = ppu_ram + 0x2000 + (ntLowRegs[i] & 0x01) * 0x400;
                    }
                }
            }
            else
            {
                // Standard mirroring (mapper 090)
                NesCore.ntChrOverrideEnabled = false;
                switch (mirroringReg & 0x03)
                {
                    case 0: *Vertical = 1; break;
                    case 1: *Vertical = 0; break;
                    case 2: *Vertical = 2; break;
                    case 3: *Vertical = 3; break;
                }
            }
        }

        // ── Expansion ROM ($5000-$5FFF) ──

        public byte MapperR_ExpansionROM(ushort address)
        {
            switch (address & 0xF803)
            {
                case 0x5000: return 0;
                case 0x5800: return (byte)((multiplyValue1 * multiplyValue2) & 0xFF);
                case 0x5801: return (byte)(((multiplyValue1 * multiplyValue2) >> 8) & 0xFF);
                case 0x5803: return regRamValue;
            }
            return NesCore.cpubus;
        }

        public void MapperW_ExpansionROM(ushort address, byte value)
        {
            switch (address & 0xF803)
            {
                case 0x5800: multiplyValue1 = value; break;
                case 0x5801: multiplyValue2 = value; break;
                case 0x5803: regRamValue = value; break;
            }
        }

        // ── PRG Write ($8000-$FFFF) ──

        public void MapperW_PRG(ushort address, byte value)
        {
            switch (address & 0xF007)
            {
                case 0x8000: case 0x8001: case 0x8002: case 0x8003:
                case 0x8004: case 0x8005: case 0x8006: case 0x8007:
                    prgRegs[address & 0x03] = (byte)(value & 0x7F);
                    break;

                case 0x9000: case 0x9001: case 0x9002: case 0x9003:
                case 0x9004: case 0x9005: case 0x9006: case 0x9007:
                    chrLowRegs[address & 0x07] = value;
                    UpdateCHRBanks();
                    break;

                case 0xA000: case 0xA001: case 0xA002: case 0xA003:
                case 0xA004: case 0xA005: case 0xA006: case 0xA007:
                    chrHighRegs[address & 0x07] = value;
                    UpdateCHRBanks();
                    break;

                case 0xB000: case 0xB001: case 0xB002: case 0xB003:
                    ntLowRegs[address & 0x03] = value;
                    UpdateMirroring();
                    break;

                case 0xB004: case 0xB005: case 0xB006: case 0xB007:
                    ntHighRegs[address & 0x03] = value;
                    UpdateMirroring();
                    break;

                case 0xC000:
                    if ((value & 0x01) != 0)
                        irqEnabled = true;
                    else
                    {
                        irqEnabled = false;
                        NesCore.statusmapperint = false;
                        NesCore.UpdateIRQLine();
                    }
                    break;

                case 0xC001:
                    irqCountDirection = (byte)((value >> 6) & 0x03);
                    irqFunkyMode = (value & 0x08) != 0;
                    irqSmallPrescaler = (value & 0x04) != 0;
                    irqSource = (byte)(value & 0x03);
                    break;

                case 0xC002:
                    irqEnabled = false;
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    break;

                case 0xC003: irqEnabled = true; break;
                case 0xC004: irqPrescaler = (byte)(value ^ irqXorReg); break;
                case 0xC005: irqCounter = (byte)(value ^ irqXorReg); break;
                case 0xC006: irqXorReg = value; break;
                case 0xC007: irqFunkyModeReg = value; break;

                case 0xD000:
                    prgMode = (byte)(value & 0x07);
                    chrMode = (byte)((value >> 3) & 0x03);
                    advancedNtControl = (value & 0x20) != 0;
                    disableNtRam = (value & 0x40) != 0;
                    enablePrgAt6000 = (value & 0x80) != 0;
                    UpdateCHRBanks();
                    UpdateMirroring();
                    break;

                case 0xD001:
                    mirroringReg = (byte)(value & 0x03);
                    UpdateMirroring();
                    break;

                case 0xD002:
                    ntRamSelectBit = (byte)(value & 0x80);
                    UpdateMirroring();
                    break;

                case 0xD003:
                    mirrorChr = (value & 0x80) != 0;
                    chrBlockMode = (value & 0x20) == 0x00;
                    chrBlock = (byte)(((value & 0x18) >> 2) | (value & 0x01));
                    UpdateCHRBanks();
                    break;
            }
        }

        // ── IRQ ──

        void TickIrqCounter()
        {
            bool clockCounter = false;
            byte mask = irqSmallPrescaler ? (byte)0x07 : (byte)0xFF;
            byte prescaler = (byte)(irqPrescaler & mask);

            if (irqCountDirection == 0x01)
            {
                prescaler++;
                if ((prescaler & mask) == 0) clockCounter = true;
            }
            else if (irqCountDirection == 0x02)
            {
                prescaler--;
                if (prescaler == 0) clockCounter = true;
            }
            irqPrescaler = (byte)((irqPrescaler & ~mask) | (prescaler & mask));

            if (clockCounter)
            {
                if (irqCountDirection == 0x01)
                {
                    irqCounter++;
                    if (irqCounter == 0 && irqEnabled)
                    { NesCore.statusmapperint = true; NesCore.UpdateIRQLine(); }
                }
                else if (irqCountDirection == 0x02)
                {
                    irqCounter--;
                    if (irqCounter == 0xFF && irqEnabled)
                    { NesCore.statusmapperint = true; NesCore.UpdateIRQLine(); }
                }
            }
        }

        public void CpuCycle()
        {
            if (irqSource == 0) TickIrqCounter();
        }

        public void NotifyA12(int addr, int ppuAbsCycle)
        {
            // A12 rise IRQ
            if (irqSource == 1)
            {
                if ((addr & 0x1000) != 0 && (lastPpuAddr & 0x1000) == 0)
                    TickIrqCounter();
            }
            lastPpuAddr = (ushort)addr;

            // Mapper 209: CHR latch on specific PPU addresses (like MMC2)
            if (MapperVariant == 209)
            {
                switch (addr & 0x2FF8)
                {
                    case 0x0FD8:
                    case 0x0FE8:
                        chrLatch[addr >> 12] = (byte)((addr >> 4) & ((addr >> 10 & 0x04) | 0x02));
                        UpdateCHRBanks();
                        break;
                }
            }
        }

        public void PpuClock() { }

        public void CpuClockRise() { }
        public void Cleanup() { }
    }
}
