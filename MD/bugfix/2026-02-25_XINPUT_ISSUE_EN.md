# ISSUE: Xbox Gamepad (XInput) Incompatibility

**Date**: 2026-02-25
**Status**: Confirmed, pending fix

## Problem Description

When using an Xbox gamepad (Xbox 360 / Xbox One / Xbox Series), the emulator cannot detect any input and the configuration interface cannot complete button mapping.

## Root Cause: WinMM vs XInput — Two Separate APIs

### Current Implementation: WinMM joyGetPos (legacy API)

```
winmm.dll → joyGetDevCaps / joyGetPos
```

- Windows Multimedia Joystick API from the 1990s
- Supports at most 32 buttons, 2 analog axes (X/Y)
- Designed for old-style joysticks; no D-Pad concept
- **Xbox gamepads do not use this path**

### Xbox Gamepads Use: XInput (modern API)

```
xinput1_4.dll → XInputGetState
```

- Designed by Microsoft specifically for Xbox 360+ gamepads
- Supports 2 analog sticks, 2 triggers (LT/RT), D-Pad, 14 buttons
- **All Xbox / Xbox One / Xbox Series gamepads use XInput by default**
- `joyGetPos` is completely invisible to XInput devices

## Impact Analysis

```
Xbox gamepad plugged in
  ↓
Windows registers it as an XInput device
  ↓
joyGetDevCaps() scan → not found (or only found in degraded compatibility mode)
  ↓
joyinfo_list is empty → no events output at all
```

Two scenarios for Xbox gamepad under WinMM:
1. **Completely invisible** (most common with Xbox One / Series)
2. **Visible but broken**: D-Pad directions not reported as X/Y axes but as POV hat, which the `JOYINFO` struct does not have a field for

## API Support Comparison

| API | Xbox Gamepad | Third-Party USB Gamepad | Legacy Joystick |
|-----|-------------|------------------------|----------------|
| WinMM (current) | ❌ Not supported | ⚠️ Partial support | ✅ |
| XInput | ✅ Full support | ❌ Xbox-compatible devices only | ❌ |
| DirectInput | ⚠️ Compatibility layer | ✅ Mostly supported | ✅ |

## Planned Solution

**Dual API in parallel**: XInput detection first (handles Xbox gamepads), fall back to WinMM on failure (handles regular USB gamepads).

### Files to Modify

| File | Change |
|------|--------|
| `AprNes/tool/joystick.cs` | Add XInput scanning and polling logic |
| `AprNes/tool/NativeAPIShare.cs` | Add XInput P/Invoke (`XINPUT_STATE`, `XInputGetState`) |
| `AprNes/UI/AprNesUI.cs` | Gamepad event handling compatible with both API sources |
| `AprNes/UI/AprNes_ConfigureUI.cs` | Configuration UI distinguishes XInput / WinMM devices |

### XInput Button Mapping (Reference)

| XInput Button | wButtons bitmask |
|--------------|-----------------|
| A | 0x1000 |
| B | 0x2000 |
| X | 0x4000 |
| Y | 0x8000 |
| LB | 0x0100 |
| RB | 0x0200 |
| D-Pad Up | 0x0001 |
| D-Pad Down | 0x0002 |
| D-Pad Left | 0x0004 |
| D-Pad Right | 0x0008 |
| Start | 0x0010 |
| Back | 0x0020 |
