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

        // Bresenham-style integer accumulator (eliminates FPU in hot path).
        // Emit a sample whenever _sampleAccum >= _cpuFreqInt, then subtract.
        // Increment per APU cycle is APU_SAMPLE_RATE; threshold is the CPU frequency.
        static int _sampleAccum = 0;
        static int _cpuFreqInt  = 1789773; // recalculated in initAPU from cpuFreq

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
        static public int* expansionChannels;   // raw output per channel (unmanaged, 8 ints)

        // 每晶片增益 — 匹配原有 mapperExpansionAudio 乘數
        // 讓 per-channel × gain 加總後落在 NES APU 混音範圍 (~0-98302)
        // Mode 2 再透過 ÷98302 正規化至 0-1.0 與 NES channel 對齊
        // N163: mapper 端已除以 (numCh+1)，所以增益固定 500
        public const int DefaultChipGainCount = 7;
        static float* DefaultChipGain;

        // ── Per-channel 音量 (Mode 2 per-channel, Mode 0/1 per-chip average) ──
        // [0]=Pulse1, [1]=Pulse2, [2]=Triangle, [3]=Noise, [4]=DMC
        // [5..12]=Expansion ch0~ch7 (VRC6: P1/P2/Saw, N163: ch0~ch7, 5B: A/B/C, etc.)
        // 範圍 0~100, 0=靜音, 100=該聲道最大
        public const int ChannelCount = 13;
        public static int* ChannelVolume;
        // Per-channel enable/disable (CheckBox mute, byte 0/1 for unmanaged)
        // [0]=Pulse1, [1]=Pulse2, [2]=Tri, [3]=Noise, [4]=DMC, [5..12]=Exp ch0~7
        public static byte* ChannelEnabled;
        // Bitmask for main 5 channels (bit0=Pulse1..bit4=DMC) — avoids array bounds check
        static public int ChannelEnableMask = 0x1F;

        // One-shot allocation of unmanaged audio arrays. Process-lifetime.
        static NesCore()
        {
            DefaultChipGain = (float*)NesCore.AllocUnmanaged(sizeof(float) * DefaultChipGainCount);
            DefaultChipGain[0] = 0f;    // None
            DefaultChipGain[1] = 740f;  // VRC6:      max≈45140 (≈APU range 1/2)
            DefaultChipGain[2] = 3f;    // VRC7:      OPLL raw ±12285, ×3 → max≈36855
            DefaultChipGain[3] = 500f;  // Namco163:  mapper 已 ÷(numCh+1), ×500 → max≈60000
            DefaultChipGain[4] = 120f;  // Sunsoft5B: 原 sum×120, max≈63720
            DefaultChipGain[5] = 43f;   // MMC5 (future)
            DefaultChipGain[6] = 20f;   // FDS  (future)

            ChannelVolume = (int*)NesCore.AllocUnmanaged(sizeof(int) * ChannelCount);
            for (int i = 0; i < ChannelCount; i++) ChannelVolume[i] = 70;

            ChannelEnabled = (byte*)NesCore.AllocUnmanaged(ChannelCount);
            for (int i = 0; i < ChannelCount; i++) ChannelEnabled[i] = 1;

            ntBankPtrs     = (byte**)NesCore.AllocUnmanaged(sizeof(byte*) * 4);
            ntBankWritable = (byte*)NesCore.AllocUnmanaged(4);
            for (int i = 0; i < 4; i++) { ntBankPtrs[i] = null; ntBankWritable[i] = 0; }
        }

        /// <summary>Call after modifying ChannelEnabled[0..4] to sync bitmask.</summary>
        static public void SyncChannelEnableMask()
        {
            ChannelEnableMask = (ChannelEnabled[0] != 0 ? 1 : 0) | (ChannelEnabled[1] != 0 ? 2 : 0) |
                (ChannelEnabled[2] != 0 ? 4 : 0) | (ChannelEnabled[3] != 0 ? 8 : 0) | (ChannelEnabled[4] != 0 ? 16 : 0);
        }

        // Mode 0/1 擴展音效增益 (從 ChannelVolume[5..] 平均值預算, 由 AudioPlus 更新)
        static public float ap_mode01ExpGain = 0f;

        // =====================================================================
        // 原有 APU 欄位 (保留相容)
        // =====================================================================
        static int apucycle = 0;
        static int* noiseperiod;
        // Frame counter — count-up model with region-dependent thresholds
        // Counter increments every CPU cycle; events fire at threshold positions.
        // NTSC: 7457/14913/22371/29828-29830 (4-step), +37281/37282 (5-step)
        // PAL:  8313/16627/24939/33251-33253 (4-step), +41565/41566 (5-step)
        // Flattened frame counter thresholds (no array bounds check in hot path)
        static int fc4_0, fc4_1, fc4_2, fc4_3, fc4_4, fc4_5;
        static int fc5_0, fc5_1, fc5_2, fc5_3, fc5_4, fc5_5;
        static ushort apuFrameCounter = 0;        // count-up counter
        static byte apuFrameCounterReset = 0xFF;  // TriCNES: APU_FrameCounterReset (0xFF=inactive, 0-4=countdown)
        static bool apuQuarterFrame = false;       // TriCNES: APU_QuarterFrameClock
        static bool apuHalfFrame = false;          // TriCNES: APU_HalfFrameClock
        static int ctrmode = 4;                    // 4=4-step, 5=5-step
        static bool apuintflag = true, statusdmcint = false, statusframeint = false;
        static bool clearingFrameInterrupt = false; // TriCNES: Clearing_APU_FrameInterrupt (deferred from $4015 read)
        static byte last4017Val = 0;
        static byte* lenCtrEnable;
        static int* volume;

        // DMC 欄位
        static int* dmcperiods;
        static int dmcrate = 0x36, dmctimer = 0x36, dmcshiftregister = 0, dmcbuffer = 0,
                   dmcvalue = 0, dmcsamplelength = 1, dmcsamplesleft = 0,
                   dmcstartaddr = 0xc000, dmcaddr = 0xc000, dmcbitsleft = 8;
        static bool dmcsilence = true, dmcirq = false, dmcloop = false;
        static int dmcLoadDmaCountdown = 0;    // Load DMA scheduling delay (2-3 APU cycles)
        static int dmcStatusDelay = 0;         // Deferred $4015 status update countdown (TriCNES: APU_DelayedDMC4015)
        static bool dmcDelayedEnable = false;  // Pending DMC enable/disable value (TriCNES: APU_Status_DelayedDMC)
        static int dmcDmaCooldown = 0;         // TriCNES: CannotRunDMCDMARightNow (blocks new DMA for 2 cycles after completion)
        static bool dmcImplicitAbortPending = false;  // TriCNES: APU_SetImplicitAbortDMC4015
        static bool dmcImplicitAbortActive = false;   // TriCNES: APU_ImplicitAbortDMC4015
        static bool dmcStatusEnabled = false;         // TriCNES: APU_Status_DMC — per-cycle DMA gate

        // Length counter — TriCNES deferred reload flag model
        static int* lengthctr;
        static int* lenctrload;         // LUT: 32-entry length counter load table
        static bool lenCtrReloadFlag0, lenCtrReloadFlag1, lenCtrReloadFlag2, lenCtrReloadFlag3;
        static int  lenCtrReloadValue0, lenCtrReloadValue1, lenCtrReloadValue2, lenCtrReloadValue3;
        // Halt read from register every APU cycle (TriCNES model)
        static byte* apuRegister;       // raw $4000-$400F register values (for halt readback)
        // TriCNES: halt flags updated every APU cycle from apuRegister (not just at HalfFrame)
        static bool lenctrHalt0, lenctrHalt1, lenctrHalt2, lenctrHalt3;

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
            // Don't zero counter directly — let deferred countdown handle it (TriCNES model)
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
            _sampleAccum = 0;
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
            // Set audio output dispatch fn based on current AudioMode
            ApuRefreshOutputFn();
            // Allocate pointer arrays (null-check pattern for re-init safety)
            if (_pulseTimer  == null) _pulseTimer  = (int*)NesCore.AllocUnmanaged(sizeof(int) * 2);
            if (_pulsePeriod == null) _pulsePeriod = (int*)NesCore.AllocUnmanaged(sizeof(int) * 2);
            if (_pulseSeq    == null) _pulseSeq    = (int*)NesCore.AllocUnmanaged(sizeof(int) * 2);
            if (_pulseDuty   == null) _pulseDuty   = (int*)NesCore.AllocUnmanaged(sizeof(int) * 2);
            if (_pulseOut    == null) _pulseOut    = (int*)NesCore.AllocUnmanaged(sizeof(int) * 2);
            if (expansionChannels == null) expansionChannels = (int*)NesCore.AllocUnmanaged(sizeof(int) * 8);
            if (volume       == null) volume       = (int*)NesCore.AllocUnmanaged(sizeof(int) * 4);
            if (SQUARELOOKUP == null) SQUARELOOKUP = (int*)NesCore.AllocUnmanaged(sizeof(int) * 31);
            if (TNDLOOKUP    == null) TNDLOOKUP    = (int*)NesCore.AllocUnmanaged(sizeof(int) * 203);
            if (noiseperiod  == null) noiseperiod  = (int*)NesCore.AllocUnmanaged(sizeof(int) * 16);
            if (lengthctr    == null) lengthctr    = (int*)NesCore.AllocUnmanaged(sizeof(int) * 4);
            if (lenctrload   == null) lenctrload   = (int*)NesCore.AllocUnmanaged(sizeof(int) * 32);
            if (apuRegister  == null) apuRegister  = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * 16);
            if (envelopeValue   == null) envelopeValue   = (int*)NesCore.AllocUnmanaged(sizeof(int) * 4);
            if (envelopeCounter == null) envelopeCounter = (int*)NesCore.AllocUnmanaged(sizeof(int) * 4);
            if (envelopePos     == null) envelopePos     = (int*)NesCore.AllocUnmanaged(sizeof(int) * 4);
            if (sweepperiod  == null) sweepperiod  = (int*)NesCore.AllocUnmanaged(sizeof(int) * 2);
            if (sweepshift   == null) sweepshift   = (int*)NesCore.AllocUnmanaged(sizeof(int) * 2);
            if (sweeppos     == null) sweeppos     = (int*)NesCore.AllocUnmanaged(sizeof(int) * 2);
            if (dmcperiods   == null) dmcperiods   = (int*)NesCore.AllocUnmanaged(sizeof(int) * 16);
            if (lenCtrEnable           == null) lenCtrEnable           = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * 4);
            if (envConstVolume         == null) envConstVolume         = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * 4);
            if (envelopeStartFlag      == null) envelopeStartFlag      = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * 4);
            if (sweepenable            == null) sweepenable            = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * 2);
            if (sweepnegate            == null) sweepnegate            = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * 2);
            if (sweepsilence           == null) sweepsilence           = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * 2);
            if (sweepreload            == null) sweepreload            = (byte*)NesCore.AllocUnmanaged(sizeof(byte) * 2);
            if (TRI_SEQ    == null) { TRI_SEQ    = (int*)NesCore.AllocUnmanaged(sizeof(int) * 32);
                int[] tv = { 15,14,13,12,11,10,9,8,7,6,5,4,3,2,1,0, 0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15 };
                for (int i = 0; i < 32; i++) TRI_SEQ[i] = tv[i]; }
            if (DUTYLOOKUP == null) { DUTYLOOKUP = (int*)NesCore.AllocUnmanaged(sizeof(int) * 32);
                int[] dv = { 0,1,0,0,0,0,0,0, 0,1,1,0,0,0,0,0, 0,1,1,1,1,0,0,0, 1,0,0,1,1,1,1,1 };
                for (int i = 0; i < 32; i++) DUTYLOOKUP[i] = dv[i]; }

            // Initialize region-dependent data arrays
            _cpuFreqInt = (int)cpuFreq;

            // Frame counter thresholds (count-up positions) — direct scalar assignment
            if (Region == RegionType.PAL)
            {
                fc4_0=8313; fc4_1=16627; fc4_2=24939; fc4_3=33252; fc4_4=33253; fc4_5=33254;
                fc5_0=8313; fc5_1=16627; fc5_2=24939; fc5_3=33253; fc5_4=41565; fc5_5=41566;
            }
            else // NTSC and Dendy
            {
                fc4_0=7457; fc4_1=14913; fc4_2=22371; fc4_3=29828; fc4_4=29829; fc4_5=29830;
                fc5_0=7457; fc5_1=14913; fc5_2=22371; fc5_3=29829; fc5_4=37281; fc5_5=37282;
            }

            if (Region == RegionType.PAL)
            {
                noiseperiod[0]=4; noiseperiod[1]=8; noiseperiod[2]=14; noiseperiod[3]=30;
                noiseperiod[4]=60; noiseperiod[5]=88; noiseperiod[6]=118; noiseperiod[7]=148;
                noiseperiod[8]=188; noiseperiod[9]=236; noiseperiod[10]=354; noiseperiod[11]=472;
                noiseperiod[12]=708; noiseperiod[13]=944; noiseperiod[14]=1890; noiseperiod[15]=3778;

                dmcperiods[0]=398; dmcperiods[1]=354; dmcperiods[2]=316; dmcperiods[3]=298;
                dmcperiods[4]=276; dmcperiods[5]=236; dmcperiods[6]=210; dmcperiods[7]=198;
                dmcperiods[8]=176; dmcperiods[9]=148; dmcperiods[10]=132; dmcperiods[11]=118;
                dmcperiods[12]=98; dmcperiods[13]=78; dmcperiods[14]=66; dmcperiods[15]=50;
            }
            else // NTSC and Dendy (Dendy uses NTSC APU tables)
            {
                noiseperiod[0]=4; noiseperiod[1]=8; noiseperiod[2]=16; noiseperiod[3]=32;
                noiseperiod[4]=64; noiseperiod[5]=96; noiseperiod[6]=128; noiseperiod[7]=160;
                noiseperiod[8]=202; noiseperiod[9]=254; noiseperiod[10]=380; noiseperiod[11]=508;
                noiseperiod[12]=762; noiseperiod[13]=1016; noiseperiod[14]=2034; noiseperiod[15]=4068;

                dmcperiods[0]=428; dmcperiods[1]=380; dmcperiods[2]=340; dmcperiods[3]=320;
                dmcperiods[4]=286; dmcperiods[5]=254; dmcperiods[6]=226; dmcperiods[7]=214;
                dmcperiods[8]=190; dmcperiods[9]=160; dmcperiods[10]=142; dmcperiods[11]=128;
                dmcperiods[12]=106; dmcperiods[13]=84; dmcperiods[14]=72; dmcperiods[15]=54;
            }

            { int[] _lv = { 10,254,20,2,40,4,80,6,160,8,60,10,14,12,26,14,12,16,24,18,48,20,96,22,192,24,72,26,16,28,32,30 };
              for (int i = 0; i < 32; i++) lenctrload[i] = _lv[i]; }

            for (int i = 0; i < 31; i++)
                SQUARELOOKUP[i] = (int)((95.52 / (8128.0 / (i == 0 ? 0.0001 : i) + 100)) * 49151);
            for (int i = 0; i < 203; i++)
                TNDLOOKUP[i] = (int)((163.67 / (24329.0 / (i == 0 ? 0.0001 : i) + 100)) * 49151);

            // Default bool* arrays
            for (int i = 0; i < 4; i++) { lenCtrEnable[i] = 1; envConstVolume[i] = 1; envelopeStartFlag[i] = 0; }
            lenCtrReloadFlag0 = lenCtrReloadFlag1 = lenCtrReloadFlag2 = lenCtrReloadFlag3 = false;
            lenCtrReloadValue0 = lenCtrReloadValue1 = lenCtrReloadValue2 = lenCtrReloadValue3 = 0;
            for (int i = 0; i < 16; i++) apuRegister[i] = 0;
            for (int i = 0; i < 2; i++) { sweepenable[i] = 0; sweepnegate[i] = 0; sweepsilence[i] = 0; sweepreload[i] = 0; }

            apuQuarterFrame = false;
            apuHalfFrame = false;
            apucycle    = 0;
            ctrmode = 4;
            apuintflag = false;
            // Power-on: counter=0, APU advances naturally during BRK/RESET handler (7 cycles)
            // No apuSoftReset at power-on — only on soft reset via SoftReset()
            apuFrameCounter = 0;
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
            _sampleAccum = 0;
            _dckiller    = 0;
            AudioPlus_Reset();

            // Power-on 狀態 (模擬 $4015=$00, $4017=$00)
            for (int i = 0; i < 4; i++)
            {
                lenCtrEnable[i] = 0;
                lengthctr[i] = 0;
                volume[i] = 0;
                envelopeValue[i] = 0;
                envelopeCounter[i] = 0;
                envelopePos[i] = 0;
                envConstVolume[i] = 0;
                envelopeStartFlag[i] = 0;
            }
            lenCtrReloadFlag0 = lenCtrReloadFlag1 = lenCtrReloadFlag2 = lenCtrReloadFlag3 = false;
            lenCtrReloadValue0 = lenCtrReloadValue1 = lenCtrReloadValue2 = lenCtrReloadValue3 = 0;
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
            clearingFrameInterrupt = false;
            UpdateIRQLine();

            // DMC 完整重置 (TriCNES: APU_ChannelTimer_DMC=1022 at power-on, APUAlignment=0)
            dmcrate = dmcperiods[0]; dmctimer = 1022;
            dmcshiftregister = 0; dmcbuffer = 0;
            dmcvalue = 0; dmcsamplelength = 1; dmcsamplesleft = 0;
            dmcstartaddr = 0xC000; dmcaddr = 0xC000; dmcbitsleft = 8;
            dmcsilence = true; dmcirq = false; dmcloop = false;
            dmcLoadDmaCountdown = 0; dmcStatusDelay = 0; dmcDelayedEnable = false;
            dmcDmaRunning = false; dmcDmaHalt = false;
            dmcDmaCooldown = 0; dmcImplicitAbortPending = false; dmcImplicitAbortActive = false; dmcStatusEnabled = false;
            spriteDmaTransfer = false; spriteDmaOffset = 0;
            dmaOamHalt = false; dmaOamAligned = false; dmaFirstCycleOam = false;
            dmaOamInternalBus = 0; dmaOamAddr = 0;
        }

        // =====================================================================
        // APU Step — TriCNES _EmulateAPU() order:
        //   GET cycle: Pulse/Noise timers, DMC clock, DMC cooldown
        //   PUT cycle: DMC Load DMA countdown
        //   Both:      DMC $4015 delay, Triangle timer, Frame counter, Quarter/Half frame
        // =====================================================================
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void apu_step()
        {
            apucycle++;

            // Controller shift processing (TriCNES: top of _EmulateAPU, before GET/PUT split)
            ProcessControllerShift();

            // ── GET cycle block (TriCNES: !APU_PutCycle) ──
            if (!mcApuPutCycle)
            {
                // Controller strobe reload (TriCNES: GET cycle = transitioning to PUT)
                ProcessControllerStrobe();
                // Pulse & Noise timers — silent channel fast-path
                int p0 = _pulsePeriod[0], lc0 = lengthctr[0];
                if (--_pulseTimer[0] < 0) { _pulseTimer[0] = p0; _pulseSeq[0] = (_pulseSeq[0] + 1) & 7; }
                _pulseOut[0] = (lc0 > 0 && p0 >= 8 && sweepsilence[0] == 0)
                    ? DUTYLOOKUP[_pulseDuty[0] * 8 + _pulseSeq[0]] : 0;

                int p1 = _pulsePeriod[1], lc1 = lengthctr[1];
                if (--_pulseTimer[1] < 0) { _pulseTimer[1] = p1; _pulseSeq[1] = (_pulseSeq[1] + 1) & 7; }
                _pulseOut[1] = (lc1 > 0 && p1 >= 8 && sweepsilence[1] == 0)
                    ? DUTYLOOKUP[_pulseDuty[1] * 8 + _pulseSeq[1]] : 0;

                if (--_noiseTimer < 0)
                {
                    _noiseTimer = noiseperiod[_noisePeriodIdx] >> 1;
                    int fb = (_noiseLfsr ^ (_noiseLfsr >> (_noiseMode ? 6 : 1))) & 1;
                    _noiseLfsr = (ushort)((_noiseLfsr >> 1) | (fb << 14));
                }
                _noiseOut = (lengthctr[3] > 0 && (_noiseLfsr & 1) == 0) ? 1 : 0;

                // DMC clock (timer -2 per GET cycle, output, buffer→shifter, reload DMA)
                clockdmc();

                // DMC cooldown (TriCNES: CannotRunDMCDMARightNow -= 2 per GET)
                if (dmcDmaCooldown > 0) dmcDmaCooldown -= 2;
            }
            else
            {
                // ── PUT cycle block (TriCNES: APU_PutCycle) ──

                // Deferred frame interrupt clear (TriCNES: Clearing_APU_FrameInterrupt)
                if (clearingFrameInterrupt) apuClearFrameIntFn();

                // DMC Load DMA countdown (from $4015 write)
                if (dmcLoadDmaCountdown > 0) apuDmcLoadDmaFn();
            }

            // ── Both cycles ──

            // DMC deferred $4015 status update
            if (dmcStatusDelay > 0) apuDmcStatusFn();

            // Triangle timer (every CPU cycle) — cached triActive condition (used 2x)
            bool triActive = linearctr > 0 && lengthctr[2] > 0 && _triPeriod >= 2;
            if (--_triTimer < 0)
            {
                _triTimer = _triPeriod;
                if (triActive) _triSeq = (_triSeq + 1) & 31;
            }
            _triOut = triActive ? TRI_SEQ[_triSeq] : 0;

            // ── Frame Counter + quarter/half frame processing ──
            // setvolumes() is called inside setlength() and processLenCtrReloadNonHalf() — not per-cycle
            ApuFrameCounterStep();

            // TriCNES: halt flags updated from registers EVERY APU cycle (lines 1139-1142)
            // Not just at HalfFrame — allows mid-frame halt changes to take effect next cycle
            // SWAR: 4 byte loads → 2 ulong loads. apuRegister is a contiguous 16-byte buffer.
            // x64 little-endian: byte 0 in low byte of ulong, byte 4 in bits 32-39, etc.
            ulong rL = *(ulong*)apuRegister;          // bytes 0..7  (pulse0/1 regs)
            ulong rH = *(ulong*)(apuRegister + 8);    // bytes 8..15 (tri/noise/dmc regs)
            lenctrHalt0 = (rL & 0x0000_0000_0000_0020UL) != 0; // byte 0 (pulse0 ctrl) bit 5
            lenctrHalt1 = (rL & 0x0000_0020_0000_0000UL) != 0; // byte 4 (pulse1 ctrl) bit 5
            lenctrHalt2 = (rH & 0x0000_0000_0000_0080UL) != 0; // byte 8 (tri ctrl)    bit 7
            lenctrHalt3 = (rH & 0x0000_0020_0000_0000UL) != 0; // byte C (noise ctrl)  bit 5

            // 生成音效樣本 — dispatched via function pointer set by ApuRefreshOutputFn()
            apuOutputFn();
        }

        // ── Audio output dispatch (function pointer set when AudioMode changes) ──
        static delegate*<void> apuOutputFn = &ApuOutputCatchup;

        public static void ApuRefreshOutputFn()
        {
            apuOutputFn = AudioMode > 0 ? &ApuOutputPushPlus : &ApuOutputCatchup;
        }

        // ── Cold helpers via function pointer (forces no-inline + indirect call) ──
        static delegate*<void> apuClearFrameIntFn = &ApuDoClearFrameInterrupt;
        static delegate*<void> apuDmcLoadDmaFn   = &ApuDoDmcLoadDma;
        static delegate*<void> apuDmcStatusFn    = &ApuDoDmcStatus;

        static void ApuDoClearFrameInterrupt()
        {
            clearingFrameInterrupt = false;
            statusframeint = false;
            irqLineCurrent = false;
            UpdateIRQLine();
        }

        static void ApuDoDmcLoadDma()
        {
            --dmcLoadDmaCountdown;
            if (dmcLoadDmaCountdown == 0 && !dmcDmaRunning)
            {
                dmcDmaRunning = true;
                dmcDmaHalt = true;
                dmcshiftregister = dmcbuffer;
                dmcsilence = false;
            }
        }

        static void ApuDoDmcStatus()
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ApuOutputPushPlus()
        {
            // Authentic / Modern: 每 APU cycle 推入 AudioPlus (需要 per-cycle 精度)
            if (expansionChannelCount > 0)
            {
                float gain = ap_mode01ExpGain;
                int sum = 0;
                for (int i = 0; i < expansionChannelCount; i++)
                {
                    if (ChannelEnabled[5 + i] != 0)
                        sum += (int)(expansionChannels[i] * gain);
                }
                mapperExpansionAudio = sum;
            }
            int mask = ChannelEnableMask;
            AudioPlus_PushApuCycle(
                (mask & 1)  != 0 ? volume[0] * _pulseOut[0] : 0,
                (mask & 2)  != 0 ? volume[1] * _pulseOut[1] : 0,
                (mask & 4)  != 0 ? _triOut : 0,
                (mask & 8)  != 0 ? volume[3] * _noiseOut : 0,
                (mask & 16) != 0 ? dmcvalue : 0,
                mapperExpansionAudio);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ApuOutputCatchup()
        {
            // Pure Digital: catchup — only compute output at sample rate (~40 cycle interval)
            _sampleAccum += APU_SAMPLE_RATE;
            if (_sampleAccum < _cpuFreqInt) return;
            _sampleAccum -= _cpuFreqInt;
            if (expansionChannelCount > 0)
            {
                float gain = ap_mode01ExpGain;
                int sum = 0;
                for (int i = 0; i < expansionChannelCount; i++)
                {
                    if (ChannelEnabled[5 + i] != 0)
                        sum += (int)(expansionChannels[i] * gain);
                }
                mapperExpansionAudio = sum;
            }
            int mask = ChannelEnableMask;
            generateSample(
                (mask & 1)  != 0 ? volume[0] * _pulseOut[0] : 0,
                (mask & 2)  != 0 ? volume[1] * _pulseOut[1] : 0,
                (mask & 4)  != 0 ? _triOut : 0,
                (mask & 8)  != 0 ? volume[3] * _noiseOut : 0,
                (mask & 16) != 0 ? dmcvalue : 0);
        }

        // =====================================================================
        // 混音並送出樣本
        // =====================================================================
        // Pre-computed float constants (eliminate runtime division)
        private const float INV_32767 = 1f / 32767f;
        private const float RF_LEVEL_MUL = 0.05f * INV_32767;
        private const float RF_PHASE_MUL = 0.0001f * INV_32767;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void generateSample(int sq1, int sq2, int tri, int noise, int dmc)
        {
            if (!AudioEnabled) return;

            int sqIdx = sq1 + sq2;
            if (sqIdx > 30) sqIdx = 30;

            int tndIdx = 3 * tri + (noise << 1) + dmc;
            if (tndIdx > 202) tndIdx = 202;

            int mixed = SQUARELOOKUP[sqIdx] + TNDLOOKUP[tndIdx];
            mixed += mapperExpansionAudio;

            mixed += _dckiller;
            _dckiller -= mixed >> 8;
            _dckiller += (mixed > 0 ? -1 : 1);

            int clamped = (mixed * Volume) / 100;
            if (clamped < -32768) clamped = -32768;
            else if (clamped > 32767) clamped = 32767;

            AudioSampleReady?.Invoke((short)clamped, (short)clamped);

            if (AnalogEnabled && AnalogOutput == AnalogOutputMode.RF)
            {
                int absClamped = clamped < 0 ? -clamped : clamped;
                RfAudioLevel = RfAudioLevel * 0.95f + absClamped * RF_LEVEL_MUL;
                RfBuzzPhase += absClamped * RF_PHASE_MUL;
                if (RfBuzzPhase >= 1.0f) RfBuzzPhase -= 1.0f;
            }
        }


        // =====================================================================
        // Frame counter step — extracted from apu_step to reduce IL size.
        // Handles counter reset, threshold comparison, quarter/half frame dispatch.
        // Most cycles don't hit any threshold (branch predictor skips the chain).
        // =====================================================================
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ApuFrameCounterStep()
        {
            if ((apuFrameCounterReset & 0x80) == 0)
            {
                apuFrameCounterReset--;
                if ((apuFrameCounterReset & 0x80) != 0)
                    apuFrameCounter = 0;
            }

            apuFrameCounter++;

            int fc = apuFrameCounter;
            if (ctrmode == 5)
            {
                if      (fc == fc5_0) apuQuarterFrame = true;
                else if (fc == fc5_1) { apuQuarterFrame = true; apuHalfFrame = true; }
                else if (fc == fc5_2) apuQuarterFrame = true;
                else if (fc == fc5_3) { } // skip — early exit avoids fc5_4/fc5_5 comparisons
                else if (fc == fc5_4) { apuQuarterFrame = true; apuHalfFrame = true; }
                else if (fc == fc5_5) apuFrameCounter = 0;
            }
            else
            {
                if      (fc == fc4_0) apuQuarterFrame = true;
                else if (fc == fc4_1) { apuQuarterFrame = true; apuHalfFrame = true; }
                else if (fc == fc4_2) apuQuarterFrame = true;
                else if (fc == fc4_3) statusframeint = true;
                else if (fc == fc4_4)
                {
                    apuQuarterFrame = true; apuHalfFrame = true;
                    statusframeint = true;
                    irqLineCurrent |= !apuintflag;
                }
                else if (fc == fc4_5)
                {
                    statusframeint = !apuintflag;
                    irqLineCurrent |= !apuintflag;
                    apuFrameCounter = 0;
                }
            }

            if (apuQuarterFrame) { setenvelope(); setlinctr(); apuQuarterFrame = false; }
            if (apuHalfFrame) { setlength(); setsweep(); apuHalfFrame = false; }
            else { processLenCtrReloadNonHalf(); }
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
            if (lenCtrReloadFlag0 && lengthctr[0] == 0) lengthctr[0] = lenCtrReloadValue0; else lenCtrReloadFlag0 = false;
            if (lenCtrReloadFlag1 && lengthctr[1] == 0) lengthctr[1] = lenCtrReloadValue1; else lenCtrReloadFlag1 = false;
            if (lenCtrReloadFlag2 && lengthctr[2] == 0) lengthctr[2] = lenCtrReloadValue2; else lenCtrReloadFlag2 = false;
            if (lenCtrReloadFlag3 && lengthctr[3] == 0) lengthctr[3] = lenCtrReloadValue3; else lenCtrReloadFlag3 = false;
            // 2. Status disable ($4015 bit=0 → zero counter)
            for (int i = 0; i < 4; i++)
                if (lenCtrEnable[i] == 0) lengthctr[i] = 0;
            // 3. Decrement (guarded: !halt && !reloadFlag)
            if (lengthctr[0] > 0 && !lenctrHalt0 && !lenCtrReloadFlag0) lengthctr[0]--;
            if (lengthctr[1] > 0 && !lenctrHalt1 && !lenCtrReloadFlag1) lengthctr[1]--;
            if (lengthctr[2] > 0 && !lenctrHalt2 && !lenCtrReloadFlag2) lengthctr[2]--;
            if (lengthctr[3] > 0 && !lenctrHalt3 && !lenCtrReloadFlag3) lengthctr[3]--;
            setvolumes();
        }

        // TriCNES: non-HalfFrame cycle — unconditional reload if flag set, then clear
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void processLenCtrReloadNonHalf()
        {
            if (lenCtrReloadFlag0) { lengthctr[0] = lenCtrReloadValue0; lenCtrReloadFlag0 = false; }
            if (lenCtrReloadFlag1) { lengthctr[1] = lenCtrReloadValue1; lenCtrReloadFlag1 = false; }
            if (lenCtrReloadFlag2) { lengthctr[2] = lenCtrReloadValue2; lenCtrReloadFlag2 = false; }
            if (lenCtrReloadFlag3) { lengthctr[3] = lenCtrReloadValue3; lenCtrReloadFlag3 = false; }
            setvolumes();
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void StepEnv(int i, int reg)
        {
            if (envelopeStartFlag[i] != 0)
            {
                // Start path: reload. pos=value+1 ≥ 1, so next tick-check can't fire.
                envelopeStartFlag[i] = 0;
                envelopePos[i]       = envelopeValue[i] + 1;
                envelopeCounter[i]   = 15;
            }
            else if (--envelopePos[i] <= 0)
            {
                envelopePos[i] = envelopeValue[i] + 1;
                if (envelopeCounter[i] > 0) --envelopeCounter[i];
                // Loop/halt flag: Pulse=$4000/$4004 bit5, Noise=$400C bit5
                else if ((apuRegister[reg] & 0x20) != 0) envelopeCounter[i] = 15;
            }
        }

        static void setenvelope()
        {
            // Channel 2 (triangle) has no envelope in NES hardware — it uses a
            // linear counter instead. Skip it (saves 1/4 of ALU, cosmetic).
            StepEnv(0, 0x0);  // Pulse 1 ($4000)
            StepEnv(1, 0x4);  // Pulse 2 ($4004)
            StepEnv(3, 0xC);  // Noise   ($400C)
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void StepSweep(int i)
        {
            sweepsilence[i] = 0;
            if (sweepreload[i] != 0)
            {
                sweepreload[i] = 0;
                sweeppos[i]    = sweepperiod[i];
            }
            ++sweeppos[i];
            int rawperiod     = _pulsePeriod[i];
            int shiftedperiod = rawperiod >> sweepshift[i];
            // NESdev Wiki: Pulse1 uses 1's complement (-c - 1); Pulse2 uses 2's complement (-c).
            // Unified formula: (-shifted - 1 + i) gives (-shifted-1) for i=0 and (-shifted) for i=1.
            if (sweepnegate[i] != 0)
                shiftedperiod = -shiftedperiod - 1 + i;
            shiftedperiod += rawperiod;

            if (rawperiod < 8 || shiftedperiod > 0x7ff)
                sweepsilence[i] = 1;
            else if (sweepenable[i] != 0 && sweepshift[i] != 0 && lengthctr[i] > 0
                     && sweeppos[i] > sweepperiod[i])
            {
                sweeppos[i]     = 0;
                _pulsePeriod[i] = shiftedperiod;
            }
        }

        static void setsweep()
        {
            StepSweep(0); // Pulse 1
            StepSweep(1); // Pulse 2
        }

        // =====================================================================
        // DMC clock — timer -2 per GET cycle (TriCNES model)
        // Rate table values are in CPU cycles; -2 per GET = -1 per CPU cycle net rate
        // NoInlining: too large for JIT inline anyway, explicit separation helps apu_step
        // =====================================================================
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void clockdmc()
        {
            dmctimer -= 2;
            if (dmctimer > 0) return; // Early-exit: ~96% of calls return here

            dmctimer = dmcrate;

            if (!dmcsilence)
            {
                // Hardware-accurate discard: if +2/-2 would overflow 0~127, keep old value
                // TriCNES model (line 914-923): only apply delta when result stays in range
                int nextValue = dmcvalue + ((dmcshiftregister & 1) << 2) - 2;
                if ((uint)nextValue <= 0x7F) dmcvalue = nextValue;
                dmcshiftregister >>= 1;
            }

            if (--dmcbitsleft <= 0)
            {
                dmcbitsleft = 8;

                // TriCNES model: DMA trigger + shifter load inside bitsRemaining==0
                if (dmcsamplesleft > 0 | dmcImplicitAbortPending)
                {
                    if (!dmcDmaRunning & dmcDmaCooldown != 2)
                    {
                        dmcDmaRunning = true;
                        dmcDmaHalt = true;
                    }
                    // Promote implicit abort
                    dmcImplicitAbortActive |= dmcImplicitAbortPending;
                    dmcImplicitAbortPending = false;

                    dmcshiftregister = dmcbuffer;
                    dmcsilence = false;
                }
                else
                {
                    dmcsilence = true;
                }
            }
        }

        // Cancel or abort DMC DMA — TriCNES per-cycle model
        static void dmcStopTransfer()
        {
            if (dmcDmaRunning)
            {
                // TriCNES: gate condition handles abort (dmcStatusEnabled=false → gate fails)
                // If still in halt phase, cancel immediately
                dmcDmaRunning = false;
                dmcDmaHalt = false;
            }
        }

        // Complete DMC DMA read — update buffer and advance address
        // TriCNES: DMCDMA_Get() always saves byte and advances address,
        // only guards the BytesRemaining decrement to prevent underflow.
        static void dmcSetReadBuffer(byte val)
        {
            dmcbuffer = val;
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
            status |= (byte)(internalBus & 0x20); // bit 5 is open bus (INTERNAL data bus, not external — DMC DMA must not pollute it)
            // TriCNES: deferred frame interrupt clear (processed on next PUT cycle)
            clearingFrameInterrupt = true;
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
            { lenCtrReloadValue0 = lenctrload[(val >> 3) & 0x1F]; lenCtrReloadFlag0 = true; }
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
            { lenCtrReloadValue1 = lenctrload[(val >> 3) & 0x1F]; lenCtrReloadFlag1 = true; }
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
            { lenCtrReloadValue2 = lenctrload[(val >> 3) & 0x1F]; lenCtrReloadFlag2 = true; }
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
            { lenCtrReloadValue3 = lenctrload[(val >> 3) & 0x1F]; lenCtrReloadFlag3 = true; }
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
            setvolumes();

            // Deferred status (TriCNES: APU_DelayedDMC4015 = PutCycle ? 3 : 4)
            dmcDelayedEnable = dmcEnable;
            dmcStatusDelay = mcApuPutCycle ? 3 : 4;

            if (dmcEnable)
            {
                if (dmcsamplesleft == 0)
                {
                    restartdmc();
                    // TriCNES: only start Load DMA if currently silent
                    if (dmcsilence)
                    {
                        dmcLoadDmaCountdown = 2;
                    }
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
