using System;

namespace AprNesAvalonia.Platform;

/// <summary>
/// Gamepad input abstraction layer.
/// Win32: DirectInput8 + XInput.  Linux: SDL2/evdev.  macOS: SDL2/IOKit.
/// </summary>
public interface IGamepadBackend
{
    /// <summary>Initialize gamepad subsystem. windowHandle required for DirectInput on Windows.</summary>
    void Initialize(IntPtr windowHandle);

    /// <summary>Poll device state and dispatch NesCore button presses. Call once per frame.</summary>
    void Poll();

    /// <summary>Shutdown and release all devices.</summary>
    void Shutdown();

    /// <summary>Check if a mapped NES button is currently pressed.</summary>
    bool IsButtonPressed(int playerIndex, GamepadButton button);

    /// <summary>
    /// Enter config mode: wait for any button/axis press and return its info.
    /// Returns null on timeout.
    /// </summary>
    GamepadCaptureResult? WaitForButton(int timeoutMs);

    /// <summary>Load button mapping from INI joypad_* keys.</summary>
    void LoadMapping(IniFile ini);

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

/// <summary>Result of gamepad button capture in config mode.</summary>
public record GamepadCaptureResult(string IniKey, string DisplayName);
