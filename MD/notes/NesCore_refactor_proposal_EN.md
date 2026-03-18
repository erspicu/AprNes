# NesCore Architecture Refactor Proposal

> Goal: Make NesCore a pure emulation logic core, with all system layers (display, audio, input, error reporting) implemented externally.

---

## Core Principles

```
NesCore only does:
  ✅ Computation (CPU instructions, PPU pixels, APU sample values, Mapper logic)
  ✅ Produce output data (frame buffer, PCM values, event notifications)
  ✅ Accept input injection (controller button states)

NesCore does NOT:
  ❌ Decide how to play audio (winmm, SDL, NAudio...)
  ❌ Decide how to display video (WinForms, DirectX, OpenGL...)
  ❌ Control FPS throttling (Sleep/Stopwatch)
  ❌ Pop up error dialogs (MessageBox)
  ❌ Configure OS timer resolution (timeBeginPeriod)
```

---

## Current Issues

### Issue 1 — APU.cs: Audio "output" mixed with audio "emulation"

**File:** `NesCore/APU.cs`

**Current situation:**
`APU.cs` is simultaneously responsible for two distinctly different things:

| Responsibility | Type | Should be in |
|----------------|------|--------------|
| `apu_step()` computes waveforms, envelopes, sweep, mixing for each channel, produces PCM sample values | Emulation logic | ✅ Stay in NesCore |
| `DllImport winmm.dll` (waveOutOpen/Write/Close, etc.) | System layer | ❌ Move out |
| `WAVEFORMATEX` / `WAVEHDR` struct declarations | System layer | ❌ Move out |
| `openAudio()` / `closeAudio()` | System layer | ❌ Move out |
| Managing GCHandle Pin for 4 audio buffers | System layer | ❌ Move out |
| Writing samples directly into WaveOut buffer and submitting | System layer | ❌ Move out |

**Impact:**
- NesCore is forced to depend on Windows WaveOut and cannot be used on other platforms or audio backends.
- Headless test mode (TestRunner) must work around this with an `AudioEnabled = false` flag, rather than cleanly not injecting an audio implementation.

**Recommended approach:**

When APU generates each PCM sample, deliver the value via a callback or ring buffer instead:

```csharp
// NesCore only exposes the interface
public static Action<short> AudioSampleReady;
// Or ring buffer approach
public static short[] AudioRingBuffer;
public static int     AudioWritePos;
```

Build a separate `WaveOutPlayer` (or any backend) externally to consume the data.  
Switching to SDL2, NAudio, OpenAL, etc. in the future requires no changes to NesCore.

---

### Issue 2 — PPU.cs: FPS throttling mixed with frame generation

**File:** `NesCore/PPU.cs`

**Current situation:**

| Responsibility | Type | Should be in |
|----------------|------|--------------|
| Compute pixels dot-by-dot, filling `ScreenBuf1x` | Emulation logic | ✅ Stay in NesCore |
| Call `Thread.Sleep(1)` after completing scanline 240 | System layer | ❌ Move out |
| `Stopwatch` + `_fpsDeadline` accumulation timing | System layer | ❌ Move out |
| `LimitFPS` flag controlling throttle behavior | System layer | ❌ Move out |

**Recommended approach:**

After PPU completes scanline 240, it only needs to fire the existing `VideoOutput` event — no waiting of any kind.  
FPS throttling is handled externally (by the UI or TestRunner) in the `VideoOutput` handler as they see fit.

```csharp
// PPU only does this
VideoOutput?.Invoke(null, VideoOut_arg);

// UI side adds FPS limiting in the handler
NesCore.VideoOutput += (s, e) => {
    RenderToScreen();
    ThrottleToTargetFps();   // UI manages this
};
```

---

### Issue 3 — Main.cs: Dependency on Windows.Forms

**File:** `NesCore/Main.cs`

**Current situation:**

| Responsibility | Type | Should be in |
|----------------|------|--------------|
| ROM parsing (iNES header), memory allocation | Emulation logic | ✅ Stay in NesCore |
| `init()` / `run()` / `SaveRam()` | Emulation logic | ✅ Stay in NesCore |
| `using System.Windows.Forms` → `MessageBox.Show()` | System layer | ❌ Move out |
| `timeBeginPeriod(1)` / `timeEndPeriod(1)` | System layer | ❌ Move out |

