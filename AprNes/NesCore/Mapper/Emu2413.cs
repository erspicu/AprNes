// emu2413 v1.5.9 — C# port for AprNes
// Original: https://github.com/digital-sound-antiques/emu2413
// Copyright (C) 2020 Mitsutaka Okazaki
// Ported from Mesen2's copy (ref/Mesen2-master/Core/Shared/Utilities/emu2413.cpp)
//
// YM2413 (OPLL) FM synthesis engine — used by VRC7 (Mapper 085)
// VRC7 uses chip_type=1 (6 FM channels, no rhythm mode)

using System;
using System.Runtime.CompilerServices;

namespace AprNes
{
    public class Emu2413
    {
        // ── Constants ──────────────────────────────────────────────────
        const int DP_BITS = 19;
        const int DP_WIDTH = 1 << DP_BITS;
        const int PG_BITS = 10;
        const int PG_WIDTH = 1 << PG_BITS;
        const int DP_BASE_BITS = DP_BITS - PG_BITS;

        const double EG_STEP = 0.375;
        const int EG_BITS = 7;
        const int EG_MUTE = (1 << EG_BITS) - 1; // 127
        const int EG_MAX = EG_MUTE - 4;          // 123

        const double TL_STEP = 0.75;
        const int TL_BITS = 6;
        const double SL_STEP = 3.0;
        const int SL_BITS = 4;
        const int DAMPER_RATE = 12;

        const int SLOT_BD1 = 12, SLOT_BD2 = 13, SLOT_HH = 14, SLOT_SD = 15, SLOT_TOM = 16, SLOT_CYM = 17;

        // Envelope states
        const int ATTACK = 0, DECAY = 1, SUSTAIN = 2, RELEASE = 3, DAMP = 4;

        // Update flags
        const int UPDATE_WS = 1, UPDATE_TLL = 2, UPDATE_RKS = 4, UPDATE_EG = 8, UPDATE_ALL = 255;

        // ── Static lookup tables (shared across all instances) ─────────
        static bool tablesInitialized;

        static readonly ushort[] exp_table = {
            0,    3,    6,    8,    11,   14,   17,   20,   22,   25,   28,   31,   34,   37,   40,   42,
            45,   48,   51,   54,   57,   60,   63,   66,   69,   72,   75,   78,   81,   84,   87,   90,
            93,   96,   99,   102,  105,  108,  111,  114,  117,  120,  123,  126,  130,  133,  136,  139,
            142,  145,  148,  152,  155,  158,  161,  164,  168,  171,  174,  177,  181,  184,  187,  190,
            194,  197,  200,  204,  207,  210,  214,  217,  220,  224,  227,  231,  234,  237,  241,  244,
            248,  251,  255,  258,  262,  265,  268,  272,  276,  279,  283,  286,  290,  293,  297,  300,
            304,  308,  311,  315,  318,  322,  326,  329,  333,  337,  340,  344,  348,  352,  355,  359,
            363,  367,  370,  374,  378,  382,  385,  389,  393,  397,  401,  405,  409,  412,  416,  420,
            424,  428,  432,  436,  440,  444,  448,  452,  456,  460,  464,  468,  472,  476,  480,  484,
            488,  492,  496,  501,  505,  509,  513,  517,  521,  526,  530,  534,  538,  542,  547,  551,
            555,  560,  564,  568,  572,  577,  581,  585,  590,  594,  599,  603,  607,  612,  616,  621,
            625,  630,  634,  639,  643,  648,  652,  657,  661,  666,  670,  675,  680,  684,  689,  693,
            698,  703,  708,  712,  717,  722,  726,  731,  736,  741,  745,  750,  755,  760,  765,  770,
            774,  779,  784,  789,  794,  799,  804,  809,  814,  819,  824,  829,  834,  839,  844,  849,
            854,  859,  864,  869,  874,  880,  885,  890,  895,  900,  906,  911,  916,  921,  927,  932,
            937,  942,  948,  953,  959,  964,  969,  975,  980,  986,  991,  996, 1002, 1007, 1013, 1018
        };

        static readonly ushort[] fullsin_table_init = {
            2137, 1731, 1543, 1419, 1326, 1252, 1190, 1137, 1091, 1050, 1013, 979,  949,  920,  894,  869,
            846,  825,  804,  785,  767,  749,  732,  717,  701,  687,  672,  659,  646,  633,  621,  609,
            598,  587,  576,  566,  556,  546,  536,  527,  518,  509,  501,  492,  484,  476,  468,  461,
            453,  446,  439,  432,  425,  418,  411,  405,  399,  392,  386,  380,  375,  369,  363,  358,
            352,  347,  341,  336,  331,  326,  321,  316,  311,  307,  302,  297,  293,  289,  284,  280,
            276,  271,  267,  263,  259,  255,  251,  248,  244,  240,  236,  233,  229,  226,  222,  219,
            215,  212,  209,  205,  202,  199,  196,  193,  190,  187,  184,  181,  178,  175,  172,  169,
            167,  164,  161,  159,  156,  153,  151,  148,  146,  143,  141,  138,  136,  134,  131,  129,
            127,  125,  122,  120,  118,  116,  114,  112,  110,  108,  106,  104,  102,  100,  98,   96,
            94,   92,   91,   89,   87,   85,   83,   82,   80,   78,   77,   75,   74,   72,   70,   69,
            67,   66,   64,   63,   62,   60,   59,   57,   56,   55,   53,   52,   51,   49,   48,   47,
            46,   45,   43,   42,   41,   40,   39,   38,   37,   36,   35,   34,   33,   32,   31,   30,
            29,   28,   27,   26,   25,   24,   23,   23,   22,   21,   20,   20,   19,   18,   17,   17,
            16,   15,   15,   14,   13,   13,   12,   12,   11,   10,   10,   9,    9,    8,    8,    7,
            7,    7,    6,    6,    5,    5,    5,    4,    4,    4,    3,    3,    3,    2,    2,    2,
            2,    1,    1,    1,    1,    1,    1,    1,    0,    0,    0,    0,    0,    0,    0,    0,
        };

