using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AprNes
{
    // =========================================================================
    // NES APU (Speed Core) - 使用 Windows WaveOut API (winmm.dll)
    // 機械轉換自 APU_old_ref.cs，適用於 partial class NesCoreSpeed
    // =========================================================================
    unsafe public partial class NesCoreSpeed
    {
        // =====================================================================
        // WaveOut API 宣告 (winmm.dll) — 重命名以避免與 NesCore 衝突
        // =====================================================================
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX_S
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
        struct WAVEHDR_S
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

        // 提升 Windows 計時器精度
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")] static extern int timeBeginPeriod_S(int uPeriod);
        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]   static extern int timeEndPeriod_S(int uPeriod);

        [DllImport("winmm.dll", EntryPoint = "waveOutOpen")]
        static extern int waveOutOpen_S(out IntPtr hwo, int devId, ref WAVEFORMATEX_S fmt,
                                        IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll", EntryPoint = "waveOutPrepareHeader")]
        static extern int waveOutPrepareHeader_S(IntPtr hwo, IntPtr hdr, int sz);
        [DllImport("winmm.dll", EntryPoint = "waveOutWrite")]
        static extern int waveOutWrite_S(IntPtr hwo, IntPtr hdr, int sz);
        [DllImport("winmm.dll", EntryPoint = "waveOutUnprepareHeader")]
        static extern int waveOutUnprepareHeader_S(IntPtr hwo, IntPtr hdr, int sz);
        [DllImport("winmm.dll", EntryPoint = "waveOutClose")]
        static extern int waveOutClose_S(IntPtr hwo);
        [DllImport("winmm.dll", EntryPoint = "waveOutReset")]
        static extern int waveOutReset_S(IntPtr hwo);

        const int  WAVE_MAPPER_S    = -1;
        const int  WAVE_FORMAT_PCM_S = 1;
        const int  CALLBACK_NULL_S  = 0;
        const uint WHDR_DONE_S      = 0x00000001u;
        const uint WHDR_INQUEUE_S   = 0x00000010u;

        // =====================================================================
        // 音效緩衝區設定
        // =====================================================================
        const int    APU_SAMPLE_RATE_S    = 44100;
        const int    APU_BUFFER_SAMPLES_S = 735;     // ~1 frame @ 60fps
        const int    APU_NUM_BUFFERS_S    = 4;       // 4 個緩衝區輪流使用
        const double CPU_FREQ_S           = 1789773.0; // NTSC CPU 頻率

        static IntPtr    _hWaveOut_S    = IntPtr.Zero;
        static bool      _audioReady_S  = false;
        static short[][] _audioBufs_S   = new short[APU_NUM_BUFFERS_S][];
        static GCHandle[] _bufPins_S    = new GCHandle[APU_NUM_BUFFERS_S];
        static GCHandle   _hdrPin_S;
        static WAVEHDR_S[] _waveHdrs_S  = new WAVEHDR_S[APU_NUM_BUFFERS_S];
        static int         _curBuf_S    = 0;
        static int         _curPos_S    = 0;
        static int         _sampleAccum_S  = 0;   // SP-9: integer accumulator (× APU_SAMPLE_RATE_S)
        static double      _cycPerSample_S = CPU_FREQ_S / APU_SAMPLE_RATE_S; // ~40.58 (kept for reference)

        // 音效開關 (可由 UI 控制)
        static public bool AudioEnabled_S = true;

        // =====================================================================
        // 各聲道狀態
        // =====================================================================

        // Pulse 1 & 2 (方波聲道)
        static int[] _pulseTimer_S  = new int[2];
        static int[] _pulsePeriod_S = new int[2];
        static int[] _pulseSeq_S    = new int[2];
        static int[] _pulseDuty_S   = new int[2];
        static int[] _pulseOut_S    = new int[2];

        // Triangle (三角波聲道)
        static int _triTimer_S  = 0;
        static int _triPeriod_S = 0;
        static int _triSeq_S    = 0;
        static int _triOut_S    = 0;
        static readonly int[] TRI_SEQ_S = {
            15,14,13,12,11,10,9,8,7,6,5,4,3,2,1,0,
             0, 1, 2, 3, 4, 5,6,7,8,9,10,11,12,13,14,15
        };

        // Noise (雜音聲道)
        static int    _noiseTimer_S     = 0;
        static int    _noisePeriodIdx_S = 0;
        static ushort _noiseLfsr_S      = 1;
        static bool   _noiseMode_S      = false;
        static int    _noiseOut_S       = 0;

        // 混音查找表
        static int[] SQUARELOOKUP_S;
        static int[] TNDLOOKUP_S;

        // DC 消除狀態
        static int _dckiller_S = 0;

        // =====================================================================
        // APU 欄位
        // =====================================================================
        static int apucycle_S = 0;
        static int[] noiseperiod_S;
        static int[] frameReload4_S = { 7457, 7456, 7458, 7458 };
        static int[] frameReload5_S = { 7457, 7456, 7458, 7458, 7453 };
        static int framectrdiv_S = 7457;
        static bool apuintflag_S = true, statusdmcint_S = false, statusframeint_S = false;
        static int framectr_S = 0, ctrmode_S = 4;
        static bool[] lenCtrEnable_S = { true, true, true, true };
        static int[] volume_S = new int[4];

        // DMC 欄位
        static int[] dmcperiods_S;
        static int dmcrate_S = 0x36, dmcpos_S = 0, dmcshiftregister_S = 0, dmcbuffer_S = 0,
                   dmcvalue_S = 0, dmcsamplelength_S = 1, dmcsamplesleft_S = 0,
                   dmcstartaddr_S = 0xc000, dmcaddr_S = 0xc000, dmcbitsleft_S = 8;
        static bool dmcsilence_S = true, dmcirq_S = false, dmcloop_S = false, dmcBufferEmpty_S = true;

        // Length counter 欄位
        static int[] lengthctr_S = { 0, 0, 0, 0 };
        static int[] lenctrload_S = {
            10, 254, 20, 2, 40, 4, 80, 6,
            160, 8, 60, 10, 14, 12, 26, 14, 12, 16, 24, 18, 48, 20, 96, 22,
            192, 24, 72, 26, 16, 28, 32, 30
        };
        static bool[] lenctrHalt_S = { true, true, true, true };

        // Linear counter (Triangle)
        static int linearctr_S  = 0;
        static int linctrreload_S = 0;
        static bool linctrflag_S = false;

        // Envelope 欄位
        static int[]  envelopeValue_S     = { 15, 15, 15, 15 };
        static int[]  envelopeCounter_S   = { 0, 0, 0, 0 };
        static int[]  envelopePos_S       = { 0, 0, 0, 0 };
        static bool[] envConstVolume_S    = { true, true, true, true };
        static bool[] envelopeStartFlag_S = { false, false, false, false };

        // Sweep 欄位 (Pulse 1 & 2)
        static bool[] sweepenable_S   = { false, false };
        static bool[] sweepnegate_S   = { false, false };
        static bool[] sweepsilence_S  = { false, false };
        static bool[] sweepreload_S   = { false, false };
        static int[]  sweepperiod_S   = { 15, 15 };
        static int[]  sweepshift_S    = { 0, 0 };
        static int[]  sweeppos_S      = { 0, 0 };

        // Duty 波形查找表
        static int[,] DUTYLOOKUP_S = new int[,] {
            { 0, 1, 0, 0, 0, 0, 0, 0 }, // 12.5%
            { 0, 1, 1, 0, 0, 0, 0, 0 }, // 25%
            { 0, 1, 1, 1, 1, 0, 0, 0 }, // 50%
            { 1, 0, 0, 1, 1, 1, 1, 1 }  // 75%
        };

        // =====================================================================
        // 混音查找表初始化
        // =====================================================================
        static int[] initTndLookup_S()
        {
            int[] lookup = new int[203];
            for (int i = 0; i < 203; ++i)
                lookup[i] = (int)((163.67 / (24329.0 / (i == 0 ? 0.0001 : i) + 100)) * 49151);
            return lookup;
        }

        static int[] initSquareLookup_S()
        {
            int[] lookup = new int[31];
            for (int i = 0; i < 31; ++i)
                lookup[i] = (int)((95.52 / (8128.0 / (i == 0 ? 0.0001 : i) + 100)) * 49151);
            return lookup;
        }

        // =====================================================================
        // APU Soft Reset
        // =====================================================================
        static void apuSoftReset_S()
        {
            framectrdiv_S = 7457;
            framectr_S = 0;
            apucycle_S = 0;

            statusframeint_S = false;
            statusdmcint_S = false;

            for (int i = 0; i < 4; i++)
            {
                lenCtrEnable_S[i] = false;
                lengthctr_S[i] = 0;
            }
            dmcsamplesleft_S = 0;

            _pulseTimer_S[0] = _pulseTimer_S[1] = 0;
            _pulsePeriod_S[0] = _pulsePeriod_S[1] = 0;
            _pulseSeq_S[0] = _pulseSeq_S[1] = 0;
            _pulseDuty_S[0] = _pulseDuty_S[1] = 0;
            _pulseOut_S[0] = _pulseOut_S[1] = 0;
            _triTimer_S = _triPeriod_S = _triSeq_S = _triOut_S = 0;
            _noiseTimer_S = 0; _noisePeriodIdx_S = 0; _noiseLfsr_S = 1;
            _noiseMode_S = false; _noiseOut_S = 0;
            _sampleAccum_S = 0;
            _dckiller_S = 0;
        }

        // =====================================================================
        // 初始化 APU
        // =====================================================================
        static void init_apu_S()
        {
            dmcperiods_S  = new int[] { 428,380,340,320,286,254,226,214,190,160,142,128,106,84,72,54 };
            noiseperiod_S = new int[] { 4,8,16,32,64,96,128,160,202,254,380,508,762,1016,2034,4068 };

            framectrdiv_S = 7457;
            apucycle_S    = 0;
            framectr_S = 0; ctrmode_S = 4;

            _pulseTimer_S[0]  = _pulseTimer_S[1]  = 0;
            _pulsePeriod_S[0] = _pulsePeriod_S[1] = 0;
            _pulseSeq_S[0]    = _pulseSeq_S[1]    = 0;
            _pulseDuty_S[0]   = _pulseDuty_S[1]   = 0;
            _pulseOut_S[0]    = _pulseOut_S[1]     = 0;
            _triTimer_S  = _triPeriod_S = _triSeq_S = _triOut_S = 0;
            _noiseTimer_S = 0; _noisePeriodIdx_S = 0; _noiseLfsr_S = 1;
            _noiseMode_S = false; _noiseOut_S = 0;
            _sampleAccum_S = 0;
            _dckiller_S    = 0;

            // Power-on 狀態
            for (int i = 0; i < 4; i++)
            {
                lenCtrEnable_S[i] = false;
                lengthctr_S[i] = 0;
                volume_S[i] = 0;
                lenctrHalt_S[i] = false;
                envelopeValue_S[i] = 0;
                envelopeCounter_S[i] = 0;
                envelopePos_S[i] = 0;
                envConstVolume_S[i] = false;
                envelopeStartFlag_S[i] = false;
            }
            for (int i = 0; i < 2; i++)
            {
                sweepenable_S[i] = false;
                sweepnegate_S[i] = false;
                sweepsilence_S[i] = false;
                sweepreload_S[i] = false;
                sweepperiod_S[i] = 0;
                sweepshift_S[i] = 0;
                sweeppos_S[i] = 0;
            }
            linearctr_S = 0; linctrreload_S = 0; linctrflag_S = false;
            apuintflag_S = false;
            statusframeint_S = false;
            statusdmcint_S = false;

            // DMC 完整重置
            dmcrate_S = dmcperiods_S[0]; dmcpos_S = 0;
            dmcshiftregister_S = 0; dmcbuffer_S = 0;
            dmcvalue_S = 0; dmcsamplelength_S = 1; dmcsamplesleft_S = 0;
            dmcstartaddr_S = 0xC000; dmcaddr_S = 0xC000; dmcbitsleft_S = 8;
            dmcsilence_S = true; dmcirq_S = false; dmcloop_S = false; dmcBufferEmpty_S = true;

            // 初始化查找表
            SQUARELOOKUP_S = initSquareLookup_S();
            TNDLOOKUP_S    = initTndLookup_S();

            // 啟動音效輸出
            if (AudioEnabled_S)
                openAudio_S();
        }

        // =====================================================================
        // 開啟 WaveOut 音效輸出
        // =====================================================================
        static public void openAudio_S()
        {
            closeAudio_S();

            WAVEFORMATEX_S fmt = new WAVEFORMATEX_S {
                wFormatTag     = WAVE_FORMAT_PCM_S,
                nChannels      = 1,
                nSamplesPerSec = APU_SAMPLE_RATE_S,
                wBitsPerSample = 16,
                nBlockAlign    = 2,
                nAvgBytesPerSec = APU_SAMPLE_RATE_S * 2,
                cbSize         = 0
            };

            if (waveOutOpen_S(out _hWaveOut_S, WAVE_MAPPER_S, ref fmt,
                              IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL_S) != 0)
            {
                _audioReady_S = false;
                return;
            }

            // 分配並 Pin 緩衝區
            for (int i = 0; i < APU_NUM_BUFFERS_S; i++)
            {
                _audioBufs_S[i] = new short[APU_BUFFER_SAMPLES_S];
                _bufPins_S[i]   = GCHandle.Alloc(_audioBufs_S[i], GCHandleType.Pinned);
            }

            // Pin WAVEHDR_S 陣列
            _hdrPin_S = GCHandle.Alloc(_waveHdrs_S, GCHandleType.Pinned);
            int hdrSz = Marshal.SizeOf(typeof(WAVEHDR_S));

            for (int i = 0; i < APU_NUM_BUFFERS_S; i++)
            {
                _waveHdrs_S[i] = new WAVEHDR_S {
                    lpData         = _bufPins_S[i].AddrOfPinnedObject(),
                    dwBufferLength = (uint)(APU_BUFFER_SAMPLES_S * 2),
                    dwFlags        = 0
                };
                IntPtr ptr = Marshal.UnsafeAddrOfPinnedArrayElement(_waveHdrs_S, i);
                waveOutPrepareHeader_S(_hWaveOut_S, ptr, hdrSz);
            }

            _curBuf_S = 0;
            _curPos_S = 0;
            _audioReady_S = true;
        }

        // =====================================================================
        // 關閉 WaveOut 音效輸出
        // =====================================================================
        static public void closeAudio_S()
        {
            if (_hWaveOut_S == IntPtr.Zero) return;
            _audioReady_S = false;
            waveOutReset_S(_hWaveOut_S);

            int hdrSz = Marshal.SizeOf(typeof(WAVEHDR_S));
            for (int i = 0; i < APU_NUM_BUFFERS_S; i++)
            {
                if (_hdrPin_S.IsAllocated)
                {
                    IntPtr ptr = Marshal.UnsafeAddrOfPinnedArrayElement(_waveHdrs_S, i);
                    waveOutUnprepareHeader_S(_hWaveOut_S, ptr, hdrSz);
                }
                if (_bufPins_S[i].IsAllocated) _bufPins_S[i].Free();
            }
            if (_hdrPin_S.IsAllocated) _hdrPin_S.Free();

            waveOutClose_S(_hWaveOut_S);
            _hWaveOut_S = IntPtr.Zero;
        }

        // =====================================================================
        // 提交已填滿的緩衝區給 WaveOut 播放
        // =====================================================================
        static void submitBuffer_S(int idx)
        {
            if (!_audioReady_S || _hWaveOut_S == IntPtr.Zero) return;

            int    hdrSz = Marshal.SizeOf(typeof(WAVEHDR_S));
            IntPtr ptr   = Marshal.UnsafeAddrOfPinnedArrayElement(_waveHdrs_S, idx);

            // 等待此緩衝區播放完畢 (最多等 50ms)
            int waited = 0;
            while ((_waveHdrs_S[idx].dwFlags & WHDR_INQUEUE_S) != 0)
            {
                Thread.Sleep(1);
                if (++waited > 50) return;
            }

            waveOutUnprepareHeader_S(_hWaveOut_S, ptr, hdrSz);
            _waveHdrs_S[idx].dwFlags = 0;
            waveOutPrepareHeader_S(_hWaveOut_S, ptr, hdrSz);
            waveOutWrite_S(_hWaveOut_S, ptr, hdrSz);
        }

        // =====================================================================
        // APU Step — 每個 CPU cycle 呼叫一次 (由 tick_S() 呼叫)
        // =====================================================================
        static void apu_step_S()
        {
            apucycle_S++;

            // SP-8: Skip most APU computation when audio is disabled (saves ~1.79M ops/sec)
            if (!AudioEnabled_S)
            {
                // Keep frame counter for IRQ generation
                if (--framectrdiv_S <= 0)
                {
                    clockframecounter_S();
                    framectrdiv_S = (ctrmode_S == 4) ? frameReload4_S[framectr_S] : frameReload5_S[framectr_S];
                }
                // Keep DMC clock for DMA cycle stealing
                clockdmc_S();
                return;
            }

            // Frame Counter
            if (--framectrdiv_S <= 0)
            {
                clockframecounter_S();
                framectrdiv_S = (ctrmode_S == 4) ? frameReload4_S[framectr_S] : frameReload5_S[framectr_S];
            }

            // Pulse & Noise 計時器：每 2 個 CPU cycles 計數一次
            if ((apucycle_S & 1) == 0)
            {
                // Pulse 1
                if (--_pulseTimer_S[0] < 0)
                {
                    _pulseTimer_S[0] = _pulsePeriod_S[0];
                    _pulseSeq_S[0]   = (_pulseSeq_S[0] + 1) & 7;
                }
                _pulseOut_S[0] = (_pulsePeriod_S[0] >= 8 && lengthctr_S[0] > 0 && !sweepsilence_S[0])
                    ? DUTYLOOKUP_S[_pulseDuty_S[0], _pulseSeq_S[0]] : 0;

                // Pulse 2
                if (--_pulseTimer_S[1] < 0)
                {
                    _pulseTimer_S[1] = _pulsePeriod_S[1];
                    _pulseSeq_S[1]   = (_pulseSeq_S[1] + 1) & 7;
                }
                _pulseOut_S[1] = (_pulsePeriod_S[1] >= 8 && lengthctr_S[1] > 0 && !sweepsilence_S[1])
                    ? DUTYLOOKUP_S[_pulseDuty_S[1], _pulseSeq_S[1]] : 0;

                // Noise
                if (--_noiseTimer_S < 0)
                {
                    _noiseTimer_S = noiseperiod_S[_noisePeriodIdx_S] >> 1;
                    int fb = _noiseMode_S
                        ? ((_noiseLfsr_S & 1) ^ ((_noiseLfsr_S >> 6) & 1))
                        : ((_noiseLfsr_S & 1) ^ ((_noiseLfsr_S >> 1) & 1));
                    _noiseLfsr_S = (ushort)((_noiseLfsr_S >> 1) | (fb << 14));
                }
                _noiseOut_S = (lengthctr_S[3] > 0 && (_noiseLfsr_S & 1) == 0) ? 1 : 0;
            }

            // Triangle 計時器：每個 CPU cycle 計數一次
            if (--_triTimer_S < 0)
            {
                _triTimer_S = _triPeriod_S;
                if (linearctr_S > 0 && lengthctr_S[2] > 0 && _triPeriod_S >= 2)
                    _triSeq_S = (_triSeq_S + 1) & 31;
            }
            _triOut_S = (linearctr_S > 0 && lengthctr_S[2] > 0 && _triPeriod_S >= 2)
                ? TRI_SEQ_S[_triSeq_S] : 0;

            // DMC
            clockdmc_S();

            // 生成音效樣本 (SP-9: integer accumulator avoids FP dependency chain)
            _sampleAccum_S += APU_SAMPLE_RATE_S;
            if (_sampleAccum_S >= (int)CPU_FREQ_S)
            {
                _sampleAccum_S -= (int)CPU_FREQ_S;
                generateSample_S();
            }
        }

        // =====================================================================
        // 混音並寫入緩衝區
        // =====================================================================
        static void generateSample_S()
        {
            if (!_audioReady_S || !AudioEnabled_S) return;

            int sqIdx = volume_S[0] * _pulseOut_S[0] + volume_S[1] * _pulseOut_S[1];
            if (sqIdx >= SQUARELOOKUP_S.Length) sqIdx = SQUARELOOKUP_S.Length - 1;

            int tndIdx = 3 * _triOut_S + 2 * volume_S[3] * _noiseOut_S + dmcvalue_S;
            if (tndIdx >= TNDLOOKUP_S.Length) tndIdx = TNDLOOKUP_S.Length - 1;

            int mixed = SQUARELOOKUP_S[sqIdx] + TNDLOOKUP_S[tndIdx];

            // High-pass filter
            mixed += _dckiller_S;
            _dckiller_S -= mixed >> 8;
            _dckiller_S += (mixed > 0 ? -1 : 1);

            int clamped = mixed;
            if (clamped >  32767) clamped =  32767;
            if (clamped < -32768) clamped = -32768;

            _audioBufs_S[_curBuf_S][_curPos_S++] = (short)clamped;

            if (_curPos_S >= APU_BUFFER_SAMPLES_S)
            {
                _curPos_S = 0;
                submitBuffer_S(_curBuf_S);
                _curBuf_S = (_curBuf_S + 1) % APU_NUM_BUFFERS_S;
            }
        }

        // =====================================================================
        // Frame Counter
        // =====================================================================
        static void clockframecounter_S()
        {
            if ((ctrmode_S == 4) || (ctrmode_S == 5 && framectr_S != 3))
            {
                setenvelope_S();
                setlinctr_S();
            }
            if ((ctrmode_S == 4 && (framectr_S == 1 || framectr_S == 3)) ||
                (ctrmode_S == 5 && (framectr_S == 1 || framectr_S == 4)))
            {
                setlength_S();
                setsweep_S();
            }
            if (!apuintflag_S && framectr_S == 3 && ctrmode_S == 4 && !statusframeint_S)
                statusframeint_S = true;

            ++framectr_S;
            framectr_S %= ctrmode_S;
            setvolumes_S();
        }

        static void setvolumes_S()
        {
            volume_S[0] = ((lengthctr_S[0] <= 0 || sweepsilence_S[0]) ? 0
                : (envConstVolume_S[0] ? envelopeValue_S[0] : envelopeCounter_S[0]));
            volume_S[1] = ((lengthctr_S[1] <= 0 || sweepsilence_S[1]) ? 0
                : (envConstVolume_S[1] ? envelopeValue_S[1] : envelopeCounter_S[1]));
            volume_S[3] = (lengthctr_S[3] <= 0 ? 0
                : (envConstVolume_S[3] ? envelopeValue_S[3] : envelopeCounter_S[3]));
        }

        static void setlength_S()
        {
            for (int i = 0; i < 4; ++i)
            {
                if (!lenctrHalt_S[i] && lengthctr_S[i] > 0)
                {
                    --lengthctr_S[i];
                    if (lengthctr_S[i] == 0) setvolumes_S();
                }
            }
        }

        static void setlinctr_S()
        {
            if (linctrflag_S)
                linearctr_S = linctrreload_S;
            else if (linearctr_S > 0)
                --linearctr_S;
            if (!lenctrHalt_S[2])
                linctrflag_S = false;
        }

        static void setenvelope_S()
        {
            for (int i = 0; i < 4; ++i)
            {
                if (envelopeStartFlag_S[i])
                {
                    envelopeStartFlag_S[i] = false;
                    envelopePos_S[i]       = envelopeValue_S[i] + 1;
                    envelopeCounter_S[i]   = 15;
                }
                else
                {
                    --envelopePos_S[i];
                }
                if (envelopePos_S[i] <= 0)
                {
                    envelopePos_S[i] = envelopeValue_S[i] + 1;
                    if (envelopeCounter_S[i] > 0)
                        --envelopeCounter_S[i];
                    else if (lenctrHalt_S[i] && envelopeCounter_S[i] <= 0)
                        envelopeCounter_S[i] = 15;
                }
            }
        }

        static void setsweep_S()
        {
            for (int i = 0; i < 2; ++i)
            {
                sweepsilence_S[i] = false;
                if (sweepreload_S[i])
                {
                    sweepreload_S[i] = false;
                    sweeppos_S[i]    = sweepperiod_S[i];
                }
                ++sweeppos_S[i];
                int rawperiod     = _pulsePeriod_S[i];
                int shiftedperiod = rawperiod >> sweepshift_S[i];
                if (sweepnegate_S[i])
                    shiftedperiod = -shiftedperiod + i;
                shiftedperiod += rawperiod;

                if (rawperiod < 8 || shiftedperiod > 0x7ff)
                    sweepsilence_S[i] = true;
                else if (sweepenable_S[i] && sweepshift_S[i] != 0 && lengthctr_S[i] > 0
                         && sweeppos_S[i] > sweepperiod_S[i])
                {
                    sweeppos_S[i]      = 0;
                    _pulsePeriod_S[i]  = shiftedperiod;
                }
            }
        }

        // =====================================================================
        // DMC (Delta Modulation Channel)
        // =====================================================================
        static void clockdmc_S()
        {
            if (dmcBufferEmpty_S && dmcsamplesleft_S > 0)
                dmcfillbuffer_S();

            dmcpos_S = (dmcpos_S + 1) % dmcrate_S;
            if (dmcpos_S == 0)
            {
                if (dmcbitsleft_S <= 0)
                {
                    dmcbitsleft_S = 8;
                    if (dmcBufferEmpty_S)
                        dmcsilence_S = true;
                    else
                    {
                        dmcsilence_S       = false;
                        dmcshiftregister_S = dmcbuffer_S;
                        dmcBufferEmpty_S   = true;
                    }
                }
                if (!dmcsilence_S)
                {
                    dmcvalue_S += ((dmcshiftregister_S & 1) != 0) ? 2 : -2;
                    if (dmcvalue_S > 0x7f) dmcvalue_S = 0x7f;
                    if (dmcvalue_S < 0)    dmcvalue_S  = 0;
                    dmcshiftregister_S >>= 1;
                    --dmcbitsleft_S;
                }
            }
        }

        // Speed Core: direct memory read without cycle stealing
        static void dmcfillbuffer_S()
        {
            if (dmcsamplesleft_S > 0)
            {
                dmcbuffer_S    = Mem_r_S((ushort)dmcaddr_S++);
                dmcBufferEmpty_S = false;
                if (dmcaddr_S > 0xffff) dmcaddr_S = 0x8000;
                --dmcsamplesleft_S;
                if (dmcsamplesleft_S == 0)
                {
                    if (dmcloop_S)
                        restartdmc_S();
                    else if (dmcirq_S && !statusdmcint_S)
                        statusdmcint_S = true;
                }
            }
            else
            {
                dmcsilence_S = true;
            }
        }

        static void restartdmc_S()
        {
            dmcaddr_S        = dmcstartaddr_S;
            dmcsamplesleft_S = dmcsamplelength_S;
            dmcsilence_S     = false;
        }

        // =====================================================================
        // 讀取 $4015 狀態暫存器
        // =====================================================================
        static byte apu_r_4015_S()
        {
            byte status = 0;
            if (lengthctr_S[0] > 0) status |= 0x01;
            if (lengthctr_S[1] > 0) status |= 0x02;
            if (lengthctr_S[2] > 0) status |= 0x04;
            if (lengthctr_S[3] > 0) status |= 0x08;
            if (dmcsamplesleft_S > 0) status |= 0x10;
            if (statusframeint_S)     status |= 0x40;
            if (statusdmcint_S)       status |= 0x80;
            statusframeint_S = false;
            return status;
        }

        // =====================================================================
        // APU Register 寫入處理
        // =====================================================================

        // $4000: Pulse 1 duty/envelope
        static void apu_4000_S(byte val)
        {
            _pulseDuty_S[0]       = (val >> 6) & 3;
            lenctrHalt_S[0]       = (val & 0x20) != 0;
            envConstVolume_S[0]   = (val & 0x10) != 0;
            envelopeValue_S[0]    = val & 0x0F;
        }
        // $4001: Pulse 1 sweep
        static void apu_4001_S(byte val)
        {
            sweepenable_S[0] = (val & 0x80) != 0;
            sweepperiod_S[0] = (val >> 4) & 7;
            sweepnegate_S[0] = (val & 0x08) != 0;
            sweepshift_S[0]  = val & 7;
            sweepreload_S[0] = true;
        }
        // $4002: Pulse 1 timer low
        static void apu_4002_S(byte val)
        {
            _pulsePeriod_S[0] = (_pulsePeriod_S[0] & 0x700) | val;
        }
        // $4003: Pulse 1 timer high + length counter
        static void apu_4003_S(byte val)
        {
            _pulsePeriod_S[0] = (_pulsePeriod_S[0] & 0xFF) | ((val & 7) << 8);
            _pulseTimer_S[0]  = _pulsePeriod_S[0];
            _pulseSeq_S[0]    = 0;
            if (lenCtrEnable_S[0])
                lengthctr_S[0] = lenctrload_S[(val >> 3) & 0x1F];
            envelopeStartFlag_S[0] = true;
        }
        // $4004: Pulse 2 duty/envelope
        static void apu_4004_S(byte val)
        {
            _pulseDuty_S[1]     = (val >> 6) & 3;
            lenctrHalt_S[1]     = (val & 0x20) != 0;
            envConstVolume_S[1] = (val & 0x10) != 0;
            envelopeValue_S[1]  = val & 0x0F;
        }
        // $4005: Pulse 2 sweep
        static void apu_4005_S(byte val)
        {
            sweepenable_S[1] = (val & 0x80) != 0;
            sweepperiod_S[1] = (val >> 4) & 7;
            sweepnegate_S[1] = (val & 0x08) != 0;
            sweepshift_S[1]  = val & 7;
            sweepreload_S[1] = true;
        }
        // $4006: Pulse 2 timer low
        static void apu_4006_S(byte val)
        {
            _pulsePeriod_S[1] = (_pulsePeriod_S[1] & 0x700) | val;
        }
        // $4007: Pulse 2 timer high + length counter
        static void apu_4007_S(byte val)
        {
            _pulsePeriod_S[1] = (_pulsePeriod_S[1] & 0xFF) | ((val & 7) << 8);
            _pulseTimer_S[1]  = _pulsePeriod_S[1];
            _pulseSeq_S[1]    = 0;
            if (lenCtrEnable_S[1])
                lengthctr_S[1] = lenctrload_S[(val >> 3) & 0x1F];
            envelopeStartFlag_S[1] = true;
        }
        // $4008: Triangle linear counter
        static void apu_4008_S(byte val)
        {
            lenctrHalt_S[2] = (val & 0x80) != 0;
            linctrreload_S  = val & 0x7F;
        }
        // $400A: Triangle timer low
        static void apu_400a_S(byte val)
        {
            _triPeriod_S = (_triPeriod_S & 0x700) | val;
        }
        // $400B: Triangle timer high + length counter
        static void apu_400b_S(byte val)
        {
            _triPeriod_S = (_triPeriod_S & 0xFF) | ((val & 7) << 8);
            _triTimer_S  = _triPeriod_S;
            if (lenCtrEnable_S[2])
                lengthctr_S[2] = lenctrload_S[(val >> 3) & 0x1F];
            linctrflag_S = true;
        }
        // $400C: Noise envelope
        static void apu_400c_S(byte val)
        {
            lenctrHalt_S[3]     = (val & 0x20) != 0;
            envConstVolume_S[3] = (val & 0x10) != 0;
            envelopeValue_S[3]  = val & 0x0F;
        }
        // $400E: Noise mode + period
        static void apu_400e_S(byte val)
        {
            _noiseMode_S      = (val & 0x80) != 0;
            _noisePeriodIdx_S = val & 0x0F;
        }
        // $400F: Noise length counter
        static void apu_400f_S(byte val)
        {
            if (lenCtrEnable_S[3])
                lengthctr_S[3] = lenctrload_S[(val >> 3) & 0x1F];
            envelopeStartFlag_S[3] = true;
        }
        // $4010: DMC flags + rate
        static void apu_4010_S(byte val)
        {
            dmcirq_S  = (val & 0x80) != 0;
            if (!dmcirq_S) statusdmcint_S = false;
            dmcloop_S = (val & 0x40) != 0;
            dmcrate_S = dmcperiods_S[val & 0x0F];
        }
        // $4011: DMC DAC 直接寫入
        static void apu_4011_S(byte val)
        {
            dmcvalue_S = val & 0x7F;
        }
        // $4012: DMC 樣本起始位址
        static void apu_4012_S(byte val)
        {
            dmcstartaddr_S = 0xC000 + val * 64;
        }
        // $4013: DMC 樣本長度
        static void apu_4013_S(byte val)
        {
            dmcsamplelength_S = val * 16 + 1;
        }
        // $4015: 聲道啟用/停用
        static void apu_4015_S(byte val)
        {
            lenCtrEnable_S[0] = (val & 0x01) != 0;
            lenCtrEnable_S[1] = (val & 0x02) != 0;
            lenCtrEnable_S[2] = (val & 0x04) != 0;
            lenCtrEnable_S[3] = (val & 0x08) != 0;
            bool dmcEnable    = (val & 0x10) != 0;

            if (!lenCtrEnable_S[0]) lengthctr_S[0] = 0;
            if (!lenCtrEnable_S[1]) lengthctr_S[1] = 0;
            if (!lenCtrEnable_S[2]) lengthctr_S[2] = 0;
            if (!lenCtrEnable_S[3]) lengthctr_S[3] = 0;

            if (dmcEnable) { if (dmcsamplesleft_S == 0) restartdmc_S(); }
            else           { dmcsamplesleft_S = 0; }
            statusdmcint_S = false;
        }
        // $4017: Frame counter mode + IRQ inhibit
        static void apu_4017_S(byte val)
        {
            ctrmode_S    = ((val & 0x80) != 0) ? 5 : 4;
            apuintflag_S = (val & 0x40) != 0;
            if (apuintflag_S) statusframeint_S = false;
            framectr_S   = 0;
            int jitter = 2 + (apucycle_S & 1);
            if (ctrmode_S == 5)
            {
                setenvelope_S();
                setlinctr_S();
                setlength_S();
                setsweep_S();
                setvolumes_S();
                framectrdiv_S = frameReload5_S[0] + jitter - 1;
            }
            else
            {
                framectrdiv_S = frameReload4_S[0] + jitter - 1;
            }
        }
    }
}
