using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AprNes
{
    // =========================================================================
    // WaveOutPlayer — 系統層音效輸出
    // 訂閱 NesCore.AudioSampleReady 接收 PCM 樣本，
    // 透過 WinMM WaveOut API 輸出音效，不依賴第三方套件。
    // =========================================================================
    static class WaveOutPlayer
    {
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

        [DllImport("winmm.dll")] static extern int timeBeginPeriod(int uPeriod);
        [DllImport("winmm.dll")] static extern int timeEndPeriod(int uPeriod);

        [DllImport("winmm.dll")]
        static extern int waveOutOpen(out IntPtr hwo, int devId, ref WAVEFORMATEX fmt,
                                      IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, int sz);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, int sz);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, int sz);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr hwo);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr hwo);

        const int  WAVE_MAPPER    = -1;
        const int  WAVE_FORMAT_PCM = 1;
        const int  CALLBACK_NULL  = 0;
        const uint WHDR_INQUEUE   = 0x00000010u;

        const int SAMPLE_RATE    = 44100;
        const int BUFFER_SAMPLES = 735;  // ~1 frame @ 60fps
        const int NUM_BUFFERS    = 4;

        static IntPtr    _hWaveOut  = IntPtr.Zero;
        static bool      _audioReady = false;
        static short[][] _audioBufs  = new short[NUM_BUFFERS][];
        static GCHandle[] _bufPins   = new GCHandle[NUM_BUFFERS];
        static GCHandle   _hdrPin;
        static WAVEHDR[]  _waveHdrs  = new WAVEHDR[NUM_BUFFERS];
        static int        _curBuf    = 0;
        static int        _curPos    = 0;

        // 開啟 WaveOut 並訂閱 NesCore.AudioSampleReady
        public static void OpenAudio()
        {
            CloseAudio();

            WAVEFORMATEX fmt = new WAVEFORMATEX {
                wFormatTag      = WAVE_FORMAT_PCM,
                nChannels       = 1,
                nSamplesPerSec  = SAMPLE_RATE,
                wBitsPerSample  = 16,
                nBlockAlign     = 2,
                nAvgBytesPerSec = SAMPLE_RATE * 2,
                cbSize          = 0
            };

            if (waveOutOpen(out _hWaveOut, WAVE_MAPPER, ref fmt,
                            IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL) != 0)
            {
                _audioReady = false;
                return;
            }

            for (int i = 0; i < NUM_BUFFERS; i++)
            {
                _audioBufs[i] = new short[BUFFER_SAMPLES];
                _bufPins[i]   = GCHandle.Alloc(_audioBufs[i], GCHandleType.Pinned);
            }

            _hdrPin   = GCHandle.Alloc(_waveHdrs, GCHandleType.Pinned);
            int hdrSz = Marshal.SizeOf(typeof(WAVEHDR));

            for (int i = 0; i < NUM_BUFFERS; i++)
            {
                _waveHdrs[i] = new WAVEHDR {
                    lpData         = _bufPins[i].AddrOfPinnedObject(),
                    dwBufferLength = (uint)(BUFFER_SAMPLES * 2),
                    dwFlags        = 0
                };
                IntPtr ptr = Marshal.UnsafeAddrOfPinnedArrayElement(_waveHdrs, i);
                waveOutPrepareHeader(_hWaveOut, ptr, hdrSz);
            }

            _curBuf = 0;
            _curPos = 0;
            _audioReady = true;
            timeBeginPeriod(1);
            NesCore.AudioSampleReady += OnSampleReady;
        }

        // 關閉 WaveOut 並取消訂閱
        public static void CloseAudio()
        {
            NesCore.AudioSampleReady -= OnSampleReady;
            if (_hWaveOut == IntPtr.Zero) return;
            _audioReady = false;
            waveOutReset(_hWaveOut);

            int hdrSz = Marshal.SizeOf(typeof(WAVEHDR));
            for (int i = 0; i < NUM_BUFFERS; i++)
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
            timeEndPeriod(1);
        }

        static void OnSampleReady(short sample)
        {
            if (!_audioReady || _hWaveOut == IntPtr.Zero) return;

            _audioBufs[_curBuf][_curPos++] = sample;

            if (_curPos >= BUFFER_SAMPLES)
            {
                _curPos = 0;
                SubmitBuffer(_curBuf);
                _curBuf = (_curBuf + 1) % NUM_BUFFERS;
            }
        }

        static void SubmitBuffer(int idx)
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
                    if (++waited > 50) return;
                }

                waveOutUnprepareHeader(_hWaveOut, ptr, hdrSz);
                _waveHdrs[idx].dwFlags = 0;
                waveOutPrepareHeader(_hWaveOut, ptr, hdrSz);
                waveOutWrite(_hWaveOut, ptr, hdrSz);
            }
            catch (Exception) { }
        }
    }
}