        static readonly ushort[] fullsin_table = new ushort[PG_WIDTH];
        static readonly ushort[] halfsin_table = new ushort[PG_WIDTH];
        static readonly ushort[][] wave_table_map = new ushort[2][];

        static readonly sbyte[][] pm_table = {
            new sbyte[] {0, 0, 0, 0, 0, 0, 0, 0},
            new sbyte[] {0, 0, 1, 0, 0, 0, -1, 0},
            new sbyte[] {0, 1, 2, 1, 0, -1, -2, -1},
            new sbyte[] {0, 1, 3, 1, 0, -1, -3, -1},
            new sbyte[] {0, 2, 4, 2, 0, -2, -4, -2},
            new sbyte[] {0, 2, 5, 2, 0, -2, -5, -2},
            new sbyte[] {0, 3, 6, 3, 0, -3, -6, -3},
            new sbyte[] {0, 3, 7, 3, 0, -3, -7, -3},
        };

        static readonly byte[] am_table = {
            0,  0,  0,  0,  0,  0,  0,  0,  1,  1,  1,  1,  1,  1,  1,  1,
            2,  2,  2,  2,  2,  2,  2,  2,  3,  3,  3,  3,  3,  3,  3,  3,
            4,  4,  4,  4,  4,  4,  4,  4,  5,  5,  5,  5,  5,  5,  5,  5,
            6,  6,  6,  6,  6,  6,  6,  6,  7,  7,  7,  7,  7,  7,  7,  7,
            8,  8,  8,  8,  8,  8,  8,  8,  9,  9,  9,  9,  9,  9,  9,  9,
            10, 10, 10, 10, 10, 10, 10, 10, 11, 11, 11, 11, 11, 11, 11, 11,
            12, 12, 12, 12, 12, 12, 12, 12,
            13, 13, 13,
            12, 12, 12, 12, 12, 12, 12, 12,
            11, 11, 11, 11, 11, 11, 11, 11, 10, 10, 10, 10, 10, 10, 10, 10,
            9,  9,  9,  9,  9,  9,  9,  9,  8,  8,  8,  8,  8,  8,  8,  8,
            7,  7,  7,  7,  7,  7,  7,  7,  6,  6,  6,  6,  6,  6,  6,  6,
            5,  5,  5,  5,  5,  5,  5,  5,  4,  4,  4,  4,  4,  4,  4,  4,
            3,  3,  3,  3,  3,  3,  3,  3,  2,  2,  2,  2,  2,  2,  2,  2,
            1,  1,  1,  1,  1,  1,  1,  1,  0,  0,  0,  0,  0,  0,  0
        };

        static readonly byte[][] eg_step_tables = {
            new byte[] {0, 1, 0, 1, 0, 1, 0, 1},
            new byte[] {0, 1, 0, 1, 1, 1, 0, 1},
            new byte[] {0, 1, 1, 1, 0, 1, 1, 1},
            new byte[] {0, 1, 1, 1, 1, 1, 1, 1},
        };

        static readonly uint[] ml_table = { 1, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 20, 24, 24, 30, 30 };

        static readonly double[] kl_table = {
            0.000, 18.000, 24.000, 27.750, 30.000, 32.250,
            33.750, 35.250, 36.000, 37.500, 38.250, 39.000,
            39.750, 40.500, 41.250, 42.000
        };

        static readonly uint[,,] tll_table = new uint[128, 64, 4]; // [blk<<4|fnum][TL][KL]
        static readonly int[,] rks_table = new int[16, 2];

