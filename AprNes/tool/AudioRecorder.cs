using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace AprNes
{
    /// <summary>
    /// Audio-only recorder: pipes raw PCM (s16le, 44100Hz, stereo) to FFmpeg → MP3.
    /// </summary>
    static class AudioRecorder
    {
        static Process _ffmpeg;
        static NamedPipeServerStream _pipe;
        static volatile bool _recording;
        static volatile bool _pipeConnected;

        // Accumulation buffer + writer thread
        static short[] _buf;
        static int _bufPos;
        static readonly object _lock = new object();
        static Thread _writerThread;

        // FFmpeg stderr capture
        static readonly object _stderrLock = new object();
        static string _stderrBuf;

        static string _logPath;
        static readonly bool DebugLog = false; // set true for debugging

        public static bool IsRecording => _recording;
        public static string LastOutputPath { get; private set; }
        public static string LastError { get; private set; }

        /// <summary>Audio bitrate in kbps: 192, 160, 128.</summary>
        public static int AudioBitrate = 160;

        static void Log(string msg)
        {
            if (!DebugLog) return;
            try
            {
                if (_logPath == null)
                {
                    string dir = Path.Combine(
                        Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                        "Captures", "Audio");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    _logPath = Path.Combine(dir, "AudioRecorder.log");
                }
                File.AppendAllText(_logPath,
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine);
            }
            catch { }
        }

        public static bool Start(string ffmpegPath, string outputDir)
        {
            LastError = null;
            Log("========== Start() called ==========");
            if (_recording) { LastError = "Already recording"; Log("ABORT: " + LastError); return false; }
            if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
            { LastError = "ffmpeg.exe not found"; Log("ABORT: " + LastError); return false; }

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            string fileName = DateTime.Now.ToString("yyyyMMddHHmmss") + ".mp3";
            string outputPath = Path.Combine(outputDir, fileName);

            _pipeConnected = false;
            _buf = new short[882000]; // 10 seconds at 44100 Hz stereo
            _bufPos = 0;

            string pid = Process.GetCurrentProcess().Id.ToString();
            string pipeName = "aprnes_audiorec_" + pid;

            try
            {
                _pipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 65536);

                var connectResult = _pipe.BeginWaitForConnection(null, null);

                // FFmpeg: raw PCM input → MP3 output
                string args = string.Format(
                    "-y -f s16le -ar 44100 -ac 2 -i \\\\.\\pipe\\{0} " +
                    "-c:a libmp3lame -b:a {2}k -ac 2 -ar 44100 " +
                    "\"{1}\"",
                    pipeName, outputPath, AudioBitrate);

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

                // Wait for pipe connection
                Log("Waiting for pipe connection...");
                if (!connectResult.AsyncWaitHandle.WaitOne(10000))
                    throw new TimeoutException("FFmpeg did not connect to audio pipe within 10s");
                _pipe.EndWaitForConnection(connectResult);
                _pipeConnected = true;
                Log("Pipe connected");

                LastOutputPath = outputPath;
                _recording = true;
                NesCore.AudioSampleReady += OnAudioSample;

                _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "AR_Writer" };
                _writerThread.Start();
                Log("Writer thread started");
            }
            catch (Exception ex)
            {
                string stderr;
                lock (_stderrLock) stderr = _stderrBuf ?? "";
                LastError = ex.Message;
                if (stderr.Length > 0)
                    LastError += "\n\nFFmpeg stderr:\n" + stderr;
                Log("START FAILED: " + ex.GetType().Name + ": " + ex.Message);
                _recording = false;
                NesCore.AudioSampleReady -= OnAudioSample;
                Cleanup();
                return false;
            }

            Log("Recording started → " + outputPath);
            return true;
        }

        static void OnAudioSample(short left, short right)
        {
            if (!_recording) return;
            lock (_lock)
            {
                if (_bufPos + 1 < _buf.Length)
                {
                    _buf[_bufPos++] = left;
                    _buf[_bufPos++] = right;
                }
            }
        }

        static void WriterLoop()
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
            if (!_pipeConnected || _pipe == null) return;
            int count;
            byte[] bytes;
            lock (_lock)
            {
                count = _bufPos;
                if (count == 0) return;
                bytes = new byte[count * 2];
                Buffer.BlockCopy(_buf, 0, bytes, 0, bytes.Length);
                _bufPos = 0;
            }
            try
            {
                _pipe.Write(bytes, 0, bytes.Length);
            }
            catch (IOException ex)
            {
                Log("[Warning] Pipe IOException: " + ex.Message);
            }
            catch (Exception ex)
            {
                Log("[Error] Pipe fatal: " + ex.Message);
            }
        }

        public static void Stop()
        {
            if (!_recording) return;
            Log("Stop() called");
            _recording = false;

            NesCore.AudioSampleReady -= OnAudioSample;

            try { _writerThread?.Join(3000); } catch { }
            Log("Writer thread joined");

            try { _pipe?.Close(); } catch { }
            Log("Pipe closed (EOF sent)");

            if (_ffmpeg != null)
            {
                try
                {
                    if (!_ffmpeg.WaitForExit(10000))
                    {
                        Log("FFmpeg did not exit in 10s, killing");
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
            try { _pipe?.Dispose(); } catch { }
            try { _ffmpeg?.Dispose(); } catch { }
            _pipe = null;
            _ffmpeg = null;
            _writerThread = null;
            _pipeConnected = false;
        }
    }
}
