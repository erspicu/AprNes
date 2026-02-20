using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
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

    unsafe static class TestRunner
    {
        const double NES_FPS = 60.0988;
        const int DEFAULT_HOLD_FRAMES = 10; // ~166ms

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
            string romPath = null;
            double timeSec = 0;
            string screenshotPath = null;
            string logPath = null;
            bool waitResult = false;
            double maxWait = 30;
            string debugLog = null;
            int debugMax = 15000;
            double softResetSec = -1; // <0 means not set
            string inputSpec = null;

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
                    case "--debug-log":
                        if (i + 1 < args.Length) debugLog = args[++i];
                        break;
                    case "--debug-max":
                        if (i + 1 < args.Length) int.TryParse(args[++i], out debugMax);
                        break;
                    case "--soft-reset":
                        if (i + 1 < args.Length) double.TryParse(args[++i], out softResetSec);
                        break;
                    case "--input":
                        if (i + 1 < args.Length) inputSpec = args[++i];
                        break;
                }
            }

            if (romPath == null)
            {
                Console.Error.WriteLine("Usage: AprNes.exe --rom <file.nes> [--time <seconds>] [--wait-result] [--max-wait <seconds>] [--soft-reset <seconds>] [--input \"A:1.0,B:2.0,...\"] [--screenshot <out.png>] [--log <results.log>] [--debug-log <path>] [--debug-max <n>]");
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
            NesCore.AudioEnabled = false;
            NesCore.LimitFPS = false;
            if (debugLog != null)
                NesCore.DebugLogPath = debugLog;
            NesCore.dbgMaxConfig = debugMax;

            // Compute max frames
            // NES runs ~60.0988 fps; if --time given, compute frame limit
            int maxFrames = 0;
            if (timeSec > 0)
                maxFrames = (int)(timeSec * 60.0988);

            // State for video output handler
            bool done = false;
            int frameCount = 0;
            byte resultCode = 0x80;
            bool testStarted = false; // true once $6000 >= 0x80

            // Soft reset state
            int softResetFrame = (softResetSec > 0) ? (int)(softResetSec * NES_FPS) : 0;
            bool softResetDone = false;
            int resetRequestFrame = -1; // frame when $6000==$81 detected

            // Input events
            List<InputEvent> inputEvents = ParseInput(inputSpec);

            // Wire up VideoOutput handler
            EventHandler handler = null;
            handler = (sender, e) =>
            {
                frameCount++;

                // --- 模擬手把輸入 ---
                for (int ie = 0; ie < inputEvents.Count; ie++)
                {
                    var ev = inputEvents[ie];
                    if (frameCount == ev.pressFrame)
                        NesCore.P1_ButtonPress((byte)ev.buttonIndex);
                    else if (frameCount == ev.releaseFrame)
                        NesCore.P1_ButtonUnPress((byte)ev.buttonIndex);
                }

                // --- Soft reset: explicit --soft-reset time ---
                if (!softResetDone && softResetFrame > 0 && frameCount >= softResetFrame)
                {
                    Console.Error.WriteLine("[TestRunner] Soft reset at frame " + frameCount);
                    NesCore.SoftReset();
                    softResetDone = true;
                }

                if (waitResult)
                {
                    // blargg test protocol:
                    // $6000 = $80 means "test running"
                    // $6000 = $81 means "press reset now" (delay >= 100ms ≈ 6 frames)
                    // $6000 < $80 means "test finished" (0 = pass, N = fail code)
                    byte status = NesCore.NES_MEM[0x6000];

                    // Auto-detect $81: test requests soft reset
                    if (!softResetDone && status == 0x81 && resetRequestFrame < 0)
                    {
                        resetRequestFrame = frameCount;
                    }
                    // Trigger soft reset ~100ms (6 frames) after $81 detected
                    if (!softResetDone && resetRequestFrame >= 0
                        && frameCount >= resetRequestFrame + 6)
                    {
                        Console.Error.WriteLine("[TestRunner] Auto soft reset at frame " + frameCount + " ($6000=$81 at frame " + resetRequestFrame + ")");
                        NesCore.SoftReset();
                        softResetDone = true;
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

                if (maxFrames > 0 && frameCount >= maxFrames)
                {
                    // time limit reached; read current status
                    resultCode = NesCore.NES_MEM[0x6000];
                    done = true;
                    NesCore.exit = true;
                    return;
                }

                // max-wait safety (frame-based)
                if (frameCount >= (int)(maxWait * 60.0988))
                {
                    resultCode = NesCore.NES_MEM[0x6000];
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

            if (!NesCore.init(romBytes))
            {
                Console.Error.WriteLine("Failed to init ROM: " + romName);
                NesCore.VideoOutput -= handler;
                return 2;
            }

            // Run emulation on a background thread
            Thread emuThread = new Thread(() => NesCore.run());
            emuThread.IsBackground = true;
            emuThread.Start();
            emuThread.Join();

            NesCore.VideoOutput -= handler;

            // Read blargg test text from $6004+
            string resultText = ReadBlarggText();

            // Take screenshot
            if (screenshotPath != null)
            {
                try
                {
                    SaveScreenshot(screenshotPath);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Screenshot failed: " + ex.Message);
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

            string outputLine = status_str + " | " + romName + " | " + resultText.Trim();
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

            // Close debug log if still open
            if (NesCore.dbgLog != null)
            {
                NesCore.dbgLog.Flush();
                NesCore.dbgLog.Close();
                NesCore.dbgLog = null;
            }

            return passed ? 0 : 1;
        }

        static string ReadBlarggText()
        {
            // blargg test protocol: $6001-$6003 should be 0xDE, 0xB0, 0x61
            byte sig1 = NesCore.NES_MEM[0x6001];
            byte sig2 = NesCore.NES_MEM[0x6002];
            byte sig3 = NesCore.NES_MEM[0x6003];

            if (sig1 != 0xDE || sig2 != 0xB0 || sig3 != 0x61)
                return "(no blargg signature)";

            StringBuilder sb = new StringBuilder();
            for (int addr = 0x6004; addr < 0x6800; addr++)
            {
                byte c = NesCore.NES_MEM[addr];
                if (c == 0) break;
                sb.Append((char)c);
            }
            return sb.ToString();
        }

        static void SaveScreenshot(string path)
        {
            Bitmap bmp = new Bitmap(256, 240, PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(
                new Rectangle(0, 0, 256, 240),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            uint* src = NesCore.ScreenBuf1x;
            byte* dst = (byte*)data.Scan0;
            int stride = data.Stride;

            for (int y = 0; y < 240; y++)
            {
                uint* dstRow = (uint*)(dst + y * stride);
                for (int x = 0; x < 256; x++)
                {
                    dstRow[x] = src[y * 256 + x];
                }
            }

            bmp.UnlockBits(data);

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            bmp.Save(path, ImageFormat.Png);
            bmp.Dispose();
        }
    }
}