        // Default instrument patches (index 1 = VRC7)
        static readonly byte[][] default_inst = {
            new byte[] { // YM2413
                0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
                0x71,0x61,0x1e,0x17,0xd0,0x78,0x00,0x17,
                0x13,0x41,0x1a,0x0d,0xd8,0xf7,0x23,0x13,
                0x13,0x01,0x99,0x00,0xf2,0xc4,0x21,0x23,
                0x11,0x61,0x0e,0x07,0x8d,0x64,0x70,0x27,
                0x32,0x21,0x1e,0x06,0xe1,0x76,0x01,0x28,
                0x31,0x22,0x16,0x05,0xe0,0x71,0x00,0x18,
                0x21,0x61,0x1d,0x07,0x82,0x81,0x11,0x07,
                0x33,0x21,0x2d,0x13,0xb0,0x70,0x00,0x07,
                0x61,0x61,0x1b,0x06,0x64,0x65,0x10,0x17,
                0x41,0x61,0x0b,0x18,0x85,0xf0,0x81,0x07,
                0x33,0x01,0x83,0x11,0xea,0xef,0x10,0x04,
                0x17,0xc1,0x24,0x07,0xf8,0xf8,0x22,0x12,
                0x61,0x50,0x0c,0x05,0xd2,0xf5,0x40,0x42,
                0x01,0x01,0x55,0x03,0xe9,0x90,0x03,0x02,
                0x41,0x41,0x89,0x03,0xf1,0xe4,0xc0,0x13,
                0x01,0x01,0x18,0x0f,0xdf,0xf8,0x6a,0x6d,
                0x01,0x01,0x00,0x00,0xc8,0xd8,0xa7,0x68,
                0x05,0x01,0x00,0x00,0xf8,0xaa,0x59,0x55,
            },
            new byte[] { // VRC7 (Nuke.YKT)
                0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
                0x03,0x21,0x05,0x06,0xe8,0x81,0x42,0x27,
                0x13,0x41,0x14,0x0d,0xd8,0xf6,0x23,0x12,
                0x11,0x11,0x08,0x08,0xfa,0xb2,0x20,0x12,
                0x31,0x61,0x0c,0x07,0xa8,0x64,0x61,0x27,
                0x32,0x21,0x1e,0x06,0xe1,0x76,0x01,0x28,
                0x02,0x01,0x06,0x00,0xa3,0xe2,0xf4,0xf4,
                0x21,0x61,0x1d,0x07,0x82,0x81,0x11,0x07,
                0x23,0x21,0x22,0x17,0xa2,0x72,0x01,0x17,
                0x35,0x11,0x25,0x00,0x40,0x73,0x72,0x01,
                0xb5,0x01,0x0f,0x0F,0xa8,0xa5,0x51,0x02,
                0x17,0xc1,0x24,0x07,0xf8,0xf8,0x22,0x12,
                0x71,0x23,0x11,0x06,0x65,0x74,0x18,0x16,
                0x01,0x02,0xd3,0x05,0xc9,0x95,0x03,0x02,
                0x61,0x63,0x0c,0x00,0x94,0xC0,0x33,0xf6,
                0x21,0x72,0x0d,0x00,0xc1,0xd5,0x56,0x06,
                0x01,0x01,0x18,0x0f,0xdf,0xf8,0x6a,0x6d,
                0x01,0x01,0x00,0x00,0xc8,0xd8,0xa7,0x68,
                0x05,0x01,0x00,0x00,0xf8,0xaa,0x59,0x55,
            },
            new byte[] { // YMF281B
                0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
                0x62,0x21,0x1a,0x07,0xf0,0x6f,0x00,0x16,
                0x40,0x10,0x45,0x00,0xf6,0x83,0x73,0x63,
                0x13,0x01,0x99,0x00,0xf2,0xc3,0x21,0x23,
                0x01,0x61,0x0b,0x0f,0xf9,0x64,0x70,0x17,
                0x32,0x21,0x1e,0x06,0xe1,0x76,0x01,0x28,
                0x60,0x01,0x82,0x0e,0xf9,0x61,0x20,0x27,
                0x21,0x61,0x1c,0x07,0x84,0x81,0x11,0x07,
                0x37,0x32,0xc9,0x01,0x66,0x64,0x40,0x28,
                0x01,0x21,0x07,0x03,0xa5,0x71,0x51,0x07,
                0x06,0x01,0x5e,0x07,0xf3,0xf3,0xf6,0x13,
                0x00,0x00,0x18,0x06,0xf5,0xf3,0x20,0x23,
                0x17,0xc1,0x24,0x07,0xf8,0xf8,0x22,0x12,
                0x35,0x64,0x00,0x00,0xff,0xf3,0x77,0xf5,
                0x11,0x31,0x00,0x07,0xdd,0xf3,0xff,0xfb,
                0x3a,0x21,0x00,0x07,0x80,0x84,0x0f,0xf5,
                0x01,0x01,0x18,0x0f,0xdf,0xf8,0x6a,0x6d,
                0x01,0x01,0x00,0x00,0xc8,0xd8,0xa7,0x68,
                0x05,0x01,0x00,0x00,0xf8,0xaa,0x59,0x55,
            }
        };

        static readonly Patch null_patch = new Patch();
        static readonly Patch[] default_patches = new Patch[3 * 19 * 2]; // [tone_num][patch_idx]

        // ── Patch struct ──────────────────────────────────────────────
        class Patch
        {
            public uint TL, FB, EG, ML, AR, DR, SL, RR, KR, KL, AM, PM, WS;
        }

        // ── Slot struct ──────────────────────────────────────────────
        class Slot
        {
            public byte number;
            public byte type; // bit0: 0=mod 1=car, bit1: single slot mode
            public Patch patch;
            public int[] output = new int[2];
            public ushort[] wave_table;
            public uint pg_phase;
            public uint pg_out;
            public byte pg_keep;
            public ushort blk_fnum;
            public ushort fnum;
            public byte blk;
            public byte eg_state;
            public int volume;
            public byte key_flag;
            public byte sus_flag;
            public ushort tll;
            public byte rks;
            public byte eg_rate_h;
            public byte eg_rate_l;
            public uint eg_shift;
            public uint eg_out;
            public uint update_requests;
        }

        // ── OPLL instance state ──────────────────────────────────────
        uint clk, rate;
        byte chip_type;
        uint adr;
        double inp_step, out_step, out_time;
        byte[] reg = new byte[0x40];
        byte test_flag;
        uint slot_key_status;
        byte rhythm_mode;
        uint eg_counter;
        uint pm_phase;
        int am_phase;
        byte lfo_am;
        uint noise;
        byte short_noise;
        int[] patch_number = new int[9];
        Slot[] slot = new Slot[18];
        Patch[] patches = new Patch[19 * 2];
        short[] ch_out = new short[14];
        short[] mix_out = new short[2];

        // No rate converter needed — we clock at exactly clk/72 = 49716 Hz

        // ── Static table initialization ──────────────────────────────
        static void InitializeTables()
        {
            if (tablesInitialized) return;

            // fullsin_table: first quarter is provided, mirror it
            Array.Copy(fullsin_table_init, fullsin_table, PG_WIDTH / 4);
            for (int x = 0; x < PG_WIDTH / 4; x++)
                fullsin_table[PG_WIDTH / 4 + x] = fullsin_table[PG_WIDTH / 4 - x - 1];
            for (int x = 0; x < PG_WIDTH / 2; x++)
                fullsin_table[PG_WIDTH / 2 + x] = (ushort)(0x8000 | fullsin_table[x]);

            // halfsin_table
            for (int x = 0; x < PG_WIDTH / 2; x++)
                halfsin_table[x] = fullsin_table[x];
            for (int x = PG_WIDTH / 2; x < PG_WIDTH; x++)
                halfsin_table[x] = 0xfff;

            wave_table_map[0] = fullsin_table;
            wave_table_map[1] = halfsin_table;

            // tll_table
            for (int fnum = 0; fnum < 16; fnum++)
            {
                for (int block = 0; block < 8; block++)
                {
                    for (int TL = 0; TL < 64; TL++)
                    {
                        for (int KL = 0; KL < 4; KL++)
                        {
                            int idx = (block << 4) | fnum;
                            if (KL == 0)
                            {
                                tll_table[idx, TL, KL] = (uint)(TL << 1);
                            }
                            else
                            {
                                int tmp = (int)(kl_table[fnum] - 6.0 * (7 - block)); // dB2(3.0) = 6.0
                                if (tmp <= 0)
                                    tll_table[idx, TL, KL] = (uint)(TL << 1);
                                else
                                    tll_table[idx, TL, KL] = (uint)((tmp >> (3 - KL)) / EG_STEP) + (uint)(TL << 1);
                            }
                        }
                    }
                }
            }

            // rks_table
            for (int fnum8 = 0; fnum8 < 2; fnum8++)
                for (int block = 0; block < 8; block++)
                {
                    rks_table[(block << 1) | fnum8, 1] = (block << 1) + fnum8;
                    rks_table[(block << 1) | fnum8, 0] = block >> 1;
                }

            // default_patches
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 19; j++)
                {
                    int baseIdx = i * 19 * 2 + j * 2;
                    default_patches[baseIdx] = new Patch();
                    default_patches[baseIdx + 1] = new Patch();
                    DumpToPatch(default_inst[i], j * 8, default_patches[baseIdx], default_patches[baseIdx + 1]);
                }

