using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;

namespace AprNes
{
    unsafe static class TestRunner
    {
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
                }
            }

            if (romPath == null)
            {
                Console.Error.WriteLine("Usage: AprNes.exe --rom <file.nes> [--time <seconds>] [--wait-result] [--max-wait <seconds>] [--screenshot <out.png>] [--log <results.log>] [--debug-log <path>] [--debug-max <n>]");
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

            // Wire up VideoOutput handler
            EventHandler handler = null;
            handler = (sender, e) =>
            {
                frameCount++;

                if (waitResult)
                {
                    // blargg test protocol:
                    // $6000 = $80 means "test running"
                    // $6000 < $80 means "test finished" (0 = pass, N = fail code)
                    // Must wait for test to start ($80) before checking for completion
                    byte status = NesCore.NES_MEM[0x6000];
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
