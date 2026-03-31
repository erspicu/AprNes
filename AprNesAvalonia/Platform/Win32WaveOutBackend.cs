using System;
using AprNes;

namespace AprNesAvalonia.Platform;

/// <summary>
/// Win32 audio backend — wraps existing WaveOutPlayer (WinMM winmm.dll).
/// NesCore.AudioSampleReady callback feeds samples to WaveOutPlayer internally,
/// so this backend only needs to open/close the device.
/// </summary>
public class Win32WaveOutBackend : IAudioBackend
{
    private bool _isOpen;

    public bool IsAvailable => OperatingSystem.IsWindows();

    public bool IsOpen => _isOpen;

    public void Open()
    {
        if (_isOpen) return;
        WaveOutPlayer.OpenAudio();
        _isOpen = true;
    }

    public void Close()
    {
        if (!_isOpen) return;
        WaveOutPlayer.CloseAudio();
        _isOpen = false;
    }
}