            tablesInitialized = true;
        }

        static void DumpToPatch(byte[] dump, int offset, Patch p0, Patch p1)
        {
            p0.AM = (uint)(dump[offset + 0] >> 7) & 1;
            p1.AM = (uint)(dump[offset + 1] >> 7) & 1;
            p0.PM = (uint)(dump[offset + 0] >> 6) & 1;
            p1.PM = (uint)(dump[offset + 1] >> 6) & 1;
            p0.EG = (uint)(dump[offset + 0] >> 5) & 1;
            p1.EG = (uint)(dump[offset + 1] >> 5) & 1;
            p0.KR = (uint)(dump[offset + 0] >> 4) & 1;
            p1.KR = (uint)(dump[offset + 1] >> 4) & 1;
            p0.ML = (uint)dump[offset + 0] & 15;
            p1.ML = (uint)dump[offset + 1] & 15;
            p0.KL = (uint)(dump[offset + 2] >> 6) & 3;
            p1.KL = (uint)(dump[offset + 3] >> 6) & 3;
            p0.TL = (uint)dump[offset + 2] & 63;
            p1.TL = 0;
            p0.FB = (uint)dump[offset + 3] & 7;
            p1.FB = 0;
            p0.WS = (uint)(dump[offset + 3] >> 3) & 1;
            p1.WS = (uint)(dump[offset + 3] >> 4) & 1;
            p0.AR = (uint)(dump[offset + 4] >> 4) & 15;
            p1.AR = (uint)(dump[offset + 5] >> 4) & 15;
            p0.DR = (uint)dump[offset + 4] & 15;
            p1.DR = (uint)dump[offset + 5] & 15;
            p0.SL = (uint)(dump[offset + 6] >> 4) & 15;
            p1.SL = (uint)(dump[offset + 7] >> 4) & 15;
            p0.RR = (uint)dump[offset + 6] & 15;
            p1.RR = (uint)dump[offset + 7] & 15;
        }

        // ── Constructor / Reset ──────────────────────────────────────
        public Emu2413(uint clk, uint rate)
        {
            InitializeTables();

            this.clk = clk;
            this.rate = rate;

            for (int i = 0; i < 19 * 2; i++)
                patches[i] = new Patch();

            for (int i = 0; i < 18; i++)
                slot[i] = new Slot();

            Reset();
            SetChipType(1); // VRC7
            ResetPatch(1);  // VRC7 tone set
        }

        public void Reset()
        {
            adr = 0;
            pm_phase = 0;
            am_phase = 0;
            noise = 1;
            rhythm_mode = 0;
            slot_key_status = 0;
            eg_counter = 0;

            // Rate conversion: set to native rate (no converter)
            out_time = 0;
            out_step = clk / 72.0;
            inp_step = rate;

            for (int i = 0; i < 18; i++)
                ResetSlot(slot[i], i);

            for (int i = 0; i < 9; i++)
                SetPatch(i, 0);

            for (int i = 0; i < 0x40; i++)
                WriteReg((uint)i, 0);

            for (int i = 0; i < 14; i++)
                ch_out[i] = 0;

            mix_out[0] = mix_out[1] = 0;
        }

        public void SetChipType(byte type) { chip_type = type; }

        public void ResetPatch(byte type)
        {
            int toneIdx = type % 3;
            for (int i = 0; i < 19 * 2; i++)
            {
                Patch src = default_patches[toneIdx * 19 * 2 + i];
                CopyPatch(patches[i], src);
            }
        }

        static void CopyPatch(Patch dst, Patch src)
        {
            dst.TL = src.TL; dst.FB = src.FB; dst.EG = src.EG; dst.ML = src.ML;
            dst.AR = src.AR; dst.DR = src.DR; dst.SL = src.SL; dst.RR = src.RR;
            dst.KR = src.KR; dst.KL = src.KL; dst.AM = src.AM; dst.PM = src.PM;
            dst.WS = src.WS;
        }

        void ResetSlot(Slot s, int number)
        {
            s.number = (byte)number;
            s.type = (byte)(number % 2);
            s.pg_keep = 0;
            s.wave_table = wave_table_map[0];
            s.pg_phase = 0;
            s.output[0] = s.output[1] = 0;
            s.eg_state = RELEASE;
            s.eg_shift = 0;
            s.rks = 0;
            s.tll = 0;
            s.key_flag = 0;
            s.sus_flag = 0;
            s.blk_fnum = 0;
            s.blk = 0;
            s.fnum = 0;
            s.volume = 0;
            s.pg_out = 0;
            s.eg_out = EG_MUTE;
            s.patch = null_patch;
        }

