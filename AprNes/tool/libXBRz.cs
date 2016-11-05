using System;
using System.Threading.Tasks;


//C# Code from https://code.google.com/p/2dimagefilter
//fix it for realtime performance
//add XBRz 6x from the least xbrz version 
// http://sourceforge.net/projects/xbrz/files/xBRZ/ C++ TO C#

namespace XBRz_speed
{
    public class HS_XBRz
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

        static int[] lTable;

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

        public static unsafe void initTable()
        {
            if (lTable != null)
            {
                return;
            }
            lTable = new int[0x1000000];

            for (uint i = 0; i < lTable.Length; i++)
            {

                float r = (i & 0xff0000) >> 16;
                float g = (i & 0x00ff00) >> 8;
                float b = (i & 0x0000ff);

                lTable[i] = (byte)(.299 * r + .587 * g + .114 * b) | ((byte)((int)(-.169 * r - .331 * g + .5 * b) + 128) << 8) | ((byte)((int)(.5 * r - .419 * g - .081 * b) + 128) << 16);
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

        static byte getAlpha(uint pix) { return (byte)((pix & 0xff000000) >> 24); }
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

        static int DistYCbCr(uint pix1, uint pix2)
        {

            if (pix1 == pix2) return 0;

            int YUV1 = lTable[pix1 & 0x00ffffff];
            int YUV2 = lTable[pix2 & 0x00ffffff];

            int y = ((YUV1 & Ymask) >> 16) - ((YUV2 & Ymask) >> 16);
            int u = ((YUV1 & Umask) >> 8) - ((YUV2 & Umask) >> 8);
            int v = (YUV1 & Vmask) - (YUV2 & Vmask);

            return y * y + u * u + v * v;
        }

        private static bool ColorEQ(uint pix1, uint pix2)
        {
            if (pix1 == pix2) return true;
            return DistYCbCr(pix1, pix2) < eqColorThres;
        }

        class OutputMatrix
        {
            private readonly ImagePointer _output;
            private int _outi;
            private readonly int _outWidth;
            private readonly int _n;
            private int _nr;

            public OutputMatrix(int scale, uint[] output, int outWidth)
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
            private uint[] _imageData;
            private int _offset;

            public ImagePointer(uint[] imageData)
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

        /* no use for nes 
        public static void ScaleImage6X(uint[][] src, uint[] trg, int srcWidth, int srcHeight)
        {
            int trgWidth = srcWidth * 6;

            byte[] preProcBuffer = new byte[srcWidth];

            OutputMatrix outputMatrix = new OutputMatrix(6, trg, trgWidth); //scale

            for (int y = 0; y < srcHeight; ++y)
            {
                int trgi = 6 * y * trgWidth; // scale
                int sM1 = Math.Max(y - 1, 0);
                int s0 = y;
                int sP1 = Math.Min(y + 1, srcHeight - 1);
                int sP2 = Math.Min(y + 2, srcHeight - 1);
                byte blendXy1 = 0;
                byte blendXy = 0;
                //-----
                int fg;
                int hc;
                bool doLineBlend;
                bool haveShallowLine;
                bool haveSteepLine;
                uint px;
                uint b, c, d, e, f, g, h, i;
                uint ker4b, ker4c, ker4e, ker4f, ker4g, ker4h, ker4i, ker4j, ker4k, ker4l, ker4n, ker4o;
                uint ker3_0, ker3_1, ker3_2, ker3_3, ker3_4, ker3_5, ker3_6, ker3_7, ker3_8;
                byte blend;

                for (int x = 0; x < srcWidth; ++x, trgi += 6) //scale
                {
                    int blendResult_f = 0, blendResult_g = 0, blendResult_j = 0, blendResult_k = 0;

                    int xM1 = Math.Max(x - 1, 0);
                    int xP1 = Math.Min(x + 1, srcWidth - 1);
                    int xP2 = Math.Min(x + 2, srcWidth - 1);

                    ker4b = src[x][sM1];    //src[(sM1 + x) % srcWidth][(sM1 + x) / srcWidth];
                    ker4c = src[xP1][sM1]; //src[(sM1 + xP1) % srcWidth][(sM1 + xP1) / srcWidth];

                    ker4e = src[xM1][s0]; //src[(s0 + xM1) % srcWidth][(s0 + xM1) / srcWidth];
                    ker4f = src[x][s0]; //src[(s0 + x) % srcWidth][(s0 + x) /srcWidth];
                    ker4g = src[xP1][s0];//  //src[(s0 + xP1) % srcWidth][(s0 + xP1) / srcWidth];
                    ker4h = src[xP2][s0]; //src[(s0 + xP2) % srcWidth][(s0 + xP2)/srcWidth];

                    ker4i = src[xM1][sP1]; //src[(sP1 + xM1) % srcWidth][(sP1 + xM1)/srcWidth];
                    ker4j = src[x][sP1]; //src[(sP1 + x) % srcWidth][(sP1 + x)/srcWidth];
                    ker4k = src[xP1][sP1];// //src[(sP1 + xP1) % srcWidth][(sP1 + xP1)/srcWidth];
                    ker4l = src[xP2][sP1];// //src[(sP1 + xP2) % srcWidth][(sP1 + xP2)/srcWidth];

                    ker4n = src[x][sP2];//  //src[(sP2 + x) % srcWidth][(sP2+x)/srcWidth];
                    ker4o = src[xP1][sP2]; //src[(sP2 + xP1) % srcWidth][(sP2 + xP1) /srcWidth];

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

                    blendXy = SetBottomR(preProcBuffer[x], blendResult_f);
                    blendXy1 = SetTopR(blendXy1, blendResult_j);
                    preProcBuffer[x] = blendXy1;
                    blendXy1 = SetTopL(0, blendResult_k);

                    if (x + 1 < srcWidth)
                        preProcBuffer[x + 1] = SetBottomL(preProcBuffer[x + 1], blendResult_g);

                    _FillBlock(trg, trgi, trgWidth, src[x][s0], 6);//scale

                    if (blendXy == 0)
                        continue;

                    ker3_0 = src[xM1][sM1]; //src[(sM1 + xM1) % srcWidth][(sM1 + xM1) / srcWidth];
                    ker3_1 = src[x][sM1]; //src[(sM1 + x) % srcWidth][(sM1 + x) / srcWidth];
                    ker3_2 = src[xP1][sM1]; //src[(sM1 + xP1) % srcWidth][(sM1 + xP1) / srcWidth];

                    ker3_3 = src[xM1][s0]; //src[(s0 + xM1) % srcWidth][(s0 + xM1) / srcWidth];
                    ker3_4 = src[x][s0]; //src[(s0 + x) % srcWidth][(s0 + x) / srcWidth];
                    ker3_5 = src[xP1][s0]; //src[(s0 + xP1) % srcWidth][(s0 + xP1) / srcWidth];

                    ker3_6 = src[xM1][sP1];//src[(sP1 + xM1) % srcWidth][(sP1 + xM1) / srcWidth];
                    ker3_7 = src[x][sP1]; //src[(sP1 + x) % srcWidth][(sP1 + x) / srcWidth];
                    ker3_8 = src[xP1][sP1];// //src[(sP1 + xP1) % srcWidth][(sP1 + xP1) / srcWidth];

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
                //---
            }
        }
        */
        public static void ScaleImage5X(uint[][] src, uint[] trg, int srcWidth, int srcHeight)
        {
            int trgWidth = srcWidth * 5;

            byte[] preProcBuffer = new byte[srcWidth];

            OutputMatrix outputMatrix = new OutputMatrix(5, trg, trgWidth); //scale

            for (int y = 0; y < srcHeight; ++y)
            {
                int trgi = 5 * y * trgWidth; // scale
                int sM1 = Math.Max(y - 1, 0);
                int s0 = y;
                int sP1 = Math.Min(y + 1, srcHeight - 1);
                int sP2 = Math.Min(y + 2, srcHeight - 1);
                byte blendXy1 = 0;
                byte blendXy = 0;
                //-----
                int fg;
                int hc;
                bool doLineBlend;
                bool haveShallowLine;
                bool haveSteepLine;
                uint px;
                uint b, c, d, e, f, g, h, i;
                uint ker4b, ker4c, ker4e, ker4f, ker4g, ker4h, ker4i, ker4j, ker4k, ker4l, ker4n, ker4o;
                uint ker3_0, ker3_1, ker3_2, ker3_3, ker3_4, ker3_5, ker3_6, ker3_7, ker3_8;
                byte blend;

                for (int x = 0; x < srcWidth; ++x, trgi += 5) //scale
                {
                    int blendResult_f = 0, blendResult_g = 0, blendResult_j = 0, blendResult_k = 0;

                    int xM1 = Math.Max(x - 1, 0);
                    int xP1 = Math.Min(x + 1, srcWidth - 1);
                    int xP2 = Math.Min(x + 2, srcWidth - 1);

                    ker4b = src[x][sM1];    //src[(sM1 + x) % srcWidth][(sM1 + x) / srcWidth];
                    ker4c = src[xP1][sM1]; //src[(sM1 + xP1) % srcWidth][(sM1 + xP1) / srcWidth];

                    ker4e = src[xM1][s0]; //src[(s0 + xM1) % srcWidth][(s0 + xM1) / srcWidth];
                    ker4f = src[x][s0]; //src[(s0 + x) % srcWidth][(s0 + x) /srcWidth];
                    ker4g = src[xP1][s0];//  //src[(s0 + xP1) % srcWidth][(s0 + xP1) / srcWidth];
                    ker4h = src[xP2][s0]; //src[(s0 + xP2) % srcWidth][(s0 + xP2)/srcWidth];

                    ker4i = src[xM1][sP1]; //src[(sP1 + xM1) % srcWidth][(sP1 + xM1)/srcWidth];
                    ker4j = src[x][sP1]; //src[(sP1 + x) % srcWidth][(sP1 + x)/srcWidth];
                    ker4k = src[xP1][sP1];// //src[(sP1 + xP1) % srcWidth][(sP1 + xP1)/srcWidth];
                    ker4l = src[xP2][sP1];// //src[(sP1 + xP2) % srcWidth][(sP1 + xP2)/srcWidth];

                    ker4n = src[x][sP2];//  //src[(sP2 + x) % srcWidth][(sP2+x)/srcWidth];
                    ker4o = src[xP1][sP2]; //src[(sP2 + xP1) % srcWidth][(sP2 + xP1) /srcWidth];


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

                    blendXy = SetBottomR(preProcBuffer[x], blendResult_f);
                    blendXy1 = SetTopR(blendXy1, blendResult_j);
                    preProcBuffer[x] = blendXy1;
                    blendXy1 = SetTopL(0, blendResult_k);

                    if (x + 1 < srcWidth)
                        preProcBuffer[x + 1] = SetBottomL(preProcBuffer[x + 1], blendResult_g);

                    _FillBlock(trg, trgi, trgWidth, src[x][s0], 5);//scale

                    if (blendXy == 0)
                        continue;

                    ker3_0 = src[xM1][sM1]; //src[(sM1 + xM1) % srcWidth][(sM1 + xM1) / srcWidth];
                    ker3_1 = src[x][sM1]; //src[(sM1 + x) % srcWidth][(sM1 + x) / srcWidth];
                    ker3_2 = src[xP1][sM1]; //src[(sM1 + xP1) % srcWidth][(sM1 + xP1) / srcWidth];

                    ker3_3 = src[xM1][s0]; //src[(s0 + xM1) % srcWidth][(s0 + xM1) / srcWidth];
                    ker3_4 = src[x][s0]; //src[(s0 + x) % srcWidth][(s0 + x) / srcWidth];
                    ker3_5 = src[xP1][s0]; //src[(s0 + xP1) % srcWidth][(s0 + xP1) / srcWidth];

                    ker3_6 = src[xM1][sP1];//src[(sP1 + xM1) % srcWidth][(sP1 + xM1) / srcWidth];
                    ker3_7 = src[x][sP1]; //src[(sP1 + x) % srcWidth][(sP1 + x) / srcWidth];
                    ker3_8 = src[xP1][sP1];// //src[(sP1 + xP1) % srcWidth][(sP1 + xP1) / srcWidth];

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
                //---
            }
        }
        public static void ScaleImage4X(uint[][] src, uint[] trg, int srcWidth, int srcHeight)
        {
            int trgWidth = srcWidth << 2;

            byte[] preProcBuffer = new byte[srcWidth];

            OutputMatrix outputMatrix = new OutputMatrix(4, trg, trgWidth); //scale

            for (int y = 0; y < srcHeight; ++y)
            {
                int trgi = 4 * y * trgWidth; // scale
                int sM1 = Math.Max(y - 1, 0);
                int s0 = y;
                int sP1 = Math.Min(y + 1, srcHeight - 1);
                int sP2 = Math.Min(y + 2, srcHeight - 1);
                byte blendXy1 = 0;
                byte blendXy = 0;
                //-----
                int fg;
                int hc;
                bool doLineBlend;
                bool haveShallowLine;
                bool haveSteepLine;
                uint px;
                uint b, c, d, e, f, g, h, i;
                uint ker4b, ker4c, ker4e, ker4f, ker4g, ker4h, ker4i, ker4j, ker4k, ker4l, ker4n, ker4o;
                uint ker3_0, ker3_1, ker3_2, ker3_3, ker3_4, ker3_5, ker3_6, ker3_7, ker3_8;
                byte blend;

                for (int x = 0; x < srcWidth; ++x, trgi += 4) //scale
                {
                    int blendResult_f = 0, blendResult_g = 0, blendResult_j = 0, blendResult_k = 0;

                    int xM1 = Math.Max(x - 1, 0);
                    int xP1 = Math.Min(x + 1, srcWidth - 1);
                    int xP2 = Math.Min(x + 2, srcWidth - 1);

                    ker4b = src[x][sM1];    //src[(sM1 + x) % srcWidth][(sM1 + x) / srcWidth];
                    ker4c = src[xP1][sM1]; //src[(sM1 + xP1) % srcWidth][(sM1 + xP1) / srcWidth];

                    ker4e = src[xM1][s0]; //src[(s0 + xM1) % srcWidth][(s0 + xM1) / srcWidth];
                    ker4f = src[x][s0]; //src[(s0 + x) % srcWidth][(s0 + x) /srcWidth];
                    ker4g = src[xP1][s0];//  //src[(s0 + xP1) % srcWidth][(s0 + xP1) / srcWidth];
                    ker4h = src[xP2][s0]; //src[(s0 + xP2) % srcWidth][(s0 + xP2)/srcWidth];

                    ker4i = src[xM1][sP1]; //src[(sP1 + xM1) % srcWidth][(sP1 + xM1)/srcWidth];
                    ker4j = src[x][sP1]; //src[(sP1 + x) % srcWidth][(sP1 + x)/srcWidth];
                    ker4k = src[xP1][sP1];// //src[(sP1 + xP1) % srcWidth][(sP1 + xP1)/srcWidth];
                    ker4l = src[xP2][sP1];// //src[(sP1 + xP2) % srcWidth][(sP1 + xP2)/srcWidth];

                    ker4n = src[x][sP2];//  //src[(sP2 + x) % srcWidth][(sP2+x)/srcWidth];
                    ker4o = src[xP1][sP2]; //src[(sP2 + xP1) % srcWidth][(sP2 + xP1) /srcWidth];

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

                    blendXy = SetBottomR(preProcBuffer[x], blendResult_f);
                    blendXy1 = SetTopR(blendXy1, blendResult_j);
                    preProcBuffer[x] = blendXy1;
                    blendXy1 = SetTopL(0, blendResult_k);

                    if (x + 1 < srcWidth)
                        preProcBuffer[x + 1] = SetBottomL(preProcBuffer[x + 1], blendResult_g);

                    _FillBlock(trg, trgi, trgWidth, src[x][s0], 4);//scale



                    if (blendXy == 0)
                        continue;

                    ker3_0 = src[xM1][sM1]; //src[(sM1 + xM1) % srcWidth][(sM1 + xM1) / srcWidth];
                    ker3_1 = src[x][sM1]; //src[(sM1 + x) % srcWidth][(sM1 + x) / srcWidth];
                    ker3_2 = src[xP1][sM1]; //src[(sM1 + xP1) % srcWidth][(sM1 + xP1) / srcWidth];

                    ker3_3 = src[xM1][s0]; //src[(s0 + xM1) % srcWidth][(s0 + xM1) / srcWidth];
                    ker3_4 = src[x][s0]; //src[(s0 + x) % srcWidth][(s0 + x) / srcWidth];
                    ker3_5 = src[xP1][s0]; //src[(s0 + xP1) % srcWidth][(s0 + xP1) / srcWidth];

                    ker3_6 = src[xM1][sP1];//src[(sP1 + xM1) % srcWidth][(sP1 + xM1) / srcWidth];
                    ker3_7 = src[x][sP1]; //src[(sP1 + x) % srcWidth][(sP1 + x) / srcWidth];
                    ker3_8 = src[xP1][sP1];// //src[(sP1 + xP1) % srcWidth][(sP1 + xP1) / srcWidth];

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
                //---
            }
        }
        public static void ScaleImage3X(uint[][] src, uint[] trg, int srcWidth, int srcHeight)
        {
            int trgWidth = srcWidth * 3;

            byte[] preProcBuffer = new byte[srcWidth];

            OutputMatrix outputMatrix = new OutputMatrix(3, trg, trgWidth);

            for (int y = 0; y < srcHeight; ++y)
            {
                int trgi = 3 * y * trgWidth; // scale
                int sM1 = Math.Max(y - 1, 0);
                int s0 = y;
                int sP1 = Math.Min(y + 1, srcHeight - 1);
                int sP2 = Math.Min(y + 2, srcHeight - 1);
                byte blendXy1 = 0;
                byte blendXy = 0;
                //-----
                int fg;
                int hc;
                bool doLineBlend;
                bool haveShallowLine;
                bool haveSteepLine;
                uint px;
                uint b, c, d, e, f, g, h, i;
                uint ker4b, ker4c, ker4e, ker4f, ker4g, ker4h, ker4i, ker4j, ker4k, ker4l, ker4n, ker4o;
                uint ker3_0, ker3_1, ker3_2, ker3_3, ker3_4, ker3_5, ker3_6, ker3_7, ker3_8;
                byte blend;

                for (int x = 0; x < srcWidth; ++x, trgi += 3)
                {
                    int blendResult_f = 0, blendResult_g = 0, blendResult_j = 0, blendResult_k = 0;

                    int xM1 = Math.Max(x - 1, 0);
                    int xP1 = Math.Min(x + 1, srcWidth - 1);
                    int xP2 = Math.Min(x + 2, srcWidth - 1);

                    ker4b = src[x][sM1];    //src[(sM1 + x) % srcWidth][(sM1 + x) / srcWidth];
                    ker4c = src[xP1][sM1]; //src[(sM1 + xP1) % srcWidth][(sM1 + xP1) / srcWidth];

                    ker4e = src[xM1][s0]; //src[(s0 + xM1) % srcWidth][(s0 + xM1) / srcWidth];
                    ker4f = src[x][s0]; //src[(s0 + x) % srcWidth][(s0 + x) /srcWidth];
                    ker4g = src[xP1][s0];//  //src[(s0 + xP1) % srcWidth][(s0 + xP1) / srcWidth];
                    ker4h = src[xP2][s0]; //src[(s0 + xP2) % srcWidth][(s0 + xP2)/srcWidth];

                    ker4i = src[xM1][sP1]; //src[(sP1 + xM1) % srcWidth][(sP1 + xM1)/srcWidth];
                    ker4j = src[x][sP1]; //src[(sP1 + x) % srcWidth][(sP1 + x)/srcWidth];
                    ker4k = src[xP1][sP1];// //src[(sP1 + xP1) % srcWidth][(sP1 + xP1)/srcWidth];
                    ker4l = src[xP2][sP1];// //src[(sP1 + xP2) % srcWidth][(sP1 + xP2)/srcWidth];

                    ker4n = src[x][sP2];//  //src[(sP2 + x) % srcWidth][(sP2+x)/srcWidth];
                    ker4o = src[xP1][sP2]; //src[(sP2 + xP1) % srcWidth][(sP2 + xP1) /srcWidth];

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

                    blendXy = SetBottomR(preProcBuffer[x], blendResult_f);
                    blendXy1 = SetTopR(blendXy1, blendResult_j);
                    preProcBuffer[x] = blendXy1;
                    blendXy1 = SetTopL(0, blendResult_k);

                    if (x + 1 < srcWidth)
                        preProcBuffer[x + 1] = SetBottomL(preProcBuffer[x + 1], blendResult_g);

                    _FillBlock(trg, trgi, trgWidth, src[x][s0], 3);

                    if (blendXy == 0)
                        continue;

                    ker3_0 = src[xM1][sM1]; //src[(sM1 + xM1) % srcWidth][(sM1 + xM1) / srcWidth];
                    ker3_1 = src[x][sM1]; //src[(sM1 + x) % srcWidth][(sM1 + x) / srcWidth];
                    ker3_2 = src[xP1][sM1]; //src[(sM1 + xP1) % srcWidth][(sM1 + xP1) / srcWidth];

                    ker3_3 = src[xM1][s0]; //src[(s0 + xM1) % srcWidth][(s0 + xM1) / srcWidth];
                    ker3_4 = src[x][s0]; //src[(s0 + x) % srcWidth][(s0 + x) / srcWidth];
                    ker3_5 = src[xP1][s0]; //src[(s0 + xP1) % srcWidth][(s0 + xP1) / srcWidth];

                    ker3_6 = src[xM1][sP1];//src[(sP1 + xM1) % srcWidth][(sP1 + xM1) / srcWidth];
                    ker3_7 = src[x][sP1]; //src[(sP1 + x) % srcWidth][(sP1 + x) / srcWidth];
                    ker3_8 = src[xP1][sP1];// //src[(sP1 + xP1) % srcWidth][(sP1 + xP1) / srcWidth];

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
                //---
            }
        }
        public static void ScaleImage2X(uint[][] src, uint[] trg, int srcWidth, int srcHeight)
        {
            int trgWidth = srcWidth << 1;

            byte[] preProcBuffer = new byte[srcWidth];

            OutputMatrix outputMatrix = new OutputMatrix(2, trg, trgWidth);

            for (int y = 0; y < srcHeight; ++y)
            {
                int trgi = 2 * y * trgWidth;
                int sM1 = Math.Max(y - 1, 0);
                int s0 = y;
                int sP1 = Math.Min(y + 1, srcHeight - 1);
                int sP2 = Math.Min(y + 2, srcHeight - 1);
                byte blendXy1 = 0;
                byte blendXy = 0;
                //-----
                int fg;
                int hc;
                bool doLineBlend;
                bool haveShallowLine;
                bool haveSteepLine;
                uint px;
                uint b, c, d, e, f, g, h, i;
                uint ker4b, ker4c, ker4e, ker4f, ker4g, ker4h, ker4i, ker4j, ker4k, ker4l, ker4n, ker4o;
                uint ker3_0, ker3_1, ker3_2, ker3_3, ker3_4, ker3_5, ker3_6, ker3_7, ker3_8;
                byte blend;

                for (int x = 0; x < srcWidth; ++x, trgi += 2)
                {
                    int blendResult_f = 0, blendResult_g = 0, blendResult_j = 0, blendResult_k = 0;

                    int xM1 = Math.Max(x - 1, 0);
                    int xP1 = Math.Min(x + 1, srcWidth - 1);
                    int xP2 = Math.Min(x + 2, srcWidth - 1);

                    ker4b = src[x][sM1];    //src[(sM1 + x) % srcWidth][(sM1 + x) / srcWidth];
                    ker4c = src[xP1][sM1]; //src[(sM1 + xP1) % srcWidth][(sM1 + xP1) / srcWidth];

                    ker4e = src[xM1][s0]; //src[(s0 + xM1) % srcWidth][(s0 + xM1) / srcWidth];
                    ker4f = src[x][s0]; //src[(s0 + x) % srcWidth][(s0 + x) /srcWidth];
                    ker4g = src[xP1][s0];//  //src[(s0 + xP1) % srcWidth][(s0 + xP1) / srcWidth];
                    ker4h = src[xP2][s0]; //src[(s0 + xP2) % srcWidth][(s0 + xP2)/srcWidth];

                    ker4i = src[xM1][sP1]; //src[(sP1 + xM1) % srcWidth][(sP1 + xM1)/srcWidth];
                    ker4j = src[x][sP1]; //src[(sP1 + x) % srcWidth][(sP1 + x)/srcWidth];
                    ker4k = src[xP1][sP1];// //src[(sP1 + xP1) % srcWidth][(sP1 + xP1)/srcWidth];
                    ker4l = src[xP2][sP1];// //src[(sP1 + xP2) % srcWidth][(sP1 + xP2)/srcWidth];

                    ker4n = src[x][sP2];//  //src[(sP2 + x) % srcWidth][(sP2+x)/srcWidth];
                    ker4o = src[xP1][sP2]; //src[(sP2 + xP1) % srcWidth][(sP2 + xP1) /srcWidth];

                    /*ker4b = src[sM1 + x];
                    ker4c = src[sM1 + xP1];

                    ker4e = src[s0 + xM1];
                    ker4f = src[s0 + x];
                    ker4g = src[s0 + xP1];
                    ker4h = src[s0 + xP2];

                    ker4i = src[sP1 + xM1];
                    ker4j = src[sP1 + x];
                    ker4k = src[sP1 + xP1];
                    ker4l = src[sP1 + xP2];

                    ker4n = src[sP2 + x];
                    ker4o = src[sP2 + xP1];*/

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

                    blendXy = SetBottomR(preProcBuffer[x], blendResult_f);
                    blendXy1 = SetTopR(blendXy1, blendResult_j);
                    preProcBuffer[x] = blendXy1;
                    blendXy1 = SetTopL(0, blendResult_k);

                    if (x + 1 < srcWidth)
                        preProcBuffer[x + 1] = SetBottomL(preProcBuffer[x + 1], blendResult_g);


                    _FillBlock(trg, trgi, trgWidth, src[x][s0], 2);

                    //--

                    // static void _FillBlock(uint[] trg, int trgi, int pitch, uint col, int blockSize)
                    //{
                   // for (int _y = 0; _y <= 1; ++y, trgi += trgWidth)
                   //     for (int _x = 0; _x <= 1; ++_x)
                    //        trg[trgi + _x] = src[x][s0];
                    //}

                    //--

                    if (blendXy == 0)
                        continue;

                    ker3_0 = src[xM1][sM1]; //src[(sM1 + xM1) % srcWidth][(sM1 + xM1) / srcWidth];
                    ker3_1 = src[x][sM1]; //src[(sM1 + x) % srcWidth][(sM1 + x) / srcWidth];
                    ker3_2 = src[xP1][sM1]; //src[(sM1 + xP1) % srcWidth][(sM1 + xP1) / srcWidth];

                    ker3_3 = src[xM1][s0]; //src[(s0 + xM1) % srcWidth][(s0 + xM1) / srcWidth];
                    ker3_4 = src[x][s0]; //src[(s0 + x) % srcWidth][(s0 + x) / srcWidth];
                    ker3_5 = src[xP1][s0]; //src[(s0 + xP1) % srcWidth][(s0 + xP1) / srcWidth];

                    ker3_6 = src[xM1][sP1];//src[(sP1 + xM1) % srcWidth][(sP1 + xM1) / srcWidth];
                    ker3_7 = src[x][sP1]; //src[(sP1 + x) % srcWidth][(sP1 + x) / srcWidth];
                    ker3_8 = src[xP1][sP1];// //src[(sP1 + xP1) % srcWidth][(sP1 + xP1) / srcWidth];

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
                //---
            }
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