**Recommended approach:**

Change `ShowError` to use a delegate; NesCore only fires the notification, and the caller decides how to display it:

```csharp
// NesCore
public static Action<string> OnError;

static public void ShowError(string msg)
{
    OnError?.Invoke(msg);
}
```

```csharp
// WinForms UI side setup
NesCore.OnError = msg => MessageBox.Show(msg);

// TestRunner setup
NesCore.OnError = msg => Console.Error.WriteLine("ERROR: " + msg);
```

Move `timeBeginPeriod` / `timeEndPeriod` to the caller of `run()` (UI or TestRunner).  
NesCore's `run()` should not adjust OS timer resolution on its own.

---

### Issue 4 — JoyPad.cs (current design is nearly correct, minor confirmation)

**File:** `NesCore/JoyPad.cs`

The current design is already quite reasonable:

| Responsibility | Type | Assessment |
|----------------|------|------------|
| `gamepad_r_4016()` / `gamepad_w_4016()` | NES hardware register behavior | ✅ Correct, stay in NesCore |
| `P1_ButtonPress()` / `P1_ButtonUnPress()` | External injection interface | ✅ Correct, this is the public API |

The external caller (UI/keyboard/controller) calls these two methods at the appropriate time — no changes needed.

---

## Refactored Overall Architecture

```
┌───────────────────────────────────────────────────────┐
│                  NesCore (Pure Emulation)               │
│                                                       │
│  CPU.cs   MEM.cs   PPU.cs   APU.cs   IO.cs            │
│  JoyPad.cs   Mapper/                                  │
│                                                       │
│  ── Outputs ───────────────────────────────────────── │
│  ScreenBuf1x          → 256×240 ARGB pixel buffer     │
│  AudioSampleReady     → PCM short sample callback     │
│  VideoOutput (event)  → end-of-frame notification     │
│  OnError (delegate)   → error message notification    │
│                                                       │
│  ── Inputs ────────────────────────────────────────── │
│  P1_ButtonPress()     → button press                  │
│  P1_ButtonUnPress()   → button release                │
└───────────────────┬───────────────────────────────────┘
                    │
        ┌───────────┴──────────────┐
        ▼                          ▼
┌───────────────┐        ┌─────────────────────┐
│  WinForms UI  │        │     TestRunner      │
│               │        │   (Headless mode)   │
│ Subscribe to  │        │                     │
│  VideoOutput  │        │ Subscribe to        │
│  for display  │        │  VideoOutput for    │
│ WaveOutPlayer │        │  blargg detection   │
│  for audio    │        │ No audio, full speed│
│ Keyboard/pad  │        │ Input injected via  │
│  key inject   │        │  InputEvent         │
│ FPS throttle  │        │ No FPS limit        │
└───────────────┘        └─────────────────────┘
```

---

## Refactor Priority

| Priority | Item | Scope of Change | Expected Benefit |
|----------|------|-----------------|-----------------|
| 🔴 High | Move APU audio output out, replace with callback/buffer interface | `APU.cs` + new `WaveOutPlayer.cs` | Remove winmm P/Invoke, cross-platform audio backend |
| 🔴 High | Change `ShowError` to delegate, remove `using System.Windows.Forms` | `Main.cs` | NesCore has zero WinForms dependency |
| 🟡 Medium | Move FPS limiter out of PPU, controlled by external handler | `PPU.cs` + `AprNesUI.cs` | Emulation speed fully determined externally |
| 🟢 Low | Move `timeBeginPeriod` to the caller | `APU.cs`/`Main.cs` + UI/TestRunner | OS timer configuration responsibility separated |

---

## Verifiable Metrics After Completion

- No `.cs` file inside `NesCore/` has `using System.Windows.Forms`
- No `.cs` file inside `NesCore/` has audio output calls with `DllImport("winmm.dll")`
- TestRunner can run cleanly without setting `AudioEnabled = false` (because no audio implementation is injected at all)
- Replacing the audio backend only requires modifying `WaveOutPlayer.cs` (or creating a new player) — NesCore is untouched

---

*Created: 2026-02-26*