        // ── Slot helpers ─────────────────────────────────────────────
        Slot MOD(int ch) => slot[ch << 1];
        Slot CAR(int ch) => slot[(ch << 1) | 1];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Min(int a, int b) => a < b ? a : b;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Max(int a, int b) => a > b ? a : b;

        void SlotOn(int i)
        {
            slot[i].key_flag = 1;
            slot[i].eg_state = DAMP;
            slot[i].update_requests |= UPDATE_EG;
        }

        void SlotOff(int i)
        {
            slot[i].key_flag = 0;
            if ((slot[i].type & 1) != 0)
            {
                slot[i].eg_state = RELEASE;
                slot[i].update_requests |= UPDATE_EG;
            }
        }

        void UpdateKeyStatus()
        {
            byte r14 = reg[0x0e];
            byte rhythmMode = (byte)((r14 >> 5) & 1);
            uint newStatus = 0;

            for (int ch = 0; ch < 9; ch++)
                if ((reg[0x20 + ch] & 0x10) != 0)
                    newStatus |= (uint)(3 << (ch * 2));

            if (rhythmMode != 0)
            {
                if ((r14 & 0x10) != 0) newStatus |= 3u << SLOT_BD1;
                if ((r14 & 0x01) != 0) newStatus |= 1u << SLOT_HH;
                if ((r14 & 0x08) != 0) newStatus |= 1u << SLOT_SD;
                if ((r14 & 0x04) != 0) newStatus |= 1u << SLOT_TOM;
                if ((r14 & 0x02) != 0) newStatus |= 1u << SLOT_CYM;
            }

            uint updated = slot_key_status ^ newStatus;
            if (updated != 0)
            {
                for (int i = 0; i < 18; i++)
                    if (((updated >> i) & 1) != 0)
                    {
                        if (((newStatus >> i) & 1) != 0) SlotOn(i);
                        else SlotOff(i);
                    }
            }

            slot_key_status = newStatus;
        }

        void SetPatch(int ch, int num)
        {
            patch_number[ch] = num;
            MOD(ch).patch = patches[num * 2];
            CAR(ch).patch = patches[num * 2 + 1];
            MOD(ch).update_requests |= UPDATE_ALL;
            CAR(ch).update_requests |= UPDATE_ALL;
        }

        void SetSusFlag(int ch, int flag)
        {
            CAR(ch).sus_flag = (byte)flag;
            CAR(ch).update_requests |= UPDATE_EG;
            if ((MOD(ch).type & 1) != 0)
            {
                MOD(ch).sus_flag = (byte)flag;
                MOD(ch).update_requests |= UPDATE_EG;
            }
        }

        void SetVolume(int ch, int volume)
        {
            CAR(ch).volume = volume;
            CAR(ch).update_requests |= UPDATE_TLL;
        }

        void SetSlotVolume(Slot s, int volume)
        {
            s.volume = volume;
            s.update_requests |= UPDATE_TLL;
        }

        void SetFnumber(int ch, int fnum)
        {
            Slot car = CAR(ch), mod = MOD(ch);
            car.fnum = (ushort)fnum;
            car.blk_fnum = (ushort)((car.blk_fnum & 0xe00) | (fnum & 0x1ff));
            mod.fnum = (ushort)fnum;
            mod.blk_fnum = (ushort)((mod.blk_fnum & 0xe00) | (fnum & 0x1ff));
            car.update_requests |= UPDATE_EG | UPDATE_RKS | UPDATE_TLL;
            mod.update_requests |= UPDATE_EG | UPDATE_RKS | UPDATE_TLL;
        }

        void SetBlock(int ch, int blk)
        {
            Slot car = CAR(ch), mod = MOD(ch);
            car.blk = (byte)blk;
            car.blk_fnum = (ushort)(((blk & 7) << 9) | (car.blk_fnum & 0x1ff));
            mod.blk = (byte)blk;
            mod.blk_fnum = (ushort)(((blk & 7) << 9) | (mod.blk_fnum & 0x1ff));
            car.update_requests |= UPDATE_EG | UPDATE_RKS | UPDATE_TLL;
            mod.update_requests |= UPDATE_EG | UPDATE_RKS | UPDATE_TLL;
        }

        void UpdateRhythmMode()
        {
            byte newRhythmMode = (byte)((reg[0x0e] >> 5) & 1);

            if (rhythm_mode != newRhythmMode)
            {
                if (newRhythmMode != 0)
                {
                    slot[SLOT_HH].type = 3;
                    slot[SLOT_HH].pg_keep = 1;
                    slot[SLOT_SD].type = 3;
                    slot[SLOT_TOM].type = 3;
                    slot[SLOT_CYM].type = 3;
                    slot[SLOT_CYM].pg_keep = 1;
                    SetPatch(6, 16);
                    SetPatch(7, 17);
                    SetPatch(8, 18);
                    SetSlotVolume(slot[SLOT_HH], ((reg[0x37] >> 4) & 15) << 2);
                    SetSlotVolume(slot[SLOT_TOM], ((reg[0x38] >> 4) & 15) << 2);
                }
                else
                {
                    slot[SLOT_HH].type = 0;
                    slot[SLOT_HH].pg_keep = 0;
                    slot[SLOT_SD].type = 1;
                    slot[SLOT_TOM].type = 0;
                    slot[SLOT_CYM].type = 1;
                    slot[SLOT_CYM].pg_keep = 0;
                    SetPatch(6, reg[0x36] >> 4);
                    SetPatch(7, reg[0x37] >> 4);
                    SetPatch(8, reg[0x38] >> 4);
                }
            }
            rhythm_mode = newRhythmMode;
        }

        // ── Synthesis core ───────────────────────────────────────────
        static int GetParameterRate(Slot s)
        {
            if ((s.type & 1) == 0 && s.key_flag == 0) return 0;
            switch (s.eg_state)
            {
                case ATTACK: return (int)s.patch.AR;
                case DECAY: return (int)s.patch.DR;
                case SUSTAIN: return s.patch.EG != 0 ? 0 : (int)s.patch.RR;
                case RELEASE:
                    if (s.sus_flag != 0) return 5;
                    else if (s.patch.EG != 0) return (int)s.patch.RR;
                    else return 7;
                case DAMP: return DAMPER_RATE;
                default: return 0;
            }
        }

