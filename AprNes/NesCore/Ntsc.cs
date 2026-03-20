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

    unsafe public static class Ntsc
    {
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
            switch (NesCore.AnalogOutput)
            {
                case NesCore.AnalogOutputMode.RF:
                    NoiseIntensity = RF_NoiseIntensity; SlewRate = RF_SlewRate; ChromaBlur = RF_ChromaBlur; break;
                case NesCore.AnalogOutputMode.SVideo:
                    NoiseIntensity = SV_NoiseIntensity; SlewRate = SV_SlewRate; ChromaBlur = SV_ChromaBlur; break;
                default:
                    NoiseIntensity = AV_NoiseIntensity; SlewRate = AV_SlewRate; ChromaBlur = AV_ChromaBlur; break;
            }
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
        // attenTab：emphasis bit count → attenuation（4 entries）
        static float* attenTab;

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

                // Phase B：Gamma LUT（256 bytes）
                for (int v = 0; v < 256; v++)
                {
                    float fv = v / 255.0f;
                    fv += 0.229f * fv * (fv - 1f);
                    int vi = (int)(fv * 255.5f);
                    gammaLUT[v] = (byte)(vi < 0 ? 0 : vi > 255 ? 255 : vi);
                }

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
            }

            scanPhase6    = 0;
            scanPhaseBase = 0;
            RfAudioLevel  = 0f;
            RfBuzzPhase   = 0f;
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
            int phase0 = scanPhase6;
            scanPhase6 = (scanPhase6 + 2) % 6;
            GenerateSignal(palBuf, emphasisBits);

            if (NesCore.AnalogOutput == NesCore.AnalogOutputMode.SVideo)
                DecodeAV_SVideo(sl);
            else
                DecodeAV_Composite(sl, phase0);
        }

        // Phase C：64-entry YIQ LUT + attenTab（取代分支+浮點計算）
        static void GenerateSignal(byte[] palBuf, byte emphasisBits)
        {
            int en = (emphasisBits & 1) + ((emphasisBits >> 1) & 1) + ((emphasisBits >> 2) & 1);
            float atten = attenTab[en];
            for (int d = 0; d < 256; d++)
            {
                int idx  = palBuf[d] & 63;
                dotY[d] = yBase[idx] * atten;
                dotI[d] = iBase[idx] * atten;
                dotQ[d] = qBase[idx] * atten;
            }
        }

        // Phase A：outX>>2（取代 *256/kOutW），ph 遞增取代 (phase0+outX)%6
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

            // IIR 初始狀態（以第 0 dot 初始化）
            int   ph0   = phase0;
            float c0    = cosTab6[ph0], s0 = sinTab6[ph0];
            float chr0  = dotI[0] * c0 - dotQ[0] * s0;
            float iFilt = chr0 * c0;
            float qFilt = -chr0 * s0;
            float yFilt = dotY[0];

            int dstW     = CrtScreen.DstW;
            int N        = NesCore.AnalogSize;   // dot index = outX / N
            int rowStart = sl * CrtScreen.DstH / CrtScreen.SrcH;
            int rowEnd   = (sl + 1) * CrtScreen.DstH / CrtScreen.SrcH;
            if (rowEnd > CrtScreen.DstH) rowEnd = CrtScreen.DstH;
            uint* row0 = NesCore.AnalogScreenBuf + rowStart * dstW;

            // Phase B：noise scale 移出迴圈
            float noiseScale  = addNoise ? (2f * NoiseIntensity / 255.0f) : 0f;
            float noiseOffset = addNoise ? NoiseIntensity : 0f;

            int ph = phase0;
            for (int outX = 0; outX < dstW; outX++)
            {
                int   d      = outX / N;           // dot index（依 AnalogSize 縮放）
                float c      = cosTab6[ph];
                float s      = sinTab6[ph];
                float chroma = dotI[d] * c - dotQ[d] * s;

                iFilt += ChromaBlur * (chroma * c  - iFilt);
                qFilt += ChromaBlur * (-chroma * s - qFilt);
                yFilt += SlewRate   * (dotY[d]     - yFilt);
                float y = yFilt;

                if (isRF) y += buzzRow;
                if (addNoise)
                {
                    ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5;
                    y += (ns & 0xFF) * noiseScale - noiseOffset;
                }

                row0[outX] = YiqToRgb(y, iFilt, qFilt);

                if (++ph == 6) ph = 0;
            }

            for (int row = rowStart + 1; row < rowEnd; row++)
                Buffer.MemoryCopy(row0, NesCore.AnalogScreenBuf + row * dstW,
                    dstW * sizeof(uint), dstW * sizeof(uint));
        }

        static unsafe void DecodeAV_SVideo(int sl)
        {
            float iFilt = dotI[0];
            float qFilt = dotQ[0];
            float yFilt = dotY[0];

            int dstW     = CrtScreen.DstW;
            int N        = NesCore.AnalogSize;
            int rowStart = sl * CrtScreen.DstH / CrtScreen.SrcH;
            int rowEnd   = (sl + 1) * CrtScreen.DstH / CrtScreen.SrcH;
            if (rowEnd > CrtScreen.DstH) rowEnd = CrtScreen.DstH;
            uint* row0 = NesCore.AnalogScreenBuf + rowStart * dstW;

            for (int outX = 0; outX < dstW; outX++)
            {
                int d = outX / N;
                iFilt += ChromaBlur * (dotI[d] - iFilt);
                qFilt += ChromaBlur * (dotQ[d] - qFilt);
                yFilt += SlewRate   * (dotY[d] - yFilt);
                row0[outX] = YiqToRgb(yFilt, iFilt, qFilt);
            }

            for (int row = rowStart + 1; row < rowEnd; row++)
                Buffer.MemoryCopy(row0, NesCore.AnalogScreenBuf + row * dstW,
                    dstW * sizeof(uint), dstW * sizeof(uint));
        }

        // ════════════════════════════════════════════════════════════════════
        // Level 3：物理路徑
        // ════════════════════════════════════════════════════════════════════

        static void DecodeScanline_Physical(int sl, byte[] palBuf, byte emphasisBits)
        {
            int phase0 = scanPhaseBase;
            scanPhaseBase = (scanPhaseBase + 2) % 6;

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

        // Phase C：waveTable LUT + Phase C：IIR 折疊（消去獨立 IIR pass）
        // Phase B：noise scale 移出迴圈
        static void GenerateWaveform(byte[] palBuf, byte emphasisBits,
                                      bool isRF, int sl, int phase0)
        {
            int   en       = (emphasisBits & 1) + ((emphasisBits >> 1) & 1) + ((emphasisBits >> 2) & 1);
            float atten    = attenTab[en];
            bool  addNoise = NoiseIntensity > 0f;

            float firstY = yBase[palBuf[0]   & 63] * atten;
            float lastY  = yBase[palBuf[255] & 63] * atten;

            float buzzRow = 0f;
            if (isRF)
            {
                float buzzAmp = RfAudioLevel * 0.06f;
                buzzRow = buzzAmp * (float)Math.Sin(
                              (sl / 240.0 + RfBuzzPhase) * 2.0 * Math.PI);
            }

            uint  ns         = 0u;
            float noiseScale = 0f, noiseOffset = 0f;
            if (addNoise)
            {
                ns          = (uint)(NesCore.frame_count * 1664525u + (uint)sl * 1013904223u + 1442695041u);
                noiseScale  = 2f * NoiseIntensity / 255.0f;
                noiseOffset = NoiseIntensity;
            }

            // 左邊填充
            for (int i = 0; i < kLeadPad; i++) waveBuf[i] = firstY;

            // Phase C：waveTable 查表 + Phase C：IIR 折疊 + buzz + noise 合併
            float vPrev = firstY;
            int   tMod  = phase0;
            for (int d = 0; d < kDots; d++)
            {
                float* src     = waveTable + ((palBuf[d] & 63) * 6 + tMod) * 4;
                int    baseIdx = kLeadPad + d * 4;
                for (int s = 0; s < 4; s++)
                {
                    float x = src[s] * atten;
                    if (isRF) x += buzzRow;
                    if (addNoise)
                    {
                        ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5;
                        x += (ns & 0xFF) * noiseScale - noiseOffset;
                    }
                    vPrev += SlewRate * (x - vPrev);
                    waveBuf[baseIdx + s] = vPrev;
                }
                tMod += 4; if (tMod >= 6) tMod -= 6;
            }

            // 右邊填充（IIR 繼續收斂至 lastY）
            for (int i = kLeadPad + kWaveLen; i < kBufLen; i++)
            {
                vPrev += SlewRate * (lastY - vPrev);
                waveBuf[i] = vPrev;
            }
        }

        // Phase C：yBase + cTable LUT + IIR 折疊
        static void GenerateWaveform_SVideo(byte[] palBuf, byte emphasisBits, int sl, int phase0)
        {
            int   en       = (emphasisBits & 1) + ((emphasisBits >> 1) & 1) + ((emphasisBits >> 2) & 1);
            float atten    = attenTab[en];
            bool  addNoise = NoiseIntensity > 0f;

            float firstY = yBase[palBuf[0] & 63] * atten;
            float lastY  = yBase[palBuf[255] & 63] * atten;

            uint  ns         = 0u;
            float noiseScale = 0f, noiseOffset = 0f;
            if (addNoise)
            {
                ns          = (uint)(NesCore.frame_count * 1664525u + (uint)sl * 1013904223u + 1442695041u);
                noiseScale  = 2f * NoiseIntensity / 255.0f;
                noiseOffset = NoiseIntensity;
            }

            // 左邊填充
            for (int i = 0; i < kLeadPad; i++) { waveBuf[i] = firstY; cBuf[i] = 0f; }

            // Phase C：Y line（SlewRate IIR 折疊）+ C line（cTable LUT，無 IIR）
            float vPrev = firstY;
            int   tMod  = phase0;
            for (int d = 0; d < kDots; d++)
            {
                float  Ytgt    = yBase[palBuf[d] & 63] * atten;
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
                    vPrev += SlewRate * (y - vPrev);
                    waveBuf[baseIdx + s] = vPrev;
                    cBuf  [baseIdx + s] = csrc[s] * atten;
                }
                tMod += 4; if (tMod >= 6) tMod -= 6;
            }

            // 右邊填充
            for (int i = kLeadPad + kWaveLen; i < kBufLen; i++)
            {
                vPrev += SlewRate * (lastY - vPrev);
                waveBuf[i] = vPrev;
                cBuf[i]    = 0f;
            }
        }

        // Phase A：滑動指標（消去 center 乘法）+ tModI 遞增（消去 double %6）
        // Phase D：Q@dot rate（256 點，4× MACs 節省）
        // Phase D：Y 6-tap 手動展開
        static unsafe void DemodulateRow(int sl, int phase0)
        {
            bool toCrt = NesCore.CrtEnabled;
            int dstW   = CrtScreen.DstW;

            int rowStart = sl * CrtScreen.DstH / CrtScreen.SrcH;
            int rowEnd   = (sl + 1) * CrtScreen.DstH / CrtScreen.SrcH;
            if (rowEnd > CrtScreen.DstH) rowEnd = CrtScreen.DstH;
            uint* row0 = NesCore.AnalogScreenBuf + rowStart * dstW;

            int VS = Vector<float>.Count;

#pragma warning disable CS8500
            // Phase D：預計算 Q@dot rate（256 點 × 54 MACs SIMD）
            float* dotQDemod = stackalloc float[256];
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
                    dotQDemod[d] = -2f * sumQ;
                    wvQ += kSampDot;
                    tModQ += 4; if (tModQ >= 6) tModQ -= 6;
                }
            }

            // 計算仍在 kOutW=1024 解析度（物理解調）；!toCrt 時 resample 到 dstW
            uint* tmpBuf = stackalloc uint[kOutW]; // 4KB，僅 !toCrt && dstW!=kOutW 時有效

            float* wvY  = waveBuf + kLeadPad - kWinY_half;
            float* wvI  = waveBuf + kLeadPad - kWinI_half;
            int tModI = ((phase0 - kWinI_half) % 6 + 6) % 6;

            for (int p = 0; p < kOutW; p++)
            {
                float sumY = hannY[0]*wvY[0] + hannY[1]*wvY[1] + hannY[2]*wvY[2]
                           + hannY[3]*wvY[3] + hannY[4]*wvY[4] + hannY[5]*wvY[5];

                float* cwI = combinedI + tModI * kWinI;
                float  sumI;
                {
                    int n = 0;
                    var acc = new Vector<float>(0f);
                    for (; n <= kWinI - VS; n += VS)
                        acc += *(Vector<float>*)(cwI + n) * *(Vector<float>*)(wvI + n);
                    sumI = Vector.Dot(acc, new Vector<float>(1f));
                    for (; n < kWinI; n++) sumI += cwI[n] * wvI[n];
                }

                float Y = sumY;
                float I = 2f * sumI;
                float Q = dotQDemod[p >> 2];

                if (toCrt)
                    WriteLinear(sl, p, Y, I, Q);
                else if (dstW == kOutW)
                    row0[p] = YiqToRgb(Y, I, Q);       // N=4：直接寫
                else
                    tmpBuf[p] = YiqToRgb(Y, I, Q);     // 其他：暫存後 resample

                wvY++; wvI++;
                if (++tModI == 6) tModI = 0;
            }
