using System;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace XBRz_speed
{
    // ============================================================
    // xBRZ 濾鏡 - .NET 4.8.1 (C# 9.0) 極致多倍率支援版 (2X ~ 6X)
    // 架構：統一特徵偵測 -> O(1) 狀態分派函式 -> 專屬硬編碼混合管線
    // ============================================================
    unsafe public class HS_XBRz
    {
        const int dominantDirectionThreshold = 4;
        const int steepDirectionThreshold = 2;
        const int eqColorThres = 900;

        const int BlendNone = 0;
        const int BlendNormal = 1;
        const int BlendDominant = 2;

        static int* lTable_dist;

        private const int _MAX_ROTS = 4;
        private const int _MAX_SCALE = 6;
        private const int _MAX_SCALE_SQUARED = _MAX_SCALE * _MAX_SCALE;

        private static readonly int[] _MATRIX_ROTATION;

        // 各倍率專屬的主函式指標陣列 (O(1) Jump Tables)
        private static readonly delegate*<uint, uint*, int, int, int, void>[] _blendFuncs2X;
        private static readonly delegate*<uint, uint*, int, int, int, void>[] _blendFuncs3X;
        private static readonly delegate*<uint, uint*, int, int, int, void>[] _blendFuncs4X;
        private static readonly delegate*<uint, uint*, int, int, int, void>[] _blendFuncs5X;
        private static readonly delegate*<uint, uint*, int, int, int, void>[] _blendFuncs6X;

        static HS_XBRz()
        {
            // 初始化旋轉矩陣 (所有倍率共用 6x6 的最大空間)
            _MATRIX_ROTATION = new int[(_MAX_SCALE - 1) * _MAX_SCALE_SQUARED * _MAX_ROTS];
            for (var n = 2; n < _MAX_SCALE + 1; n++)
                for (var r = 0; r < _MAX_ROTS; r++)
                {
                    var nr = (n - 2) * (_MAX_ROTS * _MAX_SCALE_SQUARED) + r * _MAX_SCALE_SQUARED;
                    for (var i = 0; i < _MAX_SCALE; i++)
                        for (var j = 0; j < _MAX_SCALE; j++)
                            _MATRIX_ROTATION[nr + i * _MAX_SCALE + j] = _BuildMatrixRotationPacked(r, i, j, n);
                }

            // 初始化 2X 函式表
            _blendFuncs2X = new delegate*<uint, uint*, int, int, int, void>[8];
            _blendFuncs2X[0] = _blendFuncs2X[2] = _blendFuncs2X[4] = _blendFuncs2X[6] = &BlendCorner2X;
            _blendFuncs2X[1] = &BlendLineDiagonal2X; _blendFuncs2X[3] = &BlendLineShallow2X;
            _blendFuncs2X[5] = &BlendLineSteep2X; _blendFuncs2X[7] = &BlendLineSteepAndShallow2X;

            // 初始化 3X 函式表
            _blendFuncs3X = new delegate*<uint, uint*, int, int, int, void>[8];
            _blendFuncs3X[0] = _blendFuncs3X[2] = _blendFuncs3X[4] = _blendFuncs3X[6] = &BlendCorner3X;
            _blendFuncs3X[1] = &BlendLineDiagonal3X; _blendFuncs3X[3] = &BlendLineShallow3X;
            _blendFuncs3X[5] = &BlendLineSteep3X; _blendFuncs3X[7] = &BlendLineSteepAndShallow3X;

            // 初始化 4X 函式表
            _blendFuncs4X = new delegate*<uint, uint*, int, int, int, void>[8];
            _blendFuncs4X[0] = _blendFuncs4X[2] = _blendFuncs4X[4] = _blendFuncs4X[6] = &BlendCorner4X;
            _blendFuncs4X[1] = &BlendLineDiagonal4X; _blendFuncs4X[3] = &BlendLineShallow4X;
            _blendFuncs4X[5] = &BlendLineSteep4X; _blendFuncs4X[7] = &BlendLineSteepAndShallow4X;

            // 初始化 5X 函式表
            _blendFuncs5X = new delegate*<uint, uint*, int, int, int, void>[8];
            _blendFuncs5X[0] = _blendFuncs5X[2] = _blendFuncs5X[4] = _blendFuncs5X[6] = &BlendCorner5X;
            _blendFuncs5X[1] = &BlendLineDiagonal5X; _blendFuncs5X[3] = &BlendLineShallow5X;
            _blendFuncs5X[5] = &BlendLineSteep5X; _blendFuncs5X[7] = &BlendLineSteepAndShallow5X;

            // 初始化 6X 函式表
            _blendFuncs6X = new delegate*<uint, uint*, int, int, int, void>[8];
            _blendFuncs6X[0] = _blendFuncs6X[2] = _blendFuncs6X[4] = _blendFuncs6X[6] = &BlendCorner6X;
            _blendFuncs6X[1] = &BlendLineDiagonal6X; _blendFuncs6X[3] = &BlendLineShallow6X;
            _blendFuncs6X[5] = &BlendLineSteep6X; _blendFuncs6X[7] = &BlendLineSteepAndShallow6X;
        }

        static int _BuildMatrixRotationPacked(int rotDeg, int i, int j, int n)
        {
            if (rotDeg == 0) return (i << 8) | j;
            var old = _BuildMatrixRotationPacked(rotDeg - 1, i, j, n);
            return ((n - 1 - (old & 0xFF)) << 8) | (old >> 8);
        }

        static byte* _preProcBuffer;
        static byte* results_j, results_k, results_g, results_f, preProcBuffer_local;
        static int width, height;

        public static unsafe void initTable(int _width, int _height)
        {
            if (lTable_dist != null) return;
            width = _width; height = _height;
            lTable_dist = (int*)Marshal.AllocHGlobal(sizeof(int) * 0x1000000);
            _preProcBuffer = (byte*)Marshal.AllocHGlobal(sizeof(byte) * width * height);
            results_f = (byte*)Marshal.AllocHGlobal(sizeof(byte) * width * height);
            results_j = (byte*)Marshal.AllocHGlobal(sizeof(byte) * width * height);
            results_k = (byte*)Marshal.AllocHGlobal(sizeof(byte) * width * height);
            results_g = (byte*)Marshal.AllocHGlobal(sizeof(byte) * width * height);
            preProcBuffer_local = (byte*)Marshal.AllocHGlobal(sizeof(byte) * width);

            for (int i = 0; i < 0x1000000; i++)
            {
                int r_diff = ((i & 0xff0000) >> 16) * 2 - 255;
                int g_diff = ((i & 0x00ff00) >> 8) * 2 - 255;
                int b_diff = ((i & 0x0000ff) >> 0) * 2 - 255;
                double kb = 0.0593, kr = 0.2627, kg = 1 - kb - kr;
                double y = kr * r_diff + kg * g_diff + kb * b_diff;
                double cb = (0.5 / (1 - kb)) * (b_diff - y);
                double cr = (0.5 / (1 - kr)) * (r_diff - y);
                lTable_dist[i] = (int)(y * y + cb * cb + cr * cr);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] static int GetTopR(byte b) => ((b >> 2) & 0x3);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static int GetBottomR(byte b) => ((b >> 4) & 0x3);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static int GetBottomL(byte b) => ((b >> 6) & 0x3);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] static byte Rotate(byte b, int rot) => (byte)(b << (rot << 1) | b >> (8 - (rot << 1)));

        // —— 旋轉座標繪圖工具 ——
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void _SetPixel(int i, int j, uint col, uint* trg, int outi, int outW, int nr)
        {
            int rot = _MATRIX_ROTATION[nr + i * _MAX_SCALE + j];
            trg[outi + (rot & 0xFF) + (rot >> 8) * outW] = col;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void _AlphaBlend(int i, int j, uint col, uint n, uint m, uint* trg, int outi, int outW, int nr)
        {
            int rot = _MATRIX_ROTATION[nr + i * _MAX_SCALE + j];
            int targetIdx = outi + (rot & 0xFF) + (rot >> 8) * outW;
            uint dst = trg[targetIdx]; uint invN = m - n;
            uint res_RB = (((col & 0x00FF00FFu) * n + (dst & 0x00FF00FFu) * invN) / m) & 0x00FF00FFu;
            uint res_G = (((col & 0x0000FF00u) * n + (dst & 0x0000FF00u) * invN) / m) & 0x0000FF00u;
            trg[targetIdx] = 0xFF000000u | res_RB | res_G;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int DistYCbCr(uint p1, uint p2) => lTable_dist[((((((p1 & 0xff0000) >> 16) - ((p2 & 0xff0000) >> 16)) + 255) >> 1) << 16) | ((((((p1 & 0xff00) >> 8) - ((p2 & 0xff00) >> 8)) + 255) >> 1) << 8) | ((((p1 & 0xff) - (p2 & 0xff)) + 255) >> 1)];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool ColorEQ(uint p1, uint p2) => (p1 == p2) || DistYCbCr(p1, p2) < eqColorThres;

        // ============================================================
        // 核心門面函式 (Facade)
        // ============================================================
        public static void ScaleImage(uint* src, uint* trg, int scale)
        {
            if (scale < 2 || scale > 6) throw new ArgumentOutOfRangeException(nameof(scale));

            // 統一第一步：偵測邊緣特徵矩陣
            ComputeEdgeFeatures(src);

            // 流程第二步：依倍率分別渲染 (傳入各倍率旋轉偏移量 nBase)
            // nBase 公式：(scale - 2) * (4 rotations * 36 pixels)
            switch (scale)
            {
                case 2: RenderPipeline(src, trg, 2, 0); break;
                case 3: RenderPipeline(src, trg, 3, 144); break;
                case 4: RenderPipeline(src, trg, 4, 288); break;
                case 5: RenderPipeline(src, trg, 5, 432); break;
                case 6: RenderPipeline(src, trg, 6, 576); break;
            }
        }

        // —— 流程 1：統一特徵偵測 (與 Scale 倍率無關) ——
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ComputeEdgeFeatures(uint* src)
        {
            Parallel.For(0, height, y =>
            {
                int sM1 = Math.Max(y - 1, 0); int s0 = y;
                int sP1 = Math.Min(y + 1, height - 1); int sP2 = Math.Min(y + 2, height - 1);

                for (int x = 0; x < width; ++x)
                {
                    int xM1 = Math.Max(x - 1, 0); int xP1 = Math.Min(x + 1, width - 1); int xP2 = Math.Min(x + 2, width - 1);
                    int array_loc = x + y * width;

                    uint ker4b = src[sM1 * width + x]; uint ker4c = src[sM1 * width + xP1];
                    uint ker4e = src[s0 * width + xM1]; uint ker4f = src[s0 * width + x];
                    uint ker4g = src[s0 * width + xP1]; uint ker4h = src[s0 * width + xP2];
                    uint ker4i = src[sP1 * width + xM1]; uint ker4j = src[sP1 * width + x];
                    uint ker4k = src[sP1 * width + xP1]; uint ker4l = src[sP1 * width + xP2];
                    uint ker4n = src[sP2 * width + x]; uint ker4o = src[sP2 * width + xP1];

                    bool diff = (ker4f != ker4g | ker4j != ker4k) & (ker4f != ker4j | ker4g != ker4k);
                    int jg = DistYCbCr(ker4i, ker4f) + DistYCbCr(ker4f, ker4c) + DistYCbCr(ker4n, ker4k) + DistYCbCr(ker4k, ker4h) + (DistYCbCr(ker4j, ker4g) << 2);
                    int fk = DistYCbCr(ker4e, ker4j) + DistYCbCr(ker4j, ker4o) + DistYCbCr(ker4b, ker4g) + DistYCbCr(ker4g, ker4l) + (DistYCbCr(ker4f, ker4k) << 2);

                    bool jg_lt_fk = jg < fk; bool fk_lt_jg = fk < jg;
                    bool isDom_jg = (dominantDirectionThreshold * jg < fk);
                    bool isDom_fk = (dominantDirectionThreshold * fk < jg);

                    byte val_jg = (byte)(BlendNormal + *(byte*)&isDom_jg);
                    byte val_fk = (byte)(BlendNormal + *(byte*)&isDom_fk);

                    bool cond_f = diff & jg_lt_fk & (ker4f != ker4g) & (ker4f != ker4j);
                    bool cond_k = diff & jg_lt_fk & (ker4k != ker4j) & (ker4k != ker4g);
                    bool cond_j = diff & fk_lt_jg & (ker4j != ker4f) & (ker4j != ker4k);
                    bool cond_g = diff & fk_lt_jg & (ker4g != ker4f) & (ker4g != ker4k);

                    results_f[array_loc] = (byte)(-(int)(*(byte*)&cond_f) & val_jg);
                    results_k[array_loc] = (byte)(-(int)(*(byte*)&cond_k) & val_jg);
                    results_j[array_loc] = (byte)(-(int)(*(byte*)&cond_j) & val_fk);
                    results_g[array_loc] = (byte)(-(int)(*(byte*)&cond_g) & val_fk);
                }
            });

            for (int y = 0; y < height; ++y)
            {
                byte blendXy1 = 0; int array_loc = 0;
                for (int x = 0; x < width - 1; ++x, array_loc = x + y * width)
                {
                    _preProcBuffer[array_loc] = (byte)(preProcBuffer_local[x] | (results_f[array_loc] << 4));
                    preProcBuffer_local[x] = blendXy1 = (byte)(blendXy1 | (results_j[array_loc] << 2));
                    blendXy1 = results_k[array_loc];
                    preProcBuffer_local[(x + 1)] = (byte)(preProcBuffer_local[(x + 1)] | (results_g[array_loc] << 6));
                }
            }
        }

        // —— 流程 2：渲染管線 (依據 Scale 倍率) ——
        private static void RenderPipeline(uint* src, uint* trg, int scale, int nBase)
        {
            int trgWidth = width * scale;

            Parallel.For(0, height, y =>
            {
                int trgi = scale * y * trgWidth;
                int sM1 = Math.Max(y - 1, 0); int s0 = y; int sP1 = Math.Min(y + 1, height - 1);

                for (int x = 0; x < width; ++x, trgi += scale)
                {
                    int xM1 = Math.Max(x - 1, 0); int xP1 = Math.Min(x + 1, width - 1);
                    byte blendXy = _preProcBuffer[x + y * width];

                    // 依照倍率進行 64-bit 區塊填充
                    switch (scale)
                    {
                        case 2: _FillBlock2x(trg, trgi, trgWidth, src[s0 * width + x]); break;
                        case 3: _FillBlock3x(trg, trgi, trgWidth, src[s0 * width + x]); break;
                        case 4: _FillBlock4x(trg, trgi, trgWidth, src[s0 * width + x]); break;
                        case 5: _FillBlock5x(trg, trgi, trgWidth, src[s0 * width + x]); break;
                        case 6: _FillBlock6x(trg, trgi, trgWidth, src[s0 * width + x]); break;
                    }

                    if (blendXy != 0)
                    {
                        uint ker3_0 = src[sM1 * width + xM1]; uint ker3_1 = src[sM1 * width + x]; uint ker3_2 = src[sM1 * width + xP1];
                        uint ker3_3 = src[s0 * width + xM1]; uint ker3_4 = src[s0 * width + x]; uint ker3_5 = src[s0 * width + xP1];
                        uint ker3_6 = src[sP1 * width + xM1]; uint ker3_7 = src[sP1 * width + x]; uint ker3_8 = src[sP1 * width + xP1];

                        // 呼叫專屬的 O(1) 門面函式
                        switch (scale)
                        {
                            case 2:
                                ProcessRotation2X(Rotate(blendXy, 0), ker3_1, ker3_2, ker3_3, ker3_4, ker3_5, ker3_6, ker3_7, ker3_8, trg, trgi, trgWidth, nBase);
                                ProcessRotation2X(Rotate(blendXy, 1), ker3_3, ker3_0, ker3_7, ker3_4, ker3_1, ker3_8, ker3_5, ker3_2, trg, trgi, trgWidth, nBase + 36);
                                ProcessRotation2X(Rotate(blendXy, 2), ker3_7, ker3_6, ker3_5, ker3_4, ker3_3, ker3_2, ker3_1, ker3_0, trg, trgi, trgWidth, nBase + 72);
                                ProcessRotation2X(Rotate(blendXy, 3), ker3_5, ker3_8, ker3_1, ker3_4, ker3_7, ker3_0, ker3_3, ker3_6, trg, trgi, trgWidth, nBase + 108);
                                break;
                            case 3:
                                ProcessRotation3X(Rotate(blendXy, 0), ker3_1, ker3_2, ker3_3, ker3_4, ker3_5, ker3_6, ker3_7, ker3_8, trg, trgi, trgWidth, nBase);
                                ProcessRotation3X(Rotate(blendXy, 1), ker3_3, ker3_0, ker3_7, ker3_4, ker3_1, ker3_8, ker3_5, ker3_2, trg, trgi, trgWidth, nBase + 36);
                                ProcessRotation3X(Rotate(blendXy, 2), ker3_7, ker3_6, ker3_5, ker3_4, ker3_3, ker3_2, ker3_1, ker3_0, trg, trgi, trgWidth, nBase + 72);
                                ProcessRotation3X(Rotate(blendXy, 3), ker3_5, ker3_8, ker3_1, ker3_4, ker3_7, ker3_0, ker3_3, ker3_6, trg, trgi, trgWidth, nBase + 108);
                                break;
                            case 4:
                                ProcessRotation4X(Rotate(blendXy, 0), ker3_1, ker3_2, ker3_3, ker3_4, ker3_5, ker3_6, ker3_7, ker3_8, trg, trgi, trgWidth, nBase);
                                ProcessRotation4X(Rotate(blendXy, 1), ker3_3, ker3_0, ker3_7, ker3_4, ker3_1, ker3_8, ker3_5, ker3_2, trg, trgi, trgWidth, nBase + 36);
                                ProcessRotation4X(Rotate(blendXy, 2), ker3_7, ker3_6, ker3_5, ker3_4, ker3_3, ker3_2, ker3_1, ker3_0, trg, trgi, trgWidth, nBase + 72);
                                ProcessRotation4X(Rotate(blendXy, 3), ker3_5, ker3_8, ker3_1, ker3_4, ker3_7, ker3_0, ker3_3, ker3_6, trg, trgi, trgWidth, nBase + 108);
                                break;
                            case 5:
                                ProcessRotation5X(Rotate(blendXy, 0), ker3_1, ker3_2, ker3_3, ker3_4, ker3_5, ker3_6, ker3_7, ker3_8, trg, trgi, trgWidth, nBase);
                                ProcessRotation5X(Rotate(blendXy, 1), ker3_3, ker3_0, ker3_7, ker3_4, ker3_1, ker3_8, ker3_5, ker3_2, trg, trgi, trgWidth, nBase + 36);
                                ProcessRotation5X(Rotate(blendXy, 2), ker3_7, ker3_6, ker3_5, ker3_4, ker3_3, ker3_2, ker3_1, ker3_0, trg, trgi, trgWidth, nBase + 72);
                                ProcessRotation5X(Rotate(blendXy, 3), ker3_5, ker3_8, ker3_1, ker3_4, ker3_7, ker3_0, ker3_3, ker3_6, trg, trgi, trgWidth, nBase + 108);
                                break;
                            case 6:
                                ProcessRotation6X(Rotate(blendXy, 0), ker3_1, ker3_2, ker3_3, ker3_4, ker3_5, ker3_6, ker3_7, ker3_8, trg, trgi, trgWidth, nBase);
                                ProcessRotation6X(Rotate(blendXy, 1), ker3_3, ker3_0, ker3_7, ker3_4, ker3_1, ker3_8, ker3_5, ker3_2, trg, trgi, trgWidth, nBase + 36);
                                ProcessRotation6X(Rotate(blendXy, 2), ker3_7, ker3_6, ker3_5, ker3_4, ker3_3, ker3_2, ker3_1, ker3_0, trg, trgi, trgWidth, nBase + 72);
                                ProcessRotation6X(Rotate(blendXy, 3), ker3_5, ker3_8, ker3_1, ker3_4, ker3_7, ker3_0, ker3_3, ker3_6, trg, trgi, trgWidth, nBase + 108);
                                break;
                        }
                    }
                }
            });
        }

        // ============================================================
        // 各倍率專屬的 Jump Table 分派函式 (ProcessRotationNX)
        // ============================================================

        // ============================================================
        // 各倍率專屬的 Jump Table 分派函式 (ProcessRotationNX)
        // ============================================================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ProcessRotation2X(byte blend, uint b, uint c, uint d, uint e, uint f, uint g, uint h, uint i, uint* trg, int trgi, int trgW, int nr)
        {
            if (GetBottomR(blend) == BlendNone) return;
            bool doLineBlend = (GetBottomR(blend) >= BlendDominant) | (!((GetTopR(blend) != BlendNone) & !ColorEQ(e, g)) & !((GetBottomL(blend) != BlendNone) & !ColorEQ(e, c)) & !(ColorEQ(g, h) & ColorEQ(h, i) & ColorEQ(i, f) & ColorEQ(f, c) & !ColorEQ(e, i)));
            uint px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;
            int fg = DistYCbCr(f, g), hc = DistYCbCr(h, c);

            // 修正步驟：先存入變數，再轉換位移 (解決 CS0030 錯誤)
            bool haveShallow = (steepDirectionThreshold * fg <= hc) & (e != g) & (d != g);
            bool haveSteep = (steepDirectionThreshold * hc <= fg) & (e != c) & (b != c);
            int state = (*(byte*)&doLineBlend) | ((*(byte*)&haveShallow) << 1) | ((*(byte*)&haveSteep) << 2);

            _blendFuncs2X[state](px, trg, trgi, trgW, nr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ProcessRotation3X(byte blend, uint b, uint c, uint d, uint e, uint f, uint g, uint h, uint i, uint* trg, int trgi, int trgW, int nr)
        {
            if (GetBottomR(blend) == BlendNone) return;
            bool doLineBlend = (GetBottomR(blend) >= BlendDominant) | (!((GetTopR(blend) != BlendNone) & !ColorEQ(e, g)) & !((GetBottomL(blend) != BlendNone) & !ColorEQ(e, c)) & !(ColorEQ(g, h) & ColorEQ(h, i) & ColorEQ(i, f) & ColorEQ(f, c) & !ColorEQ(e, i)));
            uint px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;
            int fg = DistYCbCr(f, g), hc = DistYCbCr(h, c);

            bool haveShallow = (steepDirectionThreshold * fg <= hc) & (e != g) & (d != g);
            bool haveSteep = (steepDirectionThreshold * hc <= fg) & (e != c) & (b != c);
            int state = (*(byte*)&doLineBlend) | ((*(byte*)&haveShallow) << 1) | ((*(byte*)&haveSteep) << 2);

            _blendFuncs3X[state](px, trg, trgi, trgW, nr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ProcessRotation4X(byte blend, uint b, uint c, uint d, uint e, uint f, uint g, uint h, uint i, uint* trg, int trgi, int trgW, int nr)
        {
            if (GetBottomR(blend) == BlendNone) return;
            bool doLineBlend = (GetBottomR(blend) >= BlendDominant) | (!((GetTopR(blend) != BlendNone) & !ColorEQ(e, g)) & !((GetBottomL(blend) != BlendNone) & !ColorEQ(e, c)) & !(ColorEQ(g, h) & ColorEQ(h, i) & ColorEQ(i, f) & ColorEQ(f, c) & !ColorEQ(e, i)));
            uint px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;
            int fg = DistYCbCr(f, g), hc = DistYCbCr(h, c);

            bool haveShallow = (steepDirectionThreshold * fg <= hc) & (e != g) & (d != g);
            bool haveSteep = (steepDirectionThreshold * hc <= fg) & (e != c) & (b != c);
            int state = (*(byte*)&doLineBlend) | ((*(byte*)&haveShallow) << 1) | ((*(byte*)&haveSteep) << 2);

            _blendFuncs4X[state](px, trg, trgi, trgW, nr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ProcessRotation5X(byte blend, uint b, uint c, uint d, uint e, uint f, uint g, uint h, uint i, uint* trg, int trgi, int trgW, int nr)
        {
            if (GetBottomR(blend) == BlendNone) return;
            bool doLineBlend = (GetBottomR(blend) >= BlendDominant) | (!((GetTopR(blend) != BlendNone) & !ColorEQ(e, g)) & !((GetBottomL(blend) != BlendNone) & !ColorEQ(e, c)) & !(ColorEQ(g, h) & ColorEQ(h, i) & ColorEQ(i, f) & ColorEQ(f, c) & !ColorEQ(e, i)));
            uint px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;
            int fg = DistYCbCr(f, g), hc = DistYCbCr(h, c);

            bool haveShallow = (steepDirectionThreshold * fg <= hc) & (e != g) & (d != g);
            bool haveSteep = (steepDirectionThreshold * hc <= fg) & (e != c) & (b != c);
            int state = (*(byte*)&doLineBlend) | ((*(byte*)&haveShallow) << 1) | ((*(byte*)&haveSteep) << 2);

            _blendFuncs5X[state](px, trg, trgi, trgW, nr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ProcessRotation6X(byte blend, uint b, uint c, uint d, uint e, uint f, uint g, uint h, uint i, uint* trg, int trgi, int trgW, int nr)
        {
            if (GetBottomR(blend) == BlendNone) return;
            bool doLineBlend = (GetBottomR(blend) >= BlendDominant) | (!((GetTopR(blend) != BlendNone) & !ColorEQ(e, g)) & !((GetBottomL(blend) != BlendNone) & !ColorEQ(e, c)) & !(ColorEQ(g, h) & ColorEQ(h, i) & ColorEQ(i, f) & ColorEQ(f, c) & !ColorEQ(e, i)));
            uint px = DistYCbCr(e, f) <= DistYCbCr(e, h) ? f : h;
            int fg = DistYCbCr(f, g), hc = DistYCbCr(h, c);

            bool haveShallow = (steepDirectionThreshold * fg <= hc) & (e != g) & (d != g);
            bool haveSteep = (steepDirectionThreshold * hc <= fg) & (e != c) & (b != c);
            int state = (*(byte*)&doLineBlend) | ((*(byte*)&haveShallow) << 1) | ((*(byte*)&haveSteep) << 2);

            _blendFuncs6X[state](px, trg, trgi, trgW, nr);
        }

        // ============================================================
        // 各倍率的基礎方塊填充與極速 64-bit 寫入 (Block Fill)
        // ============================================================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void _FillBlock2x(uint* trg, int trgi, int pitch, uint col)
        {
            ulong c64 = col | ((ulong)col << 32);
            *(ulong*)(trg + trgi) = c64; *(ulong*)(trg + trgi + pitch) = c64;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void _FillBlock3x(uint* trg, int trgi, int pitch, uint col)
        {
            ulong c64 = col | ((ulong)col << 32);
            *(ulong*)(trg + trgi) = c64; trg[trgi + 2] = col; trgi += pitch;
            *(ulong*)(trg + trgi) = c64; trg[trgi + 2] = col; trgi += pitch;
            *(ulong*)(trg + trgi) = c64; trg[trgi + 2] = col;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void _FillBlock4x(uint* trg, int trgi, int pitch, uint col)
        {
            ulong c64 = col | ((ulong)col << 32);
            *(ulong*)(trg + trgi) = c64; *(ulong*)(trg + trgi + 2) = c64; trgi += pitch;
            *(ulong*)(trg + trgi) = c64; *(ulong*)(trg + trgi + 2) = c64; trgi += pitch;
            *(ulong*)(trg + trgi) = c64; *(ulong*)(trg + trgi + 2) = c64; trgi += pitch;
            *(ulong*)(trg + trgi) = c64; *(ulong*)(trg + trgi + 2) = c64;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void _FillBlock5x(uint* trg, int trgi, int pitch, uint col)
        {
            ulong c64 = col | ((ulong)col << 32);
            *(ulong*)(trg + trgi) = c64; *(ulong*)(trg + trgi + 2) = c64; trg[trgi + 4] = col; trgi += pitch;
            *(ulong*)(trg + trgi) = c64; *(ulong*)(trg + trgi + 2) = c64; trg[trgi + 4] = col; trgi += pitch;
            *(ulong*)(trg + trgi) = c64; *(ulong*)(trg + trgi + 2) = c64; trg[trgi + 4] = col; trgi += pitch;
            *(ulong*)(trg + trgi) = c64; *(ulong*)(trg + trgi + 2) = c64; trg[trgi + 4] = col; trgi += pitch;
            *(ulong*)(trg + trgi) = c64; *(ulong*)(trg + trgi + 2) = c64; trg[trgi + 4] = col;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void _FillBlock6x(uint* trg, int trgi, int pitch, uint col)
        {
            ulong c64 = col | ((ulong)col << 32);
            *(ulong*)(trg + trgi) = c64; *(ulong*)(trg + trgi + 2) = c64; *(ulong*)(trg + trgi + 4) = c64; trgi += pitch;
            *(ulong*)(trg + trgi) = c64; *(ulong*)(trg + trgi + 2) = c64; *(ulong*)(trg + trgi + 4) = c64; trgi += pitch;
            *(ulong*)(trg + trgi) = c64; *(ulong*)(trg + trgi + 2) = c64; *(ulong*)(trg + trgi + 4) = c64; trgi += pitch;
            *(ulong*)(trg + trgi) = c64; *(ulong*)(trg + trgi + 2) = c64; *(ulong*)(trg + trgi + 4) = c64; trgi += pitch;
            *(ulong*)(trg + trgi) = c64; *(ulong*)(trg + trgi + 2) = c64; *(ulong*)(trg + trgi + 4) = c64; trgi += pitch;
            *(ulong*)(trg + trgi) = c64; *(ulong*)(trg + trgi + 2) = c64; *(ulong*)(trg + trgi + 4) = c64;
        }

        // ============================================================
        // —— 2X 專屬混合函式 ——
        // ============================================================
        static void BlendLineShallow2X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(1, 0, col, 1, 4, trg, outi, outW, nr);
            _SetPixel(1, 1, col, trg, outi, outW, nr);
        }
        static void BlendLineSteep2X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(0, 1, col, 1, 4, trg, outi, outW, nr);
            _SetPixel(1, 1, col, trg, outi, outW, nr);
        }
        static void BlendLineSteepAndShallow2X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(1, 0, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(0, 1, col, 1, 4, trg, outi, outW, nr);
            _SetPixel(1, 1, col, trg, outi, outW, nr);
        }
        static void BlendLineDiagonal2X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(1, 1, col, 1, 2, trg, outi, outW, nr);
        }
        static void BlendCorner2X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(1, 1, col, 21, 100, trg, outi, outW, nr);
        }

        // ============================================================
        // —— 3X 專屬混合函式 ——
        // ============================================================
        static void BlendLineShallow3X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(2, 0, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(2, 1, col, 3, 4, trg, outi, outW, nr);
            _SetPixel(2, 2, col, trg, outi, outW, nr);
            _SetPixel(1, 2, col, trg, outi, outW, nr);
        }
        static void BlendLineSteep3X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(0, 2, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(1, 2, col, 3, 4, trg, outi, outW, nr);
            _SetPixel(2, 2, col, trg, outi, outW, nr);
            _SetPixel(2, 1, col, trg, outi, outW, nr);
        }
        static void BlendLineSteepAndShallow3X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(2, 0, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(2, 1, col, 3, 4, trg, outi, outW, nr);
            _AlphaBlend(0, 2, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(1, 2, col, 3, 4, trg, outi, outW, nr);
            _SetPixel(2, 2, col, trg, outi, outW, nr);
        }
        static void BlendLineDiagonal3X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(2, 1, col, 1, 2, trg, outi, outW, nr);
            _AlphaBlend(1, 2, col, 1, 2, trg, outi, outW, nr);
            _SetPixel(2, 2, col, trg, outi, outW, nr);
        }
        static void BlendCorner3X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(2, 2, col, 45, 100, trg, outi, outW, nr);
            _AlphaBlend(2, 1, col, 14, 100, trg, outi, outW, nr);
            _AlphaBlend(1, 2, col, 14, 100, trg, outi, outW, nr);
        }

        // ============================================================
        // —— 4X 專屬混合函式 ——
        // ============================================================
        static void BlendLineShallow4X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(3, 0, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(2, 2, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(3, 1, col, 3, 4, trg, outi, outW, nr);
            _AlphaBlend(2, 3, col, 3, 4, trg, outi, outW, nr);
            _SetPixel(3, 2, col, trg, outi, outW, nr);
            _SetPixel(3, 3, col, trg, outi, outW, nr);
        }
        static void BlendLineSteep4X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(0, 3, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(2, 2, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(1, 3, col, 3, 4, trg, outi, outW, nr);
            _AlphaBlend(3, 2, col, 3, 4, trg, outi, outW, nr);
            _SetPixel(2, 3, col, trg, outi, outW, nr);
            _SetPixel(3, 3, col, trg, outi, outW, nr);
        }
        static void BlendLineSteepAndShallow4X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(3, 1, col, 3, 4, trg, outi, outW, nr);
            _AlphaBlend(1, 3, col, 3, 4, trg, outi, outW, nr);
            _AlphaBlend(3, 0, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(0, 3, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(2, 2, col, 1, 3, trg, outi, outW, nr);
            _SetPixel(3, 3, col, trg, outi, outW, nr);
            _SetPixel(3, 2, col, trg, outi, outW, nr);
            _SetPixel(2, 3, col, trg, outi, outW, nr);
        }
        static void BlendLineDiagonal4X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(3, 2, col, 1, 2, trg, outi, outW, nr);
            _AlphaBlend(2, 3, col, 1, 2, trg, outi, outW, nr);
            _SetPixel(3, 3, col, trg, outi, outW, nr);
        }
        static void BlendCorner4X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(3, 3, col, 68, 100, trg, outi, outW, nr);
            _AlphaBlend(3, 2, col, 9, 100, trg, outi, outW, nr);
            _AlphaBlend(2, 3, col, 9, 100, trg, outi, outW, nr);
        }

        // ============================================================
        // —— 5X 專屬混合函式 ——
        // ============================================================
        static void BlendLineShallow5X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(4, 0, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(3, 2, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(2, 4, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(4, 1, col, 3, 4, trg, outi, outW, nr);
            _AlphaBlend(3, 3, col, 3, 4, trg, outi, outW, nr);
            _SetPixel(4, 2, col, trg, outi, outW, nr);
            _SetPixel(4, 3, col, trg, outi, outW, nr);
            _SetPixel(4, 4, col, trg, outi, outW, nr);
            _SetPixel(3, 4, col, trg, outi, outW, nr);
        }
        static void BlendLineSteep5X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(0, 4, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(2, 3, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(4, 2, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(1, 4, col, 3, 4, trg, outi, outW, nr);
            _AlphaBlend(3, 3, col, 3, 4, trg, outi, outW, nr);
            _SetPixel(2, 4, col, trg, outi, outW, nr);
            _SetPixel(3, 4, col, trg, outi, outW, nr);
            _SetPixel(4, 4, col, trg, outi, outW, nr);
            _SetPixel(4, 3, col, trg, outi, outW, nr);
        }
        static void BlendLineSteepAndShallow5X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(0, 4, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(2, 3, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(1, 4, col, 3, 4, trg, outi, outW, nr);
            _AlphaBlend(4, 0, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(3, 2, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(4, 1, col, 3, 4, trg, outi, outW, nr);
            _SetPixel(2, 4, col, trg, outi, outW, nr);
            _SetPixel(3, 4, col, trg, outi, outW, nr);
            _SetPixel(4, 2, col, trg, outi, outW, nr);
            _SetPixel(4, 3, col, trg, outi, outW, nr);
            _SetPixel(4, 4, col, trg, outi, outW, nr);
            _AlphaBlend(3, 3, col, 2, 3, trg, outi, outW, nr);
        }
        static void BlendLineDiagonal5X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(4, 2, col, 1, 8, trg, outi, outW, nr);
            _AlphaBlend(3, 3, col, 1, 8, trg, outi, outW, nr);
            _AlphaBlend(2, 4, col, 1, 8, trg, outi, outW, nr);
            _AlphaBlend(4, 3, col, 7, 8, trg, outi, outW, nr);
            _AlphaBlend(3, 4, col, 7, 8, trg, outi, outW, nr);
            _SetPixel(4, 4, col, trg, outi, outW, nr);
        }
        static void BlendCorner5X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(4, 4, col, 86, 100, trg, outi, outW, nr);
            _AlphaBlend(4, 3, col, 23, 100, trg, outi, outW, nr);
            _AlphaBlend(3, 4, col, 23, 100, trg, outi, outW, nr);
        }

        // ============================================================
        // —— 6X 專屬混合函式 ——
        // ============================================================
        static void BlendLineShallow6X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(5, 0, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(4, 2, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(3, 4, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(5, 1, col, 3, 4, trg, outi, outW, nr);
            _AlphaBlend(4, 3, col, 3, 4, trg, outi, outW, nr);
            _AlphaBlend(3, 5, col, 3, 4, trg, outi, outW, nr);
            _SetPixel(5, 2, col, trg, outi, outW, nr); _SetPixel(5, 3, col, trg, outi, outW, nr);
            _SetPixel(5, 4, col, trg, outi, outW, nr); _SetPixel(5, 5, col, trg, outi, outW, nr);
            _SetPixel(4, 4, col, trg, outi, outW, nr); _SetPixel(4, 5, col, trg, outi, outW, nr);
        }
        static void BlendLineSteep6X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(0, 5, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(2, 4, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(4, 3, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(1, 5, col, 3, 4, trg, outi, outW, nr);
            _AlphaBlend(3, 4, col, 3, 4, trg, outi, outW, nr);
            _AlphaBlend(5, 3, col, 3, 4, trg, outi, outW, nr);
            _SetPixel(2, 5, col, trg, outi, outW, nr); _SetPixel(3, 5, col, trg, outi, outW, nr);
            _SetPixel(4, 5, col, trg, outi, outW, nr); _SetPixel(5, 5, col, trg, outi, outW, nr);
            _SetPixel(4, 4, col, trg, outi, outW, nr); _SetPixel(5, 4, col, trg, outi, outW, nr);
        }
        static void BlendLineSteepAndShallow6X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(0, 5, col, 1, 4, trg, outi, outW, nr); _AlphaBlend(2, 4, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(1, 5, col, 3, 4, trg, outi, outW, nr); _AlphaBlend(3, 4, col, 3, 4, trg, outi, outW, nr);
            _AlphaBlend(5, 0, col, 1, 4, trg, outi, outW, nr); _AlphaBlend(4, 2, col, 1, 4, trg, outi, outW, nr);
            _AlphaBlend(5, 1, col, 3, 4, trg, outi, outW, nr); _AlphaBlend(4, 3, col, 3, 4, trg, outi, outW, nr);
            _SetPixel(2, 5, col, trg, outi, outW, nr); _SetPixel(3, 5, col, trg, outi, outW, nr);
            _SetPixel(4, 5, col, trg, outi, outW, nr); _SetPixel(5, 5, col, trg, outi, outW, nr);
            _SetPixel(4, 4, col, trg, outi, outW, nr); _SetPixel(5, 4, col, trg, outi, outW, nr);
            _SetPixel(5, 2, col, trg, outi, outW, nr); _SetPixel(5, 3, col, trg, outi, outW, nr);
        }
        static void BlendLineDiagonal6X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(5, 3, col, 1, 2, trg, outi, outW, nr);
            _AlphaBlend(4, 4, col, 1, 2, trg, outi, outW, nr);
            _AlphaBlend(3, 5, col, 1, 2, trg, outi, outW, nr);
            _SetPixel(4, 5, col, trg, outi, outW, nr); _SetPixel(5, 5, col, trg, outi, outW, nr); _SetPixel(5, 4, col, trg, outi, outW, nr);
        }
        static void BlendCorner6X(uint col, uint* trg, int outi, int outW, int nr)
        {
            _AlphaBlend(5, 5, col, 97, 100, trg, outi, outW, nr);
            _AlphaBlend(4, 5, col, 42, 100, trg, outi, outW, nr);
            _AlphaBlend(5, 4, col, 42, 100, trg, outi, outW, nr);
            _AlphaBlend(5, 3, col, 6, 100, trg, outi, outW, nr);
            _AlphaBlend(3, 5, col, 6, 100, trg, outi, outW, nr);
        }
    }
}