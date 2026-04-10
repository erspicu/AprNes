using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace TriCNES
{
    static class TestRunner
    {
        const double NES_FPS = 60.0988;
        const int DEFAULT_HOLD_FRAMES = 10; // ~166ms

        struct InputEvent
        {
            public int buttonIndex; // 0=A,1=B,2=Sel,3=Start,4=Up,5=Down,6=Left,7=Right
            public int pressFrame;
            public int releaseFrame;
        }

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
            // TriCNES ControllerPort1 bit layout: bit7=A, bit6=B, bit5=Select, bit4=Start,
            // bit3=Up, bit2=Down, bit1=Left, bit0=Right
            switch (name.ToLower())
            {
                case "a":      return 7;
                case "b":      return 6;
                case "select": return 5;
                case "start":  return 4;
                case "up":     return 3;
                case "down":   return 2;
                case "left":   return 1;
                case "right":  return 0;
                default:       return -1;
            }
        }

        public static int Run(string[] args)
        {
            string romPath = null;
            string screenshotPath = null;
            bool waitResult = false;
            int maxWaitSec = 30;
            int timeSec = 0;
            HashSet<string> expectedCrcs = null;
            bool passOnStable = false;
            string inputSpec = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--rom":
                        if (i + 1 < args.Length) romPath = args[++i];
                        break;
                    case "--screenshot":
                        if (i + 1 < args.Length) screenshotPath = args[++i];
                        break;
                    case "--wait-result":
                        waitResult = true;
                        break;
                    case "--max-wait":
                        if (i + 1 < args.Length) maxWaitSec = int.Parse(args[++i]);
                        break;
                    case "--time":
                        if (i + 1 < args.Length) timeSec = int.Parse(args[++i]);
                        break;
                    case "--pass-on-stable":
                        passOnStable = true;
                        break;
                    case "--input":
                        if (i + 1 < args.Length) inputSpec = args[++i];
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
                Console.Error.WriteLine("Usage: TriCNES.exe --rom <file.nes> [--wait-result] [--max-wait <sec>] [--time <sec>] [--screenshot <out.png>] [--pass-on-stable] [--expected-crc CRC1,CRC2] [--input \"A:2.0,B:4.0,...\"]");
                return 2;
            }

            if (!File.Exists(romPath))
            {
                Console.WriteLine("ROM not found: " + romPath);
                return 2;
            }

            // Print iNES header info
            byte[] header = new byte[16];
            using (var fs = File.OpenRead(romPath))
                fs.Read(header, 0, 16);
            Console.WriteLine("iNes header");
            Console.WriteLine("PRG-ROM count : " + header[4]);
            Console.WriteLine("CHR-ROM count : " + header[5]);

            // Create emulator and load ROM
            Emulator emu = new Emulator();
            try
            {
                Cartridge cart = new Cartridge(romPath);
                emu.Cart = cart;
                cart.Emu = emu;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to load ROM: " + ex.Message);
                return 2;
            }

            // Parse input events
            List<InputEvent> inputEvents = ParseInput(inputSpec);

            // Main emulation loop
            int maxFrames = maxWaitSec * 60; // ~60 fps
            if (timeSec > 0) maxFrames = timeSec * 60;
            int frameCount = 0;
            bool testStarted = false;
            int resultCode = -1;
            bool done = false;

            // Soft reset tracking
            int resetRequestFrame = -1;
            bool waitForTestRestart = false;
            int autoResetCount = 0;
            const int MAX_AUTO_RESETS = 10;

            // Screen stability detection
            uint lastScreenHash = 0;
            int stableFrames = 0;

            while (frameCount < maxFrames && !done)
            {
                // Simulate controller input (must be set BEFORE frame advance so strobe picks it up)
                byte buttons = 0;
                for (int ie = 0; ie < inputEvents.Count; ie++)
                {
                    var ev = inputEvents[ie];
                    if (frameCount >= ev.pressFrame && frameCount < ev.releaseFrame)
                        buttons |= (byte)(1 << ev.buttonIndex);
                }
                emu.ControllerPort1 = buttons;

                emu._CoreFrameAdvance();
                frameCount++;

                if (waitResult)
                {
                    // blargg test protocol: $6000 = status
                    byte status = ReadPRGRAM(emu, 0x6000);

                    // Auto-detect $81: test requests soft reset
                    if (status == 0x81 && resetRequestFrame < 0
                        && !waitForTestRestart && autoResetCount < MAX_AUTO_RESETS)
                    {
                        resetRequestFrame = frameCount;
                    }
                    if (waitForTestRestart && status != 0x81)
                    {
                        waitForTestRestart = false;
                    }
                    if (resetRequestFrame >= 0 && frameCount >= resetRequestFrame + 6)
                    {
                        emu.Reset();
                        resetRequestFrame = -1;
                        waitForTestRestart = true;
                        autoResetCount++;
                    }

                    if (!testStarted)
                    {
                        if (status >= 0x80) testStarted = true;
                    }
                    else if (status < 0x80)
                    {
                        resultCode = status;
                        done = true;
                    }
                }

                // Screen stability detection (for tests without $6000 protocol)
                if (waitResult && !testStarted && frameCount > 120)
                {
                    uint hash = ComputeScreenHash(emu);
                    if (hash == lastScreenHash)
                        stableFrames++;
                    else
                    {
                        stableFrames = 0;
                        lastScreenHash = hash;
                    }

                    if (stableFrames >= 90) // ~1.5 seconds of stable screen
                    {
                        bool earlyPass = NametableContains(emu, "Passed") || NametableContains(emu, "PASSED");
                        bool earlyFail = NametableContains(emu, "Failed") || NametableContains(emu, "FAILED");

                        // Old blargg "$01"/"$02" format
                        if (!earlyPass && !earlyFail)
                        {
                            int oldCode = DetectOldBlarggScreenCode(emu);
                            if (oldCode == 0) earlyPass = true;
                            else if (oldCode > 0) earlyFail = true;
                        }

                        // Error count format: " 0/" = zero errors = pass
                        if (!earlyPass && !earlyFail && NametableContains(emu, " 0/"))
                            earlyPass = true;

                        // Old blargg multi-test: "All tests complete" = pass
                        if (!earlyPass && !earlyFail && NametableContains(emu, "All tests complete"))
                            earlyPass = true;

                        // CRC-based tests
                        if (!earlyPass && !earlyFail && expectedCrcs != null && expectedCrcs.Count > 0)
                        {
                            string foundCrc = FindNametableCrc(emu);
                            if (foundCrc != null)
                            {
                                if (expectedCrcs.Contains(foundCrc))
                                    earlyPass = true;
                                else
                                    earlyFail = true;
                            }
                        }

                        // --pass-on-stable
                        if (!earlyPass && !earlyFail && passOnStable)
                        {
                            if (NametableContains(emu, "Failed") || NametableContains(emu, "FAILED"))
                                earlyFail = true;
                            else
                                earlyPass = true;
                        }

                        if (earlyPass || earlyFail)
                        {
                            resultCode = earlyFail ? 1 : 0;
                            done = true;
                        }
                    }
                }

                // For --time mode: check results at end
                if (timeSec > 0 && frameCount >= maxFrames)
                {
                    byte status = ReadPRGRAM(emu, 0x6000);
                    if (HasBlarggSignature(emu))
                    {
                        resultCode = status;
                    }
                    else
                    {
                        resultCode = DetectNonProtocolResult(emu, expectedCrcs);
                    }
                    done = true;
                }
            }

            // Timeout — try to determine result
            if (!done)
            {
                byte status = ReadPRGRAM(emu, 0x6000);
                if (HasBlarggSignature(emu) && status < 0x80)
                    resultCode = status;
                else
                {
                    resultCode = DetectNonProtocolResult(emu, expectedCrcs);
                }
            }

            // Save screenshot
            if (screenshotPath != null && emu.Screen != null)
            {
                try
                {
                    string dir = Path.GetDirectoryName(screenshotPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    emu.Screen.Bitmap.Save(screenshotPath, ImageFormat.Png);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Screenshot failed: " + ex.Message);
                }
            }

            // Build result text
            string resultText = ReadResultText(emu, expectedCrcs);

            // Print result
            bool passed = resultCode == 0;
            string romName = Path.GetFileName(romPath);
            string statusStr = passed ? "PASS" : "FAIL(" + resultCode + ")";
            Console.WriteLine(statusStr + " | " + romName + " | " + resultText.Replace("\n", " ").Trim());

            return passed ? 0 : 1;
        }

        static byte ReadPRGRAM(Emulator emu, ushort addr)
        {
            if (emu.Cart == null || emu.Cart.PRGRAM == null) return 0;
            int offset = (addr - 0x6000) & (emu.Cart.PRGRAM.Length - 1);
            if (offset < 0 || offset >= emu.Cart.PRGRAM.Length) return 0;
            return emu.Cart.PRGRAM[offset];
        }

        static bool HasBlarggSignature(Emulator emu)
        {
            return ReadPRGRAM(emu, 0x6001) == 0xDE
                && ReadPRGRAM(emu, 0x6002) == 0xB0
                && ReadPRGRAM(emu, 0x6003) == 0x61;
        }

        static bool NametableContains(Emulator emu, string text)
        {
            if (emu.VRAM == null) return false;
            int len = text.Length;
            // Nametable 0: 30 rows x 32 cols = 960 bytes
            for (int off = 0; off <= 960 - len; off++)
            {
                bool match = true;
                for (int k = 0; k < len; k++)
                {
                    if (emu.VRAM[off + k] != (byte)text[k]) { match = false; break; }
                }
                if (match) return true;
            }
            return false;
        }

        static uint ComputeScreenHash(Emulator emu)
        {
            if (emu.Screen == null || emu.Screen.Bits == null) return 0;
            uint hash = 1;
            bool hasContent = false;
            var bits = emu.Screen.Bits;
            int firstPx = bits[0];
            for (int i = 0; i < bits.Length; i += 37)
            {
                uint px = (uint)bits[i];
                hash = hash * 31 + px;
                if (bits[i] != firstPx) hasContent = true;
            }
            return hasContent ? hash : 0;
        }

        // Detect old blargg "$XX" screen format (e.g. "$01"=pass, "$02"-"$0F"=fail)
        static int DetectOldBlarggScreenCode(Emulator emu)
        {
            if (emu.VRAM == null) return -1;
            for (int off = 0; off <= 960 - 3; off++)
            {
                if (emu.VRAM[off] == (byte)'$' && emu.VRAM[off + 1] == (byte)'0')
                {
                    byte c = emu.VRAM[off + 2];
                    if (c == (byte)'1') return 0;  // $01 = pass
                    if (c >= (byte)'2' && c <= (byte)'9') return c - (byte)'0';
                    if (c >= (byte)'A' && c <= (byte)'F') return 10 + (c - (byte)'A');
                    if (c >= (byte)'a' && c <= (byte)'f') return 10 + (c - (byte)'a');
                }
            }
            return -1;
        }

        // Find an 8-character hex CRC in nametable
        static string FindNametableCrc(Emulator emu)
        {
            if (emu.VRAM == null) return null;
            for (int off = 0; off <= 960 - 8; off++)
            {
                bool allHex = true;
                char[] crc = new char[8];
                for (int k = 0; k < 8; k++)
                {
                    byte b = emu.VRAM[off + k];
                    if ((b >= (byte)'0' && b <= (byte)'9') ||
                        (b >= (byte)'A' && b <= (byte)'F') ||
                        (b >= (byte)'a' && b <= (byte)'f'))
                        crc[k] = (char)b;
                    else { allHex = false; break; }
                }
                if (!allHex) continue;
                // Boundary check: not preceded/followed by hex
                if (off > 0)
                {
                    byte prev = emu.VRAM[off - 1];
                    if ((prev >= (byte)'0' && prev <= (byte)'9') ||
                        (prev >= (byte)'A' && prev <= (byte)'F') ||
                        (prev >= (byte)'a' && prev <= (byte)'f'))
                        continue;
                }
                if (off + 8 < 960)
                {
                    byte next = emu.VRAM[off + 8];
                    if ((next >= (byte)'0' && next <= (byte)'9') ||
                        (next >= (byte)'A' && next <= (byte)'F') ||
                        (next >= (byte)'a' && next <= (byte)'f'))
                        continue;
                }
                return new string(crc).ToUpperInvariant();
            }
            return null;
        }

        // Determine result for non-$6000-protocol tests
        static int DetectNonProtocolResult(Emulator emu, HashSet<string> expectedCrcs)
        {
            bool hasPassed = NametableContains(emu, "Passed") || NametableContains(emu, "PASSED");
            bool hasFailed = NametableContains(emu, "Failed") || NametableContains(emu, "FAILED");
            if (hasFailed) return 1;
            if (hasPassed) return 0;

            int oldCode = DetectOldBlarggScreenCode(emu);
            if (oldCode >= 0) return oldCode;

            if (NametableContains(emu, " 0/")) return 0;
            if (NametableContains(emu, "All tests complete")) return 0;

            // CRC match
            if (expectedCrcs != null && expectedCrcs.Count > 0)
            {
                string foundCrc = FindNametableCrc(emu);
                if (foundCrc != null && expectedCrcs.Contains(foundCrc))
                    return 0;
            }

            return 255; // unknown
        }

        // Build human-readable result text
        static string ReadResultText(Emulator emu, HashSet<string> expectedCrcs)
        {
            if (HasBlarggSignature(emu))
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 4; i < 0x800; i++)
                {
                    byte b = ReadPRGRAM(emu, (ushort)(0x6000 + i));
                    if (b == 0) break;
                    sb.Append((char)b);
                }
                return sb.ToString();
            }

            if (NametableContains(emu, "Passed") || NametableContains(emu, "PASSED"))
                return "(screen: Passed)";
            if (NametableContains(emu, "Failed") || NametableContains(emu, "FAILED"))
                return "(screen: Failed)";

            int oldCode = DetectOldBlarggScreenCode(emu);
            if (oldCode == 0) return "(screen: $01 = passed)";
            if (oldCode > 0) return "(screen: $0" + oldCode.ToString("X") + " = failed)";

            if (NametableContains(emu, " 0/")) return "(screen: 0 errors = passed)";
            if (NametableContains(emu, "All tests complete")) return "(screen: All tests complete = passed)";

            string crc = FindNametableCrc(emu);
            if (crc != null) return "(screen CRC: " + crc + ")";

            return "";
        }
    }
}
