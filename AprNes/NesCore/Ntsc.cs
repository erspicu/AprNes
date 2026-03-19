using System;
using System.Runtime.CompilerServices;

namespace AprNes
{
    // ============================================================
    // NES NTSC 類比訊號模擬器
    // ============================================================
    //
    //  Level 2（預設，快速）：直接 YIQ + 9-entry LUT dot crawl
    //  Level 3（UltraAnalog，精確）：21.477 MHz 時域波形 + coherent demodulation
    //
    // ── Level 2 路徑 ─────────────────────────────────────────────
    //
    //   · 直接從電壓準位計算 Y、I、Q（blargg 公式）
    //   · 水平 3-tap FIR 模糊色度（色彩暈染近似）
    //   · 9-entry LUT 模擬 composite encode/decode → dot crawl
    //   · 每 scanline 相位前進 3（768 mod 9 = 3），週期 3 行
    //
    // ── Level 3 路徑（UltraAnalog）──────────────────────────────
    //
    //   Step 1  生成 21.477 MHz 複合視訊波形（4 samples/dot）
    //
    //     composite(t) = Y - sat × cos(2πt/6 − c × π/6)
    //                  = Y + sat × ( cosTab6[t%6] × iPhase[c]
    //                              - sinTab6[t%6] × qPhase[c] )
    //
    //   Step 2  Coherent demodulation（12-sample boxcar = 2 副載波週期）
    //
    //     Y = (1/N) × Σ composite(t)
    //     I = (2/N) × Σ composite(t) × cos(2πt/6)   [×2 歸一化]
    //     Q = -(2/N) × Σ composite(t) × sin(2πt/6)
    //
    //   Step 3  YIQ → RGB（blargg -15° 解碼矩陣 + fast gamma）
    //
    //   Dot crawl 自然浮現：每 scanline 副載波相位前進 1364 mod 6 = 2，
    //   解調相位對每個輸出像素不同，週期 3 行。
    //
    //   RF 音訊干擾：雜訊與 buzz bar 加入波形後再解調（物理正確）
    //
    // ── 輸出 ─────────────────────────────────────────────────────
    //
    //   768×720 BGRA → NesCore.AnalogScreenBuf3x
    // ============================================================

    unsafe public static class Ntsc
    {
        // ── 共用：blargg 電壓準位 ────────────────────────────────────────────
        static readonly float[] loLevels = { -0.12f, 0.00f, 0.31f, 0.72f };
        static readonly float[] hiLevels = {  0.40f, 0.68f, 1.00f, 1.00f };

        // ── 共用：色相查表 ───────────────────────────────────────────────────
        //   iPhase[c] = -cos(c × π/6),  qPhase[c] = sin(c × π/6)
        static readonly float[] iPhase = new float[16];
        static readonly float[] qPhase = new float[16];

        // ── 共用：RF 音訊干擾（由 APU.generateSample() 更新）────────────────
        static public float RfAudioLevel = 0.0f;
        static public float RfBuzzPhase  = 0.0f;

        // ── 輸出尺寸 ────────────────────────────────────────────────────────
        const int kOutW = 768;
        const int kOutH = 720;

        // ════════════════════════════════════════════════════════════════════
        // Level 2 — 簡化路徑（快速）
        // ════════════════════════════════════════════════════════════════════

        // 色副載波 9-entry LUT（每格 4π/9 ≈ 80°）
        // cosLUT[k] = cos(k × 4π/9),  sinLUT[k] = sin(k × 4π/9)
        static readonly float[] cosLUT = new float[9];
        static readonly float[] sinLUT = new float[9];

        // 每 dot 的 YIQ（256 dots/scanline）
        static readonly float[] dotY = new float[256];
        static readonly float[] dotI = new float[256];
        static readonly float[] dotQ = new float[256];

        // 掃描線相位索引（0-8），每 scanline 前進 3（768 mod 9 = 3），週期 9
        static int scanPhaseIdx = 0;

        // ════════════════════════════════════════════════════════════════════
        // Level 3 — 物理路徑（UltraAnalog）
        // ════════════════════════════════════════════════════════════════════

        // 副載波 LUT（週期 6 master clocks = 3.579545 MHz）
        static readonly float[] cosTab6 = new float[6];
        static readonly float[] sinTab6 = new float[6];

