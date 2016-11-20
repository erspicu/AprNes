using System;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

//C# Code from https://code.google.com/p/2dimagefilter
//fix it for realtime performance
//add XBRz 6x from the least xbrz version 
// http://sourceforge.net/projects/xbrz/files/xBRZ/ C++ TO C#

namespace XBRz_speed
{
    unsafe public class HS_XBRz
    {
        const int dominantDirectionThreshold = 4;
        const int steepDirectionThreshold = 2;
        const int eqColorThres = 900;

        const int Ymask = 0x00ff0000;
        const int Umask = 0x0000ff00;
        const int Vmask = 0x000000ff;

        const int BlendNone = 0;
        const int BlendNormal = 1;
        const int BlendDominant = 2;

        const int Rot0 = 0;
        const int Rot90 = 1;
        const int Rot180 = 2;
        const int Rot270 = 3;

        //static int* lTable;

        static int* lTable_dist;

        private const int _MAX_ROTS = 4; // Number of 90 degree rotations
        private const int _MAX_SCALE = 6; // Highest possible scale
        private const int _MAX_SCALE_SQUARED = _MAX_SCALE * _MAX_SCALE;
        private static readonly Tuple[] _MATRIX_ROTATION;

        static HS_XBRz()
        {
            _MATRIX_ROTATION = new Tuple[(_MAX_SCALE - 1) * _MAX_SCALE_SQUARED * _MAX_ROTS];
            for (var n = 2; n < _MAX_SCALE + 1; n++)
                for (var r = 0; r < _MAX_ROTS; r++)
                {
                    var nr = (n - 2) * (_MAX_ROTS * _MAX_SCALE_SQUARED) + r * _MAX_SCALE_SQUARED;
                    for (var i = 0; i < _MAX_SCALE; i++)
                        for (var j = 0; j < _MAX_SCALE; j++)
                            _MATRIX_ROTATION[nr + i * _MAX_SCALE + j] =
                              _BuildMatrixRotation(r, i, j, n);
                }
        }

        static Tuple _BuildMatrixRotation(int rotDeg, int i, int j, int n)
        {
            int iOld;
            int jOld;

            if (rotDeg == 0)
            {
                iOld = i;
                jOld = j;
            }
            else
            {
                var old = _BuildMatrixRotation(rotDeg - 1, i, j, n);
                iOld = n - 1 - old.Item2;
                jOld = old.Item1;
            }

            return new Tuple(iOld, jOld);
        }

        class Tuple
        {
            public int Item1 { get; private set; }
            public int Item2 { get; private set; }

            public Tuple(int i, int j)
            {
                this.Item1 = i;
                this.Item2 = j;
            }
        }


        static byte* _preProcBuffer;


        static int* results_j;

        static int* results_k;

        static int* results_g;

        static int* results_f;

        static byte* preProcBuffer_local; // = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 256);

        public static unsafe void initTable(int sw)
        {
            if (lTable_dist != null)
                return;

            lTable_dist = (int*)Marshal.AllocHGlobal(sizeof(int) * 0x1000000);

            _preProcBuffer = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 256 * 240);

            results_f = (int*)Marshal.AllocHGlobal(sizeof(int) * 256 * 240);
            results_j = (int*)Marshal.AllocHGlobal(sizeof(int) * 256 * 240);
            results_k = (int*)Marshal.AllocHGlobal(sizeof(int) * 256 * 240);
            results_g = (int*)Marshal.AllocHGlobal(sizeof(int) * 256 * 240);

            preProcBuffer_local = (byte*)Marshal.AllocHGlobal(sizeof(byte) * 256);

            for (int i = 0; i < 0x1000000; i++)
            {

                int r_diff = ((i & 0xff0000) >> 16) * 2 - 255;
                int g_diff = ((i & 0x00ff00) >> 8) * 2 - 255;
                int b_diff = ((i & 0x0000ff) >> 0) * 2 - 255;

                double k_b = 0.0593; //ITU-R BT.2020 conversion
                double k_r = 0.2627; //
                double k_g = 1 - k_b - k_r;

                double scale_b = 0.5 / (1 - k_b);
                double scale_r = 0.5 / (1 - k_r);

                double y = k_r * r_diff + k_g * g_diff + k_b * b_diff; //[!], analog YCbCr!
                double c_b = scale_b * (b_diff - y);
                double c_r = scale_r * (r_diff - y);

                lTable_dist[i] = (int)(y * y + c_b * c_b + c_r * c_r);
            }
        }

        static int GetTopL(byte b)
        {
            return ((b) & 0x3);
        }

        static int GetTopR(byte b)
        {
            return ((b >> 2) & 0x3);
        }

        static int GetBottomR(byte b)
        {

            return ((b >> 4) & 0x3);
        }

        static int GetBottomL(byte b)
        {
            return ((b >> 6) & 0x3);
        }

        static byte SetTopL(byte b, int bt)
        {
            return (byte)(b | (byte)bt);
        }

        static byte SetTopR(byte b, int bt)
        {
            return (byte)(b | ((byte)bt << 2));
        }

        static byte SetBottomR(byte b, int bt)
        {
            return (byte)(b | ((byte)bt << 4));
        }

        static byte SetBottomL(byte b, int bt)
        {
            return (byte)(b | ((byte)bt << 6));
        }

        static byte Rotate(byte b, int rotDeg)
        {
            int l = (int)rotDeg << 1;
            int r = 8 - l;
            return (byte)(b << l | b >> r);
        }

        // static byte getAlpha(uint pix) { return (byte)((pix & 0xff000000) >> 24); }
        static byte getRed(uint pix) { return (byte)((pix & 0xff0000) >> 16); }
        static byte getGreen(uint pix) { return (byte)((pix & 0xff00) >> 8); }
        static byte getBlue(uint pix) { return (byte)(pix & 0xff); }

        static uint Interpolate(uint pixel1, uint pixel2, int quantifier1, int quantifier2)
        {
            uint total = (uint)(quantifier1 + quantifier2);

            return (uint)(
                ((((getRed(pixel1) * quantifier1 + getRed(pixel2) * quantifier2) / total) & 0xff) << 16) |
                ((((getGreen(pixel1) * quantifier1 + getGreen(pixel2) * quantifier2) / total) & 0xff) << 8) |
                (((getBlue(pixel1) * quantifier1 + getBlue(pixel2) * quantifier2) / total) & 0xff)
                );
        }

        static void _AlphaBlend(int n, int m, ImagePointer dstPtr, uint col)
        {
            dstPtr.SetPixel(Interpolate(col, dstPtr.GetPixel(), n, m - n));
        }

        static void _FillBlock(uint[] trg, int trgi, int pitch, uint col, int blockSize)
        {
            for (var y = 0; y < blockSize; ++y, trgi += pitch)
                for (var x = 0; x < blockSize; ++x)
                    trg[trgi + x] = col;
        }

        static void _FillBlock2x(uint* trg, int trgi, int pitch, uint col)
        {
            trg[trgi] = trg[trgi + 1] = col;
            trgi += pitch;
            trg[trgi] = trg[trgi + 1] = col;

        }

        static void _FillBlock3x(uint* trg, int trgi, int pitch, uint col)
        {
            trg[trgi] = trg[trgi + 1] = trg[trgi + 2] = col;
            trgi += pitch;
            trg[trgi] = trg[trgi + 1] = trg[trgi + 2] = col;
            trgi += pitch;
            trg[trgi] = trg[trgi + 1] = trg[trgi + 2] = col;
        }

        static void _FillBlock4x(uint* trg, int trgi, int pitch, uint col)
        {
            trg[trgi] = trg[trgi + 1] = trg[trgi + 2] = trg[trgi + 3] = col;
            trgi += pitch;
            trg[trgi] = trg[trgi + 1] = trg[trgi + 2] = trg[trgi + 3] = col;
            trgi += pitch;
            trg[trgi] = trg[trgi + 1] = trg[trgi + 2] = trg[trgi + 3] = col;
            trgi += pitch;
            trg[trgi] = trg[trgi + 1] = trg[trgi + 2] = trg[trgi + 3] = col;
        }

