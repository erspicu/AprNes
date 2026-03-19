using System;
using System.Runtime.CompilerServices;

namespace AprNes
{
    // ============================================================
    // NES NTSC 類比訊號模擬器 (Level 2)
    // ============================================================
    //
    // 色彩生成：依照 blargg nes_ntsc 0.2.2 直接 YIQ 公式
    //   Y   = (hi + lo) / 2
    //   sat = (hi - lo) / 2
    //   I   = -cos(color × π/6) × sat   (blargg TO_ANGLE_SIN)
    //   Q   =  sin(color × π/6) × sat   (blargg TO_ANGLE_COS)
    //
    // 電壓準位 (blargg nes_ntsc.inl):
    //   luma 0: lo=-0.12, hi=0.40
    //   luma 1: lo= 0.00, hi=0.68
    //   luma 2: lo= 0.31, hi=1.00
    //   luma 3: lo= 0.72, hi=1.00
    //
    // YIQ→RGB（含 -15° 色相旋轉，blargg std_decoder_hue=-15）：
    //   R = Y + 1.0841·I + 0.3523·Q
    //   G = Y − 0.4302·I − 0.5547·Q
    //   B = Y − 0.6268·I + 1.9299·Q
    //
    // 相位追蹤優化：
    //   每輸出像素相位增量 = 4π/9（≈80°）
    //   9 像素完整 2 個色副載波週期 → 使用整數 LUT[9]
    //   每 scanline 相位前進 3 步（256 dot × 4MC × 3/4 → 768 % 9 = 3）
    //   → 每 3 scanlines 相位歸零，形成 3-line dot crawl 週期
    //
    // 輸出：768×720 直接寫入 NesCore.AnalogScreenBuf3x (BGRA)
    // ============================================================

    unsafe public static class Ntsc
    {
        // ── blargg 電壓準位 ─────────────────────────────────────────────────
        static readonly float[] loLevels = { -0.12f, 0.00f, 0.31f, 0.72f };
        static readonly float[] hiLevels = {  0.40f, 0.68f, 1.00f, 1.00f };

        // ── 色相相位查表（blargg 公式）──────────────────────────────────────
        // iPhase[c] = -cos(c × π/6)  for c = 0..15
        // qPhase[c] =  sin(c × π/6)  for c = 0..15
        static readonly float[] iPhase = new float[16];
        static readonly float[] qPhase = new float[16];

        // ── 色副載波 LUT（9-entry，對應每輸出像素增量 4π/9）────────────────
        // cosLUT[k] = cos(k × 4π/9),  sinLUT[k] = sin(k × 4π/9)
        // k = 0..8；9 像素完成 2 個完整週期
        static readonly float[] cosLUT = new float[9];
        static readonly float[] sinLUT = new float[9];

        // ── 每 dot YIQ 緩衝區（256 dot/scanline）────────────────────────────
        static readonly float[] dotY = new float[256];
        static readonly float[] dotI = new float[256];
        static readonly float[] dotQ = new float[256];

        // ── 輸出尺寸 ────────────────────────────────────────────────────────
        const int kOutWidth  = 768;
        const int kOutHeight = 720;

        // ── 掃描線相位索引（0-8 整數）────────────────────────────────────────
        // 每 scanline 前進 3（768 % 9 = 3），每 3 scanlines 歸零
        static int scanPhaseIdx = 0;

        // ── RF 音訊干擾電平 ──────────────────────────────────────────────────
        // RfAudioLevel：|audio| 指數平滑值，控制 buzz bar 振幅（由 APU 更新）
        // RfBuzzPhase ：累積音量 0..1，控制 buzz bar 垂直滾動位置（由 APU 更新）
        static public float RfAudioLevel = 0.0f;
        static public float RfBuzzPhase  = 0.0f;

        // ── 初始化 ───────────────────────────────────────────────────────────
        public static void Init()
        {
            // 色相相位查表
            for (int c = 0; c < 16; c++)
            {
                double a = c * Math.PI / 6.0;
                iPhase[c] = -(float)Math.Cos(a);
                qPhase[c] =  (float)Math.Sin(a);
            }

            // 色副載波 9-entry LUT（每格 4π/9 ≈ 80°）
            for (int k = 0; k < 9; k++)
            {
                double a = k * 4.0 * Math.PI / 9.0;
                cosLUT[k] = (float)Math.Cos(a);
                sinLUT[k] = (float)Math.Sin(a);
            }

            scanPhaseIdx = 0;
            RfAudioLevel = 0f;
            RfBuzzPhase  = 0f;
        }

        // ── 訊號生成（blargg 直接 YIQ 公式）────────────────────────────────
        static void GenerateSignal(byte[] palBuf, byte emphasisBits)
        {
            // 色強調衰減（blargg 實測值 0.746/bit）
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

                // blargg 特殊情況（nes_ntsc.inl 原文邏輯）
                if      (color == 0)    lo = hi;
                else if (color == 0x0D) hi = lo;
                else if (color > 0x0D)  lo = hi = 0f;

                lo *= atten;
                hi *= atten;

                float sat  = (hi - lo) * 0.5f;
                dotY[d]    = (hi + lo) * 0.5f;

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

            // 更新相位索引（每 scanline +3，週期 9）
            scanPhaseIdx = (scanPhaseIdx + 3) % 9;
        }

