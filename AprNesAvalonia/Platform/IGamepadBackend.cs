namespace AprNesAvalonia.Platform;

/// <summary>
/// Gamepad input abstraction layer.
/// Win32: DirectInput8 + XInput.  Linux: SDL2/evdev.  macOS: SDL2/IOKit.
/// </summary>
public interface IGamepadBackend
{
    /// <summary>Initialize gamepad subsystem and enumerate devices.</summary>
    void Initialize();

    /// <summary>Poll device state. Call once per frame.</summary>
    void Poll();

    /// <summary>Shutdown and release all devices.</summary>
    void Shutdown();

    /// <summary>Check if a button is currently pressed.</summary>
    bool IsButtonPressed(int playerIndex, GamepadButton button);

    /// <summary>
    /// Enter config mode: wait for any button press and return its info.
    /// Returns null on timeout.
    /// </summary>
    GamepadButtonInfo? WaitForButton(int timeoutMs);

    /// <summary>Whether the backend is available on this platform.</summary>
    bool IsAvailable { get; }

    /// <summary>Number of currently connected gamepads.</summary>
    int ConnectedCount { get; }
}

/// <summary>NES controller buttons (matches NesCore P1_ButtonPress index).</summary>
public enum GamepadButton : byte
{
    A = 0, B = 1, Select = 2, Start = 3,
    Up = 4, Down = 5, Left = 6, Right = 7
}

/// <summary>Info returned when a gamepad button is captured in config mode.</summary>
public record GamepadButtonInfo(string DeviceId, string ButtonName, int RawIndex);