        static void _FillBlock5x(uint* trg, int trgi, int pitch, uint col)
        {
            trg[trgi] = trg[trgi + 1] = trg[trgi + 2] = trg[trgi + 3] = trg[trgi + 4] = col;
            trgi += pitch;
            trg[trgi] = trg[trgi + 1] = trg[trgi + 2] = trg[trgi + 3] = trg[trgi + 4] = col;
            trgi += pitch;
            trg[trgi] = trg[trgi + 1] = trg[trgi + 2] = trg[trgi + 3] = trg[trgi + 4] = col;
            trgi += pitch;
            trg[trgi] = trg[trgi + 1] = trg[trgi + 2] = trg[trgi + 3] = trg[trgi + 4] = col;
            trgi += pitch;
            trg[trgi] = trg[trgi + 1] = trg[trgi + 2] = trg[trgi + 3] = trg[trgi + 4] = col;
        }

        static void _FillBlock6x(uint* trg, int trgi, int pitch, uint col)
        {
            trg[trgi] = trg[trgi + 1] = trg[trgi + 2] = trg[trgi + 3] = trg[trgi + 4] = trg[trgi + 5] = col;
            trgi += pitch;
            trg[trgi] = trg[trgi + 1] = trg[trgi + 2] = trg[trgi + 3] = trg[trgi + 4] = trg[trgi + 5] = col;
            trgi += pitch;
            trg[trgi] = trg[trgi + 1] = trg[trgi + 2] = trg[trgi + 3] = trg[trgi + 4] = trg[trgi + 5] = col;
            trgi += pitch;
            trg[trgi] = trg[trgi + 1] = trg[trgi + 2] = trg[trgi + 3] = trg[trgi + 4] = trg[trgi + 5] = col;
            trgi += pitch;
            trg[trgi] = trg[trgi + 1] = trg[trgi + 2] = trg[trgi + 3] = trg[trgi + 4] = trg[trgi + 5] = col;
            trgi += pitch;
            trg[trgi] = trg[trgi + 1] = trg[trgi + 2] = trg[trgi + 3] = trg[trgi + 4] = trg[trgi + 5] = col;
        }
        static int DistYCbCr(uint pix1, uint pix2)
        {
            uint r_diff = ((pix1 & 0xff0000) >> 16) - ((pix2 & 0xff0000) >> 16);
            uint g_diff = ((pix1 & 0xff00) >> 8) - ((pix2 & 0xff00) >> 8);
            uint b_diff = (pix1 & 0xff) - (pix2 & 0xff);
            return lTable_dist[(((r_diff + 255) >> 1) << 16) | (((g_diff + 255) >> 1) << 8) | ((b_diff + 255) >> 1)];
        }

        private static bool ColorEQ(uint pix1, uint pix2)
        {
            if (pix1 == pix2) return true;
            uint r_diff = ((pix1 & 0xff0000) >> 16) - ((pix2 & 0xff0000) >> 16);
            uint g_diff = ((pix1 & 0xff00) >> 8) - ((pix2 & 0xff00) >> 8);
            uint b_diff = (pix1 & 0xff) - (pix2 & 0xff);
            return lTable_dist[(((r_diff + 255) >> 1) << 16) | (((g_diff + 255) >> 1) << 8) | ((b_diff + 255) >> 1)] < eqColorThres;
        }

        class OutputMatrix
        {
            private readonly ImagePointer _output;
            private int _outi;
            private readonly int _outWidth;
            private readonly int _n;
            private int _nr;

            public OutputMatrix(int scale, uint* output, int outWidth)
            {
                this._n = (scale - 2) * (_MAX_ROTS * _MAX_SCALE_SQUARED);
                this._output = new ImagePointer(output);
                this._outWidth = outWidth;
            }

            public void Move(int rotDeg, int outi)
            {
                this._nr = this._n + (int)rotDeg * _MAX_SCALE_SQUARED;
                this._outi = outi;
            }

            public ImagePointer Reference(int i, int j)
            {
                var rot = _MATRIX_ROTATION[this._nr + i * _MAX_SCALE + j];
                this._output.Position(this._outi + rot.Item2 + rot.Item1 * this._outWidth);
                return this._output;
            }
        }


        class ImagePointer
        {
            private uint* _imageData;
            private int _offset;

            public ImagePointer(uint* imageData)
            {
                this._imageData = imageData;
            }

            public void Position(int offset)
            {
                this._offset = offset;
            }

            public uint GetPixel()
            {
                return this._imageData[this._offset];
            }

            public void SetPixel(uint val)
            {
                this._imageData[this._offset] = val;
            }
        }


