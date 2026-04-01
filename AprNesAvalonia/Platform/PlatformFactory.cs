using System;

namespace AprNesAvalonia.Platform;

/// <summary>
/// Runtime platform detection — creates appropriate backend for current OS.
/// </summary>
public static class PlatformFactory
{
    public static IAudioBackend CreateAudioBackend()
    {
        if (OperatingSystem.IsWindows())
            return new Win32WaveOutBackend();

        // TODO: Linux → SDL2AudioBackend or PulseAudioBackend
        // TODO: macOS → SDL2AudioBackend or CoreAudioBackend

        // Return a backend that reports IsAvailable=false
        return new NullAudioBackend();
    }

    public static IGamepadBackend CreateGamepadBackend()
    {
        if (OperatingSystem.IsWindows())
            return new Win32GamepadBackend();

        // TODO: Linux → SDL2GamepadBackend or EvdevBackend
        // TODO: macOS → SDL2GamepadBackend

        return new NullGamepadBackend();
    }

    /// <summary>Fallback audio backend when platform has no implementation.</summary>
    private class NullAudioBackend : IAudioBackend
    {
        public bool IsAvailable => false;
        public bool IsOpen => false;
        public void Open() { }
        public void Close() { }
    }
}
