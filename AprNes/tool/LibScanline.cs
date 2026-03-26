using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

// Scanline filter — YCbCr 4:1:1 chroma subsampling + brightness-dependent darkening + 3-tap blur
// v2: integer math replaces 112 MB LUT (yiq_y/i/q 48 MB + toRGB 64 MB) → cache-friendly
namespace ScanLineBuilder
{
    unsafe class LibScanline
    {
        static uint* input, output;
        static byte* rates;
        static bool tableInited = false;
        static int* indexTable1x, indexTable2x, indexTable3x;

        // Thread-local line buffer — allocated once per thread, reused across frames
        [ThreadStatic] static IntPtr t_lineBuf;
        [ThreadStatic] static int t_lineBufCap;

        public static void init(uint* _input, uint* _output)
        {
            input = _input;
            output = _output;
            if (!tableInited)
            {
                // 256×240 → 600×480
                indexTable1x = (int*)Marshal.AllocHGlobal(sizeof(int) * 480 * 600);
                for (int y = 0; y < 480; y++)
                    for (int x = 0; x < 600; x++)
                        indexTable1x[x + y * 600] = ((int)(x * (1f / 600.0f * 256.0f))) + ((y >> 1) << 8);

                // 512×480 → 1196×960
                indexTable2x = (int*)Marshal.AllocHGlobal(sizeof(int) * 960 * 1196);
                for (int y = 0; y < 960; y++)
                    for (int x = 0; x < 1196; x++)
                        indexTable2x[x + y * 1196] = ((int)(x * (1f / 1196.0f * 512.0f))) + ((y >> 1) << 9);

                // 768×720 → 1792×1440
                indexTable3x = (int*)Marshal.AllocHGlobal(sizeof(int) * 1440 * 1792);
                for (int y = 0; y < 1440; y++)
                    for (int x = 0; x < 1792; x++)
                        indexTable3x[x + y * 1792] = ((int)(x * (1f / 1f / 1792.0f * 768.0f))) + ((y >> 1) * 768);

                // Brightness-dependent scanline darkening (256 bytes — fits L1 cache)
                rates = (byte*)Marshal.AllocHGlobal(256);
                const float scanStr = 0.35f;
                const float bloom   = 0.70f;
                for (int i = 0; i < 256; i++)
                {
                    float luma = i / 255f;
                    float darken = scanStr * (1f - luma * bloom);
                    rates[i] = (byte)(i * (1f - darken));
                }

                tableInited = true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int* EnsureLineBuffer(int pixelCount)
        {
            int bytes = pixelCount * sizeof(int);
            if (t_lineBufCap < bytes)
            {
                if (t_lineBuf != IntPtr.Zero) Marshal.FreeHGlobal(t_lineBuf);
                t_lineBuf = Marshal.AllocHGlobal(bytes);
                t_lineBufCap = bytes;
            }
            return (int*)t_lineBuf;
        }

        // ── Unified scanline core ────────────────────────────────────────
        // All integer math — no LUT access except rates[256] (L1-resident)
        static void ScanlineCore(int* idxTable, int outW, int outH, int scanDiv)
        {
            uint* inp = input;
            uint* outp = output;
            byte* rt = rates;

            Parallel.For(0, outH, y =>
            {
                int yOff = y * outW;
                int* idx = idxTable + yOff;
                bool isDark = ((y / scanDiv) & 1) == 1;
                int* line = EnsureLineBuffer(outW);

                int sCb = 0, sCr = 0;  // chroma state (updated every 4 px)

                // ── Pass 1: RGB → YCbCr → scanline darken → chroma 4:1:1 → RGB ──
                for (int x = 0; x < outW; x++)
                {
                    uint px = inp[idx[x]] & 0xFFFFFFu;
                    int R = (int)((px >> 16) & 0xFF);
                    int G = (int)((px >> 8) & 0xFF);
                    int B = (int)(px & 0xFF);

                    // BT.601 luma
                    int Y = (77 * R + 150 * G + 29 * B) >> 8;

                    // Brightness-dependent scanline darkening
                    if (isDark) Y = rt[Y];

                    // Chroma subsampling: update Cb/Cr every 4 pixels
                    if ((x & 3) == 0)
                    {
                        sCb = ((-43 * R - 85 * G + 128 * B) >> 8);
                        sCr = ((128 * R - 107 * G - 21 * B) >> 8);
                    }

                    // YCbCr → RGB
                    int oR = Y + ((359 * sCr) >> 8);
                    int oG = Y - ((88 * sCb + 183 * sCr) >> 8);
                    int oB = Y + ((454 * sCb) >> 8);

                    // Branchless clamp [0, 255]: negative → 0, >255 → 255
                    if ((uint)oR > 255) oR = ~(oR >> 31) & 255;
                    if ((uint)oG > 255) oG = ~(oG >> 31) & 255;
                    if ((uint)oB > 255) oB = ~(oB >> 31) & 255;

                    line[x] = (oR << 16) | (oG << 8) | oB;
                }

                // ── Pass 2: 3-tap horizontal blur → output ──
                // Weights: 25% left + 50% center + 25% right
                uint* outRow = outp + yOff;
                outRow[0] = (uint)line[0];
                int last = outW - 1;
                for (int x = 1; x < last; x++)
                {
                    int L = line[x - 1], C = line[x], Ri = line[x + 1];
                    outRow[x] = (uint)(
                        (((((L & 0xFF0000) + (Ri & 0xFF0000)) << 1) + ((C & 0xFF0000) << 2)) >> 3 & 0xFF0000) |
                        (((((L & 0x00FF00) + (Ri & 0x00FF00)) << 1) + ((C & 0x00FF00) << 2)) >> 3 & 0x00FF00) |
                        (((((L & 0x0000FF) + (Ri & 0x0000FF)) << 1) + ((C & 0x0000FF) << 2)) >> 3 & 0x0000FF));
                }
                outRow[last] = (uint)line[last];
            });
        }

        // ── Public API (unchanged signatures) ────────────────────────────
        public static void ScanlineFor1x() => ScanlineCore(indexTable1x, 600, 480, 1);
        public static void ScanlineFor2x() => ScanlineCore(indexTable2x, 1196, 960, 2);
        public static void ScanlineFor3x() => ScanlineCore(indexTable3x, 1792, 1440, 2);
    }
}