#pragma warning restore CS8500

            if (!toCrt)
            {
                if (dstW != kOutW)
                {
                    // nearest-neighbor resample kOutW → dstW
                    int fpScale = (kOutW << 16) / dstW;
                    for (int x = 0; x < dstW; x++)
                        row0[x] = tmpBuf[(x * fpScale) >> 16];
                }
                for (int row = rowStart + 1; row < rowEnd; row++)
                    Buffer.MemoryCopy(row0, NesCore.AnalogScreenBuf + row * dstW,
                        dstW * sizeof(uint), dstW * sizeof(uint));
            }
        }

        // DemodulateRow_SVideo：同 DemodulateRow，I/Q 來源改為 cBuf
        static unsafe void DemodulateRow_SVideo(int sl, int phase0)
        {
            bool toCrt = NesCore.CrtEnabled;
            int dstW   = CrtScreen.DstW;

            int rowStart = sl * CrtScreen.DstH / CrtScreen.SrcH;
            int rowEnd   = (sl + 1) * CrtScreen.DstH / CrtScreen.SrcH;
            if (rowEnd > CrtScreen.DstH) rowEnd = CrtScreen.DstH;
            uint* row0 = NesCore.AnalogScreenBuf + rowStart * dstW;

            int VS = Vector<float>.Count;

#pragma warning disable CS8500
            float* dotQDemod = stackalloc float[256];
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
                    dotQDemod[d] = -2f * sumQ;
                    wvQ += kSampDot;
                    tModQ += 4; if (tModQ >= 6) tModQ -= 6;
                }
            }

            uint* tmpBuf = stackalloc uint[kOutW];

            float* wvY = waveBuf + kLeadPad - kWinY_half;
            float* wvI = cBuf    + kLeadPad - kWinI_half;
            int tModI = ((phase0 - kWinI_half) % 6 + 6) % 6;

            for (int p = 0; p < kOutW; p++)
            {
                float sumY = hannY[0]*wvY[0] + hannY[1]*wvY[1] + hannY[2]*wvY[2]
                           + hannY[3]*wvY[3] + hannY[4]*wvY[4] + hannY[5]*wvY[5];

                float* cwI = combinedI + tModI * kWinI;
                float  sumI;
                {
                    int n = 0;
                    var acc = new Vector<float>(0f);
                    for (; n <= kWinI - VS; n += VS)
                        acc += *(Vector<float>*)(cwI + n) * *(Vector<float>*)(wvI + n);
                    sumI = Vector.Dot(acc, new Vector<float>(1f));
                    for (; n < kWinI; n++) sumI += cwI[n] * wvI[n];
                }

                float Y = sumY;
                float I = 2f * sumI;
                float Q = dotQDemod[p >> 2];

                if (toCrt)
                    WriteLinear(sl, p, Y, I, Q);
                else if (dstW == kOutW)
                    row0[p] = YiqToRgb(Y, I, Q);
                else
                    tmpBuf[p] = YiqToRgb(Y, I, Q);

                wvY++; wvI++;
                if (++tModI == 6) tModI = 0;
            }
