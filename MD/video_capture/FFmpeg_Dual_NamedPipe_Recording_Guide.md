# C# + FFmpeg 雙 Named Pipe 遊戲錄影完整實作教學

> **適用環境**: Windows / C# (.NET Framework 4.x 或 .NET 6+) / FFmpeg CLI
> **最終成果**: 即時錄製遊戲畫面 + 聲音 → H.264/AAC MP4 檔案
> **本文來自**: AprNes (NES 模擬器) 的實戰開發經驗，包含我們踩過的每一個坑

---

## 目錄

1. [為什麼選擇 FFmpeg CLI + Named Pipe？](#1-為什麼選擇-ffmpeg-cli--named-pipe)
2. [整體架構概覽](#2-整體架構概覽)
3. [七步啟動流程（含五個致命陷阱）](#3-七步啟動流程含五個致命陷阱)
4. [影像端：Triple-Buffer Frame Pool](#4-影像端triple-buffer-frame-pool)
5. [音訊端：Lock-Guarded 累積緩衝區](#5-音訊端lock-guarded-累積緩衝區)
6. [雙 Writer Thread 架構](#6-雙-writer-thread-架構)
7. [安全停止錄影與 MP4 收尾](#7-安全停止錄影與-mp4-收尾)
8. [H.264 硬體編碼器自動偵測](#8-h264-硬體編碼器自動偵測)
9. [完整程式碼](#9-完整程式碼)
10. [我們踩過的五個大坑（血淚史）](#10-我們踩過的五個大坑血淚史)
11. [Checklist：上線前必檢清單](#11-checklist上線前必檢清單)

---

## 1. 為什麼選擇 FFmpeg CLI + Named Pipe？

### OBS / RetroArch 怎麼做的？

它們直接透過 C/C++ 呼叫 FFmpeg 的底層動態連結庫（libavformat, libavcodec 等 API），在記憶體中自己封裝 A/V 封包再寫入檔案。

對 C# 開發者來說，如果走這條路（例如用 FFmpeg.AutoGen），開發成本會暴增數倍，且容易遇到記憶體外洩。**堅持用 CLI (ffmpeg.exe) 是 C# 開發者最聰明、最划算的選擇。**

### 為什麼不用 stdin？

如果用 stdin 傳影像、Named Pipe 傳音訊（或反過來），A/V 的非同步邏輯會變得混亂。而且 stdin 只能有一條，無法同時送兩路生肉資料。

如果想在 stdin 裡混合 A/V，就必須自己實作容器格式（NUT、FLV 等）的 Muxer，那簡直是拿石頭砸自己的腳。

**結論：開兩條 Named Pipe（video + audio），交給 FFmpeg 去 Mux，是最乾淨的架構。**

---

## 2. 整體架構概覽

```
┌──────────────┐
│  遊戲主迴圈    │
│  (模擬器核心)  │
│              │
│  每幀產生:     │
│  · 畫面 BGRA  │──→ PushFrame() ──→ [Triple Buffer Queue]
│  · 音訊 PCM   │──→ OnAudioSample() → [Audio Accumulation Buffer]
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

### 核心設計原則

| 原則 | 說明 |
|------|------|
| **遊戲線程永不阻塞** | PushFrame() 只做 memcpy 到 pool buffer，O(1) 操作 |
| **Video / Audio 互不阻塞** | 各自獨立的 Writer Thread，一方卡住不影響另一方 |
| **IOException 容錯** | Pipe 寫入暫時失敗只掉幀，不中斷整個錄影 |
| **乾淨的 EOF 收尾** | Close() pipe → FFmpeg 寫入 MOOV atom → 有效 MP4 |

---

## 3. 七步啟動流程（含五個致命陷阱）

這是整個實作中最複雜的部分。我們在這裡踩了無數的坑，最終歸結出以下七步流程：

```
Step 1: 建立兩個 NamedPipeServerStream（大 buffer）
Step 2: BeginWaitForConnection()（非同步等待 FFmpeg 連入）
Step 3: 啟動 ffmpeg.exe 子進程
Step 4: 等待 Video Pipe 連線成功
Step 5: 送一張 Dummy Frame（餵飽 FFmpeg Probe）   ← 關鍵！
Step 6: 等待 Audio Pipe 連線成功
Step 7: _recording = true → 啟動雙 Writer Thread   ← 同時起跑！
```

### Step 1: 建立 Pipe — 必須指定大 Buffer

```csharp
int videoBufSize = frameSize * 2;  // 2 幀的容量（例如 1024×840×4×2 ≈ 6.8MB）
int audioBufSize = 65536;          // 64KB

_videoPipe = new NamedPipeServerStream(videoPipeName, PipeDirection.Out, 1,
    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, videoBufSize);
_audioPipe = new NamedPipeServerStream(audioPipeName, PipeDirection.Out, 1,
    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, audioBufSize);
```

> **陷阱 #1：預設 Pipe Buffer 太小**
> 如果不指定 outBufferSize，Windows Named Pipe 預設 buffer 約 4KB。
> 一張 1024×840 BGRA 的影像是 3.4MB，Write() 需要 ~860 次 kernel round-trip，
> 寫入會嚴重阻塞。加大 buffer 後，整幀可以一次性寫入 pipe buffer。

### Step 2 & 3: 先 BeginWaitForConnection，再啟動 FFmpeg

```csharp
var videoConnectResult = _videoPipe.BeginWaitForConnection(null, null);
var audioConnectResult = _audioPipe.BeginWaitForConnection(null, null);

// FFmpeg 作為 client 連入兩個 pipe
_ffmpeg = new Process { ... };
_ffmpeg.Start();
```

> **陷阱 #2：順序不能反**
> 必須先 BeginWaitForConnection() 再 Start FFmpeg。
> 如果先啟動 FFmpeg，它嘗試連線時 pipe server 還不存在，會直接報錯退出。

### Step 4: 等待 Video Pipe 連線

```csharp
if (!videoConnectResult.AsyncWaitHandle.WaitOne(10000))
    throw new TimeoutException("FFmpeg did not connect to video pipe");
_videoPipe.EndWaitForConnection(videoConnectResult);
```

### Step 5: 送 Dummy Frame（最關鍵的一步！）

```csharp
byte[] dummyFrame = new byte[_frameSize]; // 全黑
_videoPipe.Write(dummyFrame, 0, _frameSize);
```

> **陷阱 #3：FFmpeg Probe 死結**
> 即使加了 `-analyzeduration 0 -probesize 32`，FFmpeg **仍然需要讀到至少 32 bytes**
> 的影像資料才會確認第一條輸入管線是活的，然後才會去開啟第二條 Audio 管線。
>
> 如果你不餵資料，FFmpeg 就痴痴等在 Video Pipe，永遠不去連 Audio Pipe。
> 而 C# 端正卡在 `audioConnectResult.WaitOne()` 等 FFmpeg 連 Audio Pipe。
> **雙方互等 = 完美死結。**
>
> 解法：在 Video Pipe 連上後，立刻從主線程寫一張全黑的 Dummy Frame。
> FFmpeg 吃到資料後，馬上就去開 Audio Pipe 了。

### Step 6: 等待 Audio Pipe 連線

```csharp
if (!audioConnectResult.AsyncWaitHandle.WaitOne(15000))
    throw new TimeoutException("FFmpeg did not connect to audio pipe");
_audioPipe.EndWaitForConnection(audioConnectResult);
```

### Step 7: 同時起跑

```csharp
_recording = true;
NesCore.AudioSampleReady += OnAudioSample;

_videoWriterThread = new Thread(VideoWriterLoop) { IsBackground = true };
_videoWriterThread.Start();

_audioWriterThread = new Thread(AudioWriterLoop) { IsBackground = true };
_audioWriterThread.Start();
```

> **陷阱 #4：起跑線不對齊 → 影音不同步**
> 如果在 Audio Pipe 連上之前就設 `_recording = true` 並開始收集 A/V 資料，
> Video 會先跑一段，Audio 晚到後 FFmpeg 的 Muxer 會產生時間軸偏移。
>
> 必須等 **兩條 Pipe 都連上** 才能同時開始送資料。

> **陷阱 #5：Writer Thread 提早死亡**
> 如果在 `_recording = false` 的狀態下就啟動 Writer Thread，
> `while (_recording)` 迴圈會立刻退出，Thread 瞬間死掉。
> 然後即使後來 `_recording` 變成 true 也沒有 Thread 在跑了。
>
> 必須先設 `_recording = true`，**再**啟動 Thread。

---

## 4. 影像端：Triple-Buffer Frame Pool

遊戲主迴圈每幀呼叫一次 `PushFrame()`，將當前畫面推入 pool：

```csharp
static byte[][] _frameBufs;                                    // 3 個 buffer
static readonly ConcurrentQueue<int> _freeFrames  = new ...;  // 空閒 index
static readonly ConcurrentQueue<int> _readyFrames = new ...;  // 待寫 index

public static unsafe void PushFrame(uint* screenBuf)
{
    if (!_recording || !_videoPipeConnected) return;

    int idx;
    if (!_freeFrames.TryDequeue(out idx)) return; // Pool 滿了就掉幀

    fixed (byte* dst = _frameBufs[idx])
        Buffer.MemoryCopy(screenBuf, dst, _frameSize, _frameSize);

    _readyFrames.Enqueue(idx);
    _videoSignal.Set(); // 喚醒 Writer Thread
}
```

### 為什麼用 Triple-Buffer？

| Buffer 數量 | 行為 |
|-------------|------|
| 1 (Single) | 遊戲線程和 Writer 搶同一塊 → 需要 lock → 遊戲卡頓 |
| 2 (Double) | Writer 寫舊的、遊戲寫新的 → 但如果 Writer 慢了就掉幀 |
| 3 (Triple) | 遊戲永遠能拿到空 buffer、Writer 永遠有東西寫 → 最平衡 |

遊戲線程只做 `TryDequeue` + `memcpy` + `Enqueue`，全部都是 O(1) 無鎖操作，**絕對不會阻塞遊戲主迴圈**。

---

## 5. 音訊端：Lock-Guarded 累積緩衝區

NES 模擬器的音訊是逐 sample 產生的（44100 Hz，每秒 44100 個 short）：

```csharp
static short[] _audioBuf;  // 441000 = 10 秒容量
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

### 為什麼 buffer 要開 10 秒這麼大？

啟動時，Video Pipe 連上到 Audio Pipe 連上之間可能有 0.5~2 秒的延遲。如果 buffer 只有 8192 個 sample（0.18 秒），在等待期間音訊資料會溢出被丟棄，導致開頭音訊缺失 → **影音不同步**。

441000 samples = 10 秒的容量，足以應付任何合理的啟動延遲。

---

## 6. 雙 Writer Thread 架構

**這是最終解決方案中最重要的設計決策。**

### 為什麼不能用單一 Writer Thread？

我們最初的設計是一個 Writer Thread 同時處理 Video 和 Audio：

```csharp
// ❌ 錯誤設計：單一 Writer Thread
while (_recording)
{
    // 寫 video
    while (_readyFrames.TryDequeue(out idx))
        _videoPipe.Write(_frameBufs[idx], 0, _frameSize); // ← 這裡阻塞！

    // 寫 audio — 但如果上面阻塞了，這裡永遠執行不到
    FlushAudio();
}
```

Video Pipe 的 Write() 要寫 3.4MB 的資料，即使有大 buffer 也可能偶爾阻塞。一旦阻塞，Audio 的 FlushAudio() 就沒機會執行。FFmpeg 需要兩路資料同時流入才能推進（因為 `-shortest` 參數），但 Audio 被餓死 → FFmpeg 也卡住不讀 Video → Video Write() 繼續阻塞 → **又一次死結**。

### 正確設計：獨立的雙 Thread

```csharp
// Video Writer Thread — 只管 video
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
                // 掉幀，不中斷錄影
            }
            catch (Exception)
            {
                _recording = false; // 致命錯誤才停止
            }
            finally { _freeFrames.Enqueue(idx); }
        }
    }
}

// Audio Writer Thread — 只管 audio，完全獨立
static void AudioWriterLoop()
{
    while (_recording)
    {
        Thread.Sleep(20); // ~50 次/秒
        if (!_recording) break;
        FlushAudio();
    }
    FlushAudio(); // 最後一次 flush
}
```

兩個 Thread 各自獨立運作，**一方阻塞絕對不影響另一方**。

### IOException 容錯

Pipe 在寫入時偶爾會因為 FFmpeg 內部重整 Queue 而產生短暫的 IOException。如果因為一次 IOException 就設 `_recording = false`，整個錄影就廢了。

正確做法：**IOException = 掉一幀，繼續錄。只有非 IO 的致命錯誤才停止。**

---

## 7. 安全停止錄影與 MP4 收尾

```csharp
public static void Stop()
{
    if (!_recording) return;
    _recording = false;

    // 1. 停止收集新資料
    NesCore.AudioSampleReady -= OnAudioSample;
    _videoSignal.Set();

    // 2. 等待 Writer Thread 排空剩餘資料
    _videoWriterThread?.Join(5000);
    _audioWriterThread?.Join(3000);

    // 3. 關閉 Pipe → FFmpeg 收到 EOF
    _videoPipe?.Close();
    _audioPipe?.Close();

    // 4. 等待 FFmpeg 完成 MP4 收尾（寫入 MOOV atom）
    if (!_ffmpeg.WaitForExit(15000))
        _ffmpeg.Kill(); // 超時強制終止
}
```

### 為什麼 MP4 檔案會壞掉？

MP4 的元資料（MOOV atom）在**檔案最後**才寫入。如果 FFmpeg 沒有收到正確的 EOF 訊號就被強制關閉，MOOV atom 不會被寫入，檔案就無法播放。

**必須正確呼叫 `pipe.Close()`**，讓 FFmpeg 知道串流結束了，它才會正常收尾。

加上 `-movflags +faststart` 可以讓 FFmpeg 在寫完後把 MOOV atom 搬到檔案開頭，方便網路串流播放。

---

## 8. H.264 硬體編碼器自動偵測

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
        if (p.ExitCode == 0) return enc; // 這個硬體編碼器可用
    }
    return "libx264"; // 全部失敗，回退到 CPU 軟編碼
}
```

| 編碼器 | GPU 廠商 | 說明 |
|--------|---------|------|
| h264_nvenc | NVIDIA | 最常見，GeForce GTX 600+ |
| h264_amf | AMD | Radeon RX 400+ |
| h264_qsv | Intel | 內顯 Quick Sync Video |
| h264_d3d12va | 通用 | DirectX 12 Video Acceleration |
| libx264 | 無 (CPU) | 最後的保底方案 |

偵測結果會被快取（`_cachedEncoder`），後續錄影不需要重新偵測。

---

## 9. 完整程式碼

以下是經過五輪迭代後的最終版本（可直接使用）：

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

## 10. 我們踩過的五個大坑（血淚史）

以下是 AprNes 開發過程中實際遇到的問題，按時間順序排列：

### 坑 1：遊戲直接凍結（單線程直寫 Pipe）

**症狀**：按下「開始錄影」後，整個遊戲畫面凍住，AprNes 完全無回應，必須用工作管理員強制結束。雖然 MP4 檔案有產生，但只有零點幾秒的內容。

**原因**：最初的版本在遊戲的渲染迴圈中直接呼叫 `_ffmpeg.StandardInput.Write()` 寫入影像資料。Named Pipe 的寫入是阻塞式的，一旦 FFmpeg 來不及消化 buffer，Write() 就會卡住，連帶把整個遊戲主迴圈給凍結。

**教訓**：**永遠不要在遊戲主迴圈中做任何可能阻塞的 I/O 操作。** 必須用獨立的 Writer Thread 處理所有 Pipe 寫入。

---

### 坑 2：FFmpeg Probe 死結（Audio Pipe 連線 Timeout）

**症狀**：錄影啟動時，Video Pipe 順利連上，但 Audio Pipe 在等待 10 秒後 Timeout。FFmpeg 的 stderr 只印出版本資訊就停住了。

**原因**：FFmpeg 處理多重輸入時，會**依序**開啟每個 `-i`。開啟第一個 Video Pipe 後，即使加了 `-analyzeduration 0 -probesize 32`，它仍需要讀到至少 32 bytes 的資料來確認管線是活的。但 C# 端此時正在等 Audio Pipe 連線，還沒有任何 Thread 往 Video Pipe 寫資料。

**雙方互等**：FFmpeg 等 Video 資料 ←→ C# 等 Audio 連線 = 完美死結。

**解法**：Video Pipe 連上後，在主線程立刻寫一張全黑的 Dummy Frame（`new byte[frameSize]`），餵飽 FFmpeg 的 Probe，它就會馬上去開 Audio Pipe。

---

### 坑 3：影片只有不到一秒（單 Writer Thread 餓死 Audio）

**症狀**：錄影可以正常啟動（兩個 Pipe 都連上了），但產出的 MP4 只有 0.19 秒、11 幀。FFmpeg stderr 顯示 `frame=0` 長達 97 秒。

**原因**：使用單一 Writer Thread 同時處理 Video 和 Audio。Video Pipe 的 Write() 每次要寫 3.4MB（1024×840 BGRA），在 Pipe buffer 不夠大的情況下會阻塞。一旦 Video Write 卡住，同一個 Thread 裡的 `FlushAudio()` 就永遠執行不到。

FFmpeg 需要兩路資料同時流入才能推進時間軸，但 Audio 被完全餓死 → FFmpeg 也無法推進 → Video 的 Pipe buffer 也滿了 → 又是死結。

97 秒的錄影期間 FFmpeg 一幀都沒編碼成功，最後 Stop() 關閉 Pipe 時殘餘 buffer 裡的幾幀才被處理。

**解法**：Video 和 Audio 各用獨立的 Writer Thread，一方阻塞絕不影響另一方。同時加大 Pipe 的 outBufferSize（video = 2 幀 ≈ 6.8MB）。

---

### 坑 4：影音嚴重不同步

**症狀**：影片可以錄製完整長度了，但聲音明顯比畫面快（或開頭聲音缺失）。

**原因**：兩個問題疊加：

1. **音訊緩衝區太小**：`_audioBuf = new short[8192]` 只能存 0.18 秒。在等待 Audio Pipe 連線的 0.5~2 秒期間，模擬器持續產生音訊 sample，超過 8192 後就被丟棄。影片有完整的開頭畫面，但音訊的開頭不見了。

2. **起跑線不對齊**：`_recording = true` 在 Video Pipe 連上後就立刻設定了，但 Audio Pipe 還要等 0.5~2 秒才連上。這段期間 Video 已經在送幀，等 Audio 開始時，FFmpeg 的 Muxer 已經有了時間差。

**解法**：
- Audio buffer 加大到 441000（10 秒容量）
- `_recording = true` 和 `AudioSampleReady += OnAudioSample` 都移到兩條 Pipe 全部連上之後才執行

---

### 坑 5：Writer Thread 提早死亡（Standby 陷阱）

**症狀**：修完坑 4 後，又回到坑 2 的症狀 — Audio Pipe Timeout。

**原因**：為了對齊起跑線，我們把 `_recording = true` 移到了 Step 7（兩管都通後）。但 Video Writer Thread 在 Step 5 就啟動了（想讓它 standby）。問題是 `VideoWriterLoop()` 的第一行是 `while (_recording)`，而此時 `_recording = false`，Thread 立刻退出。

結果回到了坑 2：沒有 Thread 餵 Video 資料給 FFmpeg → FFmpeg 不開 Audio Pipe → Timeout。

**解法**：不啟動任何 Thread standby。改用 Dummy Frame：Video Pipe 連上後在主線程直接 Write 一張全黑幀，然後等 Audio Pipe，最後才啟動雙 Thread（此時 `_recording` 已經是 true）。

---

## 11. Checklist：上線前必檢清單

- [ ] `NamedPipeServerStream` 有指定大 `outBufferSize`（video ≥ 1 幀大小）
- [ ] `PipeOptions.Asynchronous` 已設定
- [ ] FFmpeg 參數包含 `-thread_queue_size 2048`（每個 `-i` 前）
- [ ] FFmpeg 參數包含 `-analyzeduration 0 -probesize 32`（每個 `-i` 前）
- [ ] FFmpeg 參數包含 `-movflags +faststart`
- [ ] Step 5 有送 Dummy Frame
- [ ] `_recording = true` 在 **兩條 Pipe 都連上之後** 才設定
- [ ] Writer Thread 在 `_recording = true` **之後** 才啟動
- [ ] Video 和 Audio 是 **獨立的 Writer Thread**
- [ ] 音訊緩衝區夠大（建議 ≥ 10 秒 = 取樣率 × 10）
- [ ] `IOException` 只掉幀/掉 chunk，**不中斷**錄影
- [ ] `Stop()` 的順序：`_recording=false` → join threads → `Close()` pipes → `WaitForExit()` FFmpeg
- [ ] 遊戲主迴圈的 `PushFrame()` 只做 memcpy，**不做** Pipe I/O
- [ ] ROM 切換、程式關閉時有呼叫 `Stop()`
- [ ] 有 Log 機制方便除錯

---

## FFmpeg 關鍵參數速查

```
-y                          覆寫輸出檔案
-thread_queue_size 2048     輸入緩衝佇列大小（防阻塞）
-analyzeduration 0          跳過格式探測（已知格式不需要）
-probesize 32               最小探測量（只需確認管線活著）
-f rawvideo                 影像格式：未壓縮原始畫面
-pix_fmt bgra               像素格式：Blue-Green-Red-Alpha
-s 1024x840                 影像解析度
-r 60.0988                  幀率（NES 標準：60.0988 fps）
-f s16le                    音訊格式：有號 16-bit 小端序 PCM
-ar 44100                   取樣率
-ac 1                       輸入聲道數（單聲道）
-c:v libx264 -crf 6         視訊編碼：H.264, CRF 6（近無損品質）
-c:a aac -b:a 128k -ac 2    音訊編碼：AAC 128kbps 立體聲
-movflags +faststart         MOOV atom 移到檔案開頭（利於串流）
-shortest                   以最短的串流為準（防止 A/V 長度不一）
```

---

*本文件基於 AprNes NES 模擬器的實際開發經驗撰寫，2026-03-25*
