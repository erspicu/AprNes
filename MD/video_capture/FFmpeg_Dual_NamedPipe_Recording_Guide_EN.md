# C# + FFmpeg Dual Named Pipe Game Recording — Complete Implementation Guide

> **Target Environment**: Windows / C# (.NET Framework 4.x or .NET 6+) / FFmpeg CLI
> **End Result**: Real-time game video + audio recording → H.264/AAC MP4 file
> **Based on**: Real-world development experience from AprNes (NES emulator), including every pitfall we hit

---

## Table of Contents

1. [Why FFmpeg CLI + Named Pipes?](#1-why-ffmpeg-cli--named-pipes)
2. [Architecture Overview](#2-architecture-overview)
3. [Seven-Step Startup Flow (With Five Deadly Pitfalls)](#3-seven-step-startup-flow-with-five-deadly-pitfalls)
4. [Video Side: Triple-Buffer Frame Pool](#4-video-side-triple-buffer-frame-pool)
5. [Audio Side: Lock-Guarded Accumulation Buffer](#5-audio-side-lock-guarded-accumulation-buffer)
6. [Dual Writer Thread Architecture](#6-dual-writer-thread-architecture)
7. [Safe Recording Stop & MP4 Finalization](#7-safe-recording-stop--mp4-finalization)
8. [H.264 Hardware Encoder Auto-Detection](#8-h264-hardware-encoder-auto-detection)
9. [Complete Source Code & Integration Guide](#9-complete-source-code--integration-guide)
10. [Five Pitfalls We Actually Hit (War Stories)](#10-five-pitfalls-we-actually-hit-war-stories)
11. [Pre-Launch Checklist](#11-pre-launch-checklist)

---

## 1. Why FFmpeg CLI + Named Pipes?

### How Do OBS / RetroArch Do It?

They call FFmpeg's low-level dynamic libraries (libavformat, libavcodec, etc.) directly from C/C++, manually packaging A/V packets in memory and writing them to file.

For C# developers, taking that approach (e.g., via FFmpeg.AutoGen) multiplies development cost by several times and is prone to memory leaks. **Sticking with the CLI (ffmpeg.exe) is the smartest, most cost-effective choice for C# developers.**

### Why Not Use stdin?

If you use stdin for video and a Named Pipe for audio (or vice versa), the async A/V logic becomes tangled. Plus, stdin is only one stream — you can't send two raw data streams simultaneously.

If you want to multiplex A/V through stdin, you'd need to implement your own container format muxer (NUT, FLV, etc.) — that's shooting yourself in the foot.

**Conclusion: Two Named Pipes (video + audio), let FFmpeg handle the muxing — cleanest architecture.**

---

## 2. Architecture Overview

```
┌──────────────┐
│  Game Loop     │
│  (Emu Core)    │
│                │
│  Each frame:   │
│  · Video BGRA  │──→ PushFrame() ──→ [Triple Buffer Queue]
│  · Audio PCM   │──→ OnAudioSample() → [Audio Accumulation Buffer]
└──────────────┘
                                            │                    │
                                            ▼                    ▼
                                   ┌─────────────┐    ┌──────────────┐
                                   │ Video Writer │    │ Audio Writer  │
                                   │   Thread     │    │   Thread     │
                                   │              │    │              │
                                   │ _videoPipe   │    │ _audioPipe   │
                                   │  .Write()    │    │  .Write()    │
                                   └──────┬───────┘    └──────┬───────┘
                                          │                    │
                              \\.\pipe\video           \\.\pipe\audio
                                          │                    │
                                          ▼                    ▼
                                   ┌──────────────────────────────┐
                                   │         ffmpeg.exe           │
                                   │                              │
                                   │  -i \\.\pipe\video (rawvideo)│
                                   │  -i \\.\pipe\audio (s16le)   │
                                   │  → H.264 + AAC → output.mp4 │
                                   └──────────────────────────────┘
```

### Core Design Principles

| Principle | Description |
|-----------|-------------|
| **Game thread never blocks** | PushFrame() only does memcpy into pool buffer — O(1) operation |
| **Video / Audio never block each other** | Independent Writer Threads; one stalling won't affect the other |
| **IOException tolerance** | Pipe write failure only drops a frame, doesn't kill the recording |
| **Clean EOF finalization** | Close() pipe → FFmpeg writes MOOV atom → valid MP4 |

---

## 3. Seven-Step Startup Flow (With Five Deadly Pitfalls)

This is the most complex part of the entire implementation. We hit countless bugs here before arriving at this seven-step flow:

```
Step 1: Create two NamedPipeServerStreams (with large buffers)
Step 2: BeginWaitForConnection() (async wait for FFmpeg to connect)
Step 3: Launch ffmpeg.exe subprocess
Step 4: Wait for Video Pipe connection
Step 5: Send a Dummy Frame (satisfy FFmpeg Probe)        ← KEY!
Step 6: Wait for Audio Pipe connection
Step 7: _recording = true → Start both Writer Threads    ← Simultaneous start!
```

### Step 1: Create Pipes — Must Specify Large Buffers

```csharp
int videoBufSize = frameSize * 2;  // 2 frames capacity (e.g., 1024×840×4×2 ≈ 6.8MB)
int audioBufSize = 65536;          // 64KB

_videoPipe = new NamedPipeServerStream(videoPipeName, PipeDirection.Out, 1,
    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, videoBufSize);
_audioPipe = new NamedPipeServerStream(audioPipeName, PipeDirection.Out, 1,
    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, audioBufSize);
```

> **Pitfall #1: Default Pipe Buffer Is Too Small**
> Without specifying outBufferSize, Windows Named Pipe defaults to ~4KB buffer.
> A single 1024×840 BGRA frame is 3.4MB — Write() would need ~860 kernel round-trips,
> causing severe blocking. With a large buffer, the entire frame writes in one shot.

### Step 2 & 3: BeginWaitForConnection First, Then Start FFmpeg

```csharp
var videoConnectResult = _videoPipe.BeginWaitForConnection(null, null);
var audioConnectResult = _audioPipe.BeginWaitForConnection(null, null);

// FFmpeg connects as a client to both pipes
_ffmpeg = new Process { ... };
_ffmpeg.Start();
```

> **Pitfall #2: Order Cannot Be Reversed**
> You must call BeginWaitForConnection() before Start(). If you start FFmpeg first,
> it tries to connect when the pipe server doesn't exist yet, and immediately exits with an error.

### Step 4: Wait for Video Pipe Connection

```csharp
if (!videoConnectResult.AsyncWaitHandle.WaitOne(10000))
    throw new TimeoutException("FFmpeg did not connect to video pipe");
_videoPipe.EndWaitForConnection(videoConnectResult);
```

### Step 5: Send Dummy Frame (The Most Critical Step!)

```csharp
byte[] dummyFrame = new byte[_frameSize]; // all black
_videoPipe.Write(dummyFrame, 0, _frameSize);
```

> **Pitfall #3: FFmpeg Probe Deadlock**
> Even with `-analyzeduration 0 -probesize 32`, FFmpeg **still needs to read at least 32 bytes**
> of video data to confirm the first input pipe is alive, before it opens the second Audio pipe.
>
> If you don't feed data, FFmpeg sits waiting on the Video Pipe, never connecting to Audio Pipe.
> Meanwhile, C# is stuck on `audioConnectResult.WaitOne()` waiting for FFmpeg to connect Audio.
> **Both sides waiting on each other = perfect deadlock.**
>
> Solution: Immediately write a black Dummy Frame from the main thread after Video Pipe connects.
> Once FFmpeg receives the data, it immediately opens the Audio Pipe.

### Step 6: Wait for Audio Pipe Connection

```csharp
if (!audioConnectResult.AsyncWaitHandle.WaitOne(15000))
    throw new TimeoutException("FFmpeg did not connect to audio pipe");
_audioPipe.EndWaitForConnection(audioConnectResult);
```

### Step 7: Simultaneous Start

```csharp
_recording = true;
NesCore.AudioSampleReady += OnAudioSample;

_videoWriterThread = new Thread(VideoWriterLoop) { IsBackground = true };
_videoWriterThread.Start();

_audioWriterThread = new Thread(AudioWriterLoop) { IsBackground = true };
_audioWriterThread.Start();
```

> **Pitfall #4: Misaligned Starting Line → A/V Desync**
> If you set `_recording = true` and start collecting A/V data before Audio Pipe connects,
> Video runs ahead. When Audio arrives late, FFmpeg's Muxer introduces a timeline offset.
>
> You must wait for **both Pipes to connect** before sending any data.

> **Pitfall #5: Writer Thread Premature Death**
> If you start a Writer Thread while `_recording = false`,
> the `while (_recording)` loop exits immediately — the thread dies instantly.
> Even if `_recording` later becomes true, there's no thread running anymore.
>
> You must set `_recording = true` **before** starting the threads.

---

## 4. Video Side: Triple-Buffer Frame Pool

The game loop calls `PushFrame()` once per frame to push the current screen into the pool:

```csharp
static byte[][] _frameBufs;                                    // 3 buffers
static readonly ConcurrentQueue<int> _freeFrames  = new ...;  // free indices
static readonly ConcurrentQueue<int> _readyFrames = new ...;  // pending indices

public static unsafe void PushFrame(uint* screenBuf)
{
    if (!_recording || !_videoPipeConnected) return;

    int idx;
    if (!_freeFrames.TryDequeue(out idx)) return; // Pool full → drop frame

    fixed (byte* dst = _frameBufs[idx])
        Buffer.MemoryCopy(screenBuf, dst, _frameSize, _frameSize);

    _readyFrames.Enqueue(idx);
    _videoSignal.Set(); // Wake the Writer Thread
}
```

### Why Triple-Buffer?

| Buffer Count | Behavior |
|--------------|----------|
| 1 (Single) | Game thread and Writer compete for the same block → needs lock → game stutters |
| 2 (Double) | Writer writes old, game writes new → but if Writer is slow, frames drop |
| 3 (Triple) | Game always gets a free buffer, Writer always has work → optimal balance |

The game thread only does `TryDequeue` + `memcpy` + `Enqueue` — all O(1) lock-free operations. **The game loop is never blocked.**

---

## 5. Audio Side: Lock-Guarded Accumulation Buffer

NES emulator audio is generated sample-by-sample (44100 Hz = 44100 shorts per second):

```csharp
static short[] _audioBuf;  // 441000 = 10 seconds capacity
static int _audioPos;
static readonly object _audioLock = new object();

static void OnAudioSample(short sample)
{
    if (!_recording) return;
    lock (_audioLock)
    {
        if (_audioPos < _audioBuf.Length)
            _audioBuf[_audioPos++] = sample;
    }
}
```

### Why Make the Buffer 10 Seconds?

During startup, there can be a 0.5–2 second delay between Video Pipe connecting and Audio Pipe connecting. If the buffer only holds 8192 samples (0.18 seconds), audio data overflows and is discarded during the wait, causing missing audio at the beginning → **A/V desync**.

441000 samples = 10 seconds capacity — enough for any reasonable startup delay.

---

## 6. Dual Writer Thread Architecture

**This is the single most important design decision in the final solution.**

### Why a Single Writer Thread Doesn't Work

Our initial design used one Writer Thread for both Video and Audio:

```csharp
// ❌ Wrong design: Single Writer Thread
while (_recording)
{
    // Write video
    while (_readyFrames.TryDequeue(out idx))
        _videoPipe.Write(_frameBufs[idx], 0, _frameSize); // ← blocks here!

    // Write audio — but if the above blocks, this never executes
    FlushAudio();
}
```

Video Pipe Write() pushes 3.4MB of data — even with a large buffer, it can occasionally block. Once it blocks, FlushAudio() never gets a chance to run. FFmpeg needs both streams flowing simultaneously to advance (due to the `-shortest` flag), but Audio is starved → FFmpeg stalls on Video too → Video Write() stays blocked → **another deadlock**.

### Correct Design: Independent Dual Threads

```csharp
// Video Writer Thread — only handles video
static void VideoWriterLoop()
{
    while (_recording)
    {
        _videoSignal.WaitOne(50);
        if (!_recording) break;

        int idx;
        while (_readyFrames.TryDequeue(out idx))
        {
            try
            {
                if (_videoPipeConnected && _videoPipe != null)
                    _videoPipe.Write(_frameBufs[idx], 0, _frameSize);
            }
            catch (IOException)
            {
                // Drop frame, don't kill recording
            }
            catch (Exception)
            {
                _recording = false; // Only fatal errors stop recording
            }
            finally { _freeFrames.Enqueue(idx); }
        }
    }
}

// Audio Writer Thread — only handles audio, completely independent
static void AudioWriterLoop()
{
    while (_recording)
    {
        Thread.Sleep(20); // ~50 times/sec
        if (!_recording) break;
        FlushAudio();
    }
    FlushAudio(); // One final flush
}
```

Two threads operate independently — **one blocking absolutely cannot affect the other**.

### IOException Tolerance

Pipes occasionally produce transient IOExceptions when FFmpeg internally reorganizes its queue. If you set `_recording = false` on every IOException, the entire recording is ruined.

Correct approach: **IOException = drop one frame, keep recording. Only non-IO fatal errors should stop recording.**

---

## 7. Safe Recording Stop & MP4 Finalization

```csharp
public static void Stop()
{
    if (!_recording) return;
    _recording = false;

    // 1. Stop collecting new data
    NesCore.AudioSampleReady -= OnAudioSample;
    _videoSignal.Set();

    // 2. Wait for Writer Threads to drain remaining data
    _videoWriterThread?.Join(5000);
    _audioWriterThread?.Join(3000);

    // 3. Close Pipes → FFmpeg receives EOF
    _videoPipe?.Close();
    _audioPipe?.Close();

    // 4. Wait for FFmpeg to finalize MP4 (write MOOV atom)
    if (!_ffmpeg.WaitForExit(15000))
        _ffmpeg.Kill(); // Force kill on timeout
}
```

### Why Do MP4 Files Get Corrupted?

MP4 metadata (the MOOV atom) is written at the **very end** of the file. If FFmpeg is force-killed without receiving a proper EOF signal, the MOOV atom never gets written, and the file becomes unplayable.

**You must call `pipe.Close()` properly** to signal to FFmpeg that the stream has ended, allowing it to finalize normally.

Adding `-movflags +faststart` makes FFmpeg relocate the MOOV atom to the beginning of the file after writing, enabling progressive playback over the network.

---

## 8. H.264 Hardware Encoder Auto-Detection

```csharp
static string DetectEncoder(string ffmpegPath)
{
    string[] candidates = { "h264_nvenc", "h264_amf", "h264_qsv", "h264_d3d12va" };
    foreach (var enc in candidates)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-f lavfi -i nullsrc=s=256x240:d=0.1 -c:v {enc} -f null -",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        p.Start();
        p.StandardError.ReadToEnd();
        p.WaitForExit(5000);
        if (p.ExitCode == 0) return enc; // This hardware encoder is available
    }
    return "libx264"; // All failed, fall back to CPU software encoding
}
```

| Encoder | GPU Vendor | Notes |
|---------|-----------|-------|
| h264_nvenc | NVIDIA | Most common, GeForce GTX 600+ |
| h264_amf | AMD | Radeon RX 400+ |
| h264_qsv | Intel | Integrated GPU Quick Sync Video |
| h264_d3d12va | Universal | DirectX 12 Video Acceleration |
| libx264 | None (CPU) | Last-resort fallback |

Detection results are cached (`_cachedEncoder`), so subsequent recordings don't need to re-probe.

---

## 9. Complete Source Code & Integration Guide

Below is the final version after five iterations of development (ready to use), along with how to integrate it into your game/emulator.

### 9.1 Integrating Into Your Game (Four Hookup Points)

VideoRecorder is a standalone static class. You only need to do four things in your game code:

#### Hookup Point 1: Render Loop — Push Each Frame

In your game's render function, call `PushFrame()` after each frame is drawn:

```csharp
// In your game loop / render callback
unsafe void OnFrameRendered()
{
    // ... normal rendering logic ...
    RenderToScreen();

    // Recording: push the final frame to VideoRecorder
    if (VideoRecorder.IsRecording)
    {
        // screenBuffer is your final render output (BGRA format uint* pointer)
        uint* screenBuffer = GetFinalRenderOutput();
        if (screenBuffer != null)
            VideoRecorder.PushFrame(screenBuffer);
    }
}
```

> **Note**: You want to record the "final output" — the frame after all filters/scaling/post-processing.
> If your game has multiple render modes (e.g., CRT filter, xBRZ upscaling, etc.),
> make sure you're capturing the pointer to **what the player actually sees**.

#### Hookup Point 2: Audio Engine — Hook/Unhook Sample Callback

Audio hooking happens inside `Start()` on success and inside `Stop()`:

```csharp
// After Start succeeds (already done in VideoRecorder.Start() Step 7)
YourAudioEngine.OnSampleReady += VideoRecorder.PushAudioSample;

// On Stop
YourAudioEngine.OnSampleReady -= VideoRecorder.PushAudioSample;
VideoRecorder.Stop();
```

If your audio engine isn't event-based, you can call directly from where audio is generated:

```csharp
// In your audio mixing loop
short sample = MixAudioSample();
PlayToSpeaker(sample);
VideoRecorder.PushAudioSample(sample); // Also feed the recorder
```

#### Hookup Point 3: UI — Start/Stop Recording Button

```csharp
void OnRecordButtonClick()
{
    if (VideoRecorder.IsRecording)
    {
        // Stop recording
        YourAudioEngine.OnSampleReady -= VideoRecorder.PushAudioSample;
        VideoRecorder.Stop();
        UpdateUI("Ready");
        return;
    }

    // Start recording
    string ffmpegPath = Path.Combine(Application.StartupPath, "tools", "ffmpeg", "ffmpeg.exe");
    string outputDir  = Path.Combine(Application.StartupPath, "Captures");

    // Get current render output resolution
    int width  = GetCurrentRenderWidth();   // e.g., 1024
    int height = GetCurrentRenderHeight();  // e.g., 840

    bool ok = VideoRecorder.Start(ffmpegPath, outputDir, width, height);
    if (ok)
    {
        YourAudioEngine.OnSampleReady += VideoRecorder.PushAudioSample;
        UpdateUI("[REC] Recording...");
    }
    else
    {
        MessageBox.Show("Recording failed:\n" + VideoRecorder.LastError);
    }
}
```

#### Hookup Point 4: Lifecycle — Auto-Stop Recording

Call `Stop()` at these points to ensure nothing is missed:

```csharp
// When switching ROM / loading a new game
void LoadNewGame(string romPath)
{
    StopRecordingIfActive();
    // ... load new ROM ...
}

// When the application closes
void OnFormClosing(object sender, FormClosingEventArgs e)
{
    StopRecordingIfActive();
    // ... other cleanup ...
}

// Shared safe-stop method
void StopRecordingIfActive()
{
    if (VideoRecorder.IsRecording)
    {
        YourAudioEngine.OnSampleReady -= VideoRecorder.PushAudioSample;
        VideoRecorder.Stop();
    }
}
```

### 9.2 Complete VideoRecorder Source Code

```csharp
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;

static class VideoRecorder
{
    static Process _ffmpeg;
    static NamedPipeServerStream _videoPipe, _audioPipe;
    static volatile bool _recording;
    static volatile bool _videoPipeConnected;
    static volatile bool _audioPipeConnected;
    static int _frameW, _frameH, _frameSize;

    // Video: triple-buffered frame pool + dedicated writer thread
    static byte[][] _frameBufs;
    static readonly ConcurrentQueue<int> _freeFrames = new ConcurrentQueue<int>();
    static readonly ConcurrentQueue<int> _readyFrames = new ConcurrentQueue<int>();
    static readonly AutoResetEvent _videoSignal = new AutoResetEvent(false);
    static Thread _videoWriterThread;

    // Audio: lock-guarded accumulation buffer + dedicated writer thread
    static short[] _audioBuf;
    static int _audioPos;
    static readonly object _audioLock = new object();
    static Thread _audioWriterThread;

    // FFmpeg stderr capture
    static readonly object _stderrLock = new object();
    static string _stderrBuf;

    public static bool IsRecording => _recording;
    public static string LastOutputPath { get; private set; }
    public static string LastError { get; private set; }

    // ---------- Encoder Detection ----------

    static string _cachedEncoder;

    static string DetectEncoder(string ffmpegPath)
    {
        if (_cachedEncoder != null) return _cachedEncoder;
        string[] candidates = { "h264_nvenc", "h264_amf", "h264_qsv", "h264_d3d12va" };
        foreach (var enc in candidates)
        {
            try
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = string.Format(
                            "-f lavfi -i nullsrc=s=256x240:d=0.1 -c:v {0} -f null -", enc),
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                p.Start();
                p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
                if (p.ExitCode == 0) { _cachedEncoder = enc; return enc; }
            }
            catch { }
        }
        _cachedEncoder = "libx264";
        return _cachedEncoder;
    }

    static string GetEncoderArgs(string encoder)
    {
        switch (encoder)
        {
            case "h264_nvenc":
                return "-c:v h264_nvenc -rc constqp -qp 6 -preset p4 -pix_fmt yuv420p";
            case "h264_amf":
                return "-c:v h264_amf -rc cqp -qp_i 6 -qp_p 6 -quality balanced -pix_fmt yuv420p";
            case "h264_qsv":
                return "-c:v h264_qsv -global_quality 6 -preset faster -pix_fmt yuv420p";
            case "h264_d3d12va":
                return "-c:v h264_d3d12va -rc cqp -qp 6 -pix_fmt yuv420p";
            default:
                return "-c:v libx264 -crf 6 -preset fast -pix_fmt yuv420p";
        }
    }

    // ---------- Start ----------

    public static unsafe bool Start(string ffmpegPath, string outputDir,
                                     int width, int height)
    {
        LastError = null;
        if (_recording) { LastError = "Already recording"; return false; }
        if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
        { LastError = "ffmpeg.exe not found"; return false; }

        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        string outputPath = Path.Combine(outputDir,
            DateTime.Now.ToString("yyyyMMddHHmmss") + ".mp4");

        _frameW = width;
        _frameH = height;
        _frameSize = width * height * 4; // BGRA = 4 bytes/pixel
        _videoPipeConnected = false;
        _audioPipeConnected = false;

        // Init triple-buffer frame pool
        _frameBufs = new byte[3][];
        while (_freeFrames.TryDequeue(out _)) { }
        while (_readyFrames.TryDequeue(out _)) { }
        for (int i = 0; i < 3; i++)
        {
            _frameBufs[i] = new byte[_frameSize];
            _freeFrames.Enqueue(i);
        }

        _audioBuf = new short[441000]; // 10 seconds at 44100 Hz
        _audioPos = 0;

        string encoder = DetectEncoder(ffmpegPath);
        string encoderArgs = GetEncoderArgs(encoder);

        string pid = Process.GetCurrentProcess().Id.ToString();
        string videoPipeName = "myapp_video_" + pid;
        string audioPipeName = "myapp_audio_" + pid;

        int videoBufSize = _frameSize * 2; // Pipe buffer = 2 frames
        int audioBufSize = 65536;

        try
        {
            // ── Step 1: Create pipe servers with large buffers ──
            _videoPipe = new NamedPipeServerStream(videoPipeName, PipeDirection.Out, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, videoBufSize);
            _audioPipe = new NamedPipeServerStream(audioPipeName, PipeDirection.Out, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, audioBufSize);

            // ── Step 2: Begin async wait for FFmpeg to connect ──
            var videoConn = _videoPipe.BeginWaitForConnection(null, null);
            var audioConn = _audioPipe.BeginWaitForConnection(null, null);

            // ── Step 3: Start FFmpeg ──
            string args = string.Format(
                "-y " +
                "-thread_queue_size 2048 -analyzeduration 0 -probesize 32 " +
                "-f rawvideo -pix_fmt bgra -s {0}x{1} -r 60.0988 " +
                "-i \\\\.\\pipe\\{2} " +
                "-thread_queue_size 2048 -analyzeduration 0 -probesize 32 " +
                "-f s16le -ar 44100 -ac 1 " +
                "-i \\\\.\\pipe\\{3} " +
                "{4} " +
                "-c:a aac -b:a 128k -ac 2 " +
                "-movflags +faststart " +
                "-shortest \"{5}\"",
                width, height, videoPipeName, audioPipeName, encoderArgs, outputPath);

            _stderrBuf = "";
            _ffmpeg = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            _ffmpeg.ErrorDataReceived += (s, ev) =>
            {
                if (ev.Data != null)
                    lock (_stderrLock) _stderrBuf += ev.Data + "\n";
            };
            _ffmpeg.Start();
            _ffmpeg.BeginErrorReadLine();

            // ── Step 4: Wait for video pipe ──
            if (!videoConn.AsyncWaitHandle.WaitOne(10000))
                throw new TimeoutException("Video pipe timeout");
            _videoPipe.EndWaitForConnection(videoConn);
            _videoPipeConnected = true;

            // ── Step 5: Dummy frame to satisfy FFmpeg probe ──
            byte[] dummyFrame = new byte[_frameSize];
            _videoPipe.Write(dummyFrame, 0, _frameSize);

            // ── Step 6: Wait for audio pipe ──
            if (!audioConn.AsyncWaitHandle.WaitOne(15000))
                throw new TimeoutException("Audio pipe timeout");
            _audioPipe.EndWaitForConnection(audioConn);
            _audioPipeConnected = true;

            // ── Step 7: Both pipes ready — GO! ──
            LastOutputPath = outputPath;
            _recording = true;
            // ← Hook your audio sample event here

            _videoWriterThread = new Thread(VideoWriterLoop)
                { IsBackground = true, Name = "VR_Video" };
            _videoWriterThread.Start();

            _audioWriterThread = new Thread(AudioWriterLoop)
                { IsBackground = true, Name = "VR_Audio" };
            _audioWriterThread.Start();
        }
        catch (Exception ex)
        {
            string stderr;
            lock (_stderrLock) stderr = _stderrBuf ?? "";
            LastError = ex.Message;
            if (stderr.Length > 0) LastError += "\nFFmpeg: " + stderr;
            if (_recording) { _recording = false; _videoSignal.Set(); }
            try { _videoWriterThread?.Join(3000); } catch { }
            try { _audioWriterThread?.Join(3000); } catch { }
            Cleanup();
            return false;
        }

        return true;
    }

    // ---------- Push A/V Data (called from game thread) ----------

    public static unsafe void PushFrame(uint* screenBuf)
    {
        if (!_recording || !_videoPipeConnected) return;
        int idx;
        if (!_freeFrames.TryDequeue(out idx)) return; // pool full → drop
        fixed (byte* dst = _frameBufs[idx])
            Buffer.MemoryCopy(screenBuf, dst, _frameSize, _frameSize);
        _readyFrames.Enqueue(idx);
        _videoSignal.Set();
    }

    // Call this for every audio sample from your emulator/game
    public static void PushAudioSample(short sample)
    {
        if (!_recording) return;
        lock (_audioLock)
        {
            if (_audioPos < _audioBuf.Length)
                _audioBuf[_audioPos++] = sample;
        }
    }

    // ---------- Writer Threads ----------

    static void VideoWriterLoop()
    {
        while (_recording)
        {
            _videoSignal.WaitOne(50);
            if (!_recording) break;
            int idx;
            while (_readyFrames.TryDequeue(out idx))
            {
                try
                {
                    if (_videoPipeConnected && _videoPipe != null)
                        _videoPipe.Write(_frameBufs[idx], 0, _frameSize);
                }
                catch (IOException) { /* drop frame, continue */ }
                catch { _recording = false; }
                finally { _freeFrames.Enqueue(idx); }
            }
        }
        // Drain remaining
        int rem;
        while (_readyFrames.TryDequeue(out rem))
        {
            try { _videoPipe?.Write(_frameBufs[rem], 0, _frameSize); }
            catch { }
            finally { _freeFrames.Enqueue(rem); }
        }
    }

    static void AudioWriterLoop()
    {
        while (_recording)
        {
            Thread.Sleep(20);
            if (!_recording) break;
            FlushAudio();
        }
        FlushAudio();
    }

    static void FlushAudio()
    {
        if (!_audioPipeConnected || _audioPipe == null) return;
        int count;
        byte[] bytes;
        lock (_audioLock)
        {
            count = _audioPos;
            if (count == 0) return;
            bytes = new byte[count * 2];
            Buffer.BlockCopy(_audioBuf, 0, bytes, 0, bytes.Length);
            _audioPos = 0;
        }
        try { _audioPipe.Write(bytes, 0, bytes.Length); }
        catch (IOException) { /* drop chunk, continue */ }
        catch { }
    }

    // ---------- Stop ----------

    public static void Stop()
    {
        if (!_recording) return;
        _recording = false;
        // ← Unhook your audio sample event here
        _videoSignal.Set();

        try { _videoWriterThread?.Join(5000); } catch { }
        try { _audioWriterThread?.Join(3000); } catch { }

        try { _videoPipe?.Close(); } catch { }
        try { _audioPipe?.Close(); } catch { }

        if (_ffmpeg != null)
        {
            try
            {
                if (!_ffmpeg.WaitForExit(15000))
                    _ffmpeg.Kill();
            }
            catch { }
        }
        Cleanup();
    }

    static void Cleanup()
    {
        try { _videoPipe?.Dispose(); } catch { }
        try { _audioPipe?.Dispose(); } catch { }
        try { _ffmpeg?.Dispose(); } catch { }
        _videoPipe = null;
        _audioPipe = null;
        _ffmpeg = null;
        _videoWriterThread = null;
        _audioWriterThread = null;
        _videoPipeConnected = false;
        _audioPipeConnected = false;
    }
}
```

---

## 10. Five Pitfalls We Actually Hit (War Stories)

These are real problems encountered during AprNes development, listed chronologically:

### Pitfall 1: Game Completely Freezes (Single-Thread Direct Pipe Write)

**Symptom**: After clicking "Start Recording", the entire game window freezes. AprNes becomes completely unresponsive and must be force-killed via Task Manager. The MP4 file is created but contains only a fraction of a second.

**Cause**: The initial version called `_ffmpeg.StandardInput.Write()` directly in the game's render loop. Named Pipe writes are blocking — once FFmpeg can't consume the buffer fast enough, Write() stalls, taking the entire game loop down with it.

**Lesson**: **Never perform any potentially blocking I/O in the game loop.** All pipe writes must go through a dedicated Writer Thread.

---

### Pitfall 2: FFmpeg Probe Deadlock (Audio Pipe Connection Timeout)

**Symptom**: During recording startup, Video Pipe connects successfully, but Audio Pipe times out after 10 seconds. FFmpeg stderr only shows version info and then stalls.

**Cause**: FFmpeg processes multiple inputs **sequentially**. After opening the first Video Pipe, even with `-analyzeduration 0 -probesize 32`, it still needs at least 32 bytes of data to confirm the pipe is alive. But C# is waiting for Audio Pipe to connect, with no thread writing data to Video Pipe.

**Both sides waiting**: FFmpeg waits for Video data ←→ C# waits for Audio connection = perfect deadlock.

**Fix**: After Video Pipe connects, immediately write a black Dummy Frame (`new byte[frameSize]`) from the main thread to satisfy FFmpeg's probe, which then immediately opens the Audio Pipe.

---

### Pitfall 3: Video Is Less Than One Second (Single Writer Thread Starves Audio)

**Symptom**: Recording starts normally (both pipes connect), but the output MP4 is only 0.19 seconds long with 11 frames. FFmpeg stderr shows `frame=0` for 97 seconds straight.

**Cause**: A single Writer Thread handled both Video and Audio. Video Pipe Write() pushes 3.4MB per frame (1024×840 BGRA) — with an undersized pipe buffer, it blocks. Once Video Write blocks, `FlushAudio()` in the same thread never executes.

FFmpeg needs both streams flowing to advance the timeline, but Audio is completely starved → FFmpeg can't advance → Video pipe buffer fills up → another deadlock.

During 97 seconds of "recording", FFmpeg encoded zero frames. Only when Stop() closed the pipes did the few remaining buffered frames get processed.

**Fix**: Use independent Writer Threads for Video and Audio — one blocking absolutely cannot affect the other. Also increase pipe outBufferSize (video = 2 frames ≈ 6.8MB).

---

### Pitfall 4: Severe A/V Desync

**Symptom**: Video now records at full length, but audio is noticeably ahead of video (or the audio opening is missing).

**Cause**: Two problems stacked:

1. **Audio buffer too small**: `_audioBuf = new short[8192]` only holds 0.18 seconds. During the 0.5–2 second wait for Audio Pipe connection, the emulator keeps generating audio samples. Once past 8192, samples are silently dropped. Video has a complete opening, but the audio opening is gone.

2. **Misaligned starting line**: `_recording = true` was set immediately after Video Pipe connected, but Audio Pipe needed another 0.5–2 seconds. During this gap, Video is already sending frames. By the time Audio starts, FFmpeg's Muxer already has a time offset.

**Fix**:
- Increase audio buffer to 441000 (10 seconds capacity)
- Move `_recording = true` and `AudioSampleReady += OnAudioSample` to execute only after BOTH pipes are connected

---

### Pitfall 5: Writer Thread Premature Death (The Standby Trap)

**Symptom**: After fixing Pitfall 4, we're back to Pitfall 2's symptom — Audio Pipe Timeout.

**Cause**: To align the starting line, we moved `_recording = true` to Step 7 (after both pipes connect). But the Video Writer Thread was started at Step 5 (intending it to standby). Problem: `VideoWriterLoop()`'s first line is `while (_recording)`, and at that point `_recording = false` — the thread exits immediately.

Result: back to Pitfall 2 — no thread feeding Video data to FFmpeg → FFmpeg won't open Audio Pipe → Timeout.

**Fix**: Don't start any thread on standby. Instead, write a Dummy Frame on the main thread after Video Pipe connects, wait for Audio Pipe, then set `_recording = true` and start both threads (at which point `_recording` is already true).

---

## 11. Pre-Launch Checklist

- [ ] `NamedPipeServerStream` has a large `outBufferSize` specified (video ≥ 1 frame size)
- [ ] `PipeOptions.Asynchronous` is set
- [ ] FFmpeg args include `-thread_queue_size 2048` (before each `-i`)
- [ ] FFmpeg args include `-analyzeduration 0 -probesize 32` (before each `-i`)
- [ ] FFmpeg args include `-movflags +faststart`
- [ ] Step 5 sends a Dummy Frame
- [ ] `_recording = true` is set only **after both Pipes are connected**
- [ ] Writer Threads start only **after** `_recording = true`
- [ ] Video and Audio use **independent Writer Threads**
- [ ] Audio buffer is large enough (recommend ≥ 10 seconds = sample rate × 10)
- [ ] `IOException` only drops a frame/chunk, **does not kill** recording
- [ ] `Stop()` order: `_recording=false` → join threads → `Close()` pipes → `WaitForExit()` FFmpeg
- [ ] Game loop's `PushFrame()` only does memcpy, **no** Pipe I/O
- [ ] ROM switch and app close both call `Stop()`
- [ ] Logging mechanism exists for debugging

---

## FFmpeg Key Parameters Quick Reference

```
-y                          Overwrite output file
-thread_queue_size 2048     Input buffer queue size (prevents blocking)
-analyzeduration 0          Skip format probing (format is already known)
-probesize 32               Minimum probe size (just confirm the pipe is alive)
-f rawvideo                 Video format: uncompressed raw frames
-pix_fmt bgra               Pixel format: Blue-Green-Red-Alpha
-s 1024x840                 Video resolution
-r 60.0988                  Frame rate (NES standard: 60.0988 fps)
-f s16le                    Audio format: signed 16-bit little-endian PCM
-ar 44100                   Sample rate
-ac 1                       Input channel count (mono)
-c:v libx264 -crf 6         Video codec: H.264, CRF 6 (near-lossless quality)
-c:a aac -b:a 128k -ac 2    Audio codec: AAC 128kbps stereo
-movflags +faststart         Move MOOV atom to file start (enables streaming)
-shortest                   Use shortest stream as duration (prevents A/V length mismatch)
```

---

*This document is based on real development experience from the AprNes NES emulator, 2026-03-25*