        static void CommitSlotUpdate(Slot s)
        {
            if ((s.update_requests & UPDATE_WS) != 0)
                s.wave_table = wave_table_map[s.patch.WS];

            if ((s.update_requests & UPDATE_TLL) != 0)
            {
                if ((s.type & 1) == 0)
                    s.tll = (ushort)tll_table[s.blk_fnum >> 5, (int)s.patch.TL, (int)s.patch.KL];
                else
                    s.tll = (ushort)tll_table[s.blk_fnum >> 5, s.volume, (int)s.patch.KL];
            }

            if ((s.update_requests & UPDATE_RKS) != 0)
                s.rks = (byte)rks_table[s.blk_fnum >> 8, (int)s.patch.KR];

            if ((s.update_requests & (UPDATE_RKS | UPDATE_EG)) != 0)
            {
                int pRate = GetParameterRate(s);
                if (pRate == 0)
                {
                    s.eg_shift = 0;
                    s.eg_rate_h = 0;
                    s.eg_rate_l = 0;
                    s.update_requests = 0;
                    return;
                }

                s.eg_rate_h = (byte)Min(15, pRate + (s.rks >> 2));
                s.eg_rate_l = (byte)(s.rks & 3);
                if (s.eg_state == ATTACK)
                    s.eg_shift = (0 < s.eg_rate_h && s.eg_rate_h < 12) ? (uint)(13 - s.eg_rate_h) : 0;
                else
                    s.eg_shift = (s.eg_rate_h < 13) ? (uint)(13 - s.eg_rate_h) : 0;
            }

            s.update_requests = 0;
        }

        static byte LookupAttackStep(Slot s, uint counter)
        {
            int index;
            switch (s.eg_rate_h)
            {
                case 12:
                    index = (int)((counter & 0xc) >> 1);
                    return (byte)(4 - eg_step_tables[s.eg_rate_l][index]);
                case 13:
                    index = (int)((counter & 0xc) >> 1);
                    return (byte)(3 - eg_step_tables[s.eg_rate_l][index]);
                case 14:
                    index = (int)((counter & 0xc) >> 1);
                    return (byte)(2 - eg_step_tables[s.eg_rate_l][index]);
                case 0:
                case 15:
                    return 0;
                default:
                    index = (int)(counter >> (int)s.eg_shift);
                    return eg_step_tables[s.eg_rate_l][index & 7] != 0 ? (byte)4 : (byte)0;
            }
        }

        static byte LookupDecayStep(Slot s, uint counter)
        {
            int index;
            switch (s.eg_rate_h)
            {
                case 0: return 0;
                case 13:
                    index = (int)(((counter & 0xc) >> 1) | (counter & 1));
                    return eg_step_tables[s.eg_rate_l][index];
                case 14:
                    index = (int)((counter & 0xc) >> 1);
                    return (byte)(eg_step_tables[s.eg_rate_l][index] + 1);
                case 15: return 2;
                default:
                    index = (int)(counter >> (int)s.eg_shift);
                    return eg_step_tables[s.eg_rate_l][index & 7];
            }
        }

        static void StartEnvelope(Slot s)
        {
            if (Min(15, (int)s.patch.AR + (s.rks >> 2)) == 15)
            {
                s.eg_state = DECAY;
                s.eg_out = 0;
            }
            else
            {
                s.eg_state = ATTACK;
            }
            s.update_requests |= UPDATE_EG;
        }

        static void CalcEnvelope(Slot s, Slot buddy, uint egCounter, byte test)
        {
            uint mask = (1u << (int)s.eg_shift) - 1;

            if (s.eg_state == ATTACK)
            {
                if (s.eg_out > 0 && s.eg_rate_h > 0 && (egCounter & mask & ~3u) == 0)
                {
                    byte step = LookupAttackStep(s, egCounter);
                    if (step > 0)
                        s.eg_out = (uint)Max(0, (int)(s.eg_out - (s.eg_out >> step) - 1));
                }
            }
            else
            {
                if (s.eg_rate_h > 0 && (egCounter & mask) == 0)
                    s.eg_out = (uint)Min(EG_MUTE, (int)(s.eg_out + LookupDecayStep(s, egCounter)));
            }

            switch (s.eg_state)
            {
                case DAMP:
                    if (s.eg_out >= EG_MAX && (egCounter & mask) == 0)
                    {
                        StartEnvelope(s);
                        if ((s.type & 1) != 0)
                        {
                            if (s.pg_keep == 0) s.pg_phase = 0;
                            if (buddy != null && buddy.pg_keep == 0) buddy.pg_phase = 0;
                        }
                    }
                    break;
                case ATTACK:
                    if (s.eg_out == 0)
                    {
                        s.eg_state = DECAY;
                        s.update_requests |= UPDATE_EG;
                    }
                    break;
                case DECAY:
                    if ((s.eg_out >> 3) == s.patch.SL)
                    {
                        s.eg_state = SUSTAIN;
                        s.update_requests |= UPDATE_EG;
                    }
                    break;
            }

            if (test != 0) s.eg_out = 0;
        }

        void UpdateAmPm()
        {
            if ((test_flag & 2) != 0)
            {
                pm_phase = 0;
                am_phase = 0;
            }
            else
            {
                pm_phase += (uint)((test_flag & 8) != 0 ? 1024 : 1);
                am_phase += (test_flag & 8) != 0 ? 64 : 1;
            }
            lfo_am = am_table[(am_phase >> 6) % am_table.Length];
        }

        void UpdateNoise(int cycle)
        {
            for (int i = 0; i < cycle; i++)
            {
                if ((noise & 1) != 0) noise ^= 0x800200;
                noise >>= 1;
            }
        }

