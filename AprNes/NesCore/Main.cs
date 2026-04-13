using System;
using System.Runtime.CompilerServices;
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

        // TV system region
        public enum RegionType { NTSC, PAL, Dendy }
        static public RegionType Region = RegionType.NTSC;

        // ── Region-dependent timing parameters (set by ApplyRegionProfile) ──
        static int preRenderLine  = 261;      // NTSC=261, PAL/Dendy=311
        static int nmiTriggerLine = 241;      // NTSC/PAL=241, Dendy=291
        static int masterPerCpu   = 12;       // NTSC=12, PAL=16, Dendy=15 — used by init() to seed mc*Clock
        static int masterPerPpu   = 4;        // NTSC=4,  PAL=5,  Dendy=5
        static double cpuFreq          = 1789773.0;  // NTSC=1789773, PAL=1662607, Dendy=1773447
        static public double FrameSeconds = 1.0 / 60.0988; // NTSC=1/60.0988, PAL/Dendy=1/50.0070

        static void ApplyRegionProfile()
        {
            if (Region == RegionType.PAL)
            {
                preRenderLine  = 311;
                nmiTriggerLine = 241;      // PAL VBL starts at same scanline as NTSC
                masterPerCpu   = 16;
                masterPerPpu   = 5;
                cpuFreq        = 1662607.0;
                FrameSeconds   = 1.0 / 50.0070;
            }
            else if (Region == RegionType.Dendy)
            {
                preRenderLine  = 311;
                nmiTriggerLine = 291;      // Dendy: 51 extra post-render idle lines
                masterPerCpu   = 15;
                masterPerPpu   = 5;
                cpuFreq        = 1773447.0;
                FrameSeconds   = 1.0 / 50.0070;
            }
            else // NTSC
            {
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

        // Async double buffer for analog mode
        // AnalogScreenBuf = front buffer (模擬端寫入, CRT render 目標)
        // AnalogScreenBufBack = back buffer (GDI 讀取上一幀)
        static public uint* AnalogScreenBufBack = null;
        // 渲染執行緒同步事件
        static public ManualResetEventSlim analogRenderReady = new ManualResetEventSlim(false);
        static public ManualResetEventSlim analogRenderDone  = new ManualResetEventSlim(true); // 初始已完成
        static public volatile bool analogRenderThreadRunning = false;

        /// <summary>
        /// 交換 front/back buffer 指標，並更新 CRT/NTSC 的 buffer 指標。
        /// 呼叫後 AnalogScreenBuf 指向新的空 front buffer（模擬寫入），
        /// AnalogScreenBufBack 指向剛完成的幀（GDI 讀取）。
        /// 注意：只更新 buffer 指標，不同步 AnalogSize 等設定參數
        ///（避免 UI thread 已改 AnalogSize 但 weight tables 未重建的不一致）。
        /// </summary>
        static public void SwapAnalogBuffers()
        {
            var tmp = AnalogScreenBuf;
            AnalogScreenBuf = AnalogScreenBufBack;
            AnalogScreenBufBack = tmp;
            // 只更新 CRT/NTSC 的 buffer 指標（不改 analogSize 等參數）
            Ntsc_UpdateScreenBuf(AnalogScreenBuf);
            Crt_UpdateScreenBuf(AnalogScreenBuf);
        }

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
            if (spr_ram      != null) { Marshal.FreeHGlobal((IntPtr)spr_ram);      spr_ram      = null; }
            if (secondaryOAM != null) { Marshal.FreeHGlobal((IntPtr)secondaryOAM); secondaryOAM = null; }
            if (corruptOamRow!= null) { Marshal.FreeHGlobal((IntPtr)corruptOamRow);corruptOamRow= null; }
            if (ppu_ram      != null) { Marshal.FreeHGlobal((IntPtr)ppu_ram);      ppu_ram      = null; }
            if (FlipTable    != null) { Marshal.FreeHGlobal((IntPtr)FlipTable);    FlipTable    = null; }
            if (sprShiftL    != null) { Marshal.FreeHGlobal((IntPtr)sprShiftL);    sprShiftL    = null; }
            if (sprShiftH    != null) { Marshal.FreeHGlobal((IntPtr)sprShiftH);    sprShiftH    = null; }
            if (sprXCounter  != null) { Marshal.FreeHGlobal((IntPtr)sprXCounter);  sprXCounter  = null; }
            if (sprFetchAttr != null) { Marshal.FreeHGlobal((IntPtr)sprFetchAttr); sprFetchAttr = null; }
            if (sprXPos      != null) { Marshal.FreeHGlobal((IntPtr)sprXPos);      sprXPos      = null; }
            if (expansionChannels != null) { Marshal.FreeHGlobal((IntPtr)expansionChannels); expansionChannels = null; }
            if (ntscScanBuf  != null) { Marshal.FreeHGlobal((IntPtr)ntscScanBuf);  ntscScanBuf  = null; }
            if (NES_MEM      != null) { Marshal.FreeHGlobal((IntPtr)NES_MEM);      NES_MEM      = null; }
            if (Vertical           != null) { Marshal.FreeHGlobal((IntPtr)Vertical);           Vertical           = null; }
            if (AnalogScreenBuf     != null) { Marshal.FreeHGlobal((IntPtr)AnalogScreenBuf);     AnalogScreenBuf     = null; AnalogBufSize = 0; }
            if (AnalogScreenBufBack != null) { Marshal.FreeHGlobal((IntPtr)AnalogScreenBufBack); AnalogScreenBufBack = null; }
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
            // CPU registers (6502 power-up state, TriCNES values)
            r_A = 0; r_X = 0; r_Y = 0; r_SP = 0x00; // hardware: SP=0, BRK/RESET decrements to 0xFD
            r_PC = 0xFFFF; // TriCNES: nondeterministic, uses 0xFFFF (RESET handler reads vector)
            flagN = 0; flagV = 0; flagD = 0; flagI = 1; flagZ = 0; flagC = 0;
            opcode = 0; operationCycle = 0;
            cpubus = 0; cpuBusAddr = 0; addressBus = 0; dl = 0; ignoreH = false;
            cpuIsRead = true;


            // CPU interrupt state
            NMILine = false; nmiPinsSignal = false; nmiPrevPinsSignal = false;
            IRQLine = false; irqLineCurrent = false;
            statusmapperint = false;
            doNMI = false; doIRQ = false; doReset = true; doBRK = false; softreset = false;
            // doReset=true: BRK/RESET handler reads reset vector via MasterClockTick

            // DMA state (TriCNES per-cycle model)
            dmcDmaRunning = false; dmcDmaHalt = false;
            spriteDmaTransfer = false; spriteDmaOffset = 0;
            dmaOamHalt = false; dmaOamAligned = false; dmaFirstCycleOam = false;
            dmaOamInternalBus = 0; dmaOamAddr = 0;

            // PPU control registers ($2000/$2001/$2002)
            BaseNameTableAddr = 0; VramaddrIncrement = 1;
            SpPatternTableAddr = 0; BgPatternTableAddr = 0;
            Spritesize8x16 = false; NMIable = false;
            ShowBackGround = false; ShowSprites = false;
            ShowBgLeft8 = true; ShowSprLeft8 = true;
            isSpriteOverflow = false; isSprite0hit = false; isVblank = false;
            ppuVSET = false; ppuVSET_Latch1 = false; ppuVSET_Latch2 = false;
            pendingSprite0Hit2 = false;
            isSprite0hit_Delayed = false; isSpriteOverflow_Delayed = false;
            ppu2002ReadPending = false;

            // PPU VRAM address / scroll
            vram_addr_internal = 0; vram_addr = 0; FineX = 0;
            vram_latch = false;
            ppu_2007_buffer = 0;
            // SR latch pipeline reset
            ppu2007_Read_SR = false;
            ppu2007_Write_SR = false;
            ppu2007_PD_RB = false; ppu2007_ReadALE = false; ppu2007_WriteALE = false;
            ppu2007_DB_PAR = false; ppu2007_TStep_Latch = false; ppu2007_TStep = false;
            readLatch = 0x0A; writeLatch = 0x0A;  // idle: {F,T,F,T,F} = 0x0A
            ppu2006UpdateDelay = 0; ppu2006PendingAddr = 0;
            openbus = 0; open_bus_decay_timer = 77777;

            // PPU scan position & frame state (TriCNES power-on values)
            ppu_cycles_x = 7; scanline = 0; frame_count = 0;  // TriCNES: PPU_Dot=7, PPU_Scanline=0
            oddSwap = true; ppuRenderingEnabled = false; prevRenderingEnabled = false; // TriCNES: PPU_OddFrame=true
            ShowBG_EvalDelay = false; ShowSpr_EvalDelay = false;
            // deferred CXinc flag
            // Per-sprite shift registers
            for (int i = 0; i < 8; i++)
            { sprShiftL[i] = 0; sprShiftH[i] = 0; sprXCounter[i] = 0; sprFetchAttr[i] = 0; sprXPos[i] = 0; }
            sprSlotCount = 0; sprZeroInSlots = false;
            // 3-dot pixel pipeline
            dotColor = prevDotColor = prevPrevDotColor = prevPrevPrevDotColor = 0;
            dotPalIdx = prevDotPalIdx = prevPrevDotPalIdx = prevPrevPrevDotPalIdx = 0;
            skippedPreRenderDot341 = false;
            // P4-1: OAM corruption
            oamCorruptPending = false; oamCorruptSuppressed = false;
            oamCorruptDelay = 0; oamCorruptDisabledFlag = false;
            // P4-2: Palette corruption
            // TriCNES: counters start at 0, count UP, fire at 12/4.
            // AprNes: count DOWN, fire at 0. To match first-fire timing:
            // CPU first fire at tick 13 → init=12 (12→11→...→0=fire at tick 13)
            // PPU first fire at tick 5  → init=4  (4→3→2→1→0=fire at tick 5)
            mcCpuClock = masterPerCpu; mcPpuClock = masterPerPpu; mcApuPutCycle = true;
            spr_ram_add = 0;

            // PPU tile pipeline
            renderLow = 0; renderHigh = 0;
            pendingTileLow = 0; pendingTileHigh = 0;

            // PPU sprite state
prerender_sprite0_x = 0;
            prerender_sprite0_tile_low = 0; prerender_sprite0_tile_high = 0;
            prerender_sprite0_flip_x = false;
            spriteOverflowCycle = 0;

            // JoyPad (TriCNES shift register model)
            P1_ShiftRegister = 0; P2_ShiftRegister = 0;
            P1_ShiftCounter = 0; P2_ShiftCounter = 0;
            controllerStrobing = false; controllerStrobed = false;
            // DMA bus state
            dataPinsNotFloating = false;
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
                    *Vertical = 4; // four-screen: 4 unique nametables
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
                    AnalogScreenBuf     = (uint*)Marshal.AllocHGlobal(sizeof(uint) * AnalogBufSize);
                    AnalogScreenBufBack = (uint*)Marshal.AllocHGlobal(sizeof(uint) * AnalogBufSize);
                }
                Buffer_BG_array  = (int* )Marshal.AllocHGlobal(sizeof(int)  * 61440);
                NesColors        = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 64);
                sprLineBuf       = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 256);
                sprLinePri       = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 256);
                sprLineSet       = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 256);
                sprLinePalIdx    = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 256);
                spr_ram          = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 256);
                secondaryOAM     = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 32);
                corruptOamRow    = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 32);
                ppu_ram          = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 0x4000);
                palCache         = (uint*)Marshal.AllocHGlobal(sizeof(uint) * 32);
                InitFlipTable();
                sprShiftL        = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 8);
                sprShiftH        = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 8);
                sprXCounter      = (int* )Marshal.AllocHGlobal(sizeof(int)  * 8);
                sprFetchAttr     = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 8);
                sprXPos          = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 8);
                ntscScanBuf      = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 256);
                // Allocate expansionChannels early — Mapper024/019/069/085 etc.
                // touch NesCore.expansionChannels[0..7] in their Reset(), which
                // runs BEFORE initAPU() is called below.
                expansionChannels = (int*)Marshal.AllocHGlobal(sizeof(int) * 8);
                for (int i = 0; i < 8; i++) expansionChannels[i] = 0;
                // P1_joypad_status/P2_joypad_status removed — shift register model uses static bytes
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
                {
                    Console.WriteLine("ROM DB: " + dbEntry.Name);
                    if (dbEntry.MapperOverride >= 0)
                    {
                        Console.WriteLine("ROM DB: Mapper override " + mapper + " -> " + dbEntry.MapperOverride);
                        mapper = dbEntry.MapperOverride;
                    }
                }
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
                P1_Port = 0; P2_Port = 0;
                P1_ShiftRegister = 0; P2_ShiftRegister = 0;
                for (int i = 0; i < 65536; i++) NES_MEM[i] = 0;
                for (int i = 0; i < 0x4000; i++) ppu_ram[i] = 0;

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

                //init APU & audio output (must be before reset vector read)
                initAPU();

                // AudioPlus 管線初始化
                AudioPlus_Init();

                // Reset vector read by BRK/RESET handler through MasterClockTick (doReset=true)


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

        // Per-master-clock main loop (TriCNES _EmulatorCore model)
        // CPU/PPU/APU each gated by their own countdown timer.

        // Region/FDS-static dispatcher. Picks the optimal unrolled fast path
        // for the current ROM at thread start. PAL/Dendy/FDS still use the
        // legacy slow path until their own Run_*() are implemented.

        // Region-specific MasterClockTick dispatcher — set once when a Run_X()
        // method begins executing. Used by:
        //   - PPU register handlers (ppu_r_2002/2007, ppu_w_2000/2001/2005/2006)
        //     which do EmulateNMasterClockCycles(N) during register access
        //   - AlignPhaseForFastPath (cold-start phase alignment)
        // All callers see the correct region-specific tick logic (NMI/IRQ
        // offsets matched to the right masterPerCpu, no !isFDS branch). Lets
        // the legacy MasterClockTick be retired once all call sites are routed
        // through this pointer.
        static public unsafe delegate*<void> mcTickFn = null;

        static public unsafe void run()
        {
            // Safety net: FDS hardware is NTSC-only. The UI should have caught
            // this at load time (via warning dialog), but guard here too in
            // case of headless / CLI invocation with explicit --region flag.
            if (isFDS && Region != RegionType.NTSC)
            {
                Console.WriteLine("ERROR: FDS requires NTSC region. Got Region=" + Region + ". Aborting emulator thread.");
                return;
            }

            if (isFDS)
            {
                Run_FDS();
            }
            else if (Region == RegionType.NTSC)
            {
                Run_NTSC();
            }
            else if (Region == RegionType.Dendy)
            {
                Run_Dendy();
            }
            else if (Region == RegionType.PAL)
            {
                Run_PAL();
            }
            // All RegionType enum values (NTSC/PAL/Dendy) are covered above,
            // plus isFDS. No fallback path needed.
        }

        // NTSC fast path. MasterClockTickInlineNTSC is AggressiveInlined
        // directly into this method's tight loop (mirroring the Legacy
        // structure where MasterClockTick was inlined into Run_Legacy).
        // No NTSCFast12Clocks intermediate method — it was adding a function
        // call boundary per 12 MC that cost more than the branch savings.
        static unsafe void Run_NTSC()
        {
            mcTickFn = &MasterClockTickInlineNTSC;
            AlignPhaseForFastPath();

            const int ExitCheckInterval = 120000; // ~60ms @ 1.79MHz
            while (!exit)
            {
                for (int i = 0; i < ExitCheckInterval; i++)
                    MasterClockTickInlineNTSC();
            }
            Console.WriteLine("exit..");
        }

        // ARCHITECTURAL NOTE — why no "structural unroll" version of the 12-MC kernel:
        //
        // PPU register handlers (ppu_r_2002, ppu_r_2007, ppu_w_2000/2001/2005/2006)
        // internally call MasterClockTick() 2-7 times to model TriCNES's
        // EmulateUntilEndOfRead / EmulateNMasterClockCycles time advancement.
        //
        // A full unroll bundles 12 ticks into one function call, but nested
        // MasterClockTick invocations (from inside cpu_step_one_cycle) consume
        // unknown additional ticks. The outer unrolled code cannot detect or
        // compensate — events get fired at the wrong absolute master clock
        // position, breaking VBL/NMI/sprite timing tests (vbl_nmi_timing,
        // sprite_hit_tests).
        //
        // The gated form (MasterClockTickInlineNTSC × N) handles nesting
        // naturally: each call processes exactly 1 tick with self-aware gates;
        // when nested calls consume extra ticks, the outer for-loop simply
        // runs fewer of its own logical ticks — total master-clock progress
        // remains correct.
        //
        // The gate checks (if mcCpu==0/8/5/12, if mcPpu==0/2) are NOT redundant.
        // They are the mechanism that lets each tick know its position. Cannot
        // be eliminated without removing TriCNES-style intra-instruction time
        // advancement (a much larger architectural change with no clear win).

        // Run the legacy slow path until counters land at the start-of-window
        // state for the fast path (mcCpuClock==0 && mcPpuClock==0 — about to
        // fire CPU+APU+PPU coincidently). After this, fast path takes over.
        static unsafe void AlignPhaseForFastPath()
        {
            // Convergence guaranteed within masterPerCpu ticks (12 for NTSC).
            // Uses mcTickFn so PAL/Dendy/FDS align with their correct NMI offset.
            while (!exit && !(mcCpuClock == 0 && mcPpuClock == 0))
                mcTickFn();
        }

        // Unrolled 12-MC NTSC kernel. Layout (matches MasterClockTick semantics):
        //   MC 0:  CPU step (+ DMA gate) + Mapper.CpuCycle + apu_step + ppu_step_new
        //   MC 2:  ppu_half_step_new
        //   MC 4:  NMI check + ppu_step_new
        //   MC 6:  ppu_half_step_new
        //   MC 7:  IRQ check + Mapper.CpuClockRise
        //   MC 8:  ppu_step_new
        //   MC 10: ppu_half_step_new
        // Skips !isFDS branches — caller guarantees this is non-FDS NTSC.
        // Inline a single tick of MasterClockTick — used by NTSCFast12Clocks unroll
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void MasterClockTickInlineNTSC()
        {
            if (mcCpuClock == 0)
            {
                mcCpuClock = 12;
                bool isDmcActive = dmcDmaRunning & (dmcStatusEnabled | dmcImplicitAbortActive);
                if (cpuIsRead & (isDmcActive | spriteDmaTransfer)) DmaOneCycle();
                else cpu_step_one_cycle();
                if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive = false;
                MapperObj.CpuCycle();
            }
            else if (mcCpuClock == 8)
            {
                NMILine |= NMIable && isVblank;
                if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
            }

            if (mcCpuClock == 5)
            {
                IRQLine = irqLineCurrent;
                if (statusframeint && !apuintflag) irqLineCurrent = true;
                MapperObj.CpuClockRise();
            }
            else if (mcCpuClock == 12)
            {
                apu_step();
                mcApuPutCycle = !mcApuPutCycle;
            }

            if (mcPpuClock == 0)
            {
                mcPpuClock = 4;
                ppu_step_new();
            }
            else if (mcPpuClock == 2)
            {
                ppu_half_step_new();
            }

            mcCpuClock--;
            mcPpuClock--;
        }

        // FDS runs on NTSC master clock timing (12 MC CPU, 4 MC PPU). Only
        // difference: fds_CpuCycle() replaces MapperObj.CpuCycle() (FDS uses
        // FdsChrMapper whose CpuCycle is empty; the FDS-side disk/IRQ/audio
        // state machine lives in fds_CpuCycle). Mapper.CpuClockRise() is
        // skipped (FdsChrMapper's is empty).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void MasterClockTickInlineFDS()
        {
            if (mcCpuClock == 0)
            {
                mcCpuClock = 12;
                bool isDmcActive = dmcDmaRunning & (dmcStatusEnabled | dmcImplicitAbortActive);
                if (cpuIsRead & (isDmcActive | spriteDmaTransfer)) DmaOneCycle();
                else cpu_step_one_cycle();
                if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive = false;
                fds_CpuCycle();
            }
            else if (mcCpuClock == 8)
            {
                NMILine |= NMIable && isVblank;
                if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
            }

            if (mcCpuClock == 5)
            {
                IRQLine = irqLineCurrent;
                if (statusframeint && !apuintflag) irqLineCurrent = true;
                // FDS: no MapperObj.CpuClockRise() (FdsChrMapper is empty)
            }
            else if (mcCpuClock == 12)
            {
                apu_step();
                mcApuPutCycle = !mcApuPutCycle;
            }

            if (mcPpuClock == 0)
            {
                mcPpuClock = 4;
                ppu_step_new();
            }
            else if (mcPpuClock == 2)
            {
                ppu_half_step_new();
            }

            mcCpuClock--;
            mcPpuClock--;
        }

        // FDS fast path — same shape as Run_NTSC. FDS is always NTSC-timed.
        static unsafe void Run_FDS()
        {
            mcTickFn = &MasterClockTickInlineFDS;
            AlignPhaseForFastPath();

            const int ExitCheckInterval = 120000;
            while (!exit)
            {
                for (int i = 0; i < ExitCheckInterval; i++)
                    MasterClockTickInlineFDS();
            }
            Console.WriteLine("exit..");
        }

        // Dendy: CPU=15 MC, PPU=5 MC (vs NTSC 12/4). CPU:PPU ratio is still
        // 3:1 so structure mirrors NTSC but with different constants:
        //   - NMI at mcCpuClock == 11 (= 15 - 4; "CPU step + 4 MC" offset)
        //   - IRQ at mcCpuClock == 5  (= "next CPU minus 5 MC", TriCNES rule)
        //   - APU at mcCpuClock == 15 (= masterPerCpu, same tick as CPU step)
        //   - PPU full on mcPpuClock == 0 (reset to 5)
        //   - PPU half on mcPpuClock == 2 (asymmetric: half at 2/5 of cycle;
        //     matches current TriCNES masterPerPpuHalf = 2 for PAL/Dendy)
        //   - mcPpuClock counts 5→4→3→2(half)→1→0(full+reset)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void MasterClockTickInlineDendy()
        {
            if (mcCpuClock == 0)
            {
                mcCpuClock = 15;
                bool isDmcActive = dmcDmaRunning & (dmcStatusEnabled | dmcImplicitAbortActive);
                if (cpuIsRead & (isDmcActive | spriteDmaTransfer)) DmaOneCycle();
                else cpu_step_one_cycle();
                if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive = false;
                MapperObj.CpuCycle();
            }
            else if (mcCpuClock == 11)
            {
                NMILine |= NMIable && isVblank;
                if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
            }

            if (mcCpuClock == 5)
            {
                IRQLine = irqLineCurrent;
                if (statusframeint && !apuintflag) irqLineCurrent = true;
                MapperObj.CpuClockRise();
            }
            else if (mcCpuClock == 15)
            {
                apu_step();
                mcApuPutCycle = !mcApuPutCycle;
            }

            if (mcPpuClock == 0)
            {
                mcPpuClock = 5;
                ppu_step_new();
            }
            else if (mcPpuClock == 2)
            {
                ppu_half_step_new();
            }

            mcCpuClock--;
            mcPpuClock--;
        }

        static unsafe void Run_Dendy()
        {
            mcTickFn = &MasterClockTickInlineDendy;
            AlignPhaseForFastPath();

            const int ExitCheckInterval = 120000;
            while (!exit)
            {
                for (int i = 0; i < ExitCheckInterval; i++)
                    MasterClockTickInlineDendy();
            }
            Console.WriteLine("exit..");
        }

        // PAL: CPU=16 MC, PPU=5 MC. LCM(16,5) = 80.
        //   - NMI at mcCpuClock == 12 (= 16 - 4, "CPU step + 4 MC" rule)
        //   - IRQ at mcCpuClock == 5  (= "next CPU minus 5 MC", TriCNES rule)
        //   - APU at mcCpuClock == 16 (= masterPerCpu, same tick as CPU step)
        //   - PPU half at mcPpuClock == 2 (asymmetric; TriCNES masterPerPpuHalf)
        // Fixes the latent NTSC-hardcoded NMI literal 8 in MasterClockTick
        // that was wrong for PAL; PAL APU tests pass independent of this
        // because their timing is CPU-relative, not master-clock-relative.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void MasterClockTickInlinePAL()
        {
            if (mcCpuClock == 0)
            {
                mcCpuClock = 16;
                bool isDmcActive = dmcDmaRunning & (dmcStatusEnabled | dmcImplicitAbortActive);
                if (cpuIsRead & (isDmcActive | spriteDmaTransfer)) DmaOneCycle();
                else cpu_step_one_cycle();
                if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive = false;
                MapperObj.CpuCycle();
            }
            else if (mcCpuClock == 12)
            {
                NMILine |= NMIable && isVblank;
                if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
            }

            if (mcCpuClock == 5)
            {
                IRQLine = irqLineCurrent;
                if (statusframeint && !apuintflag) irqLineCurrent = true;
                MapperObj.CpuClockRise();
            }
            else if (mcCpuClock == 16)
            {
                apu_step();
                mcApuPutCycle = !mcApuPutCycle;
            }

            if (mcPpuClock == 0)
            {
                mcPpuClock = 5;
                ppu_step_new();
            }
            else if (mcPpuClock == 2)
            {
                ppu_half_step_new();
            }

            mcCpuClock--;
            mcPpuClock--;
        }

        static unsafe void Run_PAL()
        {
            mcTickFn = &MasterClockTickInlinePAL;
            AlignPhaseForFastPath();

            const int ExitCheckInterval = 120000;
            while (!exit)
            {
                for (int i = 0; i < ExitCheckInterval; i++)
                    MasterClockTickInlinePAL();
            }
            Console.WriteLine("exit..");
        }

        // Legacy MasterClockTick() removed 2026-04-14 — all call sites now
        // route through the region-specific inline variants via mcTickFn.
        // History: this generic form was the original single-implementation
        // tick used by Run_Legacy + PPU register handlers. Static dispatch
        // refactor (feature/static-dispatch-mainloop → master commit 2780287)
        // added MasterClockTickInline{NTSC,PAL,Dendy,FDS} for Run_X outer
        // loops. This follow-up routes nested callers (PPU handlers,
        // AlignPhaseForFastPath) through mcTickFn so the legacy NTSC-hardcoded
        // NMI literal (mcCpu == 8) stops leaking into PAL/Dendy sessions.
    }

}
