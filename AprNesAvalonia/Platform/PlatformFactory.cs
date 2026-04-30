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

        // Linux / macOS: Hexa.NET.MiniAudio (WASAPI fallback never used here;
        // ALSA/PulseAudio on Linux, CoreAudio on macOS).
        return new MiniAudioBackend();
    }

    public static IGamepadBackend CreateGamepadBackend()
    {
        if (OperatingSystem.IsWindows())
            return new Win32GamepadBackend();

        // Linux / macOS: Hexa.NET.SDL3 with SDL_INIT_GAMEPAD only — no
        // SDL window, coexists cleanly with Avalonia's windowing.
        return new Sdl3GamepadBackend();
    }
}
