using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AprNes
{
    // ============================================================
    // NES NTSC 訊號解碼器（Stage 1）
    // ============================================================
    //
    //  輸出：float* linearBuffer [1024×240×3]  ← 線性 RGB，無 Gamma（Planar layout）
    //  Stage 2 (CrtScreen) 負責垂直擴展 + 高斯掃描線 + Bloom + Gamma → 1024×840
    //
    //  Level 2（預設，快速）：直接 YIQ + 6-entry LUT dot crawl
    //  Level 3（UltraAnalog，精確）：21.477 MHz 時域波形 + coherent demodulation
    //
    //  優化歷程：
    //    · Phase A：>>2 取代 *256/kOutW；相位遞增取代 %6；滑動指標消去乘法
    //    · Phase B：gammaLUT[256]（byte 查表取代 float gamma 乘法）
    //    · Phase C：yBase/iBase/qBase[64] + waveTable/cTable[64×6×4]（LUT 重構）
    //               SlewRate IIR 折疊進生成迴圈（消去獨立 IIR pass）
    //    · Phase D：Q channel 降採樣至 dot 率（256 點，4× MAC 減少）
    //               Y window 手動展開 6-tap（消去迴圈 overhead）
    // ============================================================

    // 共用端子模式列舉（獨立於 NesCore，供外部程式使用）
    public enum AnalogOutputMode { AV = 0, SVideo = 1, RF = 2 }

    unsafe public static class Ntsc
    {
        // ── 解耦參數（由外部透過 ApplyConfig 注入）────────────────────────
        static int    _analogOutput;        // AnalogOutputMode as int
        static bool   _ultraAnalog;
        static int    _analogSize;
        static bool   _crtEnabled;
        static uint*  _analogScreenBuf;
        static int    _frameCount;

        /// <summary>
        /// 將外部運行時參數注入 Ntsc 模組（每次設定變更或 Init 時呼叫）
        /// </summary>
        public static void ApplyConfig(int analogOutput, bool ultraAnalog, int analogSize,
                                        bool crtEnabled, uint* analogScreenBuf)
        {
            _analogOutput    = analogOutput;
            _ultraAnalog     = ultraAnalog;
            _analogSize      = analogSize;
            _crtEnabled      = crtEnabled;
            _analogScreenBuf = analogScreenBuf;
            ApplyProfile();  // 端子參數只在設定變更時需要套用
        }

        /// <summary>每幀更新幀計數器（供雜訊/jitter 使用）</summary>
        public static void SetFrameCount(int fc) => _frameCount = fc;

        // ── 共用：blargg 電壓準位 ────────────────────────────────────────────
        static float* loLevels;
        static float* hiLevels;

        // ── 共用：色相查表 ───────────────────────────────────────────────────
        static float* iPhase;
        static float* qPhase;

        // ── 共用：RF 音訊干擾 ────────────────────────────────────────────────
        static public float RfAudioLevel = 0.0f;
        static public float RfBuzzPhase  = 0.0f;

        // ── 輸出：線性 RGB 緩衝區（Planar） ─────────────────────────────────
        public const int kOutW  = 1024;
        public const int kSrcH  = 240;
        public const int kPlane = kOutW * kSrcH; // 245,760 floats per plane
        public static float* linearBuffer;        // planar: [R][G][B]

        // ── 端子參數組 ───────────────────────────────────────────────────────
        public static float RF_NoiseIntensity = 0.04f;
        public static float RF_SlewRate       = 0.60f;
        public static float RF_ChromaBlur     = 0.10f;
        public static float AV_NoiseIntensity = 0.003f;
        public static float AV_SlewRate       = 0.80f;
        public static float AV_ChromaBlur     = 0.35f;
        public static float SV_NoiseIntensity = 0.00f;
        public static float SV_SlewRate       = 0.90f;
        public static float SV_ChromaBlur     = 0.45f;

        static float NoiseIntensity;
        static float SlewRate;
        static float ChromaBlur;

        static void ApplyProfile()
        {
            if (_analogOutput == (int)AnalogOutputMode.RF)
            { NoiseIntensity = RF_NoiseIntensity; SlewRate = RF_SlewRate; ChromaBlur = RF_ChromaBlur; }
            else if (_analogOutput == (int)AnalogOutputMode.SVideo)
            { NoiseIntensity = SV_NoiseIntensity; SlewRate = SV_SlewRate; ChromaBlur = SV_ChromaBlur; }
            else
            { NoiseIntensity = AV_NoiseIntensity; SlewRate = AV_SlewRate; ChromaBlur = AV_ChromaBlur; }
        }

        // ── Level 2 ─────────────────────────────────────────────────────────
        static float* dotY;
        static float* dotI;
        static float* dotQ;
        static int    scanPhase6 = 0;

        // ── Level 3 ─────────────────────────────────────────────────────────
        static float* cosTab6;
        static float* sinTab6;

        const int kDots    = 256;
        const int kSampDot = 4;
        const int kWaveLen = kDots * kSampDot;        // 1024
        const int kLeadPad = 30;                       // ≥ kWinQ_half=27
        const int kBufLen  = kLeadPad * 2 + kWaveLen; // 1084
        static float* waveBuf;
        static float* cBuf;
        static float* demodQBuf;    // [256] Q dot-rate demod (static for Parallel.For)
        static uint*  demodTmpBuf;  // [kOutW] resample temp (static for Parallel.For)

        const int kWinY      = 6;  const int kWinY_half = kWinY / 2;
        const int kWinI      = 18; const int kWinI_half = kWinI / 2;
        const int kWinQ      = 54; const int kWinQ_half = kWinQ / 2;
        static float* hannY;
        static float* hannI;
        static float* hannQ;

        // 預計算合併 I/Q 權重（6 相位 × kWinI/kWinQ）
        static float* combinedI;
        static float* combinedQ;

        static int scanPhaseBase = 0;

        // ── Phase B/C LUT ────────────────────────────────────────────────────
        // gammaLUT：float gamma → byte（256 entries）
        public static byte*  gammaLUT;
        // yBase/iBase/qBase：palette→YIQ（無 emphasis，64 entries）
        static float* yBase;
        static float* iBase;
        static float* qBase;
        // waveTable：palette×phase→複合訊號（64×6×4 = 1536 entries）
        static float* waveTable;
        // cTable：palette×phase→純色度 C（S-Video 用，64×6×4 entries）
        static float* cTable;
        // attenTab：emphasis bit count → attenuation（4 entries, legacy）
        static float* attenTab;
        // #1 Emphasis per-phase attenuation (8 combos × 12 phases, 12 avoids mod)
        static float* emphAtten;
        // #1 Emphasis-adjusted YIQ (64 palette × 8 emphasis combos)
        static float* yBaseE;
        static float* iBaseE;
        static float* qBaseE;

        // #16 Color temperature RGB multipliers (default neutral)
        public static float ColorTempR = 1.0f;
        public static float ColorTempG = 1.0f;
        public static float ColorTempB = 1.0f;
        // #16 Pre-computed YIQ→RGB matrix (blargg -15° × color temperature)
        static float yiq_rY = 1.0f,   yiq_rI =  1.0841f, yiq_rQ =  0.3523f;
        static float yiq_gY = 1.0f,   yiq_gI = -0.4302f, yiq_gQ = -0.5547f;
        static float yiq_bY = 1.0f,   yiq_bI = -0.6268f, yiq_bQ =  1.9299f;

        // #17 Configurable gamma coefficient (default 0.229f ≈ pow(v, 1/1.13))
        public static float GammaCoeff = 0.229f;

        // #5 Ringing / Gibbs effect (0=off, 0.3=default)
        public static float RingStrength = 0.3f;

        // #4 HBI simulation (blanking level at line start)
        public static bool HbiSimulation = true;

        // #3 Color burst phase jitter enable (RF only, ~3% of scanlines)
        public static bool ColorBurstJitter = true;

        // ════════════════════════════════════════════════════════════════════
        // Init
        // ════════════════════════════════════════════════════════════════════
        public static void Init()
        {
            if (loLevels == null)
            {
                loLevels = (float*)Marshal.AllocHGlobal(4 * sizeof(float));
                loLevels[0] = -0.12f; loLevels[1] = 0.00f; loLevels[2] = 0.31f; loLevels[3] = 0.72f;
                hiLevels = (float*)Marshal.AllocHGlobal(4 * sizeof(float));
                hiLevels[0] = 0.40f;  hiLevels[1] = 0.68f; hiLevels[2] = 1.00f; hiLevels[3] = 1.00f;

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
                demodQBuf    = (float*)Marshal.AllocHGlobal(256 * sizeof(float));
                demodTmpBuf  = (uint*) Marshal.AllocHGlobal(kOutW * sizeof(uint));
                hannY        = (float*)Marshal.AllocHGlobal(kWinY * sizeof(float));
                hannI        = (float*)Marshal.AllocHGlobal(kWinI * sizeof(float));
                hannQ        = (float*)Marshal.AllocHGlobal(kWinQ * sizeof(float));
                combinedI    = (float*)Marshal.AllocHGlobal(6 * kWinI * sizeof(float));
                combinedQ    = (float*)Marshal.AllocHGlobal(6 * kWinQ * sizeof(float));
                gammaLUT     = (byte*) Marshal.AllocHGlobal(256);
                attenTab     = (float*)Marshal.AllocHGlobal(4 * sizeof(float));
                yBase        = (float*)Marshal.AllocHGlobal(64 * sizeof(float));
                iBase        = (float*)Marshal.AllocHGlobal(64 * sizeof(float));
                qBase        = (float*)Marshal.AllocHGlobal(64 * sizeof(float));
                waveTable    = (float*)Marshal.AllocHGlobal(64 * 6 * 4 * sizeof(float));
                cTable       = (float*)Marshal.AllocHGlobal(64 * 6 * 4 * sizeof(float));
                emphAtten    = (float*)Marshal.AllocHGlobal(8 * 12 * sizeof(float));
                yBaseE       = (float*)Marshal.AllocHGlobal(64 * 8 * sizeof(float));
                iBaseE       = (float*)Marshal.AllocHGlobal(64 * 8 * sizeof(float));
                qBaseE       = (float*)Marshal.AllocHGlobal(64 * 8 * sizeof(float));

                // 色相、副載波
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

                // Hann 視窗
                ComputeHann(hannY, kWinY);
                ComputeHann(hannI, kWinI);
                ComputeHann(hannQ, kWinQ);

                // 合併 I/Q 解調權重
                for (int ph = 0; ph < 6; ph++)
                {
                    for (int n = 0; n < kWinI; n++)
                        combinedI[ph * kWinI + n] = hannI[n] * cosTab6[(ph + n) % 6];
                    for (int n = 0; n < kWinQ; n++)
                        combinedQ[ph * kWinQ + n] = hannQ[n] * sinTab6[(ph + n) % 6];
                }

                // Phase B：Gamma LUT — computed by UpdateGammaLUT() after if block

                // Phase B：Emphasis attenuation table
                attenTab[0] = 1.0f;
                for (int n = 1; n <= 3; n++)
                    attenTab[n] = (float)Math.Pow(0.746, n);

                // Phase C：YIQ 基底表（64 palette entries，at atten=1）
                for (int p = 0; p < 64; p++)
                {
                    int luma  = (p >> 4) & 3;
                    int color = p & 0x0F;
                    float lo = loLevels[luma], hi = hiLevels[luma];
                    if      (color == 0)    lo = hi;
                    else if (color == 0x0D) hi = lo;
                    else if (color > 0x0D)  lo = hi = 0f;
                    float sat = (hi - lo) * 0.5f;
                    yBase[p] = (hi + lo) * 0.5f;
                    if (color >= 1 && color <= 12) { iBase[p] = iPhase[color] * sat; qBase[p] = qPhase[color] * sat; }
                    else                           { iBase[p] = 0f; qBase[p] = 0f; }
                }

                // Phase C：waveTable[64×6×4] 與 cTable[64×6×4]
                for (int p = 0; p < 64; p++)
                {
                    for (int ph = 0; ph < 6; ph++)
                    {
                        float* wdst = waveTable + (p * 6 + ph) * 4;
                        float* cdst = cTable    + (p * 6 + ph) * 4;
                        for (int s = 0; s < 4; s++)
                        {
                            int tm  = (ph + s) % 6;
                            cdst[s] = cosTab6[tm] * iBase[p] - sinTab6[tm] * qBase[p];
                            wdst[s] = yBase[p] + cdst[s];
                        }
                    }
                }

                // #1 Emphasis per-phase attenuation (8 combos × 12 phases)
                // R(bit0) attenuates phases {1,2,3}, G(bit1) {3,4,5}, B(bit2) {5,0,1}
                for (int e = 0; e < 8; e++)
                {
                    for (int p = 0; p < 6; p++)
                    {
                        int cnt = 0;
                        if ((e & 1) != 0 && p >= 1 && p <= 3) cnt++;
                        if ((e & 2) != 0 && p >= 3 && p <= 5) cnt++;
                        if ((e & 4) != 0 && (p >= 5 || p <= 1)) cnt++;
                        emphAtten[e * 12 + p] = (float)Math.Pow(0.746, cnt);
                    }
                    for (int p = 0; p < 6; p++)
                        emphAtten[e * 12 + 6 + p] = emphAtten[e * 12 + p];
                }

                // #1 Emphasis-adjusted YIQ (Fourier decomposition of per-phase emphasized waveform)
                for (int p = 0; p < 64; p++)
                {
                    for (int e = 0; e < 8; e++)
                    {
                        float sumY = 0f, sumI = 0f, sumQ = 0f;
                        for (int ph = 0; ph < 6; ph++)
                        {
                            float V = yBase[p] + iBase[p] * cosTab6[ph] - qBase[p] * sinTab6[ph];
                            V *= emphAtten[e * 12 + ph];
                            sumY += V;
                            sumI += V * cosTab6[ph];
                            sumQ -= V * sinTab6[ph];
                        }
                        yBaseE[p * 8 + e] = sumY / 6f;
                        iBaseE[p * 8 + e] = sumI / 3f;
                        qBaseE[p * 8 + e] = sumQ / 3f;
                    }
                }
            }

            // #16 #17 Recompute derived constants (color temp / gamma may change between ROM loads)
            UpdateColorTemp();
            UpdateGammaLUT();

            scanPhase6    = 0;
            scanPhaseBase = 0;
            RfAudioLevel  = 0f;
            RfBuzzPhase   = 0f;
        }

        // #16 Recompute YIQ→RGB matrix with current color temperature
        public static void UpdateColorTemp()
        {
            yiq_rY =  1.0f    * ColorTempR; yiq_rI =  1.0841f * ColorTempR; yiq_rQ =  0.3523f * ColorTempR;
            yiq_gY =  1.0f    * ColorTempG; yiq_gI = -0.4302f * ColorTempG; yiq_gQ = -0.5547f * ColorTempG;
            yiq_bY =  1.0f    * ColorTempB; yiq_bI = -0.6268f * ColorTempB; yiq_bQ =  1.9299f * ColorTempB;
        }

        // #17 Recompute gamma LUT with current GammaCoeff
        public static void UpdateGammaLUT()
        {
            if (gammaLUT == null) return;
            float gc = GammaCoeff;
            for (int v = 0; v < 256; v++)
            {
                float fv = v / 255.0f;
                fv += gc * fv * (fv - 1f);
                int vi = (int)(fv * 255.5f);
                gammaLUT[v] = (byte)(vi < 0 ? 0 : vi > 255 ? 255 : vi);
            }
        }

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
        // 主進入點
        // ════════════════════════════════════════════════════════════════════
        public static void DecodeScanline(int sl, byte[] palBuf, byte emphasisBits)
        {
            if (sl < 0 || sl >= kSrcH) return;

            if (_ultraAnalog)
                DecodeScanline_Physical(sl, palBuf, emphasisBits);
            else
                DecodeScanline_Fast(sl, palBuf, emphasisBits);
        }

        // ════════════════════════════════════════════════════════════════════
        // Level 2：快速路徑
        // ════════════════════════════════════════════════════════════════════

        static void DecodeScanline_Fast(int sl, byte[] palBuf, byte emphasisBits)
        {
            int phase0 = scanPhase6;
            scanPhase6 = (scanPhase6 + 2) % 6;
            GenerateSignal(palBuf, emphasisBits);

            if ((AnalogOutputMode)_analogOutput == AnalogOutputMode.SVideo)
                DecodeAV_SVideo(sl);
            else
                DecodeAV_Composite(sl, phase0);
        }

        // Phase C：64×8 YIQ LUT（#1 per-phase emphasis-adjusted，取代 uniform attenTab）
        static void GenerateSignal(byte[] palBuf, byte emphasisBits)
        {
            int emph = emphasisBits & 7;
            for (int d = 0; d < 256; d++)
            {
                int k = (palBuf[d] & 63) * 8 + emph;
                dotY[d] = yBaseE[k];
                dotI[d] = iBaseE[k];
                dotQ[d] = qBaseE[k];
            }
        }

        // Phase A：outX>>2（取代 *256/kOutW），ph 遞增取代 (phase0+outX)%6
        static unsafe void DecodeAV_Composite(int sl, int phase0)
        {
            bool isRF     = (AnalogOutputMode)_analogOutput == AnalogOutputMode.RF;
            bool addNoise = NoiseIntensity > 0f;

            uint ns = 0u;
            if (addNoise || isRF)
                ns = (uint)(_frameCount * 1664525u + (uint)sl * 1013904223u + 1442695041u);

            // IIR 初始狀態
            int   ph0   = phase0;
            float c0    = cosTab6[ph0], s0 = sinTab6[ph0];
            float chr0  = dotI[0] * c0 - dotQ[0] * s0;
            // #4 HBI: start from blanking level (0) instead of first pixel
            float iFilt = HbiSimulation ? 0f : chr0 * c0;
            float qFilt = HbiSimulation ? 0f : -chr0 * s0;
            float yFilt = HbiSimulation ? 0f : dotY[0];
            // #5 Ringing: damped spring velocity
            float ringDamp = RingStrength * 0.5f;
            float yVel     = 0f;

            int dstW     = CrtScreen.DstW;
            int N        = _analogSize;   // dot index = outX / N
            int rowStart = sl * CrtScreen.DstH / CrtScreen.SrcH;
            int rowEnd   = (sl + 1) * CrtScreen.DstH / CrtScreen.SrcH;
            if (rowEnd > CrtScreen.DstH) rowEnd = CrtScreen.DstH;
            uint* row0 = _analogScreenBuf + rowStart * dstW;

            // Phase B：noise scale 移出迴圈
            float noiseScale  = addNoise ? (2f * NoiseIntensity / 255.0f) : 0f;
            float noiseOffset = addNoise ? NoiseIntensity : 0f;

            // #9 RF herringbone: per-pixel 4.5MHz oscillator (Level 2)
            float hR2 = 0f, hI2 = 0f, hC2 = 1f, hS2 = 0f;
            bool herring2 = false;
            if (isRF)
            {
                float buzzAmp = RfAudioLevel * 0.06f;
                float envelope = buzzAmp * (float)Math.Sin((sl / 240.0 + RfBuzzPhase) * 2.0 * Math.PI);
                if (envelope > 0.0001f || envelope < -0.0001f)
                {
                    herring2 = true;
                    float radsPerPx = 1.31683f * 1024f / dstW; // scale to output pixel rate
                    hC2 = (float)Math.Cos(radsPerPx);
                    hS2 = (float)Math.Sin(radsPerPx);
                    float linePhase = sl * 1364f * 1.31683f;
                    hR2 = envelope * (float)Math.Cos(linePhase);
                    hI2 = envelope * (float)Math.Sin(linePhase);
                }
            }

            int ph = phase0;
            for (int outX = 0; outX < dstW; outX++)
            {
                int   d      = outX / N;           // dot index（依 AnalogSize 縮放）
                float c      = cosTab6[ph];
                float s      = sinTab6[ph];
                float chroma = dotI[d] * c - dotQ[d] * s;

                iFilt += ChromaBlur * (chroma * c  - iFilt);
                qFilt += ChromaBlur * (-chroma * s - qFilt);
                // #5 Ringing: damped spring on Y channel
                yVel = yVel * ringDamp + (dotY[d] - yFilt) * SlewRate;
                yFilt += yVel;
                float y = yFilt;

                if (herring2) { y += hI2; float t = hR2*hC2 - hI2*hS2; hI2 = hR2*hS2 + hI2*hC2; hR2 = t; }
                if (addNoise)
                {
                    ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5;
                    y += (ns & 0xFF) * noiseScale - noiseOffset;
                }

                row0[outX] = YiqToRgb(y, iFilt, qFilt);

                if (++ph == 6) ph = 0;
            }

            for (int row = rowStart + 1; row < rowEnd; row++)
                Buffer.MemoryCopy(row0, _analogScreenBuf + row * dstW,
                    dstW * sizeof(uint), dstW * sizeof(uint));
        }

        static unsafe void DecodeAV_SVideo(int sl)
        {
            // #4 HBI: start from blanking level (0)
            float iFilt = HbiSimulation ? 0f : dotI[0];
            float qFilt = HbiSimulation ? 0f : dotQ[0];
            float yFilt = HbiSimulation ? 0f : dotY[0];

            int dstW     = CrtScreen.DstW;
            int N        = _analogSize;
            int rowStart = sl * CrtScreen.DstH / CrtScreen.SrcH;
            int rowEnd   = (sl + 1) * CrtScreen.DstH / CrtScreen.SrcH;
            if (rowEnd > CrtScreen.DstH) rowEnd = CrtScreen.DstH;
            uint* row0 = _analogScreenBuf + rowStart * dstW;

            for (int outX = 0; outX < dstW; outX++)
            {
                int d = outX / N;
                iFilt += ChromaBlur * (dotI[d] - iFilt);
                qFilt += ChromaBlur * (dotQ[d] - qFilt);
                yFilt += SlewRate   * (dotY[d] - yFilt);
                row0[outX] = YiqToRgb(yFilt, iFilt, qFilt);
            }

            for (int row = rowStart + 1; row < rowEnd; row++)
                Buffer.MemoryCopy(row0, _analogScreenBuf + row * dstW,
                    dstW * sizeof(uint), dstW * sizeof(uint));
        }

        // ════════════════════════════════════════════════════════════════════
        // Level 3：物理路徑
        // ════════════════════════════════════════════════════════════════════

        static void DecodeScanline_Physical(int sl, byte[] palBuf, byte emphasisBits)
        {
            int phase0 = scanPhaseBase;
            scanPhaseBase = (scanPhaseBase + 2) % 6;

            // #3 Color burst phase jitter (RF only, ~3% of scanlines)
            if (ColorBurstJitter && (AnalogOutputMode)_analogOutput == AnalogOutputMode.RF)
            {
                uint jns = (uint)(_frameCount * 2654435761u + (uint)sl * 340573321u);
                jns ^= jns << 13; jns ^= jns >> 17; jns ^= jns << 5;
                if ((jns & 31) == 0)
                    phase0 = (phase0 + ((jns & 64) != 0 ? 1 : 5)) % 6;
            }

            if ((AnalogOutputMode)_analogOutput == AnalogOutputMode.SVideo)
            {
                GenerateWaveform_SVideo(palBuf, emphasisBits, sl, phase0);
                DemodulateRow_SVideo(sl, phase0);
            }
            else
            {
                bool isRF = (AnalogOutputMode)_analogOutput == AnalogOutputMode.RF;
                GenerateWaveform(palBuf, emphasisBits, isRF, sl, phase0);
                DemodulateRow(sl, phase0);
            }
        }

        // Phase C：waveTable LUT + Phase C：IIR 折疊（消去獨立 IIR pass）
        // Phase B：noise scale 移出迴圈
        static void GenerateWaveform(byte[] palBuf, byte emphasisBits,
                                      bool isRF, int sl, int phase0)
        {
            int    emph    = emphasisBits & 7;
            float* ea      = emphAtten + emph * 12;  // #1 per-phase attenuation
            bool   addNoise = NoiseIntensity > 0f;

            float firstY = yBaseE[(palBuf[0]   & 63) * 8 + emph];
            float lastY  = yBaseE[(palBuf[255] & 63) * 8 + emph];

            // #9 RF herringbone: per-sample 4.5MHz oscillator (replaces constant buzzRow)
            const float kHerringRPS = 1.31683f; // 2π × 4.5 / 21.477
            float hR_buzz = 0f, hI_buzz = 0f, hC_buzz = 1f, hS_buzz = 0f;
            bool herring = false;
            if (isRF)
            {
                float buzzAmp = RfAudioLevel * 0.06f;
                float envelope = buzzAmp * (float)Math.Sin(
                                     (sl / 240.0 + RfBuzzPhase) * 2.0 * Math.PI);
                if (envelope > 0.0001f || envelope < -0.0001f)
                {
                    herring = true;
                    hC_buzz = (float)Math.Cos(kHerringRPS);
                    hS_buzz = (float)Math.Sin(kHerringRPS);
                    float linePhase = sl * 1364f * kHerringRPS;
                    hR_buzz = envelope * (float)Math.Cos(linePhase);
                    hI_buzz = envelope * (float)Math.Sin(linePhase);
                }
            }

            uint  ns         = 0u;
            float noiseScale = 0f, noiseOffset = 0f;
            if (addNoise)
            {
                ns          = (uint)(_frameCount * 1664525u + (uint)sl * 1013904223u + 1442695041u);
                noiseScale  = 2f * NoiseIntensity / 255.0f;
                noiseOffset = NoiseIntensity;
            }

            // #4 HBI: left padding uses blanking level (0) instead of first pixel
            float leftPad = HbiSimulation ? 0.0f : firstY;
            for (int i = 0; i < kLeadPad; i++) waveBuf[i] = leftPad;

            // Phase C：waveTable 查表 + Phase C：IIR 折疊 + buzz + noise 合併
            // #5 Ringing: damped spring model (vVel has memory → overshoot)
            float vPrev    = leftPad;
            float ringDamp = RingStrength * 0.5f;
            float vVel     = 0f;
            int   tMod     = phase0;
            for (int d = 0; d < kDots; d++)
            {
                float* src     = waveTable + ((palBuf[d] & 63) * 6 + tMod) * 4;
                int    baseIdx = kLeadPad + d * 4;
                for (int s = 0; s < 4; s++)
                {
                    float x = src[s] * ea[tMod + s];  // #1 per-phase emphasis
                    if (herring) { x += hI_buzz; float t = hR_buzz*hC_buzz - hI_buzz*hS_buzz; hI_buzz = hR_buzz*hS_buzz + hI_buzz*hC_buzz; hR_buzz = t; }
                    if (addNoise)
                    {
                        ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5;
                        x += (ns & 0xFF) * noiseScale - noiseOffset;
                    }
                    vVel = vVel * ringDamp + (x - vPrev) * SlewRate;
                    vPrev += vVel;
                    waveBuf[baseIdx + s] = vPrev;
                }
                tMod += 4; if (tMod >= 6) tMod -= 6;
            }

            // 右邊填充（IIR 繼續收斂至 lastY）
            for (int i = kLeadPad + kWaveLen; i < kBufLen; i++)
            {
                vVel = vVel * ringDamp + (lastY - vPrev) * SlewRate;
                vPrev += vVel;
                waveBuf[i] = vPrev;
            }
        }

        // Phase C：yBase + cTable LUT + IIR 折疊
        static void GenerateWaveform_SVideo(byte[] palBuf, byte emphasisBits, int sl, int phase0)
        {
            int    emph    = emphasisBits & 7;
            float* ea      = emphAtten + emph * 12;  // #1 per-phase attenuation
            bool   addNoise = NoiseIntensity > 0f;

            float firstY = yBaseE[(palBuf[0]   & 63) * 8 + emph];
            float lastY  = yBaseE[(palBuf[255] & 63) * 8 + emph];

            uint  ns         = 0u;
            float noiseScale = 0f, noiseOffset = 0f;
            if (addNoise)
            {
                ns          = (uint)(_frameCount * 1664525u + (uint)sl * 1013904223u + 1442695041u);
                noiseScale  = 2f * NoiseIntensity / 255.0f;
                noiseOffset = NoiseIntensity;
            }

            // #4 HBI: left padding uses blanking level (0) instead of first pixel
            float leftPad = HbiSimulation ? 0.0f : firstY;
            for (int i = 0; i < kLeadPad; i++) { waveBuf[i] = leftPad; cBuf[i] = 0f; }

            // Phase C：Y line（SlewRate IIR 折疊）+ C line（cTable LUT，per-phase emphasis）
            // #5 Ringing: damped spring model
            float vPrev    = leftPad;
            float ringDamp = RingStrength * 0.5f;
            float vVel     = 0f;
            int   tMod     = phase0;
            for (int d = 0; d < kDots; d++)
            {
                float  Ytgt    = yBaseE[(palBuf[d] & 63) * 8 + emph];  // #1
                float* csrc    = cTable + ((palBuf[d] & 63) * 6 + tMod) * 4;
                int    baseIdx = kLeadPad + d * 4;
                for (int s = 0; s < 4; s++)
                {
                    float y = Ytgt;
                    if (addNoise)
                    {
                        ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5;
                        y += (ns & 0xFF) * noiseScale - noiseOffset;
                    }
                    vVel = vVel * ringDamp + (y - vPrev) * SlewRate;
                    vPrev += vVel;
                    waveBuf[baseIdx + s] = vPrev;
                    cBuf  [baseIdx + s] = csrc[s] * ea[tMod + s];  // #1 per-phase emphasis
                }
                tMod += 4; if (tMod >= 6) tMod -= 6;
            }

            // 右邊填充
            for (int i = kLeadPad + kWaveLen; i < kBufLen; i++)
            {
                vVel = vVel * ringDamp + (lastY - vPrev) * SlewRate;
                vPrev += vVel;
                waveBuf[i] = vPrev;
                cBuf[i]    = 0f;
            }
        }

        // Phase A：滑動指標（消去 center 乘法）+ tModI 遞增（消去 double %6）
        // Phase D：Q@dot rate（256 點，4× MACs 節省）
        // Phase D：Y 6-tap 手動展開
        static unsafe void DemodulateRow(int sl, int phase0)
        {
            bool toCrt = _crtEnabled;
            int dstW   = CrtScreen.DstW;

            int rowStart = sl * CrtScreen.DstH / CrtScreen.SrcH;
            int rowEnd   = (sl + 1) * CrtScreen.DstH / CrtScreen.SrcH;
            if (rowEnd > CrtScreen.DstH) rowEnd = CrtScreen.DstH;
            uint* row0 = _analogScreenBuf + rowStart * dstW;

            int VS = Vector<float>.Count;

#pragma warning disable CS8500
            // Phase D：預計算 Q@dot rate（256 點 × 54 MACs SIMD）
            {
                float* wvQ  = waveBuf + kLeadPad - kWinQ_half + 2;
                int tModQ = ((phase0 - kWinQ_half + 2) % 6 + 6) % 6;
                for (int d = 0; d < 256; d++)
                {
                    float* cwQ = combinedQ + tModQ * kWinQ;
                    int n = 0;
                    var acc = new Vector<float>(0f);
                    for (; n <= kWinQ - VS; n += VS)
                        acc += *(Vector<float>*)(cwQ + n) * *(Vector<float>*)(wvQ + n);
                    float sumQ = Vector.Dot(acc, new Vector<float>(1f));
                    for (; n < kWinQ; n++) sumQ += cwQ[n] * wvQ[n];
                    demodQBuf[d] = -2f * sumQ;
                    wvQ += kSampDot;
                    tModQ += 4; if (tModQ >= 6) tModQ -= 6;
                }
            }
#pragma warning restore CS8500

            // 主迴圈 Parallel.For（每像素獨立 FIR，無資料依賴）
            int wvYOff = kLeadPad - kWinY_half;
            int wvIOff = kLeadPad - kWinI_half;
            int tModIBase = ((phase0 - kWinI_half) % 6 + 6) % 6;
            int slC = sl; bool toCrtC = toCrt; int dstWC = dstW; int rowStartC = rowStart;

            Parallel.For(0, kOutW, p =>
            {
                unsafe
                {
#pragma warning disable CS8500
                    float* wvY = waveBuf + wvYOff + p;
                    float* wvI = waveBuf + wvIOff + p;
                    int tModI = (tModIBase + p) % 6;

                    float sumY = hannY[0]*wvY[0] + hannY[1]*wvY[1] + hannY[2]*wvY[2]
                               + hannY[3]*wvY[3] + hannY[4]*wvY[4] + hannY[5]*wvY[5];

                    float* cwI = combinedI + tModI * kWinI;
                    float  sumI;
                    {
                        int VS = Vector<float>.Count;
                        int n = 0;
                        var acc = new Vector<float>(0f);
                        for (; n <= kWinI - VS; n += VS)
                            acc += *(Vector<float>*)(cwI + n) * *(Vector<float>*)(wvI + n);
                        sumI = Vector.Dot(acc, new Vector<float>(1f));
                        for (; n < kWinI; n++) sumI += cwI[n] * wvI[n];
                    }

                    float Y = sumY;
                    float I = 2f * sumI;
                    float Q = demodQBuf[p >> 2];

                    if (toCrtC)
                        WriteLinear(slC, p, Y, I, Q);
                    else
                    {
                        uint pixel = YiqToRgb(Y, I, Q);
                        uint* outRow = _analogScreenBuf + rowStartC * dstWC;
                        if (dstWC == kOutW)
                            outRow[p] = pixel;
                        else
                            demodTmpBuf[p] = pixel;
                    }
#pragma warning restore CS8500
                }
            });

            if (!toCrt)
            {
                if (dstW != kOutW)
                {
                    // nearest-neighbor resample kOutW → dstW
                    int fpScale = (kOutW << 16) / dstW;
                    for (int x = 0; x < dstW; x++)
                        row0[x] = demodTmpBuf[(x * fpScale) >> 16];
                }
                for (int row = rowStart + 1; row < rowEnd; row++)
                    Buffer.MemoryCopy(row0, _analogScreenBuf + row * dstW,
                        dstW * sizeof(uint), dstW * sizeof(uint));
            }
        }

        // DemodulateRow_SVideo：同 DemodulateRow，I/Q 來源改為 cBuf
        static unsafe void DemodulateRow_SVideo(int sl, int phase0)
        {
            bool toCrt = _crtEnabled;
            int dstW   = CrtScreen.DstW;

            int rowStart = sl * CrtScreen.DstH / CrtScreen.SrcH;
            int rowEnd   = (sl + 1) * CrtScreen.DstH / CrtScreen.SrcH;
            if (rowEnd > CrtScreen.DstH) rowEnd = CrtScreen.DstH;
            uint* row0 = _analogScreenBuf + rowStart * dstW;

            int VS = Vector<float>.Count;

#pragma warning disable CS8500
            {
                float* wvQ  = cBuf + kLeadPad - kWinQ_half + 2;
                int tModQ = ((phase0 - kWinQ_half + 2) % 6 + 6) % 6;
                for (int d = 0; d < 256; d++)
                {
                    float* cwQ = combinedQ + tModQ * kWinQ;
                    int n = 0;
                    var acc = new Vector<float>(0f);
                    for (; n <= kWinQ - VS; n += VS)
                        acc += *(Vector<float>*)(cwQ + n) * *(Vector<float>*)(wvQ + n);
                    float sumQ = Vector.Dot(acc, new Vector<float>(1f));
                    for (; n < kWinQ; n++) sumQ += cwQ[n] * wvQ[n];
                    demodQBuf[d] = -2f * sumQ;
                    wvQ += kSampDot;
                    tModQ += 4; if (tModQ >= 6) tModQ -= 6;
                }
            }
#pragma warning restore CS8500

            // 主迴圈 Parallel.For（每像素獨立 FIR，無資料依賴）
            int wvYOff = kLeadPad - kWinY_half;
            int wvIOff = kLeadPad - kWinI_half;  // offset into cBuf for SVideo
            int tModIBase = ((phase0 - kWinI_half) % 6 + 6) % 6;
            int slC = sl; bool toCrtC = toCrt; int dstWC = dstW; int rowStartC = rowStart;

            Parallel.For(0, kOutW, p =>
            {
                unsafe
                {
#pragma warning disable CS8500
                    float* wvY = waveBuf + wvYOff + p;
                    float* wvI = cBuf    + wvIOff + p;
                    int tModI = (tModIBase + p) % 6;

                    float sumY = hannY[0]*wvY[0] + hannY[1]*wvY[1] + hannY[2]*wvY[2]
                               + hannY[3]*wvY[3] + hannY[4]*wvY[4] + hannY[5]*wvY[5];

                    float* cwI = combinedI + tModI * kWinI;
                    float  sumI;
                    {
                        int VS = Vector<float>.Count;
                        int n = 0;
                        var acc = new Vector<float>(0f);
                        for (; n <= kWinI - VS; n += VS)
                            acc += *(Vector<float>*)(cwI + n) * *(Vector<float>*)(wvI + n);
                        sumI = Vector.Dot(acc, new Vector<float>(1f));
                        for (; n < kWinI; n++) sumI += cwI[n] * wvI[n];
                    }

                    float Y = sumY;
                    float I = 2f * sumI;
                    float Q = demodQBuf[p >> 2];

                    if (toCrtC)
                        WriteLinear(slC, p, Y, I, Q);
                    else
                    {
                        uint pixel = YiqToRgb(Y, I, Q);
                        uint* outRow = _analogScreenBuf + rowStartC * dstWC;
                        if (dstWC == kOutW)
                            outRow[p] = pixel;
                        else
                            demodTmpBuf[p] = pixel;
                    }
#pragma warning restore CS8500
                }
            });

            if (!toCrt)
            {
                if (dstW != kOutW)
                {
                    int fpScale = (kOutW << 16) / dstW;
                    for (int x = 0; x < dstW; x++)
                        row0[x] = demodTmpBuf[(x * fpScale) >> 16];
                }
                for (int row = rowStart + 1; row < rowEnd; row++)
                    Buffer.MemoryCopy(row0, _analogScreenBuf + row * dstW,
                        dstW * sizeof(uint), dstW * sizeof(uint));
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // 共用：YIQ → Linear RGB（blargg -15° 矩陣，無 Gamma）
        // ════════════════════════════════════════════════════════════════════
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void WriteLinear(int sl, int p, float y, float i, float q)
        {
            float r = yiq_rY * y + yiq_rI * i + yiq_rQ * q;  // #16 color temp
            float g = yiq_gY * y + yiq_gI * i + yiq_gQ * q;
            float b = yiq_bY * y + yiq_bI * i + yiq_bQ * q;

            if (r < 0f) r = 0f; else if (r > 1f) r = 1f;
            if (g < 0f) g = 0f; else if (g > 1f) g = 1f;
            if (b < 0f) b = 0f; else if (b > 1f) b = 1f;

            int idx = sl * kOutW + p;
            linearBuffer[idx]            = r;
            linearBuffer[kPlane  + idx]  = g;
            linearBuffer[2*kPlane + idx] = b;
        }

        // Phase B：gammaLUT 取代 float gamma（Level 2 直接輸出路徑）
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint YiqToRgb(float y, float i, float q)
        {
            float r = yiq_rY * y + yiq_rI * i + yiq_rQ * q;  // #16 color temp
            float g = yiq_gY * y + yiq_gI * i + yiq_gQ * q;
            float b = yiq_bY * y + yiq_bI * i + yiq_bQ * q;

            int ri = (int)(r * 255.5f); if (ri < 0) ri = 0; else if (ri > 255) ri = 255;
            int gi = (int)(g * 255.5f); if (gi < 0) gi = 0; else if (gi > 255) gi = 255;
            int bi = (int)(b * 255.5f); if (bi < 0) bi = 0; else if (bi > 255) bi = 255;

            return (uint)(gammaLUT[bi] | ((uint)gammaLUT[gi] << 8) | ((uint)gammaLUT[ri] << 16) | 0xFF000000u);
        }
    }
}
