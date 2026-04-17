namespace AprNes
{
    // Mapper 176 — FK23C (Waixing / 外星科技 multicart chip)
    //
    // Full port of Mesen2 Waixing/Fk23C.h. Covers:
    //   - MMC3 base (12-reg extended array, regs 0-7 standard, 8-11 extended)
    //   - 5 PRG banking modes (0-2 std/extended, 3 = 2x mirror, 4 = 4x linear)
    //   - CHR modes: MMC3 / CNROM (outerChrBankSize inner-mask)
    //   - invertPrgA14 / invertChrA12 from $8000 bits 6/7
    //   - Extended MMC3 mode (bit 1 of $5013): uses regs 8-11 for 4x 8KB PRG and
    //     additional 1KB CHR slots (regs 10/11 interleave between 1KB entries 1 and 3)
    //   - $A000 mirroring: 2-bit reg; with allowSingleScreenMirroring also screen A/B
    //   - $A001 WRAM config: bit 5 → wramConfigEnabled (bankable $4000-$7FFF);
    //     bit 4 → ramInFirstChrBank; bit 3 → allowSingleScreenMirroring;
    //     bits 0-1 → wramBankSelect; bit 6 → fk23RegistersEnabled + wramWriteProtected;
    //     bit 7 → wramEnabled
    //   - IRQ: A12 rising edge detector, 2-CPU-cycle delay before asserting
    //   - Subtype 1 default: PRG==CHR==1024KB → prgBaseBits=0x20
    //   - Subtype 2 quirk: 16MB PRG swaps MMC3 regs $46/$47
    //   - 32KB WRAM (4x 8KB banks): bank 0 shares NES_MEM[$6000..$7FFF] (battery-compat);
    //     banks 1-3 allocated. In wramConfig mode: $6000-$7FFF → bank wramBankSelect,
    //     $4100-$5FFF → bank (wramBankSelect+1)&3. In non-config: $6000-$7FFF → bank 0.
    //   - When wramConfig enabled + fk23Registers disabled: $4100-$5FFF becomes writable
    //     WRAM instead of register area.
    //
    // Not implemented (very low impact):
    //   - ramInFirstChrBank (CHR RAM overlay for first 8KB)
    //   - Subtype-specific CHR RAM size auto-detection (fixed 32KB CHR-RAM buffer used)
    //   - Battery persistence for WRAM banks 1-3 (bank 0 saves via NES_MEM)
    //
    // Ref: Mesen2 Fk23C.h (407 lines).
    unsafe public class Mapper176 : IMapper
    {
        byte* PRG_ROM, CHR_ROM, ppu_ram, NES_MEM;
        int PRG_ROM_count, CHR_ROM_count;
        int* Vertical;

        // 32KB CHR-RAM overlay for selectChrRam / ramInFirstChrBank (pre-allocated)
        byte* CHR_RAM;

        // 32KB WRAM split into 4x 8KB banks. Bank 0 aliases NES_MEM+$6000 for battery
        // compatibility; banks 1-3 are private allocations.
        byte* _extraWram;           // 24KB buffer holding banks 1,2,3 contiguously
        byte* wramBank0, wramBank1, wramBank2, wramBank3;

        // --- FK23C control state ---
        byte prgBankingMode;
        byte outerChrBankSize;
        bool selectChrRam;
        bool mmc3ChrMode = true;
        bool cnromChrMode;
        int  prgBaseBits;         // 10-bit
        byte chrBaseBits;
        bool extendedMmc3Mode;
        byte wramBankSelect;
        bool ramInFirstChrBank;
        bool allowSingleScreenMirroring;
        bool fk23RegistersEnabled;
        bool wramConfigEnabled;
        bool wramEnabled;
        bool wramWriteProtected;
        bool invertPrgA14;
        bool invertChrA12;
        byte currentRegister;     // 4-bit (extended) or 3-bit (standard)
        byte mirroringReg;        // 2-bit
        byte cnromChrReg;

        // 12-register MMC3 array (reg 0..7 = standard MMC3, 8..11 = extended)
        readonly byte[] mmc3Registers = new byte[12];

        // IRQ
        byte irqLatchVal, irqCounter;
        bool irqReload, irqEnabled;
        int  irqDelay;
        int  m2Filter;

        // PRG bank slots (computed)
        readonly int[] _prg = new int[4];

        public MapperA12Mode A12NotifyMode => MapperA12Mode.MMC3;

        public void MapperInit(byte* _PRG_ROM, byte* _CHR_ROM, byte* _ppu_ram, int _PRG_ROM_count, int _CHR_ROM_count, int* _Vertical)
        {
            PRG_ROM = _PRG_ROM; CHR_ROM = _CHR_ROM; ppu_ram = _ppu_ram;
            PRG_ROM_count = _PRG_ROM_count; CHR_ROM_count = _CHR_ROM_count;
            Vertical = _Vertical; NES_MEM = NesCore.NES_MEM;

            // 32KB CHR-RAM overlay (Mesen2 allocates up to 256KB; 32KB is enough for typical multicarts)
            CHR_RAM = (byte*)System.Runtime.InteropServices.Marshal.AllocHGlobal(0x8000);
            for (int i = 0; i < 0x8000; i++) CHR_RAM[i] = 0;

            // WRAM banks: bank 0 aliased to NES_MEM+$6000; banks 1-3 allocated (24KB).
            _extraWram = (byte*)System.Runtime.InteropServices.Marshal.AllocHGlobal(3 * 0x2000);
            for (int i = 0; i < 3 * 0x2000; i++) _extraWram[i] = 0;
            wramBank0 = NES_MEM + 0x6000;
            wramBank1 = _extraWram;
            wramBank2 = _extraWram + 0x2000;
            wramBank3 = _extraWram + 0x4000;

            InitState();
        }

        void InitState()
        {
            prgBankingMode = 0;
            outerChrBankSize = 0;
            selectChrRam = false;
            mmc3ChrMode = true;
            cnromChrMode = false;

            // Subtype 1: PRG==CHR==1024KB → boot in second 512KB half (prgBaseBits=0x20)
            int prgBytes = PRG_ROM_count * 16384;
            int chrBytes = CHR_ROM_count * 8192;
            prgBaseBits = (prgBytes == 1024 * 1024 && prgBytes == chrBytes) ? 0x20 : 0;
            chrBaseBits = 0;

            extendedMmc3Mode = false;

            wramBankSelect = 0;
            ramInFirstChrBank = false;
            allowSingleScreenMirroring = false;
            wramConfigEnabled = false;
            fk23RegistersEnabled = false;
            wramEnabled = false;
            wramWriteProtected = false;

            currentRegister = 0;
            cnromChrReg = 0;

            // Mesen2 init values: {0,2,4,5,6,7,0,1,0xFE,0xFF,0xFF,0xFF}
            byte[] initValues = { 0, 2, 4, 5, 6, 7, 0, 1, 0xFE, 0xFF, 0xFF, 0xFF };
            for (int i = 0; i < 12; i++) mmc3Registers[i] = initValues[i];

            invertPrgA14 = false;
            invertChrA12 = false;

            mirroringReg = 0;

            irqCounter = 0;
            irqEnabled = false;
            irqReload = false;
            irqLatchVal = 0;
            irqDelay = 0;
            m2Filter = 0;

            UpdateState();
        }

        public void Reset() { InitState(); }

        byte* GetWramBank(int b)
        {
            switch (b & 3)
            {
                case 0: return wramBank0;
                case 1: return wramBank1;
                case 2: return wramBank2;
                default: return wramBank3;
            }
        }

        public byte MapperR_ExpansionROM(ushort address)
        {
            // In wramConfig mode, $4100-$5FFF reads WRAM bank (wramBankSelect+1)&3
            if (wramConfigEnabled)
            {
                byte* bank = GetWramBank((wramBankSelect + 1) & 3);
                return bank[address - 0x4000];
            }
            return NesCore.cpubus;
        }

        public void MapperW_ExpansionROM(ushort address, byte value) { WriteRegister(address, value); }

        public byte MapperR_RAM(ushort address)
        {
            if (wramConfigEnabled)
            {
                byte* bank = GetWramBank(wramBankSelect);
                return bank[address - 0x6000];
            }
            // Non-config: always readable from bank 0 (matches pre-10%-patch behavior)
            return NES_MEM[address];
        }

        public void MapperW_RAM(ushort address, byte value)
        {
            if (wramConfigEnabled)
            {
                byte* bank = GetWramBank(wramBankSelect);
                bank[address - 0x6000] = value;
                return;
            }
            // Non-config: respect write protection
            if (wramWriteProtected) return;
            NES_MEM[address] = value;
        }

        public void MapperW_PRG(ushort address, byte value) { WriteRegister(address, value); }

        void WriteRegister(int addr, byte value)
        {
            if (addr < 0x8000)
            {
                // FK23C control at $5010-$5FFF, or accepted when wramConfig is disabled
                if (fk23RegistersEnabled || !wramConfigEnabled)
                {
                    if ((addr & 0x5010) != 0x5010) return;

                    switch (addr & 0x03)
                    {
                        case 0:
                            prgBankingMode = (byte)(value & 0x07);
                            outerChrBankSize = (byte)((value & 0x10) >> 4);
                            selectChrRam = (value & 0x20) != 0;
                            mmc3ChrMode = (value & 0x40) == 0;
                            prgBaseBits = (prgBaseBits & ~0x180) | ((value & 0x80) << 1) | ((value & 0x08) << 4);
                            break;
                        case 1:
                            prgBaseBits = (prgBaseBits & ~0x7F) | (value & 0x7F);
                            break;
                        case 2:
                            prgBaseBits = (prgBaseBits & ~0x200) | ((value & 0x40) << 3);
                            chrBaseBits = value;
                            cnromChrReg = 0;
                            break;
                        case 3:
                            extendedMmc3Mode = (value & 0x02) != 0;
                            cnromChrMode = (value & 0x44) != 0;
                            break;
                    }
                    UpdateState();
                }
                else
                {
                    // FK23C regs disabled + wramConfig enabled → $4100-$5FFF is writable WRAM,
                    // mapped to bank (wramBankSelect+1)&3.
                    byte* bank = GetWramBank((wramBankSelect + 1) & 3);
                    bank[addr - 0x4000] = value;
                }
                return;
            }

            // $8000+ register area
            if (cnromChrMode && (addr <= 0x9FFF || addr >= 0xC000))
            {
                cnromChrReg = (byte)(value & 0x03);
                UpdateState();
            }

            switch (addr & 0xE001)
            {
                case 0x8000:
                {
                    // Subtype 2: 16MB PRG, no CHR-ROM → swap MMC3 regs $46/$47
                    int prgBytes = PRG_ROM_count * 16384;
                    byte v = value;
                    if (prgBytes == 16384 * 1024 && (v == 0x46 || v == 0x47)) v ^= 1;
                    invertPrgA14 = (v & 0x40) != 0;
                    invertChrA12 = (v & 0x80) != 0;
                    currentRegister = (byte)(v & 0x0F);
                    UpdateState();
                    break;
                }
                case 0x8001:
                {
                    int reg = currentRegister & (extendedMmc3Mode ? 0x0F : 0x07);
                    if (reg < 12)
                    {
                        mmc3Registers[reg] = value;
                        UpdateState();
                    }
                    break;
                }
                case 0xA000:
                    mirroringReg = (byte)(value & 0x03);
                    UpdateState();
                    break;
                case 0xA001:
                {
                    byte v = value;
                    if ((v & 0x20) == 0) v &= 0xC0;
                    wramBankSelect = (byte)(v & 0x03);
                    ramInFirstChrBank = (v & 0x04) != 0;
                    allowSingleScreenMirroring = (v & 0x08) != 0;
                    wramConfigEnabled = (v & 0x20) != 0;
                    fk23RegistersEnabled = (v & 0x40) != 0;
                    wramWriteProtected = (v & 0x40) != 0;
                    wramEnabled = (v & 0x80) != 0;
                    UpdateState();
                    break;
                }
                case 0xC000:
                    irqLatchVal = value;
                    break;
                case 0xC001:
                    irqCounter = 0;
                    irqReload = true;
                    break;
                case 0xE000:
                    irqEnabled = false;
                    NesCore.statusmapperint = false;
                    NesCore.UpdateIRQLine();
                    break;
                case 0xE001:
                    irqEnabled = true;
                    break;
            }
        }

        void UpdateState()
        {
            // Mirroring: 2-bit reg with allowSingleScreenMirroring expanding to 4 modes
            int mask = allowSingleScreenMirroring ? 0x03 : 0x01;
            switch (mirroringReg & mask)
            {
                case 0: *Vertical = 0; break;  // Vertical
                case 1: *Vertical = 1; break;  // Horizontal
                case 2: *Vertical = 2; break;  // ScreenA (single-screen low)
                case 3: *Vertical = 3; break;  // ScreenB (single-screen high)
            }
            UpdatePRG();
            UpdateCHRBanks();
        }

        void UpdatePRG()
        {
            int total8k = PRG_ROM_count * 2;
            if (total8k < 1) total8k = 1;

            switch (prgBankingMode)
            {
                case 0:
                case 1:
                case 2:
                    if (extendedMmc3Mode)
                    {
                        int swap = invertPrgA14 ? 2 : 0;
                        int outer = (prgBaseBits << 1);
                        _prg[0 ^ swap] = (mmc3Registers[6] | outer) % total8k;
                        _prg[1]        = (mmc3Registers[7] | outer) % total8k;
                        _prg[2 ^ swap] = (mmc3Registers[8] | outer) % total8k;
                        _prg[3]        = (mmc3Registers[9] | outer) % total8k;
                    }
                    else
                    {
                        int swap = invertPrgA14 ? 2 : 0;
                        int innerMask = 0x3F >> prgBankingMode;
                        int outer = (prgBaseBits << 1) & ~innerMask;
                        _prg[0 ^ swap] = ((mmc3Registers[6] & innerMask) | outer) % total8k;
                        _prg[1]        = ((mmc3Registers[7] & innerMask) | outer) % total8k;
                        _prg[2 ^ swap] = ((0xFE & innerMask) | outer) % total8k;
                        _prg[3]        = ((0xFF & innerMask) | outer) % total8k;
                    }
                    break;
                case 3:
                {
                    int basep = (prgBaseBits << 1) % total8k;
                    _prg[0] = basep;     _prg[1] = (basep + 1) % total8k;
                    _prg[2] = basep;     _prg[3] = (basep + 1) % total8k;
                    break;
                }
                case 4:
                {
                    int basep = ((prgBaseBits & 0xFFE) << 1) % total8k;
                    _prg[0] = basep;
                    _prg[1] = (basep + 1) % total8k;
                    _prg[2] = (basep + 2) % total8k;
                    _prg[3] = (basep + 3) % total8k;
                    break;
                }
            }
        }

        public byte MapperR_RPG(ushort address)
        {
            int slot = (address >> 13) & 3;
            int bank = _prg[slot];
            return PRG_ROM[(address & 0x1FFF) + (bank << 13)];
        }

        public void UpdateCHRBanks()
        {
            // selectChrRam overrides → CHR-RAM
            bool useRam = CHR_ROM_count == 0 || selectChrRam;
            byte* chrBase = useRam ? CHR_RAM : CHR_ROM;
            int total1k = useRam ? 32 : (CHR_ROM_count * 8);  // 32KB / 1KB = 32 slots
            if (total1k < 1) total1k = 1;

            if (!mmc3ChrMode)
            {
                // CNROM mode
                int innerMask = cnromChrMode ? (outerChrBankSize != 0 ? 1 : 3) : 0;
                for (int i = 0; i < 8; i++)
                {
                    int bank = ((((cnromChrReg & innerMask) | chrBaseBits) << 3) + i) % total1k;
                    NesCore.chrBankPtrs[i] = chrBase + (bank << 10);
                }
                return;
            }

            // MMC3 CHR mode
            int swap = invertChrA12 ? 4 : 0;
            int[] banks = new int[8];

            if (extendedMmc3Mode)
            {
                int outer = (chrBaseBits << 3);
                banks[0 ^ swap] = mmc3Registers[0]  | outer;
                banks[1 ^ swap] = mmc3Registers[10] | outer;
                banks[2 ^ swap] = mmc3Registers[1]  | outer;
                banks[3 ^ swap] = mmc3Registers[11] | outer;
                banks[4 ^ swap] = mmc3Registers[2]  | outer;
                banks[5 ^ swap] = mmc3Registers[3]  | outer;
                banks[6 ^ swap] = mmc3Registers[4]  | outer;
                banks[7 ^ swap] = mmc3Registers[5]  | outer;
            }
            else
            {
                int innerM = outerChrBankSize != 0 ? 0x7F : 0xFF;
                int outer = (chrBaseBits << 3) & ~innerM;
                banks[0 ^ swap] = ((mmc3Registers[0] & 0xFE) & innerM) | outer;
                banks[1 ^ swap] = ((mmc3Registers[0] | 0x01) & innerM) | outer;
                banks[2 ^ swap] = ((mmc3Registers[1] & 0xFE) & innerM) | outer;
                banks[3 ^ swap] = ((mmc3Registers[1] | 0x01) & innerM) | outer;
                banks[4 ^ swap] = (mmc3Registers[2] & innerM) | outer;
                banks[5 ^ swap] = (mmc3Registers[3] & innerM) | outer;
                banks[6 ^ swap] = (mmc3Registers[4] & innerM) | outer;
                banks[7 ^ swap] = (mmc3Registers[5] & innerM) | outer;
            }

            for (int i = 0; i < 8; i++)
                NesCore.chrBankPtrs[i] = chrBase + ((banks[i] % total1k) << 10);
        }

        public byte MapperR_CHR(int address)
        {
            return NesCore.chrBankPtrs[(address >> 10) & 7][address & 0x3FF];
        }

        public void MapperW_CHR(int addr, byte val)
        {
            // Allow writes when backing store is CHR-RAM (selectChrRam or no CHR-ROM)
            bool useRam = CHR_ROM_count == 0 || selectChrRam;
            if (!useRam) return;
            int slot = (addr >> 10) & 7;
            // Figure out which 1KB bank is mapped at this slot to write to the right CHR-RAM offset.
            // chrBankPtrs stores an absolute pointer; derive offset from base.
            byte* basep = CHR_ROM_count == 0 ? ppu_ram : CHR_RAM;
            long off = NesCore.chrBankPtrs[slot] - basep;
            if (CHR_ROM_count == 0)
            {
                ppu_ram[addr] = val;
            }
            else if (off >= 0 && off < 0x8000)
            {
                CHR_RAM[off + (addr & 0x3FF)] = val;
            }
        }

        public void CpuCycle() { }

        public void CpuClockRise()
        {
            // IRQ delay countdown (Mesen2: 2 CPU cycles after A12 rise)
            if (irqDelay > 0)
            {
                irqDelay--;
                if (irqDelay == 0)
                {
                    NesCore.statusmapperint = true;
                    NesCore.UpdateIRQLine();
                }
            }
        }

        public void NotifyA12(int addr, int ppuAbsCycle) { }

        public void PpuClock()
        {
            // A12 rising edge detection (MMC3-style m2 filter)
            bool a12Now = (NesCore.ppuAddressBus & 0x1000) != 0;
            if (!a12Now) { if (m2Filter < 16) m2Filter++; }
            if (!NesCore.ppuA12Prev && a12Now && m2Filter >= 10) StepIrq();
            if (a12Now) m2Filter = 0;
        }

        void StepIrq()
        {
            if (irqCounter == 0 || irqReload)
            {
                irqCounter = irqLatchVal;
            }
            else
            {
                irqCounter--;
            }

            if (irqCounter == 0 && irqEnabled)
            {
                irqDelay = 2;
            }
            irqReload = false;
        }

        public void Cleanup()
        {
            if (CHR_RAM != null)
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal((System.IntPtr)CHR_RAM);
                CHR_RAM = null;
            }
            if (_extraWram != null)
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal((System.IntPtr)_extraWram);
                _extraWram = null;
                wramBank0 = wramBank1 = wramBank2 = wramBank3 = null;
            }
        }
    }
}
