namespace AprNesAvalonia.Platform;

/// <summary>
/// Audio output abstraction layer.
/// Win32: WinMM WaveOut.  Linux: SDL2/PulseAudio.  macOS: SDL2/CoreAudio.
/// </summary>
public interface IAudioBackend
{
    /// <summary>Open audio device and start playback.</summary>
    void Open();

    /// <summary>Stop playback and release device.</summary>
    void Close();

    /// <summary>Whether the backend is available on this platform.</summary>
    bool IsAvailable { get; }

    /// <summary>Whether audio is currently open and playing.</summary>
    bool IsOpen { get; }
}