        // 波形緩衝區：[kLeadPad] [256×4 = 1024 samples] [kLeadPad]
        const int kDots    = 256;
        const int kSampDot = 4;
        const int kWaveLen = kDots * kSampDot;     // 1024
        const int kLeadPad = 12;
        const int kBufLen  = kLeadPad * 2 + kWaveLen; // 1048
        static readonly float[] waveBuf = new float[kBufLen];

        // 解調視窗（12 samples = 2 副載波週期，chroma BW ≈ 0.9 MHz）
        const int kWinSize = 12;
        const int kWinHalf = kWinSize / 2;

        // 掃描線副載波相位（每 scanline 前進 1364 mod 6 = 2，週期 3 行）
        static int scanPhaseBase = 0;

        // ════════════════════════════════════════════════════════════════════
        // Init
        // ════════════════════════════════════════════════════════════════════
        public static void Init()
        {
            // 共用：色相查表
            for (int c = 0; c < 16; c++)
            {
                double a = c * Math.PI / 6.0;
                iPhase[c] = -(float)Math.Cos(a);
                qPhase[c] =  (float)Math.Sin(a);
            }

            // Level 2：9-entry dot crawl LUT（每格 4π/9）
            for (int k = 0; k < 9; k++)
            {
                double a = k * 4.0 * Math.PI / 9.0;
                cosLUT[k] = (float)Math.Cos(a);
                sinLUT[k] = (float)Math.Sin(a);
            }

            // Level 3：6-entry 副載波 LUT（每格 2π/6）
            for (int k = 0; k < 6; k++)
            {
                double a = k * 2.0 * Math.PI / 6.0;
                cosTab6[k] = (float)Math.Cos(a);
                sinTab6[k] = (float)Math.Sin(a);
            }

            scanPhaseIdx  = 0;
            scanPhaseBase = 0;
            RfAudioLevel  = 0f;
            RfBuzzPhase   = 0f;
        }

        // ════════════════════════════════════════════════════════════════════
        // 主進入點（由 PPU 每 scanline 呼叫）
        // ════════════════════════════════════════════════════════════════════
        public static void DecodeScanline(int sl, byte[] palBuf, byte emphasisBits)
        {
            if (sl < 0 || sl >= 240 || NesCore.AnalogScreenBuf3x == null) return;

            if (NesCore.UltraAnalog)
                DecodeScanline_Physical(sl, palBuf, emphasisBits);
            else
                DecodeScanline_Fast(sl, palBuf, emphasisBits);
        }

        // ════════════════════════════════════════════════════════════════════
        // Level 2：快速路徑
        // ════════════════════════════════════════════════════════════════════

        static void DecodeScanline_Fast(int sl, byte[] palBuf, byte emphasisBits)
        {
            GenerateSignal(palBuf, emphasisBits);
            int phase0 = (scanPhaseIdx - 3 + 9) % 9;

            switch (NesCore.AnalogOutput)
            {
                case NesCore.AnalogOutputMode.SVideo: DecodeAV_SVideo(sl);           break;
                case NesCore.AnalogOutputMode.RF:     DecodeAV_RF(sl, phase0);       break;
                default:                              DecodeAV_Composite(sl, phase0); break;
            }
        }

        // 計算每 dot 的 YIQ（256 dots），並前進 scanPhaseIdx
        static void GenerateSignal(byte[] palBuf, byte emphasisBits)
        {
            float atten = 1.0f;
            if (emphasisBits != 0)
            {
                int n = (emphasisBits & 1) + ((emphasisBits >> 1) & 1) + ((emphasisBits >> 2) & 1);
                atten = (float)Math.Pow(0.746, n);
            }

            for (int d = 0; d < 256; d++)
            {
                int   p     = palBuf[d];
                int   luma  = (p >> 4) & 3;
                int   color = p & 0x0F;

                float lo = loLevels[luma];
                float hi = hiLevels[luma];

                if      (color == 0)    lo = hi;
                else if (color == 0x0D) hi = lo;
                else if (color > 0x0D)  lo = hi = 0f;

                lo *= atten;
                hi *= atten;

                float sat = (hi - lo) * 0.5f;
                dotY[d]   = (hi + lo) * 0.5f;

                if (color >= 1 && color <= 12)
                {
                    dotI[d] = iPhase[color] * sat;
                    dotQ[d] = qPhase[color] * sat;
                }
                else
                {
                    dotI[d] = 0f;
                    dotQ[d] = 0f;
                }
            }

            scanPhaseIdx = (scanPhaseIdx + 3) % 9;
        }

