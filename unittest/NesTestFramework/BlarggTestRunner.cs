using System;
using System.Collections.Generic;
using System.Text;

namespace NesTestFramework
{
    /// <summary>
    /// Emulator-agnostic blargg test protocol engine.
    /// Drives any IEmulatorCore through the standard blargg test flow.
    /// </summary>
    public class BlarggTestRunner
    {
        private readonly IEmulatorCore _emu;

        // Reusable buffers (avoid per-frame allocation)
        private readonly uint[] _screenBuf = new uint[256 * 240];
        private readonly byte[] _ntBuf = new byte[960];

        // Screen stability tracking
        private uint _prevScreenHash;
        private int _stableFrames;
        private const int STABLE_THRESHOLD = 90;

        // Soft-reset tracking
        private int _resetCount;
        private int _resetDelay;
        private const int MAX_RESETS = 10;
        private const int RESET_DELAY_FRAMES = 6;

        // Timed input
        private struct TimedInput { public NesButton Button; public double TimeSeconds; }
        private List<TimedInput> _inputs;
        private readonly bool[] _buttonState = new bool[8];

        public BlarggTestRunner(IEmulatorCore emulator)
        {
            _emu = emulator ?? throw new ArgumentNullException(nameof(emulator));
        }

        /// <summary>Run a single test to completion. Returns the result.</summary>
        public TestResult RunTest(TestDefinition test, string romDir)
        {
            var result = new TestResult { Test = test, DetectionMethod = "timeout" };

            // Load ROM
            string romPath = System.IO.Path.Combine(romDir, test.Suite, test.Rom);
            if (!System.IO.File.Exists(romPath))
            {
                result.ResultCode = 255;
                result.ResultText = "ROM not found: " + romPath;
                return result;
            }

            byte[] romData = System.IO.File.ReadAllBytes(romPath);
            _emu.SetRegion(test.Region);
            if (!_emu.LoadRom(romData))
            {
                result.ResultCode = 255;
                result.ResultText = "Failed to load ROM";
                return result;
            }

            // Parse timed input
            _inputs = ParseInputSpec(test.InputSpec);
            for (int i = 0; i < 8; i++) _buttonState[i] = false;

            // Reset state
            _prevScreenHash = 0;
            _stableFrames = 0;
            _resetCount = 0;
            _resetDelay = 0;

            int maxFrames = test.MaxWaitSeconds * 60; // ~60 fps
            bool has6000Protocol = false;

            for (int frame = 0; frame < maxFrames; frame++)
            {
                // Apply timed input
                double frameTime = frame / 60.0988;
                ApplyTimedInput(frameTime);
                _emu.SetP1Buttons(_buttonState[0], _buttonState[1], _buttonState[2], _buttonState[3],
                                  _buttonState[4], _buttonState[5], _buttonState[6], _buttonState[7]);

                _emu.RunOneFrame();
                result.FrameCount = frame + 1;

                // Handle soft-reset delay
                if (_resetDelay > 0) { _resetDelay--; continue; }

                // ── $6000 Protocol Detection ──
                byte sig1 = _emu.ReadCpuMemory(0x6001);
                byte sig2 = _emu.ReadCpuMemory(0x6002);
                byte sig3 = _emu.ReadCpuMemory(0x6003);

                if (sig1 == 0xDE && sig2 == 0xB0 && sig3 == 0x61)
                {
                    has6000Protocol = true;
                    byte status = _emu.ReadCpuMemory(0x6000);

                    if (status == 0x81 && _resetCount < MAX_RESETS)
                    {
                        // Soft reset requested
                        _emu.SoftReset();
                        _resetCount++;
                        _resetDelay = RESET_DELAY_FRAMES;
                        continue;
                    }

                    if (status < 0x80)
                    {
                        // Test complete
                        result.Passed = (status == 0);
                        result.ResultCode = status;
                        result.ResultText = Read6004Text();
                        result.DetectionMethod = "$6000 protocol";
                        return result;
                    }
                    // status >= 0x80: still running
                    continue;
                }

                // ── Screen Stability Detection ──
                _emu.GetScreenPixels(_screenBuf);
                uint hash = HashScreen(_screenBuf);

                if (hash == _prevScreenHash)
                    _stableFrames++;
                else
                {
                    _stableFrames = 0;
                    _prevScreenHash = hash;
                }

                if (_stableFrames >= STABLE_THRESHOLD)
                {
                    // Screen stable — check nametable for result text
                    _emu.GetNametable0(_ntBuf);

                    // Check for "Passed" / "Failed"
                    string ntText = DecodeNametable(_ntBuf);
                    if (ntText.Contains("Passed") || ntText.Contains("PASSED") ||
                        ntText.Contains("All tests complete"))
                    {
                        result.Passed = true;
                        result.ResultCode = 0;
                        result.ResultText = "screen: Passed";
                        result.DetectionMethod = "screen stability";
                        return result;
                    }
                    if (ntText.Contains("Failed") || ntText.Contains("FAILED"))
                    {
                        result.Passed = false;
                        result.ResultCode = 1;
                        result.ResultText = "screen: Failed";
                        result.DetectionMethod = "screen stability";
                        return result;
                    }

                    // CRC matching
                    if (test.ExpectedCrcs != null)
                    {
                        string crcInScreen = FindCrcInNametable(ntText);
                        if (crcInScreen != null)
                        {
                            foreach (var expected in test.ExpectedCrcs)
                            {
                                if (string.Equals(crcInScreen, expected, StringComparison.OrdinalIgnoreCase))
                                {
                                    result.Passed = true;
                                    result.ResultCode = 0;
                                    result.ResultText = "CRC match: " + crcInScreen;
                                    result.DetectionMethod = "CRC match";
                                    return result;
                                }
                            }
                            result.Passed = false;
                            result.ResultCode = 1;
                            result.ResultText = "CRC mismatch: " + crcInScreen;
                            result.DetectionMethod = "CRC match";
                            return result;
                        }
                    }

                    // Pass-on-stable fallback (error counter tests)
                    if (test.PassOnStable && ntText.Contains(" 0/"))
                    {
                        result.Passed = true;
                        result.ResultCode = 0;
                        result.ResultText = "screen stable: 0 errors";
                        result.DetectionMethod = "pass-on-stable";
                        return result;
                    }

                    // Stable but can't determine result — keep waiting (reset stability counter)
                    _stableFrames = 0;
                }
            }

            // Timeout — check $F0 fallback
            byte f0 = _emu.ReadCpuMemory(0x00F0);
            if (!has6000Protocol && f0 != 0)
            {
                result.ResultCode = f0;
                result.ResultText = $"$F0=0x{f0:X2}";
                result.DetectionMethod = "$F0 fallback";
            }
            else
            {
                result.ResultCode = 255;
                result.ResultText = "Timeout";
            }

            return result;
        }

