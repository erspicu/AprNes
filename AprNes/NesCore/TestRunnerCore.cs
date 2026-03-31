using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace AprNes
{
    // 按鍵事件：在指定 frame 按下按鈕，持續 holdFrames 後放開
    struct InputEvent
    {
        public int buttonIndex; // 0=A,1=B,2=Select,3=Start,4=Up,5=Down,6=Left,7=Right
        public int pressFrame;
        public int releaseFrame;
    }

    unsafe static class TestRunnerCore
    {
        const double NES_FPS = 60.0988;
        const int DEFAULT_HOLD_FRAMES = 10; // ~166ms

        // ── Platform delegates (must be set before calling Run) ──
        public static Action<string> SaveScreenshotFn;
        public static Func<string> GetBaseDirectoryFn; // returns exe directory for INI + FDS BIOS

        // Optional: benchmark filter pipeline (AprNes sets these, Ava leaves null)
        public static Action<string[], int> BenchmarkFilterInitFn; // args: [stage1, stage2, scanline?], frameHandler index
        public static Action BenchmarkFilterStepFn;  // called each frame during benchmark
        public static Action BenchmarkFilterCleanupFn;
        public static Func<string, string, bool, string> BenchmarkFilterDescFn; // (stage1, stage2, scanline) → description

        // 解析 --input 參數，格式: "A:1.0,B:2.0,Select:3.0,Start:4.0,Up:5.0,Down:6.0,Left:7.0,Right:8.0"
        static List<InputEvent> ParseInput(string input)
        {
            var events = new List<InputEvent>();
            if (string.IsNullOrEmpty(input)) return events;

            foreach (string entry in input.Split(','))
            {
                string[] parts = entry.Trim().Split(':');
                if (parts.Length < 2) continue;

                int btnIdx = ButtonNameToIndex(parts[0].Trim());
                if (btnIdx < 0) continue;

                double sec;
                if (!double.TryParse(parts[1].Trim(), out sec)) continue;

                int hold = DEFAULT_HOLD_FRAMES;
                if (parts.Length >= 3)
                {
                    double holdSec;
                    if (double.TryParse(parts[2].Trim(), out holdSec))
                        hold = Math.Max(1, (int)(holdSec * NES_FPS));
                }

                int pf = (int)(sec * NES_FPS);
                events.Add(new InputEvent { buttonIndex = btnIdx, pressFrame = pf, releaseFrame = pf + hold });
            }
            return events;
        }

        // Parse --timed-screenshots "path1:t1,path2:t2,...", returns list sorted by frame
        static List<KeyValuePair<int, string>> ParseTimedScreenshots(string spec)
        {
            var list = new List<KeyValuePair<int, string>>();
            if (string.IsNullOrEmpty(spec)) return list;
            foreach (string entry in spec.Split(','))
            {
                int sep = entry.LastIndexOf(':');
                if (sep <= 0) continue;
                string path = entry.Substring(0, sep).Trim();
                double sec;
                if (!double.TryParse(entry.Substring(sep + 1).Trim(), out sec)) continue;
                list.Add(new KeyValuePair<int, string>((int)(sec * NES_FPS), path));
            }
            list.Sort((a, b) => a.Key.CompareTo(b.Key));
            return list;
        }

        static int ButtonNameToIndex(string name)
        {
            switch (name.ToLower())
            {
                case "a":      return 0;
                case "b":      return 1;
                case "select": return 2;
                case "start":  return 3;
                case "up":     return 4;
                case "down":   return 5;
                case "left":   return 6;
                case "right":  return 7;
                default:       return -1;
            }
        }

        public static int Run(string[] args)
        {
            string baseDir = GetBaseDirectoryFn != null
                ? GetBaseDirectoryFn()
                : AppContext.BaseDirectory;

            // Load accuracy settings from INI (same file as GUI uses)
            string iniPath = Path.Combine(baseDir, "configure", "AprNes.ini");
            if (File.Exists(iniPath))
            {
                foreach (string line in File.ReadAllLines(iniPath))
                {
                    string[] kv = line.Split(new char[] { '=' }, 2);
                    if (kv.Length == 2 && kv[0].Trim() == "AccuracyOptA")
                    {
                        NesCore.AccuracyOptA = kv[1].Trim() != "0";
                        break;
                    }
                }
            }

            // Validation tests (--wait-result, --dump-ac-results) require full accuracy
            bool isValidation = Array.Exists(args, a => a == "--wait-result" || a == "--dump-ac-results");
            if (isValidation)
            {
                NesCore.AccuracyOptA = true;
                NesCore.Region = NesCore.RegionType.NTSC;
            }

            // --accuracy flag overrides everything (including validation default)
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--accuracy")
                {
                    string flags = args[i + 1].ToUpper();
                    NesCore.AccuracyOptA = flags.IndexOf('A') >= 0;
                    break;
                }
            }

            // ── perf mode: --perf <rom> [seconds] [note] ──
            {
                int perfIdx = Array.IndexOf(args, "--perf");
                if (perfIdx >= 0 && perfIdx + 1 < args.Length)
                {
                    string rom = args[perfIdx + 1];
                    int s; int seconds = (perfIdx + 2 < args.Length && int.TryParse(args[perfIdx + 2], out s)) ? s : 20;
                    string note = (perfIdx + 3 < args.Length) ? args[perfIdx + 3] : null;
                    RunPerf(rom, seconds, note);
                    return 0;
                }
            }

            string romPath = null;
            double timeSec = 0;
            string screenshotPath = null;
            string logPath = null;
            bool waitResult = false;
            double maxWait = 30;

            double softResetSec = -1;
            string inputSpec = null;
            HashSet<string> expectedCrcs = null;
            bool passOnStable = false;
            string timedScreenshotsSpec = null;
            bool dumpAcResults = false;
            bool dumpDebug = false;
            bool benchmarkMode = false;
            double benchmarkSec = 20;
            bool audioDsp = false;
            int audioDspMode = -1;
            string resizeStage1 = null;
            string resizeStage2 = null;
            bool resizeScanline = false;

            // Parse arguments
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--rom":
                        if (i + 1 < args.Length) romPath = args[++i];
                        break;
                    case "--time":
                        if (i + 1 < args.Length) double.TryParse(args[++i], out timeSec);
                        break;
                    case "--screenshot":
                        if (i + 1 < args.Length) screenshotPath = args[++i];
                        break;
                    case "--log":
                        if (i + 1 < args.Length) logPath = args[++i];
                        break;
                    case "--wait-result":
                        waitResult = true;
                        break;
                    case "--max-wait":
                        if (i + 1 < args.Length) double.TryParse(args[++i], out maxWait);
                        break;
                    case "--soft-reset":
                        if (i + 1 < args.Length) double.TryParse(args[++i], out softResetSec);
                        break;
                    case "--input":
                        if (i + 1 < args.Length) inputSpec = args[++i];
                        break;
                    case "--pass-on-stable":
                        passOnStable = true;
                        break;
                    case "--timed-screenshots":
                        if (i + 1 < args.Length) timedScreenshotsSpec = args[++i];
                        break;
                    case "--dump-ac-results":
                        dumpAcResults = true;
                        break;
                    case "--dump-debug":
                        dumpDebug = true;
                        break;
                    case "--analog":
                        NesCore.AnalogEnabled = true;
                        break;
                    case "--ultra-analog":
                        NesCore.AnalogEnabled = true;
                        NesCore.UltraAnalog = true;
                        break;
                    case "--analog-output":
                        if (i + 1 < args.Length)
                        {
                            var mode = args[++i].ToUpperInvariant();
                            if (mode == "RF") NesCore.AnalogOutput = AnalogOutputMode.RF;
                            else if (mode == "SVIDEO") NesCore.AnalogOutput = AnalogOutputMode.SVideo;
                            else NesCore.AnalogOutput = AnalogOutputMode.AV;
                        }
                        break;
                    case "--accuracy":
                        if (i + 1 < args.Length) i++; // already pre-parsed
                        break;
                    case "--benchmark":
                        benchmarkMode = true;
                        if (i + 1 < args.Length) double.TryParse(args[++i], out benchmarkSec);
                        break;
                    case "--analog-size":
                        if (i + 1 < args.Length)
                        {
                            int sz;
                            if (int.TryParse(args[++i], out sz)) NesCore.AnalogSize = sz;
                        }
                        break;
                    case "--crt":
                        NesCore.CrtEnabled = true;
                        break;
                    case "--expected-crc":
                        if (i + 1 < args.Length)
                        {
                            expectedCrcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (string c in args[++i].Split(','))
                            {
                                string t = c.Trim();
                                if (t.Length > 0) expectedCrcs.Add(t);
                            }
                        }
                        break;
                    case "--audio-dsp":
                        audioDsp = true;
                        break;
                    case "--audio-mode":
                        if (i + 1 < args.Length)
                        {
                            int m;
                            if (int.TryParse(args[++i], out m) && m >= 0 && m <= 2)
                                audioDspMode = m;
                        }
                        break;
                    case "--resize-stage1":
                        if (i + 1 < args.Length) resizeStage1 = args[++i];
                        break;
                    case "--resize-stage2":
                        if (i + 1 < args.Length) resizeStage2 = args[++i];
                        break;
                    case "--scanline":
                        resizeScanline = true;
                        break;
                    case "--region":
                        if (i + 1 < args.Length)
                        {
                            string r = args[++i].ToUpperInvariant();
                            if (r == "PAL") NesCore.Region = NesCore.RegionType.PAL;
                            else if (r == "DENDY") NesCore.Region = NesCore.RegionType.Dendy;
                            else NesCore.Region = NesCore.RegionType.NTSC;
                        }
                        break;
                }
            }

            if (romPath == null)
            {
                Console.Error.WriteLine("Usage: --rom <file.nes> [--time <seconds>] [--wait-result] [--max-wait <seconds>]");
                Console.Error.WriteLine("       [--soft-reset <seconds>] [--input \"A:1.0,B:2.0,...\"]");
                Console.Error.WriteLine("       [--screenshot <out.png>] [--timed-screenshots \"path1:t1,path2:t2,...\"]");
                Console.Error.WriteLine("       [--dump-ac-results] [--log <results.log>]");
                Console.Error.WriteLine("       [--benchmark <seconds>] [--resize-stage1 <filter>] [--resize-stage2 <filter>] [--scanline]");
                Console.Error.WriteLine("       [--analog] [--ultra-analog] [--analog-output <AV|RF|SVIDEO>] [--analog-size <N>] [--crt]");
                Console.Error.WriteLine("       [--audio-dsp] [--audio-mode <0|1|2>] [--region <NTSC|PAL|DENDY>]");
                Console.Error.WriteLine("       [--perf <rom> [seconds] [note]]");
                Console.Error.WriteLine("  Filter specs: xbrz_2..xbrz_6, scalex_2, scalex_3, nn_2..nn_N, none");
                return 2;
            }

            if (!File.Exists(romPath))
            {
                Console.Error.WriteLine("ROM not found: " + romPath);
                return 2;
            }

            string romName = Path.GetFileName(romPath);

            // Headless init
            NesCore.HeadlessMode = true;
            NesCore.OnError = msg => Console.Error.WriteLine("ERROR: " + msg);
            NesCore.AudioEnabled = audioDsp; // false unless --audio-dsp enables DSP pipeline

            // Audio DSP benchmark: set mode and max-cost parameters (no playback)
            if (audioDsp)
            {
                NesCore.AudioMode = (audioDspMode >= 0) ? audioDspMode : 0;

                if (NesCore.AudioMode == 1)
                {
                    NesCore.RfCrosstalk = true;
                    NesCore.CustomBuzz = true;
                    NesCore.BuzzAmplitude = 30;
                }
                else if (NesCore.AudioMode == 2)
                {
                    NesCore.StereoWidth = 100;
                    NesCore.BassBoostDb = 12;
                    NesCore.HaasDelay = 20;
                    NesCore.HaasCrossfeed = 40;
                    NesCore.ReverbWet = 15;
                    NesCore.CombFeedback = 70;
                    NesCore.CombDamp = 30;
                }
            }

            // Compute max frames
            int maxFrames = 0;
            if (timeSec > 0)
            {
                maxFrames = (int)(timeSec * 60.0988);
                if (timeSec > maxWait)
                    maxWait = timeSec + 5;
            }

            // State for video output handler
            bool done = false;
            int frameCount = 0;
            byte resultCode = 0x80;
            bool testStarted = false;

            // Soft reset state
            int softResetFrame = (softResetSec > 0) ? (int)(softResetSec * NES_FPS) : 0;
            bool explicitResetDone = false;
            int resetRequestFrame = -1;
            bool waitForTestRestart = false;
            int autoResetCount = 0;
            const int MAX_AUTO_RESETS = 10;

            // Input events
            List<InputEvent> inputEvents = ParseInput(inputSpec);

            // Timed screenshots
            var timedShots = ParseTimedScreenshots(timedScreenshotsSpec);

            // Screen stability tracking
            uint prevHash = 0;
            int stableFrameCount = 0;

            // Benchmark stopwatch
            Stopwatch benchSw = benchmarkMode ? new Stopwatch() : null;

            // Wire up VideoOutput handler
            EventHandler handler = null;
            handler = (sender, e) =>
            {
                frameCount++;

                // Benchmark mode: just count frames for wall-clock duration
                if (benchmarkMode)
                {
                    // Apply image filter pipeline if configured (platform-specific)
                    if (BenchmarkFilterStepFn != null)
                        BenchmarkFilterStepFn();

                    if (!benchSw.IsRunning) benchSw.Start();
                    if (benchSw.Elapsed.TotalSeconds >= benchmarkSec)
                    {
                        benchSw.Stop();
                        done = true;
                        NesCore.exit = true;
                    }
                    return;
                }

                // --- 模擬手把輸入 ---
                for (int ie = 0; ie < inputEvents.Count; ie++)
                {
                    var ev = inputEvents[ie];
                    if (frameCount == ev.pressFrame)
                        NesCore.P1_ButtonPress((byte)ev.buttonIndex);
                    else if (frameCount == ev.releaseFrame)
                        NesCore.P1_ButtonUnPress((byte)ev.buttonIndex);
                }

                // --- Timed screenshots ---
                for (int ts = 0; ts < timedShots.Count; ts++)
                {
                    if (frameCount == timedShots[ts].Key)
                    {
                        try { SaveScreenshotFn(timedShots[ts].Value); }
                        catch (Exception ex) { Console.Error.WriteLine("[TestRunner] Timed screenshot failed: " + ex.Message); }
                    }
                }

                // --- Soft reset: explicit --soft-reset time ---
                if (!explicitResetDone && softResetFrame > 0 && frameCount >= softResetFrame)
                {
                    Console.Error.WriteLine("[TestRunner] Soft reset at frame " + frameCount);
                    NesCore.SoftReset();
                    explicitResetDone = true;
                }

                if (waitResult)
                {
                    byte status = NesCore.NES_MEM[0x6000];

                    if (status == 0x81 && resetRequestFrame < 0
                        && !waitForTestRestart && autoResetCount < MAX_AUTO_RESETS)
                    {
                        resetRequestFrame = frameCount;
                    }
                    if (waitForTestRestart && status != 0x81)
                    {
                        waitForTestRestart = false;
                    }
                    if (resetRequestFrame >= 0
                        && frameCount >= resetRequestFrame + 6)
                    {
                        Console.Error.WriteLine("[TestRunner] Auto soft reset #" + (autoResetCount + 1) + " at frame " + frameCount + " ($6000=$81 at frame " + resetRequestFrame + ")");
                        NesCore.SoftReset();
                        resetRequestFrame = -1;
                        waitForTestRestart = true;
                        autoResetCount++;
                    }

                    if (!testStarted)
                    {
                        if (status >= 0x80)
                            testStarted = true;
                    }
                    else if (status < 0x80)
                    {
                        resultCode = status;
                        done = true;
                        NesCore.exit = true;
                        return;
                    }
                }

                // --- Screen stability detection ---
                if (waitResult && !testStarted && frameCount > 120)
                {
                    uint hash = 1;
                    bool hasContent = false;
                    uint firstPx = NesCore.ScreenBuf1x[0];
                    for (int i = 0; i < 256 * 240; i += 37)
                    {
                        uint px = NesCore.ScreenBuf1x[i];
                        hash = hash * 31 + px;
                        if (px != firstPx) hasContent = true;
                    }

                    if (hash == prevHash && hasContent)
                        stableFrameCount++;
                    else
                    {
                        prevHash = hash;
                        stableFrameCount = 0;
                    }

                    if (stableFrameCount >= 90)
                    {
                        bool earlyPass = NametableContains("Passed") || NametableContains("PASSED");
                        bool earlyFail = NametableContains("Failed") || NametableContains("FAILED");

                        if (!earlyPass && !earlyFail)
                        {
                            int oldCode = DetectOldBlarggScreenCode();
                            if (oldCode == 0) earlyPass = true;
                            else if (oldCode > 0) earlyFail = true;
                        }

                        if (!earlyPass && !earlyFail && NametableContains(" 0/"))
                            earlyPass = true;

                        if (!earlyPass && !earlyFail && NametableContains("All tests complete"))
                            earlyPass = true;

                        if (!earlyPass && !earlyFail && expectedCrcs != null && expectedCrcs.Count > 0)
                        {
                            string foundCrc = FindNametableCrc();
                            if (foundCrc != null)
                            {
                                if (expectedCrcs.Contains(foundCrc))
                                    earlyPass = true;
                                else
                                    earlyFail = true;
                                Console.Error.WriteLine("[TestRunner] CRC on screen: " + foundCrc
                                    + (earlyPass ? " (matched)" : " (NOT in expected set)"));
                            }
                        }

                        if (!earlyPass && !earlyFail && passOnStable)
                        {
                            if (NametableContains("Failed") || NametableContains("FAILED"))
                                earlyFail = true;
                            else
                                earlyPass = true;
                        }

                        if (earlyPass || earlyFail)
                        {
                            resultCode = earlyFail ? (byte)1 : (byte)0;
                            Console.Error.WriteLine("[TestRunner] Screen stable at frame " + frameCount
                                + ", detected " + (earlyFail ? "Failed" : "Passed") + " on screen"
                                + (passOnStable && !NametableContains("Passed") && !NametableContains("PASSED")
                                   ? " (pass-on-stable)" : ""));
                            done = true;
                            NesCore.exit = true;
                            return;
                        }
                    }
                }

                if (maxFrames > 0 && frameCount >= maxFrames)
                {
                    if (HasBlarggSignature())
                        resultCode = NesCore.NES_MEM[0x6000];
                    else
                        resultCode = DetectNonProtocolResult();
                    if (resultCode != 0 && expectedCrcs != null && expectedCrcs.Count > 0)
                    {
                        string foundCrc = FindNametableCrc();
                        if (foundCrc != null && expectedCrcs.Contains(foundCrc))
                            resultCode = 0;
                    }
                    done = true;
                    NesCore.exit = true;
                    return;
                }

                // max-wait safety (frame-based)
                if (frameCount >= (int)(maxWait * 60.0988))
                {
                    if (testStarted || HasBlarggSignature())
                    {
                        resultCode = NesCore.NES_MEM[0x6000];
                    }
                    else
                    {
                        resultCode = DetectNonProtocolResult();
                        Console.Error.WriteLine("[TestRunner] Timeout at frame " + frameCount
                            + ", no $6000 protocol. result=0x" + resultCode.ToString("X2"));
                    }
                    if (resultCode != 0 && expectedCrcs != null && expectedCrcs.Count > 0)
                    {
                        string foundCrc = FindNametableCrc();
                        if (foundCrc != null)
                        {
                            Console.Error.WriteLine("[TestRunner] CRC on screen: " + foundCrc
                                + (expectedCrcs.Contains(foundCrc) ? " (matched)" : " (NOT in expected set)"));
                            if (expectedCrcs.Contains(foundCrc))
                                resultCode = 0;
                        }
                    }
                    done = true;
                    NesCore.exit = true;
                }
            };

            NesCore.VideoOutput += handler;

            // Load ROM
            byte[] romBytes;
            try
            {
                romBytes = File.ReadAllBytes(romPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Failed to read ROM: " + ex.Message);
                return 2;
            }

            NesCore.rom_file_name = romPath;
            NesCore.exit = false;

            bool initOk;
            if (NesCore.IsFdsFile(romBytes))
            {
                byte[] bios = NesCore.LoadAndValidateFdsBios(baseDir);
                initOk = bios != null && NesCore.initFDS(bios, romBytes);
            }
            else
            {
                initOk = NesCore.init(romBytes);
            }
            if (!initOk)
            {
                Console.Error.WriteLine("Failed to init ROM: " + romName);
                NesCore.VideoOutput -= handler;
                return 2;
            }

            // AudioPlus settings for DSP testing
            if (audioDsp && NesCore.AudioMode > 0)
            {
                NesCore.AudioPlus_ApplySettings();
            }

            // Setup benchmark filter pipeline (platform-specific, e.g. Render_resize on WinForms)
            string benchFilterDesc = null;
            if (benchmarkMode && BenchmarkFilterInitFn != null
                && (resizeStage1 != null || resizeStage2 != null || resizeScanline))
            {
                // Pack filter args for the platform callback
                string[] filterArgs = new string[]
                {
                    resizeStage1 ?? "",
                    resizeStage2 ?? "",
                    resizeScanline ? "1" : "0"
                };
                BenchmarkFilterInitFn(filterArgs, 0);
                benchFilterDesc = BenchmarkFilterDescFn != null
                    ? BenchmarkFilterDescFn(resizeStage1, resizeStage2, resizeScanline)
                    : null;
                if (benchFilterDesc != null)
                    Console.Error.WriteLine("[Benchmark] Filter: " + benchFilterDesc
                        + " → " + NesCore.RenderOutputW + "×" + NesCore.RenderOutputH);
            }

            // Run emulation on a background thread
            Thread emuThread = new Thread(() => NesCore.run());
            emuThread.IsBackground = true;
            emuThread.Start();
            emuThread.Join();

            NesCore.VideoOutput -= handler;

            // Benchmark mode: print FPS and exit
            if (benchmarkMode)
            {
                double elapsed = benchSw.Elapsed.TotalSeconds;
                double fps = frameCount / elapsed;
                string filterDesc = benchFilterDesc ?? "none";
                Console.WriteLine(string.Format("BENCHMARK: {0} frames in {1:F2}s = {2:F2} FPS [Filter: {3}]",
                    frameCount, elapsed, fps, filterDesc));
                if (BenchmarkFilterCleanupFn != null) BenchmarkFilterCleanupFn();
                return 0;
            }

            // Read blargg test text from $6004+
            string resultText = ReadBlarggText();

            // Take screenshot
            if (screenshotPath != null && SaveScreenshotFn != null)
            {
                try
                {
                    SaveScreenshotFn(screenshotPath);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Screenshot failed: " + ex.Message);
                }
            }

            // Dump AccuracyCoin results ($0300-$04FF)
            if (dumpAcResults)
            {
                var sb = new StringBuilder("AC_RESULTS_HEX:");
                for (int addr = 0x0300; addr < 0x0500; addr++)
                    sb.Append(NesCore.NES_MEM[addr].ToString("X2"));
                Console.WriteLine(sb.ToString());
            }

            // Dump debug memory ranges
            if (dumpDebug)
            {
                var sb = new StringBuilder();
                sb.Append("DEBUG_ZP_00: ");
                for (int addr = 0x00; addr < 0x20; addr++)
                    sb.Append(NesCore.NES_MEM[addr].ToString("X2") + " ");
                Console.Error.WriteLine(sb.ToString().TrimEnd());

                sb.Clear();
                sb.Append("DEBUG_ZP_50: ");
                for (int addr = 0x50; addr < 0x70; addr++)
                    sb.Append(NesCore.NES_MEM[addr].ToString("X2") + " ");
                Console.Error.WriteLine(sb.ToString().TrimEnd());

                for (int row = 0; row < 16; row++)
                {
                    sb.Clear();
                    sb.Append("DEBUG_" + (0x500 + row * 16).ToString("X4") + ": ");
                    for (int col = 0; col < 16; col++)
                        sb.Append(NesCore.NES_MEM[0x500 + row * 16 + col].ToString("X2") + " ");
                    Console.Error.WriteLine(sb.ToString().TrimEnd());
                }
            }

            // Determine pass/fail
            bool passed = (resultCode == 0);
            string status_str;
            if (!done && !waitResult && maxFrames == 0)
                status_str = "DONE";
            else if (passed)
                status_str = "PASS";
            else
                status_str = "FAIL(" + resultCode + ")";

            string outputLine = status_str + " | " + romName + " | " + (resultText ?? "").Trim();
            Console.WriteLine(outputLine);

            // Write log
            if (logPath != null)
            {
                try
                {
                    File.AppendAllText(logPath, outputLine + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Log write failed: " + ex.Message);
                }
            }

            return passed ? 0 : 1;
        }

        static bool HasBlarggSignature()
        {
            return NesCore.NES_MEM[0x6001] == 0xDE
                && NesCore.NES_MEM[0x6002] == 0xB0
                && NesCore.NES_MEM[0x6003] == 0x61;
        }

        static bool NametableContains(string text)
        {
            byte* nt = NesCore.ppu_ram;
            if (nt == null) return false;
            int len = text.Length;
            for (int off = 0x2000; off <= 0x23BF - len + 1; off++)
            {
                bool match = true;
                for (int k = 0; k < len; k++)
                {
                    if (nt[off + k] != (byte)text[k]) { match = false; break; }
                }
                if (match) return true;
            }
            return false;
        }

        static int DetectOldBlarggScreenCode()
        {
            byte* nt = NesCore.ppu_ram;
            if (nt == null) return -1;

            for (int off = 0x2000; off <= 0x23BF - 2; off++)
            {
                if (nt[off] == (byte)'$' && nt[off + 1] == (byte)'0')
                {
                    byte c = nt[off + 2];
                    if (c == (byte)'1') return 0;
                    if (c >= (byte)'2' && c <= (byte)'9') return c - (byte)'0';
                    if (c >= (byte)'A' && c <= (byte)'F') return 10 + (c - (byte)'A');
                    if (c >= (byte)'a' && c <= (byte)'f') return 10 + (c - (byte)'a');
                }
            }
            return -1;
        }

        static byte DetectNonProtocolResult()
        {
            bool hasPassed = NametableContains("Passed") || NametableContains("PASSED");
            bool hasFailed = NametableContains("Failed") || NametableContains("FAILED");

            if (hasFailed) return 1;
            if (hasPassed) return 0;

            int oldCode = DetectOldBlarggScreenCode();
            if (oldCode >= 0) return (byte)oldCode;

            if (NametableContains(" 0/")) return 0;
            if (NametableContains("All tests complete")) return 0;

            byte f0 = NesCore.NES_MEM[0xF0];
            if (f0 == 1) return 0;
            if (f0 >= 2 && f0 <= 15) return f0;

            return 0xFF;
        }

        static string FindNametableCrc()
        {
            byte* nt = NesCore.ppu_ram;
            if (nt == null) return null;
            for (int off = 0x2000; off <= 0x23BF - 7; off++)
            {
                bool allHex = true;
                char[] crc = new char[8];
                for (int k = 0; k < 8; k++)
                {
                    byte b = nt[off + k];
                    if ((b >= (byte)'0' && b <= (byte)'9') ||
                        (b >= (byte)'A' && b <= (byte)'F') ||
                        (b >= (byte)'a' && b <= (byte)'f'))
                    {
                        crc[k] = (char)b;
                    }
                    else { allHex = false; break; }
                }
                if (!allHex) continue;
                if (off > 0x2000)
                {
                    byte prev = nt[off - 1];
                    if ((prev >= (byte)'0' && prev <= (byte)'9') ||
                        (prev >= (byte)'A' && prev <= (byte)'F') ||
                        (prev >= (byte)'a' && prev <= (byte)'f'))
                        continue;
                }
                if (off + 8 <= 0x23BF)
                {
                    byte next = nt[off + 8];
                    if ((next >= (byte)'0' && next <= (byte)'9') ||
                        (next >= (byte)'A' && next <= (byte)'F') ||
                        (next >= (byte)'a' && next <= (byte)'f'))
                        continue;
                }
                return new string(crc).ToUpperInvariant();
            }
            return null;
        }

        static string ReadBlarggText()
        {
            if (HasBlarggSignature())
            {
                StringBuilder sb = new StringBuilder();
                for (int addr = 0x6004; addr < 0x6800; addr++)
                {
                    byte c = NesCore.NES_MEM[addr];
                    if (c == 0) break;
                    sb.Append((char)c);
                }
                return sb.ToString();
            }

            bool hasPassed = NametableContains("Passed") || NametableContains("PASSED");
            bool hasFailed = NametableContains("Failed") || NametableContains("FAILED");

            if (hasPassed) return "(screen: Passed)";
            if (hasFailed) return "(screen: Failed)";

            int oldCode = DetectOldBlarggScreenCode();
            if (oldCode == 0) return "(screen: $01 = passed)";
            if (oldCode > 0)  return "(screen: $0" + oldCode.ToString("X") + " = failed)";

            if (NametableContains(" 0/")) return "(screen: 0 errors = passed)";
            if (NametableContains("All tests complete")) return "(screen: All tests complete = passed)";

            string crc = FindNametableCrc();
            if (crc != null) return "(screen CRC: " + crc + ")";

            byte f0 = NesCore.NES_MEM[0xF0];
            if (f0 == 1)   return "(no $6000 signature, $F0=0x01 = passed)";
            if (f0 >= 2 && f0 <= 15) return "(no $6000 signature, $F0=0x" + f0.ToString("X2") + " = failed)";
            return "(no $6000 signature, $F0=0x" + f0.ToString("X2") + ")";
        }

        // ── Perf benchmark (--perf mode) ───────────────────────────────────
        static void RunPerf(string romPath, int seconds, string note)
        {
            if (!File.Exists(romPath))
            {
                Console.WriteLine("[PERF] ROM not found: " + romPath);
                Environment.Exit(1);
            }

            byte[] rom = File.ReadAllBytes(romPath);

            string baseDir = GetBaseDirectoryFn != null
                ? GetBaseDirectoryFn()
                : AppContext.BaseDirectory;

            // Walk up from exe to find project-root Performance/ directory
            string perfDir = baseDir;
            for (int i = 0; i < 6; i++)
            {
                string candidate = Path.Combine(perfDir, "Performance");
                if (Directory.Exists(candidate)) { perfDir = candidate; break; }
                perfDir = Path.GetDirectoryName(perfDir) ?? perfDir;
            }
            Directory.CreateDirectory(perfDir);

            int version = GetNextPerfVersion(perfDir);
            string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
            string fileName = dateStr + "_perf_v" + version + ".md";
            string filePath = Path.Combine(perfDir, fileName);

            Console.WriteLine("[PERF] Running " + seconds + "s benchmark: " + Path.GetFileName(romPath));
            Console.WriteLine("[PERF] No audio, no FPS cap, headless mode");

            int frames = RunPerfJit(rom, seconds);
            double fps = frames / (double)seconds;

            Console.WriteLine(string.Format("[PERF] Result: {0} frames in {1}s  »  {2:F2} avg FPS", frames, seconds, fps));
            Console.WriteLine("[PERF] Saving report: " + filePath);

            string label = version == 1 ? "Baseline" : "v" + version;
            var sb = new StringBuilder();
            sb.AppendLine("# AprNes Performance Benchmark – " + label);
            sb.AppendLine();
            sb.AppendLine("## Test Environment");
            sb.AppendLine();
            sb.AppendLine("| Item | Value |");
            sb.AppendLine("|------|-------|");
            sb.AppendLine("| Date | " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " |");
            sb.AppendLine("| ROM | " + Path.GetFileName(romPath) + " |");
            sb.AppendLine("| Duration | " + seconds + " seconds |");
            sb.AppendLine("| Mode | Headless, No audio, No FPS cap |");
            sb.AppendLine("| OS | " + Environment.OSVersion + " |");
            sb.AppendLine("| CPU | " + GetCpuName() + " |");
            sb.AppendLine("| Runtime | " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " |");
            sb.AppendLine();
            sb.AppendLine("## Results");
            sb.AppendLine();
            sb.AppendLine("| Frames | Average FPS |");
            sb.AppendLine("|--------|-------------|");
            sb.AppendLine("| " + frames + " | " + fps.ToString("F2") + " |");
            sb.AppendLine();
            sb.AppendLine("## Notes");
            sb.AppendLine();
            sb.AppendLine(string.IsNullOrWhiteSpace(note)
                ? (version == 1 ? "Baseline measurement." : "(no description)")
                : note);
            sb.AppendLine();

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            Console.WriteLine("[PERF] Done. Saved to " + fileName);
        }

        static int RunPerfJit(byte[] rom, int seconds)
        {
            int frames = 0;
            NesCore.exit = false;
            NesCore.AudioEnabled = false;
            NesCore.HeadlessMode = true;
            NesCore.init(rom);
            NesCore.LimitFPS = false;
            EventHandler counter = (s, e) => Interlocked.Increment(ref frames);
            NesCore.VideoOutput += counter;
            var t = new Thread(NesCore.run) { IsBackground = true };
            t.Start();
            Thread.Sleep(seconds * 1000);
            NesCore.exit = true;
            NesCore._event.Set();
            t.Join(2000);
            NesCore.VideoOutput -= counter;
            NesCore.AudioEnabled = true;
            NesCore.HeadlessMode = false;
            return frames;
        }

        static int GetNextPerfVersion(string perfDir)
        {
            int max = 0;
            foreach (string f in Directory.GetFiles(perfDir, "*_perf_v*.md"))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                int idx = name.LastIndexOf("_perf_v", StringComparison.Ordinal);
                if (idx < 0) continue;
                int n;
                if (int.TryParse(name.Substring(idx + 7), out n) && n > max) max = n;
            }
            return max + 1;
        }

        static string GetCpuName()
        {
            try
            {
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                        System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine
                        .OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    {
                        return key != null ? (key.GetValue("ProcessorNameString") as string ?? "Unknown") : "Unknown";
                    }
                }
                foreach (string line in File.ReadLines("/proc/cpuinfo"))
                    if (line.StartsWith("model name", StringComparison.Ordinal))
                        return line.Split(':')[1].Trim();
            }
            catch { }
            return "Unknown";
        }
    }
}
