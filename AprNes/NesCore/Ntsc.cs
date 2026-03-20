using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
    //   Step 2  Y/I/Q 分離帶限解調（Hann 視窗）：
    //           Y = kWinY=6（≈4.2 MHz），I = kWinI=18（≈1.3 MHz），Q = kWinQ=54（≈0.4 MHz）
    //           RF/AV：全部從 waveBuf（混合訊號）解調
    //           SVideo：Y 從 waveBuf（純亮度），I/Q 從 cBuf（純 C 線，無亮度串擾）
    //   Step 3  YIQ → Linear RGB（blargg -15° 矩陣，無 Gamma）
    // ============================================================

    unsafe public static class Ntsc
    {
        // ── 共用：blargg 電壓準位 ────────────────────────────────────────────
        static float* loLevels;
        static float* hiLevels;

        // ── 共用：色相查表 ───────────────────────────────────────────────────
        //   iPhase[c] = -cos(c × π/6),  qPhase[c] = sin(c × π/6)
        static float* iPhase;
        static float* qPhase;

        // ── 共用：RF 音訊干擾（由 APU.generateSample() 更新）────────────────
        static public float RfAudioLevel = 0.0f;
        static public float RfBuzzPhase  = 0.0f;

        // ── 輸出：線性 RGB 緩衝區（768×240×3，Stage 2 輸入）────────────────
        public const int kOutW  = 1024;
        public const int kSrcH  = 240;
        public const int kPlane = kOutW * kSrcH; // 245,760 floats per colour plane（R/G/B 各一平面）
        public static float* linearBuffer;        // planar：[R plane][G plane][B plane]

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
        static float* dotY;
        static float* dotI;
        static float* dotQ;

        // Level 2 副載波相位（6-entry，共用 cosTab6/sinTab6）
        // ─ 物理根據：副載波 / PPU 像素時脈 = (master/6)/(master/4) = 2/3 cycle/dot
        //             = 240°/dot，4 output pixels/dot → 60°/pixel = 1 step（6-entry）
        // ─ 每 scanline 推進 2 steps（1364 dots × 240° mod 360° = 120° = 2 steps）
        static int scanPhase6 = 0;

        // ════════════════════════════════════════════════════════════════════
        // Level 3 — 物理路徑（UltraAnalog）
        // ════════════════════════════════════════════════════════════════════

        // 副載波 LUT（週期 6 master clocks = 3.579545 MHz）
        static float* cosTab6;
        static float* sinTab6;

        // 波形緩衝區：[kLeadPad] [256×4 = 1024 samples] [kLeadPad]
        const int kDots    = 256;
        const int kSampDot = 4;
        const int kWaveLen = kDots * kSampDot;        // 1024
        const int kLeadPad = 30;                       // ≥ kWinQ_half=27
        const int kBufLen  = kLeadPad * 2 + kWaveLen; // 1084
        static float* waveBuf; // composite / S-Video Y line
        static float* cBuf;   // S-Video C line（副載波彩度）

        // Y/I/Q 分離解調視窗（Hann，物理帶限）
        //   Y ≈ 4.2 MHz → 6 samples（1 副載波週期，精確消色度）
        //   I ≈ 1.3 MHz → 18 samples（3 副載波週期）
        //   Q ≈ 0.4 MHz → 54 samples（9 副載波週期）
        const int kWinY      = 6;  const int kWinY_half = kWinY / 2;
        const int kWinI      = 18; const int kWinI_half = kWinI / 2;
        const int kWinQ      = 54; const int kWinQ_half = kWinQ / 2;
        static float* hannY;
        static float* hannI;
        static float* hannQ;

        // 預計算合併 I/Q 權重（6 相位 × kWinI/kWinQ）
        // combinedI[ph * kWinI + n] = hannI[n] * cosTab6[(ph + n) % 6]
        // combinedQ[ph * kWinQ + n] = hannQ[n] * sinTab6[(ph + n) % 6]
        static float* combinedI;
        static float* combinedQ;

        // 掃描線副載波相位（每 scanline 前進 1364 mod 6 = 2，週期 3 行）
        static int scanPhaseBase = 0;

        // ════════════════════════════════════════════════════════════════════
        // Init
        // ════════════════════════════════════════════════════════════════════
        public static void Init()
        {
            // 首次呼叫時配置 unmanaged 記憶體（hard reset 後不重配）
            if (loLevels == null)
            {
                loLevels = (float*)Marshal.AllocHGlobal(4 * sizeof(float));
                loLevels[0] = -0.12f; loLevels[1] = 0.00f; loLevels[2] = 0.31f; loLevels[3] = 0.72f;
                hiLevels = (float*)Marshal.AllocHGlobal(4 * sizeof(float));
                hiLevels[0] = 0.40f; hiLevels[1] = 0.68f; hiLevels[2] = 1.00f; hiLevels[3] = 1.00f;

                iPhase       = (float*)Marshal.AllocHGlobal(16 * sizeof(float));
                qPhase       = (float*)Marshal.AllocHGlobal(16 * sizeof(float));
                linearBuffer = (float*)Marshal.AllocHGlobal(kOutW * kSrcH * 3 * sizeof(float));
                dotY         = (float*)Marshal.AllocHGlobal(256 * sizeof(float));
                dotI         = (float*)Marshal.AllocHGlobal(256 * sizeof(float));
                dotQ         = (float*)Marshal.AllocHGlobal(256 * sizeof(float));
                cosTab6      = (float*)Marshal.AllocHGlobal(6 * sizeof(float));
                sinTab6      = (float*)Marshal.AllocHGlobal(6 * sizeof(float));
                waveBuf      = (float*)Marshal.AllocHGlobal(kBufLen * sizeof(float));
                cBuf         = (float*)Marshal.AllocHGlobal(kBufLen * sizeof(float));
                hannY        = (float*)Marshal.AllocHGlobal(kWinY * sizeof(float));
                hannI        = (float*)Marshal.AllocHGlobal(kWinI * sizeof(float));
                hannQ        = (float*)Marshal.AllocHGlobal(kWinQ * sizeof(float));

                // 固定常數查表（只需初始化一次）
                for (int c = 0; c < 16; c++)
                {
                    double a = c * Math.PI / 6.0;
                    iPhase[c] = -(float)Math.Cos(a);
                    qPhase[c] =  (float)Math.Sin(a);
                }
                for (int k = 0; k < 6; k++)
                {
                    double a = k * 2.0 * Math.PI / 6.0;
                    cosTab6[k] = (float)Math.Cos(a);
                    sinTab6[k] = (float)Math.Sin(a);
                }
                ComputeHann(hannY, kWinY);
                ComputeHann(hannI, kWinI);
                ComputeHann(hannQ, kWinQ);

                // 預計算合併 I/Q 解調權重（6 個起始相位 × 視窗長度）
                combinedI = (float*)Marshal.AllocHGlobal(6 * kWinI * sizeof(float));
                combinedQ = (float*)Marshal.AllocHGlobal(6 * kWinQ * sizeof(float));
                for (int ph = 0; ph < 6; ph++)
                {
                    for (int n = 0; n < kWinI; n++)
                        combinedI[ph * kWinI + n] = hannI[n] * cosTab6[(ph + n) % 6];
                    for (int n = 0; n < kWinQ; n++)
                        combinedQ[ph * kWinQ + n] = hannQ[n] * sinTab6[(ph + n) % 6];
                }
            }

            scanPhase6    = 0;
            scanPhaseBase = 0;
            RfAudioLevel  = 0f;
            RfBuzzPhase   = 0f;
        }

        // Hann 視窗計算並歸一化（Σw = 1）
        static void ComputeHann(float* w, int N)
        {
            double sum = 0.0;
            for (int n = 0; n < N; n++)
            {
                w[n] = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * n / (N - 1))));
                sum += w[n];
            }
            float inv = (float)(1.0 / sum);
            for (int n = 0; n < N; n++) w[n] *= inv;
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
            {
                GenerateWaveform_SVideo(palBuf, emphasisBits, sl, phase0);
                DemodulateRow_SVideo(sl, phase0);
            }
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

        // Step 2：Y/I/Q 分離帶限解調（Hann 視窗，物理正確）
        //   Y：Hann kWinY=6（≈ 4.2 MHz，1 副載波週期精確消色度）
        //   I：SIMD dot(combinedI[tModI], waveBuf) — 預計算合併 Hann×cos 消去 per-sample mod
        //   Q：SIMD dot(combinedQ[tModQ], waveBuf) — 預計算合併 Hann×sin
        //   CrtEnabled=true  → 寫入 linearBuffer（供 Stage 2 CrtScreen 使用）
        //   CrtEnabled=false → 直接 YIQ→BGRA 寫入 AnalogScreenBuf3x
        static unsafe void DemodulateRow(int sl, int phase0)
        {
            bool toCrt = NesCore.CrtEnabled;

            int rowStart = sl * CrtScreen.DstH / CrtScreen.SrcH;
            int rowEnd   = (sl + 1) * CrtScreen.DstH / CrtScreen.SrcH;
            if (rowEnd > CrtScreen.DstH) rowEnd = CrtScreen.DstH;
            uint* row0 = NesCore.AnalogScreenBuf3x + rowStart * kOutW;

            int VS = Vector<float>.Count; // 4（SSE2）或 8（AVX2）

            for (int p = 0; p < kOutW; p++)
            {
                int center = kLeadPad + p * kWaveLen / kOutW;

                // ── Y：Hann kWinY（1 副載波週期，精確消色度，kWinY=6 不做 SIMD）
                int startY = center - kWinY_half;
                float sumY = 0f;
                for (int n = 0; n < kWinY; n++)
                    sumY += hannY[n] * waveBuf[startY + n];

                // ── I：SIMD dot product（combinedI[tModI] × waveBuf[startI]）─
                int startI = center - kWinI_half;
                int tModI  = ((phase0 + startI - kLeadPad) % 6 + 6) % 6;
                float* cwI  = combinedI + tModI * kWinI;
                float* wvI  = waveBuf   + startI;
                float  sumI = 0f;
#pragma warning disable CS8500
                {
                    int n = 0;
                    var acc = new Vector<float>(0f);
                    for (; n <= kWinI - VS; n += VS)
                        acc += *(Vector<float>*)(cwI + n) * *(Vector<float>*)(wvI + n);
                    sumI = Vector.Dot(acc, new Vector<float>(1f));
                    for (; n < kWinI; n++) sumI += cwI[n] * wvI[n];
                }

                // ── Q：SIMD dot product（combinedQ[tModQ] × waveBuf[startQ]）─
                int startQ = center - kWinQ_half;
                int tModQ  = ((phase0 + startQ - kLeadPad) % 6 + 6) % 6;
                float* cwQ  = combinedQ + tModQ * kWinQ;
                float* wvQ  = waveBuf   + startQ;
                float  sumQ = 0f;
                {
                    int n = 0;
                    var acc = new Vector<float>(0f);
                    for (; n <= kWinQ - VS; n += VS)
                        acc += *(Vector<float>*)(cwQ + n) * *(Vector<float>*)(wvQ + n);
                    sumQ = Vector.Dot(acc, new Vector<float>(1f));
                    for (; n < kWinQ; n++) sumQ += cwQ[n] * wvQ[n];
                }
#pragma warning restore CS8500

                float Y = sumY;
                float I = 2f * sumI;
                float Q = -2f * sumQ;

                if (toCrt)
                    WriteLinear(sl, p, Y, I, Q);
                else
                    row0[p] = YiqToRgb(Y, I, Q);
            }

            if (!toCrt)
                for (int row = rowStart + 1; row < rowEnd; row++)
                    Buffer.MemoryCopy(row0,
                        NesCore.AnalogScreenBuf3x + row * kOutW,
                        kOutW * sizeof(uint), kOutW * sizeof(uint));
        }

        // S-Video Step 1：Y 線與 C 線分別生成（物理正確，Y/C 分離傳輸）
        //   waveBuf = Y line：純亮度，每 dot 固定 DC，SlewRate IIR 模擬 Y 通道帶限
        //   cBuf    = C line：副載波調幅彩度訊號，無 Y 分量，邊緣填 0（空白期無彩度）
        static void GenerateWaveform_SVideo(byte[] palBuf, byte emphasisBits, int sl, int phase0)
        {
            float atten = 1.0f;
            if (emphasisBits != 0)
            {
                int n = (emphasisBits & 1) + ((emphasisBits >> 1) & 1) + ((emphasisBits >> 2) & 1);
                atten = (float)Math.Pow(0.746, n);
            }

            float firstY = 0f;
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
                float ip  = (color >= 1 && color <= 12) ? iPhase[color] * sat : 0f;
                float qp  = (color >= 1 && color <= 12) ? qPhase[color] * sat : 0f;

                if (d == 0) firstY = Y;

                int baseIdx = kLeadPad + d * kSampDot;
                for (int s = 0; s < kSampDot; s++)
                {
                    waveBuf[baseIdx + s] = Y;
                    cBuf[baseIdx + s]    = cosTab6[tMod] * ip - sinTab6[tMod] * qp;
                    tMod = tMod == 5 ? 0 : tMod + 1;
                }
            }

            // Y 線邊緣：DC 延伸；C 線邊緣：0（空白期無彩度）
            float lastY = waveBuf[kLeadPad + kWaveLen - 1];
            for (int i = 0; i < kLeadPad; i++)            { waveBuf[i] = firstY; cBuf[i] = 0f; }
            for (int i = kLeadPad + kWaveLen; i < kBufLen; i++) { waveBuf[i] = lastY; cBuf[i] = 0f; }

            // SlewRate IIR：Y 通道帶限（S-Video Y 約 3-4 MHz）
            if (SlewRate < 1.0f)
            {
                float vPrev = waveBuf[0];
                for (int i = 1; i < kBufLen; i++)
                {
                    vPrev += SlewRate * (waveBuf[i] - vPrev);
                    waveBuf[i] = vPrev;
                }
            }

            // 熱雜訊（S-Video 通常 NoiseIntensity=0，但保留路徑）
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
        }

        // S-Video Step 2：Y/C 分離帶限解調（SIMD I/Q dot product 版）
        //   Y：Hann kWinY 平均 waveBuf（純亮度，無副載波干擾）
        //   I：SIMD dot(combinedI[tModI], cBuf) — 預計算合併 Hann×cos
        //   Q：SIMD dot(combinedQ[tModQ], cBuf) — 預計算合併 Hann×sin
        //   CrtEnabled=true  → WriteLinear → linearBuffer（供 Stage 2 CrtScreen）
        //   CrtEnabled=false → 直接 YIQ→BGRA 寫入 AnalogScreenBuf3x
        static unsafe void DemodulateRow_SVideo(int sl, int phase0)
        {
            bool toCrt = NesCore.CrtEnabled;

            int rowStart = sl * CrtScreen.DstH / CrtScreen.SrcH;
            int rowEnd   = (sl + 1) * CrtScreen.DstH / CrtScreen.SrcH;
            if (rowEnd > CrtScreen.DstH) rowEnd = CrtScreen.DstH;
            uint* row0 = NesCore.AnalogScreenBuf3x + rowStart * kOutW;

            int VS = Vector<float>.Count;

            for (int p = 0; p < kOutW; p++)
            {
                int center = kLeadPad + p * kWaveLen / kOutW;

                // ── Y：Hann kWinY，waveBuf（純亮度，kWinY=6 不做 SIMD）──────
                int startY = center - kWinY_half;
                float sumY = 0f;
                for (int n = 0; n < kWinY; n++)
                    sumY += hannY[n] * waveBuf[startY + n];

                // ── I：SIMD dot product（combinedI[tModI] × cBuf[startI]）──
                int startI = center - kWinI_half;
                int tModI  = ((phase0 + startI - kLeadPad) % 6 + 6) % 6;
                float* cwI  = combinedI + tModI * kWinI;
                float* wvI  = cBuf      + startI;
                float  sumI = 0f;
#pragma warning disable CS8500
                {
                    int n = 0;
                    var acc = new Vector<float>(0f);
                    for (; n <= kWinI - VS; n += VS)
                        acc += *(Vector<float>*)(cwI + n) * *(Vector<float>*)(wvI + n);
                    sumI = Vector.Dot(acc, new Vector<float>(1f));
                    for (; n < kWinI; n++) sumI += cwI[n] * wvI[n];
                }

                // ── Q：SIMD dot product（combinedQ[tModQ] × cBuf[startQ]）──
                int startQ = center - kWinQ_half;
                int tModQ  = ((phase0 + startQ - kLeadPad) % 6 + 6) % 6;
                float* cwQ  = combinedQ + tModQ * kWinQ;
                float* wvQ  = cBuf      + startQ;
                float  sumQ = 0f;
                {
                    int n = 0;
                    var acc = new Vector<float>(0f);
                    for (; n <= kWinQ - VS; n += VS)
                        acc += *(Vector<float>*)(cwQ + n) * *(Vector<float>*)(wvQ + n);
                    sumQ = Vector.Dot(acc, new Vector<float>(1f));
                    for (; n < kWinQ; n++) sumQ += cwQ[n] * wvQ[n];
                }
#pragma warning restore CS8500

                float Y = sumY;
                float I = 2f * sumI;
                float Q = -2f * sumQ;

                if (toCrt)
                    WriteLinear(sl, p, Y, I, Q);
                else
                    row0[p] = YiqToRgb(Y, I, Q);
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
        //  linearBuffer planar layout：[R plane 0..kPlane-1][G plane][B plane]
        //  R[sl,p] = linearBuffer[sl*kOutW + p]
        //  G[sl,p] = linearBuffer[kPlane  + sl*kOutW + p]
        //  B[sl,p] = linearBuffer[2*kPlane + sl*kOutW + p]
        //
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void WriteLinear(int sl, int p, float y, float i, float q)
        {
            float r = y + 1.0841f * i + 0.3523f * q;
            float g = y - 0.4302f * i - 0.5547f * q;
            float b = y - 0.6268f * i + 1.9299f * q;

            if (r < 0f) r = 0f; else if (r > 1f) r = 1f;
            if (g < 0f) g = 0f; else if (g > 1f) g = 1f;
            if (b < 0f) b = 0f; else if (b > 1f) b = 1f;

            int idx = sl * kOutW + p;
            linearBuffer[idx]            = r;
            linearBuffer[kPlane  + idx]  = g;
            linearBuffer[2*kPlane + idx] = b;
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
