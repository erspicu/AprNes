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
        // ── Unmanaged memory helpers ───────────────────────────────────────
        // .NET 10: NativeMemory.AlignedAlloc/AlignedFree (64-byte aligned for SIMD/cache-line).
        // net48 fallback: NesCore.AllocUnmanaged/FreeHGlobal (no guaranteed alignment).
        // CRITICAL: alloc + free MUST be paired via these helpers (mixing AlignedAlloc with
        // FreeHGlobal or vice-versa crashes the allocator). All project code must go through.
#if NET10_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IntPtr AllocUnmanaged(int size) => (IntPtr)NativeMemory.AlignedAlloc((nuint)size, 64);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FreeUnmanaged(IntPtr ptr) { if (ptr != IntPtr.Zero) NativeMemory.AlignedFree((void*)ptr); }
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IntPtr AllocUnmanaged(int size) => Marshal.AllocHGlobal(size);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FreeUnmanaged(IntPtr ptr) { if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr); }
#endif

        public static event EventHandler VideoOutput;


        static VideoOut VideoOut_arg = new VideoOut();

        static int mapper;
        static byte PRG_ROM_count, CHR_ROM_count, ROM_Control_1, ROM_Control_2, RAM_banks_count;
        static byte* PRG_ROM, CHR_ROM;
        static bool NesHeaderV2 = false;
        static public bool HasBattery = false;
        static public string rom_file_name = "";

        static IMapper MapperObj;
        // 64-byte block (8 × byte* on x64) — enables single-struct copy instead of 8× element assign
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        unsafe struct PtrBlock8 { public byte* p0, p1, p2, p3, p4, p5, p6, p7; }
        // 32-byte block (4 × byte*) — for mappers that switch banks in 4KB halves
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        unsafe struct PtrBlock4 { public byte* p0, p1, p2, p3; }

        static public byte** chrBankPtrs = null; // P34: 8×1KB CHR bank pointers, updated by mapper (unmanaged)
        static public bool mapperNeedsA12  = false; // any A12 notification needed (MMC3 or MMC2/4)
        static public bool mapperA12IsMmc3 = false; // true=MMC3-style, false=MMC2/4-style (only when mapperNeedsA12)
        // Mapper 68 (Sunsoft #4): CHR ROM pages used as nametable tiles
        public static byte** ntBankPtrs;    // 4×1KB NT pointers (one per nametable slot, unmanaged)
        public static byte*  ntBankWritable; // 4 bytes per-slot write enable (0=CHR-ROM, 1=CIRAM)
        static public bool ntChrOverrideEnabled = false; // when true PPU reads NT from ntBankPtrs instead of ppu_ram
        // MMC5 CHR A/B auto-switch (for 8x16 sprites: A=sprites, B=background)
        static public bool chrABAutoSwitch = false;
        static public byte** chrBankPtrsA = null; // A set (sprites, $5120-$5127) (unmanaged)
        static public byte** chrBankPtrsB = null; // B set (background, $5128-$512B) (unmanaged)
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
        static public ManualResetEventSlim renderReady = new ManualResetEventSlim(false);
        static public ManualResetEventSlim renderDone  = new ManualResetEventSlim(true); // 初始已完成
        static public volatile bool renderThreadRunning = false;

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
            if (PRG_ROM      != null) { NesCore.FreeUnmanaged((IntPtr)PRG_ROM);      PRG_ROM      = null; }
            if (CHR_ROM      != null) { NesCore.FreeUnmanaged((IntPtr)CHR_ROM);      CHR_ROM      = null; }
            if (NesColors    != null) { NesCore.FreeUnmanaged((IntPtr)NesColors);    NesColors    = null; }
            if (spr_ram      != null) { NesCore.FreeUnmanaged((IntPtr)spr_ram);      spr_ram      = null; }
            if (secondaryOAM != null) { NesCore.FreeUnmanaged((IntPtr)secondaryOAM); secondaryOAM = null; }
            if (corruptOamRow!= null) { NesCore.FreeUnmanaged((IntPtr)corruptOamRow);corruptOamRow= null; }
            if (ppu_ram      != null) { NesCore.FreeUnmanaged((IntPtr)ppu_ram);      ppu_ram      = null; }
            if (ntsc_rowPalettes != null) { NesCore.FreeUnmanaged((IntPtr)ntsc_rowPalettes); ntsc_rowPalettes = null; }
            if (digitalFrameRgb != null) { NesCore.FreeUnmanaged((IntPtr)digitalFrameRgb); digitalFrameRgb = null; }
            if (FlipTable    != null) { NesCore.FreeUnmanaged((IntPtr)FlipTable);    FlipTable    = null; }
            if (sprShiftL    != null) { NesCore.FreeUnmanaged((IntPtr)sprShiftL);    sprShiftL    = null; }
            if (sprShiftH    != null) { NesCore.FreeUnmanaged((IntPtr)sprShiftH);    sprShiftH    = null; }
            if (sprXCounter  != null) { NesCore.FreeUnmanaged((IntPtr)sprXCounter);  sprXCounter  = null; }
            if (sprFetchAttr != null) { NesCore.FreeUnmanaged((IntPtr)sprFetchAttr); sprFetchAttr = null; }
            if (expansionChannels != null) { NesCore.FreeUnmanaged((IntPtr)expansionChannels); expansionChannels = null; }
            if (chrBankPtrs  != null) { NesCore.FreeUnmanaged((IntPtr)chrBankPtrs);  chrBankPtrs  = null; }
            if (chrBankPtrsA != null) { NesCore.FreeUnmanaged((IntPtr)chrBankPtrsA); chrBankPtrsA = null; }
            if (chrBankPtrsB != null) { NesCore.FreeUnmanaged((IntPtr)chrBankPtrsB); chrBankPtrsB = null; }
            if (NES_MEM      != null) { NesCore.FreeUnmanaged((IntPtr)NES_MEM);      NES_MEM      = null; }
            if (Vertical           != null) { NesCore.FreeUnmanaged((IntPtr)Vertical);           Vertical           = null; }
            if (AnalogScreenBuf     != null) { NesCore.FreeUnmanaged((IntPtr)AnalogScreenBuf);     AnalogScreenBuf     = null; AnalogBufSize = 0; }
            if (AnalogScreenBufBack != null) { NesCore.FreeUnmanaged((IntPtr)AnalogScreenBufBack); AnalogScreenBufBack = null; }
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
            cpubus = 0; internalBus = 0; cpuBusAddr = 0; addressBus = 0; dl = 0; ignoreH = false;
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
            VramaddrIncrement = 1;
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
            { sprShiftL[i] = 0; sprShiftH[i] = 0; sprXCounter[i] = 0; sprFetchAttr[i] = 0; }
            sprSlotCount = 0; sprZeroInSlots = false;
            // 3-dot pixel pipeline (Phase A5: palette indices only)
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


                Vertical = (int*)NesCore.AllocUnmanaged(sizeof(int));

                PRG_ROM_count = rom_bytes[4];
                Console.WriteLine("PRG-ROM count : " + PRG_ROM_count);

                int PRG_ROM_count_needs = PRG_ROM_count;
                if (PRG_ROM_count == 1) PRG_ROM_count_needs = 2;//min PRG ROM is 2
                PRG_ROM = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * PRG_ROM_count_needs * 16384);
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
                    CHR_ROM = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * CHR_ROM_count * 8192);
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
                if (AnalogEnabled)
                {
                    SyncAnalogConfig();  // 確保 Crt_DstW/DstH 使用正確的 AnalogSize
                    AnalogBufSize   = Crt_DstW * Crt_DstH;
                    AnalogScreenBuf     = (uint*)NesCore.AllocUnmanaged(sizeof(uint) * AnalogBufSize);
                    AnalogScreenBufBack = (uint*)NesCore.AllocUnmanaged(sizeof(uint) * AnalogBufSize);
                }
                NesColors        = (uint*)NesCore.AllocUnmanaged(sizeof(uint) * 64);
                spr_ram          = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * 256);
                secondaryOAM     = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * 32);
                corruptOamRow    = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * 32);
                ppu_ram          = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * 0x4000);
                palCache         = (uint*)NesCore.AllocUnmanaged(sizeof(uint) * 32);
                // Phase A2: per-frame palette index buffer, used by both analog and digital paths.
                // 240 scanlines × 256 pixels = 60 KB.
                ntsc_rowPalettes = (byte*)NesCore.AllocUnmanaged(240 * 256);
                // Phase C-3: pre-converted RGB buffer for digital path (256×240 uint = 256 KB).
                // emu populates at frame end via Convert_PalIdxFrameToRGB; render thread reads —
                // race-free vs next frame's PixelZone palette index writes.
                digitalFrameRgb = (uint*)NesCore.AllocUnmanaged(sizeof(uint) * 256 * 240);
                InitFlipTable();
                sprShiftL        = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * 8);
                sprShiftH        = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * 8);
                sprXCounter      = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * 8);
                sprFetchAttr     = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * 8);
                // Allocate expansionChannels early — Mapper024/019/069/085 etc.
                // touch NesCore.expansionChannels[0..7] in their Reset(), which
                // runs BEFORE initAPU() is called below.
                expansionChannels = (int*)NesCore.AllocUnmanaged(sizeof(int) * 8);
                for (int i = 0; i < 8; i++) expansionChannels[i] = 0;
                chrBankPtrs      = (byte**)NesCore.AllocUnmanaged(sizeof(byte*) * 8);
                chrBankPtrsA     = (byte**)NesCore.AllocUnmanaged(sizeof(byte*) * 8);
                chrBankPtrsB     = (byte**)NesCore.AllocUnmanaged(sizeof(byte*) * 8);
                for (int i = 0; i < 8; i++) { chrBankPtrs[i] = null; chrBankPtrsA[i] = null; chrBankPtrsB[i] = null; }
                // P1_joypad_status/P2_joypad_status removed — shift register model uses static bytes
                NES_MEM          = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * 65536);

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
                for (int i = 0; i < 4; i++) ntBankWritable[i] = 1;
                chrABAutoSwitch = false;
                chrBGUseASet = false;
                extAttrEnabled = false;
                mmc5Ref = null;
                MapperObj.MapperInit(PRG_ROM, CHR_ROM, ppu_ram, PRG_ROM_count, CHR_ROM_count, Vertical);
                MapperObj.Reset();
                if (!dbEntry.IsNone && dbEntry.MirrorOverride >= 0)
                    *Vertical = dbEntry.MirrorOverride;
                MapperObj.UpdateCHRBanks();

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
                InitPpuDispatchTable();

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

        // Specialized "nested tick" dispatch for PPU register handlers that do
        // TriCNES EmulateNMasterClockCycles(N). Set alongside mcTickFn at each
        // Run_X entry. NTSC uses fully-unrolled variants (no internal call to
        // mcTickFn → no recursion). Other regions fall back to a for-loop of
        // mcTickFn (same behavior as before this refactor).
        static public unsafe delegate*<void> nestedTick7Fn = null;  // $2002/$2007/$2004 read, $2007 write
        static public unsafe delegate*<void> nestedTick2Fn = null;  // $2000 write

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

            emuThreadAlive = true;
            try
            {
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
            finally
            {
                emuThreadAlive = false;
            }
        }

        // NTSC fast path. MasterClockTickInlineNTSC is AggressiveInlined
        // directly into this method's tight loop (mirroring the Legacy
        // structure where MasterClockTick was inlined into Run_Legacy).
        // No NTSCFast12Clocks intermediate method — it was adding a function
        // call boundary per 12 MC that cost more than the branch savings.
        static unsafe void Run_NTSC()
        {
            nestedTick7Fn = &NestedTick7_NTSC;
            nestedTick2Fn = &NestedTick2_NTSC;
            WarmUpNTSC();

            // One unrolled call = 12 MC. Divide gated batch count (120000) by 12.
            const int ExitCheckInterval = 10000; // ~60ms @ 1.79MHz
            while (!exit)
            {
                for (int i = 0; i < ExitCheckInterval; i++)
                    MasterClockTickUnrolledNTSC();
            }
            Console.WriteLine("exit..");
        }

        // 12-MC structural unroll for NTSC. Enabled by Phase 1's NestedTickN
        // de-recursion: since cpu_step_one_cycle no longer calls back into
        // mcTickFn (it calls self-contained NestedTickN which leaves a
        // deterministic counter state), the outer can detect which branch to
        // take by inspecting mcCpuClock after cpu_step returns:
        //   mcCpu==12 → no nesting, run full 12-MC sequence
        //   mcCpu==10 → NestedTick2 ran, skip MC 0 events, run MC 2-10 tail
        //   mcCpu==5  → NestedTick7 ran, skip MC 0-6 events, run MC 7-10 tail
        //
        // Each CPU cycle has at most one bus access, so at most one nested
        // variant fires per outer call. The 3 cases cover all possibilities.
        // Counter writes between events preserve the slow-path values that
        // PPU register handlers would observe via mcCpu&3 / mcPpu&3 (but
        // those handlers are only invoked during cpu_step, where counters
        // are already correct by the time they're consulted).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void MasterClockTickUnrolledNTSC()
        {
            // ── MC 0: CPU gate (may trigger NestedTickN via cpu_step) ──
            mcCpuClock = 12;
            bool isDmcActive = dmcDmaRunning & (dmcStatusEnabled | dmcImplicitAbortActive);
            if (cpuIsRead & (isDmcActive | spriteDmaTransfer)) DmaOneCycle();
            else cpu_step_one_cycle();
            if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive = false;
            MapperObj.CpuCycle();

            int state = mcCpuClock; // 12 / 10 / 5

            if (state == 12)
            {
                // No nesting — fire full 12-MC event sequence.
                apu_step();
                mcApuPutCycle = !mcApuPutCycle;
                mcPpuClock = 4;
                ppu_step_new();                            // MC 0 PPU full

                mcCpuClock = 10; mcPpuClock = 2;
                ppu_half_step_new();                       // MC 2

                mcCpuClock = 8; mcPpuClock = 0;
                NMILine |= NMIable && isVblank;
                if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
                mcPpuClock = 4;
                ppu_step_new();                            // MC 4 PPU full

                mcCpuClock = 6; mcPpuClock = 2;
                ppu_half_step_new();                       // MC 6

                mcCpuClock = 5;
                IRQLine = irqLineCurrent;
                if (statusframeint && !apuintflag) irqLineCurrent = true;
                MapperObj.CpuClockRise();                  // MC 7 IRQ

                mcCpuClock = 4; mcPpuClock = 4;
                ppu_step_new();                            // MC 8 PPU full

                mcCpuClock = 2; mcPpuClock = 2;
                ppu_half_step_new();                       // MC 10
            }
            else if (state == 10)
            {
                // NestedTick2 fired MC 0 APU + PPU full. State now (10, 2).
                // Tail: MC 2 half, MC 4 NMI + PPU full, MC 6 half, MC 7 IRQ,
                //       MC 8 PPU full, MC 10 half.
                ppu_half_step_new();                       // MC 2

                mcCpuClock = 8; mcPpuClock = 0;
                NMILine |= NMIable && isVblank;
                if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
                mcPpuClock = 4;
                ppu_step_new();                            // MC 4

                mcCpuClock = 6; mcPpuClock = 2;
                ppu_half_step_new();                       // MC 6

                mcCpuClock = 5;
                IRQLine = irqLineCurrent;
                if (statusframeint && !apuintflag) irqLineCurrent = true;
                MapperObj.CpuClockRise();                  // MC 7

                mcCpuClock = 4; mcPpuClock = 4;
                ppu_step_new();                            // MC 8

                mcCpuClock = 2; mcPpuClock = 2;
                ppu_half_step_new();                       // MC 10
            }
            else // state == 5: NestedTick7 fired MC 0-6 events. State (5, 1).
            {
                // Tail: MC 7 IRQ, MC 8 PPU full, MC 10 half.
                IRQLine = irqLineCurrent;
                if (statusframeint && !apuintflag) irqLineCurrent = true;
                MapperObj.CpuClockRise();                  // MC 7

                mcCpuClock = 4; mcPpuClock = 4;
                ppu_step_new();                            // MC 8

                mcCpuClock = 2; mcPpuClock = 2;
                ppu_half_step_new();                       // MC 10
            }

            // End state (0, 0) — next iteration's MC 0 CPU gate starts fresh.
            mcCpuClock = 0;
            mcPpuClock = 0;
        }

        // ARCHITECTURAL NOTE — structural unroll (applies to all 4 regions):
        //
        // Phase 1 (NestedTickN variants) replaced PPU register handlers'
        // recursive mcTickFn calls with self-contained functions producing a
        // deterministic end counter state:
        //   NTSC/FDS : NestedTick2 → mcCpu=10,  NestedTick7 → mcCpu=5
        //   PAL      : NestedTick2 → mcCpu=14,  NestedTick7 → mcCpu=9
        //              (with 5-way switch on starting mcPpu, since PAL has
        //               5 CPU gates per 80-MC window at varying phases)
        //   Dendy    : NestedTick2 → mcCpu=13,  NestedTick7 → mcCpu=8
        // De-recursion made the outer's post-cpu_step state a reliable
        // dispatch signal.
        //
        // Phase 2 leverages that: MasterClockTickUnrolled<Region> runs one
        // full window per call, choosing a tail based on mcCpu after
        // cpu_step. Each case covers remaining events of the window.
        //
        // MasterClockTickInline<Region> (the gated single-tick form) is
        // retained as the backend used by AlignPhaseForFastPath for cold-
        // start phase alignment — invoked via mcTickFn pointer (PAL only now).

        // ════════════════════════════════════════════════════════════════════
        // Cold-start warm-up functions — directly unrolled event sequences
        // that run at thread start to bring counters from init state
        // (masterPerCpu, masterPerPpu) to (0, 0) before the main loop's
        // Unrolled<Region> takes over.
        //
        // These replace the AlignPhaseForFastPath + MasterClockTickInline<R>
        // mechanism for regions whose warm-up is simple (no CPU gate fires
        // during warm-up). PAL keeps AlignPhase because LCM(16,5)=80 forces
        // warm-up past 5 CPU gates, making it as complex as the full outer
        // unroll — no simplification possible there.
        // ════════════════════════════════════════════════════════════════════

        // NTSC warm-up: from (12, 4) to (0, 0) in 12 ticks. No CPU gate fires.
        // Events in slow-path order: APU, PPU-half, NMI, PPU-full, PPU-half,
        //   IRQ+CpuClockRise, PPU-full, PPU-half.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void WarmUpNTSC()
        {
            // t1 APU (mcCpu=12, mcPpu=4)
            apu_step();
            mcApuPutCycle = !mcApuPutCycle;

            // t3 PPU half (mcCpu=10, mcPpu=2)
            mcCpuClock = 10; mcPpuClock = 2;
            ppu_half_step_new();

            // t5 NMI (mcCpu=8) + PPU full (mcPpu=0→4)
            mcCpuClock = 8; mcPpuClock = 0;
            NMILine |= NMIable && isVblank;
            if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
            mcPpuClock = 4;
            ppu_step_new();

            // t7 PPU half (mcCpu=6, mcPpu=2)
            mcCpuClock = 6; mcPpuClock = 2;
            ppu_half_step_new();

            // t8 IRQ (mcCpu=5) + Mapper.CpuClockRise
            mcCpuClock = 5; mcPpuClock = 1;
            IRQLine = irqLineCurrent;
            if (statusframeint && !apuintflag) irqLineCurrent = true;
            MapperObj.CpuClockRise();

            // t9 PPU full (mcCpu=4, mcPpu=0→4)
            mcCpuClock = 4; mcPpuClock = 4;
            ppu_step_new();

            // t11 PPU half (mcCpu=2, mcPpu=2)
            mcCpuClock = 2; mcPpuClock = 2;
            ppu_half_step_new();

            // End state
            mcCpuClock = 0; mcPpuClock = 0;
        }

        // FDS warm-up: identical to NTSC except Mapper.CpuClockRise is skipped
        // (FdsChrMapper.CpuClockRise is empty).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void WarmUpFDS()
        {
            apu_step();
            mcApuPutCycle = !mcApuPutCycle;

            mcCpuClock = 10; mcPpuClock = 2;
            ppu_half_step_new();

            mcCpuClock = 8; mcPpuClock = 0;
            NMILine |= NMIable && isVblank;
            if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
            mcPpuClock = 4;
            ppu_step_new();

            mcCpuClock = 6; mcPpuClock = 2;
            ppu_half_step_new();

            mcCpuClock = 5; mcPpuClock = 1;
            IRQLine = irqLineCurrent;
            if (statusframeint && !apuintflag) irqLineCurrent = true;
            // FDS: no MapperObj.CpuClockRise()

            mcCpuClock = 4; mcPpuClock = 4;
            ppu_step_new();

            mcCpuClock = 2; mcPpuClock = 2;
            ppu_half_step_new();

            mcCpuClock = 0; mcPpuClock = 0;
        }

        // Dendy warm-up: from (15, 5) to (0, 0) in 15 ticks. No CPU gate.
        // NMI gate at mcCpu==11 (vs NTSC's 8); t11 IRQ+PPU-full coincident.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void WarmUpDendy()
        {
            // t1 APU (mcCpu=15, mcPpu=5)
            apu_step();
            mcApuPutCycle = !mcApuPutCycle;

            // t4 PPU half (mcCpu=12, mcPpu=2)
            mcCpuClock = 12; mcPpuClock = 2;
            ppu_half_step_new();

            // t5 NMI (mcCpu=11, mcPpu=1)
            mcCpuClock = 11; mcPpuClock = 1;
            NMILine |= NMIable && isVblank;
            if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;

            // t6 PPU full (mcCpu=10, mcPpu=0→5)
            mcCpuClock = 10; mcPpuClock = 5;
            ppu_step_new();

            // t9 PPU half (mcCpu=7, mcPpu=2)
            mcCpuClock = 7; mcPpuClock = 2;
            ppu_half_step_new();

            // t11 IRQ (mcCpu=5) — fires before PPU full per slow-path order
            mcCpuClock = 5; mcPpuClock = 0;
            IRQLine = irqLineCurrent;
            if (statusframeint && !apuintflag) irqLineCurrent = true;
            MapperObj.CpuClockRise();
            // t11 PPU full (mcCpu=5, mcPpu=5 after reset)
            mcPpuClock = 5;
            ppu_step_new();

            // t14 PPU half (mcCpu=2, mcPpu=2)
            mcCpuClock = 2; mcPpuClock = 2;
            ppu_half_step_new();

            // End state
            mcCpuClock = 0; mcPpuClock = 0;
        }

        // PAL-only: run the legacy slow path until counters land at
        // (0, 0). PAL LCM=80 forces warm-up past 5 CPU gates (executing
        // real CPU cycles including BRK reset vector read), so a fixed
        // unrolled warm-up is not simpler than just running gated ticks.
        static unsafe void AlignPhaseForFastPath()
        {
            while (!exit && !(mcCpuClock == 0 && mcPpuClock == 0))
                mcTickFn();
        }

        // MasterClockTickInlineNTSC removed 2026-04-14: NTSC's cold-start
        // warm-up is now handled by the unrolled WarmUpNTSC() above (since
        // NTSC's LCM=12 warm-up window contains no CPU gate fires, a fixed
        // event sequence suffices — no need for the gated single-tick form).

        // ════════════════════════════════════════════════════════════════════
        // NTSC NestedTick variants — for PPU register handlers (ppu_r_2002,
        // ppu_r_2007, ppu_r_2004, ppu_w_2000, ppu_w_2007) that perform
        // TriCNES EmulateNMasterClockCycles(N) intra-instruction time
        // advancement.
        //
        // Called only from inside cpu_step_one_cycle (which itself is only
        // invoked from MasterClockTickInlineNTSC's CPU gate at mcCpu==0 →
        // immediately reset to 12). Therefore the entry state is ALWAYS
        // (mcCpu=12, mcPpu=0). The N-step event sequence from this fixed
        // starting point is fully deterministic — no need for per-tick gates,
        // no recursion back through mcTickFn.
        //
        // Counter values are pinned to the "during-event" slow-path values
        // before each event call (so PPU register handler &3 reads see the
        // correct alignment). End state matches what 7 (or 2) gated ticks
        // would leave behind, so the outer MasterClockTickInlineNTSC's
        // remaining gates (IRQ at mcCpu==5 etc.) fire correctly after return.
        // ════════════════════════════════════════════════════════════════════

        // 7-tick nested. Used by $2002 read, $2007 read, $2004 read, $2007 write.
        // Trace (start state (12, 0), each call decrements 1 of mcCpu/mcPpu):
        //   call 1 (12, 0): APU + toggle, mcPpu→4, PPU full       → (11, 3)
        //   call 2 (11, 3): no events                              → (10, 2)
        //   call 3 (10, 2): PPU half                               → (9, 1)
        //   call 4 (9, 1):  no events                              → (8, 0)
        //   call 5 (8, 0):  NMI check, mcPpu→4, PPU full          → (7, 3)
        //   call 6 (7, 3):  no events                              → (6, 2)
        //   call 7 (6, 2):  PPU half                               → (5, 1)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void NestedTick7_NTSC()
        {
            // ── Call 1: APU + PPU full ──
            // mcCpu=12 (set by outer's CPU gate), mcPpu=0 (carry-over)
            apu_step();
            mcApuPutCycle = !mcApuPutCycle;
            mcPpuClock = 4;
            ppu_step_new();

            // ── Call 3: PPU half (mcCpu=10, mcPpu=2) ──
            mcCpuClock = 10;
            mcPpuClock = 2;
            ppu_half_step_new();

            // ── Call 5: NMI check + PPU full (mcCpu=8) ──
            mcCpuClock = 8;
            mcPpuClock = 0;
            NMILine |= NMIable && isVblank;
            if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
            mcPpuClock = 4;
            ppu_step_new();

            // ── Call 7: PPU half (mcCpu=6, mcPpu=2) ──
            mcCpuClock = 6;
            mcPpuClock = 2;
            ppu_half_step_new();

            // End state matches gated equivalent of 7 ticks from (12, 0).
            mcCpuClock = 5;
            mcPpuClock = 1;
        }

        // 2-tick nested. Used by $2000 write.
        // Trace:
        //   call 1 (12, 0): APU + toggle, mcPpu→4, PPU full       → (11, 3)
        //   call 2 (11, 3): no events                              → (10, 2)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void NestedTick2_NTSC()
        {
            // ── Call 1: APU + PPU full ──
            apu_step();
            mcApuPutCycle = !mcApuPutCycle;
            mcPpuClock = 4;
            ppu_step_new();

            // End state.
            mcCpuClock = 10;
            mcPpuClock = 2;
        }

        // ════════════════════════════════════════════════════════════════════
        // PAL NestedTick variants (Phase 1B)
        //
        // PAL has 5 CPU gates per 80-MC window (LCM(16,5)=80). Each gate
        // fires cpu_step at a different mcPpu starting value — namely
        // 0, 4, 3, 2, 1 at the 5 gates respectively (derived from
        // (k-1 ticks from window start) mod 5 mapped to the mcPpu cycle).
        //
        // Therefore NestedTick variants need a 5-way switch on mcPpuClock
        // at entry to pick the correct event sequence. Each case is a
        // careful trace of 7 (or 2) gated ticks from (16, startPpu).
        //
        // PAL gate constants:
        //   mcCpu==0  → CPU+reset to 16    (not reached during nested)
        //   mcCpu==12 → NMI
        //   mcCpu==5  → IRQ                 (not reached during 7 nested ticks)
        //   mcCpu==16 → APU
        //   mcPpu==0  → PPU full+reset to 5
        //   mcPpu==2  → PPU half
        //
        // End state invariants:
        //   NestedTick7_PAL: mcCpu always = 9 (different mcPpu per case)
        //   NestedTick2_PAL: mcCpu always = 14
        // These invariants give a clean post-cpu_step dispatch signal for
        // a future Phase 2B PAL outer unroll.
        // ════════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void NestedTick7_PAL()
        {
            switch (mcPpuClock)
            {
                case 0:
                    // Gate 1: start (16, 0)
                    // t1: APU + PPU full   → (15, 4)
                    // t4: PPU half @(13,2) → (12, 1)
                    // t5: NMI @(12,1)      → (11, 0)
                    // t6: PPU full @(11,0) → (10, 4)
                    // end: (9, 3)
                    apu_step();
                    mcApuPutCycle = !mcApuPutCycle;
                    mcPpuClock = 5;
                    ppu_step_new();
                    mcCpuClock = 13; mcPpuClock = 2;
                    ppu_half_step_new();
                    mcCpuClock = 12; mcPpuClock = 1;
                    NMILine |= NMIable && isVblank;
                    if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
                    mcCpuClock = 11; mcPpuClock = 5;
                    ppu_step_new();
                    mcCpuClock = 9; mcPpuClock = 3;
                    break;

                case 4:
                    // Gate 2: start (16, 4)
                    // t1: APU                → (15, 3)
                    // t3: PPU half @(14,2)   → (13, 1)
                    // t5: NMI + PPU full @(12,0) → (11, 4)
                    // end: (9, 2)
                    apu_step();
                    mcApuPutCycle = !mcApuPutCycle;
                    mcCpuClock = 14; mcPpuClock = 2;
                    ppu_half_step_new();
                    mcCpuClock = 12; mcPpuClock = 0;
                    NMILine |= NMIable && isVblank;
                    if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
                    mcPpuClock = 5;
                    ppu_step_new();
                    mcCpuClock = 9; mcPpuClock = 2;
                    break;

                case 3:
                    // Gate 3: start (16, 3)
                    // t1: APU                → (15, 2)
                    // t2: PPU half @(15,2)   → (14, 1)
                    // t4: PPU full @(13,0)   → (12, 4)
                    // t5: NMI @(12,4)        → (11, 3)
                    // t7: PPU half @(10,2)   → (9, 1)
                    apu_step();
                    mcApuPutCycle = !mcApuPutCycle;
                    mcCpuClock = 15; mcPpuClock = 2;
                    ppu_half_step_new();
                    mcCpuClock = 13; mcPpuClock = 5;
                    ppu_step_new();
                    mcCpuClock = 12; mcPpuClock = 4;
                    NMILine |= NMIable && isVblank;
                    if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
                    mcCpuClock = 10; mcPpuClock = 2;
                    ppu_half_step_new();
                    mcCpuClock = 9; mcPpuClock = 1;
                    break;

                case 2:
                    // Gate 4: start (16, 2)
                    // t1: APU + PPU half     → (15, 1)
                    // t3: PPU full @(14,0)   → (13, 4)
                    // t5: NMI @(12,3)        → (11, 2)
                    // t6: PPU half @(11,2)   → (10, 1)
                    // end: (9, 0)
                    apu_step();
                    mcApuPutCycle = !mcApuPutCycle;
                    ppu_half_step_new();
                    mcCpuClock = 14; mcPpuClock = 5;
                    ppu_step_new();
                    mcCpuClock = 12; mcPpuClock = 3;
                    NMILine |= NMIable && isVblank;
                    if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
                    mcCpuClock = 11; mcPpuClock = 2;
                    ppu_half_step_new();
                    mcCpuClock = 9; mcPpuClock = 0;
                    break;

                case 1:
                    // Gate 5: start (16, 1)
                    // t1: APU                → (15, 0)
                    // t2: PPU full @(15,0)   → (14, 4)
                    // t5: NMI + PPU half @(12,2) → (11, 1)
                    // t7: PPU full @(10,0)   → (9, 4)
                    apu_step();
                    mcApuPutCycle = !mcApuPutCycle;
                    mcCpuClock = 15; mcPpuClock = 5;
                    ppu_step_new();
                    mcCpuClock = 12; mcPpuClock = 2;
                    NMILine |= NMIable && isVblank;
                    if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
                    ppu_half_step_new();
                    mcCpuClock = 10; mcPpuClock = 5;
                    ppu_step_new();
                    mcCpuClock = 9; mcPpuClock = 4;
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void NestedTick2_PAL()
        {
            switch (mcPpuClock)
            {
                case 0:
                    // (16, 0): t1 APU + PPU full → (15, 4). t2 → (14, 3).
                    apu_step();
                    mcApuPutCycle = !mcApuPutCycle;
                    mcPpuClock = 5;
                    ppu_step_new();
                    mcCpuClock = 14; mcPpuClock = 3;
                    break;

                case 4:
                    // (16, 4): t1 APU → (15, 3). t2 → (14, 2).
                    apu_step();
                    mcApuPutCycle = !mcApuPutCycle;
                    mcCpuClock = 14; mcPpuClock = 2;
                    break;

                case 3:
                    // (16, 3): t1 APU → (15, 2). t2 PPU half → (14, 1).
                    apu_step();
                    mcApuPutCycle = !mcApuPutCycle;
                    mcCpuClock = 15; mcPpuClock = 2;
                    ppu_half_step_new();
                    mcCpuClock = 14; mcPpuClock = 1;
                    break;

                case 2:
                    // (16, 2): t1 APU + PPU half → (15, 1). t2 → (14, 0).
                    apu_step();
                    mcApuPutCycle = !mcApuPutCycle;
                    ppu_half_step_new();
                    mcCpuClock = 14; mcPpuClock = 0;
                    break;

                case 1:
                    // (16, 1): t1 APU → (15, 0). t2 PPU full → (14, 4).
                    apu_step();
                    mcApuPutCycle = !mcApuPutCycle;
                    mcCpuClock = 15; mcPpuClock = 5;
                    ppu_step_new();
                    mcCpuClock = 14; mcPpuClock = 4;
                    break;
            }
        }

        // NestedTick7/2_Fallback removed 2026-04-14 — all 4 regions (NTSC/PAL/
        // Dendy/FDS) now bind to specialized NestedTickN variants. Fallback
        // wrappers were dead code (no caller) after Phase 1D+2D completion.

        // ════════════════════════════════════════════════════════════════════
        // PAL structural unroll (Phase 2B)
        //
        // PAL's 80-MC window = 5 CPU gates × 16 MC each. Each gate's cpu_step
        // may trigger NestedTick2_PAL or NestedTick7_PAL (or nothing), and the
        // nested variants leave deterministic end states:
        //   no nest  → mcCpu = 16
        //   N=2 nest → mcCpu = 14
        //   N=7 nest → mcCpu =  9
        // These three values are the dispatch signal for each gate's tail.
        //
        // Each of the 5 gates has its own event pattern (depending on starting
        // mcPpu which varies 0→4→3→2→1 across the window). End-of-gate state
        // feeds into the next gate's start.
        //
        // PAL gate constants (recap):
        //   mcCpu==12 → NMI
        //   mcCpu==5  → IRQ
        //   mcCpu==16 → APU
        //   mcPpu==0  → PPU full (reset to 5)
        //   mcPpu==2  → PPU half
        // ════════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PalNMI()
        {
            NMILine |= NMIable && isVblank;
            if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void PalIRQ()
        {
            IRQLine = irqLineCurrent;
            if (statusframeint && !apuintflag) irqLineCurrent = true;
            MapperObj.CpuClockRise();
        }

        // ── Gate 1 — enters (0, 0), exits (0, 4) ──
        // Full event trace (no nest): APU, PPU-full@(16,5), PPU-half@(13,2),
        //   NMI@(12,1), PPU-full@(11,5), PPU-half@(8,2), PPU-full@(6,5),
        //   IRQ@(5,4), PPU-half@(3,2), PPU-full@(1,5). End (0, 4).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void PalGate1()
        {
            mcCpuClock = 16;
            bool isDmcActive = dmcDmaRunning & (dmcStatusEnabled | dmcImplicitAbortActive);
            if (cpuIsRead & (isDmcActive | spriteDmaTransfer)) DmaOneCycle();
            else cpu_step_one_cycle();
            if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive = false;
            MapperObj.CpuCycle();

            int state = mcCpuClock;

            if (state == 16)
            {
                apu_step(); mcApuPutCycle = !mcApuPutCycle;
                mcPpuClock = 5; ppu_step_new();                        // t1 full
                mcCpuClock = 13; mcPpuClock = 2; ppu_half_step_new();  // t4
                mcCpuClock = 12; mcPpuClock = 1; PalNMI();             // t5
                mcCpuClock = 11; mcPpuClock = 5; ppu_step_new();       // t6 full
                mcCpuClock = 8;  mcPpuClock = 2; ppu_half_step_new();  // t9
                mcCpuClock = 6;  mcPpuClock = 5; ppu_step_new();       // t11 full
                mcCpuClock = 5;                  PalIRQ();             // t12
                mcCpuClock = 3;  mcPpuClock = 2; ppu_half_step_new();  // t14
                mcCpuClock = 1;  mcPpuClock = 5; ppu_step_new();       // t16 full
            }
            else if (state == 14)
            {
                // NestedTick2_PAL case 0 did APU + PPU-full@(16,5). Tail from t4.
                mcCpuClock = 13; mcPpuClock = 2; ppu_half_step_new();
                mcCpuClock = 12; mcPpuClock = 1; PalNMI();
                mcCpuClock = 11; mcPpuClock = 5; ppu_step_new();
                mcCpuClock = 8;  mcPpuClock = 2; ppu_half_step_new();
                mcCpuClock = 6;  mcPpuClock = 5; ppu_step_new();
                mcCpuClock = 5;                  PalIRQ();
                mcCpuClock = 3;  mcPpuClock = 2; ppu_half_step_new();
                mcCpuClock = 1;  mcPpuClock = 5; ppu_step_new();
            }
            else // state == 9: NestedTick7_PAL case 0 fired t1 APU/full, t4 half, t5 NMI, t6 full. Tail from t9.
            {
                mcCpuClock = 8;  mcPpuClock = 2; ppu_half_step_new();
                mcCpuClock = 6;  mcPpuClock = 5; ppu_step_new();
                mcCpuClock = 5;                  PalIRQ();
                mcCpuClock = 3;  mcPpuClock = 2; ppu_half_step_new();
                mcCpuClock = 1;  mcPpuClock = 5; ppu_step_new();
            }

            mcCpuClock = 0; mcPpuClock = 4;
        }

        // ── Gate 2 — enters (0, 4), exits (0, 3) ──
        // Full: APU, PPU-half@(14,2), NMI@(12,0), PPU-full@(12,5),
        //   PPU-half@(9,2), PPU-full@(7,5), IRQ@(5,3), PPU-half@(4,2),
        //   PPU-full@(2,5). End (0, 3).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void PalGate2()
        {
            mcCpuClock = 16; mcPpuClock = 4;
            bool isDmcActive = dmcDmaRunning & (dmcStatusEnabled | dmcImplicitAbortActive);
            if (cpuIsRead & (isDmcActive | spriteDmaTransfer)) DmaOneCycle();
            else cpu_step_one_cycle();
            if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive = false;
            MapperObj.CpuCycle();

            int state = mcCpuClock;

            if (state == 16)
            {
                apu_step(); mcApuPutCycle = !mcApuPutCycle;
                mcCpuClock = 14; mcPpuClock = 2; ppu_half_step_new();   // t3
                mcCpuClock = 12; mcPpuClock = 0; PalNMI();              // t5 NMI
                                 mcPpuClock = 5; ppu_step_new();         // t5 full
                mcCpuClock = 9;  mcPpuClock = 2; ppu_half_step_new();   // t8
                mcCpuClock = 7;  mcPpuClock = 5; ppu_step_new();        // t10 full
                mcCpuClock = 5;  mcPpuClock = 3; PalIRQ();              // t12
                mcCpuClock = 4;  mcPpuClock = 2; ppu_half_step_new();   // t13
                mcCpuClock = 2;  mcPpuClock = 5; ppu_step_new();        // t15 full
            }
            else if (state == 14)
            {
                // NestedTick2_PAL case 4 did APU only. Tail from t3.
                mcCpuClock = 14; mcPpuClock = 2; ppu_half_step_new();
                mcCpuClock = 12; mcPpuClock = 0; PalNMI();
                                 mcPpuClock = 5; ppu_step_new();
                mcCpuClock = 9;  mcPpuClock = 2; ppu_half_step_new();
                mcCpuClock = 7;  mcPpuClock = 5; ppu_step_new();
                mcCpuClock = 5;  mcPpuClock = 3; PalIRQ();
                mcCpuClock = 4;  mcPpuClock = 2; ppu_half_step_new();
                mcCpuClock = 2;  mcPpuClock = 5; ppu_step_new();
            }
            else // state == 9: NestedTick7_PAL case 4 fired APU, PPU-half@t3, NMI@t5, PPU-full@t5. Tail from t8.
            {
                mcCpuClock = 9;  mcPpuClock = 2; ppu_half_step_new();
                mcCpuClock = 7;  mcPpuClock = 5; ppu_step_new();
                mcCpuClock = 5;  mcPpuClock = 3; PalIRQ();
                mcCpuClock = 4;  mcPpuClock = 2; ppu_half_step_new();
                mcCpuClock = 2;  mcPpuClock = 5; ppu_step_new();
            }

            mcCpuClock = 0; mcPpuClock = 3;
        }

        // ── Gate 3 — enters (0, 3), exits (0, 2) ──
        // Full: APU, PPU-half@(15,2), PPU-full@(13,5), NMI@(12,4),
        //   PPU-half@(10,2), PPU-full@(8,5), IRQ@(5,2)+PPU-half@(5,2),
        //   PPU-full@(3,5). End (0, 2).
        // NOTE: at t12 state is (5, 2), slow path fires IRQ then PPU half.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void PalGate3()
        {
            mcCpuClock = 16; mcPpuClock = 3;
            bool isDmcActive = dmcDmaRunning & (dmcStatusEnabled | dmcImplicitAbortActive);
            if (cpuIsRead & (isDmcActive | spriteDmaTransfer)) DmaOneCycle();
            else cpu_step_one_cycle();
            if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive = false;
            MapperObj.CpuCycle();

            int state = mcCpuClock;

            if (state == 16)
            {
                apu_step(); mcApuPutCycle = !mcApuPutCycle;
                mcCpuClock = 15; mcPpuClock = 2; ppu_half_step_new();  // t2
                mcCpuClock = 13; mcPpuClock = 5; ppu_step_new();       // t4 full
                mcCpuClock = 12; mcPpuClock = 4; PalNMI();             // t5
                mcCpuClock = 10; mcPpuClock = 2; ppu_half_step_new();  // t7
                mcCpuClock = 8;  mcPpuClock = 5; ppu_step_new();       // t9 full
                mcCpuClock = 5;  mcPpuClock = 2; PalIRQ();             // t12 IRQ
                                                  ppu_half_step_new(); // t12 PPU half (same state)
                mcCpuClock = 3;  mcPpuClock = 5; ppu_step_new();       // t14 full
            }
            else if (state == 14)
            {
                // NestedTick2_PAL case 3 did APU + PPU-half@(15,2). Tail from t4.
                mcCpuClock = 13; mcPpuClock = 5; ppu_step_new();
                mcCpuClock = 12; mcPpuClock = 4; PalNMI();
                mcCpuClock = 10; mcPpuClock = 2; ppu_half_step_new();
                mcCpuClock = 8;  mcPpuClock = 5; ppu_step_new();
                mcCpuClock = 5;  mcPpuClock = 2; PalIRQ();
                                                  ppu_half_step_new();
                mcCpuClock = 3;  mcPpuClock = 5; ppu_step_new();
            }
            else // state == 9: NestedTick7 case 3 fired APU, PPU-half@t2, PPU-full@t4, NMI@t5, PPU-half@t7. Tail from t9.
            {
                mcCpuClock = 8;  mcPpuClock = 5; ppu_step_new();
                mcCpuClock = 5;  mcPpuClock = 2; PalIRQ();
                                                  ppu_half_step_new();
                mcCpuClock = 3;  mcPpuClock = 5; ppu_step_new();
            }

            mcCpuClock = 0; mcPpuClock = 2;
        }

        // ── Gate 4 — enters (0, 2), exits (0, 1) ──
        // Full: APU, PPU-half@(16,2), PPU-full@(14,5), NMI@(12,3),
        //   PPU-half@(11,2), PPU-full@(9,5), PPU-half@(6,2), IRQ@(5,1),
        //   PPU-full@(4,5), PPU-half@(1,2). End (0, 1).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void PalGate4()
        {
            mcCpuClock = 16; mcPpuClock = 2;
            bool isDmcActive = dmcDmaRunning & (dmcStatusEnabled | dmcImplicitAbortActive);
            if (cpuIsRead & (isDmcActive | spriteDmaTransfer)) DmaOneCycle();
            else cpu_step_one_cycle();
            if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive = false;
            MapperObj.CpuCycle();

            int state = mcCpuClock;

            if (state == 16)
            {
                apu_step(); mcApuPutCycle = !mcApuPutCycle;
                /* mcCpu=16 already */          ppu_half_step_new();  // t1 half (16, 2)
                mcCpuClock = 14; mcPpuClock = 5; ppu_step_new();      // t3 full
                mcCpuClock = 12; mcPpuClock = 3; PalNMI();             // t5
                mcCpuClock = 11; mcPpuClock = 2; ppu_half_step_new(); // t6
                mcCpuClock = 9;  mcPpuClock = 5; ppu_step_new();      // t8 full
                mcCpuClock = 6;  mcPpuClock = 2; ppu_half_step_new(); // t11
                mcCpuClock = 5;  mcPpuClock = 1; PalIRQ();             // t12
                mcCpuClock = 4;  mcPpuClock = 5; ppu_step_new();      // t13 full
                mcCpuClock = 1;  mcPpuClock = 2; ppu_half_step_new(); // t16
            }
            else if (state == 14)
            {
                // NestedTick2_PAL case 2 did APU + PPU-half@(16,2). Tail from t3.
                mcCpuClock = 14; mcPpuClock = 5; ppu_step_new();
                mcCpuClock = 12; mcPpuClock = 3; PalNMI();
                mcCpuClock = 11; mcPpuClock = 2; ppu_half_step_new();
                mcCpuClock = 9;  mcPpuClock = 5; ppu_step_new();
                mcCpuClock = 6;  mcPpuClock = 2; ppu_half_step_new();
                mcCpuClock = 5;  mcPpuClock = 1; PalIRQ();
                mcCpuClock = 4;  mcPpuClock = 5; ppu_step_new();
                mcCpuClock = 1;  mcPpuClock = 2; ppu_half_step_new();
            }
            else // state == 9: NestedTick7 case 2 fired APU, PPU-half@t1, PPU-full@t3, NMI@t5, PPU-half@t6. Tail from t8.
            {
                mcCpuClock = 9;  mcPpuClock = 5; ppu_step_new();
                mcCpuClock = 6;  mcPpuClock = 2; ppu_half_step_new();
                mcCpuClock = 5;  mcPpuClock = 1; PalIRQ();
                mcCpuClock = 4;  mcPpuClock = 5; ppu_step_new();
                mcCpuClock = 1;  mcPpuClock = 2; ppu_half_step_new();
            }

            mcCpuClock = 0; mcPpuClock = 1;
        }

        // ── Gate 5 — enters (0, 1), exits (0, 0) ──
        // Full: APU, PPU-full@(15,5), NMI@(12,2)+PPU-half@(12,2), PPU-full@(10,5),
        //   PPU-half@(7,2), IRQ@(5,0)+PPU-full@(5,5), PPU-half@(2,2). End (0, 0).
        // NOTE: at t5 state (12, 2), slow path fires NMI then PPU half.
        //       at t12 state (5, 0), slow path fires IRQ then PPU full.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe void PalGate5()
        {
            mcCpuClock = 16; mcPpuClock = 1;
            bool isDmcActive = dmcDmaRunning & (dmcStatusEnabled | dmcImplicitAbortActive);
            if (cpuIsRead & (isDmcActive | spriteDmaTransfer)) DmaOneCycle();
            else cpu_step_one_cycle();
            if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive = false;
            MapperObj.CpuCycle();

            int state = mcCpuClock;

            if (state == 16)
            {
                apu_step(); mcApuPutCycle = !mcApuPutCycle;
                mcCpuClock = 15; mcPpuClock = 5; ppu_step_new();       // t2 full
                mcCpuClock = 12; mcPpuClock = 2; PalNMI();              // t5 NMI
                                                  ppu_half_step_new();  // t5 PPU half
                mcCpuClock = 10; mcPpuClock = 5; ppu_step_new();       // t7 full
                mcCpuClock = 7;  mcPpuClock = 2; ppu_half_step_new();  // t10
                mcCpuClock = 5;  mcPpuClock = 0; PalIRQ();              // t12 IRQ
                                 mcPpuClock = 5; ppu_step_new();       // t12 PPU full
                mcCpuClock = 2;  mcPpuClock = 2; ppu_half_step_new();  // t15
            }
            else if (state == 14)
            {
                // NestedTick2_PAL case 1 did APU + PPU-full@(15,5). Tail from t5.
                mcCpuClock = 12; mcPpuClock = 2; PalNMI();
                                                  ppu_half_step_new();
                mcCpuClock = 10; mcPpuClock = 5; ppu_step_new();
                mcCpuClock = 7;  mcPpuClock = 2; ppu_half_step_new();
                mcCpuClock = 5;  mcPpuClock = 0; PalIRQ();
                                 mcPpuClock = 5; ppu_step_new();
                mcCpuClock = 2;  mcPpuClock = 2; ppu_half_step_new();
            }
            else // state == 9: NestedTick7 case 1 fired APU, PPU-full@t2, NMI@t5, PPU-half@t5, PPU-full@t7. Tail from t10.
            {
                mcCpuClock = 7;  mcPpuClock = 2; ppu_half_step_new();
                mcCpuClock = 5;  mcPpuClock = 0; PalIRQ();
                                 mcPpuClock = 5; ppu_step_new();
                mcCpuClock = 2;  mcPpuClock = 2; ppu_half_step_new();
            }

            mcCpuClock = 0; mcPpuClock = 0;
        }

        // 80-MC PAL unrolled kernel — sequences 5 gates with 3-way nested
        // dispatch each. Entry state: (0, 0). Exit state: (0, 0).
        static unsafe void MasterClockTickUnrolledPAL()
        {
            PalGate1();  // (0,0) → (0,4)
            PalGate2();  // (0,4) → (0,3)
            PalGate3();  // (0,3) → (0,2)
            PalGate4();  // (0,2) → (0,1)
            PalGate5();  // (0,1) → (0,0)
        }

        // ════════════════════════════════════════════════════════════════════
        // Dendy NestedTick variants (Phase 1D)
        //
        // Dendy: CPU=15 MC, PPU=5 MC. LCM(15,5) = 15 MC per window (CPU:PPU
        // ratio 3:1, same structure as NTSC). Gate constants:
        //   mcCpu==11 → NMI (= 15 - 4, "CPU step + 4 MC" rule)
        //   mcCpu==5  → IRQ
        //   mcCpu==15 → APU (= masterPerCpu)
        //   mcPpu==0  → PPU full (reset to 5)
        //   mcPpu==2  → PPU half
        //
        // cpu_step always runs at outer MC 0 with mcCpu reset to 15, mcPpu=0.
        // NestedTick counter trace from (15, 0):
        //   t1 (15,0): APU + PPU full → (14, 4)
        //   t2-t3:     nothing         → (12, 2)
        //   t4 (12,2): PPU half        → (11, 1)
        //   t5 (11,1): NMI             → (10, 0)
        //   t6 (10,0): PPU full        → (9, 4)
        //   t7 (9,4):  nothing         → (8, 3)  [end for N=7]
        //   (for N=2 end after t2:     → (13, 3))
        // ════════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void NestedTick7_Dendy()
        {
            // Call 1 — APU + PPU full at (15, 5)
            apu_step();
            mcApuPutCycle = !mcApuPutCycle;
            mcPpuClock = 5;
            ppu_step_new();

            // Call 4 — PPU half at (12, 2)
            mcCpuClock = 12; mcPpuClock = 2;
            ppu_half_step_new();

            // Call 5 — NMI at (11, 1)
            mcCpuClock = 11; mcPpuClock = 1;
            NMILine |= NMIable && isVblank;
            if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;

            // Call 6 — PPU full at (10, 5)
            mcCpuClock = 10; mcPpuClock = 5;
            ppu_step_new();

            // End state (8, 3)
            mcCpuClock = 8; mcPpuClock = 3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void NestedTick2_Dendy()
        {
            // Call 1 — APU + PPU full
            apu_step();
            mcApuPutCycle = !mcApuPutCycle;
            mcPpuClock = 5;
            ppu_step_new();

            // End state (13, 3)
            mcCpuClock = 13; mcPpuClock = 3;
        }

        // ════════════════════════════════════════════════════════════════════
        // Dendy outer unroll (Phase 2D)
        //
        // Single CPU gate per 15-MC window (like NTSC). 3-way dispatch on
        // post-cpu_step mcCpuClock:
        //   15 → no nesting, run full 15-MC event sequence
        //   13 → NestedTick2_Dendy ran, skip t1 events, run tail from t4
        //    8 → NestedTick7_Dendy ran, skip t1-t6 events, run tail from t9
        //
        // Full event sequence from (0, 0) start:
        //   t1  (15,5): CPU, Mapper.CpuCycle, APU, PPU full
        //   t4  (12,2): PPU half
        //   t5  (11,1): NMI
        //   t6  (10,5): PPU full
        //   t9  (7, 2): PPU half
        //   t11 (5, 4): IRQ + Mapper.CpuClockRise; (5, 5) PPU full
        //   t14 (2, 2): PPU half
        //   End (0, 0)
        // ════════════════════════════════════════════════════════════════════

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void MasterClockTickUnrolledDendy()
        {
            mcCpuClock = 15;
            bool isDmcActive = dmcDmaRunning & (dmcStatusEnabled | dmcImplicitAbortActive);
            if (cpuIsRead & (isDmcActive | spriteDmaTransfer)) DmaOneCycle();
            else cpu_step_one_cycle();
            if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive = false;
            MapperObj.CpuCycle();

            int state = mcCpuClock;

            if (state == 15)
            {
                // No nest — fire full 15-MC event sequence
                apu_step();
                mcApuPutCycle = !mcApuPutCycle;
                mcPpuClock = 5;
                ppu_step_new();                             // t1 PPU full

                mcCpuClock = 12; mcPpuClock = 2;
                ppu_half_step_new();                        // t4

                mcCpuClock = 11; mcPpuClock = 1;
                NMILine |= NMIable && isVblank;
                if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;   // t5 NMI

                mcCpuClock = 10; mcPpuClock = 5;
                ppu_step_new();                             // t6 PPU full

                mcCpuClock = 7; mcPpuClock = 2;
                ppu_half_step_new();                        // t9

                mcCpuClock = 5; mcPpuClock = 4;
                IRQLine = irqLineCurrent;
                if (statusframeint && !apuintflag) irqLineCurrent = true;
                MapperObj.CpuClockRise();                   // t11 IRQ
                mcPpuClock = 5;
                ppu_step_new();                             // t11 PPU full

                mcCpuClock = 2; mcPpuClock = 2;
                ppu_half_step_new();                        // t14
            }
            else if (state == 13)
            {
                // NestedTick2_Dendy fired t1 events (APU + PPU full). Tail from t4.
                mcCpuClock = 12; mcPpuClock = 2;
                ppu_half_step_new();                        // t4

                mcCpuClock = 11; mcPpuClock = 1;
                NMILine |= NMIable && isVblank;
                if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;

                mcCpuClock = 10; mcPpuClock = 5;
                ppu_step_new();

                mcCpuClock = 7; mcPpuClock = 2;
                ppu_half_step_new();

                mcCpuClock = 5; mcPpuClock = 4;
                IRQLine = irqLineCurrent;
                if (statusframeint && !apuintflag) irqLineCurrent = true;
                MapperObj.CpuClockRise();
                mcPpuClock = 5;
                ppu_step_new();

                mcCpuClock = 2; mcPpuClock = 2;
                ppu_half_step_new();
            }
            else // state == 8: NestedTick7_Dendy fired t1-t6 events. Tail from t9.
            {
                mcCpuClock = 7; mcPpuClock = 2;
                ppu_half_step_new();                        // t9

                mcCpuClock = 5; mcPpuClock = 4;
                IRQLine = irqLineCurrent;
                if (statusframeint && !apuintflag) irqLineCurrent = true;
                MapperObj.CpuClockRise();                   // t11 IRQ
                mcPpuClock = 5;
                ppu_step_new();                             // t11 PPU full

                mcCpuClock = 2; mcPpuClock = 2;
                ppu_half_step_new();                        // t14
            }

            // End state (0, 0)
            mcCpuClock = 0; mcPpuClock = 0;
        }

        // MasterClockTickInlineFDS removed 2026-04-14: FDS warm-up now uses
        // WarmUpFDS() above. FDS is NTSC-timed (12 MC CPU, 4 MC PPU); warm-up
        // window contains no CPU gate fires, so fixed event sequence suffices.

        // FDS fast path. Reuses NTSC NestedTickN variants (their event sequence
        // is identical — APU/PPU/NMI events don't touch the mapper, so the only
        // cartridge-side difference (fds_CpuCycle vs MapperObj.CpuCycle) lives
        // exclusively in the outer CPU gate body, never in nested ticks).
        static unsafe void Run_FDS()
        {
            nestedTick7Fn = &NestedTick7_NTSC;
            nestedTick2Fn = &NestedTick2_NTSC;
            WarmUpFDS();

            // One unrolled call = 12 MC. 10000 × 12 = 120K MC per exit check.
            const int ExitCheckInterval = 10000;
            while (!exit)
            {
                for (int i = 0; i < ExitCheckInterval; i++)
                    MasterClockTickUnrolledFDS();
            }
            Console.WriteLine("exit..");
        }

        // 12-MC FDS unrolled kernel — direct port of MasterClockTickUnrolledNTSC
        // with two cartridge-side substitutions:
        //   1. MapperObj.CpuCycle() → fds_CpuCycle()  (FDS disk/audio/IRQ state machine)
        //   2. MapperObj.CpuClockRise() removed       (FdsChrMapper.CpuClockRise is empty)
        // All other timing constants (12/4/2/8/5) and event ordering identical
        // to NTSC since FDS hardware is NTSC-only.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void MasterClockTickUnrolledFDS()
        {
            // ── MC 0: CPU gate (may trigger NestedTickN via cpu_step) ──
            mcCpuClock = 12;
            bool isDmcActive = dmcDmaRunning & (dmcStatusEnabled | dmcImplicitAbortActive);
            if (cpuIsRead & (isDmcActive | spriteDmaTransfer)) DmaOneCycle();
            else cpu_step_one_cycle();
            if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive = false;
            fds_CpuCycle();   // FDS-specific: replaces MapperObj.CpuCycle()

            int state = mcCpuClock; // 12 / 10 / 5

            if (state == 12)
            {
                apu_step();
                mcApuPutCycle = !mcApuPutCycle;
                mcPpuClock = 4;
                ppu_step_new();                            // MC 0 PPU full

                mcCpuClock = 10; mcPpuClock = 2;
                ppu_half_step_new();                       // MC 2

                mcCpuClock = 8; mcPpuClock = 0;
                NMILine |= NMIable && isVblank;
                if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
                mcPpuClock = 4;
                ppu_step_new();                            // MC 4 PPU full

                mcCpuClock = 6; mcPpuClock = 2;
                ppu_half_step_new();                       // MC 6

                mcCpuClock = 5;
                IRQLine = irqLineCurrent;
                if (statusframeint && !apuintflag) irqLineCurrent = true;
                // FDS: MapperObj.CpuClockRise() omitted (FdsChrMapper.CpuClockRise is empty)

                mcCpuClock = 4; mcPpuClock = 4;
                ppu_step_new();                            // MC 8 PPU full

                mcCpuClock = 2; mcPpuClock = 2;
                ppu_half_step_new();                       // MC 10
            }
            else if (state == 10)
            {
                // NestedTick2_NTSC fired MC 0 APU + PPU full. State now (10, 2).
                ppu_half_step_new();                       // MC 2

                mcCpuClock = 8; mcPpuClock = 0;
                NMILine |= NMIable && isVblank;
                if (operationCycle == 0 && !(isVblank && NMIable)) NMILine = false;
                mcPpuClock = 4;
                ppu_step_new();                            // MC 4

                mcCpuClock = 6; mcPpuClock = 2;
                ppu_half_step_new();                       // MC 6

                mcCpuClock = 5;
                IRQLine = irqLineCurrent;
                if (statusframeint && !apuintflag) irqLineCurrent = true;
                // FDS: no MapperObj.CpuClockRise()

                mcCpuClock = 4; mcPpuClock = 4;
                ppu_step_new();                            // MC 8

                mcCpuClock = 2; mcPpuClock = 2;
                ppu_half_step_new();                       // MC 10
            }
            else // state == 5: NestedTick7_NTSC fired MC 0-6 events. State (5, 1).
            {
                IRQLine = irqLineCurrent;
                if (statusframeint && !apuintflag) irqLineCurrent = true;
                // FDS: no MapperObj.CpuClockRise()

                mcCpuClock = 4; mcPpuClock = 4;
                ppu_step_new();                            // MC 8

                mcCpuClock = 2; mcPpuClock = 2;
                ppu_half_step_new();                       // MC 10
            }

            mcCpuClock = 0;
            mcPpuClock = 0;
        }

        // MasterClockTickInlineDendy removed 2026-04-14: Dendy warm-up now
        // uses WarmUpDendy() above. Dendy LCM=15 with CPU:PPU ratio 3:1;
        // warm-up window contains no CPU gate fires, so fixed event sequence
        // suffices. Gate constants (NMI mcCpu==11, IRQ mcCpu==5, APU mcCpu==15,
        // PPU full mcPpu==0→5, PPU half mcPpu==2) embedded directly in
        // WarmUpDendy and MasterClockTickUnrolledDendy / NestedTick*_Dendy.

        static unsafe void Run_Dendy()
        {
            nestedTick7Fn = &NestedTick7_Dendy;
            nestedTick2Fn = &NestedTick2_Dendy;
            WarmUpDendy();

            // One unrolled call = 15 MC. 8000 × 15 = 120K MC per exit check.
            const int ExitCheckInterval = 8000;
            while (!exit)
            {
                for (int i = 0; i < ExitCheckInterval; i++)
                    MasterClockTickUnrolledDendy();
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
            nestedTick7Fn = &NestedTick7_PAL;
            nestedTick2Fn = &NestedTick2_PAL;
            AlignPhaseForFastPath();

            // One unrolled call = 80 MC. 1500 × 80 = 120K MC per exit check.
            const int ExitCheckInterval = 1500;
            while (!exit)
            {
                for (int i = 0; i < ExitCheckInterval; i++)
                    MasterClockTickUnrolledPAL();
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