        // AV（合成視訊）：水平 IQ 模糊 + 9-entry LUT dot crawl
        static void DecodeAV_Composite(int sl, int phase0)
        {
            for (int outRow = 0; outRow < 3; outRow++)
            {
                int row = sl * 3 + outRow;
                if (row >= kOutH) continue;
                uint* rowPtr = NesCore.AnalogScreenBuf3x + row * kOutW;

                for (int outX = 0; outX < kOutW; outX++)
                {
                    int d  = outX * 256 / kOutW;
                    int ph = (phase0 + outX) % 9;

                    float ii, iq;
                    if (d > 0 && d < 255)
                    {
                        ii = dotI[d-1] * 0.25f + dotI[d] * 0.5f + dotI[d+1] * 0.25f;
                        iq = dotQ[d-1] * 0.25f + dotQ[d] * 0.5f + dotQ[d+1] * 0.25f;
                    }
                    else { ii = dotI[d]; iq = dotQ[d]; }

                    float c      = cosLUT[ph];
                    float s      = sinLUT[ph];
                    float chroma = ii * c - iq * s;
                    float iDec   = chroma * c;
                    float qDec   = -chroma * s;

                    rowPtr[outX] = YiqToRgb(dotY[d], iDec, qDec);
                }
            }
        }

        // S-Video：輕度 IQ 低通（0.1/0.8/0.1），無 dot crawl
        static void DecodeAV_SVideo(int sl)
        {
            for (int outRow = 0; outRow < 3; outRow++)
            {
                int row = sl * 3 + outRow;
                if (row >= kOutH) continue;
                uint* rowPtr = NesCore.AnalogScreenBuf3x + row * kOutW;

                for (int outX = 0; outX < kOutW; outX++)
                {
                    int d = outX * 256 / kOutW;

                    float ii = dotI[d];
                    float iq = dotQ[d];
                    if (d > 0 && d < 255)
                    {
                        ii = dotI[d-1] * 0.1f + dotI[d] * 0.8f + dotI[d+1] * 0.1f;
                        iq = dotQ[d-1] * 0.1f + dotQ[d] * 0.8f + dotQ[d+1] * 0.1f;
                    }

                    rowPtr[outX] = YiqToRgb(dotY[d], ii, iq);
                }
            }
        }

