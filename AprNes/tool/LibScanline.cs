using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

//High speed YIQ 4:1:1 Scanline Blur filter
namespace ScanLineBuilder
{
    unsafe class LibScanline
    {
        const int scanline_px = 2;
        static byte* yiq_y, yiq_i, yiq_q;
        static int* toRGB, clip;
        static uint* input, output;
        static byte* rates;
        static bool tableInited = false;
        static int* indexTable1x, indexTable2x, indexTable3x;

        public static void init(uint* _intput, uint* _output)
        {
            //bind input
            input = _intput;
            output = _output;
            if (!tableInited)
            {
                indexTable1x = (int*)Marshal.AllocHGlobal(sizeof(int) * 480 * 600);
                //256x240 -> 600X480  
                for (int y = 0; y < 480; y++)
                    for (int x = 0; x < 600; x++)
                        indexTable1x[x + y * 600] = ((int)(x * (1f / 600.0f * 256.0f))) + ((y >> 1) << 8);

                indexTable2x = (int*)Marshal.AllocHGlobal(sizeof(int) * 960 * 1196);
                //512x480 -> 1196X960  
                for (int y = 0; y < 960; y++)
                    for (int x = 0; x < 1196; x++)
                        indexTable2x[x + y * 1196] = ((int)(x * (1f / 1196.0f * 512.0f))) + ((y >> 1) << 9);

                //768*720
                indexTable3x = (int*)Marshal.AllocHGlobal(sizeof(int) * 1440 * 1792);
                for (int y = 0; y < 1440; y++)
                    for (int x = 0; x < 1792; x++)
                        indexTable3x[x + y * 1792] = ((int)(x * (1f / 1f / 1792.0f * 768.0f))) + ((y >> 1) * 768);

                clip = (int*)Marshal.AllocHGlobal(sizeof(int) * 1024);

                rates = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 256);
                for (int i = 0; i < 256; i++)
                    rates[i] = (byte)(i * 0.85f);

                clip = (int*)Marshal.AllocHGlobal(sizeof(int) * 1024);
                for (int i = 0; i < 1024; i++)
                {
                    int v = i - 512;
                    if (v > 255) v = 255;
                    else if (v < 0) v = 0;
                    clip[i] = v;
                }
                yiq_y = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 16777216);
                yiq_i = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 16777216);
                yiq_q = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 16777216);
                for (int i = 0; i <= 16777215; i++)
                {
                    int b1_o = (byte)i;//Blue
                    int b2_o = (byte)(i >> 8);//Green
                    int b3_o = (byte)(i >> 16);//Red
                    yiq_y[i] = (byte)(0.299 * b3_o + 0.587 * b2_o + 0.114 * b1_o);
                    yiq_i[i] = (byte)(((0.596 * b3_o - 0.274 * b2_o - 0.322 * b1_o) + 151.98) * (255 / (151.98 * 2)));
                    yiq_q[i] = (byte)(((0.212 * b3_o - 0.523 * b2_o + 0.311 * b1_o) + 133.365) * (255 / (133.365 * 2)));
                }

                toRGB = (int*)Marshal.AllocHGlobal(sizeof(int) * 16777216);
                for (int i = 0; i <= 16777215; i++)
                {
                    byte _y = (byte)(i >> 16);
                    byte _i = (byte)(i >> 8);
                    byte _q = (byte)i;

                    int _r = (int)(1.0 * _y + 0.956 * (_i / (255 / (151.98 * 2)) - 151.98) + 0.621 * (_q / (255 / (133.365 * 2)) - 133.365));
                    int _g = (int)(1.0 * _y - 0.272 * (_i / (255 / (151.98 * 2)) - 151.98) - 0.647 * (_q / (255 / (133.365 * 2)) - 133.365));
                    int _b = (int)(1.0 * _y - 1.105 * (_i / (255 / (151.98 * 2)) - 151.98) + 1.702 * (_q / (255 / (133.365 * 2)) - 133.365));

                    if (_r > 255) _r = 255;
                    else if (_r < 0) _r = 0;

                    if (_g > 255) _g = 255;
                    else if (_g < 0) _g = 0;

                    if (_b > 255) _b = 255;
                    else if (_b < 0) _b = 0;

                    toRGB[i] = (_r << 16) | (_g << 8) | _b;
                }
                tableInited = true;
            }
        }

        const int m3 = 0xff0000;
        const int m2 = 0xff00;
        const int m1 = 0xff;

        //height *2 , width  = ((w - 1) / 3 + 1) * 7 nearest to 4 divid

        //for 256x240 source to  600*480
        public static void ScanlineFor1x()
        {
            Parallel.For(0, 480, y =>
            {
                byte I = 0, Q = 0;
                int x = 0;
                int* line = (int*)Marshal.AllocHGlobal(2400);
                do
                {
                    int index = (int)(input[indexTable1x[x + y * 600]] & 0xffffff);
                    int Y = yiq_y[index];
                    if (((y /1) & 1) == 1) Y = rates[Y];
                    if ((x & 3) == 0)
                    {
                        I = yiq_i[index];
                        Q = yiq_q[index];
                    }
                    line[x] = toRGB[(Y << 16) | (I << 8) | Q];
                } while (++x < 600);
                x = 1;
                do
                {
                    int c_l = line[x - 1], c_o = line[x], c_r = line[x + 1];
                    output[x + y * 600] = (uint)(((((((c_r & m3) + (c_l & m3)) << 1) + (((c_o & m3)) << 2)) >> 3) & m3) | ((((((c_r & m2) + (c_l & m2)) << 1) + (((c_o & m2)) << 2)) >> 3) & m2) | ((((((c_r & m1) + (c_l & m1)) << 1) + ((c_o & m1) << 2)) >> 3) & m1));
                } while (++x < 599);
                Marshal.FreeHGlobal((IntPtr)line);
            });
        }
        //for 512x480 source to 1196 * 960
        public static void ScanlineFor2x()
        {
            //for test
            //const float p = 1f / 1196.0f * 512.0f;
            Parallel.For(0, 960, y =>
            {
                int I = 0, Q = 0, x = 0;
                int* line = (int*)Marshal.AllocHGlobal(4784);
                do
                {
                    uint index = input[indexTable2x[x + y * 1196]] & 0xffffff;
                    int Y = yiq_y[index];
                    if (((y / scanline_px) & 1) == 1) Y = rates[Y];
                    if ((x & 3) == 0)
                    {
                        I = yiq_i[index];
                        Q = yiq_q[index];
                    }
                    line[x] = toRGB[(Y << 16) | (I << 8) | Q];
                } while (++x < 1196);
                x = 1;
                do
                {
                    int c_l = line[x - 1], c_o = line[x], c_r = line[x + 1];
                    output[x + y * 1196] = (uint)(((((((c_r & m3) + (c_l & m3)) << 1) + (((c_o & m3)) << 2)) >> 3) & m3) | ((((((c_r & m2) + (c_l & m2)) << 1) + (((c_o & m2)) << 2)) >> 3) & m2) | ((((((c_r & m1) + (c_l & m1)) << 1) + ((c_o & m1) << 2)) >> 3) & m1));
                } while (++x < 1195);
                Marshal.FreeHGlobal((IntPtr)line);
            });
        }

        //for 768x720 sources to 1792*1440
        public static void ScanlineFor3x()
        {
            Parallel.For(0, 1440, y =>
            {
                int I = 0, Q = 0, x = 0;
                int* line = (int*)Marshal.AllocHGlobal(7168);
                do
                {
                    uint index = input[indexTable3x[x + y * 1792]] & 0xffffff;
                    int Y = yiq_y[index];
                    if (((y / scanline_px) & 1) == 1) Y = rates[Y];
                    if ((x & 3) == 0)
                    {
                        I = yiq_i[index];
                        Q = yiq_q[index];
                    }
                    line[x] = toRGB[(Y << 16) | (I << 8) | Q];
                } while (++x < 1792);
                x = 1;
                do
                {
                    int c_l = line[x - 1], c_o = line[x], c_r = line[x + 1];
                    output[x + y * 1792] = (uint)(((((((c_r & m3) + (c_l & m3)) << 1) + (((c_o & m3)) << 2)) >> 3) & m3) | ((((((c_r & m2) + (c_l & m2)) << 1) + (((c_o & m2)) << 2)) >> 3) & m2) | ((((((c_r & m1) + (c_l & m1)) << 1) + ((c_o & m1) << 2)) >> 3) & m1));
                } while (++x < 1791);
                Marshal.FreeHGlobal((IntPtr)line);
            });
        }
    }
}