        public static void ScaleImage6X(uint* src, uint* trg, int srcWidth, int srcHeight)
        {
            int trgWidth = srcWidth * 6;

            Parallel.For(0, (srcHeight), y =>
            {
                int sM1 = Math.Max(y - 1, 0);
                int s0 = y;
                int sP1 = Math.Min(y + 1, srcHeight - 1);
                int sP2 = Math.Min(y + 2, srcHeight - 1);
                uint ker4b, ker4c, ker4e, ker4f, ker4g, ker4h, ker4i, ker4j, ker4k, ker4l, ker4n, ker4o;
                for (int x = 0; x < srcWidth; ++x)
                {
                    int blendResult_f = 0, blendResult_g = 0, blendResult_j = 0, blendResult_k = 0;

                    int xM1 = Math.Max(x - 1, 0);
                    int xP1 = Math.Min(x + 1, srcWidth - 1);
                    int xP2 = Math.Min(x + 2, srcWidth - 1);

                    int array_loc = x + y * srcWidth;

                    ker4b = src[sM1 * srcWidth + x];
                    ker4c = src[sM1 * srcWidth + xP1];

                    ker4e = src[s0 * srcWidth + xM1];
                    ker4f = src[s0 * srcWidth + x];
                    ker4g = src[s0 * srcWidth + xP1];
                    ker4h = src[s0 * srcWidth + xP2];

                    ker4i = src[sP1 * srcWidth + xM1];
                    ker4j = src[sP1 * srcWidth + x];
                    ker4k = src[sP1 * srcWidth + xP1];
                    ker4l = src[sP1 * srcWidth + xP2];

                    ker4n = src[sP2 * srcWidth + x];
                    ker4o = src[sP2 * srcWidth + xP1];


                    //--------------------------------------

                    if ((ker4f != ker4g || ker4j != ker4k) && (ker4f != ker4j || ker4g != ker4k))
                    {
                        int jg = DistYCbCr(ker4i, ker4f) + DistYCbCr(ker4f, ker4c) + DistYCbCr(ker4n, ker4k) + DistYCbCr(ker4k, ker4h) + (DistYCbCr(ker4j, ker4g) << 2);
                        int fk = DistYCbCr(ker4e, ker4j) + DistYCbCr(ker4j, ker4o) + DistYCbCr(ker4b, ker4g) + DistYCbCr(ker4g, ker4l) + (DistYCbCr(ker4f, ker4k) << 2);

                        if (jg < fk)
                        {
                            bool dominantGradient = dominantDirectionThreshold * jg < fk;
                            if (ker4f != ker4g && ker4f != ker4j)
                                blendResult_f = dominantGradient ? BlendDominant : BlendNormal;
                            if (ker4k != ker4j && ker4k != ker4g)
                                blendResult_k = dominantGradient ? BlendDominant : BlendNormal;

                        }
                        else if (fk < jg)
                        {
                            bool dominantGradient = dominantDirectionThreshold * fk < jg;
                            if (ker4j != ker4f && ker4j != ker4k)
                                blendResult_j = dominantGradient ? BlendDominant : BlendNormal;
                            if (ker4g != ker4f && ker4g != ker4k)
                                blendResult_g = dominantGradient ? BlendDominant : BlendNormal;
                        }

                    }
                    //--------------------------------------

                    results_f[array_loc] = blendResult_f;
                    results_j[array_loc] = blendResult_j;
                    results_g[array_loc] = blendResult_g;
                    results_k[array_loc] = blendResult_k;
                }
            });

            for (int y = 0; y < srcHeight; ++y)
            {
                byte blendXy1 = 0;
                int array_loc = 0;
                for (int x = 0; x < srcWidth - 1; ++x, array_loc = x + y * srcWidth)
                {
                    _preProcBuffer[array_loc] = (byte)(preProcBuffer_local[x] | ((byte)results_f[array_loc] << 4));
                    preProcBuffer_local[x] = blendXy1 = (byte)(blendXy1 | ((byte)results_j[array_loc] << 2));
                    blendXy1 = (byte)results_k[array_loc];
                    //if (x + 1 < srcWidth) 
                    preProcBuffer_local[(x + 1)] = (byte)(preProcBuffer_local[(x + 1)] | ((byte)results_g[array_loc] << 6));
                }
            }

            Parallel.For(0, srcHeight, y =>
            {
                int trgi = 6 * y * trgWidth; // scale
                int sM1 = Math.Max(y - 1, 0);
                int s0 = y;
                int sP1 = Math.Min(y + 1, srcHeight - 1);
                int sP2 = Math.Min(y + 2, srcHeight - 1);
                byte blendXy = 0;
                //-----
                int fg;
                int hc;
                bool doLineBlend;
                bool haveShallowLine;
                bool haveSteepLine;
                uint px;
                uint b, c, d, e, f, g, h, i;
                uint ker3_0, ker3_1, ker3_2, ker3_3, ker3_4, ker3_5, ker3_6, ker3_7, ker3_8;
                byte blend;

                OutputMatrix outputMatrix = new OutputMatrix(6, trg, trgWidth);

                for (int x = 0; x < srcWidth; ++x, trgi += 6)
                {



                    int xM1 = Math.Max(x - 1, 0);
                    int xP1 = Math.Min(x + 1, srcWidth - 1);
                    int xP2 = Math.Min(x + 2, srcWidth - 1);



                    blendXy = _preProcBuffer[x + y * srcWidth];

                    _FillBlock6x(trg, trgi, trgWidth, src[s0 * srcWidth + x]);


                    if (blendXy != 0)
                    {

                        ker3_0 = src[sM1 * srcWidth + xM1];
                        ker3_1 = src[sM1 * srcWidth + x];
                        ker3_2 = src[sM1 * srcWidth + xP1];

                        ker3_3 = src[s0 * srcWidth + xM1];
                        ker3_4 = src[s0 * srcWidth + x];
                        ker3_5 = src[s0 * srcWidth + xP1];

                        ker3_6 = src[sP1 * srcWidth + xM1];
                        ker3_7 = src[sP1 * srcWidth + x];
                        ker3_8 = src[sP1 * srcWidth + xP1];
                        //--
                        //--

                        b = ker3_1;
                        c = ker3_2;
                        d = ker3_3;
                        e = ker3_4;
                        f = ker3_5;
                        g = ker3_6;
                        h = ker3_7;
                        i = ker3_8;

                        blend = Rotate(blendXy, 0);

                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(0, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_6X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {

                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_6X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_6X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_6X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_6X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }
                        //-----

                        b = ker3_3;
                        c = ker3_0;
                        d = ker3_7;
                        e = ker3_4;
                        f = ker3_1;
                        g = ker3_8;
                        h = ker3_5;
                        i = ker3_2;

                        blend = Rotate(blendXy, 1);

                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(1, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_6X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {
                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_6X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_6X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_6X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_6X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }

                        //--------------------------------

                        b = ker3_7;
                        c = ker3_6;
                        d = ker3_5;
                        e = ker3_4;
                        f = ker3_3;
                        g = ker3_2;
                        h = ker3_1;
                        i = ker3_0;

                        blend = Rotate(blendXy, 2);

                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(2, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_6X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {
                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_6X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_6X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_6X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_6X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }
                        //--------------------------------
                        b = ker3_5;
                        c = ker3_8;
                        d = ker3_1;
                        e = ker3_4;
                        f = ker3_7;
                        g = ker3_0;
                        h = ker3_3;
                        i = ker3_6;
                        blend = Rotate(blendXy, 3);
                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(3, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_6X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {
                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_6X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_6X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_6X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_6X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }
                    }
                }
                //---
            });
            // });
        }

        public static void ScaleImage5X(uint* src, uint* trg, int srcWidth, int srcHeight)
        {
            int trgWidth = srcWidth * 5;

            Parallel.For(0, (srcHeight), y =>
            {
                int sM1 = Math.Max(y - 1, 0);
                int s0 = y;
                int sP1 = Math.Min(y + 1, srcHeight - 1);
                int sP2 = Math.Min(y + 2, srcHeight - 1);
                uint ker4b, ker4c, ker4e, ker4f, ker4g, ker4h, ker4i, ker4j, ker4k, ker4l, ker4n, ker4o;
                for (int x = 0; x < srcWidth; ++x)
                {
                    int blendResult_f = 0, blendResult_g = 0, blendResult_j = 0, blendResult_k = 0;

                    int xM1 = Math.Max(x - 1, 0);
                    int xP1 = Math.Min(x + 1, srcWidth - 1);
                    int xP2 = Math.Min(x + 2, srcWidth - 1);

                    int array_loc = x + y * srcWidth;

                    ker4b = src[sM1 * srcWidth + x];
                    ker4c = src[sM1 * srcWidth + xP1];

                    ker4e = src[s0 * srcWidth + xM1];
                    ker4f = src[s0 * srcWidth + x];
                    ker4g = src[s0 * srcWidth + xP1];
                    ker4h = src[s0 * srcWidth + xP2];

                    ker4i = src[sP1 * srcWidth + xM1];
                    ker4j = src[sP1 * srcWidth + x];
                    ker4k = src[sP1 * srcWidth + xP1];
                    ker4l = src[sP1 * srcWidth + xP2];

                    ker4n = src[sP2 * srcWidth + x];
                    ker4o = src[sP2 * srcWidth + xP1];


                    //--------------------------------------

                    if ((ker4f != ker4g || ker4j != ker4k) && (ker4f != ker4j || ker4g != ker4k))
                    {
                        int jg = DistYCbCr(ker4i, ker4f) + DistYCbCr(ker4f, ker4c) + DistYCbCr(ker4n, ker4k) + DistYCbCr(ker4k, ker4h) + (DistYCbCr(ker4j, ker4g) << 2);
                        int fk = DistYCbCr(ker4e, ker4j) + DistYCbCr(ker4j, ker4o) + DistYCbCr(ker4b, ker4g) + DistYCbCr(ker4g, ker4l) + (DistYCbCr(ker4f, ker4k) << 2);

                        if (jg < fk)
                        {
                            bool dominantGradient = dominantDirectionThreshold * jg < fk;
                            if (ker4f != ker4g && ker4f != ker4j)
                                blendResult_f = dominantGradient ? BlendDominant : BlendNormal;
                            if (ker4k != ker4j && ker4k != ker4g)
                                blendResult_k = dominantGradient ? BlendDominant : BlendNormal;

                        }
                        else if (fk < jg)
                        {
                            bool dominantGradient = dominantDirectionThreshold * fk < jg;
                            if (ker4j != ker4f && ker4j != ker4k)
                                blendResult_j = dominantGradient ? BlendDominant : BlendNormal;
                            if (ker4g != ker4f && ker4g != ker4k)
                                blendResult_g = dominantGradient ? BlendDominant : BlendNormal;
                        }

                    }
                    //--------------------------------------

                    results_f[array_loc] = blendResult_f;
                    results_j[array_loc] = blendResult_j;
                    results_g[array_loc] = blendResult_g;
                    results_k[array_loc] = blendResult_k;
                }
            });

            for (int y = 0; y < srcHeight; ++y)
            {
                byte blendXy1 = 0;
                int array_loc = 0;
                for (int x = 0; x < srcWidth - 1; ++x, array_loc = x + y * srcWidth)
                {
                    _preProcBuffer[array_loc] = (byte)(preProcBuffer_local[x] | ((byte)results_f[array_loc] << 4));
                    preProcBuffer_local[x] = blendXy1 = (byte)(blendXy1 | ((byte)results_j[array_loc] << 2));
                    blendXy1 = (byte)results_k[array_loc];
                    preProcBuffer_local[(x + 1)] = (byte)(preProcBuffer_local[(x + 1)] | ((byte)results_g[array_loc] << 6));
                }
            }

            Parallel.For(0, srcHeight, y =>
            {
                int trgi = 5 * y * trgWidth; // scale
                int sM1 = Math.Max(y - 1, 0);
                int s0 = y;
                int sP1 = Math.Min(y + 1, srcHeight - 1);
                int sP2 = Math.Min(y + 2, srcHeight - 1);
                byte blendXy = 0;
                //-----
                int fg;
                int hc;
                bool doLineBlend;
                bool haveShallowLine;
                bool haveSteepLine;
                uint px;
                uint b, c, d, e, f, g, h, i;
                uint ker3_0, ker3_1, ker3_2, ker3_3, ker3_4, ker3_5, ker3_6, ker3_7, ker3_8;
                byte blend;

                OutputMatrix outputMatrix = new OutputMatrix(5, trg, trgWidth);

                for (int x = 0; x < srcWidth; ++x, trgi += 5)
                {



                    int xM1 = Math.Max(x - 1, 0);
                    int xP1 = Math.Min(x + 1, srcWidth - 1);
                    int xP2 = Math.Min(x + 2, srcWidth - 1);



                    blendXy = _preProcBuffer[x + y * srcWidth];

                    _FillBlock5x(trg, trgi, trgWidth, src[s0 * srcWidth + x]);


                    if (blendXy != 0)
                    {

                        ker3_0 = src[sM1 * srcWidth + xM1];
                        ker3_1 = src[sM1 * srcWidth + x];
                        ker3_2 = src[sM1 * srcWidth + xP1];

                        ker3_3 = src[s0 * srcWidth + xM1];
                        ker3_4 = src[s0 * srcWidth + x];
                        ker3_5 = src[s0 * srcWidth + xP1];

                        ker3_6 = src[sP1 * srcWidth + xM1];
                        ker3_7 = src[sP1 * srcWidth + x];
                        ker3_8 = src[sP1 * srcWidth + xP1];
                        //--
                        //--

                        b = ker3_1;
                        c = ker3_2;
                        d = ker3_3;
                        e = ker3_4;
                        f = ker3_5;
                        g = ker3_6;
                        h = ker3_7;
                        i = ker3_8;

                        blend = Rotate(blendXy, 0);

                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(0, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_6X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {

                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_5X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_5X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_5X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_5X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }
                        //-----

                        b = ker3_3;
                        c = ker3_0;
                        d = ker3_7;
                        e = ker3_4;
                        f = ker3_1;
                        g = ker3_8;
                        h = ker3_5;
                        i = ker3_2;

                        blend = Rotate(blendXy, 1);

                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(1, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_5X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {
                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_5X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_5X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_5X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_5X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }

                        //--------------------------------

                        b = ker3_7;
                        c = ker3_6;
                        d = ker3_5;
                        e = ker3_4;
                        f = ker3_3;
                        g = ker3_2;
                        h = ker3_1;
                        i = ker3_0;

                        blend = Rotate(blendXy, 2);

                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(2, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_5X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {
                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_5X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_5X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_5X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_5X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }
                        //--------------------------------
                        b = ker3_5;
                        c = ker3_8;
                        d = ker3_1;
                        e = ker3_4;
                        f = ker3_7;
                        g = ker3_0;
                        h = ker3_3;
                        i = ker3_6;
                        blend = Rotate(blendXy, 3);
                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(3, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_5X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {
                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_5X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_5X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_5X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_5X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }
                    }
                }
                //---
            });
            // });
        }

        public static void ScaleImage4X(uint* src, uint* trg, int srcWidth, int srcHeight)
        {
            int trgWidth = srcWidth * 4;

            Parallel.For(0, (srcHeight), y =>
            {
                int sM1 = Math.Max(y - 1, 0);
                int s0 = y;
                int sP1 = Math.Min(y + 1, srcHeight - 1);
                int sP2 = Math.Min(y + 2, srcHeight - 1);
                uint ker4b, ker4c, ker4e, ker4f, ker4g, ker4h, ker4i, ker4j, ker4k, ker4l, ker4n, ker4o;
                for (int x = 0; x < srcWidth; ++x)
                {
                    int blendResult_f = 0, blendResult_g = 0, blendResult_j = 0, blendResult_k = 0;

                    int xM1 = Math.Max(x - 1, 0);
                    int xP1 = Math.Min(x + 1, srcWidth - 1);
                    int xP2 = Math.Min(x + 2, srcWidth - 1);

                    int array_loc = x + y * srcWidth;

                    ker4b = src[sM1 * srcWidth + x];
                    ker4c = src[sM1 * srcWidth + xP1];

                    ker4e = src[s0 * srcWidth + xM1];
                    ker4f = src[s0 * srcWidth + x];
                    ker4g = src[s0 * srcWidth + xP1];
                    ker4h = src[s0 * srcWidth + xP2];

                    ker4i = src[sP1 * srcWidth + xM1];
                    ker4j = src[sP1 * srcWidth + x];
                    ker4k = src[sP1 * srcWidth + xP1];
                    ker4l = src[sP1 * srcWidth + xP2];

                    ker4n = src[sP2 * srcWidth + x];
                    ker4o = src[sP2 * srcWidth + xP1];


                    //--------------------------------------

                    if ((ker4f != ker4g || ker4j != ker4k) && (ker4f != ker4j || ker4g != ker4k))
                    {
                        int jg = DistYCbCr(ker4i, ker4f) + DistYCbCr(ker4f, ker4c) + DistYCbCr(ker4n, ker4k) + DistYCbCr(ker4k, ker4h) + (DistYCbCr(ker4j, ker4g) << 2);
                        int fk = DistYCbCr(ker4e, ker4j) + DistYCbCr(ker4j, ker4o) + DistYCbCr(ker4b, ker4g) + DistYCbCr(ker4g, ker4l) + (DistYCbCr(ker4f, ker4k) << 2);

                        if (jg < fk)
                        {
                            bool dominantGradient = dominantDirectionThreshold * jg < fk;
                            if (ker4f != ker4g && ker4f != ker4j)
                                blendResult_f = dominantGradient ? BlendDominant : BlendNormal;
                            if (ker4k != ker4j && ker4k != ker4g)
                                blendResult_k = dominantGradient ? BlendDominant : BlendNormal;

                        }
                        else if (fk < jg)
                        {
                            bool dominantGradient = dominantDirectionThreshold * fk < jg;
                            if (ker4j != ker4f && ker4j != ker4k)
                                blendResult_j = dominantGradient ? BlendDominant : BlendNormal;
                            if (ker4g != ker4f && ker4g != ker4k)
                                blendResult_g = dominantGradient ? BlendDominant : BlendNormal;
                        }

                    }
                    //--------------------------------------

                    results_f[array_loc] = blendResult_f;
                    results_j[array_loc] = blendResult_j;
                    results_g[array_loc] = blendResult_g;
                    results_k[array_loc] = blendResult_k;
                }
            });

            for (int y = 0; y < srcHeight; ++y)
            {
                byte blendXy1 = 0;
                int array_loc = 0;
                for (int x = 0; x < srcWidth - 1; ++x, array_loc = x + y * srcWidth)
                {
                    _preProcBuffer[array_loc] = (byte)(preProcBuffer_local[x] | ((byte)results_f[array_loc] << 4));
                    preProcBuffer_local[x] = blendXy1 = (byte)(blendXy1 | ((byte)results_j[array_loc] << 2));
                    blendXy1 = (byte)results_k[array_loc];
                    //if (x + 1 < srcWidth) 
                    preProcBuffer_local[(x + 1)] = (byte)(preProcBuffer_local[(x + 1)] | ((byte)results_g[array_loc] << 6));
                }
            }

            Parallel.For(0, srcHeight, y =>
            {
                int trgi = 4 * y * trgWidth; // scale
                int sM1 = Math.Max(y - 1, 0);
                int s0 = y;
                int sP1 = Math.Min(y + 1, srcHeight - 1);
                int sP2 = Math.Min(y + 2, srcHeight - 1);
                byte blendXy = 0;
                //-----
                int fg;
                int hc;
                bool doLineBlend;
                bool haveShallowLine;
                bool haveSteepLine;
                uint px;
                uint b, c, d, e, f, g, h, i;
                uint ker3_0, ker3_1, ker3_2, ker3_3, ker3_4, ker3_5, ker3_6, ker3_7, ker3_8;
                byte blend;

                OutputMatrix outputMatrix = new OutputMatrix(4, trg, trgWidth);

                for (int x = 0; x < srcWidth; ++x, trgi += 4)
                {



                    int xM1 = Math.Max(x - 1, 0);
                    int xP1 = Math.Min(x + 1, srcWidth - 1);
                    int xP2 = Math.Min(x + 2, srcWidth - 1);



                    blendXy = _preProcBuffer[x + y * srcWidth];

                    _FillBlock4x(trg, trgi, trgWidth, src[s0 * srcWidth + x]);


                    if (blendXy != 0)
                    {

                        ker3_0 = src[sM1 * srcWidth + xM1];
                        ker3_1 = src[sM1 * srcWidth + x];
                        ker3_2 = src[sM1 * srcWidth + xP1];

                        ker3_3 = src[s0 * srcWidth + xM1];
                        ker3_4 = src[s0 * srcWidth + x];
                        ker3_5 = src[s0 * srcWidth + xP1];

                        ker3_6 = src[sP1 * srcWidth + xM1];
                        ker3_7 = src[sP1 * srcWidth + x];
                        ker3_8 = src[sP1 * srcWidth + xP1];
                        //--
                        //--

                        b = ker3_1;
                        c = ker3_2;
                        d = ker3_3;
                        e = ker3_4;
                        f = ker3_5;
                        g = ker3_6;
                        h = ker3_7;
                        i = ker3_8;

                        blend = Rotate(blendXy, 0);

                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(0, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_4X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {

                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_4X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_4X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_4X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_4X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }
                        //-----

                        b = ker3_3;
                        c = ker3_0;
                        d = ker3_7;
                        e = ker3_4;
                        f = ker3_1;
                        g = ker3_8;
                        h = ker3_5;
                        i = ker3_2;

                        blend = Rotate(blendXy, 1);

                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(1, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_4X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {
                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_4X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_4X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_4X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_4X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }

                        //--------------------------------

                        b = ker3_7;
                        c = ker3_6;
                        d = ker3_5;
                        e = ker3_4;
                        f = ker3_3;
                        g = ker3_2;
                        h = ker3_1;
                        i = ker3_0;

                        blend = Rotate(blendXy, 2);

                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(2, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_4X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {
                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_4X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_4X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_4X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_4X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }
                        //--------------------------------
                        b = ker3_5;
                        c = ker3_8;
                        d = ker3_1;
                        e = ker3_4;
                        f = ker3_7;
                        g = ker3_0;
                        h = ker3_3;
                        i = ker3_6;
                        blend = Rotate(blendXy, 3);
                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(3, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_4X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {
                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_4X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_4X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_4X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_4X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }
                    }
                }
                //---
            });
            // });
        }


        public static void ScaleImage3X(uint* src, uint* trg, int srcWidth, int srcHeight)
        {
            int trgWidth = srcWidth * 3;

            Parallel.For(0, (srcHeight), y =>
            {
                int sM1 = Math.Max(y - 1, 0);
                int s0 = y;
                int sP1 = Math.Min(y + 1, srcHeight - 1);
                int sP2 = Math.Min(y + 2, srcHeight - 1);
                uint ker4b, ker4c, ker4e, ker4f, ker4g, ker4h, ker4i, ker4j, ker4k, ker4l, ker4n, ker4o;
                for (int x = 0; x < srcWidth; ++x)
                {
                    int blendResult_f = 0, blendResult_g = 0, blendResult_j = 0, blendResult_k = 0;

                    int xM1 = Math.Max(x - 1, 0);
                    int xP1 = Math.Min(x + 1, srcWidth - 1);
                    int xP2 = Math.Min(x + 2, srcWidth - 1);

                    int array_loc = x + y * srcWidth;

                    ker4b = src[sM1 * srcWidth + x];
                    ker4c = src[sM1 * srcWidth + xP1];

                    ker4e = src[s0 * srcWidth + xM1];
                    ker4f = src[s0 * srcWidth + x];
                    ker4g = src[s0 * srcWidth + xP1];
                    ker4h = src[s0 * srcWidth + xP2];

                    ker4i = src[sP1 * srcWidth + xM1];
                    ker4j = src[sP1 * srcWidth + x];
                    ker4k = src[sP1 * srcWidth + xP1];
                    ker4l = src[sP1 * srcWidth + xP2];

                    ker4n = src[sP2 * srcWidth + x];
                    ker4o = src[sP2 * srcWidth + xP1];


                    //--------------------------------------

                    if ((ker4f != ker4g || ker4j != ker4k) && (ker4f != ker4j || ker4g != ker4k))
                    {
                        int jg = DistYCbCr(ker4i, ker4f) + DistYCbCr(ker4f, ker4c) + DistYCbCr(ker4n, ker4k) + DistYCbCr(ker4k, ker4h) + (DistYCbCr(ker4j, ker4g) << 2);
                        int fk = DistYCbCr(ker4e, ker4j) + DistYCbCr(ker4j, ker4o) + DistYCbCr(ker4b, ker4g) + DistYCbCr(ker4g, ker4l) + (DistYCbCr(ker4f, ker4k) << 2);

                        if (jg < fk)
                        {
                            bool dominantGradient = dominantDirectionThreshold * jg < fk;
                            if (ker4f != ker4g && ker4f != ker4j)
                                blendResult_f = dominantGradient ? BlendDominant : BlendNormal;
                            if (ker4k != ker4j && ker4k != ker4g)
                                blendResult_k = dominantGradient ? BlendDominant : BlendNormal;

                        }
                        else if (fk < jg)
                        {
                            bool dominantGradient = dominantDirectionThreshold * fk < jg;
                            if (ker4j != ker4f && ker4j != ker4k)
                                blendResult_j = dominantGradient ? BlendDominant : BlendNormal;
                            if (ker4g != ker4f && ker4g != ker4k)
                                blendResult_g = dominantGradient ? BlendDominant : BlendNormal;
                        }

                    }
                    //--------------------------------------

                    results_f[array_loc] = blendResult_f;
                    results_j[array_loc] = blendResult_j;
                    results_g[array_loc] = blendResult_g;
                    results_k[array_loc] = blendResult_k;
                }
            });

            for (int y = 0; y < srcHeight; ++y)
            {
                byte blendXy1 = 0;
                int array_loc = 0;
                for (int x = 0; x < srcWidth - 1; ++x, array_loc = x + y * srcWidth)
                {
                    _preProcBuffer[array_loc] = (byte)(preProcBuffer_local[x] | ((byte)results_f[array_loc] << 4));
                    preProcBuffer_local[x] = blendXy1 = (byte)(blendXy1 | ((byte)results_j[array_loc] << 2));
                    blendXy1 = (byte)results_k[array_loc];
                    //if (x + 1 < srcWidth) 
                    preProcBuffer_local[(x + 1)] = (byte)(preProcBuffer_local[(x + 1)] | ((byte)results_g[array_loc] << 6));
                }
            }

            Parallel.For(0, srcHeight, y =>
            {
                int trgi = 3 * y * trgWidth; // scale
                int sM1 = Math.Max(y - 1, 0);
                int s0 = y;
                int sP1 = Math.Min(y + 1, srcHeight - 1);
                int sP2 = Math.Min(y + 2, srcHeight - 1);
                byte blendXy = 0;
                //-----
                int fg;
                int hc;
                bool doLineBlend;
                bool haveShallowLine;
                bool haveSteepLine;
                uint px;
                uint b, c, d, e, f, g, h, i;
                uint ker3_0, ker3_1, ker3_2, ker3_3, ker3_4, ker3_5, ker3_6, ker3_7, ker3_8;
                byte blend;

                OutputMatrix outputMatrix = new OutputMatrix(3, trg, trgWidth);

                for (int x = 0; x < srcWidth; ++x, trgi += 3)
                {



                    int xM1 = Math.Max(x - 1, 0);
                    int xP1 = Math.Min(x + 1, srcWidth - 1);
                    int xP2 = Math.Min(x + 2, srcWidth - 1);



                    blendXy = _preProcBuffer[x + y * srcWidth];

                    _FillBlock3x(trg, trgi, trgWidth, src[s0 * srcWidth + x]);


                    if (blendXy != 0)
                    {

                        ker3_0 = src[sM1 * srcWidth + xM1];
                        ker3_1 = src[sM1 * srcWidth + x];
                        ker3_2 = src[sM1 * srcWidth + xP1];

                        ker3_3 = src[s0 * srcWidth + xM1];
                        ker3_4 = src[s0 * srcWidth + x];
                        ker3_5 = src[s0 * srcWidth + xP1];

                        ker3_6 = src[sP1 * srcWidth + xM1];
                        ker3_7 = src[sP1 * srcWidth + x];
                        ker3_8 = src[sP1 * srcWidth + xP1];
                        //--
                        //--

                        b = ker3_1;
                        c = ker3_2;
                        d = ker3_3;
                        e = ker3_4;
                        f = ker3_5;
                        g = ker3_6;
                        h = ker3_7;
                        i = ker3_8;

                        blend = Rotate(blendXy, 0);

                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(0, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_3X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {

                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_3X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_3X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_3X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_3X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }
                        //-----

                        b = ker3_3;
                        c = ker3_0;
                        d = ker3_7;
                        e = ker3_4;
                        f = ker3_1;
                        g = ker3_8;
                        h = ker3_5;
                        i = ker3_2;

                        blend = Rotate(blendXy, 1);

                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(1, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_3X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {
                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_3X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_3X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_3X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_3X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }

                        //--------------------------------

                        b = ker3_7;
                        c = ker3_6;
                        d = ker3_5;
                        e = ker3_4;
                        f = ker3_3;
                        g = ker3_2;
                        h = ker3_1;
                        i = ker3_0;

                        blend = Rotate(blendXy, 2);

                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(2, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_3X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {
                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_3X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_3X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_3X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_3X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }
                        //--------------------------------
                        b = ker3_5;
                        c = ker3_8;
                        d = ker3_1;
                        e = ker3_4;
                        f = ker3_7;
                        g = ker3_0;
                        h = ker3_3;
                        i = ker3_6;
                        blend = Rotate(blendXy, 3);
                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(3, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_3X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {
                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_3X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_3X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_3X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_3X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }
                    }
                }
                //---
            });
            // });
        }


        public static void ScaleImage2X(uint* src, uint* trg, int srcWidth, int srcHeight)
        {
            int trgWidth = srcWidth * 2;

            Parallel.For(0, (srcHeight), y =>
            {
                int sM1 = Math.Max(y - 1, 0);
                int s0 = y;
                int sP1 = Math.Min(y + 1, srcHeight - 1);
                int sP2 = Math.Min(y + 2, srcHeight - 1);
                uint ker4b, ker4c, ker4e, ker4f, ker4g, ker4h, ker4i, ker4j, ker4k, ker4l, ker4n, ker4o;
                for (int x = 0; x < srcWidth; ++x)
                {
                    int blendResult_f = 0, blendResult_g = 0, blendResult_j = 0, blendResult_k = 0;

                    int xM1 = Math.Max(x - 1, 0);
                    int xP1 = Math.Min(x + 1, srcWidth - 1);
                    int xP2 = Math.Min(x + 2, srcWidth - 1);

                    int array_loc = x + y * srcWidth;

                    ker4b = src[sM1 * srcWidth + x];
                    ker4c = src[sM1 * srcWidth + xP1];

                    ker4e = src[s0 * srcWidth + xM1];
                    ker4f = src[s0 * srcWidth + x];
                    ker4g = src[s0 * srcWidth + xP1];
                    ker4h = src[s0 * srcWidth + xP2];

                    ker4i = src[sP1 * srcWidth + xM1];
                    ker4j = src[sP1 * srcWidth + x];
                    ker4k = src[sP1 * srcWidth + xP1];
                    ker4l = src[sP1 * srcWidth + xP2];

                    ker4n = src[sP2 * srcWidth + x];
                    ker4o = src[sP2 * srcWidth + xP1];


                    //--------------------------------------

                    if ((ker4f != ker4g || ker4j != ker4k) && (ker4f != ker4j || ker4g != ker4k))
                    {
                        int jg = DistYCbCr(ker4i, ker4f) + DistYCbCr(ker4f, ker4c) + DistYCbCr(ker4n, ker4k) + DistYCbCr(ker4k, ker4h) + (DistYCbCr(ker4j, ker4g) << 2);
                        int fk = DistYCbCr(ker4e, ker4j) + DistYCbCr(ker4j, ker4o) + DistYCbCr(ker4b, ker4g) + DistYCbCr(ker4g, ker4l) + (DistYCbCr(ker4f, ker4k) << 2);

                        if (jg < fk)
                        {
                            bool dominantGradient = dominantDirectionThreshold * jg < fk;
                            if (ker4f != ker4g && ker4f != ker4j)
                                blendResult_f = dominantGradient ? BlendDominant : BlendNormal;
                            if (ker4k != ker4j && ker4k != ker4g)
                                blendResult_k = dominantGradient ? BlendDominant : BlendNormal;

                        }
                        else if (fk < jg)
                        {
                            bool dominantGradient = dominantDirectionThreshold * fk < jg;
                            if (ker4j != ker4f && ker4j != ker4k)
                                blendResult_j = dominantGradient ? BlendDominant : BlendNormal;
                            if (ker4g != ker4f && ker4g != ker4k)
                                blendResult_g = dominantGradient ? BlendDominant : BlendNormal;
                        }

                    }
                    //--------------------------------------

                    results_f[array_loc] = blendResult_f;
                    results_j[array_loc] = blendResult_j;
                    results_g[array_loc] = blendResult_g;
                    results_k[array_loc] = blendResult_k;
                }
            });

            for (int y = 0; y < srcHeight; ++y)
            {
                byte blendXy1 = 0;
                int array_loc = 0;
                for (int x = 0; x < srcWidth - 1; ++x, array_loc = x + y * srcWidth)
                {
                    _preProcBuffer[array_loc] = (byte)(preProcBuffer_local[x] | ((byte)results_f[array_loc] << 4));
                    preProcBuffer_local[x] = blendXy1 = (byte)(blendXy1 | ((byte)results_j[array_loc] << 2));
                    blendXy1 = (byte)results_k[array_loc];
                    //if (x + 1 < srcWidth) 
                    preProcBuffer_local[(x + 1)] = (byte)(preProcBuffer_local[(x + 1)] | ((byte)results_g[array_loc] << 6));
                }
            }

            Parallel.For(0, srcHeight, y =>
            {
                int trgi = 2 * y * trgWidth; // scale
                int sM1 = Math.Max(y - 1, 0);
                int s0 = y;
                int sP1 = Math.Min(y + 1, srcHeight - 1);
                int sP2 = Math.Min(y + 2, srcHeight - 1);
                byte blendXy = 0;
                //-----
                int fg;
                int hc;
                bool doLineBlend;
                bool haveShallowLine;
                bool haveSteepLine;
                uint px;
                uint b, c, d, e, f, g, h, i;
                uint ker3_0, ker3_1, ker3_2, ker3_3, ker3_4, ker3_5, ker3_6, ker3_7, ker3_8;
                byte blend;

                OutputMatrix outputMatrix = new OutputMatrix(2, trg, trgWidth);

                for (int x = 0; x < srcWidth; ++x, trgi += 2)
                {



                    int xM1 = Math.Max(x - 1, 0);
                    int xP1 = Math.Min(x + 1, srcWidth - 1);
                    int xP2 = Math.Min(x + 2, srcWidth - 1);



                    blendXy = _preProcBuffer[x + y * srcWidth];

                    _FillBlock2x(trg, trgi, trgWidth, src[s0 * srcWidth + x]);


                    if (blendXy != 0)
                    {

                        ker3_0 = src[sM1 * srcWidth + xM1];
                        ker3_1 = src[sM1 * srcWidth + x];
                        ker3_2 = src[sM1 * srcWidth + xP1];

                        ker3_3 = src[s0 * srcWidth + xM1];
                        ker3_4 = src[s0 * srcWidth + x];
                        ker3_5 = src[s0 * srcWidth + xP1];

                        ker3_6 = src[sP1 * srcWidth + xM1];
                        ker3_7 = src[sP1 * srcWidth + x];
                        ker3_8 = src[sP1 * srcWidth + xP1];
                        //--
                        //--

                        b = ker3_1;
                        c = ker3_2;
                        d = ker3_3;
                        e = ker3_4;
                        f = ker3_5;
                        g = ker3_6;
                        h = ker3_7;
                        i = ker3_8;

                        blend = Rotate(blendXy, 0);

                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(0, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_2X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {

                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_2X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_2X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_2X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_2X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }
                        //-----

                        b = ker3_3;
                        c = ker3_0;
                        d = ker3_7;
                        e = ker3_4;
                        f = ker3_1;
                        g = ker3_8;
                        h = ker3_5;
                        i = ker3_2;

                        blend = Rotate(blendXy, 1);

                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(1, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_2X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {
                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_2X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_2X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_2X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_2X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }

                        //--------------------------------

                        b = ker3_7;
                        c = ker3_6;
                        d = ker3_5;
                        e = ker3_4;
                        f = ker3_3;
                        g = ker3_2;
                        h = ker3_1;
                        i = ker3_0;

                        blend = Rotate(blendXy, 2);

                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(2, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_2X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {
                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_2X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_2X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_2X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_2X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }
                        //--------------------------------
                        b = ker3_5;
                        c = ker3_8;
                        d = ker3_1;
                        e = ker3_4;
                        f = ker3_7;
                        g = ker3_0;
                        h = ker3_3;
                        i = ker3_6;
                        blend = Rotate(blendXy, 3);
                        if (GetBottomR(blend) != BlendNone)
                        {
                            if (GetBottomR(blend) >= BlendDominant)
                                doLineBlend = true;
                            else if (GetTopR(blend) != BlendNone && !ColorEQ(e, g))
                                doLineBlend = false;
                            else if (GetBottomL(blend) != BlendNone && !ColorEQ(e, c))
                                doLineBlend = false;
                            else if (ColorEQ(g, h) && ColorEQ(h, i) && ColorEQ(i, f) && ColorEQ(f, c) && !ColorEQ(e, i))
                                doLineBlend = false;
                            else
                                doLineBlend = true;
                            px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;

                            outputMatrix.Move(3, trgi);

                            if (!doLineBlend)
                            {
                                Scaler_2X.BlendCorner(px, outputMatrix);

                            }
                            else
                            {
                                fg = DistYCbCr(f, g);
                                hc = DistYCbCr(h, c);

                                haveShallowLine = steepDirectionThreshold * fg <= hc && e != g && d != g;
                                haveSteepLine = steepDirectionThreshold * hc <= fg && e != c && b != c;

                                if (haveShallowLine)
                                {
                                    if (haveSteepLine)
                                        Scaler_2X.BlendLineSteepAndShallow(px, outputMatrix);
                                    else
                                        Scaler_2X.BlendLineShallow(px, outputMatrix);
                                }
                                else
                                {
                                    if (haveSteepLine)
                                        Scaler_2X.BlendLineSteep(px, outputMatrix);
                                    else
                                        Scaler_2X.BlendLineDiagonal(px, outputMatrix);
                                }
                            }
                        }
                    }
                }
                //---
            });
            // });
        }















        #region Scaler
        private class Scaler_2X
        {
            private const int _SCALE = 2;

            public static void BlendLineShallow(uint col, OutputMatrix output)
            {
                _AlphaBlend(1, 4, output.Reference(_SCALE - 1, 0), col);
                _AlphaBlend(3, 4, output.Reference(_SCALE - 1, 1), col);
            }

            public static void BlendLineSteep(uint col, OutputMatrix output)
            {
                _AlphaBlend(1, 4, output.Reference(0, _SCALE - 1), col);
                _AlphaBlend(3, 4, output.Reference(1, _SCALE - 1), col);
            }

            public static void BlendLineSteepAndShallow(uint col, OutputMatrix output)
            {
                _AlphaBlend(1, 4, output.Reference(1, 0), col);
                _AlphaBlend(1, 4, output.Reference(0, 1), col);
                _AlphaBlend(5, 6, output.Reference(1, 1), col); //[!] fixes 7/8 used in xBR
            }

            public static void BlendLineDiagonal(uint col, OutputMatrix output)
            {
                _AlphaBlend(1, 2, output.Reference(1, 1), col);
            }

            public static void BlendCorner(uint col, OutputMatrix output)
            {
                _AlphaBlend(21, 100, output.Reference(1, 1), col); //exact: 1 - pi/4 = 0.2146018366
            }
        }

        private class Scaler_3X
        {
            private const int _SCALE = 3;

            public static void BlendLineShallow(uint col, OutputMatrix output)
            {
                _AlphaBlend(1, 4, output.Reference(_SCALE - 1, 0), col);
                _AlphaBlend(1, 4, output.Reference(_SCALE - 2, 2), col);
                _AlphaBlend(3, 4, output.Reference(_SCALE - 1, 1), col);
                output.Reference(_SCALE - 1, 2).SetPixel(col);
            }

            public static void BlendLineSteep(uint col, OutputMatrix output)
            {
                _AlphaBlend(1, 4, output.Reference(0, _SCALE - 1), col);
                _AlphaBlend(1, 4, output.Reference(2, _SCALE - 2), col);
                _AlphaBlend(3, 4, output.Reference(1, _SCALE - 1), col);
                output.Reference(2, _SCALE - 1).SetPixel(col);
            }

            public static void BlendLineSteepAndShallow(uint col, OutputMatrix output)
            {
                _AlphaBlend(1, 4, output.Reference(2, 0), col);
                _AlphaBlend(1, 4, output.Reference(0, 2), col);
                _AlphaBlend(3, 4, output.Reference(2, 1), col);
                _AlphaBlend(3, 4, output.Reference(1, 2), col);
                output.Reference(2, 2).SetPixel(col);
            }

            public static void BlendLineDiagonal(uint col, OutputMatrix output)
            {
                _AlphaBlend(1, 8, output.Reference(1, 2), col);
                _AlphaBlend(1, 8, output.Reference(2, 1), col);
                _AlphaBlend(7, 8, output.Reference(2, 2), col);
            }

            public static void BlendCorner(uint col, OutputMatrix output)
            {

                _AlphaBlend(45, 100, output.Reference(2, 2), col); //exact: 0.4545939598
            }
        }

        private class Scaler_4X
        {
            private const int _SCALE = 4;

            public static void BlendLineShallow(uint col, OutputMatrix output)
            {
                _AlphaBlend(1, 4, output.Reference(_SCALE - 1, 0), col);
                _AlphaBlend(1, 4, output.Reference(_SCALE - 2, 2), col);
                _AlphaBlend(3, 4, output.Reference(_SCALE - 1, 1), col);
                _AlphaBlend(3, 4, output.Reference(_SCALE - 2, 3), col);
                output.Reference(_SCALE - 1, 2).SetPixel(col);
                output.Reference(_SCALE - 1, 3).SetPixel(col);
            }

            public static void BlendLineSteep(uint col, OutputMatrix output)
            {
                _AlphaBlend(1, 4, output.Reference(0, _SCALE - 1), col);
                _AlphaBlend(1, 4, output.Reference(2, _SCALE - 2), col);
                _AlphaBlend(3, 4, output.Reference(1, _SCALE - 1), col);
                _AlphaBlend(3, 4, output.Reference(3, _SCALE - 2), col);
                output.Reference(2, _SCALE - 1).SetPixel(col);
                output.Reference(3, _SCALE - 1).SetPixel(col);
            }

            public static void BlendLineSteepAndShallow(uint col, OutputMatrix output)
            {
                _AlphaBlend(3, 4, output.Reference(3, 1), col);
                _AlphaBlend(3, 4, output.Reference(1, 3), col);
                _AlphaBlend(1, 4, output.Reference(3, 0), col);
                _AlphaBlend(1, 4, output.Reference(0, 3), col);
                _AlphaBlend(1, 3, output.Reference(2, 2), col); //[!] fixes 1/4 used in xBR
                output.Reference(3, 3).SetPixel(col);
                output.Reference(3, 2).SetPixel(col);
                output.Reference(2, 3).SetPixel(col);
            }

            public static void BlendLineDiagonal(uint col, OutputMatrix output)
            {
                _AlphaBlend(1, 2, output.Reference(_SCALE - 1, _SCALE / 2), col);
                _AlphaBlend(1, 2, output.Reference(_SCALE - 2, _SCALE / 2 + 1), col);
                output.Reference(_SCALE - 1, _SCALE - 1).SetPixel(col);
            }

            public static void BlendCorner(uint col, OutputMatrix output)
            {
                //model a round corner
                _AlphaBlend(68, 100, output.Reference(3, 3), col); //exact: 0.6848532563
                _AlphaBlend(9, 100, output.Reference(3, 2), col); //0.08677704501
                _AlphaBlend(9, 100, output.Reference(2, 3), col); //0.08677704501
            }
        }

        private class Scaler_5X
        {
            private const int _SCALE = 5;

            public static void BlendLineShallow(uint col, OutputMatrix output)
            {
                _AlphaBlend(1, 4, output.Reference(_SCALE - 1, 0), col);
                _AlphaBlend(1, 4, output.Reference(_SCALE - 2, 2), col);
                _AlphaBlend(1, 4, output.Reference(_SCALE - 3, 4), col);
                _AlphaBlend(3, 4, output.Reference(_SCALE - 1, 1), col);
                _AlphaBlend(3, 4, output.Reference(_SCALE - 2, 3), col);
                output.Reference(_SCALE - 1, 2).SetPixel(col);
                output.Reference(_SCALE - 1, 3).SetPixel(col);
                output.Reference(_SCALE - 1, 4).SetPixel(col);
                output.Reference(_SCALE - 2, 4).SetPixel(col);
            }

            public static void BlendLineSteep(uint col, OutputMatrix output)
            {
                _AlphaBlend(1, 4, output.Reference(0, _SCALE - 1), col);
                _AlphaBlend(1, 4, output.Reference(2, _SCALE - 2), col);
                _AlphaBlend(1, 4, output.Reference(4, _SCALE - 3), col);
                _AlphaBlend(3, 4, output.Reference(1, _SCALE - 1), col);
                _AlphaBlend(3, 4, output.Reference(3, _SCALE - 2), col);
                output.Reference(2, _SCALE - 1).SetPixel(col);
                output.Reference(3, _SCALE - 1).SetPixel(col);
                output.Reference(4, _SCALE - 1).SetPixel(col);
                output.Reference(4, _SCALE - 2).SetPixel(col);
            }

            public static void BlendLineSteepAndShallow(uint col, OutputMatrix output)
            {
                _AlphaBlend(1, 4, output.Reference(0, _SCALE - 1), col);
                _AlphaBlend(1, 4, output.Reference(2, _SCALE - 2), col);
                _AlphaBlend(3, 4, output.Reference(1, _SCALE - 1), col);
                _AlphaBlend(1, 4, output.Reference(_SCALE - 1, 0), col);
                _AlphaBlend(1, 4, output.Reference(_SCALE - 2, 2), col);
                _AlphaBlend(3, 4, output.Reference(_SCALE - 1, 1), col);
                output.Reference(2, _SCALE - 1).SetPixel(col);
                output.Reference(3, _SCALE - 1).SetPixel(col);
                output.Reference(_SCALE - 1, 2).SetPixel(col);
                output.Reference(_SCALE - 1, 3).SetPixel(col);
                output.Reference(4, _SCALE - 1).SetPixel(col);
                _AlphaBlend(2, 3, output.Reference(3, 3), col);
            }

            public static void BlendLineDiagonal(uint col, OutputMatrix output)
            {
                _AlphaBlend(1, 8, output.Reference(_SCALE - 1, _SCALE / 2), col);
                _AlphaBlend(1, 8, output.Reference(_SCALE - 2, _SCALE / 2 + 1), col);
                _AlphaBlend(1, 8, output.Reference(_SCALE - 3, _SCALE / 2 + 2), col);
                _AlphaBlend(7, 8, output.Reference(4, 3), col);
                _AlphaBlend(7, 8, output.Reference(3, 4), col);
                output.Reference(4, 4).SetPixel(col);
            }

            public static void BlendCorner(uint col, OutputMatrix output)
            {
                _AlphaBlend(86, 100, output.Reference(4, 4), col); //exact: 0.8631434088
                _AlphaBlend(23, 100, output.Reference(4, 3), col); //0.2306749731
                _AlphaBlend(23, 100, output.Reference(3, 4), col); //0.2306749731
            }
        }

        private class Scaler_6X //editing
        {
            private const int _SCALE = 6;

            public static void BlendLineShallow(uint col, OutputMatrix output)
            {
                _AlphaBlend(1, 4, output.Reference(_SCALE - 1, 0), col);
                _AlphaBlend(1, 4, output.Reference(_SCALE - 2, 2), col);
                _AlphaBlend(1, 4, output.Reference(_SCALE - 3, 4), col);

                _AlphaBlend(3, 4, output.Reference(_SCALE - 1, 1), col);
                _AlphaBlend(3, 4, output.Reference(_SCALE - 2, 3), col);
                _AlphaBlend(3, 4, output.Reference(_SCALE - 3, 5), col);

                output.Reference(_SCALE - 1, 2).SetPixel(col);
                output.Reference(_SCALE - 1, 3).SetPixel(col);
                output.Reference(_SCALE - 1, 4).SetPixel(col);
                output.Reference(_SCALE - 1, 5).SetPixel(col);

                output.Reference(_SCALE - 2, 4).SetPixel(col);
                output.Reference(_SCALE - 2, 5).SetPixel(col);
            }

            public static void BlendLineSteep(uint col, OutputMatrix output)//ok
            {
                _AlphaBlend(1, 4, output.Reference(0, _SCALE - 1), col);
                _AlphaBlend(1, 4, output.Reference(2, _SCALE - 2), col);
                _AlphaBlend(1, 4, output.Reference(4, _SCALE - 3), col);

                _AlphaBlend(3, 4, output.Reference(1, _SCALE - 1), col);
                _AlphaBlend(3, 4, output.Reference(3, _SCALE - 2), col);
                _AlphaBlend(3, 4, output.Reference(5, _SCALE - 3), col);

                output.Reference(2, _SCALE - 1).SetPixel(col);
                output.Reference(3, _SCALE - 1).SetPixel(col);
                output.Reference(4, _SCALE - 1).SetPixel(col);
                output.Reference(5, _SCALE - 1).SetPixel(col);

                output.Reference(4, _SCALE - 2).SetPixel(col);
                output.Reference(5, _SCALE - 2).SetPixel(col);
            }

            public static void BlendLineSteepAndShallow(uint col, OutputMatrix output) //ok
            {
                _AlphaBlend(1, 4, output.Reference(0, _SCALE - 1), col);
                _AlphaBlend(1, 4, output.Reference(2, _SCALE - 2), col);
                _AlphaBlend(3, 4, output.Reference(1, _SCALE - 1), col);
                _AlphaBlend(3, 4, output.Reference(3, _SCALE - 2), col);

                _AlphaBlend(1, 4, output.Reference(_SCALE - 1, 0), col);
                _AlphaBlend(1, 4, output.Reference(_SCALE - 2, 2), col);
                _AlphaBlend(3, 4, output.Reference(_SCALE - 1, 1), col);
                _AlphaBlend(3, 4, output.Reference(_SCALE - 2, 3), col);

                output.Reference(2, _SCALE - 1).SetPixel(col);
                output.Reference(3, _SCALE - 1).SetPixel(col);
                output.Reference(4, _SCALE - 1).SetPixel(col);
                output.Reference(5, _SCALE - 1).SetPixel(col);

                output.Reference(4, _SCALE - 2).SetPixel(col);
                output.Reference(5, _SCALE - 2).SetPixel(col);

                output.Reference(_SCALE - 1, 2).SetPixel(col);
                output.Reference(_SCALE - 1, 3).SetPixel(col);
            }

            public static void BlendLineDiagonal(uint col, OutputMatrix output) //ok
            {


                _AlphaBlend(1, 2, output.Reference(_SCALE - 1, _SCALE / 2), col);
                _AlphaBlend(1, 2, output.Reference(_SCALE - 2, _SCALE / 2 + 1), col);
                _AlphaBlend(1, 2, output.Reference(_SCALE - 3, _SCALE / 2 + 2), col);

                output.Reference(_SCALE - 2, _SCALE - 1).SetPixel(col);
                output.Reference(_SCALE - 1, _SCALE - 1).SetPixel(col);
                output.Reference(_SCALE - 1, _SCALE - 2).SetPixel(col);

            }

            public static void BlendCorner(uint col, OutputMatrix output) //ok
            {
                _AlphaBlend(97, 100, output.Reference(5, 5), col); //exact: 0.9711013910
                _AlphaBlend(42, 100, output.Reference(4, 5), col); //0.4236372243
                _AlphaBlend(42, 100, output.Reference(5, 4), col); //0.4236372243
                _AlphaBlend(6, 100, output.Reference(5, 3), col); //0.05652034508
                _AlphaBlend(6, 100, output.Reference(3, 5), col); //0.05652034508
            }
        }
        #endregion
    }
}
