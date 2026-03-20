using System;
using System.Runtime.CompilerServices;

namespace AprNes
{
    // ============================================================
    // NES NTSC 訊號解碼器（Stage 1）
    // ============================================================
    //
    //  輸出：float[] linearBuffer [768 × 240 × 3]  ← 線性 RGB，無 Gamma
    //  Stage 2 (CrtScreen) 負責垂直擴展 + 高斯掃描線 + Bloom + Gamma → 768×630
    //
    //  Level 2（預設，快速）：直接 YIQ + 9-entry LUT dot crawl
    //  Level 3（UltraAnalog，精確）：21.477 MHz 時域波形 + coherent demodulation
    //
    //  三種端子參數組（由 NesCore.AnalogOutput 決定）：
    //    RF     : NoiseIntensity=0.20, SlewRate=0.30, ChromaBlur=0.05
    //    AV     : NoiseIntensity=0.02, SlewRate=0.65, ChromaBlur=0.20
    //    SVideo : NoiseIntensity=0.00, SlewRate=0.90, ChromaBlur=0.45
    //
    // ── Level 2 路徑 ─────────────────────────────────────────────
    //   · 直接從電壓準位計算 Y、I、Q（blargg 公式）
    //   · SlewRate IIR 模擬電容遲滯（作用在 Y）
    //   · ChromaBlur IIR 模擬色彩暈染（取代固定 FIR）
    //   · NoiseIntensity 熱雜訊
    //   · 9-entry LUT dot crawl（AV/RF）
    //
    // ── Level 3 路徑（UltraAnalog）──────────────────────────────
    //   Step 1  生成 21.477 MHz 複合視訊波形（4 samples/dot）
    //           + SlewRate IIR（電容遲滯作用在波形）
    //           + NoiseIntensity 熱雜訊疊加
    //   Step 2  12-sample boxcar coherent demodulation
    //           + ChromaBlur IIR（作用在解調後 I/Q）
    //   Step 3  YIQ → Linear RGB（blargg -15° 矩陣，無 Gamma）
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

        // ── 輸出：線性 RGB 緩衝區（768×240×3，Stage 2 輸入）────────────────
        public const int kOutW = 1024;
        public const int kSrcH = 240;
        public static readonly float[] linearBuffer = new float[kOutW * kSrcH * 3];

        // ── 端子參數組（INI 讀入，開機時載入一次）──────────────────────────
        // RF 端子
        public static float RF_NoiseIntensity = 0.04f;
        public static float RF_SlewRate       = 0.60f;
        public static float RF_ChromaBlur     = 0.10f;
        // AV 端子
        public static float AV_NoiseIntensity = 0.003f;
        public static float AV_SlewRate       = 0.80f;
        public static float AV_ChromaBlur     = 0.35f;
        // S-Video 端子
        public static float SV_NoiseIntensity = 0.00f;
        public static float SV_SlewRate       = 0.90f;
        public static float SV_ChromaBlur     = 0.45f;

        // 每 scanline 使用中的參數（由 ApplyProfile 設定）
        static float NoiseIntensity;
        static float SlewRate;
        static float ChromaBlur;

