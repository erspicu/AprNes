using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NativeTools;

namespace AprNesAvalonia.Platform;

/// <summary>
/// Win32 gamepad backend wrapping joystick.cs (DirectInput8 + XInput).
/// Reads joypad mapping from INI and dispatches NesCore P1/P2 button presses on Poll().
/// </summary>
public class Win32GamepadBackend : IGamepadBackend
{
    private readonly joystick _joy = new();
    private bool _initialized;

    // Mapping: INI key string → (player 0/1, buttonIndex 0-7)
    private readonly Dictionary<string, (int player, byte button)> _mapping = new();

    // Track pressed state for release detection
    private readonly bool[,] _pressed = new bool[2, 8];

    public bool IsAvailable => true;
    public int ConnectedCount => 0;

    public void Initialize(IntPtr windowHandle)
    {
        if (_initialized) return;
        try
        {
            _joy.Init(windowHandle);
            _initialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Win32GamepadBackend init error: " + ex.Message);
        }
    }

    public void LoadMapping(IniFile ini)
    {
        _mapping.Clear();

        // P1 buttons (player=0, button index 0-7)
        TryMap(ini.Get("joypad_A", ""),      0, 0);
        TryMap(ini.Get("joypad_B", ""),      0, 1);
        TryMap(ini.Get("joypad_SELECT", ""), 0, 2);
        TryMap(ini.Get("joypad_START", ""),  0, 3);
        TryMap(ini.Get("joypad_UP", ""),     0, 4);
        TryMap(ini.Get("joypad_DOWN", ""),   0, 5);
        TryMap(ini.Get("joypad_LEFT", ""),   0, 6);
        TryMap(ini.Get("joypad_RIGHT", ""),  0, 7);

        // P2 buttons (player=1, button index 0-7)
        TryMap(ini.Get("joypad_P2_A", ""),      1, 0);
        TryMap(ini.Get("joypad_P2_B", ""),      1, 1);
        TryMap(ini.Get("joypad_P2_SELECT", ""), 1, 2);
        TryMap(ini.Get("joypad_P2_START", ""),  1, 3);
        TryMap(ini.Get("joypad_P2_UP", ""),     1, 4);
        TryMap(ini.Get("joypad_P2_DOWN", ""),   1, 5);
        TryMap(ini.Get("joypad_P2_LEFT", ""),   1, 6);
        TryMap(ini.Get("joypad_P2_RIGHT", ""),  1, 7);
    }

    private void TryMap(string iniVal, int player, byte button)
    {
        if (!string.IsNullOrEmpty(iniVal))
            _mapping[iniVal] = (player, button);
    }

    public void Poll()
    {
        if (!_initialized) return;

        var events = _joy.joy_event_captur();
        foreach (var ev in events)
        {
            if (ev.event_type == 1) // button
            {
                string key = ev.joystick_id + ",Button " + ev.button_id + "," + ev.button_id;
                if (!_mapping.TryGetValue(key, out var map)) continue;

                if (ev.button_event == 1)
                {
                    _pressed[map.player, map.button] = true;
                    if (map.player == 0) AprNes.NesCore.P1_ButtonPress(map.button);
                    else                 AprNes.NesCore.P2_ButtonPress(map.button);
                }
                else
                {
                    _pressed[map.player, map.button] = false;
                    if (map.player == 0) AprNes.NesCore.P1_ButtonUnPress(map.button);
                    else                 AprNes.NesCore.P2_ButtonUnPress(map.button);
                }
            }
            else // axis/direction (event_type == 0)
            {
                string xy = ev.way_type == 0 ? "X" : "Y";
                string dirName = WayName(xy, ev.way_value);
                string key = ev.joystick_id + "," + dirName + ",0," + ev.way_value;

                if (_mapping.TryGetValue(key, out var map))
                {
                    // Non-center direction: press
                    _pressed[map.player, map.button] = true;
                    if (map.player == 0) AprNes.NesCore.P1_ButtonPress(map.button);
                    else                 AprNes.NesCore.P2_ButtonPress(map.button);
                }
                else
                {
                    // Center or unmapped: release both directions for this axis
                    string keyLo = ev.joystick_id + "," + WayName(xy, 0) + ",0,0";
                    string keyHi = ev.joystick_id + "," + WayName(xy, 65535) + ",0,65535";

                    bool anyBound = _mapping.ContainsKey(keyLo) || _mapping.ContainsKey(keyHi);
                    if (!anyBound) continue;

                    // Determine player from whichever direction is bound
                    int player = 0;
                    if (_mapping.TryGetValue(keyLo, out var mLo)) player = mLo.player;
                    else if (_mapping.TryGetValue(keyHi, out var mHi)) player = mHi.player;

                    // Release the pair (LEFT/RIGHT or UP/DOWN)
                    byte btnLo = (byte)(xy == "X" ? 6 : 4); // LEFT=6, UP=4
                    byte btnHi = (byte)(xy == "X" ? 7 : 5); // RIGHT=7, DOWN=5

                    _pressed[player, btnLo] = false;
                    _pressed[player, btnHi] = false;
                    if (player == 0) { AprNes.NesCore.P1_ButtonUnPress(btnLo); AprNes.NesCore.P1_ButtonUnPress(btnHi); }
                    else             { AprNes.NesCore.P2_ButtonUnPress(btnLo); AprNes.NesCore.P2_ButtonUnPress(btnHi); }
                }
            }
        }
    }

    public bool IsButtonPressed(int playerIndex, GamepadButton button)
    {
        if (playerIndex < 0 || playerIndex > 1) return false;
        return _pressed[playerIndex, (int)button];
    }

    public GamepadCaptureResult? WaitForButton(int timeoutMs)
    {
        if (!_initialized) return null;

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var events = _joy.joy_event_captur();
            foreach (var ev in events)
            {
                if (ev.event_type == 1 && ev.button_event == 1)
                {
                    string name = "Button " + ev.button_id;
                    string iniKey = ev.joystick_id + "," + name + "," + ev.button_id;
                    return new GamepadCaptureResult(iniKey, name);
                }
                if (ev.event_type == 0 && ev.way_value != 32767)
                {
                    string xy = ev.way_type == 0 ? "X" : "Y";
                    string name = WayName(xy, ev.way_value);
                    string iniKey = ev.joystick_id + "," + name + ",0," + ev.way_value;
                    return new GamepadCaptureResult(iniKey, name);
                }
            }
            Thread.Sleep(10);
        }
        return null;
    }

    public void Shutdown()
    {
        _initialized = false;
    }

    private static string WayName(string xy, int value)
    {
        if (xy == "X") return value == 0 ? "LEFT" : value == 65535 ? "RIGHT" : "";
        return value == 0 ? "UP" : value == 65535 ? "DOWN" : "";
    }
}
