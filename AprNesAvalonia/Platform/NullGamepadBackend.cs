namespace AprNesAvalonia.Platform;

/// <summary>
/// No-op gamepad backend — used when no platform-specific implementation is available.
/// </summary>
public class NullGamepadBackend : IGamepadBackend
{
    public bool IsAvailable => false;
    public int ConnectedCount => 0;

    public void Initialize() { }
    public void Poll() { }
    public void Shutdown() { }
    public bool IsButtonPressed(int playerIndex, GamepadButton button) => false;
    public GamepadButtonInfo? WaitForButton(int timeoutMs) => null;
}
