using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using AprNes;
using Hexa.NET.SDL3;

namespace AprNesAvalonia.Platform;

/// <summary>
/// Cross-platform gamepad backend for Linux x64/ARM64 + macOS ARM64.
/// Uses SDL3's gamepad subsystem in headless mode (SDL_INIT_GAMEPAD without
/// Video) — no SDL window is created, so it coexists with Avalonia's
/// windowing without stepping on it.
///
/// SDL3 ships a controller-mapping database covering Xbox / PlayStation /
/// Switch Pro / 8BitDo / Stadia, so any standard gamepad reports the same
/// logical SOUTH/EAST/DPAD_*/etc buttons regardless of vendor.
///
/// INI key format mirrors Win32GamepadBackend's shape (joystickId, name,
/// rawCode) but is prefixed "sdl3:" so the same .ini cleanly carries
/// per-platform mappings without collision:
///   sdl3:&lt;instanceId&gt;,Button N,N            — gamepad button
///   sdl3:&lt;instanceId&gt;,LEFT|RIGHT,0,&lt;sign&gt;    — left-stick X
///   sdl3:&lt;instanceId&gt;,UP|DOWN,0,&lt;sign&gt;       — left-stick Y
/// </summary>
public class Sdl3GamepadBackend : IGamepadBackend
{
    // ~50 % of int16 full-scale; SDL applies its own deadzone too but this
    // keeps the D-pad emulation calm against analog noise on cheap pads.
    const short AXIS_THRESHOLD = 16384;

    private bool _initialized;
    private readonly Dictionary<int, SDLGamepadPtr> _gamepads = new();

    // Mapping: INI key string → (player 0/1, NES button index 0-7)
    private readonly Dictionary<string, (int player, byte button)> _mapping = new();

    // Latched state for IsButtonPressed and axis-direction edge detection.
    private readonly bool[,] _pressed = new bool[2, 8];
    // Per-instance per-axis-direction last-known "active" state, so axis
    // motion produces edge-triggered press/release events.
    // Key: (instanceId, axisId, signBit) where signBit = 0 for negative half.
    private readonly Dictionary<(int, byte, int), bool> _axisActive = new();

    public bool IsAvailable => !OperatingSystem.IsWindows();
    public int ConnectedCount => _gamepads.Count;

    public unsafe void Initialize(IntPtr windowHandle)
    {
        if (_initialized) return;
        // SDL_INIT_GAMEPAD (0x2000) — "Gamecontroller" name retained from SDL2
        // in the Hexa.NET binding. Implies SDL_INIT_JOYSTICK + SDL_INIT_EVENTS;
        // does NOT init video, so no window is created.
        if (!SDL.Init((uint)SDLInitFlags.Gamecontroller))
        {
            Console.WriteLine("[Sdl3GamepadBackend] SDL_Init failed");
            return;
        }
        _initialized = true;

        // SDL queues GamepadAdded events for already-connected pads at init
        // time, so the first Poll() call will pick them up.
    }

