using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace AprNes
{
    /// <summary>
    /// Two-process architecture: separate ffmpeg for video and audio (each with its own stdin).
    /// On stop, mux both into final MP4. No named pipes — eliminates deadlock.
    /// </summary>
    static class VideoRecorder
    {
        static Process _videoProc, _audioProc;
        static Stream _videoIn, _audioIn;
        static volatile bool _recording;
        static int _frameW, _frameH, _frameSize;
        static string _ffmpegPath, _outputPath, _tempVideoPath, _tempAudioPath;

        // Video: triple-buffered frame pool (emulation thread never blocks on pipe)
        static byte[][] _frameBufs;
        static readonly ConcurrentQueue<int> _freeFrames = new ConcurrentQueue<int>();
        static readonly ConcurrentQueue<int> _readyFrames = new ConcurrentQueue<int>();
        static readonly AutoResetEvent _writerSignal = new AutoResetEvent(false);
        static Thread _writerThread;

        // Audio: lock-guarded accumulation buffer
        static short[] _audioBuf;
        static int _audioPos;
        static readonly object _audioLock = new object();

        public static bool IsRecording => _recording;
        public static string LastOutputPath { get; private set; }
        public static string DetectedEncoder { get; private set; }

        /// <summary>
        /// Detect best available H.264 encoder: prefer GPU hardware, fallback to libx264.
        /// </summary>
        static string DetectEncoder(string ffmpegPath)
        {
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
                    if (p.ExitCode == 0) return enc;
                }
                catch { }
            }
            return "libx264";
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

        static Process StartFfmpeg(string ffmpegPath, string args)
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            p.Start();
            // Drain stderr in background to prevent pipe buffer deadlock
            p.BeginErrorReadLine();
            return p;
        }

        public static unsafe bool Start(string ffmpegPath, string outputDir, int width, int height)
        {
            if (_recording) return false;
            if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath)) return false;

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            _ffmpegPath = ffmpegPath;
            string fileName = DateTime.Now.ToString("yyyyMMddHHmmss");
            _outputPath = Path.Combine(outputDir, fileName + ".mp4");
            _tempVideoPath = Path.Combine(outputDir, fileName + ".tmp_v.mkv");
            _tempAudioPath = Path.Combine(outputDir, fileName + ".tmp_a.m4a");

            _frameW = width;
            _frameH = height;
            _frameSize = width * height * 4;

            // Init triple-buffer frame pool
            _frameBufs = new byte[3][];
            while (_freeFrames.TryDequeue(out _)) { }
            while (_readyFrames.TryDequeue(out _)) { }
            for (int i = 0; i < 3; i++)
            {
                _frameBufs[i] = new byte[_frameSize];
                _freeFrames.Enqueue(i);
            }

            _audioBuf = new short[8192];
            _audioPos = 0;

            // Detect best H.264 encoder
            string encoder = DetectEncoder(ffmpegPath);
            DetectedEncoder = encoder;
            string encoderArgs = GetEncoderArgs(encoder);

            try
            {
                // Process 1: video (rawvideo stdin → H.264 temp mkv)
                string videoArgs = string.Format(
                    "-y -f rawvideo -pix_fmt bgra -s {0}x{1} -r 60.0988 -i - " +
                    "{2} \"{3}\"",
                    width, height, encoderArgs, _tempVideoPath);
                _videoProc = StartFfmpeg(ffmpegPath, videoArgs);
                _videoIn = _videoProc.StandardInput.BaseStream;

                // Process 2: audio (s16le stdin → AAC temp m4a)
                string audioArgs = string.Format(
                    "-y -f s16le -ar 44100 -ac 1 -i - " +
                    "-c:a aac -b:a 128k -ac 2 \"{0}\"",
                    _tempAudioPath);
                _audioProc = StartFfmpeg(ffmpegPath, audioArgs);
                _audioIn = _audioProc.StandardInput.BaseStream;
            }
            catch
            {
                Cleanup(false);
                return false;
            }

            _recording = true;
            LastOutputPath = _outputPath;

            // Start dedicated writer thread
            _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "VideoRecorderWriter" };
            _writerThread.Start();

            NesCore.AudioSampleReady += OnAudioSample;
            return true;
        }

        public static unsafe void PushFrame(uint* screenBuf)
        {
            if (!_recording) return;
            int idx;
            if (!_freeFrames.TryDequeue(out idx)) return;
            fixed (byte* dst = _frameBufs[idx])
                Buffer.MemoryCopy(screenBuf, dst, _frameSize, _frameSize);
            _readyFrames.Enqueue(idx);
            _writerSignal.Set();
        }

        static void OnAudioSample(short sample)
        {
            if (!_recording) return;
            lock (_audioLock)
            {
                if (_audioPos < _audioBuf.Length)
                    _audioBuf[_audioPos++] = sample;
            }
        }

        static void WriterLoop()
        {
            while (_recording)
            {
                _writerSignal.WaitOne(50);
                if (!_recording) break;

                // Write all pending video frames
                int idx;
                while (_readyFrames.TryDequeue(out idx))
                {
                    try
                    {
                        if (_videoIn != null)
                            _videoIn.Write(_frameBufs[idx], 0, _frameSize);
                    }
                    catch { _recording = false; }
                    finally { _freeFrames.Enqueue(idx); }
                }

                // Flush buffered audio
                FlushAudio();
            }

            // Drain remaining frames after _recording set to false
            int rem;
            while (_readyFrames.TryDequeue(out rem))
            {
                try { _videoIn?.Write(_frameBufs[rem], 0, _frameSize); }
                catch { }
                finally { _freeFrames.Enqueue(rem); }
            }
            FlushAudio();
        }

        static void FlushAudio()
        {
            if (_audioIn == null) return;
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
            try { _audioIn.Write(bytes, 0, bytes.Length); }
            catch { }
        }

        public static void Stop()
        {
            if (!_recording) return;
            _recording = false;

            NesCore.AudioSampleReady -= OnAudioSample;
            _writerSignal.Set();

            // Wait for writer thread to drain and exit
            try { _writerThread?.Join(5000); } catch { }

            // Close stdin pipes → ffmpeg sees EOF → finalizes output
            try { _videoIn?.Close(); } catch { }
            try { _audioIn?.Close(); } catch { }

            // Wait for both ffmpeg processes to finish (with timeout)
            WaitOrKill(_videoProc, 15000);
            WaitOrKill(_audioProc, 10000);

            // Mux video + audio into final MP4
            bool muxOk = MuxFinal();

            Cleanup(muxOk);
        }

        static void WaitOrKill(Process p, int timeoutMs)
        {
            if (p == null) return;
            try
            {
                if (!p.WaitForExit(timeoutMs))
                    p.Kill();
            }
            catch { }
            try { p.Dispose(); } catch { }
        }

        static bool MuxFinal()
        {
            if (!File.Exists(_tempVideoPath)) return false;

            try
            {
                string muxArgs;
                if (File.Exists(_tempAudioPath))
                {
                    muxArgs = string.Format(
                        "-y -i \"{0}\" -i \"{1}\" -c copy -movflags +faststart -shortest \"{2}\"",
                        _tempVideoPath, _tempAudioPath, _outputPath);
                }
                else
                {
                    // Audio-only fallback (no audio temp)
                    muxArgs = string.Format(
                        "-y -i \"{0}\" -c copy -movflags +faststart \"{1}\"",
                        _tempVideoPath, _outputPath);
                }

                var mux = StartFfmpeg(_ffmpegPath, muxArgs);
                mux.WaitForExit(30000);
                bool ok = mux.ExitCode == 0;
                mux.Dispose();
                return ok;
            }
            catch { return false; }
        }

        static void Cleanup(bool deleteTempFiles)
        {
            _videoProc = null;
            _audioProc = null;
            _videoIn = null;
            _audioIn = null;
            _writerThread = null;

            if (deleteTempFiles)
            {
                try { if (File.Exists(_tempVideoPath)) File.Delete(_tempVideoPath); } catch { }
                try { if (File.Exists(_tempAudioPath)) File.Delete(_tempAudioPath); } catch { }
            }
        }
    }
}
