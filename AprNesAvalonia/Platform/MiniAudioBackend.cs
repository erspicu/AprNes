using System;
using System.Runtime.InteropServices;
using AprNes;
using Hexa.NET.MiniAudio;

namespace AprNesAvalonia.Platform;

/// <summary>
/// miniaudio playback backend for Linux x64/ARM64 + macOS ARM64.
/// Mirrors the producer/consumer split used by Win32WaveOutBackend:
/// emu thread (NesCore.AudioSampleReady) writes interleaved S16 stereo
/// into a lock-free SPSC ring; miniaudio's audio thread pulls from the
/// ring inside a static [UnmanagedCallersOnly] data callback.
///
/// Single-instance: ring buffer + callback are static because
/// PlatformFactory creates exactly one IAudioBackend per process and
/// miniaudio's data proc must be a function pointer (no captured state).
/// </summary>
public class MiniAudioBackend : IAudioBackend
{
    const int SAMPLE_RATE = 44100;
    const int CHANNELS    = 2;

    // 16384 shorts = 8192 stereo samples ≈ 186 ms cushion @ 44.1 kHz.
    // Power-of-two so we can mask instead of mod.
    const int RING_SHORTS = 16384;
    const int RING_MASK   = RING_SHORTS - 1;

    static readonly short[] _ring = new short[RING_SHORTS];
    static volatile int _writeIdx;
    static volatile int _readIdx;

    static MaDevice _device;
    static bool _started;

    public bool IsAvailable => !OperatingSystem.IsWindows();
    public bool IsOpen => _started;

    public unsafe void Open()
    {
        if (_started) return;

        var cfg = MiniAudio.DeviceConfigInit(MaDeviceType.Playback);
        cfg.Playback.Format         = MaFormat.S16;
        cfg.Playback.Channels       = CHANNELS;
        cfg.SampleRate              = SAMPLE_RATE;
        cfg.PeriodSizeInMilliseconds = 20;
        delegate* unmanaged<MaDevice*, void*, void*, uint, void> fp = &DataCallback;
        cfg.DataCallback = fp;

        _writeIdx = 0;
        _readIdx  = 0;

        var rc = MiniAudio.DeviceInit(default(MaContextPtr), in cfg, ref _device);
        if (rc != MaResult.Success) return;

        rc = MiniAudio.DeviceStart(ref _device);
        if (rc != MaResult.Success)
        {
            MiniAudio.DeviceUninit(ref _device);
            return;
        }

        NesCore.AudioSampleReady += OnSample;
        _started = true;
    }

    public void Close()
    {
        if (!_started) return;
        NesCore.AudioSampleReady -= OnSample;
        _started = false;
        MiniAudio.DeviceStop(ref _device);
        MiniAudio.DeviceUninit(ref _device);
    }

    static void OnSample(short left, short right)
    {
        int w = _writeIdx;
        int next = (w + 2) & RING_MASK;
        // Drop on overrun (consumer slow) — better than blocking the emu thread.
        if (next == _readIdx) return;
        _ring[w]                  = left;
        _ring[(w + 1) & RING_MASK] = right;
        _writeIdx = next;
    }

    [UnmanagedCallersOnly]
    static unsafe void DataCallback(MaDevice* device, void* pOutput, void* pInput, uint frameCount)
    {
        short* o = (short*)pOutput;
        int shortsNeeded = (int)frameCount * CHANNELS;
        int r = _readIdx;
        int w = _writeIdx;
        int available = (w - r) & RING_MASK;
        int take = shortsNeeded < available ? shortsNeeded : available;

        for (int i = 0; i < take; i++)
            o[i] = _ring[(r + i) & RING_MASK];
        for (int i = take; i < shortsNeeded; i++)
            o[i] = 0;

        _readIdx = (r + take) & RING_MASK;
    }
}