        // ── 主解碼進入點 ─────────────────────────────────────────────────────
        public static void DecodeScanline(int sl, byte[] palBuf, byte emphasisBits)
        {
            if (sl < 0 || sl >= 240 || NesCore.AnalogScreenBuf3x == null) return;

            GenerateSignal(palBuf, emphasisBits);

            // 此 scanline 開始時的相位索引（GenerateSignal 已 +3）
            int phase0 = (scanPhaseIdx - 3 + 9) % 9;

            switch (NesCore.AnalogOutput)
            {
                case NesCore.AnalogOutputMode.SVideo: DecodeSVideo(sl); break;
                case NesCore.AnalogOutputMode.RF:     DecodeRF(sl, phase0); break;
                default:                              DecodeAV(sl, phase0); break;
            }
        }

        // ── AV（合成視訊）解碼 ─────────────────────────────────────────────
        // 色彩暈染：水平 IQ 模糊
        // Dot crawl：利用 9-entry LUT 對每輸出像素做相位調變
        static void DecodeAV(int sl, int phase0)
        {
            for (int outRow = 0; outRow < 3; outRow++)
            {
                int row = sl * 3 + outRow;
                if (row >= kOutHeight) continue;
                uint* rowPtr = NesCore.AnalogScreenBuf3x + row * kOutWidth;

                for (int outX = 0; outX < kOutWidth; outX++)
                {
                    int d  = outX * 256 / kOutWidth;
                    int ph = (phase0 + outX) % 9;

                    // 水平 IQ 模糊（色彩暈染）
                    float ii, iq;
                    if (d > 0 && d < 255)
                    {
                        ii = dotI[d-1] * 0.25f + dotI[d] * 0.5f + dotI[d+1] * 0.25f;
                        iq = dotQ[d-1] * 0.25f + dotQ[d] * 0.5f + dotQ[d+1] * 0.25f;
                    }
                    else
                    {
                        ii = dotI[d]; iq = dotQ[d];
                    }

                    // 合成編碼 → 即時解調（dot crawl 效果）
                    float c = cosLUT[ph];
                    float s = sinLUT[ph];
                    float chroma = ii * c - iq * s;
                    float iDec   = chroma * c;
                    float qDec   = -chroma * s;

                    rowPtr[outX] = YiqToRgb(dotY[d], iDec, qDec);
                }
            }
        }

        // ── S-Video 解碼 ──────────────────────────────────────────────────────
        // Y/C 已分離，無 dot crawl，輕度 IQ 低通
        static void DecodeSVideo(int sl)
        {
            for (int outRow = 0; outRow < 3; outRow++)
            {
                int row = sl * 3 + outRow;
                if (row >= kOutHeight) continue;
                uint* rowPtr = NesCore.AnalogScreenBuf3x + row * kOutWidth;

                for (int outX = 0; outX < kOutWidth; outX++)
                {
                    int d = outX * 256 / kOutWidth;

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

        // ── RF 解碼 ───────────────────────────────────────────────────────────
        // AV + AM 白雜訊 + 音訊 buzz bar
        static void DecodeRF(int sl, int phase0)
        {
            // buzz bar：振幅來自實際 NES 音訊電平，垂直位置隨音量累積滾動
            // sin 引數：sl/240 = 0..1（掃描線位置），+ RfBuzzPhase = bar 滾動偏移
            // × 2π = 1 個完整橫條貫穿全畫面
            float buzzAmp = RfAudioLevel * 0.06f;
            float buzzRow = buzzAmp * (float)Math.Sin((sl / 240.0 + RfBuzzPhase) * 2.0 * Math.PI);
            uint  ns      = (uint)(NesCore.frame_count * 1664525 + sl * 1013904223 + 1442695041);

            for (int outRow = 0; outRow < 3; outRow++)
            {
                int row = sl * 3 + outRow;
                if (row >= kOutHeight) continue;
                uint* rowPtr = NesCore.AnalogScreenBuf3x + row * kOutWidth;

                for (int outX = 0; outX < kOutWidth; outX++)
                {
                    int d  = outX * 256 / kOutWidth;
                    int ph = (phase0 + outX) % 9;

                    float ii, iq;
                    if (d > 0 && d < 255)
                    {
                        ii = dotI[d-1] * 0.25f + dotI[d] * 0.5f + dotI[d+1] * 0.25f;
                        iq = dotQ[d-1] * 0.25f + dotQ[d] * 0.5f + dotQ[d+1] * 0.25f;
                    }
                    else { ii = dotI[d]; iq = dotQ[d]; }

                    float c = cosLUT[ph];
                    float s = sinLUT[ph];
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

        // ── YIQ → RGB（blargg -15° 解碼器 + 快速 Gamma 校正）───────────────
        //
        // 解碼矩陣（default_decoder 旋轉 -15°，blargg std_decoder_hue=-15）：
        //   R = Y + 1.0841·I + 0.3523·Q
        //   G = Y − 0.4302·I − 0.5547·Q
        //   B = Y − 0.6268·I + 1.9299·Q
        //
        // 快速 Gamma（blargg CRT→PC 校正，factor ≈ 0.229）：
        //   v' = v + 0.229·v·(v−1)   ≈ pow(v, 1.1333)
        //
        // 輸出：GDI DIB BGRA（B=低位元組，A=0xFF）
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint YiqToRgb(float y, float i, float q)
        {
            float r = y + 1.0841f * i + 0.3523f * q;
            float g = y - 0.4302f * i - 0.5547f * q;
            float b = y - 0.6268f * i + 1.9299f * q;

            // 快速 Gamma（CRT→PC）
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