    public void LoadMapping(IniFile ini)
    {
        _mapping.Clear();
        // P1
        TryMap(ini.Get("joypad_A", ""),      0, 0);
        TryMap(ini.Get("joypad_B", ""),      0, 1);
        TryMap(ini.Get("joypad_SELECT", ""), 0, 2);
        TryMap(ini.Get("joypad_START", ""),  0, 3);
        TryMap(ini.Get("joypad_UP", ""),     0, 4);
        TryMap(ini.Get("joypad_DOWN", ""),   0, 5);
        TryMap(ini.Get("joypad_LEFT", ""),   0, 6);
        TryMap(ini.Get("joypad_RIGHT", ""),  0, 7);
        // P2
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

    public unsafe void Poll()
    {
        if (!_initialized) return;

        SDL.PumpEvents();
        SDLEvent ev;
        while (SDL.PollEvent(&ev))
        {
            switch (ev.Type)
            {
                case (uint)SDLEventType.GamepadAdded:
                    OnGamepadAdded(ev.Gdevice.Which);
                    break;
                case (uint)SDLEventType.GamepadRemoved:
                    OnGamepadRemoved(ev.Gdevice.Which);
                    break;
                case (uint)SDLEventType.GamepadButtonDown:
                case (uint)SDLEventType.GamepadButtonUp:
                    HandleButton(ev.Gbutton.Which, ev.Gbutton.Button, ev.Gbutton.Down != 0);
                    break;
                case (uint)SDLEventType.GamepadAxisMotion:
                    HandleAxis(ev.Gaxis.Which, ev.Gaxis.Axis, ev.Gaxis.Value);
                    break;
            }
        }
    }

    private unsafe void OnGamepadAdded(int instanceId)
    {
        if (_gamepads.ContainsKey(instanceId)) return;
        var pad = SDL.OpenGamepad(instanceId);
        if (pad.Handle == null) return;
        _gamepads[instanceId] = pad;
    }

    private void OnGamepadRemoved(int instanceId)
    {
        if (!_gamepads.TryGetValue(instanceId, out var pad)) return;
        SDL.CloseGamepad(pad);
        _gamepads.Remove(instanceId);
    }

    private void HandleButton(int instanceId, byte buttonId, bool down)
    {
        string key = $"sdl3:{instanceId},Button {buttonId},{buttonId}";
        if (!_mapping.TryGetValue(key, out var map)) return;

        _pressed[map.player, map.button] = down;
        if (down)
        {
            if (map.player == 0) NesCore.P1_ButtonPress(map.button);
            else                 NesCore.P2_ButtonPress(map.button);
        }
        else
        {
            if (map.player == 0) NesCore.P1_ButtonUnPress(map.button);
            else                 NesCore.P2_ButtonUnPress(map.button);
        }
    }

    // Edge-detected axis: when value crosses ±AXIS_THRESHOLD we emit a
    // press for that direction; when it falls back inside the deadzone we
    // emit a release. Tracks (axis, sign) state independently so quick
    // diagonals don't lose either component.
    private void HandleAxis(int instanceId, byte axisId, short value)
    {
        // Only the left stick maps to D-pad-style direction; right stick
        // and triggers stay unbound by default.
        if (axisId != (byte)SDLGamepadAxis.Leftx && axisId != (byte)SDLGamepadAxis.Lefty)
            return;

        bool nowNeg = value < -AXIS_THRESHOLD;
        bool nowPos = value >  AXIS_THRESHOLD;

        UpdateAxisDirection(instanceId, axisId, isPositive: false, nowNeg);
        UpdateAxisDirection(instanceId, axisId, isPositive: true,  nowPos);
    }

    private void UpdateAxisDirection(int instanceId, byte axisId, bool isPositive, bool active)
    {
        var stateKey = (instanceId, axisId, isPositive ? 1 : 0);
        bool wasActive = _axisActive.TryGetValue(stateKey, out var prev) && prev;
        if (active == wasActive) return;
        _axisActive[stateKey] = active;

        string dirName = AxisDirName(axisId, isPositive);
        int sign = isPositive ? 1 : -1;
        string key = $"sdl3:{instanceId},{dirName},0,{sign}";
        if (!_mapping.TryGetValue(key, out var map)) return;

        _pressed[map.player, map.button] = active;
        if (active)
        {
            if (map.player == 0) NesCore.P1_ButtonPress(map.button);
            else                 NesCore.P2_ButtonPress(map.button);
        }
        else
        {
            if (map.player == 0) NesCore.P1_ButtonUnPress(map.button);
            else                 NesCore.P2_ButtonUnPress(map.button);
        }
    }

    private static string AxisDirName(byte axisId, bool isPositive)
    {
        // SDL: +Y is down, -Y is up.
        if (axisId == (byte)SDLGamepadAxis.Leftx) return isPositive ? "RIGHT" : "LEFT";
        return isPositive ? "DOWN" : "UP";
    }

    public bool IsButtonPressed(int playerIndex, GamepadButton button)
    {
        if (playerIndex < 0 || playerIndex > 1) return false;
        return _pressed[playerIndex, (int)button];
    }

    public unsafe GamepadCaptureResult? WaitForButton(int timeoutMs)
    {
        if (!_initialized) return null;

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            SDL.PumpEvents();
            SDLEvent ev;
            while (SDL.PollEvent(&ev))
            {
                switch (ev.Type)
                {
                    case (uint)SDLEventType.GamepadAdded:
                        OnGamepadAdded(ev.Gdevice.Which);
                        break;
                    case (uint)SDLEventType.GamepadRemoved:
                        OnGamepadRemoved(ev.Gdevice.Which);
                        break;
                    case (uint)SDLEventType.GamepadButtonDown:
                    {
                        int who = ev.Gbutton.Which;
                        byte btn = ev.Gbutton.Button;
                        if (ev.Gbutton.Down == 0) break;
                        string name = $"Button {btn}";
                        return new GamepadCaptureResult(
                            $"sdl3:{who},{name},{btn}", name);
                    }
                    case (uint)SDLEventType.GamepadAxisMotion:
                    {
                        byte axis = ev.Gaxis.Axis;
                        short val = ev.Gaxis.Value;
                        if (axis != (byte)SDLGamepadAxis.Leftx &&
                            axis != (byte)SDLGamepadAxis.Lefty) break;
                        if (val > AXIS_THRESHOLD || val < -AXIS_THRESHOLD)
                        {
                            bool pos = val > 0;
                            string dn = AxisDirName(axis, pos);
                            int sign = pos ? 1 : -1;
                            return new GamepadCaptureResult(
                                $"sdl3:{ev.Gaxis.Which},{dn},0,{sign}", dn);
                        }
                        break;
                    }
                }
            }
            Thread.Sleep(10);
        }
        return null;
    }

    public void Shutdown()
    {
        if (!_initialized) return;
        foreach (var pad in _gamepads.Values)
            SDL.CloseGamepad(pad);
        _gamepads.Clear();
        _axisActive.Clear();
        SDL.QuitSubSystem((uint)SDLInitFlags.Gamecontroller);
        SDL.Quit();
        _initialized = false;
    }
}
