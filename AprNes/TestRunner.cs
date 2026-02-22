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
            HashSet<string> expectedCrcs = null; // --expected-crc "CRC1,CRC2,..."
            bool passOnStable = false; // --pass-on-stable: screen stable + no "Failed" = PASS

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
                    case "--pass-on-stable":
                        passOnStable = true;
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
            bool explicitResetDone = false;  // for --soft-reset flag (one-shot)
            int resetRequestFrame = -1;      // frame when $6000==$81 detected
            bool waitForTestRestart = false; // after auto-reset, wait for $6000 != $81
            int autoResetCount = 0;
            const int MAX_AUTO_RESETS = 10;

            // Input events
            List<InputEvent> inputEvents = ParseInput(inputSpec);

            // Screen stability tracking (for non-$6000-protocol test detection)
            uint prevHash = 0;
            int stableFrameCount = 0;

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
                if (!explicitResetDone && softResetFrame > 0 && frameCount >= softResetFrame)
                {
                    Console.Error.WriteLine("[TestRunner] Soft reset at frame " + frameCount);
                    NesCore.SoftReset();
                    explicitResetDone = true;
                }

                if (waitResult)
                {
                    // blargg test protocol:
                    // $6000 = $80 means "test running"
                    // $6000 = $81 means "press reset now" (delay >= 100ms ≈ 6 frames)
                    // $6000 < $80 means "test finished" (0 = pass, N = fail code)
                    byte status = NesCore.NES_MEM[0x6000];

                    // Auto-detect $81: test requests soft reset (supports multiple resets)
                    if (status == 0x81 && resetRequestFrame < 0
                        && !waitForTestRestart && autoResetCount < MAX_AUTO_RESETS)
                    {
                        resetRequestFrame = frameCount;
                    }
                    // After reset, wait for $6000 to leave $81 before detecting again
                    if (waitForTestRestart && status != 0x81)
                    {
                        waitForTestRestart = false;
                    }
                    // Trigger soft reset ~100ms (6 frames) after $81 detected
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

                // --- Screen stability detection (early exit for non-$6000-protocol tests) ---
                // Old blargg tests don't use $6000; they display results on screen and
                // store the result code at $F0 (1=pass, >=2=fail). Detect when the screen
                // stops changing to avoid waiting for the full timeout.
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

                    if (stableFrameCount >= 90) // ~1.5 seconds of stable screen
                    {
                        // Only exit early if screen explicitly shows pass/fail text.
                        // Tests that display a title while running internally (e.g.
                        // cpu_timing_test6 "16 SECONDS") would falsely trigger otherwise.
                        bool earlyPass = NametableContains("Passed") || NametableContains("PASSED");
                        bool earlyFail = NametableContains("Failed") || NametableContains("FAILED");

                        // Also check old blargg "$01"/"$02" format
                        if (!earlyPass && !earlyFail)
                        {
                            int oldCode = DetectOldBlarggScreenCode();
                            if (oldCode == 0) earlyPass = true;
                            else if (oldCode > 0) earlyFail = true;
                        }

                        // Also check error count format: " 0/" = zero errors = pass
                        if (!earlyPass && !earlyFail && NametableContains(" 0/"))
                            earlyPass = true;

                        // Old blargg multi-test: "All tests complete" = pass
                        if (!earlyPass && !earlyFail && NametableContains("All tests complete"))
                            earlyPass = true;

                        // CRC-based tests: match displayed CRC against expected values
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

                        // --pass-on-stable: no explicit result text, but screen stable = PASS
                        // (for tests that exit silently with code 0 on success)
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
                        // Screen stable but no result text yet — keep waiting
                    }
                }

                if (maxFrames > 0 && frameCount >= maxFrames)
                {
                    if (HasBlarggSignature())
                        resultCode = NesCore.NES_MEM[0x6000];
                    else
                        resultCode = DetectNonProtocolResult();
                    // Override with CRC check if expected CRCs provided
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
                    // Override with CRC check if expected CRCs provided
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

        static bool HasBlarggSignature()
        {
            return NesCore.NES_MEM[0x6001] == 0xDE
                && NesCore.NES_MEM[0x6002] == 0xB0
                && NesCore.NES_MEM[0x6003] == 0x61;
        }

        // Search PPU nametable 0 ($2000-$23BF) for ASCII text.
        // Blargg test ROMs use a font where tile index = ASCII code.
        static bool NametableContains(string text)
        {
            byte* nt = NesCore.ppu_ram;
            if (nt == null) return false;
            int len = text.Length;
            // Nametable 0: 30 rows × 32 cols = 960 bytes at $2000
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

        // Detect old blargg "$XX" screen format (e.g. "$01"=pass, "$02"-"$0F"=fail)
        // Returns -1 if no match, 0 for pass ($01), 1+ for fail code
        static int DetectOldBlarggScreenCode()
        {
            byte* nt = NesCore.ppu_ram;
            if (nt == null) return -1;

            for (int off = 0x2000; off <= 0x23BF - 2; off++)
            {
                if (nt[off] == (byte)'$' && nt[off + 1] == (byte)'0')
                {
                    byte c = nt[off + 2];
                    if (c == (byte)'1') return 0;  // $01 = pass
                    if (c >= (byte)'2' && c <= (byte)'9') return c - (byte)'0';
                    if (c >= (byte)'A' && c <= (byte)'F') return 10 + (c - (byte)'A');
                    if (c >= (byte)'a' && c <= (byte)'f') return 10 + (c - (byte)'a');
                }
            }
            return -1;
        }

        // Determine result for non-$6000-protocol tests by reading the screen.
        // Priority: nametable text > old "$XX" format > error count > $F0 byte > unknown
        static byte DetectNonProtocolResult()
        {
            // Check PPU nametable for "Passed"/"PASSED"/"Failed"/"FAILED"
            bool hasPassed = NametableContains("Passed") || NametableContains("PASSED");
            bool hasFailed = NametableContains("Failed") || NametableContains("FAILED");

            if (hasFailed)
                return 1; // fail
            if (hasPassed)
                return 0; // pass

            // Check old blargg "$01"/"$02" screen format
            int oldCode = DetectOldBlarggScreenCode();
            if (oldCode >= 0)
                return (byte)oldCode;

            // Check error count format: " 0/" means zero errors = pass
            if (NametableContains(" 0/"))
                return 0; // pass (zero errors)

            // Old blargg multi-test format: "All tests complete" = pass
            if (NametableContains("All tests complete"))
                return 0; // pass

            // Fallback: old blargg $F0 protocol (1 = pass, 2-15 = fail code)
            byte f0 = NesCore.NES_MEM[0xF0];
            if (f0 == 1) return 0;       // pass
            if (f0 >= 2 && f0 <= 15) return f0; // fail code

            // Can't determine — return 0xFF (timeout/unknown)
            return 0xFF;
        }

        // Find an 8-character hex CRC in nametable, return it (uppercase) or null
        static string FindNametableCrc()
        {
            byte* nt = NesCore.ppu_ram;
            if (nt == null) return null;
            // Scan nametable 0 for 8 consecutive hex digits
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
                // Must NOT be preceded or followed by another hex digit (avoid partial matches)
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

            // Non-protocol: report what we found
            bool hasPassed = NametableContains("Passed") || NametableContains("PASSED");
            bool hasFailed = NametableContains("Failed") || NametableContains("FAILED");

            if (hasPassed) return "(screen: Passed)";
            if (hasFailed) return "(screen: Failed)";

            int oldCode = DetectOldBlarggScreenCode();
            if (oldCode == 0) return "(screen: $01 = passed)";
            if (oldCode > 0)  return "(screen: $0" + oldCode.ToString("X") + " = failed)";

            if (NametableContains(" 0/")) return "(screen: 0 errors = passed)";
            if (NametableContains("All tests complete")) return "(screen: All tests complete = passed)";

            // CRC-based result
            string crc = FindNametableCrc();
            if (crc != null) return "(screen CRC: " + crc + ")";

            byte f0 = NesCore.NES_MEM[0xF0];
            if (f0 == 1)   return "(no $6000 signature, $F0=0x01 = passed)";
            if (f0 >= 2 && f0 <= 15) return "(no $6000 signature, $F0=0x" + f0.ToString("X2") + " = failed)";
            return "(no $6000 signature, $F0=0x" + f0.ToString("X2") + ")";
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
