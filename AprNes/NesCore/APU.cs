using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AprNes
{
    // =========================================================================
    // NES APU - 使用 Windows WaveOut API (winmm.dll)，無需第三方套件
    // 實作 Pulse1/2、Triangle、Noise、DMC 五個音效聲道
    // =========================================================================
    public partial class NesCore
    {
        // =====================================================================
        // WaveOut API 宣告 (winmm.dll - 專案已引用)
        // =====================================================================
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint   nSamplesPerSec;
            public uint   nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR
        {
            public IntPtr lpData;
            public uint   dwBufferLength;
            public uint   dwBytesRecorded;
            public IntPtr dwUser;
            public uint   dwFlags;
            public uint   dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        // 提升 Windows 計時器精度 → Thread.Sleep(1) 實際睡眠 ~1ms 而非預設 ~15ms
        [DllImport("winmm.dll")] static extern int timeBeginPeriod(int uPeriod);
        [DllImport("winmm.dll")] static extern int timeEndPeriod(int uPeriod);

        [DllImport("winmm.dll")]
        static extern int waveOutOpen(out IntPtr hwo, int devId, ref WAVEFORMATEX fmt,
                                      IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")]
        static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, int sz);
        [DllImport("winmm.dll")]
        static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, int sz);
        [DllImport("winmm.dll")]
        static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, int sz);
        [DllImport("winmm.dll")]
        static extern int waveOutClose(IntPtr hwo);
        [DllImport("winmm.dll")]
        static extern int waveOutReset(IntPtr hwo);

        const int  WAVE_MAPPER    = -1;
        const int  WAVE_FORMAT_PCM = 1;
        const int  CALLBACK_NULL  = 0;
        const uint WHDR_DONE      = 0x00000001u;
        const uint WHDR_INQUEUE   = 0x00000010u;

        // =====================================================================
        // 音效緩衝區設定
        // =====================================================================
        const int    APU_SAMPLE_RATE    = 44100;
        const int    APU_BUFFER_SAMPLES = 735;     // ~1 frame @ 60fps
        const int    APU_NUM_BUFFERS    = 4;       // 4 個緩衝區輪流使用
        const double CPU_FREQ           = 1789773.0; // NTSC CPU 頻率

        static IntPtr   _hWaveOut   = IntPtr.Zero;
        static bool     _audioReady = false;
        static short[][] _audioBufs = new short[APU_NUM_BUFFERS][];
        static GCHandle[] _bufPins  = new GCHandle[APU_NUM_BUFFERS];
        static GCHandle   _hdrPin;
        static WAVEHDR[]  _waveHdrs = new WAVEHDR[APU_NUM_BUFFERS];
        static int        _curBuf   = 0;
        static int        _curPos   = 0;
        static double     _sampleAccum = 0.0;
        static double     _cycPerSample = CPU_FREQ / APU_SAMPLE_RATE; // ~40.58

        // 音效開關 (可由 UI 控制)
        static public bool AudioEnabled = true;

        // =====================================================================
        // 各聲道狀態
        // =====================================================================

        // Pulse 1 & 2 (方波聲道)
        static int[] _pulseTimer  = new int[2]; // 計時器目前值
        static int[] _pulsePeriod = new int[2]; // 11-bit 週期 (從 register 寫入)
        static int[] _pulseSeq    = new int[2]; // duty 序列位置 (0-7)
        static int[] _pulseDuty   = new int[2]; // duty 種類 (0-3)
        static int[] _pulseOut    = new int[2]; // 目前輸出 (0 或 1)

        // Triangle (三角波聲道)
        static int _triTimer  = 0;
        static int _triPeriod = 0;
        static int _triSeq    = 0; // 0-31 序列位置
        static int _triOut    = 0;
        static readonly int[] TRI_SEQ = {
            15,14,13,12,11,10,9,8,7,6,5,4,3,2,1,0,
             0, 1, 2, 3, 4, 5,6,7,8,9,10,11,12,13,14,15
        };

        // Noise (雜音聲道)
        static int    _noiseTimer     = 0;
        static int    _noisePeriodIdx = 0;
        static ushort _noiseLfsr      = 1; // 15-bit LFSR，初始值=1
        static bool   _noiseMode      = false; // false=bit1, true=bit6
        static int    _noiseOut       = 0;

        // 混音查找表
        static int[] SQUARELOOKUP;
        static int[] TNDLOOKUP;

        // DC 消除狀態 (high-pass filter)
        static int _dckiller = 0;

        // =====================================================================
        // 原有 APU 欄位 (保留相容)
        // =====================================================================
        static int apucycle = 0;
        static int[] noiseperiod;
        // Per-step reload values for frame counter (non-uniform, matching real NES NTSC timing)
        // 4-step: steps fire at CPU cycles 7460, 14916, 22374, 29832 from $4017 write (+2 offset)
        // 5-step: steps fire at CPU cycles 7460, 14916, 22374, 29832, 37284
        static int[] frameReload4 = { 7458, 7456, 7458, 7458 };
        static int[] frameReload5 = { 7458, 7456, 7458, 7458, 7452 };
        static int framectrdiv = 7458;
        static bool apuintflag = true, statusdmcint = false, statusframeint = false;
        static int irqAssertCycles = 0; // post-fire: assert IRQ flag for extra cycles after step 3
        static int framectr = 0, ctrmode = 4;
        static byte last4017Val = 0;  // track last value written to $4017 for reset
        static bool[] lenCtrEnable = { true, true, true, true };
        static bool[] lengthClockThisCycle = { false, false, false, false };
        static int[] volume = new int[4];

        // DMC 欄位
        static int[] dmcperiods;
        static int dmcrate = 0x36, dmctimer = 0x36, dmcshiftregister = 0, dmcbuffer = 0,
                   dmcvalue = 0, dmcsamplelength = 1, dmcsamplesleft = 0,
                   dmcstartaddr = 0xc000, dmcaddr = 0xc000, dmcbitsleft = 8;
        static bool dmcsilence = true, dmcirq = false, dmcloop = false, dmcBufferEmpty = true;
        static bool dmcDmaInProgress = false; // prevent re-entrant DMC fill during stolen ticks
        static bool dmcIsLoadDma = false;      // true = Load DMA (after $4015), false = Reload DMA
        static int dmcLoadDmaCountdown = 0;    // Load DMA scheduling delay (3-4 CPU cycles)
        static bool dmcReloadPending = false;  // Reload DMA deferred to next tick (matches hardware behavior)
        static bool oamDmaInProgress = false;  // true during OAM DMA (DMC overlaps with OAM cycles)
        static int oamDmaByteIndex = -1;       // current OAM byte being transferred (0-255, -1 = halt/align)

        // Length counter 欄位
        static int[] lengthctr = { 0, 0, 0, 0 };
        static int[] lengthctr_snapshot = { 0, 0, 0, 0 }; // snapshot for $4015 reads (pre-step values)
        static int[] lenctrload = {
            10, 254, 20, 2, 40, 4, 80, 6,
            160, 8, 60, 10, 14, 12, 26, 14, 12, 16, 24, 18, 48, 20, 96, 22,
            192, 24, 72, 26, 16, 28, 32, 30
        };
        static bool[] lenctrHalt = { true, true, true, true };

        // Linear counter (Triangle)
        static int linearctr  = 0;
        static int linctrreload = 0;
        static bool linctrflag = false;

        // Envelope 欄位
        static int[]  envelopeValue     = { 15, 15, 15, 15 };
        static int[]  envelopeCounter   = { 0, 0, 0, 0 };
        static int[]  envelopePos       = { 0, 0, 0, 0 };
        static bool[] envConstVolume    = { true, true, true, true };
        static bool[] envelopeStartFlag = { false, false, false, false };

        // Sweep 欄位 (Pulse 1 & 2)
        static bool[] sweepenable   = { false, false };
        static bool[] sweepnegate   = { false, false };
        static bool[] sweepsilence  = { false, false };
        static bool[] sweepreload   = { false, false };
        static int[]  sweepperiod   = { 15, 15 };
        static int[]  sweepshift    = { 0, 0 };
        static int[]  sweeppos      = { 0, 0 };

        // Duty 波形查找表
        static int[,] DUTYLOOKUP = new int[,] {
            { 0, 1, 0, 0, 0, 0, 0, 0 }, // 12.5%
            { 0, 1, 1, 0, 0, 0, 0, 0 }, // 25%
            { 0, 1, 1, 1, 1, 0, 0, 0 }, // 50%
            { 1, 0, 0, 1, 1, 1, 1, 1 }  // 75%
        };

        // =====================================================================
        // 混音查找表初始化 (非線性混音，模擬 NES 實際電路)
        // =====================================================================
        static int[] initTndLookup()
        {
            int[] lookup = new int[203];
            for (int i = 0; i < 203; ++i)
                lookup[i] = (int)((163.67 / (24329.0 / (i == 0 ? 0.0001 : i) + 100)) * 49151);
            return lookup;
        }

        static int[] initSquareLookup()
        {
            int[] lookup = new int[31];
            for (int i = 0; i < 31; ++i)
                lookup[i] = (int)((95.52 / (8128.0 / (i == 0 ? 0.0001 : i) + 100)) * 49151);
            return lookup;
        }

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
            if (apuintflag) statusframeint = false;
            framectr   = 0;
            if (ctrmode == 5)
            {
                setenvelope();
                setlinctr();
                setlength();
                setsweep();
                setvolumes();
            }
            framectrdiv = 7449; // match power-on advance (with tick-before-write compensation)
            irqAssertCycles = 0;

            // 清除 IRQ flags
            statusframeint = false;
            statusdmcint = false;

            // 模擬 $4015=$00: 停止所有聲道
            for (int i = 0; i < 4; i++)
            {
                lenCtrEnable[i] = false;
                lengthctr[i] = 0;
            }
            dmcsamplesleft = 0;
            dmcLoadDmaCountdown = 0;
            dmcIsLoadDma = false;
            dmcReloadPending = false;
            oamDmaInProgress = false;

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
            _dckiller = 0;
        }

        // =====================================================================
        // 初始化 APU
        // =====================================================================
        static void initAPU()
        {
            dmcperiods  = new int[] { 428,380,340,320,286,254,226,214,190,160,142,128,106,84,72,54 };
            noiseperiod = new int[] { 4,8,16,32,64,96,128,160,202,254,380,508,762,1016,2034,4068 };

            framectrdiv = 7449; // 7458 + 1(even jitter) - 9(power-on advance) - 1(tick-before-write compensation)
            irqAssertCycles = 0;
            apucycle    = 0;
            framectr = 0; ctrmode = 4;

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

            // Power-on 狀態 (模擬 $4015=$00, $4017=$00)
            for (int i = 0; i < 4; i++)
            {
                lenCtrEnable[i] = false;
                lengthctr[i] = 0;
                volume[i] = 0;
                lenctrHalt[i] = false;
                envelopeValue[i] = 0;
                envelopeCounter[i] = 0;
                envelopePos[i] = 0;
                envConstVolume[i] = false;
                envelopeStartFlag[i] = false;
            }
            for (int i = 0; i < 2; i++)
            {
                sweepenable[i] = false;
                sweepnegate[i] = false;
                sweepsilence[i] = false;
                sweepreload[i] = false;
                sweepperiod[i] = 0;
                sweepshift[i] = 0;
                sweeppos[i] = 0;
            }
            linearctr = 0; linctrreload = 0; linctrflag = false;
            apuintflag = false;      // $4017=$00: IRQ 未禁止
            statusframeint = false;
            statusdmcint = false;

            // DMC 完整重置
            dmcrate = dmcperiods[0]; dmctimer = dmcrate;
            dmcshiftregister = 0; dmcbuffer = 0;
            dmcvalue = 0; dmcsamplelength = 1; dmcsamplesleft = 0;
            dmcstartaddr = 0xC000; dmcaddr = 0xC000; dmcbitsleft = 8;
            dmcsilence = true; dmcirq = false; dmcloop = false; dmcBufferEmpty = true;
            dmcDmaInProgress = false; dmcIsLoadDma = false; dmcLoadDmaCountdown = 0;
            dmcReloadPending = false; oamDmaInProgress = false; oamDmaByteIndex = -1;

            // 初始化查找表
            SQUARELOOKUP = initSquareLookup();
            TNDLOOKUP    = initTndLookup();

            // 啟動音效輸出
            if (AudioEnabled)
                openAudio();
        }

        // =====================================================================
        // 開啟 WaveOut 音效輸出
        // =====================================================================
        static public void openAudio()
        {
            closeAudio();

            WAVEFORMATEX fmt = new WAVEFORMATEX {
                wFormatTag     = WAVE_FORMAT_PCM,
                nChannels      = 1,
                nSamplesPerSec = APU_SAMPLE_RATE,
                wBitsPerSample = 16,
                nBlockAlign    = 2,
                nAvgBytesPerSec = APU_SAMPLE_RATE * 2,
                cbSize         = 0
            };

            if (waveOutOpen(out _hWaveOut, WAVE_MAPPER, ref fmt,
                            IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL) != 0)
            {
                _audioReady = false;
                return;
            }

            // 分配並 Pin 緩衝區
            for (int i = 0; i < APU_NUM_BUFFERS; i++)
            {
                _audioBufs[i] = new short[APU_BUFFER_SAMPLES];
                _bufPins[i]   = GCHandle.Alloc(_audioBufs[i], GCHandleType.Pinned);
            }

            // Pin WAVEHDR 陣列，讓 WaveOut 能持有其位址
            _hdrPin   = GCHandle.Alloc(_waveHdrs, GCHandleType.Pinned);
            int hdrSz = Marshal.SizeOf(typeof(WAVEHDR));

            for (int i = 0; i < APU_NUM_BUFFERS; i++)
            {
                _waveHdrs[i] = new WAVEHDR {
                    lpData        = _bufPins[i].AddrOfPinnedObject(),
                    dwBufferLength = (uint)(APU_BUFFER_SAMPLES * 2),
                    dwFlags       = 0
                };
                IntPtr ptr = Marshal.UnsafeAddrOfPinnedArrayElement(_waveHdrs, i);
                waveOutPrepareHeader(_hWaveOut, ptr, hdrSz);
            }

            _curBuf = 0;
            _curPos = 0;
            _audioReady = true;
        }

        // =====================================================================
        // 關閉 WaveOut 音效輸出 (可從外部呼叫)
        // =====================================================================
        static public void closeAudio()
        {
            if (_hWaveOut == IntPtr.Zero) return;
            _audioReady = false;
            waveOutReset(_hWaveOut);

            int hdrSz = Marshal.SizeOf(typeof(WAVEHDR));
            for (int i = 0; i < APU_NUM_BUFFERS; i++)
            {
                if (_hdrPin.IsAllocated)
                {
                    IntPtr ptr = Marshal.UnsafeAddrOfPinnedArrayElement(_waveHdrs, i);
                    waveOutUnprepareHeader(_hWaveOut, ptr, hdrSz);
                }
                if (_bufPins[i].IsAllocated) _bufPins[i].Free();
            }
            if (_hdrPin.IsAllocated) _hdrPin.Free();

            waveOutClose(_hWaveOut);
            _hWaveOut = IntPtr.Zero;
        }

        // =====================================================================
        // 提交已填滿的緩衝區給 WaveOut 播放
        // =====================================================================
        static void submitBuffer(int idx)
        {
            try
            {
                if (!_audioReady || _hWaveOut == IntPtr.Zero) return;

                int hdrSz = Marshal.SizeOf(typeof(WAVEHDR));
                IntPtr ptr = Marshal.UnsafeAddrOfPinnedArrayElement(_waveHdrs, idx);

                // 等待此緩衝區播放完畢 (最多等 50ms)
                int waited = 0;
                while ((_waveHdrs[idx].dwFlags & WHDR_INQUEUE) != 0)
                {
                    Thread.Sleep(1);
                    if (++waited > 50) return; // 超時就放棄此 frame
                }

                waveOutUnprepareHeader(_hWaveOut, ptr, hdrSz);
                _waveHdrs[idx].dwFlags = 0;
                waveOutPrepareHeader(_hWaveOut, ptr, hdrSz);
                waveOutWrite(_hWaveOut, ptr, hdrSz);
            }
            catch (Exception ex) { }
        }

        // =====================================================================
        // APU Step — 每個 CPU cycle 呼叫一次
        // =====================================================================
        static void apu_step()
        {
            apucycle++;
            lengthClockThisCycle[0] = lengthClockThisCycle[1] =
            lengthClockThisCycle[2] = lengthClockThisCycle[3] = false;

            // Snapshot lengthctr for $4015 reads (pre-step values, compensates tick-before-read)
            lengthctr_snapshot[0] = lengthctr[0];
            lengthctr_snapshot[1] = lengthctr[1];
            lengthctr_snapshot[2] = lengthctr[2];
            lengthctr_snapshot[3] = lengthctr[3];

            // Mode 0: IRQ post-fire (1 cycle after step 3)
            if (irqAssertCycles > 0 && !apuintflag)
            {
                statusframeint = true;
                --irqAssertCycles;
            }

            // Frame Counter：non-uniform step intervals matching real NES (~240Hz)
            if (--framectrdiv <= 0)
            {
                clockframecounter(); // increments framectr
                framectrdiv = (ctrmode == 4) ? frameReload4[framectr] : frameReload5[framectr];
            }

            // Pulse & Noise 計時器：每 2 個 CPU cycles 計數一次 (APU clock)
            if ((apucycle & 1) == 0)
            {
                // Pulse 1
                if (--_pulseTimer[0] < 0)
                {
                    _pulseTimer[0] = _pulsePeriod[0];
                    _pulseSeq[0]   = (_pulseSeq[0] + 1) & 7;
                }
                _pulseOut[0] = (_pulsePeriod[0] >= 8 && lengthctr[0] > 0 && !sweepsilence[0])
                    ? DUTYLOOKUP[_pulseDuty[0], _pulseSeq[0]] : 0;

                // Pulse 2
                if (--_pulseTimer[1] < 0)
                {
                    _pulseTimer[1] = _pulsePeriod[1];
                    _pulseSeq[1]   = (_pulseSeq[1] + 1) & 7;
                }
                _pulseOut[1] = (_pulsePeriod[1] >= 8 && lengthctr[1] > 0 && !sweepsilence[1])
                    ? DUTYLOOKUP[_pulseDuty[1], _pulseSeq[1]] : 0;

                // Noise
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

            // Triangle 計時器：每個 CPU cycle 計數一次
            if (--_triTimer < 0)
            {
                _triTimer = _triPeriod;
                if (linearctr > 0 && lengthctr[2] > 0 && _triPeriod >= 2)
                    _triSeq = (_triSeq + 1) & 31;
            }
            _triOut = (linearctr > 0 && lengthctr[2] > 0 && _triPeriod >= 2)
                ? TRI_SEQ[_triSeq] : 0;

            // DMC
            clockdmc();

            // 生成音效樣本
            _sampleAccum += 1.0;
            if (_sampleAccum >= _cycPerSample)
            {
                _sampleAccum -= _cycPerSample;
                generateSample();
            }
        }

        // =====================================================================
        // 混音並寫入緩衝區
        // =====================================================================
        static void generateSample()
        {
            if (!_audioReady || !AudioEnabled) return;

            // 更新 volume (由 frame counter 在 setvolumes() 控制)
            // Pulse 混音 (非線性查找表)
            int sqIdx = volume[0] * _pulseOut[0] + volume[1] * _pulseOut[1];
            if (sqIdx >= SQUARELOOKUP.Length) sqIdx = SQUARELOOKUP.Length - 1;

            // TND 混音
            int tndIdx = 3 * _triOut + 2 * volume[3] * _noiseOut + dmcvalue;
            if (tndIdx >= TNDLOOKUP.Length) tndIdx = TNDLOOKUP.Length - 1;

            int mixed = SQUARELOOKUP[sqIdx] + TNDLOOKUP[tndIdx]; // 0..~98302

            // High-pass filter 消除 DC 偏移
            mixed += _dckiller;
            _dckiller -= mixed >> 8;
            _dckiller += (mixed > 0 ? -1 : 1);

            // 縮放至 16-bit signed (-32768..32767)
            int clamped = mixed;
            if (clamped >  32767) clamped =  32767;
            if (clamped < -32768) clamped = -32768;

            _audioBufs[_curBuf][_curPos++] = (short)clamped;

            if (_curPos >= APU_BUFFER_SAMPLES)
            {
                _curPos = 0;
                submitBuffer(_curBuf);
                _curBuf = (_curBuf + 1) % APU_NUM_BUFFERS;
            }
        }

        // =====================================================================
        // Frame Counter — 驅動 Envelope、Length Counter、Sweep (~240Hz)
        // =====================================================================
        static void clockframecounter()
        {
            if ((ctrmode == 4) || (ctrmode == 5 && framectr != 3))
            {
                setenvelope();
                setlinctr();
            }
            if ((ctrmode == 4 && (framectr == 1 || framectr == 3)) ||
                (ctrmode == 5 && (framectr == 1 || framectr == 4)))
            {
                setlength();
                setsweep();
            }
            if (!apuintflag && framectr == 3 && ctrmode == 4)
            {
                statusframeint = true;
                irqAssertCycles = 2; // post-fire: assert flag 2 more cycles after step 3 (fire + 2 post = 3 total)
            }

            ++framectr;
            framectr %= ctrmode;
            setvolumes();
        }

        static void setvolumes()
        {
            volume[0] = ((lengthctr[0] <= 0 || sweepsilence[0]) ? 0
                : (envConstVolume[0] ? envelopeValue[0] : envelopeCounter[0]));
            volume[1] = ((lengthctr[1] <= 0 || sweepsilence[1]) ? 0
                : (envConstVolume[1] ? envelopeValue[1] : envelopeCounter[1]));
            volume[3] = (lengthctr[3] <= 0 ? 0
                : (envConstVolume[3] ? envelopeValue[3] : envelopeCounter[3]));
        }

        static void setlength()
        {
            for (int i = 0; i < 4; ++i)
            {
                if (!lenctrHalt[i] && lengthctr[i] > 0)
                {
                    --lengthctr[i];
                    lengthClockThisCycle[i] = true;
                    if (lengthctr[i] == 0) setvolumes();
                }
            }
        }

        static void setlinctr()
        {
            if (linctrflag)
                linearctr = linctrreload;
            else if (linearctr > 0)
                --linearctr;
            if (!lenctrHalt[2])
                linctrflag = false;
        }

        static void setenvelope()
        {
            for (int i = 0; i < 4; ++i)
            {
                if (envelopeStartFlag[i])
                {
                    envelopeStartFlag[i] = false;
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
                    else if (lenctrHalt[i] && envelopeCounter[i] <= 0)
                        envelopeCounter[i] = 15;
                }
            }
        }

        static void setsweep()
        {
            for (int i = 0; i < 2; ++i)
            {
                sweepsilence[i] = false;
                if (sweepreload[i])
                {
                    sweepreload[i] = false;
                    sweeppos[i]    = sweepperiod[i];
                }
                ++sweeppos[i];
                int rawperiod     = _pulsePeriod[i]; // 使用正確的週期值
                int shiftedperiod = rawperiod >> sweepshift[i];
                if (sweepnegate[i])
                    shiftedperiod = -shiftedperiod + i; // channel 2 加 1
                shiftedperiod += rawperiod;

                if (rawperiod < 8 || shiftedperiod > 0x7ff)
                    sweepsilence[i] = true;
                else if (sweepenable[i] && sweepshift[i] != 0 && lengthctr[i] > 0
                         && sweeppos[i] > sweepperiod[i])
                {
                    sweeppos[i]      = 0;
                    _pulsePeriod[i]  = shiftedperiod;
                }
            }
        }

        // =====================================================================
        // DMC (Delta Modulation Channel)
        // =====================================================================
        static void clockdmc()
        {
            if (--dmctimer <= 0)
            {
                dmctimer = dmcrate; // reload with current period

                // NES hardware order: output → shift → decrement → check zero
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

            // Don't trigger new DMA during active DMA stolen ticks
            if (dmcDmaInProgress) return;

            // Handle Load DMA countdown (scheduled DMA from $4015 write)
            if (dmcLoadDmaCountdown > 0)
            {
                --dmcLoadDmaCountdown;
                if (dmcLoadDmaCountdown == 0 && dmcBufferEmpty && dmcsamplesleft > 0)
                    dmcfillbuffer();
                return;
            }

            // Reload DMA: buffer emptied by output unit → immediate fill
            if (dmcBufferEmpty && dmcsamplesleft > 0)
                dmcfillbuffer();
        }

        static void dmcfillbuffer()
        {
            if (dmcDmaInProgress) return; // prevent re-entrant fill during stolen ticks
            if (dmcsamplesleft > 0)
            {
                dmcDmaInProgress = true;
                bool isLoad = dmcIsLoadDma;
                dmcIsLoadDma = false;

                if (oamDmaInProgress)
                {
                    // DMC during OAM DMA — cycle cost depends on position within OAM DMA.
                    // DMC halt/dummy/alignment overlap with remaining OAM cycles (free).
                    // The extra cycles are what remains after OAM can no longer absorb them.
                    //
                    // Normal middle case (byte 0-253 on write, 0-253 on read): 2 extra
                    //   DMC get (1) + OAM realignment (1) = 2
                    //
                    // Second-to-last put (byte 254, write cycle): 1 extra
                    //   OAM's remaining read[255]+write[255] absorb halt+dummy, DMC get = 1
                    //   No realignment needed because OAM ends right after DMC get.
                    //
                    // Last put (byte 255, write cycle): 3 extra
                    //   OAM is done, DMC still needs dummy+alignment+get = 3
                    //
                    // Read of byte 254: 1 extra (same as write[254] — 2 OAM cycles remain)
                    // Read of byte 255: 3 extra (same as write[255] — only 1 OAM cycle remains)

                    int stolenCycles;
                    if (oamDmaByteIndex >= 255)
                        stolenCycles = 3; // last byte (read or write): 3 extra
                    else if (oamDmaByteIndex == 254)
                        stolenCycles = 1; // second-to-last byte (read or write): 1 extra
                    else
                        stolenCycles = 2; // normal middle case: 2 extra

                    for (int sc = 0; sc < stolenCycles; sc++)
                    {
                        if (sc == stolenCycles - 1)
                        {
                            // Last stolen cycle is the actual DMA read
                            dmc_stolen_tick();
                            dmcbuffer = mem_read_fun[(ushort)dmcaddr]((ushort)dmcaddr);
                            cpubus = (byte)dmcbuffer;
                        }
                        else
                        {
                            dmc_stolen_tick(); // dummy or alignment cycle
                        }
                    }
                }
                else
                {
                    // Standalone DMC DMA cycle stealing (NESdev Wiki "DMA" reference):
                    //
                    // Load DMA (from $4015 write): scheduled to halt on GET cycle
                    //   Normal (CPU reading): halt(GET) + dummy(PUT) + get(GET) = 3 cycles
                    //   Delayed (CPU writing): halt(PUT) + dummy(GET) + align(PUT) + get(GET) = 4 cycles
                    //
                    // Reload DMA (buffer emptied by output unit): scheduled to halt on PUT cycle
                    //   Normal (CPU reading): halt(PUT) + dummy(GET) + align(PUT) + get(GET) = 4 cycles
                    //   Delayed (CPU writing): halt(GET) + dummy(PUT) + get(GET) = 3 cycles
                    //
                    int haltCycles;
                    if (isLoad)
                        haltCycles = cpuBusIsWrite ? 3 : 2; // Load: 4 when write-delayed, 3 normally
                    else
                        haltCycles = cpuBusIsWrite ? 2 : 3; // Reload: 3 when write-delayed, 4 normally

                    for (int i = 0; i < haltCycles; i++)
                    {
                        dmc_stolen_tick();

                        // Phantom reads: CPU repeats last read on all no-op DMA cycles
                        if (!cpuBusIsWrite)
                        {
                            // DMA phantom reads bypass $2007 read cooldown
                            // (each DMA cycle is a separate bus transaction with full side effects)
                            ppu2007ReadCooldown = 0;

                            // Joypad $4016/$4017: /OE contiguity rule (NES-001)
                            // Controllers see 1 read per contiguous set → only halt cycle
                            if (cpuBusAddr == 0x4016 || cpuBusAddr == 0x4017)
                            {
                                if (i == 0) // halt cycle only
                                    mem_read_fun[cpuBusAddr](cpuBusAddr);
                            }
                            else
                            {
                                // All other registers ($2007, $2002, $4015, etc.):
                                // every no-op cycle triggers full side effects
                                mem_read_fun[cpuBusAddr](cpuBusAddr);
                            }
                        }
                    }

                    // DMA read cycle — fetch actual sample from PRG ROM
                    dmc_stolen_tick();
                    dmcbuffer = mem_read_fun[(ushort)dmcaddr]((ushort)dmcaddr);
                    cpubus = (byte)dmcbuffer;
                }

                dmcBufferEmpty = false;
                dmcaddr++;
                if (dmcaddr > 0xffff) dmcaddr = 0x8000;
                --dmcsamplesleft;
                if (dmcsamplesleft == 0)
                {
                    if (dmcloop)
                        restartdmc();
                    else if (dmcirq && !statusdmcint)
                        statusdmcint = true;
                }
                dmcDmaInProgress = false;
            }
            else
            {
                dmcsilence = true;
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
            if (lengthctr_snapshot[0] > 0) status |= 0x01;
            if (lengthctr_snapshot[1] > 0) status |= 0x02;
            if (lengthctr_snapshot[2] > 0) status |= 0x04;
            if (lengthctr_snapshot[3] > 0) status |= 0x08;
            if (dmcsamplesleft > 0) status |= 0x10;
            if (statusframeint)     status |= 0x40;
            if (statusdmcint)       status |= 0x80;
            statusframeint = false;
            return status;
        }

        // =====================================================================
        // APU Register 寫入處理
        // =====================================================================

        // $4000: Pulse 1 duty/envelope
        static void apu_4000(byte val)
        {
            _pulseDuty[0]       = (val >> 6) & 3;
            lenctrHalt[0]       = (val & 0x20) != 0;
            envConstVolume[0]   = (val & 0x10) != 0;
            envelopeValue[0]    = val & 0x0F;
        }
        // $4001: Pulse 1 sweep
        static void apu_4001(byte val)
        {
            sweepenable[0] = (val & 0x80) != 0;
            sweepperiod[0] = (val >> 4) & 7;
            sweepnegate[0] = (val & 0x08) != 0;
            sweepshift[0]  = val & 7;
            sweepreload[0] = true;
        }
        // $4002: Pulse 1 timer low
        static void apu_4002(byte val)
        {
            _pulsePeriod[0] = (_pulsePeriod[0] & 0x700) | val;
        }
        // $4003: Pulse 1 timer high + length counter
        static void apu_4003(byte val)
        {
            _pulsePeriod[0] = (_pulsePeriod[0] & 0xFF) | ((val & 7) << 8);
            _pulseTimer[0]  = _pulsePeriod[0];
            _pulseSeq[0]    = 0;
            if (lenCtrEnable[0] && !(lengthClockThisCycle[0] && lengthctr[0] > 0))
                lengthctr[0] = lenctrload[(val >> 3) & 0x1F];
            envelopeStartFlag[0] = true;
        }
        // $4004: Pulse 2 duty/envelope
        static void apu_4004(byte val)
        {
            _pulseDuty[1]     = (val >> 6) & 3;
            lenctrHalt[1]     = (val & 0x20) != 0;
            envConstVolume[1] = (val & 0x10) != 0;
            envelopeValue[1]  = val & 0x0F;
        }
        // $4005: Pulse 2 sweep
        static void apu_4005(byte val)
        {
            sweepenable[1] = (val & 0x80) != 0;
            sweepperiod[1] = (val >> 4) & 7;
            sweepnegate[1] = (val & 0x08) != 0;
            sweepshift[1]  = val & 7;
            sweepreload[1] = true;
        }
        // $4006: Pulse 2 timer low
        static void apu_4006(byte val)
        {
            _pulsePeriod[1] = (_pulsePeriod[1] & 0x700) | val;
        }
        // $4007: Pulse 2 timer high + length counter
        static void apu_4007(byte val)
        {
            _pulsePeriod[1] = (_pulsePeriod[1] & 0xFF) | ((val & 7) << 8);
            _pulseTimer[1]  = _pulsePeriod[1];
            _pulseSeq[1]    = 0;
            if (lenCtrEnable[1] && !(lengthClockThisCycle[1] && lengthctr[1] > 0))
                lengthctr[1] = lenctrload[(val >> 3) & 0x1F];
            envelopeStartFlag[1] = true;
        }
        // $4008: Triangle linear counter
        static void apu_4008(byte val)
        {
            lenctrHalt[2] = (val & 0x80) != 0;
            linctrreload  = val & 0x7F;
        }
        static void apu_4009(byte val) { }
        // $400A: Triangle timer low
        static void apu_400a(byte val)
        {
            _triPeriod = (_triPeriod & 0x700) | val;
        }
        // $400B: Triangle timer high + length counter
        static void apu_400b(byte val)
        {
            _triPeriod = (_triPeriod & 0xFF) | ((val & 7) << 8);
            _triTimer  = _triPeriod;
            if (lenCtrEnable[2] && !(lengthClockThisCycle[2] && lengthctr[2] > 0))
                lengthctr[2] = lenctrload[(val >> 3) & 0x1F];
            linctrflag = true;
        }
        // $400C: Noise envelope
        static void apu_400c(byte val)
        {
            lenctrHalt[3]     = (val & 0x20) != 0;
            envConstVolume[3] = (val & 0x10) != 0;
            envelopeValue[3]  = val & 0x0F;
        }
        // $400E: Noise mode + period
        static void apu_400e(byte val)
        {
            _noiseMode      = (val & 0x80) != 0;
            _noisePeriodIdx = val & 0x0F;
        }
        // $400F: Noise length counter
        static void apu_400f(byte val)
        {
            if (lenCtrEnable[3] && !(lengthClockThisCycle[3] && lengthctr[3] > 0))
                lengthctr[3] = lenctrload[(val >> 3) & 0x1F];
            envelopeStartFlag[3] = true;
        }
        // $4010: DMC flags + rate
        static void apu_4010(byte val)
        {
            dmcirq  = (val & 0x80) != 0;
            if (!dmcirq) statusdmcint = false;   // disable 時清除 DMC IRQ flag
            dmcloop = (val & 0x40) != 0;
            dmcrate = dmcperiods[val & 0x0F];
        }
        // $4011: DMC DAC 直接寫入
        static void apu_4011(byte val)
        {
            dmcvalue = val & 0x7F;
        }
        // $4012: DMC 樣本起始位址
        static void apu_4012(byte val)
        {
            dmcstartaddr = 0xC000 + val * 64;
        }
        // $4013: DMC 樣本長度
        static void apu_4013(byte val)
        {
            dmcsamplelength = val * 16 + 1;
        }
        // $4015: 聲道啟用/停用
        static void apu_4015(byte val)
        {
            lenCtrEnable[0] = (val & 0x01) != 0;
            lenCtrEnable[1] = (val & 0x02) != 0;
            lenCtrEnable[2] = (val & 0x04) != 0;
            lenCtrEnable[3] = (val & 0x08) != 0;
            bool dmcEnable  = (val & 0x10) != 0;

            if (!lenCtrEnable[0]) lengthctr[0] = 0;
            if (!lenCtrEnable[1]) lengthctr[1] = 0;
            if (!lenCtrEnable[2]) lengthctr[2] = 0;
            if (!lenCtrEnable[3]) lengthctr[3] = 0;

            if (dmcEnable)
            {
                if (dmcsamplesleft == 0)
                {
                    restartdmc();
                    if (dmcBufferEmpty)
                    {
                        dmcIsLoadDma = true;
                        dmcLoadDmaCountdown = 3;
                    }
                }
            }
            else
            {
                dmcsamplesleft = 0;
                dmcLoadDmaCountdown = 0;
                dmcIsLoadDma = false;
                dmcReloadPending = false;
            }
            statusdmcint = false;
        }
    }
}
