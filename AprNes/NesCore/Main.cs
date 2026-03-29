using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AprNes
{

    #region video & audio output event arg
    unsafe class VideoOut : EventArgs
    {
        //nothing need to pass
    }
    #endregion


    unsafe public partial class NesCore
    {


        public static event EventHandler VideoOutput;


        static VideoOut VideoOut_arg = new VideoOut();

        static int mapper;
        static byte PRG_ROM_count, CHR_ROM_count, ROM_Control_1, ROM_Control_2, RAM_banks_count;
        static byte* PRG_ROM, CHR_ROM;
        static bool NesHeaderV2 = false;
        static public bool HasBattery = false;
        static public string rom_file_name = "";

        static IMapper MapperObj;
        static public byte*[] chrBankPtrs = new byte*[8]; // P34: 8×1KB CHR bank pointers, updated by mapper
        static public bool mapperNeedsA12  = false; // any A12 notification needed (MMC3 or MMC2/4)
        static public bool mapperA12IsMmc3 = false; // true=MMC3-style, false=MMC2/4-style (only when mapperNeedsA12)
        // Mapper 68 (Sunsoft #4): CHR ROM pages used as nametable tiles
        static public byte*[] ntBankPtrs = new byte*[4]; // 4×1KB NT pointers (one per nametable slot)
        static public bool[] ntBankWritable = new bool[4]; // per-slot write enable (false = CHR-ROM, true = CIRAM)
        static public bool ntChrOverrideEnabled = false; // when true PPU reads NT from ntBankPtrs instead of ppu_ram
        // MMC5 CHR A/B auto-switch (for 8x16 sprites: A=sprites, B=background)
        static public bool chrABAutoSwitch = false;
        static public byte*[] chrBankPtrsA = new byte*[8]; // A set (sprites, $5120-$5127)
        static public byte*[] chrBankPtrsB = new byte*[8]; // B set (background, $5128-$512B)
        static public bool chrBGUseASet = false;           // MMC5 lastChrReg: true=use A set for BG
        // MMC5 Extended Attribute Mode (ExRAM mode 1)
        static public bool extAttrEnabled = false;
        static public byte* extAttrRAM = null;       // ExRAM pointer (1KB)
        static public byte extAttrChrUpperBits = 0;  // $5130 upper bits
        static public byte* extAttrCHR = null;       // CHR-ROM base pointer
        static public int extAttrChrSize = 0;        // CHR-ROM size for wrapping
        // MMC5 direct reference for PPU → mapper VRAM read notifications
        static public Mapper005 mmc5Ref = null;

        // ROM info accessors (read-only, set during init)
        static public int  RomMapper   => mapper;
        static public int  RomPrgCount => PRG_ROM_count;
        static public int  RomChrCount => CHR_ROM_count;
        static public bool RomHorizMirror => (ROM_Control_1 & 1) == 0;

        // FPS limiting flag (set by UI, checked in VideoOutput handler)
        static public bool LimitFPS = false;

        // Accuracy option: per-dot secondary OAM evaluation FSM (dots 1-64 clear, 65-256 evaluate)
        // true = full hardware accuracy; false = skip FSM for ~13% performance gain (no test failures)
        static public bool AccuracyOptA = false;

        // TV system region
        public enum RegionType { NTSC, PAL, Dendy }
        static public RegionType Region = RegionType.NTSC;

        // ── Region-dependent timing parameters (set by ApplyRegionProfile) ──
        static int regionMode     = 0;        // 0=NTSC, 1=PAL, 2=Dendy (for hot-path if-else branching)
        static int totalScanlines = 262;      // NTSC=262, PAL/Dendy=312
        static int preRenderLine  = 261;      // NTSC=261, PAL/Dendy=311
        static int nmiTriggerLine = 241;      // NTSC/PAL=241, Dendy=291
        static int masterPerCpu   = 12;       // NTSC=12, PAL=16, Dendy=15
        static int masterPerPpu   = 4;        // NTSC=4,  PAL/Dendy=5
        static double cpuFreq          = 1789773.0;  // NTSC=1789773, PAL=1662607, Dendy=1773447
        static public double FrameSeconds = 1.0 / 60.0988; // NTSC=1/60.0988, PAL/Dendy=1/50.0070

        static void ApplyRegionProfile()
        {
            if (Region == RegionType.PAL)
            {
                regionMode     = 1;
                totalScanlines = 312;
                preRenderLine  = 311;
                nmiTriggerLine = 241;
                masterPerCpu   = 16;
                masterPerPpu   = 5;
                cpuFreq        = 1662607.0;
                FrameSeconds   = 1.0 / 50.0070;
            }
            else if (Region == RegionType.Dendy)
            {
                regionMode     = 2;
                totalScanlines = 312;
                preRenderLine  = 311;
                nmiTriggerLine = 291;   // 51 lines post-render idle (240-290), NMI at 291
                masterPerCpu   = 15;    // PAL master clock ÷15
                masterPerPpu   = 5;     // same as PAL
                cpuFreq        = 1773447.0; // 26601712 / 15
                FrameSeconds   = 1.0 / 50.0070;
            }
            else // NTSC
            {
                regionMode     = 0;
                totalScanlines = 262;
                preRenderLine  = 261;
                nmiTriggerLine = 241;
                masterPerCpu   = 12;
                masterPerPpu   = 4;
                cpuFreq        = 1789773.0;
                FrameSeconds   = 1.0 / 60.0988;
            }
        }

        // ── AudioPlus 音訊引擎設定 ──────────────────────────────────
        // AudioMode: 0=Pure Digital, 1=Authentic, 2=Modern
        static public int AudioMode = 0;

        // ── Authentic 模式設定 ──
        // ConsoleModel: 0=Famicom, 1=Front-Loader, 2=Top-Loader, 3=AV Famicom, 4=Sharp Twin, 5=Sharp Titler, 6=Custom
        static public int ConsoleModel = 0;
        static public bool RfCrosstalk = false;         // RF 音訊洩漏干擾
        static public int CustomLpfCutoff = 14000;      // Custom 模式 LPF 截止頻率 (Hz, 1000-22000)
        static public bool CustomBuzz = false;           // Custom 模式 60Hz buzz 開關
        static public int BuzzAmplitude = 30;            // Buzz 振幅 (0-100, 映射 0.000~0.010)
        static public int BuzzFreq = 60;                 // Buzz 頻率 (50 或 60 Hz)
        static public int RfVolume = 50;                 // RF 串擾音量 (0-200, 映射 0.00~0.20)

        // ── Modern 模式設定 ──
        static public int StereoWidth = 50;              // 立體聲寬度 (0-100%)
        static public int HaasDelay = 20;                // Haas 延遲 (10-30 ms)
        static public int HaasCrossfeed = 40;            // Haas crossfeed 比例 (0-80%)
        static public int ReverbWet = 0;                 // 殘響濕度 (0-30%)
        static public int CombFeedback = 70;             // Comb 回饋增益 (30-90%)
        static public int CombDamp = 30;                 // Comb 高頻阻尼 (10-70%)
        static public int BassBoostDb = 0;               // Triangle 低音增強 (0-12 dB)
        static public int BassBoostFreq = 150;           // 低音增強中心頻率 (80-300 Hz)

        // 類比訊號模擬模式 (Level 2 NTSC signal simulation)
        // false = 傳統調色盤查表（預設）
        // true  = NTSC 電壓波形生成 + YIQ 解碼重採樣
        static public bool AnalogEnabled = false;

        // Ultra 類比模式：開啟後使用完整物理模擬（21.477 MHz 時域波形 + coherent demodulation）
        // false（預設）= Level 2 簡化路徑（直接 YIQ + LUT dot crawl）
        // true          = Level 3 物理路徑（Step 1 波形 + Step 2 解調 + Step 3 YIQ→RGB）
        static public bool UltraAnalog = false;

        // CRT 電子束光學模擬（UltraAnalog=true 時有效）
        // false = 跳過 Stage 2（CrtScreen），物理解調後直接輸出至 AnalogScreenBuf
        // true  = 完整兩階段管線：Stage 1 → linearBuffer → Stage 2 CrtScreen → AnalogScreenBuf
        static public bool CrtEnabled = true;

        // 類比輸出端子模式（AnalogEnabled=true 時有效）
        // AV     = Composite：Y+C 混合，標準 IIR 解碼，產生 Dot Crawl / 色彩暈染
        // SVideo = S-Video：Y/C 分離傳輸，較銳利，色彩暈染較少
        // RF     = RF 射頻：額外 AM 調變/解調，雜訊最多，
        //          並包含音訊載波洩漏干擾（Buzz bar、音量振幅調變視訊亮度）
        // AnalogOutputMode enum 已移至 namespace AprNes 層級（Ntsc.cs），供獨立 library 使用
        static public AnalogOutputMode AnalogOutput = AnalogOutputMode.AV;

        // 類比輸出尺寸（2/4/6/8，預設 4）。對應像素：256×N × 210×N（8:7 AR）
        // 2→512×420, 4→1024×840, 6→1536×1260, 8→2048×1680
        static public int AnalogSize = 4;

        // 類比模式輸出緩衝區（CrtScreen Stage 2 寫入，Render_Analog 讀取）
        // 僅在 AnalogEnabled=true 時分配，其他情況為 null
        static public uint* AnalogScreenBuf = null;
        static public int   AnalogBufSize   = 0;  // 目前已分配的 pixel 數（DstW×DstH）

        // 錄影用：目前 RenderObj 的最終輸出緩衝區指標與尺寸（由各 Render class init() 設定）
        static public uint* RenderOutputPtr = null;
        static public int   RenderOutputW   = 256;
        static public int   RenderOutputH   = 240;

        static int* Vertical; //  Vertical = false,

        static public ManualResetEvent _event = new ManualResetEvent(true);

        static public Action<string> OnError;

        static public void ShowError(string msg)
        {
            OnError?.Invoke(msg);
        }

        static void FreeUnmanagedMemory()
        {
            fds_FreeMemory();
            if (MapperObj != null) { MapperObj.Cleanup(); MapperObj = null; }
            if (PRG_ROM      != null) { Marshal.FreeHGlobal((IntPtr)PRG_ROM);      PRG_ROM      = null; }
            if (CHR_ROM      != null) { Marshal.FreeHGlobal((IntPtr)CHR_ROM);      CHR_ROM      = null; }
            if (ScreenBuf1x  != null) { Marshal.FreeHGlobal((IntPtr)ScreenBuf1x);  ScreenBuf1x  = null; }
            if (Buffer_BG_array != null) { Marshal.FreeHGlobal((IntPtr)Buffer_BG_array); Buffer_BG_array = null; }
            if (NesColors    != null) { Marshal.FreeHGlobal((IntPtr)NesColors);    NesColors    = null; }
            if (palCacheR    != null) { Marshal.FreeHGlobal((IntPtr)palCacheR);    palCacheR    = null; }
            if (palCacheN    != null) { Marshal.FreeHGlobal((IntPtr)palCacheN);    palCacheN    = null; }
            if (spr_ram      != null) { Marshal.FreeHGlobal((IntPtr)spr_ram);      spr_ram      = null; }
            if (secondaryOAM != null) { Marshal.FreeHGlobal((IntPtr)secondaryOAM); secondaryOAM = null; }
            if (corruptOamRow!= null) { Marshal.FreeHGlobal((IntPtr)corruptOamRow);corruptOamRow= null; }
            if (ppu_ram      != null) { Marshal.FreeHGlobal((IntPtr)ppu_ram);      ppu_ram      = null; }
            if (P1_joypad_status != null) { Marshal.FreeHGlobal((IntPtr)P1_joypad_status); P1_joypad_status = null; }
            if (P2_joypad_status != null) { Marshal.FreeHGlobal((IntPtr)P2_joypad_status); P2_joypad_status = null; }
            if (NES_MEM      != null) { Marshal.FreeHGlobal((IntPtr)NES_MEM);      NES_MEM      = null; }
            if (Vertical           != null) { Marshal.FreeHGlobal((IntPtr)Vertical);           Vertical           = null; }
            if (AnalogScreenBuf  != null) { Marshal.FreeHGlobal((IntPtr)AnalogScreenBuf);  AnalogScreenBuf  = null; AnalogBufSize = 0; }
        }

        /// <summary>
        /// 將 NesCore 的類比參數同步至 Ntsc / CrtScreen 模組（解耦橋接）。
        /// 在 Init、設定變更、AnalogScreenBuf 重新分配後呼叫。
        /// </summary>
        static public void SyncAnalogConfig()
        {
            Ntsc_ApplyConfig(
                analogOutput:    (int)AnalogOutput,
                ultraAnalog:     UltraAnalog,
                analogSize:      AnalogSize,
                crtEnabled:      CrtEnabled,
                analogScreenBuf: AnalogScreenBuf
            );
            Crt_ApplyConfig(
                analogOutput:    (int)AnalogOutput,
                analogSize:      AnalogSize,
                analogScreenBuf: AnalogScreenBuf
            );
        }

        static void HardResetState()
        {
            // CPU registers (6502 power-up state)
            r_A = 0; r_X = 0; r_Y = 0; r_SP = 0xFD;
            flagN = 0; flagV = 0; flagD = 0; flagI = 1; flagZ = 0; flagC = 0;
            opcode = 0; operationCycle = 0;
            cpubus = 0; cpuBusAddr = 0; addressBus = 0; dl = 0; ignoreH = false;
            m2PhaseIsWrite = false;

            // CPU interrupt state
            nmi_pending = false; nmi_delay_cycle = -1; nmi_output_prev = false;
            irq_pending = false; irqLinePrev = false; irqLineCurrent = false;
            statusmapperint = false;
            doNMI = false; doIRQ = false; doReset = false; doBRK = false; softreset = false;

            // DMA state
            dmaNeedHalt = false; dmcNeedDummyRead = false; dmcDmaRunning = false;
            spriteDmaTransfer = false; spriteDmaOffset = 0;
            dmaPrevReadAddress = 0; dmaReadSkipBusUpdate = false;

            // PPU control registers ($2000/$2001/$2002)
            BaseNameTableAddr = 0; VramaddrIncrement = 1;
            SpPatternTableAddr = 0; BgPatternTableAddr = 0;
            Spritesize8x16 = false; NMIable = false;
            ShowBackGround = false; ShowSprites = false;
            ShowBgLeft8 = true; ShowSprLeft8 = true;
            isSpriteOverflow = false; isSprite0hit = false; isVblank = false;
            SuppressVbl = false;

            // PPU VRAM address / scroll
            vram_addr_internal = 0; vram_addr = 0; scrol_y = 0; FineX = 0;
            vram_latch = false;
            ppu_2007_buffer = 0; ppu_2007_temp = 0; ppu2007ReadCooldown = 0;
            ppu2006UpdateDelay = 0; ppu2006PendingAddr = 0;
            openbus = 0; open_bus_decay_timer = 77777;

            // PPU scan position & frame state
            ppu_cycles_x = 0; scanline = -1; frame_count = 0;
            oddSwap = false; ppuRenderingEnabled = false; prevRenderingEnabled = false;
            spr_ram_add = 0;

            // PPU tile pipeline
            NTVal = 0; ATVal = 0; lowTile = 0; highTile = 0; ioaddr = 0;
            lowshift = 0; highshift = 0;
            lowshift_s0 = 0; highshift_s0 = 0;
            bg_attr_p1 = 0; bg_attr_p2 = 0; bg_attr_p3 = 0;

            // PPU sprite state
            sprite0_on_line = false; sprite0_line_x = 0;
            sprite0_tile_low = 0; sprite0_tile_high = 0; sprite0_flip_x = false;
            prerender_sprite0_valid = false; prerender_sprite0_x = 0;
            prerender_sprite0_tile_low = 0; prerender_sprite0_tile_high = 0;
            prerender_sprite0_flip_x = false;
            spriteOverflowCycle = 0;

            // JoyPad
            P1_LastWrite = 0; strobeWritePending = 0; strobeWriteValue = 0;
        }

        static public bool init(byte[] rom_bytes) //for Hard Reset effect
        {
            FreeUnmanagedMemory();
            isFDS = false; // ensure FDS mode is off when loading normal NES ROMs
            try
            {
                //http://nesdev.com/iNES.txt
                //https://github.com/dsedivec/inestool/blob/master/inestool.py
                if (!(rom_bytes[0] == 'N' && rom_bytes[1] == 'E' && rom_bytes[2] == 'S' && rom_bytes[3] == 0x1a))
                {
                    ShowError("Bad Magic Number !");
                    return false;
                }
                Console.WriteLine("iNes header");


                Vertical = (int*)Marshal.AllocHGlobal(sizeof(int));

                PRG_ROM_count = rom_bytes[4];
                Console.WriteLine("PRG-ROM count : " + PRG_ROM_count);

                int PRG_ROM_count_needs = PRG_ROM_count;
                if (PRG_ROM_count == 1) PRG_ROM_count_needs = 2;//min PRG ROM is 2
                PRG_ROM = (byte*)Marshal.AllocHGlobal(sizeof(byte) * PRG_ROM_count_needs * 16384);
                for (int i = 0; i < PRG_ROM_count * 16384; i++) PRG_ROM[i] = rom_bytes[16 + i];
                if (PRG_ROM_count == 1) for (int i = 0; i < PRG_ROM_count * 16384; i++) PRG_ROM[i + 16384] = rom_bytes[16 + i]; // if only 1 RPG_ROM ,copy to another space

                CHR_ROM_count = rom_bytes[5];
                // Validate: clamp CHR_ROM_count to actual file data to handle corrupt headers
                {
                    int chrOffset = PRG_ROM_count * 16384 + 16;
                    int maxChrBanks = (rom_bytes.Length - chrOffset) / 8192;
                    if (CHR_ROM_count > maxChrBanks)
                    {
                        Console.WriteLine($"Warning: header claims {CHR_ROM_count} CHR banks but file only has {maxChrBanks}. Clamping.");
                        CHR_ROM_count = (byte)maxChrBanks;
                    }
                }
                Console.WriteLine("CHR-ROM count : " + CHR_ROM_count);

                if (CHR_ROM_count != 0)
                {
                    CHR_ROM = (byte*)Marshal.AllocHGlobal(sizeof(byte) * CHR_ROM_count * 8192);
                    for (int i = 0; i < CHR_ROM_count * 8192; i++)
                        CHR_ROM[i] = rom_bytes[PRG_ROM_count * 16384 + 16 + i];
                }

                ROM_Control_1 = rom_bytes[6];
                ROM_Control_2 = rom_bytes[7];

                if ((ROM_Control_1 & 1) != 0)
                {
                    *Vertical = 1;// true;
                    Console.WriteLine("vertical mirroring");
                }
                else
                {
                    *Vertical = 0;// false;
                    Console.WriteLine("horizontal mirroring");
                }

                if ((ROM_Control_1 & 2) != 0)
                {
                    HasBattery = true;
                    Console.WriteLine("battery-backed RAM : yes");
                }
                else
                {
                    HasBattery = false;
                    Console.WriteLine("battery-backed RAM : no");
                }

                if ((ROM_Control_1 & 4) != 0) Console.WriteLine("trainer : yes");
                else Console.WriteLine("trainer : no");

                if ((ROM_Control_1 & 8) != 0)
                {
                    Console.WriteLine("fourscreen mirroring : yes");
                }
                else Console.WriteLine("fourscreen mirroring : no");

                // https://wiki.nesdev.com/w/index.php/NES_2.0
                if ((ROM_Control_2 & 0xf) != 0)
                {
                    mapper = (ROM_Control_1 & 0xf0) >> 4;
                    if ((ROM_Control_2 & 0xc) == 8)
                    {
                        NesHeaderV2 = true;
                        mapper = (byte)(((ROM_Control_1 & 0xf0) >> 4) | (ROM_Control_2 & 0xf0));
                        Console.WriteLine("Nes header 2.0 version !");
                    }
                    else
                    {
                        mapper = (ROM_Control_1 & 0xf0) >> 4;
                        Console.WriteLine("Old style Mapper info !");
                    }
                }
                else mapper = (byte)(((ROM_Control_1 & 0xf0) >> 4) | (ROM_Control_2 & 0xf0));
                Console.WriteLine("Mapper number : " + mapper);
                if (!MapperRegistry.IsSupported(mapper))
                {
                    ShowError("not support mapper ! " + mapper);
                    return false;
                }
                if (NesHeaderV2)
                {
                    RAM_banks_count = rom_bytes[8];
                    Console.WriteLine("RAM banks count : " + RAM_banks_count);
                }

                //init allocate
                ScreenBuf1x      = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 61440);
                if (AnalogEnabled)
                {
                    SyncAnalogConfig();  // 確保 Crt_DstW/DstH 使用正確的 AnalogSize
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

                // Compute PRG+CHR CRC32 (skip 16-byte iNES header, matching Mesen2 DB format)
                uint romCrc = 0xFFFFFFFF;
                int trainerOffset = ((ROM_Control_1 & 4) != 0) ? 512 : 0;
                int prgChrStart = 16 + trainerOffset;
                for (int i = prgChrStart; i < rom_bytes.Length; i++)
                {
                    romCrc ^= rom_bytes[i];
                    for (int j = 0; j < 8; j++)
                        romCrc = (romCrc >> 1) ^ (((romCrc & 1) != 0) ? 0xEDB88320u : 0);
                }
                romCrc ^= 0xFFFFFFFF;
                Console.WriteLine("ROM CRC32: " + romCrc.ToString("X8"));
                RomDbEntry dbEntry = RomDatabase.Lookup(romCrc);
                if (!dbEntry.IsNone)
                    Console.WriteLine("ROM DB: " + dbEntry.Name);
                MapperObj = MapperRegistry.Create(mapper, dbEntry);
                var a12mode = MapperObj.A12NotifyMode;
                mapperNeedsA12  = a12mode != MapperA12Mode.None;
                mapperA12IsMmc3 = a12mode == MapperA12Mode.MMC3;
                ntChrOverrideEnabled = false;
                for (int i = 0; i < 4; i++) ntBankWritable[i] = true;
                chrABAutoSwitch = false;
                chrBGUseASet = false;
                extAttrEnabled = false;
                mmc5Ref = null;
                MapperObj.MapperInit(PRG_ROM, CHR_ROM, ppu_ram, PRG_ROM_count, CHR_ROM_count, Vertical);
                MapperObj.Reset();
                if (!dbEntry.IsNone && dbEntry.MirrorOverride >= 0)
                    *Vertical = dbEntry.MirrorOverride;
                MapperObj.UpdateCHRBanks();

                for (int i = 0; i < 61440; i++) ScreenBuf1x[i] = 0;
                for (int i = 0; i < 16384; i++) ppu_ram[i] = 0;
                for (int i = 0; i < 256; i++) spr_ram[i] = 0;
                for (int i = 0; i < 32; i++) { secondaryOAM[i] = 0; corruptOamRow[i] = 0; }
                for (int i = 0; i < 8; i++) P1_joypad_status[i] = 0x40;
                for (int i = 0; i < 8; i++) P2_joypad_status[i] = 0x40;
                for (int i = 0; i < 65536; i++) NES_MEM[i] = 0;

                ApplyRegionProfile(); // set timing parameters before any subsystem init
                HardResetState();  // reset all CPU/PPU/DMA static state

                if (AnalogEnabled)
                {
                    SyncAnalogConfig();  // buffer 已分配，同步完整參數
                    Ntsc_Init(); Crt_Init();
                }

                initPalette();
                initPaletteRam();

                //init function array
                init_function();
                InitOpHandlers();

                //init APU & audio output (must be before reset vector read so tick() can run)
                initAPU();

                // AudioPlus 管線初始化
                AudioPlus_Init();

                //init cpu pc (read reset vector — no tick needed, boot cycles already counted)
                r_PC = (ushort)(mem_read_fun[0xfffc](0xfffc) | (mem_read_fun[0xfffd](0xfffd) << 8));


            }
            catch (Exception e)
            {
                ShowError(e.Message);
                return false;
            }
            return true;
        }

        static public void LoadSRam(byte[] data)
        {
            for (int i = 0; i < 0x2000; i++) NES_MEM[i + 0x6000] = data[i];
        }

        static public byte[] DumpSRam()
        {
            byte[] buf = new byte[0x2000];
            for (int i = 0; i < 0x2000; i++) buf[i] = NES_MEM[i + 0x6000];
            return buf;
        }

        static public void run()
        {
            bool nmi_just_deferred = false;
            while (!exit)
            {
                // === Interrupt service point (before instruction fetch) ===
                if (nmi_pending && !nmi_just_deferred)
                {
                    nmi_pending = false;
                    doNMI = true;
                    irq_pending = false; // NMI handler sets I=1; IRQ re-polled after RTI
                }
                else if (nmi_just_deferred)
                {
                    // Deferred NMI: skip this iteration, let 1 instruction run first
                    nmi_just_deferred = false;
                }
                else if (irq_pending)
                {
                    irq_pending = false;
                    doIRQ = true;
                }

                byte prevFlagI_run = flagI; // capture I flag before instruction for IRQ delay
                cpu_step();

                // BRK/NMI/IRQ: NMI arising during sequence → defer 1 instruction
                if (opcode == 0x00 && nmi_pending)
                    nmi_just_deferred = true;

                // === IRQ polling (end of instruction, for next instruction) ===
                if (opcode != 0x00)
                {
                    byte irqPollI = (opcode == 0x40) ? flagI : prevFlagI_run;
                    irq_pending = (irqPollI == 0 && irqLinePrev);
                }
            }
            Console.WriteLine("exit..");
        }
    }

}
