using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AprNes
{
    // =========================================================================
    // NES APU — 實作 Pulse1/2、Triangle、Noise、DMC 五個音效聲道
    // 音效樣本透過 AudioSampleReady callback 送出，由外部播放器（WaveOutPlayer）消費。
    // =========================================================================
    public unsafe partial class NesCore
    {
        // =====================================================================
        // 音效樣本輸出介面 (由外部訂閱，例如 WaveOutPlayer)
        // =====================================================================
        static public Action<short, short> AudioSampleReady; // (L, R) stereo pair

        // =====================================================================
        // APU 基本常數
        // =====================================================================
        const int    APU_SAMPLE_RATE = 44100;
        // CPU_FREQ is now region-dependent: use cpuFreq field from Main.cs

        static double _sampleAccum  = 0.0;
        static double _cycPerSample = 1789773.0 / APU_SAMPLE_RATE; // recalculated in initAPU from cpuFreq

        // 音效開關與音量 (可由 UI 控制)
        static public bool AudioEnabled = true;
        static public int Volume = 70; // 0~100

        // =====================================================================
        // 各聲道狀態
        // =====================================================================

        // Pulse 1 & 2 (方波聲道)
        static int* _pulseTimer; // 計時器目前值
        static int* _pulsePeriod; // 11-bit 週期 (從 register 寫入)
        static int* _pulseSeq; // duty 序列位置 (0-7)
        static int* _pulseDuty; // duty 種類 (0-3)
        static int* _pulseOut; // 目前輸出 (0 或 1)

        // Triangle (三角波聲道)
        static int _triTimer  = 0;
        static int _triPeriod = 0;
        static int _triSeq    = 0; // 0-31 序列位置
        static int _triOut    = 0;
        static int* TRI_SEQ;

        // Noise (雜音聲道)
        static int    _noiseTimer     = 0;
        static int    _noisePeriodIdx = 0;
        static ushort _noiseLfsr      = 1; // 15-bit LFSR，初始值=1
        static bool   _noiseMode      = false; // false=bit1, true=bit6
        static int    _noiseOut       = 0;

        // 混音查找表
        static int* SQUARELOOKUP;
        static int* TNDLOOKUP;

        // DC 消除狀態 (high-pass filter ~90 Hz) — Pure Digital 基線濾波
        static int _dckiller = 0;

        // Expansion audio (VRC6, Namco163, VRC7 etc.) — set by mapper each CPU cycle
        static public int mapperExpansionAudio = 0;

        // ── Expansion Audio 多 channel 獨立處理 ──
        // Mapper 啟動時設定 chipType 和 channelCount，每 cycle 寫入 expansionChannels[]
        // Mode 2 (Modern) 使用獨立 oversampler；Mode 0/1 用 mapperExpansionAudio 向後相容
        public enum ExpansionChipType : byte
        {
            None = 0,
            VRC6 = 1,      // Mapper 024/026 — 2 Pulse + 1 Sawtooth (3 ch)
            VRC7 = 2,      // Mapper 085 — 6 FM (OPLL) (1 ch mixed output)
            Namco163 = 3,  // Mapper 019 — 1~8 Wavetable (dynamic)
            Sunsoft5B = 4, // Mapper 069 — 3 Square (5B) (3 ch)
            MMC5 = 5,      // Mapper 005 — 2 Pulse + PCM (future)
            FDS = 6,       // FDS — Wavetable (future)
        }

        static public ExpansionChipType expansionChipType = ExpansionChipType.None;
        static public int   expansionChannelCount = 0;       // 0~8
        static public int[] expansionChannels = new int[8];   // raw output per channel

        // 每晶片增益 — 匹配原有 mapperExpansionAudio 乘數
        // 讓 per-channel × gain 加總後落在 NES APU 混音範圍 (~0-98302)
        // Mode 2 再透過 ÷98302 正規化至 0-1.0 與 NES channel 對齊
        // N163: mapper 端已除以 (numCh+1)，所以增益固定 500
        static readonly float[] DefaultChipGain = new float[]
        {
            0f,    // None
            740f,  // VRC6:      max≈45140 (≈APU range 1/2)
            3f,    // VRC7:      OPLL raw ±12285, ×3 → max≈36855
            500f,  // Namco163:  mapper 已 ÷(numCh+1), ×500 → max≈60000
            120f,  // Sunsoft5B: 原 sum×120, max≈63720
            43f,   // MMC5      (future)
            20f,   // FDS       (future)
        };

        // ── Per-channel 音量 (Mode 2 per-channel, Mode 0/1 per-chip average) ──
        // [0]=Pulse1, [1]=Pulse2, [2]=Triangle, [3]=Noise, [4]=DMC
        // [5..12]=Expansion ch0~ch7 (VRC6: P1/P2/Saw, N163: ch0~ch7, 5B: A/B/C, etc.)
        // 範圍 0~100, 0=靜音, 100=該聲道最大
        static public int[] ChannelVolume = new int[] { 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70, 70 };
        // Per-channel enable/disable (CheckBox mute)
        // [0]=Pulse1, [1]=Pulse2, [2]=Tri, [3]=Noise, [4]=DMC, [5..12]=Exp ch0~7
        static public bool[] ChannelEnabled = new bool[] { true, true, true, true, true, true, true, true, true, true, true, true, true };

        // Mode 0/1 擴展音效增益 (從 ChannelVolume[5..] 平均值預算, 由 AudioPlus 更新)
        static public float ap_mode01ExpGain = 0f;

        // =====================================================================
        // 原有 APU 欄位 (保留相容)
        // =====================================================================
        static int apucycle = 0;
        static int* noiseperiod;
        // Frame counter — TriCNES count-up model
        // Counter increments every CPU cycle; events fire at hardcoded positions via switch.
        static ushort apuFrameCounter = 0;        // TriCNES: APU_Framecounter (count-up)
        static byte apuFrameCounterReset = 0xFF;  // TriCNES: APU_FrameCounterReset (0xFF=inactive, 0-4=countdown)
        static bool apuQuarterFrame = false;       // TriCNES: APU_QuarterFrameClock
        static bool apuHalfFrame = false;          // TriCNES: APU_HalfFrameClock
        static int ctrmode = 4;                    // 4=4-step, 5=5-step
        static bool apuintflag = true, statusdmcint = false, statusframeint = false;
        static byte last4017Val = 0;
        static byte* lenCtrEnable;
        static int* volume;

        // DMC 欄位
        static int* dmcperiods;
        static int dmcrate = 0x36, dmctimer = 0x36, dmcshiftregister = 0, dmcbuffer = 0,
                   dmcvalue = 0, dmcsamplelength = 1, dmcsamplesleft = 0,
                   dmcstartaddr = 0xc000, dmcaddr = 0xc000, dmcbitsleft = 8;
        static bool dmcsilence = true, dmcirq = false, dmcloop = false, dmcBufferEmpty = true;
        static int dmcLoadDmaCountdown = 0;    // Load DMA scheduling delay (2-3 APU cycles)
        static int dmcStatusDelay = 0;         // Deferred $4015 status update countdown (TriCNES: APU_DelayedDMC4015)
        static bool dmcDelayedEnable = false;  // Pending DMC enable/disable value (TriCNES: APU_Status_DelayedDMC)
        static bool dmcAbortDma = false;       // Abort flag for in-progress DMA (Mesen2: _abortDmcDma)
        static int dmcDmaCooldown = 0;         // TriCNES: CannotRunDMCDMARightNow (blocks new DMA for 2 cycles after completion)
        static bool dmcImplicitAbortPending = false;  // TriCNES: APU_SetImplicitAbortDMC4015
        static bool dmcImplicitAbortActive = false;   // TriCNES: APU_ImplicitAbortDMC4015
        static bool dmcStatusEnabled = false;         // TriCNES: APU_Status_DMC — per-cycle DMA gate

        // Length counter — TriCNES deferred reload flag model
        static int* lengthctr;
        static int* lenctrload;         // LUT: 32-entry length counter load table
        static bool[] lenCtrReloadFlag = new bool[4];   // deferred reload pending
        static int[] lenCtrReloadValue = new int[4];     // deferred reload value
        // Halt read from register every APU cycle (TriCNES model)
        static byte* apuRegister;       // raw $4000-$400F register values (for halt readback)

        // Linear counter (Triangle)
        static int linearctr  = 0;
        static int linctrreload = 0;
        static bool linctrflag = false;

        // Envelope 欄位
        static int*  envelopeValue;
        static int*  envelopeCounter;
        static int*  envelopePos;
        static byte* envConstVolume;
        static byte* envelopeStartFlag;

        // Sweep 欄位 (Pulse 1 & 2)
        static byte* sweepenable;
        static byte* sweepnegate;
        static byte* sweepsilence;
        static byte* sweepreload;
        static int*  sweepperiod;
        static int*  sweepshift;
        static int*  sweeppos;

        // Duty 波形查找表 (flattened 4×8 → 32 ints, index = duty*8 + seq)
        static int* DUTYLOOKUP;

        // =====================================================================
        // APU Soft Reset — 只重置內部狀態，不碰 WaveOut 音效設備
        // 在模擬線程內由 ResetInterrupt() 呼叫，避免跨線程存取
        // =====================================================================
        static void apuSoftReset()
        {
            apucycle = 0;

            // Re-apply last $4017 value (nesdev: "at reset, $4017 rewritten with last value")
            ctrmode    = ((last4017Val & 0x80) != 0) ? 5 : 4;
            apuintflag = (last4017Val & 0x40) != 0;
            if (apuintflag) { statusframeint = false; irqLineCurrent = false; UpdateIRQLine(); }
            if (ctrmode == 5)
            {
                apuQuarterFrame = true;
                apuHalfFrame = true;
            }
            // Deferred reset (same as $4017 write mechanism)
            apuFrameCounter = 0;
            apuFrameCounterReset = (byte)(mcApuPutCycle ? 3 : 4);

            // 清除 IRQ flags
            statusframeint = false;
            statusdmcint = false;
            UpdateIRQLine();

            // 模擬 $4015=$00: 停止所有聲道
            for (int i = 0; i < 4; i++)
            {
                lenCtrEnable[i] = 0;
                lengthctr[i] = 0;
            }
            dmcsamplesleft = 0;
            dmcLoadDmaCountdown = 0;
            dmcStatusDelay = 0;
            dmcDelayedEnable = false;

            // 重置音色產生器
            _pulseTimer[0] = _pulseTimer[1] = 0;
            _pulsePeriod[0] = _pulsePeriod[1] = 0;
            _pulseSeq[0] = _pulseSeq[1] = 0;
            _pulseDuty[0] = _pulseDuty[1] = 0;
            _pulseOut[0] = _pulseOut[1] = 0;
            _triTimer = _triPeriod = _triSeq = _triOut = 0;
            _noiseTimer = 0; _noisePeriodIdx = 0; _noiseLfsr = 1;
            _noiseMode = false; _noiseOut = 0;
            _sampleAccum = 0.0;
            _dckiller    = 0;

            // 清除 expansion audio 的暫存值，但不重設 chipType/channelCount
            // (由 mapper Reset 負責設定，且 mapper Reset 在 apuSoftReset 之前執行)
            mapperExpansionAudio = 0;
            for (int i = 0; i < 8; i++) expansionChannels[i] = 0;

            AudioPlus_Reset();
        }

        // =====================================================================
        // 初始化 APU
        // =====================================================================
        static void initAPU()
        {
            // Allocate pointer arrays (null-check pattern for re-init safety)
            if (_pulseTimer  == null) _pulseTimer  = (int*)Marshal.AllocHGlobal(sizeof(int) * 2);
            if (_pulsePeriod == null) _pulsePeriod = (int*)Marshal.AllocHGlobal(sizeof(int) * 2);
            if (_pulseSeq    == null) _pulseSeq    = (int*)Marshal.AllocHGlobal(sizeof(int) * 2);
            if (_pulseDuty   == null) _pulseDuty   = (int*)Marshal.AllocHGlobal(sizeof(int) * 2);
            if (_pulseOut    == null) _pulseOut    = (int*)Marshal.AllocHGlobal(sizeof(int) * 2);
            if (volume       == null) volume       = (int*)Marshal.AllocHGlobal(sizeof(int) * 4);
            if (SQUARELOOKUP == null) SQUARELOOKUP = (int*)Marshal.AllocHGlobal(sizeof(int) * 31);
            if (TNDLOOKUP    == null) TNDLOOKUP    = (int*)Marshal.AllocHGlobal(sizeof(int) * 203);
            if (noiseperiod  == null) noiseperiod  = (int*)Marshal.AllocHGlobal(sizeof(int) * 16);
            if (lengthctr    == null) lengthctr    = (int*)Marshal.AllocHGlobal(sizeof(int) * 4);
            if (lenctrload   == null) lenctrload   = (int*)Marshal.AllocHGlobal(sizeof(int) * 32);
            if (apuRegister  == null) apuRegister  = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 16);
            if (envelopeValue   == null) envelopeValue   = (int*)Marshal.AllocHGlobal(sizeof(int) * 4);
            if (envelopeCounter == null) envelopeCounter = (int*)Marshal.AllocHGlobal(sizeof(int) * 4);
            if (envelopePos     == null) envelopePos     = (int*)Marshal.AllocHGlobal(sizeof(int) * 4);
            if (sweepperiod  == null) sweepperiod  = (int*)Marshal.AllocHGlobal(sizeof(int) * 2);
            if (sweepshift   == null) sweepshift   = (int*)Marshal.AllocHGlobal(sizeof(int) * 2);
            if (sweeppos     == null) sweeppos     = (int*)Marshal.AllocHGlobal(sizeof(int) * 2);
            if (dmcperiods   == null) dmcperiods   = (int*)Marshal.AllocHGlobal(sizeof(int) * 16);
            if (lenCtrEnable           == null) lenCtrEnable           = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 4);
            if (envConstVolume         == null) envConstVolume         = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 4);
            if (envelopeStartFlag      == null) envelopeStartFlag      = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 4);
            if (sweepenable            == null) sweepenable            = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 2);
            if (sweepnegate            == null) sweepnegate            = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 2);
            if (sweepsilence           == null) sweepsilence           = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 2);
            if (sweepreload            == null) sweepreload            = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 2);
            if (TRI_SEQ    == null) { TRI_SEQ    = (int*)Marshal.AllocHGlobal(sizeof(int) * 32);
                int[] tv = { 15,14,13,12,11,10,9,8,7,6,5,4,3,2,1,0, 0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15 };
                for (int i = 0; i < 32; i++) TRI_SEQ[i] = tv[i]; }
            if (DUTYLOOKUP == null) { DUTYLOOKUP = (int*)Marshal.AllocHGlobal(sizeof(int) * 32);
                int[] dv = { 0,1,0,0,0,0,0,0, 0,1,1,0,0,0,0,0, 0,1,1,1,1,0,0,0, 1,0,0,1,1,1,1,1 };
                for (int i = 0; i < 32; i++) DUTYLOOKUP[i] = dv[i]; }

            // Initialize region-dependent data arrays
            _cycPerSample = cpuFreq / APU_SAMPLE_RATE;

            // NTSC only — PAL tables stripped for architecture validation
            noiseperiod[0]=4; noiseperiod[1]=8; noiseperiod[2]=16; noiseperiod[3]=32;
            noiseperiod[4]=64; noiseperiod[5]=96; noiseperiod[6]=128; noiseperiod[7]=160;
            noiseperiod[8]=202; noiseperiod[9]=254; noiseperiod[10]=380; noiseperiod[11]=508;
            noiseperiod[12]=762; noiseperiod[13]=1016; noiseperiod[14]=2034; noiseperiod[15]=4068;

            dmcperiods[0]=428; dmcperiods[1]=380; dmcperiods[2]=340; dmcperiods[3]=320;
            dmcperiods[4]=286; dmcperiods[5]=254; dmcperiods[6]=226; dmcperiods[7]=214;
            dmcperiods[8]=190; dmcperiods[9]=160; dmcperiods[10]=142; dmcperiods[11]=128;
            dmcperiods[12]=106; dmcperiods[13]=84; dmcperiods[14]=72; dmcperiods[15]=54;

            { int[] _lv = { 10,254,20,2,40,4,80,6,160,8,60,10,14,12,26,14,12,16,24,18,48,20,96,22,192,24,72,26,16,28,32,30 };
              for (int i = 0; i < 32; i++) lenctrload[i] = _lv[i]; }

            for (int i = 0; i < 31; i++)
                SQUARELOOKUP[i] = (int)((95.52 / (8128.0 / (i == 0 ? 0.0001 : i) + 100)) * 49151);
            for (int i = 0; i < 203; i++)
                TNDLOOKUP[i] = (int)((163.67 / (24329.0 / (i == 0 ? 0.0001 : i) + 100)) * 49151);

            // Default bool* arrays
            for (int i = 0; i < 4; i++) { lenCtrEnable[i] = 1; envConstVolume[i] = 1; envelopeStartFlag[i] = 0; lenCtrReloadFlag[i] = false; lenCtrReloadValue[i] = 0; }
            for (int i = 0; i < 16; i++) apuRegister[i] = 0;
            for (int i = 0; i < 2; i++) { sweepenable[i] = 0; sweepnegate[i] = 0; sweepsilence[i] = 0; sweepreload[i] = 0; }

            apuQuarterFrame = false;
            apuHalfFrame = false;
            apucycle    = 0;
            ctrmode = 4;
            apuintflag = false;
            // Power-on: implicit $4017=$00 write (NES hardware)
            // Pre-advance counter by CPU reset sequence (7 reset cycles + 2 alignment)
            // Old model: framectrdiv = 7458 - 9 = 7449. New model: start counter at 9.
            apuFrameCounter = 9;
            apuFrameCounterReset = 0xFF;

            // 聲道計時器重置
            _pulseTimer[0]  = _pulseTimer[1]  = 0;
            _pulsePeriod[0] = _pulsePeriod[1] = 0;
            _pulseSeq[0]    = _pulseSeq[1]    = 0;
            _pulseDuty[0]   = _pulseDuty[1]   = 0;
            _pulseOut[0]    = _pulseOut[1]    = 0;
            _triTimer  = _triPeriod = _triSeq = _triOut = 0;
            _noiseTimer = 0; _noisePeriodIdx = 0; _noiseLfsr = 1;
            _noiseMode = false; _noiseOut = 0;
            _sampleAccum = 0.0;
            _dckiller    = 0;
            AudioPlus_Reset();

            // Power-on 狀態 (模擬 $4015=$00, $4017=$00)
            for (int i = 0; i < 4; i++)
            {
                lenCtrEnable[i] = 0;
                lengthctr[i] = 0;
                volume[i] = 0;
                lenCtrReloadFlag[i] = false;
                lenCtrReloadValue[i] = 0;
                envelopeValue[i] = 0;
                envelopeCounter[i] = 0;
                envelopePos[i] = 0;
                envConstVolume[i] = 0;
                envelopeStartFlag[i] = 0;
            }
            for (int i = 0; i < 2; i++)
            {
                sweepenable[i] = 0;
                sweepnegate[i] = 0;
                sweepsilence[i] = 0;
                sweepreload[i] = 0;
                sweepperiod[i] = 0;
                sweepshift[i] = 0;
                sweeppos[i] = 0;
            }
            linearctr = 0; linctrreload = 0; linctrflag = false;
            apuintflag = false;      // $4017=$00: IRQ 未禁止
            statusframeint = false;
            statusdmcint = false;
            UpdateIRQLine();

            // DMC 完整重置
            dmcrate = dmcperiods[0]; dmctimer = dmcrate;
            dmcshiftregister = 0; dmcbuffer = 0;
            dmcvalue = 0; dmcsamplelength = 1; dmcsamplesleft = 0;
            dmcstartaddr = 0xC000; dmcaddr = 0xC000; dmcbitsleft = 8;
            dmcsilence = true; dmcirq = false; dmcloop = false; dmcBufferEmpty = true;
            dmcLoadDmaCountdown = 0; dmcStatusDelay = 0; dmcDelayedEnable = false; dmcAbortDma = false;
            dmcDmaRunning = false; dmcDmaHalt = false;
            dmcDmaCooldown = 0; dmcImplicitAbortPending = false; dmcImplicitAbortActive = false; dmcStatusEnabled = false;
            spriteDmaTransfer = false; spriteDmaOffset = 0;
            dmaOamHalt = false; dmaOamAligned = false; dmaFirstCycleOam = false;
            dmaOamInternalBus = 0; dmaOamAddr = 0; dmaEnableInternalRegReads = false;
        }

        // =====================================================================
        // APU Step — TriCNES _EmulateAPU() order:
        //   GET cycle: Pulse/Noise timers, DMC clock, DMC cooldown
        //   PUT cycle: DMC Load DMA countdown
        //   Both:      DMC $4015 delay, Triangle timer, Frame counter, Quarter/Half frame
        // =====================================================================
        static void apu_step()
        {
            apucycle++;

            // ── GET cycle block (TriCNES: !APU_PutCycle) ──
            if (!mcApuPutCycle)
            {
                // Pulse & Noise timers (every GET cycle = every 2 CPU cycles)
                {
                    int p0 = _pulsePeriod[0], p1 = _pulsePeriod[1];
                    int d0 = _pulseDuty[0],   d1 = _pulseDuty[1];
                    int lc0 = lengthctr[0],   lc1 = lengthctr[1];
                    int sw0 = sweepsilence[0], sw1 = sweepsilence[1];

                    if (--_pulseTimer[0] < 0)
                    { _pulseTimer[0] = p0; _pulseSeq[0] = (_pulseSeq[0] + 1) & 7; }
                    _pulseOut[0] = (p0 >= 8 && lc0 > 0 && sw0 == 0)
                        ? DUTYLOOKUP[d0 * 8 + _pulseSeq[0]] : 0;

                    if (--_pulseTimer[1] < 0)
                    { _pulseTimer[1] = p1; _pulseSeq[1] = (_pulseSeq[1] + 1) & 7; }
                    _pulseOut[1] = (p1 >= 8 && lc1 > 0 && sw1 == 0)
                        ? DUTYLOOKUP[d1 * 8 + _pulseSeq[1]] : 0;

                    if (--_noiseTimer < 0)
                    {
                        _noiseTimer = noiseperiod[_noisePeriodIdx] >> 1;
                        int fb = _noiseMode
                            ? ((_noiseLfsr & 1) ^ ((_noiseLfsr >> 6) & 1))
                            : ((_noiseLfsr & 1) ^ ((_noiseLfsr >> 1) & 1));
                        _noiseLfsr = (ushort)((_noiseLfsr >> 1) | (fb << 14));
                    }
                    _noiseOut = (lengthctr[3] > 0 && (_noiseLfsr & 1) == 0) ? 1 : 0;
                }

                // DMC clock (timer -2 per GET cycle, output, buffer→shifter, reload DMA)
                clockdmc();

                // DMC cooldown (TriCNES: CannotRunDMCDMARightNow -= 2 per GET)
                if (dmcDmaCooldown > 0) dmcDmaCooldown -= 2;
            }
            else
            {
                // ── PUT cycle block (TriCNES: APU_PutCycle) ──

                // DMC Load DMA countdown (from $4015 write)
                // TriCNES: DMCDMADelay decrements on PUT cycles
                if (dmcLoadDmaCountdown > 0)
                {
                    --dmcLoadDmaCountdown;
                    if (dmcLoadDmaCountdown == 0 && dmcBufferEmpty && dmcsamplesleft > 0)
                        dmcStartTransfer();
                }
            }

            // ── Both cycles ──

            // DMC deferred $4015 status update
            if (dmcStatusDelay > 0)
            {
                --dmcStatusDelay;
                if (dmcStatusDelay == 0)
                {
                    dmcStatusEnabled = dmcDelayedEnable;
                    if (!dmcDelayedEnable)
                    {
                        dmcsamplesleft = 0;
                        dmcStopTransfer();
                    }
                }
            }

            // Triangle timer (every CPU cycle)
            {
                int triPeriod = _triPeriod;
                int lc2 = lengthctr[2];
                int linCtr = linearctr;
                if (--_triTimer < 0)
                {
                    _triTimer = triPeriod;
                    if (linCtr > 0 && lc2 > 0 && triPeriod >= 2)
                        _triSeq = (_triSeq + 1) & 31;
                }
                _triOut = (linCtr > 0 && lc2 > 0 && triPeriod >= 2)
                    ? TRI_SEQ[_triSeq] : 0;
            }

            // ── Frame Counter (TriCNES: after timers) ──
            if ((apuFrameCounterReset & 0x80) == 0)
            {
                apuFrameCounterReset--;
                if ((apuFrameCounterReset & 0x80) != 0)
                    apuFrameCounter = 0;
            }

            apuFrameCounter++;

            // Frame counter switch: OR flags (preserve $4017-set flags)
            if (ctrmode == 5)
            {
                switch (apuFrameCounter)
                {
                    case 7457: apuQuarterFrame = true; break;
                    case 14913: apuQuarterFrame = true; apuHalfFrame = true; break;
                    case 22371: apuQuarterFrame = true; break;
                    case 29829: break;
                    case 37281: apuQuarterFrame = true; apuHalfFrame = true; break;
                    case 37282: apuFrameCounter = 0; break;
                }
            }
            else
            {
                switch (apuFrameCounter)
                {
                    case 7457: apuQuarterFrame = true; break;
                    case 14913: apuQuarterFrame = true; apuHalfFrame = true; break;
                    case 22371: apuQuarterFrame = true; break;
                    case 29828: statusframeint = true; break;
                    case 29829:
                        apuQuarterFrame = true; apuHalfFrame = true;
                        statusframeint = true;
                        irqLineCurrent |= !apuintflag;
                        break;
                    case 29830:
                        statusframeint = !apuintflag;
                        irqLineCurrent |= !apuintflag;
                        apuFrameCounter = 0;
                        break;
                }
            }

            // Process quarter/half frame, then clear flags (TriCNES: clear inside processing)
            if (apuQuarterFrame) { setenvelope(); setlinctr(); apuQuarterFrame = false; }
            if (apuHalfFrame) { setlength(); setsweep(); apuHalfFrame = false; }
            else { processLenCtrReloadNonHalf(); }
            setvolumes();

            // 生成音效樣本
            // 為 Mode 0/1 計算相容的單一 mapperExpansionAudio 值
            if (expansionChannelCount > 0 && AudioMode < 2)
            {
                float gain = ap_mode01ExpGain;
                int sum = 0;
                for (int i = 0; i < expansionChannelCount; i++)
                {
                    if (ChannelEnabled[5 + i])
                        sum += (int)(expansionChannels[i] * gain);
                }
                mapperExpansionAudio = sum;
            }

            // Apply per-channel enable/mute
            int sq1val  = ChannelEnabled[0] ? volume[0] * _pulseOut[0] : 0;
            int sq2val  = ChannelEnabled[1] ? volume[1] * _pulseOut[1] : 0;
            int trival  = ChannelEnabled[2] ? _triOut : 0;
            int noisval = ChannelEnabled[3] ? volume[3] * _noiseOut : 0;
            int dmcval  = ChannelEnabled[4] ? dmcvalue : 0;

            if (AudioMode > 0)
            {
                // Authentic / Modern: 每 APU cycle 推入 AudioPlus
                AudioPlus_PushApuCycle(sq1val, sq2val, trival, noisval, dmcval, mapperExpansionAudio);
            }
            else
            {
                // Pure Digital: 原有 ~40.58 cycle 降頻 + DC killer
                _sampleAccum += 1.0;
                if (_sampleAccum >= _cycPerSample)
                {
                    _sampleAccum -= _cycPerSample;
                    generateSample(sq1val, sq2val, trival, noisval, dmcval);
                }
            }
        }

        // =====================================================================
        // 混音並送出樣本
        // =====================================================================
        static void generateSample(int sq1, int sq2, int tri, int noise, int dmc)
        {
            if (!AudioEnabled) return;

            // Pulse 混音 (非線性查找表)
            int sqIdx = sq1 + sq2;
            if (sqIdx >= 31) sqIdx = 30;

            // TND 混音
            int tndIdx = 3 * tri + 2 * noise + dmc;
            if (tndIdx >= 203) tndIdx = 202;

            int mixed = SQUARELOOKUP[sqIdx] + TNDLOOKUP[tndIdx]; // 0..~98302
            mixed += mapperExpansionAudio; // expansion audio (VRC6, Namco163, VRC7...)

            // High-pass filter ~90 Hz — DC 偏移消除（Pure Digital 基線）
            mixed += _dckiller;
            _dckiller -= mixed >> 8;
            _dckiller += (mixed > 0 ? -1 : 1);

            // 縮放至 16-bit signed，套用使用者音量
            int clamped = mixed * Volume / 100;
            if (clamped >  32767) clamped =  32767;
            if (clamped < -32768) clamped = -32768;

            AudioSampleReady?.Invoke((short)clamped, (short)clamped); // dual mono

            // RF 音訊干擾：將實際音訊電平饋入 NTSC RF 模擬
            // RfAudioLevel = |sample| 指數平滑 → buzz bar 振幅
            // RfBuzzPhase  = 累積音量 → bar 垂直滾動速度（音量越大滾越快）
            if (AnalogEnabled && AnalogOutput == AnalogOutputMode.RF)
            {
                float absS = clamped < 0 ? -clamped / 32767f : clamped / 32767f;
                RfAudioLevel = RfAudioLevel * 0.95f + absS * 0.05f;
                RfBuzzPhase  = (RfBuzzPhase + absS * 0.0001f) % 1.0f;
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void setvolumes()
        {
            volume[0] = ((lengthctr[0] <= 0 || sweepsilence[0] != 0) ? 0
                : (envConstVolume[0] != 0 ? envelopeValue[0] : envelopeCounter[0]));
            volume[1] = ((lengthctr[1] <= 0 || sweepsilence[1] != 0) ? 0
                : (envConstVolume[1] != 0 ? envelopeValue[1] : envelopeCounter[1]));
            volume[3] = (lengthctr[3] <= 0 ? 0
                : (envConstVolume[3] != 0 ? envelopeValue[3] : envelopeCounter[3]));
        }

        // TriCNES HalfFrame length counter: reload-first → status-zero → decrement (guarded)
        static void setlength()
        {
            // 1. Reload (only if flag set AND counter==0)
            for (int i = 0; i < 4; i++)
            {
                if (lenCtrReloadFlag[i] && lengthctr[i] == 0)
                    lengthctr[i] = lenCtrReloadValue[i];
                else
                    lenCtrReloadFlag[i] = false;
            }
            // 2. Status disable ($4015 bit=0 → zero counter)
            for (int i = 0; i < 4; i++)
                if (lenCtrEnable[i] == 0) lengthctr[i] = 0;
            // 3. Decrement (guarded: !halt && !reloadFlag)
            bool haltP1 = (apuRegister[0x0] & 0x20) != 0;
            bool haltP2 = (apuRegister[0x4] & 0x20) != 0;
            bool haltTri = (apuRegister[0x8] & 0x80) != 0;
            bool haltNoi = (apuRegister[0xC] & 0x20) != 0;
            if (lengthctr[0] > 0 && !haltP1 && !lenCtrReloadFlag[0]) lengthctr[0]--;
            if (lengthctr[1] > 0 && !haltP2 && !lenCtrReloadFlag[1]) lengthctr[1]--;
            if (lengthctr[2] > 0 && !haltTri && !lenCtrReloadFlag[2]) lengthctr[2]--;
            if (lengthctr[3] > 0 && !haltNoi && !lenCtrReloadFlag[3]) lengthctr[3]--;
            setvolumes();
        }

        // TriCNES: non-HalfFrame cycle — unconditional reload if flag set, then clear
        static void processLenCtrReloadNonHalf()
        {
            for (int i = 0; i < 4; i++)
            {
                if (lenCtrReloadFlag[i])
                {
                    lengthctr[i] = lenCtrReloadValue[i];
                    lenCtrReloadFlag[i] = false;
                }
            }
        }

        static void setlinctr()
        {
            if (linctrflag)
                linearctr = linctrreload;
            else if (linearctr > 0)
                --linearctr;
            // TriCNES: halt flag from register (triangle's halt = $4008 bit 7)
            if ((apuRegister[0x8] & 0x80) == 0)
                linctrflag = false;
        }

        static void setenvelope()
        {
            for (int i = 0; i < 4; ++i)
            {
                if (envelopeStartFlag[i] != 0)
                {
                    envelopeStartFlag[i] = 0;
                    envelopePos[i]       = envelopeValue[i] + 1;
                    envelopeCounter[i]   = 15;
                }
                else
                {
                    --envelopePos[i];
                }
                if (envelopePos[i] <= 0)
                {
                    envelopePos[i] = envelopeValue[i] + 1;
                    if (envelopeCounter[i] > 0)
                        --envelopeCounter[i];
                    // Loop flag = halt flag: Pulse=$4000/$4004 bit5, Noise=$400C bit5
                    else if (envelopeCounter[i] <= 0 && (apuRegister[i * 4] & 0x20) != 0)
                        envelopeCounter[i] = 15;
                }
            }
        }

        static void setsweep()
        {
            for (int i = 0; i < 2; ++i)
            {
                sweepsilence[i] = 0;
                if (sweepreload[i] != 0)
                {
                    sweepreload[i] = 0;
                    sweeppos[i]    = sweepperiod[i];
                }
                ++sweeppos[i];
                int rawperiod     = _pulsePeriod[i]; // 使用正確的週期值
                int shiftedperiod = rawperiod >> sweepshift[i];
                if (sweepnegate[i] != 0)
                    shiftedperiod = -shiftedperiod + i; // channel 2 加 1
                shiftedperiod += rawperiod;

                if (rawperiod < 8 || shiftedperiod > 0x7ff)
                    sweepsilence[i] = 1;
                else if (sweepenable[i] != 0 && sweepshift[i] != 0 && lengthctr[i] > 0
                         && sweeppos[i] > sweepperiod[i])
                {
                    sweeppos[i]      = 0;
                    _pulsePeriod[i]  = shiftedperiod;
                }
            }
        }

        // =====================================================================
        // DMC clock — timer -2 per GET cycle (TriCNES model)
        // Rate table values are in CPU cycles; -2 per GET = -1 per CPU cycle net rate
        // =====================================================================
        static void clockdmc()
        {
            dmctimer -= 2;
            if (dmctimer <= 0)
            {
                dmctimer = dmcrate;

                if (!dmcsilence)
                {
                    dmcvalue += ((dmcshiftregister & 1) != 0) ? 2 : -2;
                    if (dmcvalue > 0x7f) dmcvalue = 0x7f;
                    if (dmcvalue < 0)    dmcvalue  = 0;
                    dmcshiftregister >>= 1;
                }
                --dmcbitsleft;
                if (dmcbitsleft <= 0)
                {
                    dmcbitsleft = 8;
                    if (dmcBufferEmpty)
                        dmcsilence = true;
                    else
                    {
                        dmcsilence        = false;
                        dmcshiftregister  = dmcbuffer;
                        dmcBufferEmpty    = true;
                    }
                }
            }

            // Reload DMA: buffer emptied → request DMA
            if (!dmcDmaRunning && dmcBufferEmpty && (dmcsamplesleft > 0 || dmcImplicitAbortPending))
            {
                if (dmcDmaCooldown != 2)
                {
                    if (dmcImplicitAbortPending)
                    {
                        dmcImplicitAbortActive = true;
                        dmcImplicitAbortPending = false;
                    }
                    dmcStartTransfer();
                }
            }
        }

        // Request DMC DMA — TriCNES per-cycle model (starts with halt flag)
        static void dmcStartTransfer()
        {
            if (!dmcDmaRunning && (dmcBufferEmpty && dmcsamplesleft > 0 || dmcImplicitAbortActive))
            {
                dmcDmaRunning = true;
                dmcDmaHalt = true;
                // Capture bus state for internal register conflict handling (only if no OAM DMA already running)
                if (!spriteDmaTransfer)
                {
                    dmaPrevReadAddress = cpuBusAddr;
                    dmaEnableInternalRegReads = ((cpuBusAddr & 0xFFE0) == 0x4000);
                }
            }
        }

        // Cancel or abort DMC DMA — TriCNES per-cycle model
        static void dmcStopTransfer()
        {
            if (dmcDmaRunning)
            {
                if (dmcDmaHalt) // Still in halt phase — cancel immediately
                {
                    dmcDmaRunning = false;
                    dmcDmaHalt = false;
                }
                else // Past halt — deferred abort (picked up by DmaOneCycle gate)
                {
                    dmcAbortDma = true;
                }
            }
        }

        // Complete DMC DMA read — update buffer and advance address
        // TriCNES: DMCDMA_Get() always saves byte and advances address,
        // only guards the BytesRemaining decrement to prevent underflow.
        static void dmcSetReadBuffer(byte val)
        {
            dmcbuffer = val;
            dmcBufferEmpty = false;
            dmcaddr++;
            if (dmcaddr > 0xffff) dmcaddr = 0x8000;
            if (dmcsamplesleft > 0)
            {
                --dmcsamplesleft;
                if (dmcsamplesleft == 0)
                {
                    if (dmcloop)
                        restartdmc();
                    else if (dmcirq)
                    {
                        statusdmcint = true;
                        UpdateIRQLine();
                    }
                }
            }
        }

        static void restartdmc()
        {
            dmcaddr        = dmcstartaddr;
            dmcsamplesleft = dmcsamplelength;
        }


        // =====================================================================
        // 讀取 $4015 狀態暫存器
        // =====================================================================
        static byte apu_r_4015()
        {
            byte status = 0;
            // TriCNES: reads current counter values (no snapshot)
            if (lengthctr[0] > 0) status |= 0x01;
            if (lengthctr[1] > 0) status |= 0x02;
            if (lengthctr[2] > 0) status |= 0x04;
            if (lengthctr[3] > 0) status |= 0x08;
            // TriCNES: uses APU_Status_DelayedDMC (immediate write value) for $4015 reads
            // This ensures bit 4 reflects the last $4015 write immediately, even during deferred delay
            if (dmcsamplesleft > 0 && dmcDelayedEnable) status |= 0x10;
            if (statusframeint)     status |= 0x40;
            if (statusdmcint)       status |= 0x80;
            status |= (byte)(cpubus & 0x20); // bit 5 is open bus (CPU data bus)
            statusframeint = false;
            UpdateIRQLine();
            return status;
        }

        // =====================================================================
        // APU Register 寫入處理
        // =====================================================================

        // $4000: Pulse 1 duty/envelope
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void apu_4000(byte val)
        {
            apuRegister[0x0] = val; // store for halt readback
            _pulseDuty[0]       = (val >> 6) & 3;
            envConstVolume[0]   = (byte)((val & 0x10) != 0 ? 1 : 0);
            envelopeValue[0]    = val & 0x0F;
        }
        // $4001: Pulse 1 sweep
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void apu_4001(byte val)
        {
            sweepenable[0] = (byte)((val & 0x80) != 0 ? 1 : 0);
            sweepperiod[0] = (val >> 4) & 7;
            sweepnegate[0] = (byte)((val & 0x08) != 0 ? 1 : 0);
            sweepshift[0]  = val & 7;
            sweepreload[0] = 1;
        }
        // $4002: Pulse 1 timer low
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void apu_4002(byte val)
        {
            _pulsePeriod[0] = (_pulsePeriod[0] & 0x700) | val;
        }
        // $4003: Pulse 1 timer high + length counter (deferred reload)
        static void apu_4003(byte val)
        {
            _pulsePeriod[0] = (_pulsePeriod[0] & 0xFF) | ((val & 7) << 8);
            _pulseTimer[0]  = _pulsePeriod[0];
            _pulseSeq[0]    = 0;
            if (lenCtrEnable[0] != 0)
            { lenCtrReloadValue[0] = lenctrload[(val >> 3) & 0x1F]; lenCtrReloadFlag[0] = true; }
            envelopeStartFlag[0] = 1;
        }
        // $4004: Pulse 2 duty/envelope
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void apu_4004(byte val)
        {
            apuRegister[0x4] = val;
            _pulseDuty[1]     = (val >> 6) & 3;
            envConstVolume[1] = (byte)((val & 0x10) != 0 ? 1 : 0);
            envelopeValue[1]  = val & 0x0F;
        }
        // $4005: Pulse 2 sweep
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void apu_4005(byte val)
        {
            sweepenable[1] = (byte)((val & 0x80) != 0 ? 1 : 0);
            sweepperiod[1] = (val >> 4) & 7;
            sweepnegate[1] = (byte)((val & 0x08) != 0 ? 1 : 0);
            sweepshift[1]  = val & 7;
            sweepreload[1] = 1;
        }
        // $4006: Pulse 2 timer low
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void apu_4006(byte val)
        {
            _pulsePeriod[1] = (_pulsePeriod[1] & 0x700) | val;
        }
        // $4007: Pulse 2 timer high + length counter (deferred reload)
        static void apu_4007(byte val)
        {
            _pulsePeriod[1] = (_pulsePeriod[1] & 0xFF) | ((val & 7) << 8);
            _pulseTimer[1]  = _pulsePeriod[1];
            _pulseSeq[1]    = 0;
            if (lenCtrEnable[1] != 0)
            { lenCtrReloadValue[1] = lenctrload[(val >> 3) & 0x1F]; lenCtrReloadFlag[1] = true; }
            envelopeStartFlag[1] = 1;
        }
        // $4008: Triangle linear counter
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void apu_4008(byte val)
        {
            apuRegister[0x8] = val;
            linctrreload  = val & 0x7F;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void apu_4009(byte val) { }
        // $400A: Triangle timer low
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void apu_400a(byte val)
        {
            _triPeriod = (_triPeriod & 0x700) | val;
        }
        // $400B: Triangle timer high + length counter (deferred reload)
        static void apu_400b(byte val)
        {
            _triPeriod = (_triPeriod & 0xFF) | ((val & 7) << 8);
            _triTimer  = _triPeriod;
            if (lenCtrEnable[2] != 0)
            { lenCtrReloadValue[2] = lenctrload[(val >> 3) & 0x1F]; lenCtrReloadFlag[2] = true; }
            linctrflag = true;
        }
        // $400C: Noise envelope
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void apu_400c(byte val)
        {
            apuRegister[0xC] = val;
            envConstVolume[3] = (byte)((val & 0x10) != 0 ? 1 : 0);
            envelopeValue[3]  = val & 0x0F;
        }
        // $400E: Noise mode + period
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void apu_400e(byte val)
        {
            _noiseMode      = (val & 0x80) != 0;
            _noisePeriodIdx = val & 0x0F;
        }
        // $400F: Noise length counter (deferred reload)
        static void apu_400f(byte val)
        {
            if (lenCtrEnable[3] != 0)
            { lenCtrReloadValue[3] = lenctrload[(val >> 3) & 0x1F]; lenCtrReloadFlag[3] = true; }
            envelopeStartFlag[3] = 1;
        }
        // $4010: DMC flags + rate
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void apu_4010(byte val)
        {
            dmcirq  = (val & 0x80) != 0;
            if (!dmcirq) { statusdmcint = false; UpdateIRQLine(); }   // disable 時清除 DMC IRQ flag
            dmcloop = (val & 0x40) != 0;
            dmcrate = dmcperiods[val & 0x0F];
        }
        // $4011: DMC DAC 直接寫入
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void apu_4011(byte val)
        {
            dmcvalue = val & 0x7F;
        }
        // $4012: DMC 樣本起始位址
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void apu_4012(byte val)
        {
            dmcstartaddr = 0xC000 + val * 64;
        }
        // $4013: DMC 樣本長度
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void apu_4013(byte val)
        {
            dmcsamplelength = val * 16 + 1;
        }
        // $4015: 聲道啟用/停用
        // TriCNES: deferred status update via APU_DelayedDMC4015 countdown
        static void apu_4015(byte val)
        {
            lenCtrEnable[0]= (byte)((val & 0x01) != 0 ? 1 : 0);
            lenCtrEnable[1] = (byte)((val & 0x02) != 0 ? 1 : 0);
            lenCtrEnable[2] = (byte)((val & 0x04) != 0 ? 1 : 0);
            lenCtrEnable[3] = (byte)((val & 0x08) != 0 ? 1 : 0);
            bool dmcEnable  = (val & 0x10) != 0;

            if (lenCtrEnable[0] == 0) lengthctr[0] = 0;
            if (lenCtrEnable[1] == 0) lengthctr[1] = 0;
            if (lenCtrEnable[2] == 0) lengthctr[2] = 0;
            if (lenCtrEnable[3] == 0) lengthctr[3] = 0;

            // Deferred status (TriCNES: APU_DelayedDMC4015 = PutCycle ? 3 : 4)
            dmcDelayedEnable = dmcEnable;
            dmcStatusDelay = mcApuPutCycle ? 3 : 4;

            if (dmcEnable)
            {
                if (dmcsamplesleft == 0)
                {
                    restartdmc();
                    dmcLoadDmaCountdown = 2;
                }

                // Implicit abort (TriCNES: timer==10&&!PutCycle || timer==8&&PutCycle)
                if ((dmctimer == 10 && !mcApuPutCycle) || (dmctimer == 8 && mcApuPutCycle))
                {
                    dmcImplicitAbortPending = true;
                }
            }
            else
            {
                dmcLoadDmaCountdown = 0;

                // Explicit abort: extend delay at fire boundary
                // TriCNES: (timer==2&&!PutCycle) || (timer==Rate&&PutCycle)
                if ((dmctimer == 2 && !mcApuPutCycle) || (dmctimer == dmcrate && mcApuPutCycle))
                {
                    dmcStatusDelay = mcApuPutCycle ? 5 : 6;
                }
            }
            statusdmcint = false;
            UpdateIRQLine();
        }

    }
}