#pragma warning restore CS8500

            if (!toCrt)
            {
                if (dstW != kOutW)
                {
                    int fpScale = (kOutW << 16) / dstW;
                    for (int x = 0; x < dstW; x++)
                        row0[x] = tmpBuf[(x * fpScale) >> 16];
                }
                for (int row = rowStart + 1; row < rowEnd; row++)
                    Buffer.MemoryCopy(row0, NesCore.AnalogScreenBuf + row * dstW,
                        dstW * sizeof(uint), dstW * sizeof(uint));
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // 共用：YIQ → Linear RGB（blargg -15° 矩陣，無 Gamma）
        // ════════════════════════════════════════════════════════════════════
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

        // Phase B：gammaLUT 取代 float gamma（Level 2 直接輸出路徑）
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint YiqToRgb(float y, float i, float q)
        {
            float r = y + 1.0841f * i + 0.3523f * q;
            float g = y - 0.4302f * i - 0.5547f * q;
            float b = y - 0.6268f * i + 1.9299f * q;

            int ri = (int)(r * 255.5f); if (ri < 0) ri = 0; else if (ri > 255) ri = 255;
            int gi = (int)(g * 255.5f); if (gi < 0) gi = 0; else if (gi > 255) gi = 255;
            int bi = (int)(b * 255.5f); if (bi < 0) bi = 0; else if (bi > 255) bi = 255;

            return (uint)(gammaLUT[bi] | ((uint)gammaLUT[gi] << 8) | ((uint)gammaLUT[ri] << 16) | 0xFF000000u);
        }
    }
}