        void UpdateShortNoise()
        {
            uint pg_hh = slot[SLOT_HH].pg_out;
            uint pg_cym = slot[SLOT_CYM].pg_out;
            byte h2 = (byte)((pg_hh >> (PG_BITS - 8)) & 1);
            byte h7 = (byte)((pg_hh >> (PG_BITS - 3)) & 1);
            byte h3 = (byte)((pg_hh >> (PG_BITS - 7)) & 1);
            byte c3 = (byte)((pg_cym >> (PG_BITS - 7)) & 1);
            byte c5 = (byte)((pg_cym >> (PG_BITS - 5)) & 1);
            short_noise = (byte)((h2 ^ h7) | (h3 ^ c5) | (c3 ^ c5));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CalcPhase(Slot s, uint pmPhase, byte reset)
        {
            int pm = s.patch.PM != 0 ? pm_table[(s.fnum >> 6) & 7][(pmPhase >> 10) & 7] : 0;
            if (reset != 0) s.pg_phase = 0;
            s.pg_phase += (uint)((((s.fnum & 0x1ff) * 2 + pm) * (int)ml_table[s.patch.ML]) << s.blk >> 2);
            s.pg_phase &= (DP_WIDTH - 1);
            s.pg_out = s.pg_phase >> DP_BASE_BITS;
        }

        void UpdateSlots()
        {
            eg_counter++;
            for (int i = 0; i < 18; i++)
            {
                Slot s = slot[i];
                Slot buddy = null;
                if (s.type == 0) buddy = slot[i + 1];
                if (s.type == 1) buddy = slot[i - 1];
                if (s.update_requests != 0) CommitSlotUpdate(s);
                CalcEnvelope(s, buddy, eg_counter, (byte)(test_flag & 1));
                CalcPhase(s, pm_phase, (byte)((test_flag >> 2) & 1));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static short LookupExpTable(ushort i)
        {
            short t = (short)(exp_table[(i & 0xff) ^ 0xff] + 1024);
            short res = (short)(t >> ((i & 0x7f00) >> 8));
            return (short)(((i & 0x8000) != 0 ? ~res : res) << 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static short ToLinear(ushort h, Slot s, short am)
        {
            if (s.eg_out > EG_MAX) return 0;
            ushort att = (ushort)(Min(EG_MUTE, (int)(s.eg_out + s.tll + am)) << 4);
            return LookupExpTable((ushort)(h + att));
        }

        short CalcSlotCar(int ch, short fm)
        {
            Slot s = CAR(ch);
            byte am = s.patch.AM != 0 ? lfo_am : (byte)0;
            s.output[1] = s.output[0];
            s.output[0] = ToLinear(s.wave_table[(s.pg_out + 2 * (fm >> 1)) & (PG_WIDTH - 1)], s, am);
            return (short)s.output[0];
        }

        short CalcSlotMod(int ch)
        {
            Slot s = MOD(ch);
            short fm = s.patch.FB > 0 ? (short)((s.output[1] + s.output[0]) >> (int)(9 - s.patch.FB)) : (short)0;
            byte am = s.patch.AM != 0 ? lfo_am : (byte)0;
            s.output[1] = s.output[0];
            s.output[0] = ToLinear(s.wave_table[(s.pg_out + (uint)fm) & (PG_WIDTH - 1)], s, am);
            return (short)s.output[0];
        }

        short CalcSlotTom()
        {
            Slot s = MOD(8);
            return ToLinear(s.wave_table[s.pg_out], s, 0);
        }

        short CalcSlotSnare()
        {
            Slot s = CAR(7);
            uint phase;
            if (((s.pg_out >> (PG_BITS - 2)) & 1) != 0)
                phase = (noise & 1) != 0 ? PD(0x300) : PD(0x200);
            else
                phase = (noise & 1) != 0 ? PD(0x0) : PD(0x100);
            return ToLinear(s.wave_table[phase], s, 0);
        }

        short CalcSlotCym()
        {
            Slot s = CAR(8);
            uint phase = short_noise != 0 ? PD(0x300) : PD(0x100);
            return ToLinear(s.wave_table[phase], s, 0);
        }

        short CalcSlotHat()
        {
            Slot s = MOD(7);
            uint phase;
            if (short_noise != 0)
                phase = (noise & 1) != 0 ? PD(0x2d0) : PD(0x234);
            else
                phase = (noise & 1) != 0 ? PD(0x34) : PD(0xd0);
            return ToLinear(s.wave_table[phase], s, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint PD(int phase)
        {
            // PG_BITS=10, so this is identity
            return (uint)phase;
        }

        void UpdateOutput()
        {
            UpdateAmPm();
            if (chip_type == 0) UpdateShortNoise();
            UpdateSlots();

            // CH1-6 (always computed)
            for (int i = 0; i < 6; i++)
                ch_out[i] = (short)(-(CalcSlotCar(i, CalcSlotMod(i))) >> 1);

            if (chip_type == 0)
            {
                // CH7
                if (rhythm_mode == 0)
                    ch_out[6] = (short)(-(CalcSlotCar(6, CalcSlotMod(6))) >> 1);
                else
                    ch_out[9] = CalcSlotCar(6, CalcSlotMod(6));
                UpdateNoise(14);

                // CH8
                if (rhythm_mode == 0)
                    ch_out[7] = (short)(-(CalcSlotCar(7, CalcSlotMod(7))) >> 1);
                else
                {
                    ch_out[10] = CalcSlotHat();
                    ch_out[11] = CalcSlotSnare();
                }
                UpdateNoise(2);

                // CH9
                if (rhythm_mode == 0)
                    ch_out[8] = (short)(-(CalcSlotCar(8, CalcSlotMod(8))) >> 1);
                else
                {
                    ch_out[12] = CalcSlotTom();
                    ch_out[13] = CalcSlotCym();
                }
                UpdateNoise(2);
            }
        }

        void MixOutput()
        {
            short outVal = 0;
            if (chip_type == 0)
            {
                for (int i = 0; i < 14; i++) outVal += ch_out[i];
            }
            else
            {
                for (int i = 0; i < 6; i++) outVal += ch_out[i];
            }
            mix_out[0] = outVal;
        }

        // ── Public API ───────────────────────────────────────────────
        public void WriteReg(uint regAddr, byte data)
        {
            if (regAddr >= 0x40) return;

            // mirror registers
            if ((0x19 <= regAddr && regAddr <= 0x1f) || (0x29 <= regAddr && regAddr <= 0x2f) || (0x39 <= regAddr && regAddr <= 0x3f))
                regAddr -= 9;

            reg[regAddr] = data;
            int ch;

            switch (regAddr)
            {
                case 0x00:
                    patches[0].AM = (uint)(data >> 7) & 1;
                    patches[0].PM = (uint)(data >> 6) & 1;
                    patches[0].EG = (uint)(data >> 5) & 1;
                    patches[0].KR = (uint)(data >> 4) & 1;
                    patches[0].ML = (uint)data & 15;
                    for (int i = 0; i < 9; i++)
                        if (patch_number[i] == 0) MOD(i).update_requests |= UPDATE_RKS | UPDATE_EG;
                    break;

                case 0x01:
                    patches[1].AM = (uint)(data >> 7) & 1;
                    patches[1].PM = (uint)(data >> 6) & 1;
                    patches[1].EG = (uint)(data >> 5) & 1;
                    patches[1].KR = (uint)(data >> 4) & 1;
                    patches[1].ML = (uint)data & 15;
                    for (int i = 0; i < 9; i++)
                        if (patch_number[i] == 0) CAR(i).update_requests |= UPDATE_RKS | UPDATE_EG;
                    break;

                case 0x02:
                    patches[0].KL = (uint)(data >> 6) & 3;
                    patches[0].TL = (uint)data & 63;
                    for (int i = 0; i < 9; i++)
                        if (patch_number[i] == 0) MOD(i).update_requests |= UPDATE_TLL;
                    break;

                case 0x03:
                    patches[1].KL = (uint)(data >> 6) & 3;
                    patches[1].WS = (uint)(data >> 4) & 1;
                    patches[0].WS = (uint)(data >> 3) & 1;
                    patches[0].FB = (uint)data & 7;
                    for (int i = 0; i < 9; i++)
                        if (patch_number[i] == 0)
                        {
                            MOD(i).update_requests |= UPDATE_WS;
                            CAR(i).update_requests |= UPDATE_WS | UPDATE_TLL;
                        }
                    break;

                case 0x04:
                    patches[0].AR = (uint)(data >> 4) & 15;
                    patches[0].DR = (uint)data & 15;
                    for (int i = 0; i < 9; i++)
                        if (patch_number[i] == 0) MOD(i).update_requests |= UPDATE_EG;
                    break;

                case 0x05:
                    patches[1].AR = (uint)(data >> 4) & 15;
                    patches[1].DR = (uint)data & 15;
                    for (int i = 0; i < 9; i++)
                        if (patch_number[i] == 0) CAR(i).update_requests |= UPDATE_EG;
                    break;

                case 0x06:
                    patches[0].SL = (uint)(data >> 4) & 15;
                    patches[0].RR = (uint)data & 15;
                    for (int i = 0; i < 9; i++)
                        if (patch_number[i] == 0) MOD(i).update_requests |= UPDATE_EG;
                    break;

                case 0x07:
                    patches[1].SL = (uint)(data >> 4) & 15;
                    patches[1].RR = (uint)data & 15;
                    for (int i = 0; i < 9; i++)
                        if (patch_number[i] == 0) CAR(i).update_requests |= UPDATE_EG;
                    break;

                case 0x0e:
                    if (chip_type == 1) break; // VRC7 ignores rhythm
                    UpdateRhythmMode();
                    UpdateKeyStatus();
                    break;

                case 0x0f:
                    test_flag = data;
                    break;

                case 0x10: case 0x11: case 0x12: case 0x13:
                case 0x14: case 0x15: case 0x16: case 0x17: case 0x18:
                    if (chip_type == 1 && regAddr >= 0x16) break;
                    ch = (int)regAddr - 0x10;
                    SetFnumber(ch, data + ((reg[0x20 + ch] & 1) << 8));
                    break;

                case 0x20: case 0x21: case 0x22: case 0x23:
                case 0x24: case 0x25: case 0x26: case 0x27: case 0x28:
                    if (chip_type == 1 && regAddr >= 0x26) break;
                    ch = (int)regAddr - 0x20;
                    SetFnumber(ch, ((data & 1) << 8) + reg[0x10 + ch]);
                    SetBlock(ch, (data >> 1) & 7);
                    SetSusFlag(ch, (data >> 5) & 1);
                    UpdateKeyStatus();
                    break;

                case 0x30: case 0x31: case 0x32: case 0x33:
                case 0x34: case 0x35: case 0x36: case 0x37: case 0x38:
                    if (chip_type == 1 && regAddr >= 0x36) break;
                    if ((reg[0x0e] & 32) != 0 && regAddr >= 0x36)
                    {
                        switch (regAddr)
                        {
                            case 0x37: SetSlotVolume(MOD(7), ((data >> 4) & 15) << 2); break;
                            case 0x38: SetSlotVolume(MOD(8), ((data >> 4) & 15) << 2); break;
                        }
                    }
                    else
                    {
                        SetPatch((int)regAddr - 0x30, (data >> 4) & 15);
                    }
                    SetVolume((int)regAddr - 0x30, (data & 15) << 2);
                    break;
            }
        }

        /// <summary>
        /// Calculate one sample at the native rate (clk/72).
        /// Returns a signed 16-bit sample.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public short Calc()
        {
            while (out_step > out_time)
            {
                out_time += inp_step;
                UpdateOutput();
                MixOutput();
            }
            out_time -= out_step;
            return mix_out[0];
        }
    }
}
