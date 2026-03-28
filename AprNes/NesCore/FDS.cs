using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AprNes
{
    /// <summary>
    /// Famicom Disk System (FDS) support — NesCore partial class.
    /// Independent of IMapper; plugs directly into mem_read_fun/mem_write_fun.
    /// </summary>
    unsafe public partial class NesCore
    {
        // ── FDS mode flag ──────────────────────────────────────────────────
        static public bool isFDS = false;

        // ── BIOS ROM (8KB, $E000-$FFFF) ────────────────────────────────────
        static byte* fdsBiosRom = null;      // allocated in initFDS

        // ── PRG-RAM (32KB, $6000-$DFFF) ────────────────────────────────────
        static byte* fdsPrgRam = null;       // allocated in initFDS

        // ── Disk data ──────────────────────────────────────────────────────
        static byte[][] fdsDiskSides;        // [sideIndex][position] — gap-inserted images
        static byte[][] fdsDiskHeaders;      // [sideIndex][56 bytes] — raw disk headers
        static int fdsSideCount;
        static int fdsDiskNumber = -1;       // -1 = no disk inserted
        static int fdsDiskPosition;
        // ── Disk I/O state machine ─────────────────────────────────────────
        const int FDS_NO_DISK = -1;
        const int FDS_DISK_SIDE_CAPACITY = 65500;
        const int FDS_HEAD_DELAY = 50000;
        const int FDS_BYTE_DELAY = 149;

        static bool fdsMotorOn;
        static bool fdsResetTransfer;
        static bool fdsReadMode;
        static bool fdsCrcControl;
        static bool fdsDiskReady;
        static bool fdsDiskIrqEnabled;
        static bool fdsEndOfHead;
        static bool fdsGapEnded;
        static bool fdsScanningDisk;
        static bool fdsTransferComplete;
        static bool fdsBadCrc;
        static bool fdsPreviousCrcControl;
        static int  fdsDelay;
        static ushort fdsCrcAccumulator;

        // ── IRQ Timer ──────────────────────────────────────────────────────
        static ushort fdsIrqReloadValue;
        static ushort fdsIrqCounter;
        static bool fdsIrqEnabled;
        static bool fdsIrqRepeatEnabled;
        static bool fdsTimerIrqPending;  // External IRQ (timer)
        static bool fdsDiskIrqPending;   // Disk transfer IRQ

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void fds_UpdateIrq()
        {
            statusmapperint = fdsTimerIrqPending || fdsDiskIrqPending;
            UpdateIRQLine();
        }

        // ── Register enables ───────────────────────────────────────────────
        static bool fdsDiskRegEnabled;
        static bool fdsSoundRegEnabled;

        // ── Data registers ─────────────────────────────────────────────────
        static byte fdsWriteDataReg;
        static byte fdsReadDataReg;
        static byte fdsExtConWriteReg;

        // ── Auto disk insert ───────────────────────────────────────────────
        static int fdsAutoDiskEjectCounter;
        static int fdsAutoDiskSwitchCounter;
        static int fdsPreviousDiskNumber;
        static bool fdsGameStarted;
        static int fdsLastDiskCheckFrame;
        static int fdsSuccessiveChecks;

        // ── FDS Audio ──────────────────────────────────────────────────────
        // Volume channel (BaseFdsChannel)
        static byte fdsVolSpeed, fdsVolGain;
        static bool fdsVolEnvelopeOff, fdsVolIncrease;
        static ushort fdsVolFrequency;
        static uint fdsVolTimer;
        static byte fdsVolMasterSpeed;

        // Modulation channel
        static byte fdsModSpeed, fdsModGain;
        static bool fdsModEnvelopeOff, fdsModIncrease;
        static ushort fdsModFrequency;
        static uint fdsModTimer;
        static byte fdsModMasterSpeed;
        static sbyte fdsModCounter;
        static bool fdsModDisabled;
        static byte[] fdsModTable = new byte[64];
        static byte fdsModTablePosition;
        static ushort fdsModOverflowCounter;
        static int fdsModOutput;

        // Waveform
        static byte[] fdsWaveTable = new byte[64];
        static bool fdsWaveWriteEnabled;
        static bool fdsDisableEnvelopes;
        static bool fdsHaltWaveform;
        static byte fdsMasterVolume;
        static ushort fdsWaveOverflowCounter;
        static byte fdsWavePosition;
        static byte fdsLastAudioOutput;

        static readonly uint[] FdsWaveVolumeTable = { 36, 24, 17, 14 };

        // ── BIOS validation ────────────────────────────────────────────────
        const int FDS_BIOS_SIZE = 8192;
        const string FDS_BIOS_SHA256 = "99c18490ed9002d9c6d999b9d8d15be5c051bdfa7cc7e73318053c9a994b0178";

        /// <summary>
        /// Validates and loads the FDS BIOS from {exeDir}/FDSBIOS/DISKSYS.ROM.
        /// Returns the BIOS bytes on success, null on failure (with error shown).
        /// </summary>
        static public byte[] LoadAndValidateFdsBios(string exeDir)
        {
            string biosPath = Path.Combine(exeDir, "FDSBIOS", "DISKSYS.ROM");
            if (!File.Exists(biosPath))
            {
                ShowError("Famicom Disk System BIOS not found.\nPlease place DISKSYS.ROM in the FDSBIOS folder next to the executable.\n\nExpected path:\n" + biosPath);
                return null;
            }

            byte[] bios = File.ReadAllBytes(biosPath);
            if (bios.Length != FDS_BIOS_SIZE)
            {
                ShowError("Invalid FDS BIOS file (wrong size).\nExpected " + FDS_BIOS_SIZE + " bytes, got " + bios.Length + " bytes.");
                return null;
            }

            // SHA-256 check (warning only, not blocking)
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bios);
                string hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                if (hex != FDS_BIOS_SHA256)
                {
                    Console.WriteLine("FDS BIOS SHA-256 mismatch: " + hex);
                    Console.WriteLine("Expected: " + FDS_BIOS_SHA256);
                    Console.WriteLine("The file may be a different revision. Proceeding anyway.");
                }
                else
                {
                    Console.WriteLine("FDS BIOS SHA-256 verified OK.");
                }
            }

            return bios;
        }

        /// <summary>
        /// Detect whether a ROM file is FDS format (header "FDS\x1a" or headerless with correct size).
        /// </summary>
        static public bool IsFdsFile(byte[] data)
        {
            if (data == null || data.Length < 4) return false;
            // With header
            if (data[0] == 'F' && data[1] == 'D' && data[2] == 'S' && data[3] == 0x1A)
                return true;
            // Headerless: must be multiple of 65500
            if (data.Length % FDS_DISK_SIDE_CAPACITY == 0 && data.Length >= FDS_DISK_SIDE_CAPACITY)
                return true;
            return false;
        }

        // ── FDS file parsing ───────────────────────────────────────────────

        /// <summary>
        /// Parse .fds file data into gap-inserted disk side images.
        /// </summary>
        static void fds_ParseDiskData(byte[] fdsData)
        {
            int fileOffset = 0;
            bool hasHeader = (fdsData.Length >= 4 && fdsData[0] == 'F' && fdsData[1] == 'D' && fdsData[2] == 'S' && fdsData[3] == 0x1A);

            if (hasHeader)
            {
                fdsSideCount = fdsData[4];
                fileOffset = 16;
            }
            else
            {
                fdsSideCount = fdsData.Length / FDS_DISK_SIDE_CAPACITY;
            }

            Console.WriteLine("FDS: " + fdsSideCount + " disk side(s)");

            fdsDiskSides = new byte[fdsSideCount][];
            fdsDiskHeaders = new byte[fdsSideCount][];

            for (int side = 0; side < fdsSideCount; side++)
            {
                int sideStart = fileOffset + side * FDS_DISK_SIDE_CAPACITY;

                // Save disk header (56 bytes starting at offset+1, skip block type byte)
                fdsDiskHeaders[side] = new byte[56];
                int headerSrc = sideStart + 1; // skip block type byte (0x01)
                int copyLen = Math.Min(56, fdsData.Length - headerSrc);
                if (copyLen > 0) Array.Copy(fdsData, headerSrc, fdsDiskHeaders[side], 0, copyLen);

                // Build gap-inserted disk image
                fdsDiskSides[side] = fds_AddGaps(fdsData, sideStart, FDS_DISK_SIDE_CAPACITY);

                Console.WriteLine("FDS: Side " + side + " image size = " + fdsDiskSides[side].Length);
            }
        }

        /// <summary>
        /// Add gaps between disk blocks (matching Mesen2 FdsLoader::AddGaps).
        /// </summary>
        static byte[] fds_AddGaps(byte[] raw, int offset, int capacity)
        {
            var disk = new System.Collections.Generic.List<byte>(capacity + 4000);
            int bufferSize = Math.Min(capacity, raw.Length - offset);

            // Safe read helper (like Mesen2's lambda)
            Func<int, byte> read = (int i) =>
                (i >= 0 && i < bufferSize) ? raw[offset + i] : (byte)0;

            // Initial gap: 28300 bits = 3537 bytes of 0x00
            for (int i = 0; i < 28300 / 8; i++) disk.Add(0);

            for (int j = 0; j < capacity; )
            {
                byte blockType = read(j);
                int blockLength = 1;

                switch (blockType)
                {
                    case 1: blockLength = 56; break;
                    case 2: blockLength = 2; break;
                    case 3: blockLength = 16; break;
                    case 4:
                        blockLength = 1 + (read(j - 3) | (read(j - 2) << 8));
                        break;
                    default:
                        disk.Add(0x80);
                        for (int k = j; k < bufferSize; k++)
                            disk.Add(raw[offset + k]);
                        goto done;
                }

                if (j + blockLength >= bufferSize) break;

                disk.Add(0x80);
                for (int k = 0; k < blockLength; k++)
                    disk.Add(read(j + k));

                // Fake CRC (2 bytes)
                disk.Add(0x4D);
                disk.Add(0x62);
                // Inter-block gap: 976 bits = 122 bytes of 0x00
                for (int k = 0; k < 976 / 8; k++) disk.Add(0);

                j += blockLength;
            }

            done:
            while (disk.Count < FDS_DISK_SIDE_CAPACITY) disk.Add(0);
            return disk.ToArray();
        }

        // ── initFDS ────────────────────────────────────────────────────────

        /// <summary>
        /// Initialize NesCore for FDS mode. Bypasses IMapper entirely.
        /// </summary>
        static public bool initFDS(byte[] biosRom, byte[] fdsData)
        {
            FreeUnmanagedMemory();
            fds_FreeMemory();

            try
            {
                isFDS = true;
                mapper = 20;
                // Use a minimal CHR-RAM shim so init_function() PPU setup works
                MapperObj = new FdsChrMapper();

                Console.WriteLine("=== FDS Mode ===");

                // Parse disk data
                fds_ParseDiskData(fdsData);

                // Allocate mirroring control
                Vertical = (int*)Marshal.AllocHGlobal(sizeof(int));
                *Vertical = 0; // Horizontal mirroring (FDS default)

                // PRG-ROM: not used in FDS, set to null
                PRG_ROM = null;
                PRG_ROM_count = 0;
                CHR_ROM = null;
                CHR_ROM_count = 0;
                NesHeaderV2 = false;
                HasBattery = false; // FDS handles its own save

                // Allocate BIOS ROM (8KB)
                fdsBiosRom = (byte*)Marshal.AllocHGlobal(FDS_BIOS_SIZE);
                for (int i = 0; i < FDS_BIOS_SIZE; i++) fdsBiosRom[i] = biosRom[i];

                // Allocate PRG-RAM (32KB, $6000-$DFFF)
                fdsPrgRam = (byte*)Marshal.AllocHGlobal(32768);
                for (int i = 0; i < 32768; i++) fdsPrgRam[i] = 0;

                // Shared hardware allocations (same as init())
                ScreenBuf1x      = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 61440);
                if (AnalogEnabled)
                {
                    SyncAnalogConfig();
                    AnalogBufSize   = Crt_DstW * Crt_DstH;
                    AnalogScreenBuf = (uint*)Marshal.AllocHGlobal(sizeof(uint) * AnalogBufSize);
                }
                Buffer_BG_array  = (int* )Marshal.AllocHGlobal(sizeof(int)  * 61440);
                NesColors        = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 64);
                palCacheR        = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 4);
                palCacheN        = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 4);
                spr_ram          = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 256);
                secondaryOAM     = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 32);
                corruptOamRow    = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 32);
                ppu_ram          = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 0x4000);
                P1_joypad_status = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 8);
                P2_joypad_status = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 8);
                NES_MEM          = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 65536);

                // Init the CHR-RAM shim mapper (needed for PPU function pointer setup)
                MapperObj.MapperInit(null, null, ppu_ram, 0, 0, Vertical);
                MapperObj.Reset();
                MapperObj.UpdateCHRBanks();

                // Mapper-related flags
                mapperNeedsA12 = false;
                mapperA12IsMmc3 = false;
                ntChrOverrideEnabled = false;
                for (int i = 0; i < 4; i++) ntBankWritable[i] = true;
                chrABAutoSwitch = false;
                chrBGUseASet = false;
                extAttrEnabled = false;
                mmc5Ref = null;

                // Clear all buffers
                for (int i = 0; i < 61440; i++) ScreenBuf1x[i] = 0;
                for (int i = 0; i < 16384; i++) ppu_ram[i] = 0;
                for (int i = 0; i < 256; i++) spr_ram[i] = 0;
                for (int i = 0; i < 32; i++) { secondaryOAM[i] = 0; corruptOamRow[i] = 0; }
                for (int i = 0; i < 8; i++) P1_joypad_status[i] = 0x40;
                for (int i = 0; i < 8; i++) P2_joypad_status[i] = 0x40;
                for (int i = 0; i < 65536; i++) NES_MEM[i] = 0;

                HardResetState();

                // FDS-specific state reset
                fds_ResetState();

                if (AnalogEnabled)
                {
                    SyncAnalogConfig();
                    Ntsc_Init(); Crt_Init();
                }

                initPalette();

                // Memory function pointer setup (FDS-specific)
                fds_InitFunction();
                InitOpHandlers();

                // APU & audio
                initAPU();
                AudioPlus_Init();

                // FDS expansion audio setup
                expansionChipType = ExpansionChipType.FDS;
                expansionChannelCount = 1;
                expansionChannels[0] = 0;
                mmix_UpdateChannelGains();

                // Auto-insert disk 0, side A
                fds_InsertDisk(0);

                // Read reset vector from BIOS ($FFFC/$FFFD)
                r_PC = (ushort)(mem_read_fun[0xFFFC](0xFFFC) | (mem_read_fun[0xFFFD](0xFFFD) << 8));
                Console.WriteLine("FDS: Reset vector = $" + r_PC.ToString("X4"));
            }
            catch (Exception e)
            {
                ShowError(e.Message);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Reset all FDS-specific state variables.
        /// </summary>
        static void fds_ResetState()
        {
            fdsMotorOn = false;
            fdsResetTransfer = false;
            fdsReadMode = true;
            fdsCrcControl = false;
            fdsDiskReady = false;
            fdsDiskIrqEnabled = false;
            fdsEndOfHead = true;
            fdsGapEnded = false;
            fdsScanningDisk = false;
            fdsTransferComplete = false;
            fdsBadCrc = false;
            fdsPreviousCrcControl = false;
            fdsDelay = 0;
            fdsCrcAccumulator = 0;
            fdsDiskPosition = 0;

            fdsIrqReloadValue = 0;
            fdsIrqCounter = 0;
            fdsIrqEnabled = false;
            fdsIrqRepeatEnabled = false;
            fdsTimerIrqPending = false;
            fdsDiskIrqPending = false;

            fdsDiskRegEnabled = false;
            fdsSoundRegEnabled = false;

            fdsWriteDataReg = 0;
            fdsReadDataReg = 0;
            fdsExtConWriteReg = 0x80; // bit7=1: battery good (BIOS checks this)

            fdsAutoDiskEjectCounter = -1;
            fdsAutoDiskSwitchCounter = -1;
            fdsPreviousDiskNumber = FDS_NO_DISK;
            fdsGameStarted = false;
            fdsLastDiskCheckFrame = 0;
            fdsSuccessiveChecks = 0;

            // Audio reset
            fdsVolSpeed = 0; fdsVolGain = 0;
            fdsVolEnvelopeOff = false; fdsVolIncrease = false;
            fdsVolFrequency = 0; fdsVolTimer = 0;
            fdsVolMasterSpeed = 0xE8;

            fdsModSpeed = 0; fdsModGain = 0;
            fdsModEnvelopeOff = false; fdsModIncrease = false;
            fdsModFrequency = 0; fdsModTimer = 0;
            fdsModMasterSpeed = 0xE8;
            fdsModCounter = 0; fdsModDisabled = false;
            for (int i = 0; i < 64; i++) { fdsModTable[i] = 0; fdsWaveTable[i] = 0; }
            fdsModTablePosition = 0; fdsModOverflowCounter = 0; fdsModOutput = 0;

            fdsWaveWriteEnabled = false;
            fdsDisableEnvelopes = false;
            fdsHaltWaveform = false;
            fdsMasterVolume = 0;
            fdsWaveOverflowCounter = 0;
            fdsWavePosition = 0;
            fdsLastAudioOutput = 0;
        }

        /// <summary>
        /// Free FDS-specific unmanaged memory.
        /// </summary>
        static void fds_FreeMemory()
        {
            if (fdsBiosRom != null) { Marshal.FreeHGlobal((IntPtr)fdsBiosRom); fdsBiosRom = null; }
            if (fdsPrgRam != null)  { Marshal.FreeHGlobal((IntPtr)fdsPrgRam);  fdsPrgRam = null; }
            fdsDiskSides = null;
            fdsDiskHeaders = null;
            fdsSideCount = 0;
            fdsDiskNumber = FDS_NO_DISK;
            isFDS = false;
        }

        // ── Memory function pointer setup (FDS-specific) ──────────────────
        // Strategy: use standard init_function() (which sets up PPU function pointers
        // correctly via MapperObj) then override CPU-side entries for FDS memory map.

        static void fds_InitFunction()
        {
            // init_function() uses MapperObj for PPU read/write — fdsChrMapper provides that.
            // It also sets CPU-side entries via MapperObj, which we'll override below.
            init_function();

            // Override CPU-side memory map for FDS
            for (int address = 0x4020; address < 0x4100; address++)
            {
                mem_write_fun[address] = fds_RegWrite;
                mem_read_fun[address] = fds_RegRead;
            }
            for (int address = 0x4100; address < 0x6000; address++)
            {
                mem_write_fun[address] = (addr, val) => { };
                mem_read_fun[address] = (addr) => cpubus;
            }
            for (int address = 0x6000; address < 0xE000; address++)
            {
                mem_write_fun[address] = fds_PrgRamWrite;
                mem_read_fun[address] = fds_PrgRamRead;
            }
            for (int address = 0xE000; address < 0x10000; address++)
            {
                mem_write_fun[address] = (addr, val) => { }; // BIOS is read-only
                mem_read_fun[address] = fds_BiosReadWithCheck;
            }
        }

        // ── Memory access handlers ─────────────────────────────────────────

        static byte fds_BiosReadWithCheck(ushort addr)
        {
            fds_CheckBiosAccess(addr);
            return fdsBiosRom[addr - 0xE000];
        }

        static byte fds_PrgRamRead(ushort addr)
        {
            return fdsPrgRam[addr - 0x6000];
        }

        static void fds_PrgRamWrite(ushort addr, byte val)
        {
            fdsPrgRam[addr - 0x6000] = val;
        }

        // ── Register read ($4020-$40FF) ────────────────────────────────────

        static byte fds_RegRead(ushort addr)
        {
            // Audio registers
            if (fdsSoundRegEnabled && addr >= 0x4040)
                return fds_AudioRead(addr);

            if (fdsDiskRegEnabled && addr <= 0x4033)
            {
                switch (addr)
                {
                    case 0x4030:
                    {
                        byte val = (byte)(cpubus & 0x24); // bits 2,5 are open bus
                        if (fdsTimerIrqPending) val |= 0x01;
                        if (fdsTransferComplete) val |= 0x02;
                        if (*Vertical == 0) val |= 0x08; // Horizontal mirroring
                        if (fdsBadCrc) val |= 0x10;

                        fdsTransferComplete = false;
                        fdsTimerIrqPending = false;
                        fdsDiskIrqPending = false;
                        fds_UpdateIrq();
                        return val;
                    }
                    case 0x4031:
                        fdsTransferComplete = false;
                        fdsDiskIrqPending = false;
                        fds_UpdateIrq();
                        return fdsReadDataReg;

                    case 0x4032:
                    {
                        byte val = (byte)(cpubus & 0xF8); // bits 3-7 open bus
                        bool inserted = fdsDiskNumber != FDS_NO_DISK;
                        if (!inserted) val |= 0x01;                        // Disk not in drive
                        if (!inserted || !fdsScanningDisk) val |= 0x02;    // Disk not ready
                        if (!inserted) val |= 0x04;                        // Disk not writable

                        // Auto-disk-switch detection
                        if (fdsGameStarted)
                        {
                            if (frame_count - fdsLastDiskCheckFrame < 100)
                                fdsSuccessiveChecks++;
                            else
                                fdsSuccessiveChecks = 0;
                            fdsLastDiskCheckFrame = frame_count;

                            if (fdsSuccessiveChecks > 20 && fdsAutoDiskEjectCounter == 0 && fdsAutoDiskSwitchCounter == -1)
                            {
                                fdsLastDiskCheckFrame = 0;
                                fdsSuccessiveChecks = 0;
                                fdsAutoDiskSwitchCounter = 77;
                                fdsPreviousDiskNumber = fdsDiskNumber;
                                fdsDiskNumber = FDS_NO_DISK;
                                Console.WriteLine("[FDS] Disk automatically ejected.");
                            }
                        }
                        return val;
                    }
                    case 0x4033:
                        return fdsExtConWriteReg;
                }
            }

            return cpubus; // open bus
        }

        // ── Register write ($4020-$40FF) ───────────────────────────────────

        static void fds_RegWrite(ushort addr, byte value)
        {
            // Gate: disk regs disabled blocks $4024-$4026
            if (!fdsDiskRegEnabled && addr >= 0x4024 && addr <= 0x4026) return;
            // Gate: sound regs disabled blocks $4040+
            if (!fdsSoundRegEnabled && addr >= 0x4040) return;

            switch (addr)
            {
                case 0x4020:
                    fdsIrqReloadValue = (ushort)((fdsIrqReloadValue & 0xFF00) | value);
                    break;
                case 0x4021:
                    fdsIrqReloadValue = (ushort)((fdsIrqReloadValue & 0x00FF) | (value << 8));
                    break;
                case 0x4022:
                    fdsIrqRepeatEnabled = (value & 0x01) != 0;
                    fdsIrqEnabled = (value & 0x02) != 0 && fdsDiskRegEnabled;
                    if (fdsIrqEnabled)
                        fdsIrqCounter = fdsIrqReloadValue;
                    else
                    {
                        fdsTimerIrqPending = false;
                        fds_UpdateIrq();
                    }
                    break;
                case 0x4023:
                    fdsDiskRegEnabled = (value & 0x01) != 0;
                    fdsSoundRegEnabled = (value & 0x02) != 0;
                    if (!fdsDiskRegEnabled)
                    {
                        fdsIrqEnabled = false;
                        fdsTimerIrqPending = false;
                        fdsDiskIrqPending = false;
                        fds_UpdateIrq();
                    }
                    break;
                case 0x4024:
                    fdsWriteDataReg = value;
                    fdsTransferComplete = false;
                    fdsDiskIrqPending = false;
                    fds_UpdateIrq();
                    break;
                case 0x4025:
                    fdsMotorOn = (value & 0x01) != 0;
                    fdsResetTransfer = (value & 0x02) != 0;
                    fdsReadMode = (value & 0x04) != 0;
                    *Vertical = (value & 0x08) != 0 ? 0 : 1; // bit3: 1=horizontal, 0=vertical
                    fdsCrcControl = (value & 0x10) != 0;
                    fdsDiskReady = (value & 0x40) != 0;
                    fdsDiskIrqEnabled = (value & 0x80) != 0;
                    // Writing $4025 clears disk IRQ (Mesen2/FCEUX/Nintendulator)
                    fdsDiskIrqPending = false;
                    fds_UpdateIrq();
                    break;
                case 0x4026:
                    fdsExtConWriteReg = value;
                    break;
                default:
                    if (addr >= 0x4040)
                        fds_AudioWrite(addr, value);
                    break;
            }
        }

        // ── CRC-16 ────────────────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void fds_UpdateCrc(byte value)
        {
            fdsCrcAccumulator ^= value;
            for (int n = 0; n < 8; n++)
            {
                byte carry = (byte)(fdsCrcAccumulator & 1);
                fdsCrcAccumulator >>= 1;
                if (carry != 0) fdsCrcAccumulator ^= 0x8408;
            }
        }

        // ── Disk I/O + IRQ (called every CPU cycle) ───────────────────────

        static void fds_CpuCycle()
        {
            // Auto disk insert logic
            fds_ProcessAutoDiskInsert();

            // IRQ timer
            fds_ClockIrq();

            // FDS audio
            fds_ClockAudio();

            // Disk I/O state machine
            if (fdsDiskNumber == FDS_NO_DISK || !fdsMotorOn)
            {
                fdsEndOfHead = true;
                fdsScanningDisk = false;
                return;
            }

            if (fdsResetTransfer && !fdsScanningDisk) return;

            if (fdsEndOfHead)
            {
                fdsDelay = FDS_HEAD_DELAY;
                fdsEndOfHead = false;
                fdsDiskPosition = 0;
                fdsGapEnded = false;
                return;
            }

            if (fdsDelay > 0)
            {
                fdsDelay--;
            }
            else
            {
                fdsScanningDisk = true;
                fdsAutoDiskEjectCounter = -1;
                fdsAutoDiskSwitchCounter = -1;

                byte diskData = 0;
                bool needIrq = fdsDiskIrqEnabled;

                if (fdsReadMode)
                {
                    // Read
                    diskData = fdsDiskSides[fdsDiskNumber][fdsDiskPosition];

                    if (!fdsPreviousCrcControl) fds_UpdateCrc(diskData);

                    if (!fdsDiskReady)
                    {
                        fdsGapEnded = false;
                        fdsCrcAccumulator = 0;
                        fdsBadCrc = false;
                    }
                    else if (diskData != 0 && !fdsGapEnded)
                    {
                        fdsGapEnded = true;
                        needIrq = false;
                    }

                    if (fdsGapEnded)
                    {
                        fdsTransferComplete = true;
                        fdsReadDataReg = diskData;
                        if (needIrq)
                        {
                            fdsDiskIrqPending = true;
                            fds_UpdateIrq();
                        }
                    }

                    if (!fdsPreviousCrcControl && fdsCrcControl)
                    {
                        // Gap-inserted disk images use fake CRC bytes (emulator convention).
                        // Real hardware stores valid CRC; since we can't validate fake CRC,
                        // always report CRC OK. This matches FCEUX/Nestopia behavior.
                        fdsBadCrc = false;
                    }
                }
                else
                {
                    // Write
                    if (!fdsCrcControl)
                    {
                        fdsTransferComplete = true;
                        diskData = fdsWriteDataReg;
                        if (needIrq)
                        {
                            fdsDiskIrqPending = true;
                            fds_UpdateIrq();
                        }
                    }

                    if (!fdsDiskReady)
                    {
                        diskData = 0x00;
                        fdsCrcAccumulator = 0;
                    }

                    if (!fdsCrcControl)
                        fds_UpdateCrc(diskData);
                    else
                    {
                        diskData = (byte)(fdsCrcAccumulator & 0xFF);
                        fdsCrcAccumulator >>= 8;
                    }

                    fdsDiskSides[fdsDiskNumber][fdsDiskPosition] = diskData;
                    fdsGapEnded = false;
                    fdsBadCrc = false;
                }

                fdsPreviousCrcControl = fdsCrcControl;

                fdsDiskPosition++;
                if (fdsDiskPosition >= fdsDiskSides[fdsDiskNumber].Length)
                {
                    fdsMotorOn = false;
                    if (fdsDiskIrqEnabled)
                    {
                        fdsDiskIrqPending = true;
                        fds_UpdateIrq();
                    }
                    fdsAutoDiskEjectCounter = 77;
                }
                else
                {
                    fdsDelay = FDS_BYTE_DELAY;
                }
            }
        }

        // ── IRQ Timer ──────────────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void fds_ClockIrq()
        {
            if (fdsIrqEnabled)
            {
                if (fdsIrqCounter == 0)
                {
                    fdsTimerIrqPending = true;
                    fds_UpdateIrq();
                    fdsIrqCounter = fdsIrqReloadValue;
                    if (!fdsIrqRepeatEnabled) fdsIrqEnabled = false;
                }
                else
                {
                    fdsIrqCounter--;
                }
            }
        }

        // ── Auto disk insert ───────────────────────────────────────────────

        static int fdsPreviousFrame;

        static void fds_ProcessAutoDiskInsert()
        {
            if (!fdsGameStarted) return;

            int currentFrame = frame_count;
            if (fdsPreviousFrame == currentFrame) return;
            fdsPreviousFrame = currentFrame;

            if (fdsAutoDiskEjectCounter > 0)
            {
                fdsAutoDiskEjectCounter--;
            }
            else if (fdsAutoDiskSwitchCounter > 0)
            {
                fdsAutoDiskSwitchCounter--;
                if (fdsAutoDiskSwitchCounter == 0)
                {
                    Console.WriteLine("[FDS] Auto-inserted dummy disk.");
                    fds_InsertDisk(0);
                }
            }
        }

        // ── Auto disk side detection (on BIOS reads) ──────────────────────

        /// <summary>
        /// Called when CPU reads from BIOS area. Detects disk switch requests.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void fds_CheckBiosAccess(ushort addr)
        {
            if (addr == 0xE18C && !fdsGameStarted)
            {
                // NMI entry: check if $100 & $C0 != 0 to detect game start
                if ((NES_MEM[0x100] & 0xC0) != 0)
                {
                    fdsGameStarted = true;
                    Console.WriteLine("[FDS] Game started.");
                }
            }
            else if (addr == 0xE445 && fdsGameStarted)
            {
                // Disk check routine — auto-select matching side
                ushort bufferAddr = (ushort)(NES_MEM[0] | (NES_MEM[1] << 8));
                byte[] buffer = new byte[10];
                for (int i = 0; i < 10; i++)
                {
                    ushort a = (ushort)(bufferAddr + i);
                    buffer[i] = (a != 0xE445) ? NES_MEM[a] : (byte)0;
                }

                int matchCount = 0, matchIndex = -1;
                for (int j = 0; j < fdsSideCount; j++)
                {
                    bool match = true;
                    for (int i = 0; i < 10; i++)
                    {
                        if (buffer[i] != 0xFF && buffer[i] != fdsDiskHeaders[j][i + 14])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) { matchCount++; matchIndex = matchCount > 1 ? -1 : j; }
                }

                if (matchIndex >= 0)
                {
                    fdsDiskNumber = matchIndex;
                    if (fdsDiskNumber != fdsPreviousDiskNumber)
                    {
                        Console.WriteLine("[FDS] Auto disk: Disk " + (fdsDiskNumber / 2 + 1) +
                            ((fdsDiskNumber & 1) != 0 ? " Side B" : " Side A"));
                        fdsPreviousDiskNumber = fdsDiskNumber;
                    }
                    if (matchIndex > 0) fdsGameStarted = true;
                }

                fdsAutoDiskSwitchCounter = -1;
            }
        }

        // ── Disk operations (public, for UI) ───────────────────────────────

        static public void fds_InsertDisk(int side)
        {
            if (fdsDiskNumber == FDS_NO_DISK && side >= 0 && side < fdsSideCount)
            {
                fdsDiskNumber = side;
                Console.WriteLine("[FDS] Inserted: Disk " + (side / 2 + 1) + ((side & 1) != 0 ? " Side B" : " Side A"));
            }
        }

        static public void fds_EjectDisk()
        {
            fdsDiskNumber = FDS_NO_DISK;
            Console.WriteLine("[FDS] Disk ejected.");
        }

        static public int fds_GetCurrentDisk() { return fdsDiskNumber; }
        static public int fds_GetSideCount() { return fdsSideCount; }

        // ── Nametable address helper ───────────────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int fds_NtAddr(int addr)
        {
            int nt = (addr >> 10) & 3;
            int offset = addr & 0x3FF;
            // Mirroring: Vertical=1 maps NT 0,1,0,1; Horizontal=0 maps NT 0,0,1,1
            if (*Vertical == 1) // vertical
                return 0x2000 + ((nt & 1) << 10) + offset;
            else // horizontal
                return 0x2000 + ((nt >> 1) << 10) + offset;
        }

        // ════════════════════════════════════════════════════════════════════
        // ── FDS Audio Implementation ────────────────────────────────────────
        // ════════════════════════════════════════════════════════════════════

        static void fds_ClockAudio()
        {
            int frequency = fdsVolFrequency;

            if (!fdsHaltWaveform && !fdsDisableEnvelopes)
            {
                fds_VolTickEnvelope();
                if (fds_ModTickEnvelope())
                    fds_ModUpdateOutput(frequency);
            }

            if (fds_TickModulator())
                fds_ModUpdateOutput(frequency);

            fds_UpdateAudioOutput();

            if (!fdsHaltWaveform && frequency + fdsModOutput > 0)
            {
                int pitch = frequency + fdsModOutput;
                fdsWaveOverflowCounter += (ushort)pitch;
                if (fdsWaveOverflowCounter < pitch) // overflow
                    fdsWavePosition = (byte)((fdsWavePosition + 1) & 0x3F);
            }
        }

        static void fds_UpdateAudioOutput()
        {
            if (fdsWaveWriteEnabled) return;

            uint level = (uint)(Math.Min((int)fdsVolGain, 32) * FdsWaveVolumeTable[fdsMasterVolume]);
            byte outputLevel = (byte)((fdsWaveTable[fdsWavePosition] * level) / 1152);

            if (fdsLastAudioOutput != outputLevel)
            {
                fdsLastAudioOutput = outputLevel;
            }

            // Output to expansion audio system
            expansionChannels[0] = outputLevel;
        }

        // Volume envelope tick
        static bool fds_VolTickEnvelope()
        {
            if (!fdsVolEnvelopeOff && fdsVolMasterSpeed > 0)
            {
                fdsVolTimer--;
                if (fdsVolTimer == 0)
                {
                    fdsVolTimer = (uint)(8 * (fdsVolSpeed + 1) * fdsVolMasterSpeed);
                    if (fdsVolIncrease && fdsVolGain < 32) fdsVolGain++;
                    else if (!fdsVolIncrease && fdsVolGain > 0) fdsVolGain--;
                    return true;
                }
            }
            return false;
        }

        // Mod envelope tick
        static bool fds_ModTickEnvelope()
        {
            if (!fdsModEnvelopeOff && fdsModMasterSpeed > 0)
            {
                fdsModTimer--;
                if (fdsModTimer == 0)
                {
                    fdsModTimer = (uint)(8 * (fdsModSpeed + 1) * fdsModMasterSpeed);
                    if (fdsModIncrease && fdsModGain < 32) fdsModGain++;
                    else if (!fdsModIncrease && fdsModGain > 0) fdsModGain--;
                    return true;
                }
            }
            return false;
        }

        // Modulator tick
        static readonly int[] fdsModLut = { 0, 1, 2, 4, 0xFF, -4, -2, -1 };

        static bool fds_TickModulator()
        {
            if (!fdsModDisabled && fdsModFrequency > 0)
            {
                fdsModOverflowCounter += fdsModFrequency;
                if (fdsModOverflowCounter < fdsModFrequency) // overflow
                {
                    int offset = fdsModLut[fdsModTable[fdsModTablePosition]];
                    fds_ModUpdateCounter(offset == 0xFF ? (sbyte)0 : (sbyte)(fdsModCounter + offset));
                    fdsModTablePosition = (byte)((fdsModTablePosition + 1) & 0x3F);
                    return true;
                }
            }
            return false;
        }

        static void fds_ModUpdateCounter(sbyte value)
        {
            fdsModCounter = value;
            if (fdsModCounter >= 64) fdsModCounter = (sbyte)(fdsModCounter - 128);
            else if (fdsModCounter < -64) fdsModCounter = (sbyte)(fdsModCounter + 128);
        }

        // Modulation output calculation (NesDev Wiki algorithm)
        static void fds_ModUpdateOutput(int volumePitch)
        {
            int temp = fdsModCounter * fdsModGain;
            int remainder = temp & 0xF;
            temp >>= 4;
            if (remainder > 0 && (temp & 0x80) == 0)
                temp += fdsModCounter < 0 ? -1 : 2;

            if (temp >= 192) temp -= 256;
            else if (temp < -64) temp += 256;

            temp = volumePitch * temp;
            remainder = temp & 0x3F;
            temp >>= 6;
            if (remainder >= 32) temp += 1;

            fdsModOutput = temp;
        }

        // Audio register read
        static byte fds_AudioRead(ushort addr)
        {
            byte val = (byte)(cpubus & 0xC0); // bits 6-7 are open bus
            if (addr <= 0x407F)
            {
                if (fdsWaveWriteEnabled)
                    val |= fdsWaveTable[addr & 0x3F];
                else
                    val |= fdsWaveTable[fdsWavePosition];
            }
            else if (addr == 0x4090)
                val |= fdsVolGain;
            else if (addr == 0x4092)
                val |= fdsModGain;
            return val;
        }

        // Audio register write
        static void fds_AudioWrite(ushort addr, byte value)
        {
            if (addr <= 0x407F)
            {
                if (fdsWaveWriteEnabled)
                    fdsWaveTable[addr & 0x3F] = (byte)(value & 0x3F);
                return;
            }

            switch (addr)
            {
                case 0x4080: // Volume envelope
                    fdsVolSpeed = (byte)(value & 0x3F);
                    fdsVolIncrease = (value & 0x40) != 0;
                    fdsVolEnvelopeOff = (value & 0x80) != 0;
                    fdsVolTimer = (uint)(8 * (fdsVolSpeed + 1) * fdsVolMasterSpeed);
                    if (fdsVolEnvelopeOff) fdsVolGain = fdsVolSpeed;
                    fds_ModUpdateOutput(fdsVolFrequency);
                    break;

                case 0x4082: // Freq low
                    fdsVolFrequency = (ushort)((fdsVolFrequency & 0x0F00) | value);
                    fds_ModUpdateOutput(fdsVolFrequency);
                    break;

                case 0x4083: // Freq high + control
                    fdsVolFrequency = (ushort)((fdsVolFrequency & 0xFF) | ((value & 0x0F) << 8));
                    fdsDisableEnvelopes = (value & 0x40) != 0;
                    fdsHaltWaveform = (value & 0x80) != 0;
                    if (fdsHaltWaveform) fdsWavePosition = 0;
                    if (fdsDisableEnvelopes)
                    {
                        fdsVolTimer = (uint)(8 * (fdsVolSpeed + 1) * fdsVolMasterSpeed);
                        fdsModTimer = (uint)(8 * (fdsModSpeed + 1) * fdsModMasterSpeed);
                    }
                    fds_ModUpdateOutput(fdsVolFrequency);
                    break;

                case 0x4084: // Mod envelope
                    fdsModSpeed = (byte)(value & 0x3F);
                    fdsModIncrease = (value & 0x40) != 0;
                    fdsModEnvelopeOff = (value & 0x80) != 0;
                    fdsModTimer = (uint)(8 * (fdsModSpeed + 1) * fdsModMasterSpeed);
                    if (fdsModEnvelopeOff) fdsModGain = fdsModSpeed;
                    fds_ModUpdateOutput(fdsVolFrequency);
                    break;

                case 0x4085: // Mod counter
                    fds_ModUpdateCounter((sbyte)(value & 0x7F));
                    fds_ModUpdateOutput(fdsVolFrequency);
                    break;

                case 0x4086: // Mod freq low
                    fdsModFrequency = (ushort)((fdsModFrequency & 0x0F00) | value);
                    break;

                case 0x4087: // Mod freq high + disable
                    fdsModFrequency = (ushort)((fdsModFrequency & 0xFF) | ((value & 0x0F) << 8));
                    fdsModDisabled = (value & 0x80) != 0;
                    if (fdsModDisabled) fdsModOverflowCounter = 0;
                    break;

                case 0x4088: // Mod table write
                    if (fdsModDisabled)
                    {
                        fdsModTable[fdsModTablePosition & 0x3F] = (byte)(value & 0x07);
                        fdsModTable[(fdsModTablePosition + 1) & 0x3F] = (byte)(value & 0x07);
                        fdsModTablePosition = (byte)((fdsModTablePosition + 2) & 0x3F);
                    }
                    break;

                case 0x4089: // Master volume + wave write
                    fdsMasterVolume = (byte)(value & 0x03);
                    fdsWaveWriteEnabled = (value & 0x80) != 0;
                    break;

                case 0x408A: // Master envelope speed
                    fdsVolMasterSpeed = value;
                    fdsModMasterSpeed = value;
                    break;
            }
        }
    }
}
