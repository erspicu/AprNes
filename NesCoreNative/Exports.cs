// NesCoreNative – Native AOT DLL Export Layer
// Exposes NesCore as a C-callable native library via [UnmanagedCallersOnly].
//
// Build (Native AOT .dll):
//   dotnet publish -r win-x64 -c Release
//   → publish/NesCoreNative.dll  (fully native, no .NET runtime required)
//
// Exported C API:
//   void  nescore_set_video_callback(void (*cb)())
//   void  nescore_set_audio_callback(void (*cb)(short))
//   void  nescore_set_error_callback(void (*cb)(const char*, int))
//   int   nescore_init(uint8_t* romData, int len)   // 1=ok, 0=fail
//   void  nescore_run()                              // starts emu thread
//   void  nescore_stop()
//   uint32_t* nescore_get_screen()                  // 256x240 ARGB pixels
//   void  nescore_joypad_press(uint8_t btn)          // btn: 0=A 1=B 2=SEL 3=START 4=UP 5=DOWN 6=LEFT 7=RIGHT
//   void  nescore_joypad_release(uint8_t btn)
//   void  nescore_set_volume(int vol)               // 0~100
//   void  nescore_set_limitfps(int enable)          // 0=unlimited, 1=~60fps
//   int   nescore_benchmark(int seconds)            // blocking; returns frame count

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AprNes
{
    public static unsafe class NesCoreExports
    {
        // ── Stored callback function pointers ─────────────────────────────────
        static delegate* unmanaged[Cdecl]<void>             _videoCallback;
        static delegate* unmanaged[Cdecl]<short, void>      _audioCallback;
        static delegate* unmanaged[Cdecl]<byte*, int, void> _errorCallback;

        // ── Benchmark state ────────────────────────────────────────────────────
        static volatile int  _benchFrames;
        static volatile bool _benchMode;

        // ── Static managed-side event handlers (no closures → AOT-safe) ───────
        static void OnVideo(object sender, EventArgs e)
        {
            if (_benchMode) _benchFrames++;
            if (_videoCallback != null) _videoCallback();
        }

        static void OnAudio(short sample)
        {
            if (_audioCallback != null) _audioCallback(sample);
        }

        static readonly byte[] _errBuf = new byte[512];
        static void OnError(string msg)
        {
            if (_errorCallback == null) return;
            int n = System.Text.Encoding.UTF8.GetBytes(msg, 0, Math.Min(msg.Length, 510), _errBuf, 0);
            _errBuf[n] = 0;
            fixed (byte* p = _errBuf) _errorCallback(p, n);
        }

        // ── Exports ───────────────────────────────────────────────────────────

        [UnmanagedCallersOnly(EntryPoint = "nescore_set_video_callback")]
        public static void SetVideoCallback(delegate* unmanaged[Cdecl]<void> cb)
            => _videoCallback = cb;

        [UnmanagedCallersOnly(EntryPoint = "nescore_set_audio_callback")]
        public static void SetAudioCallback(delegate* unmanaged[Cdecl]<short, void> cb)
            => _audioCallback = cb;

        [UnmanagedCallersOnly(EntryPoint = "nescore_set_error_callback")]
        public static void SetErrorCallback(delegate* unmanaged[Cdecl]<byte*, int, void> cb)
            => _errorCallback = cb;

        /// <summary>Init emulator with ROM bytes. Returns 1 on success, 0 on failure.</summary>
        [UnmanagedCallersOnly(EntryPoint = "nescore_init")]
        public static int Init(byte* romData, int len)
        {
            // Unsubscribe first to avoid duplicate handlers on re-init
            NesCore.VideoOutput      -= OnVideo;
            NesCore.AudioSampleReady -= OnAudio;
            NesCore.VideoOutput      += OnVideo;
            NesCore.AudioSampleReady += OnAudio;
            NesCore.OnError           = OnError;

            byte[] rom = new byte[len];
            Marshal.Copy((nint)romData, rom, 0, len);
            return NesCore.init(rom) ? 1 : 0;
        }

        /// <summary>Start the emulator loop on a background thread.</summary>
        [UnmanagedCallersOnly(EntryPoint = "nescore_run")]
        public static void Run()
        {
            NesCore.exit = false;
            new Thread(NesCore.run) { IsBackground = true }.Start();
        }

        /// <summary>Signal the emulator loop to exit.</summary>
        [UnmanagedCallersOnly(EntryPoint = "nescore_stop")]
        public static void Stop() => NesCore.exit = true;

        /// <summary>Returns pointer to the 256×240 ARGB screen buffer (61440 uint32).</summary>
        [UnmanagedCallersOnly(EntryPoint = "nescore_get_screen")]
        public static uint* GetScreen() => NesCore.ScreenBuf1x;

        /// <summary>Press a joypad button (btn 0–7: A B SEL START UP DOWN LEFT RIGHT).</summary>
        [UnmanagedCallersOnly(EntryPoint = "nescore_joypad_press")]
        public static void JoypadPress(byte btn) => NesCore.P1_ButtonPress(btn);

        /// <summary>Release a joypad button.</summary>
        [UnmanagedCallersOnly(EntryPoint = "nescore_joypad_release")]
        public static void JoypadRelease(byte btn) => NesCore.P1_ButtonUnPress(btn);

        /// <summary>Set audio volume (0–100).</summary>
        [UnmanagedCallersOnly(EntryPoint = "nescore_set_volume")]
        public static void SetVolume(int vol) => NesCore.Volume = vol;

        /// <summary>Enable (1) or disable (0) the ~60 fps limiter.</summary>
        [UnmanagedCallersOnly(EntryPoint = "nescore_set_limitfps")]
        public static void SetLimitFps(int enable) => NesCore.LimitFPS = enable != 0;

        /// <summary>
        /// Blocking benchmark: runs emulator at max speed for <paramref name="seconds"/> seconds.
        /// Returns total frames rendered. ROM must be init'd before calling.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "nescore_benchmark")]
        public static int Benchmark(int seconds)
        {
            _benchFrames = 0;
            _benchMode   = true;
            NesCore.LimitFPS = false;
            NesCore.exit     = false;

            var t = new Thread(NesCore.run) { IsBackground = true };
            t.Start();
            Thread.Sleep(seconds * 1000);
            NesCore.exit = true;
            t.Join(2000);

            _benchMode = false;
            return _benchFrames;
        }
    }
}
