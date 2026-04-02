using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace AprNes
{
    unsafe public class Mapper005 : IMapper
    {
        // MMC5 (Nintendo ExROM) — https://wiki.nesdev.com/w/index.php/MMC5
        //
        // Features implemented:
        //   - PRG banking (4 modes), PRG-RAM (64KB), $6000-$7FFF banking
        //   - CHR banking (4 modes), A/B set switching for 8x16 sprites
        //   - Nametable mapping ($5105): CIRAM / ExRAM / Fill-mode
        //   - Scanline IRQ via 3-consecutive-NT-read detection (Mesen2 model)
        //   - 8×8 hardware multiplier ($5205/$5206)
        //   - Expansion RAM ($5C00-$5FFF, modes 0-3)
        //   - NMI vector read ($FFFA-$FFFB) resets frame state
        //
        // Not yet implemented:
        //   - Extended attribute mode (ExRAM mode 1 attribute replacement)
        //   - Vertical split mode ($5200-$5202)
        //   - MMC5 audio (2 pulse + PCM)

        byte* PRG_ROM, CHR_ROM, ppu_ram;
        int CHR_ROM_count, PRG_ROM_count;
        int* Vertical;
        int prgRomSize, chrRomSize;

        // ── PRG banking ────────────────────────────────────────────────
        int prgMode;
        byte[] prgBanks = new byte[5]; // [0]=$5113, [1]=$5114, [2]=$5115, [3]=$5116, [4]=$5117

        // ── CHR banking ────────────────────────────────────────────────
        int chrMode;
        ushort[] chrBanks = new ushort[12]; // [0-7]=A set ($5120-$5127), [8-11]=B set ($5128-$512B)
        byte chrUpperBits;
        ushort lastChrReg;  // last CHR register written ($5120-$512B); determines A/B set for BG

        // ── PRG-RAM (64KB) ─────────────────────────────────────────────
        byte[] prgRam = new byte[0x10000];

        // ── Expansion RAM + Fill nametable (unmanaged for ntBankPtrs) ──
        byte* exRamPtr;
        byte* fillNTPtr;
        byte extendedRamMode;

        // ── Nametable mapping ($5105) ──────────────────────────────────
        byte nametableMapping;
        byte fillModeTile;
        byte fillModeColor;

        // ── PRG-RAM protect ($5102/$5103) ──────────────────────────────
        byte prgRamProtect1, prgRamProtect2;

        // ── Multiplier ($5205/$5206) ───────────────────────────────────
        byte multiplierValue1, multiplierValue2;

        // ── Scanline IRQ ($5203/$5204) — Mesen2-style 3-consecutive-NT-read detection ──
        byte irqCounterTarget;
        bool irqEnabled;
        int scanlineCounter;
        bool irqPending;
        bool ppuInFrame;
        bool needInFrame;          // first 3-identical-reads detected; promotes to ppuInFrame on next NT fetch
        byte ppuIdleCounter;       // 3 CPU cycles without PPU read → ppuInFrame=false
        ushort lastPpuReadAddr;    // last VRAM address read by PPU (for consecutive detection)
        byte ntReadCounter;        // consecutive identical NT address read counter
        int splitTileNumber;       // tile counter within scanline (0-31=BG, 32-39=sprites, 40-41=prefetch)
        bool prevChrA;             // previous chrA state (optimization: skip redundant bank updates)

        // ================================================================
        //  Init
        // ================================================================

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram,
            int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM;
            CHR_ROM = _CHR_ROM;
            ppu_ram = _ppu_ram;
            CHR_ROM_count = _CHR_ROM_count;
            PRG_ROM_count = _PRG_ROM_count;
            Vertical = _Vertical;

            prgRomSize = PRG_ROM_count * 0x4000;
            chrRomSize = CHR_ROM_count * 0x2000;

            // Allocate unmanaged memory for ExRAM (1KB) and fill nametable (1KB)
            // Free any previous allocation to prevent leak on repeated MapperInit calls
            if (exRamPtr != null) Marshal.FreeHGlobal((IntPtr)exRamPtr);
            if (fillNTPtr != null) Marshal.FreeHGlobal((IntPtr)fillNTPtr);
            exRamPtr = (byte*)Marshal.AllocHGlobal(1024);
            fillNTPtr = (byte*)Marshal.AllocHGlobal(1024);
            for (int i = 0; i < 1024; i++) { exRamPtr[i] = 0; fillNTPtr[i] = 0; }

            // Power-on defaults (per Mesen2/NESdev wiki)
            prgMode = 3;
            prgBanks[4] = 0xFF; // $5117 = last bank

            chrMode = 0;
            chrUpperBits = 0;
            lastChrReg = 0;
            extendedRamMode = 0;
            nametableMapping = 0;
            fillModeTile = 0;
            fillModeColor = 0;
            prgRamProtect1 = 0;
            prgRamProtect2 = 0;
            multiplierValue1 = 0;
            multiplierValue2 = 0;
            irqCounterTarget = 0;
            irqEnabled = false;
            scanlineCounter = 0;
            irqPending = false;
            ppuInFrame = false;
            needInFrame = false;
            ppuIdleCounter = 0;
            lastPpuReadAddr = 0;
            ntReadCounter = 0;
            splitTileNumber = 0;
            prevChrA = false;

            NesCore.extAttrEnabled = false;
            NesCore.chrABAutoSwitch = false; // MMC5 handles CHR A/B switching via NotifyVramRead
            NesCore.mmc5Ref = this;          // PPU calls NotifyVramRead directly

            UpdateNametableMapping();
            UpdateChrBanks(true); // initial CHR bank setup
        }

        // ================================================================
        //  PRG helpers
        // ================================================================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int PrgRomAddr(int bank8k, int localOffset)
        {
            if (prgRomSize == 0) return 0;
            return ((bank8k << 13) | localOffset) % prgRomSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int PrgRamAddr(int bank, int localOffset)
        {
            return (((bank & 0x07) << 13) | localOffset) & 0xFFFF;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsRamWritable()
        {
            return prgRamProtect1 == 0x02 && prgRamProtect2 == 0x01;
        }

        // ================================================================
        //  PRG read ($8000-$FFFF)
        // ================================================================

        public byte MapperR_RPG(ushort address)
        {
            // NMI vector read resets frame state (Mesen2: ReadRegister $FFFA/$FFFB)
            if (address >= 0xFFFA && address <= 0xFFFB)
            {
                ppuInFrame = false;
                needInFrame = false;
                lastPpuReadAddr = 0;
                ntReadCounter = 0;
                scanlineCounter = 0;
                irqPending = false;
                NesCore.statusmapperint = false;
                NesCore.UpdateIRQLine();
                UpdateChrBanks(true);
            }

            int offset;
            switch (prgMode)
            {
                case 0: // 32KB: $5117 (ignore bottom 2 bits)
                    offset = address - 0x8000;
                    return PRG_ROM[PrgRomAddr(prgBanks[4] & 0x7C, offset)];

                case 1: // 16KB + 16KB
                    if (address < 0xC000)
                    {
                        offset = address - 0x8000;
                        if ((prgBanks[2] & 0x80) == 0)
                            return prgRam[PrgRamAddr(prgBanks[2], offset & 0x3FFF)];
                        return PRG_ROM[PrgRomAddr(prgBanks[2] & 0x7E, offset)];
                    }
                    else
                    {
                        offset = address - 0xC000;
                        return PRG_ROM[PrgRomAddr(prgBanks[4] & 0x7E, offset)];
                    }

                case 2: // 16KB + 8KB + 8KB
                    if (address < 0xC000)
                    {
                        offset = address - 0x8000;
                        if ((prgBanks[2] & 0x80) == 0)
                            return prgRam[PrgRamAddr(prgBanks[2], offset & 0x3FFF)];
                        return PRG_ROM[PrgRomAddr(prgBanks[2] & 0x7E, offset)];
                    }
                    else if (address < 0xE000)
                    {
                        offset = address - 0xC000;
                        if ((prgBanks[3] & 0x80) == 0)
                            return prgRam[PrgRamAddr(prgBanks[3], offset)];
                        return PRG_ROM[PrgRomAddr(prgBanks[3] & 0x7F, offset)];
                    }
                    else
                    {
                        offset = address - 0xE000;
                        return PRG_ROM[PrgRomAddr(prgBanks[4] & 0x7F, offset)];
                    }

                default: // mode 3: 4× 8KB
                    if (address < 0xA000)
                    {
                        offset = address - 0x8000;
                        if ((prgBanks[1] & 0x80) == 0)
                            return prgRam[PrgRamAddr(prgBanks[1], offset)];
                        return PRG_ROM[PrgRomAddr(prgBanks[1] & 0x7F, offset)];
                    }
                    else if (address < 0xC000)
                    {
                        offset = address - 0xA000;
                        if ((prgBanks[2] & 0x80) == 0)
                            return prgRam[PrgRamAddr(prgBanks[2], offset)];
                        return PRG_ROM[PrgRomAddr(prgBanks[2] & 0x7F, offset)];
                    }
                    else if (address < 0xE000)
                    {
                        offset = address - 0xC000;
                        if ((prgBanks[3] & 0x80) == 0)
                            return prgRam[PrgRamAddr(prgBanks[3], offset)];
                        return PRG_ROM[PrgRomAddr(prgBanks[3] & 0x7F, offset)];
                    }
                    else
                    {
                        offset = address - 0xE000;
                        return PRG_ROM[PrgRomAddr(prgBanks[4] & 0x7F, offset)];
                    }
            }
        }

        // ================================================================
        //  PRG write ($8000-$FFFF) — RAM-mapped banks only
        // ================================================================

        public void MapperW_PRG(ushort address, byte value)
        {
            int offset;
            switch (prgMode)
            {
                case 0: break;

                case 1:
                    if (address < 0xC000 && (prgBanks[2] & 0x80) == 0 && IsRamWritable())
                    {
                        offset = (address - 0x8000) & 0x3FFF;
                        prgRam[PrgRamAddr(prgBanks[2], offset)] = value;
                    }
                    break;

                case 2:
                    if (address < 0xC000 && (prgBanks[2] & 0x80) == 0 && IsRamWritable())
                    {
                        offset = (address - 0x8000) & 0x3FFF;
                        prgRam[PrgRamAddr(prgBanks[2], offset)] = value;
                    }
                    else if (address >= 0xC000 && address < 0xE000 && (prgBanks[3] & 0x80) == 0 && IsRamWritable())
                    {
                        offset = address - 0xC000;
                        prgRam[PrgRamAddr(prgBanks[3], offset)] = value;
                    }
                    break;

                case 3:
                    if (address < 0xA000 && (prgBanks[1] & 0x80) == 0 && IsRamWritable())
                    {
                        offset = address - 0x8000;
                        prgRam[PrgRamAddr(prgBanks[1], offset)] = value;
                    }
                    else if (address >= 0xA000 && address < 0xC000 && (prgBanks[2] & 0x80) == 0 && IsRamWritable())
                    {
                        offset = address - 0xA000;
                        prgRam[PrgRamAddr(prgBanks[2], offset)] = value;
                    }
                    else if (address >= 0xC000 && address < 0xE000 && (prgBanks[3] & 0x80) == 0 && IsRamWritable())
                    {
                        offset = address - 0xC000;
                        prgRam[PrgRamAddr(prgBanks[3], offset)] = value;
                    }
                    break;
            }
        }

        // ================================================================
        //  SRAM / WRAM ($6000-$7FFF)
        // ================================================================

        public byte MapperR_RAM(ushort address)
        {
            int bank = prgBanks[0] & 0x07;
            return prgRam[(bank << 13) | (address & 0x1FFF)];
        }

        public void MapperW_RAM(ushort address, byte value)
        {
            int bank = prgBanks[0] & 0x07;
            prgRam[(bank << 13) | (address & 0x1FFF)] = value;
        }

        // ================================================================
        //  Expansion ROM read ($4100-$5FFF)
        // ================================================================

        public byte MapperR_ExpansionROM(ushort address)
        {
            // Expansion RAM ($5C00-$5FFF)
            if (address >= 0x5C00 && address <= 0x5FFF)
            {
                if (extendedRamMode >= 2)
                    return exRamPtr[address - 0x5C00];
                return NesCore.cpubus;
            }

            switch (address)
            {
                case 0x5204:
                {
                    byte val = (byte)((ppuInFrame ? 0x40 : 0) | (irqPending ? 0x80 : 0));
                    irqPending = false;
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    return val;
                }
                case 0x5205:
                    return (byte)((multiplierValue1 * multiplierValue2) & 0xFF);
                case 0x5206:
                    return (byte)((multiplierValue1 * multiplierValue2) >> 8);
            }

            return NesCore.cpubus;
        }

        // ================================================================
        //  Expansion ROM write ($4100-$5FFF) — all MMC5 registers
        // ================================================================

        public void MapperW_ExpansionROM(ushort address, byte value)
        {
            // Expansion RAM ($5C00-$5FFF)
            if (address >= 0x5C00 && address <= 0x5FFF)
            {
                if (extendedRamMode <= 1)
                    exRamPtr[address - 0x5C00] = ppuInFrame ? value : (byte)0;
                else if (extendedRamMode == 2)
                    exRamPtr[address - 0x5C00] = value;
                return;
            }

            switch (address)
            {
                // ── Audio (stub) ──
                case 0x5000: case 0x5001: case 0x5002: case 0x5003:
                case 0x5004: case 0x5005: case 0x5006: case 0x5007:
                case 0x5010: case 0x5011: case 0x5015:
                    break;

                // ── Control ──
                case 0x5100: prgMode = value & 3; break;
                case 0x5101: chrMode = value & 3; UpdateChrBanks(true); break;
                case 0x5102: prgRamProtect1 = (byte)(value & 3); break;
                case 0x5103: prgRamProtect2 = (byte)(value & 3); break;
                case 0x5104:
                    extendedRamMode = (byte)(value & 3);
                    NesCore.extAttrEnabled = (extendedRamMode == 1);
                    if (extendedRamMode == 1)
                    {
                        NesCore.extAttrRAM = exRamPtr;
                        NesCore.extAttrCHR = CHR_ROM;
                        NesCore.extAttrChrSize = chrRomSize;
                        NesCore.extAttrChrUpperBits = chrUpperBits;
                    }
                    break;
                case 0x5105: nametableMapping = value; UpdateNametableMapping(); break;
                case 0x5106: fillModeTile = value; UpdateFillNametable(); break;
                case 0x5107: fillModeColor = (byte)(value & 3); UpdateFillNametable(); break;

                // ── PRG bank registers ──
                case 0x5113: prgBanks[0] = value; break;
                case 0x5114: prgBanks[1] = value; break;
                case 0x5115: prgBanks[2] = value; break;
                case 0x5116: prgBanks[3] = value; break;
                case 0x5117: prgBanks[4] = value; break;

                // ── CHR bank registers (A set: $5120-$5127) ──
                case 0x5120: chrBanks[0] = (ushort)(value | (chrUpperBits << 8)); lastChrReg = 0x5120; UpdateChrBanks(true); break;
                case 0x5121: chrBanks[1] = (ushort)(value | (chrUpperBits << 8)); lastChrReg = 0x5121; UpdateChrBanks(true); break;
                case 0x5122: chrBanks[2] = (ushort)(value | (chrUpperBits << 8)); lastChrReg = 0x5122; UpdateChrBanks(true); break;
                case 0x5123: chrBanks[3] = (ushort)(value | (chrUpperBits << 8)); lastChrReg = 0x5123; UpdateChrBanks(true); break;
                case 0x5124: chrBanks[4] = (ushort)(value | (chrUpperBits << 8)); lastChrReg = 0x5124; UpdateChrBanks(true); break;
                case 0x5125: chrBanks[5] = (ushort)(value | (chrUpperBits << 8)); lastChrReg = 0x5125; UpdateChrBanks(true); break;
                case 0x5126: chrBanks[6] = (ushort)(value | (chrUpperBits << 8)); lastChrReg = 0x5126; UpdateChrBanks(true); break;
                case 0x5127: chrBanks[7] = (ushort)(value | (chrUpperBits << 8)); lastChrReg = 0x5127; UpdateChrBanks(true); break;

                // ── CHR bank registers (B set: $5128-$512B) ──
                case 0x5128: chrBanks[8] = (ushort)(value | (chrUpperBits << 8)); lastChrReg = 0x5128; UpdateChrBanks(true); break;
                case 0x5129: chrBanks[9] = (ushort)(value | (chrUpperBits << 8)); lastChrReg = 0x5129; UpdateChrBanks(true); break;
                case 0x512A: chrBanks[10] = (ushort)(value | (chrUpperBits << 8)); lastChrReg = 0x512A; UpdateChrBanks(true); break;
                case 0x512B: chrBanks[11] = (ushort)(value | (chrUpperBits << 8)); lastChrReg = 0x512B; UpdateChrBanks(true); break;

                case 0x5130:
                    chrUpperBits = (byte)(value & 3);
                    if (extendedRamMode == 1) NesCore.extAttrChrUpperBits = chrUpperBits;
                    break;

                // ── Vertical split (stub) ──
                case 0x5200: case 0x5201: case 0x5202: break;

                // ── IRQ ──
                case 0x5203:
                    irqCounterTarget = value;
                    break;
                case 0x5204:
                    irqEnabled = (value & 0x80) != 0;
                    if (!irqEnabled)
                    {
                        NesCore.statusmapperint = false;
                        NesCore.UpdateIRQLine();
                    }
                    else if (irqPending)
                    {
                        NesCore.statusmapperint = true;
                        NesCore.UpdateIRQLine();
                    }
                    break;

                // ── Multiplier ──
                case 0x5205: multiplierValue1 = value; break;
                case 0x5206: multiplierValue2 = value; break;
            }
        }

        // ================================================================
        //  Nametable mapping ($5105)
        // ================================================================

        void UpdateNametableMapping()
        {
            // Each 2-bit field: 0=CIRAM-A, 1=CIRAM-B, 2=ExRAM, 3=Fill
            int nt0 = nametableMapping & 3;
            int nt1 = (nametableMapping >> 2) & 3;
            int nt2 = (nametableMapping >> 4) & 3;
            int nt3 = (nametableMapping >> 6) & 3;

            bool needOverride = false;
            int[] nts = { nt0, nt1, nt2, nt3 };
            for (int i = 0; i < 4; i++)
            {
                switch (nts[i])
                {
                    case 0: NesCore.ntBankPtrs[i] = ppu_ram + 0x2000; break;         // CIRAM page 0
                    case 1: NesCore.ntBankPtrs[i] = ppu_ram + 0x2400; break;         // CIRAM page 1
                    case 2: NesCore.ntBankPtrs[i] = exRamPtr; needOverride = true; break;  // ExRAM
                    case 3: NesCore.ntBankPtrs[i] = fillNTPtr; needOverride = true; break; // Fill
                }
            }

            // If all nametables are CIRAM, try to use the faster CIRAMAddr path
            if (!needOverride)
            {
                // Check if this maps to a standard mirroring mode
                if (nt0 == 0 && nt1 == 0 && nt2 == 1 && nt3 == 1) { *Vertical = 0; NesCore.ntChrOverrideEnabled = false; return; } // H
                if (nt0 == 0 && nt1 == 1 && nt2 == 0 && nt3 == 1) { *Vertical = 1; NesCore.ntChrOverrideEnabled = false; return; } // V
                if (nt0 == 0 && nt1 == 0 && nt2 == 0 && nt3 == 0) { *Vertical = 2; NesCore.ntChrOverrideEnabled = false; return; } // 1A
                if (nt0 == 1 && nt1 == 1 && nt2 == 1 && nt3 == 1) { *Vertical = 3; NesCore.ntChrOverrideEnabled = false; return; } // 1B
            }

            // Non-standard config or ExRAM/Fill involved → use ntBankPtrs
            NesCore.ntChrOverrideEnabled = true;
        }

        void UpdateFillNametable()
        {
            for (int i = 0; i < 960; i++) fillNTPtr[i] = fillModeTile;
            byte attr = (byte)(fillModeColor | (fillModeColor << 2) | (fillModeColor << 4) | (fillModeColor << 6));
            for (int i = 960; i < 1024; i++) fillNTPtr[i] = attr;
        }

        // ================================================================
        //  CHR banking — Mesen2-style per-NT-fetch A/B set switching
        // ================================================================

        // Determine chrA (A-set vs B-set) and fill chrBankPtrs directly.
        // Called on every NT fetch (via NotifyVramRead) and on register writes.
        //
        // chrA conditions (Mesen2 UpdateChrBanks):
        //   - !largeSprites (8x8 mode): always use A set
        //   - splitTileNumber 32-39: sprite fetch phase → use A set
        //   - !ppuInFrame && lastChrReg <= $5127: VBlank/early frame, last write was A-set
        void UpdateChrBanks(bool forceUpdate)
        {
            if (chrRomSize == 0) return;

            bool largeSprites = NesCore.Spritesize8x16;
            if (!largeSprites) lastChrReg = 0; // 8x8 resets lastChrReg (Mesen2 behavior)

            bool chrA = !largeSprites
                     || (splitTileNumber >= 32 && splitTileNumber < 40)
                     || (!ppuInFrame && lastChrReg <= 0x5127);

            if (!forceUpdate && chrA == prevChrA) return;
            prevChrA = chrA;

            FillCHRBankPtrs(NesCore.chrBankPtrs, chrA);
        }

        void FillCHRBankPtrs(byte*[] dst, bool useA)
        {
            switch (chrMode)
            {
                case 0: // 8KB
                {
                    int bank = useA ? chrBanks[7] : chrBanks[11];
                    long baseAddr = (long)bank * 8192 % chrRomSize;
                    for (int i = 0; i < 8; i++)
                        dst[i] = CHR_ROM + baseAddr + i * 1024;
                    break;
                }
                case 1: // 4KB × 2
                {
                    int bankLo = useA ? chrBanks[3] : chrBanks[11];
                    int bankHi = useA ? chrBanks[7] : chrBanks[11];
                    long baseLo = (long)bankLo * 4096 % chrRomSize;
                    long baseHi = (long)bankHi * 4096 % chrRomSize;
                    for (int i = 0; i < 4; i++) dst[i] = CHR_ROM + baseLo + i * 1024;
                    for (int i = 0; i < 4; i++) dst[i + 4] = CHR_ROM + baseHi + i * 1024;
                    break;
                }
                case 2: // 2KB × 4
                {
                    int b0, b1, b2, b3;
                    if (useA) { b0 = chrBanks[1]; b1 = chrBanks[3]; b2 = chrBanks[5]; b3 = chrBanks[7]; }
                    else { b0 = chrBanks[9]; b1 = chrBanks[11]; b2 = chrBanks[9]; b3 = chrBanks[11]; }
                    long a0 = (long)b0 * 2048 % chrRomSize, a1 = (long)b1 * 2048 % chrRomSize;
                    long a2 = (long)b2 * 2048 % chrRomSize, a3 = (long)b3 * 2048 % chrRomSize;
                    dst[0] = CHR_ROM + a0; dst[1] = CHR_ROM + a0 + 1024;
                    dst[2] = CHR_ROM + a1; dst[3] = CHR_ROM + a1 + 1024;
                    dst[4] = CHR_ROM + a2; dst[5] = CHR_ROM + a2 + 1024;
                    dst[6] = CHR_ROM + a3; dst[7] = CHR_ROM + a3 + 1024;
                    break;
                }
                default: // 3: 1KB × 8
                {
                    if (useA)
                    {
                        for (int i = 0; i < 8; i++)
                            dst[i] = CHR_ROM + (long)chrBanks[i] * 1024 % chrRomSize;
                    }
                    else
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            byte* p = CHR_ROM + (long)chrBanks[8 + i] * 1024 % chrRomSize;
                            dst[i] = p; dst[i + 4] = p;
                        }
                    }
                    break;
                }
            }
        }

        // UpdateCHRBanks (IMapper interface) — re-applies current set
        public void UpdateCHRBanks() { UpdateChrBanks(true); }

        public byte MapperR_CHR(int address)
        {
            if (chrRomSize == 0) return ppu_ram[address];
            int slot = address >> 10;
            return NesCore.chrBankPtrs[slot][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val)
        {
            if (CHR_ROM_count == 0) ppu_ram[addr] = val;
        }

        // ================================================================
        //  Scanline IRQ — Mesen2-style 3-consecutive-NT-read detection
        // ================================================================

        public MapperA12Mode A12NotifyMode => MapperA12Mode.None; // MMC5 uses VRAM read notifications, not A12

        public void NotifyA12(int addr, int ppuAbsCycle) { } // unused — all logic in NotifyVramRead

        // ================================================================
        //  Pre-sprite render CHR switch — called by PPU at dot 257 before RenderSpritesLine
        // ================================================================
        //
        // When chrABAutoSwitch is false (MMC5 handles CHR A/B via NotifyVramRead),
        // RenderSpritesLine at cx=257 runs before any sprite-phase NotifyVramRead calls.
        // Force chrBankPtrs to A set (sprites) here so sprite rendering uses correct tiles.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PreSpriteRender()
        {
            if (NesCore.Spritesize8x16 && chrRomSize > 0)
            {
                prevChrA = true;
                FillCHRBankPtrs(NesCore.chrBankPtrs, true);
            }
        }

        // ================================================================
        //  PPU VRAM read notification — called by PPU on every fetch during rendering
        // ================================================================
        //
        // Mesen2 MapperReadVram equivalent. Handles:
        //   1. splitTileNumber tracking (BG/sprite/prefetch phase detection)
        //   2. ppuInFrame promotion (needInFrame → ppuInFrame on next NT fetch)
        //   3. Per-NT-fetch CHR bank evaluation (UpdateChrBanks)
        //   4. 3-consecutive-identical-NT-read scanline boundary detection (IRQ)
        //   5. ppuIdleCounter reset (for ppuInFrame timeout in CpuCycle)

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void NotifyVramRead(int addr)
        {
            bool isNtFetch = addr >= 0x2000 && addr <= 0x2FFF && (addr & 0x3FF) < 0x3C0;

            if (isNtFetch)
            {
                splitTileNumber++;

                if (ppuInFrame)
                {
                    UpdateChrBanks(false);
                }
                else if (needInFrame)
                {
                    needInFrame = false;
                    ppuInFrame = true;
                    UpdateChrBanks(false);
                }
            }

            DetectScanlineStart(addr);

            ppuIdleCounter = 3;
            lastPpuReadAddr = (ushort)addr;
        }

        // Detect scanline boundaries via 3 consecutive identical nametable reads.
        // The PPU reads the same NT address at dots 337, 339, and 1 of the next scanline.
        // When we detect this pattern, we increment the scanline counter and check for IRQ.
        void DetectScanlineStart(int addr)
        {
            if (ntReadCounter >= 2)
            {
                // 3+ identical NT reads already detected → scanline boundary
                if (!ppuInFrame && !needInFrame)
                {
                    needInFrame = true;
                    scanlineCounter = 0;
                }
                else
                {
                    scanlineCounter++;
                    if (irqCounterTarget == scanlineCounter)
                    {
                        irqPending = true;
                        if (irqEnabled)
                        {
                            NesCore.statusmapperint = true;
                            NesCore.UpdateIRQLine();
                        }
                    }
                }
            }
            else if (addr >= 0x2000 && addr <= 0x2FFF)
            {
                if (lastPpuReadAddr == (ushort)addr)
                {
                    ntReadCounter++;
                    if (ntReadCounter >= 2)
                    {
                        splitTileNumber = 0;
                    }
                }
            }

            if (lastPpuReadAddr != (ushort)addr)
            {
                ntReadCounter = 0;
            }
        }

        // ================================================================
        //  CPU cycle — PPU idle detection for ppuInFrame timeout
        // ================================================================

        public void CpuCycle()
        {
            // 3 CPU cycles without a PPU read → ppuInFrame goes false
            if (ppuIdleCounter > 0)
            {
                ppuIdleCounter--;
                if (ppuIdleCounter == 0)
                {
                    ppuInFrame = false;
                    needInFrame = false;
                    UpdateChrBanks(true);
                }
            }
        }

        public void Reset() { }
        public void CpuClockRise() { }
        public void Cleanup()
        {
            if (exRamPtr != null) { Marshal.FreeHGlobal((IntPtr)exRamPtr); exRamPtr = null; }
            if (fillNTPtr != null) { Marshal.FreeHGlobal((IntPtr)fillNTPtr); fillNTPtr = null; }
        }
    }
}