        static void ApplyProfile()
        {
            switch (NesCore.AnalogOutput)
            {
                case NesCore.AnalogOutputMode.RF:
                    NoiseIntensity = RF_NoiseIntensity; SlewRate = RF_SlewRate; ChromaBlur = RF_ChromaBlur; break;
                case NesCore.AnalogOutputMode.SVideo:
                    NoiseIntensity = SV_NoiseIntensity; SlewRate = SV_SlewRate; ChromaBlur = SV_ChromaBlur; break;
                default: // AV
                    NoiseIntensity = AV_NoiseIntensity; SlewRate = AV_SlewRate; ChromaBlur = AV_ChromaBlur; break;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Level 2 — 簡化路徑（快速）
        // ════════════════════════════════════════════════════════════════════

        // 每 dot 的 YIQ（256 dots/scanline）
        static readonly float[] dotY = new float[256];
        static readonly float[] dotI = new float[256];
        static readonly float[] dotQ = new float[256];

        // Level 2 副載波相位（6-entry，共用 cosTab6/sinTab6）
        // ─ 物理根據：副載波 / PPU 像素時脈 = (master/6)/(master/4) = 2/3 cycle/dot
        //             = 240°/dot，4 output pixels/dot → 60°/pixel = 1 step（6-entry）
        // ─ 每 scanline 推進 2 steps（1364 dots × 240° mod 360° = 120° = 2 steps）
        static int scanPhase6 = 0;

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

        // 解調視窗（12 samples = 2 副載波週期）
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

            // Level 2 / Level 3 共用：6-entry 副載波 LUT（每格 60° = π/3）
            for (int k = 0; k < 6; k++)
            {
                double a = k * 2.0 * Math.PI / 6.0;
                cosTab6[k] = (float)Math.Cos(a);
                sinTab6[k] = (float)Math.Sin(a);
            }

            scanPhase6    = 0;
            scanPhaseBase = 0;
            RfAudioLevel  = 0f;
            RfBuzzPhase   = 0f;
        }

        // ════════════════════════════════════════════════════════════════════
        // 主進入點（由 PPU 每 scanline 呼叫）
        // ════════════════════════════════════════════════════════════════════
        public static void DecodeScanline(int sl, byte[] palBuf, byte emphasisBits)
        {
            if (sl < 0 || sl >= kSrcH) return;

            // 每幀第一行時更新端子參數組
            if (sl == 0) ApplyProfile();

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
            int phase0 = scanPhase6;               // 本行起始相位（6-entry index）
            scanPhase6 = (scanPhase6 + 2) % 6;     // 推進 120°（= 2 steps）供下一 scanline
            GenerateSignal(palBuf, emphasisBits);

            if (NesCore.AnalogOutput == NesCore.AnalogOutputMode.SVideo)
                DecodeAV_SVideo(sl);
            else
                DecodeAV_Composite(sl, phase0);
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

        }

        // AV / RF：6-entry LUT dot crawl（60°/pixel，物理正確） + ChromaBlur IIR + SlewRate IIR + Noise
        // 直接寫入 AnalogScreenBuf3x（跳過 linearBuffer / CrtScreen）
        static unsafe void DecodeAV_Composite(int sl, int phase0)
        {
            bool isRF     = NesCore.AnalogOutput == NesCore.AnalogOutputMode.RF;
            bool addNoise = NoiseIntensity > 0f;

            float buzzRow = 0f;
            if (isRF)
            {
                float buzzAmp = RfAudioLevel * 0.06f;
                buzzRow = buzzAmp * (float)Math.Sin((sl / 240.0 + RfBuzzPhase) * 2.0 * Math.PI);
            }

            uint ns = 0u;
            if (addNoise || isRF)
                ns = (uint)(NesCore.frame_count * 1664525u + (uint)sl * 1013904223u + 1442695041u);

            // IIR 初始狀態（以第 0 dot 的解碼值初始化，避免啟動暫態）
            int   d0   = 0;
            int   ph0  = phase0 % 6;
            float c0   = cosTab6[ph0], s0 = sinTab6[ph0];
            float chr0 = dotI[d0] * c0 - dotQ[d0] * s0;
            float iFilt = chr0 * c0;
            float qFilt = -chr0 * s0;
            float yFilt = dotY[d0];

            // 計算此 scanline 對應的輸出行範圍（840/240 = 3.5，交替 3/4 行）
            int rowStart = sl * CrtScreen.DstH / CrtScreen.SrcH;
            int rowEnd   = (sl + 1) * CrtScreen.DstH / CrtScreen.SrcH;
            if (rowEnd > CrtScreen.DstH) rowEnd = CrtScreen.DstH;

            uint* row0 = NesCore.AnalogScreenBuf3x + rowStart * kOutW;

            for (int outX = 0; outX < kOutW; outX++)
            {
                int d  = outX * 256 / kOutW;
                int ph = (phase0 + outX) % 6;

                float c      = cosTab6[ph];
                float s      = sinTab6[ph];
                float chroma = dotI[d] * c - dotQ[d] * s;
                float iRaw   = chroma * c;
                float qRaw   = -chroma * s;

                iFilt += ChromaBlur * (iRaw - iFilt);
                qFilt += ChromaBlur * (qRaw - qFilt);
                yFilt += SlewRate   * (dotY[d] - yFilt);
                float y = yFilt;

                if (isRF) y += buzzRow;

                if (addNoise)
                {
                    ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5;
                    y += ((ns & 0xFF) / 255.0f - 0.5f) * 2f * NoiseIntensity;
                }

                row0[outX] = YiqToRgb(y, iFilt, qFilt);
            }

            // 複製到相同 scanline 的其餘輸出行
            for (int row = rowStart + 1; row < rowEnd; row++)
                Buffer.MemoryCopy(row0,
                    NesCore.AnalogScreenBuf3x + row * kOutW,
                    kOutW * sizeof(uint), kOutW * sizeof(uint));
        }

        // S-Video：無 dot crawl，直接寫入 AnalogScreenBuf3x
        static unsafe void DecodeAV_SVideo(int sl)
        {
            float iFilt = dotI[0];
            float qFilt = dotQ[0];
            float yFilt = dotY[0];

            int rowStart = sl * CrtScreen.DstH / CrtScreen.SrcH;
            int rowEnd   = (sl + 1) * CrtScreen.DstH / CrtScreen.SrcH;
            if (rowEnd > CrtScreen.DstH) rowEnd = CrtScreen.DstH;

            uint* row0 = NesCore.AnalogScreenBuf3x + rowStart * kOutW;

            for (int outX = 0; outX < kOutW; outX++)
            {
                int d = outX * 256 / kOutW;

                iFilt += ChromaBlur * (dotI[d] - iFilt);
                qFilt += ChromaBlur * (dotQ[d] - qFilt);
                yFilt += SlewRate   * (dotY[d] - yFilt);

                row0[outX] = YiqToRgb(yFilt, iFilt, qFilt);
            }

            for (int row = rowStart + 1; row < rowEnd; row++)
                Buffer.MemoryCopy(row0,
                    NesCore.AnalogScreenBuf3x + row * kOutW,
                    kOutW * sizeof(uint), kOutW * sizeof(uint));
        }

        // ════════════════════════════════════════════════════════════════════
        // Level 3：物理路徑（UltraAnalog）
        // ════════════════════════════════════════════════════════════════════

        static void DecodeScanline_Physical(int sl, byte[] palBuf, byte emphasisBits)
        {
            int phase0 = scanPhaseBase;
            scanPhaseBase = (scanPhaseBase + 2) % 6; // 1364 mod 6 = 2

            if (NesCore.AnalogOutput == NesCore.AnalogOutputMode.SVideo)
                DecodePhysical_SVideo(sl, palBuf, emphasisBits);
            else
            {
                bool isRF = NesCore.AnalogOutput == NesCore.AnalogOutputMode.RF;
                GenerateWaveform(palBuf, emphasisBits, isRF, sl, phase0);
                DemodulateRow(sl, phase0);
            }
        }

        // Step 1：生成 21.477 MHz 複合視訊波形 + SlewRate IIR + Noise
        static void GenerateWaveform(byte[] palBuf, byte emphasisBits,
                                      bool isRF, int sl, int phase0)
        {
            float atten = 1.0f;
            if (emphasisBits != 0)
            {
                int n = (emphasisBits & 1) + ((emphasisBits >> 1) & 1) + ((emphasisBits >> 2) & 1);
                atten = (float)Math.Pow(0.746, n);
            }

            float firstY = 0f, lastY = 0f;
            int tMod = phase0;

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
            for (int i = 0; i < kLeadPad; i++)
                waveBuf[i] = firstY;
            for (int i = kLeadPad + kWaveLen; i < kBufLen; i++)
                waveBuf[i] = lastY;

            // RF buzz（音訊干擾，加入波形後解調）
            if (isRF)
            {
                float buzzAmp = RfAudioLevel * 0.06f;
                float buzzRow = buzzAmp * (float)Math.Sin(
                                    (sl / 240.0 + RfBuzzPhase) * 2.0 * Math.PI);
                for (int i = kLeadPad; i < kLeadPad + kWaveLen; i++)
                    waveBuf[i] += buzzRow;
            }

            // 熱雜訊（由 NoiseIntensity 控制）
            if (NoiseIntensity > 0f)
            {
                uint ns = (uint)(NesCore.frame_count * 1664525u + (uint)sl * 1013904223u + 1442695041u);
                float amp = 2f * NoiseIntensity;
                for (int i = kLeadPad; i < kLeadPad + kWaveLen; i++)
                {
                    ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5;
                    waveBuf[i] += ((ns & 0xFF) / 255.0f - 0.5f) * amp;
                }
            }

            // SlewRate IIR（電容遲滯，作用在波形上）
            if (SlewRate < 1.0f)
            {
                float vPrev = waveBuf[0];
                for (int i = 1; i < kBufLen; i++)
                {
                    vPrev += SlewRate * (waveBuf[i] - vPrev);
                    waveBuf[i] = vPrev;
                }
            }
        }

        // Step 2：複合視訊解調 + ChromaBlur IIR
        //   CrtEnabled=true  → 寫入 linearBuffer（供 Stage 2 CrtScreen 使用）
        //   CrtEnabled=false → 直接 YIQ→BGRA 寫入 AnalogScreenBuf3x
        static unsafe void DemodulateRow(int sl, int phase0)
        {
            bool toCrt = NesCore.CrtEnabled;
            int rowOff = sl * kOutW * 3;  // linearBuffer 偏移（toCrt=true 時使用）

            // 直寫路徑：計算此 scanline 對應的輸出行範圍
            int rowStart = sl * CrtScreen.DstH / CrtScreen.SrcH;
            int rowEnd   = (sl + 1) * CrtScreen.DstH / CrtScreen.SrcH;
            if (rowEnd > CrtScreen.DstH) rowEnd = CrtScreen.DstH;
            uint* row0 = NesCore.AnalogScreenBuf3x + rowStart * kOutW;

            float iFilt = 0f, qFilt = 0f;
            bool first = true;

            for (int p = 0; p < kOutW; p++)
            {
                int center = kLeadPad + p * kWaveLen / kOutW;
                int start  = center - kWinHalf;
                int end    = center + kWinHalf;

                if (start < 0)       start = 0;
                if (end   > kBufLen) end   = kBufLen;

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

                // ChromaBlur IIR（解調後 I/Q）
                if (first) { iFilt = I; qFilt = Q; first = false; }
                else
                {
                    iFilt += ChromaBlur * (I - iFilt);
                    qFilt += ChromaBlur * (Q - qFilt);
                }

                if (toCrt)
                    WriteLinear(rowOff + p * 3, Y, iFilt, qFilt);
                else
                    row0[p] = YiqToRgb(Y, iFilt, qFilt);
            }

            // 直寫路徑：複製到此 scanline 的其餘輸出行
            if (!toCrt)
                for (int row = rowStart + 1; row < rowEnd; row++)
                    Buffer.MemoryCopy(row0,
                        NesCore.AnalogScreenBuf3x + row * kOutW,
                        kOutW * sizeof(uint), kOutW * sizeof(uint));
        }

        // S-Video 物理路徑：直接 YIQ + SlewRate IIR(Y) + ChromaBlur IIR(I/Q)
        //   CrtEnabled=true  → 寫入 linearBuffer
        //   CrtEnabled=false → 直接 YIQ→BGRA 寫入 AnalogScreenBuf3x
        static unsafe void DecodePhysical_SVideo(int sl, byte[] palBuf, byte emphasisBits)
        {
            float atten = 1.0f;
            if (emphasisBits != 0)
            {
                int n = (emphasisBits & 1) + ((emphasisBits >> 1) & 1) + ((emphasisBits >> 2) & 1);
                atten = (float)Math.Pow(0.746, n);
            }

            bool toCrt = NesCore.CrtEnabled;
            int rowOff = sl * kOutW * 3;

            int rowStart = sl * CrtScreen.DstH / CrtScreen.SrcH;
            int rowEnd   = (sl + 1) * CrtScreen.DstH / CrtScreen.SrcH;
            if (rowEnd > CrtScreen.DstH) rowEnd = CrtScreen.DstH;
            uint* row0 = NesCore.AnalogScreenBuf3x + rowStart * kOutW;

            float yFilt = 0f, iFilt = 0f, qFilt = 0f;
            bool first = true;

            for (int p = 0; p < kOutW; p++)
            {
                int d     = p * kDots / kOutW;
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

                if (first) { yFilt = Y; iFilt = I; qFilt = Q; first = false; }
                else
                {
                    yFilt += SlewRate * (Y - yFilt);
                    iFilt += ChromaBlur * (I - iFilt);
                    qFilt += ChromaBlur * (Q - qFilt);
                }

                if (toCrt)
                    WriteLinear(rowOff + p * 3, yFilt, iFilt, qFilt);
                else
                    row0[p] = YiqToRgb(yFilt, iFilt, qFilt);
            }

            if (!toCrt)
                for (int row = rowStart + 1; row < rowEnd; row++)
                    Buffer.MemoryCopy(row0,
                        NesCore.AnalogScreenBuf3x + row * kOutW,
                        kOutW * sizeof(uint), kOutW * sizeof(uint));
        }

        // ════════════════════════════════════════════════════════════════════
        // 共用：YIQ → Linear RGB（blargg -15° 矩陣，無 Gamma）
        // ════════════════════════════════════════════════════════════════════
        //
        //   R = Y + 1.0841·I + 0.3523·Q
        //   G = Y − 0.4302·I − 0.5547·Q
        //   B = Y − 0.6268·I + 1.9299·Q
        //
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void WriteLinear(int off, float y, float i, float q)
        {
            float r = y + 1.0841f * i + 0.3523f * q;
            float g = y - 0.4302f * i - 0.5547f * q;
            float b = y - 0.6268f * i + 1.9299f * q;

            if (r < 0f) r = 0f; else if (r > 1f) r = 1f;
            if (g < 0f) g = 0f; else if (g > 1f) g = 1f;
            if (b < 0f) b = 0f; else if (b > 1f) b = 1f;

            linearBuffer[off]     = r;
            linearBuffer[off + 1] = g;
            linearBuffer[off + 2] = b;
        }

        // ════════════════════════════════════════════════════════════════════
        // Level 2 直接輸出：YIQ → BGRA uint（Fast Gamma ≈ pow(v,1/1.13)）
        // ════════════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint YiqToRgb(float y, float i, float q)
        {
            float r = y + 1.0841f * i + 0.3523f * q;
            float g = y - 0.4302f * i - 0.5547f * q;
            float b = y - 0.6268f * i + 1.9299f * q;

            if (r < 0f) r = 0f; else if (r > 1f) r = 1f;
            if (g < 0f) g = 0f; else if (g > 1f) g = 1f;
            if (b < 0f) b = 0f; else if (b > 1f) b = 1f;

            const float gf = 0.229f;
            r += gf * r * (r - 1f);
            g += gf * g * (g - 1f);
            b += gf * b * (b - 1f);

            int ri = (int)(r * 255.5f);
            int gi = (int)(g * 255.5f);
            int bi = (int)(b * 255.5f);
            if (ri > 255) ri = 255;
            if (gi > 255) gi = 255;
            if (bi > 255) bi = 255;

            return (uint)(bi | (gi << 8) | (ri << 16) | 0xFF000000u);
        }
    }
}
