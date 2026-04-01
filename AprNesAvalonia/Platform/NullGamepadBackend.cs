using System;

namespace AprNesAvalonia.Platform;

/// <summary>
/// No-op gamepad backend — used when no platform-specific implementation is available.
/// </summary>
public class NullGamepadBackend : IGamepadBackend
{
    public bool IsAvailable => false;
    public int ConnectedCount => 0;

    public void Initialize(IntPtr windowHandle) { }
    public void Poll() { }
    public void Shutdown() { }
    public void LoadMapping(IniFile ini) { }
    public bool IsButtonPressed(int playerIndex, GamepadButton button) => false;
    public GamepadCaptureResult? WaitForButton(int timeoutMs) => null;
}
