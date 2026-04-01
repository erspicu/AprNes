using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

// Scanline filter — brightness-dependent darkening + 3-tap horizontal blur
// Used by Render_resize as optional post-process (ApplyInPlace)
namespace ScanLineBuilder
{
    unsafe class LibScanline
    {
        static byte* rates;

        // Thread-local line buffer — allocated once per thread, reused across frames
        [ThreadStatic] static IntPtr t_lineBuf;
        [ThreadStatic] static int t_lineBufCap;

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

        // ── Initialize rates table (for Render_resize) ──────────────────
        static bool ratesInited = false;
        public static void InitRates()
        {
            if (ratesInited) return;
            rates = (byte*)Marshal.AllocHGlobal(256);
            const float scanStr = 0.35f;
            const float bloom   = 0.70f;
            for (int i = 0; i < 256; i++)
            {
                float luma = i / 255f;
                float darken = scanStr * (1f - luma * bloom);
                rates[i] = (byte)(i * (1f - darken));
            }
            ratesInited = true;
        }

        // ── In-place scanline post-process for any resolution ───────────
        // Brightness-dependent darkening on odd lines + 3-tap horizontal blur
        public static void ApplyInPlace(uint* buffer, int width, int height)
        {
            byte* rt = rates;

            Parallel.For(0, height, y =>
            {
                uint* row = buffer + y * width;
                bool isDark = (y & 1) == 1;

                int* line = EnsureLineBuffer(width);

                for (int x = 0; x < width; x++)
                {
                    uint px = row[x];

                    if (isDark)
                    {
                        int R = (int)((px >> 16) & 0xFF);
                        int G = (int)((px >> 8) & 0xFF);
                        int B = (int)(px & 0xFF);
                        R = rt[R]; G = rt[G]; B = rt[B];
                        line[x] = (int)(0xFF000000u | (uint)(R << 16) | (uint)(G << 8) | (uint)B);
                    }
                    else
                    {
                        line[x] = (int)(px | 0xFF000000u);
                    }
                }

                // 3-tap horizontal blur → write back
                row[0] = (uint)line[0];
                int last = width - 1;
                for (int x = 1; x < last; x++)
                {
                    int L = line[x - 1], C = line[x], Ri = line[x + 1];
                    row[x] = 0xFF000000u |
                        (uint)(((((L & 0xFF0000) + (Ri & 0xFF0000)) << 1) + ((C & 0xFF0000) << 2)) >> 3 & 0xFF0000) |
                        (uint)(((((L & 0x00FF00) + (Ri & 0x00FF00)) << 1) + ((C & 0x00FF00) << 2)) >> 3 & 0x00FF00) |
                        (uint)(((((L & 0x0000FF) + (Ri & 0x0000FF)) << 1) + ((C & 0x0000FF) << 2)) >> 3 & 0x0000FF);
                }
                row[last] = (uint)line[last];
            });
        }
    }
}
