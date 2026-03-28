using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace AprNes
{
    /// <summary>
    /// Single FFmpeg process with dual named pipes (video + audio).
    /// Separate video/audio writer threads — neither can block the other.
    /// </summary>
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

        static string _logPath;

        public static bool IsRecording => _recording;
        public static string LastOutputPath { get; private set; }
        public static string DetectedEncoder { get; private set; }
        public static string LastError { get; private set; }

        static readonly bool DebugLog = true; // set false for release

        static void Log(string msg)
        {
            if (!DebugLog) return;
            try
            {
                if (_logPath == null)
                {
                    string dir = Path.Combine(Path.GetDirectoryName(
                        System.Reflection.Assembly.GetExecutingAssembly().Location), "Captures", "Video");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    _logPath = Path.Combine(dir, "VideoRecorder.log");
                }
                File.AppendAllText(_logPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + msg + "\r\n");
            }
            catch { }
        }

        static string _cachedEncoder;

        static string DetectEncoder(string ffmpegPath)
        {
            if (_cachedEncoder != null) return _cachedEncoder;
            Log("DetectEncoder: probing HW encoders...");
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
                    if (p.ExitCode == 0)
                    {
                        Log("DetectEncoder: selected " + enc);
                        _cachedEncoder = enc; return enc;
                    }
                    else
                        Log("DetectEncoder: " + enc + " exit=" + p.ExitCode);
                }
                catch (Exception ex) { Log("DetectEncoder: " + enc + " exception: " + ex.Message); }
            }
            Log("DetectEncoder: fallback to libx264");
            _cachedEncoder = "libx264";
            return _cachedEncoder;
        }

        /// <summary>Video quality: 90, 80, 70, 60 (percent). Maps to qp/crf values.</summary>
        public static int VideoQuality = 90;

        static int QualityToQp(int quality)
        {
            switch (quality)
            {
                case 90: return 4;
                case 80: return 8;
                case 70: return 14;
                default: return 20; // 60%
            }
        }

        static string GetEncoderArgs(string encoder)
        {
            int qp = QualityToQp(VideoQuality);
            switch (encoder)
            {
                case "h264_nvenc":
                    return string.Format("-c:v h264_nvenc -rc constqp -qp {0} -preset p4 -pix_fmt yuv420p", qp);
                case "h264_amf":
                    return string.Format("-c:v h264_amf -rc cqp -qp_i {0} -qp_p {0} -quality balanced -pix_fmt yuv420p", qp);
                case "h264_qsv":
                    return string.Format("-c:v h264_qsv -global_quality {0} -preset faster -pix_fmt yuv420p", qp);
                case "h264_d3d12va":
                    return string.Format("-c:v h264_d3d12va -rc cqp -qp {0} -pix_fmt yuv420p", qp);
                default:
                    return string.Format("-c:v libx264 -crf {0} -preset fast -pix_fmt yuv420p", qp);
            }
        }

        public static unsafe bool Start(string ffmpegPath, string outputDir, int width, int height)
        {
            LastError = null;
            Log("========== Start() called ==========");
            Log("ffmpegPath=" + (ffmpegPath ?? "(null)") + "  exists=" + (!string.IsNullOrEmpty(ffmpegPath) && File.Exists(ffmpegPath)));
            Log("outputDir=" + outputDir + "  size=" + width + "x" + height);
            if (_recording) { LastError = "Already recording"; Log("ABORT: " + LastError); return false; }
            if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
            { LastError = "ffmpeg.exe not found: " + (ffmpegPath ?? "(null)"); Log("ABORT: " + LastError); return false; }

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            string fileName = DateTime.Now.ToString("yyyyMMddHHmmss") + ".mp4";
            string outputPath = Path.Combine(outputDir, fileName);

            _frameW = width;
            _frameH = height;
            _frameSize = width * height * 4;
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

            _audioBuf = new short[882000]; // 10 seconds at 44100 Hz stereo (L,R interleaved)
            _audioPos = 0;

            string encoder = DetectEncoder(ffmpegPath);
            DetectedEncoder = encoder;
            string encoderArgs = GetEncoderArgs(encoder);

            string pid = Process.GetCurrentProcess().Id.ToString();
            string videoPipeName = "aprnes_video_" + pid;
            string audioPipeName = "aprnes_audio_" + pid;

            // Pipe buffer: video = 2 frames, audio = 64KB
            int videoBufSize = _frameSize * 2;
            int audioBufSize = 65536;

            try
            {
                // Step 1: Create both pipe servers with large buffers
                _videoPipe = new NamedPipeServerStream(videoPipeName, PipeDirection.Out, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, videoBufSize);
                _audioPipe = new NamedPipeServerStream(audioPipeName, PipeDirection.Out, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, audioBufSize);

                Log("Pipes created: " + videoPipeName + " (buf=" + videoBufSize + "), " +
                    audioPipeName + " (buf=" + audioBufSize + ")");

                // Step 2: Begin waiting for FFmpeg to connect (async)
                var videoConnectResult = _videoPipe.BeginWaitForConnection(null, null);
                var audioConnectResult = _audioPipe.BeginWaitForConnection(null, null);

                // Step 3: Start FFmpeg
                string args = string.Format(
                    "-y " +
                    "-thread_queue_size 2048 -analyzeduration 0 -probesize 32 -f rawvideo -pix_fmt bgra -s {0}x{1} -r 60.0988 -i \\\\.\\pipe\\{2} " +
                    "-thread_queue_size 2048 -analyzeduration 0 -probesize 32 -f s16le -ar 44100 -ac 2 -i \\\\.\\pipe\\{3} " +
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
                Log("FFmpeg started PID=" + _ffmpeg.Id + "  args=" + args);

                // Step 4: Wait for video pipe
                Log("Waiting for video pipe connection...");
                if (!videoConnectResult.AsyncWaitHandle.WaitOne(10000))
                    throw new TimeoutException("FFmpeg did not connect to video pipe within 10s");
                _videoPipe.EndWaitForConnection(videoConnectResult);
                Log("Video pipe connected");

                // Step 5: Feed dummy frame to satisfy FFmpeg probe (needs ≥32 bytes)
                _videoPipeConnected = true;
                byte[] dummyFrame = new byte[_frameSize];
                _videoPipe.Write(dummyFrame, 0, _frameSize);
                Log("Dummy frame sent to satisfy FFmpeg probe");

                // Step 6: Wait for audio pipe (FFmpeg opens it after probing video)
                Log("Waiting for audio pipe connection...");
                if (!audioConnectResult.AsyncWaitHandle.WaitOne(15000))
                    throw new TimeoutException("FFmpeg did not connect to audio pipe within 15s");
                _audioPipe.EndWaitForConnection(audioConnectResult);
                _audioPipeConnected = true;
                Log("Audio pipe connected");

                // Step 7: Both pipes ready — set recording flag, then start both threads
                LastOutputPath = outputPath;
                _recording = true;
                NesCore.AudioSampleReady += OnAudioSample;

                _videoWriterThread = new Thread(VideoWriterLoop) { IsBackground = true, Name = "VR_Video" };
                _videoWriterThread.Start();
                Log("Video writer thread started");

                _audioWriterThread = new Thread(AudioWriterLoop) { IsBackground = true, Name = "VR_Audio" };
                _audioWriterThread.Start();
                Log("Audio writer thread started");
            }
            catch (Exception ex)
            {
                string stderr;
                lock (_stderrLock) stderr = _stderrBuf ?? "";
                LastError = ex.Message;
                if (stderr.Length > 0)
                    LastError += "\n\nFFmpeg stderr:\n" + stderr;
                Log("START FAILED: " + ex.GetType().Name + ": " + ex.Message);
                if (stderr.Length > 0) Log("FFmpeg stderr:\n" + stderr);
                if (_recording) { _recording = false; _videoSignal.Set(); }
                try { _videoWriterThread?.Join(3000); } catch { }
                try { _audioWriterThread?.Join(3000); } catch { }
                NesCore.AudioSampleReady -= OnAudioSample;
                Cleanup();
                return false;
            }

            Log("Recording started → " + outputPath);
            return true;
        }

        public static unsafe void PushFrame(uint* screenBuf)
        {
            if (!_recording || !_videoPipeConnected) return;
            int idx;
            if (!_freeFrames.TryDequeue(out idx)) return;
            fixed (byte* dst = _frameBufs[idx])
                Buffer.MemoryCopy(screenBuf, dst, _frameSize, _frameSize);
            _readyFrames.Enqueue(idx);
            _videoSignal.Set();
        }

        static void OnAudioSample(short left, short right)
        {
            if (!_recording) return;
            lock (_audioLock)
            {
                if (_audioPos + 1 < _audioBuf.Length)
                {
                    _audioBuf[_audioPos++] = left;
                    _audioBuf[_audioPos++] = right;
                }
            }
        }

        // Dedicated video writer — only writes to _videoPipe
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
                    catch (IOException ex)
                    {
                        Log("[Warning] Video pipe IOException: " + ex.Message);
                        // Drop frame, continue recording
                    }
                    catch (Exception ex)
                    {
                        Log("[Error] Video pipe fatal: " + ex.Message);
                        _recording = false;
                    }
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

        // Dedicated audio writer — runs independently, never blocked by video
        static void AudioWriterLoop()
        {
            while (_recording)
            {
                Thread.Sleep(20); // ~50 flushes/sec, well above audio rate
                if (!_recording) break;
                FlushAudio();
            }
            // Final flush
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
            try
            {
                _audioPipe.Write(bytes, 0, bytes.Length);
            }
            catch (IOException ex)
            {
                Log("[Warning] Audio pipe IOException: " + ex.Message);
            }
            catch (Exception ex)
            {
                Log("[Error] Audio pipe fatal: " + ex.Message);
            }
        }

        public static void Stop()
        {
            if (!_recording) return;
            Log("Stop() called");
            _recording = false;

            NesCore.AudioSampleReady -= OnAudioSample;
            _videoSignal.Set();

            // Wait for both writer threads to drain
            try { _videoWriterThread?.Join(5000); } catch { }
            try { _audioWriterThread?.Join(3000); } catch { }
            Log("Writer threads joined");

            // Close pipes → FFmpeg receives EOF → finalizes MP4
            try { _videoPipe?.Close(); } catch { }
            try { _audioPipe?.Close(); } catch { }
            Log("Pipes closed (EOF sent)");

            // Wait for FFmpeg to finish
            if (_ffmpeg != null)
            {
                try
                {
                    if (!_ffmpeg.WaitForExit(15000))
                    {
                        Log("FFmpeg did not exit in 15s, killing");
                        _ffmpeg.Kill();
                    }
                    else
                        Log("FFmpeg exited normally, code=" + _ffmpeg.ExitCode);
                }
                catch (Exception ex) { Log("Stop WaitForExit error: " + ex.Message); }
            }

            string stderr;
            lock (_stderrLock) stderr = _stderrBuf ?? "";
            if (stderr.Length > 0) Log("FFmpeg stderr (final):\n" + stderr);

            Cleanup();
            Log("Stop() done");
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
}