        private string Read6004Text()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 200; i++)
            {
                byte b = _emu.ReadCpuMemory((ushort)(0x6004 + i));
                if (b == 0) break;
                sb.Append((char)b);
            }
            return sb.ToString();
        }

        private static uint HashScreen(uint[] pixels)
        {
            // Fast hash: sample every 37th pixel (prime stride avoids patterns)
            uint h = 0x811C9DC5;
            for (int i = 0; i < pixels.Length; i += 37)
            {
                h ^= pixels[i];
                h *= 0x01000193;
            }
            return h;
        }

        private static string DecodeNametable(byte[] nt)
        {
            // Convert nametable tile indices to ASCII (blargg uses ASCII-mapped CHR)
            var sb = new StringBuilder(960);
            for (int i = 0; i < 960; i++)
            {
                byte tile = nt[i];
                char c = (tile >= 0x20 && tile < 0x7F) ? (char)tile : ' ';
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string FindCrcInNametable(string text)
        {
            // Look for 8-character hex string (CRC32)
            for (int i = 0; i <= text.Length - 8; i++)
            {
                bool valid = true;
                for (int j = 0; j < 8; j++)
                {
                    char c = text[i + j];
                    if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                    { valid = false; break; }
                }
                if (valid)
                {
                    // Verify it's surrounded by non-hex (not part of a longer hex string)
                    bool leftOk = (i == 0 || !IsHex(text[i - 1]));
                    bool rightOk = (i + 8 >= text.Length || !IsHex(text[i + 8]));
                    if (leftOk && rightOk)
                        return text.Substring(i, 8).ToUpperInvariant();
                }
            }
            return null;
        }

        private static bool IsHex(char c) =>
            (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

        private static List<TimedInput> ParseInputSpec(string spec)
        {
            var list = new List<TimedInput>();
            if (string.IsNullOrEmpty(spec)) return list;
            foreach (var part in spec.Split(','))
            {
                var kv = part.Trim().Split(':');
                if (kv.Length != 2) continue;
                NesButton btn;
                switch (kv[0].Trim())
                {
                    case "A": btn = NesButton.A; break;
                    case "B": btn = NesButton.B; break;
                    case "Select": btn = NesButton.Select; break;
                    case "Start": btn = NesButton.Start; break;
                    case "Up": btn = NesButton.Up; break;
                    case "Down": btn = NesButton.Down; break;
                    case "Left": btn = NesButton.Left; break;
                    case "Right": btn = NesButton.Right; break;
                    default: continue;
                }
                if (double.TryParse(kv[1].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double t))
                    list.Add(new TimedInput { Button = btn, TimeSeconds = t });
            }
            list.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));
            return list;
        }

        private void ApplyTimedInput(double time)
        {
            foreach (var inp in _inputs)
            {
                int idx = (int)inp.Button;
                if (time >= inp.TimeSeconds && time < inp.TimeSeconds + 0.1)
                    _buttonState[idx] = true;
                else if (time >= inp.TimeSeconds + 0.1)
                    _buttonState[idx] = false;
            }
        }
    }
}