        // RF：AV + AM 白雜訊 + 音訊 buzz bar
        static void DecodeAV_RF(int sl, int phase0)
        {
            float buzzAmp = RfAudioLevel * 0.06f;
            float buzzRow = buzzAmp * (float)Math.Sin((sl / 240.0 + RfBuzzPhase) * 2.0 * Math.PI);
            uint  ns      = (uint)(NesCore.frame_count * 1664525 + sl * 1013904223 + 1442695041);

            for (int outRow = 0; outRow < 3; outRow++)
            {
                int row = sl * 3 + outRow;
                if (row >= kOutH) continue;
                uint* rowPtr = NesCore.AnalogScreenBuf3x + row * kOutW;

                for (int outX = 0; outX < kOutW; outX++)
                {
                    int d  = outX * 256 / kOutW;
                    int ph = (phase0 + outX) % 9;

                    float ii, iq;
                    if (d > 0 && d < 255)
                    {
                        ii = dotI[d-1] * 0.25f + dotI[d] * 0.5f + dotI[d+1] * 0.25f;
                        iq = dotQ[d-1] * 0.25f + dotQ[d] * 0.5f + dotQ[d+1] * 0.25f;
                    }
                    else { ii = dotI[d]; iq = dotQ[d]; }

                    float c      = cosLUT[ph];
                    float s      = sinLUT[ph];
                    float chroma = ii * c - iq * s;
                    float iDec   = chroma * c;
                    float qDec   = -chroma * s;

                    float y = dotY[d] + buzzRow;
                    ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5;
                    y  += ((ns & 0xFF) / 255.0f - 0.5f) * 0.012f;

                    rowPtr[outX] = YiqToRgb(y, iDec, qDec);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Level 3：物理路徑（UltraAnalog）
        // ════════════════════════════════════════════════════════════════════

        static void DecodeScanline_Physical(int sl, byte[] palBuf, byte emphasisBits)
        {
            int phase0 = scanPhaseBase;
            scanPhaseBase = (scanPhaseBase + 2) % 6; // 1364 mod 6 = 2

            switch (NesCore.AnalogOutput)
            {
                case NesCore.AnalogOutputMode.SVideo:
                    DecodePhysical_SVideo(sl, palBuf, emphasisBits);
                    break;
                default:
                    bool isRF = NesCore.AnalogOutput == NesCore.AnalogOutputMode.RF;
                    GenerateWaveform(palBuf, emphasisBits, isRF, sl, phase0);
                    DecodeComposite(sl, phase0);
                    break;
            }
        }

        // Step 1：生成 21.477 MHz 複合視訊波形
        //
        //   composite(t) = Y - sat × cos(2πt/6 - c×π/6)
        //                = Y + cosTab6[t%6] × ip - sinTab6[t%6] × qp
        //
        //   其中 ip = iPhase[c]×sat = -cos(c×π/6)×sat
        //        qp = qPhase[c]×sat =  sin(c×π/6)×sat
        //
        //   驗證（c=2, luma=1, phase0=0）：
        //     6 個連續 samples [0.17,0.00,0.17,0.51,0.68,0.51]
        //     解調後 Y=0.34, I=-0.17, Q=0.295 與 blargg 完全一致
        //
        static void GenerateWaveform(byte[] palBuf, byte emphasisBits,
                                      bool addRfNoise, int sl, int phase0)
        {
            float atten = 1.0f;
            if (emphasisBits != 0)
            {
                int n = (emphasisBits & 1) + ((emphasisBits >> 1) & 1) + ((emphasisBits >> 2) & 1);
                atten = (float)Math.Pow(0.746, n);
            }

            float firstY = 0f, lastY = 0f;
            int tMod = phase0; // 從 phase0 開始，逐 sample 遞增

            for (int d = 0; d < kDots; d++)
            {
                int p     = palBuf[d];
                int luma  = (p >> 4) & 3;
                int color = p & 0xF;

                float lo = loLevels[luma] * atten;
                float hi = hiLevels[luma] * atten;

                if      (color == 0)    lo = hi;
                else if (color == 0x0D) hi = lo;
                else if (color > 0x0D)  lo = hi = 0f;

                float Y   = (hi + lo) * 0.5f;
                float sat = (hi - lo) * 0.5f;

                float ip = (color >= 1 && color <= 12) ? iPhase[color] * sat : 0f;
                float qp = (color >= 1 && color <= 12) ? qPhase[color] * sat : 0f;

                if (d == 0)   firstY = Y;
                if (d == 255) lastY  = Y;

                int baseIdx = kLeadPad + d * kSampDot;
                for (int s = 0; s < kSampDot; s++)
                {
                    waveBuf[baseIdx + s] = Y + cosTab6[tMod] * ip - sinTab6[tMod] * qp;
                    tMod = tMod == 5 ? 0 : tMod + 1;
                }
            }

            // 邊緣填充（DC 延伸，無色度）
            for (int i = 0; i < kLeadPad; i++)               waveBuf[i] = firstY;
            for (int i = kLeadPad + kWaveLen; i < kBufLen; i++) waveBuf[i] = lastY;

            // RF：干擾加入波形後再解調（物理正確）
            if (addRfNoise)
            {
                float buzzAmp = RfAudioLevel * 0.06f;
                float buzzRow = buzzAmp * (float)Math.Sin(
                                    (sl / 240.0 + RfBuzzPhase) * 2.0 * Math.PI);
                uint ns = (uint)(NesCore.frame_count * 1664525 + sl * 1013904223 + 1442695041);

                for (int i = kLeadPad; i < kLeadPad + kWaveLen; i++)
                {
                    ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5;
                    float noise = ((ns & 0xFF) / 255.0f - 0.5f) * 0.012f;
                    waveBuf[i] += buzzRow + noise;
                }
            }
        }

        // Step 2：複合視訊解調
        static unsafe void DecodeComposite(int sl, int phase0)
        {
            uint* row0 = NesCore.AnalogScreenBuf3x + sl * 3 * kOutW;
            DemodulateRow(row0, phase0);

            int row1 = sl * 3 + 1, row2 = sl * 3 + 2;
            if (row1 < kOutH)
                Buffer.MemoryCopy(row0,
                    NesCore.AnalogScreenBuf3x + row1 * kOutW,
                    kOutW * sizeof(uint), kOutW * sizeof(uint));
            if (row2 < kOutH)
                Buffer.MemoryCopy(row0,
                    NesCore.AnalogScreenBuf3x + row2 * kOutW,
                    kOutW * sizeof(uint), kOutW * sizeof(uint));
        }

        // 單行解調（768 output pixels，12-sample boxcar coherent demodulation）
        //
        //   輸出像素 p → 波形中心 center = kLeadPad + p × 1024/768
        //   視窗 [center-6, center+6)，共 12 samples
        //
        //   歸一化 ×2：
        //     mean(composite × cos(ωt)) = -sat/2 × cos(c×π/6)
        //     ×2 → I = -sat × cos(c×π/6) = iPhase[c] × sat  ✓
        //
        static unsafe void DemodulateRow(uint* rowPtr, int phase0)
        {
            for (int p = 0; p < kOutW; p++)
            {
                int center = kLeadPad + p * kWaveLen / kOutW;
                int start  = center - kWinHalf;
                int end    = center + kWinHalf;

                if (start < 0)       start = 0;
                if (end   > kBufLen) end   = kBufLen;

                // 起始副載波相位（處理負數 mod）
                int tMod = ((phase0 + start - kLeadPad) % 6 + 6) % 6;

                float sumY = 0f, sumI = 0f, sumQ = 0f;
                for (int t = start; t < end; t++)
                {
                    float w = waveBuf[t];
                    sumY += w;
                    sumI += w * cosTab6[tMod];
                    sumQ += w * sinTab6[tMod];
                    tMod = tMod == 5 ? 0 : tMod + 1;
                }

                int   N = end - start;
                float Y = sumY / N;
                float I = 2f * sumI / N;
                float Q = -2f * sumQ / N;

                rowPtr[p] = YiqToRgb(Y, I, Q);
            }
        }

        // S-Video 物理路徑：直接 YIQ（Y/C 分離傳輸，無複合信號路徑）
        static unsafe void DecodePhysical_SVideo(int sl, byte[] palBuf, byte emphasisBits)
        {
            float atten = 1.0f;
            if (emphasisBits != 0)
            {
                int n = (emphasisBits & 1) + ((emphasisBits >> 1) & 1) + ((emphasisBits >> 2) & 1);
                atten = (float)Math.Pow(0.746, n);
            }

            uint* row0 = NesCore.AnalogScreenBuf3x + sl * 3 * kOutW;
            for (int p = 0; p < kOutW; p++)
            {
                int d = p * kDots / kOutW;
                int pal   = palBuf[d];
                int luma  = (pal >> 4) & 3;
                int color = pal & 0xF;

                float lo = loLevels[luma] * atten;
                float hi = hiLevels[luma] * atten;

                if      (color == 0)    lo = hi;
                else if (color == 0x0D) hi = lo;
                else if (color > 0x0D)  lo = hi = 0f;

                float Y = (hi + lo) * 0.5f;
                float I = 0f, Q = 0f;
                if (color >= 1 && color <= 12)
                {
                    float sat = (hi - lo) * 0.5f;
                    I = iPhase[color] * sat;
                    Q = qPhase[color] * sat;
                }
                row0[p] = YiqToRgb(Y, I, Q);
            }

            int row1 = sl * 3 + 1, row2 = sl * 3 + 2;
            if (row1 < kOutH)
                Buffer.MemoryCopy(row0,
                    NesCore.AnalogScreenBuf3x + row1 * kOutW,
                    kOutW * sizeof(uint), kOutW * sizeof(uint));
            if (row2 < kOutH)
                Buffer.MemoryCopy(row0,
                    NesCore.AnalogScreenBuf3x + row2 * kOutW,
                    kOutW * sizeof(uint), kOutW * sizeof(uint));
        }

        // ════════════════════════════════════════════════════════════════════
        // 共用：YIQ → RGB（blargg -15° 解碼矩陣 + fast gamma）
        // ════════════════════════════════════════════════════════════════════
        //
        //   R = Y + 1.0841·I + 0.3523·Q
        //   G = Y − 0.4302·I − 0.5547·Q
        //   B = Y − 0.6268·I + 1.9299·Q
        //
        //   fast gamma：v' = v + 0.229·v·(v−1)  ≈ pow(v, 1/1.1333)
        //
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint YiqToRgb(float y, float i, float q)
        {
            float r = y + 1.0841f * i + 0.3523f * q;
            float g = y - 0.4302f * i - 0.5547f * q;
            float b = y - 0.6268f * i + 1.9299f * q;

            const float gf = 0.229f;
            r += gf * r * (r - 1f);
            g += gf * g * (g - 1f);
            b += gf * b * (b - 1f);

            int ri = Clamp255((int)(r * 255.5f));
            int gi = Clamp255((int)(g * 255.5f));
            int bi = Clamp255((int)(b * 255.5f));
            return (uint)(bi | (gi << 8) | (ri << 16) | 0xFF000000u);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Clamp255(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
    }
}
