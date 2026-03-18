# 2026-02-25 ISSUE: Multi-Key Simultaneous Press Investigation

## Problem Description

In SMB3, pressing Right + B (run) + A (jump) simultaneously does not work. However, Left + B + A works correctly.

## Initial Suspicions

1. Emulator keyboard input implementation issue
2. `ProcessCmdKey`'s `keyData` carries modifier bits causing lookup table failure
3. Event-driven input model unreliable

## Investigation

### 1. Code Analysis

Examined the keyboard input chain:

```
ProcessCmdKey (WM_KEYDOWN)
  → NES_KeyMAP[(int)keyData]
  → NesCore.P1_ButtonPress()
  → P1_joypad_status[v] = 0x41

AprNesUI_KeyUp
  → NES_KeyMAP[e.KeyValue]
  → NesCore.P1_ButtonUnPress()
  → P1_joypad_status[v] = 0x40
```

Found that `ProcessCmdKey`'s `keyData` may carry modifier bits (upper 16 bits), while `KeyUp`'s `e.KeyValue` only has the pure VK code. This could theoretically cause asymmetry between key press and key release events.

### 2. Attempting GetAsyncKeyState Polling

Rewrote keyboard input to poll all mapped keys each frame using `GetAsyncKeyState()`, directly querying physical key state and completely bypassing the Windows message queue.

### 3. Validation Tool

Built `KeyTest/KeyTest.exe`, which uses `GetAsyncKeyState` to display currently detected keys in real time, to confirm which layer has the problem.

### 4. Test Results

| Key Combination | GetAsyncKeyState Result |
|-----------------|------------------------|
| Z + X + ← | ✅ All three keys detected |
| Z + X + → | ❌ → not detected (only Z + X seen) |

**GetAsyncKeyState also cannot detect Z + X + →**, meaning the problem is not in the emulator code but at a lower level.

## Root Cause: Hardware Keyboard Matrix Ghosting

### Principle

Membrane keyboards use a row×column scanning matrix, not an independent circuit per key:

```
        Col-A    Col-B    Col-C
Row-1 [  Z  ]  [  X  ]  [  ←  ]
Row-2 [  ?  ]  [  ?  ]  [  →  ]  ← Right arrow on different Row
```

When Z + X are held simultaneously, the two columns on Row-1 are shorted. When → (different Row) is then pressed, current creates ghost paths through Z or X, and the keyboard firmware cannot confirm whether → is truly pressed — it chooses to block it and does not report the key.

The Left arrow ← happens to be at a matrix position that does not conflict with Z/X, so Z + X + ← works correctly.

### Data Flow

```
Keyboard Hardware Matrix → Keyboard Firmware → HID Driver → Windows → GetAsyncKeyState → Emulator
```

The problem occurs at the far left (hardware firmware); no software-level change can fix it.

## Conclusion

**This issue is unrelated to the emulator implementation.** The original event-driven keyboard handling is completely correct, and polling also cannot solve this problem.

## Solutions (User-Side)

| Solution | Description |
|----------|-------------|
| Remap keys | Change A/B to K/L or J/L or other keys not conflicting with arrow keys in the matrix |
| Switch to mechanical keyboard | Most support 6KRO or higher, some support NKRO (unlimited N-key rollover) |
| Switch to USB gamepad | Each key has its own independent circuit, zero ghosting, fundamental solution |

## Related Files

- `KeyTest/KeyTest.cs` — Keyboard multi-key simultaneous press validation tool source code
- `KeyTest/KeyTest.exe` — Compiled test tool, ready to run directly
