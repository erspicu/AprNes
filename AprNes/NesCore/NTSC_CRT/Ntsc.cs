using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AprNes
{
    // ============================================================
    // NES NTSC 訊號解碼器 - .NET 4.8.1 究極暴力攤平無分支版
    // ============================================================
    public enum AnalogOutputMode { AV = 0, SVideo = 1, RF = 2 }

    unsafe public partial class NesCore
    {
        public static int UpscaleMode = 1;

        // ── 解耦參數 ────────────────────────
        static int ntsc_analogOutput;
        static bool ntsc_ultraAnalog;
        static int ntsc_analogSize;
        static bool ntsc_crtEnabled;
        static uint* ntsc_analogScreenBuf;
        static int ntsc_frameCount;

        public static void Ntsc_ApplyConfig(int analogOutput, bool ultraAnalog, int analogSize,
                                        bool crtEnabled, uint* analogScreenBuf)
        {
            ntsc_analogOutput = analogOutput;
            ntsc_ultraAnalog = ultraAnalog;
            ntsc_analogSize = analogSize;
            ntsc_crtEnabled = crtEnabled;
            ntsc_analogScreenBuf = analogScreenBuf;
            Ntsc_ApplyProfile();
        }

        public static void Ntsc_SetFrameCount(int fc) => ntsc_frameCount = fc;

        // ── 共用唯讀查表與參數 (Thread-Safe) ─────────────────
        static float* loLevels; static float* hiLevels;
        static float* iPhase; static float* qPhase;
        static float* cosTab6; static float* sinTab6;
        static float* hannY; static float* hannI; static float* hannQ;
        static float* combinedI; static float* combinedQ;
        public static byte* gammaLUT;
        static float* yBase; static float* iBase; static float* qBase;
        static float* waveTable; static float* cTable;
        static float* attenTab; static float* emphAtten;
        static float* yBaseE; static float* iBaseE; static float* qBaseE;

        public static float RfAudioLevel = 0.0f;
        public static float RfBuzzPhase = 0.0f;

        public const int kOutW = 1024;
        public const int kSrcH = 240;
        public const int kPlane = kOutW * kSrcH;
        public static float* linearBuffer;

        public static float RF_NoiseIntensity = 0.04f;
        public static float RF_SlewRate = 0.60f;
        public static float RF_ChromaBlur = 0.10f;
        public static float AV_NoiseIntensity = 0.003f;
        public static float AV_SlewRate = 0.80f;
        public static float AV_ChromaBlur = 0.35f;
        public static float SV_NoiseIntensity = 0.00f;
        public static float SV_SlewRate = 0.90f;
        public static float SV_ChromaBlur = 0.45f;

        static float NoiseIntensity; static float SlewRate; static float ChromaBlur;

        static void Ntsc_ApplyProfile()
        {
            if (ntsc_analogOutput == (int)AnalogOutputMode.RF)
            { NoiseIntensity = RF_NoiseIntensity; SlewRate = RF_SlewRate; ChromaBlur = RF_ChromaBlur; }
            else if (ntsc_analogOutput == (int)AnalogOutputMode.SVideo)
            { NoiseIntensity = SV_NoiseIntensity; SlewRate = SV_SlewRate; ChromaBlur = SV_ChromaBlur; }
            else
            { NoiseIntensity = AV_NoiseIntensity; SlewRate = AV_SlewRate; ChromaBlur = AV_ChromaBlur; }
        }

        static int scanPhase6 = 0;
        static int scanPhaseBase = 0;

        const int kDots = 256;
        const int kSampDot = 4;
        const int kWaveLen = kDots * kSampDot;
        const int kLeadPad = 30;
        const int kBufLen = kLeadPad * 2 + kWaveLen;

        // ── SIMD 常數向量 ────────────────────────────
        static Vector<float> vRY, vRI, vRQ;
        static Vector<float> vGY, vGI, vGQ;
        static Vector<float> vBY, vBI, vBQ;
        static Vector<float> vGC;
        static Vector<float> v1_minus_GC;
        static readonly Vector<float> vOneN = new Vector<float>(1f);
        static readonly Vector<float> vZeroN = new Vector<float>(0f);
        static readonly Vector<float> v255_0N = new Vector<float>(255.0f);
        static readonly Vector<int> v255iN = new Vector<int>(255);
        static readonly Vector<int> vZeroiN = new Vector<int>(0);
        static readonly Vector<int> v256iN = new Vector<int>(256);
        static readonly Vector<int> v65536iN = new Vector<int>(65536);
        static readonly Vector<int> vAlphaiN = new Vector<int>(unchecked((int)0xFF000000));

        const int kWinY = 6; const int kWinY_half = kWinY / 2;
        const int kWinI = 18; const int kWinI_half = kWinI / 2;
        const int kWinQ = 54; const int kWinQ_half = kWinQ / 2;

        public static float ColorTempR = 1.0f;
        public static float ColorTempG = 1.0f;
        public static float ColorTempB = 1.0f;
        static float yiq_rY = 1.0f, yiq_rI = 1.0841f, yiq_rQ = 0.3523f;
        static float yiq_gY = 1.0f, yiq_gI = -0.4302f, yiq_gQ = -0.5547f;
        static float yiq_bY = 1.0f, yiq_bI = -0.6268f, yiq_bQ = 1.9299f;
        // YiqToRgb 專用：預乘 255.5 倍，省去每次呼叫的 3 次乘法
        static float yiq_rY_255, yiq_rI_255, yiq_rQ_255;
        static float yiq_gY_255, yiq_gI_255, yiq_gQ_255;
        static float yiq_bY_255, yiq_bI_255, yiq_bQ_255;

        public static float GammaCoeff = 0.229f;
        public static float GammaCoeffInv = 1f - 0.229f; // 1 - GC，Gamma 代數提取用
        public static float RingStrength = 0.3f;
        public static bool HbiSimulation = true;
        public static bool ColorBurstJitter = true;

        public static void Ntsc_Init()
        {
            if (loLevels == null)
            {
                loLevels = (float*)Marshal.AllocHGlobal(4 * sizeof(float));
                loLevels[0] = -0.12f; loLevels[1] = 0.00f; loLevels[2] = 0.31f; loLevels[3] = 0.72f;
                hiLevels = (float*)Marshal.AllocHGlobal(4 * sizeof(float));
                hiLevels[0] = 0.40f; hiLevels[1] = 0.68f; hiLevels[2] = 1.00f; hiLevels[3] = 1.00f;
                iPhase = (float*)Marshal.AllocHGlobal(16 * sizeof(float));
                qPhase = (float*)Marshal.AllocHGlobal(16 * sizeof(float));
                linearBuffer = (float*)Marshal.AllocHGlobal(kOutW * kSrcH * 3 * sizeof(float));
                cosTab6 = (float*)Marshal.AllocHGlobal(6 * sizeof(float));
                sinTab6 = (float*)Marshal.AllocHGlobal(6 * sizeof(float));
                hannY = (float*)Marshal.AllocHGlobal(kWinY * sizeof(float));
                hannI = (float*)Marshal.AllocHGlobal(kWinI * sizeof(float));
                hannQ = (float*)Marshal.AllocHGlobal(kWinQ * sizeof(float));
                combinedI = (float*)Marshal.AllocHGlobal(6 * kWinI * sizeof(float));
                combinedQ = (float*)Marshal.AllocHGlobal(6 * kWinQ * sizeof(float));
                gammaLUT = (byte*)Marshal.AllocHGlobal(4096);
                attenTab = (float*)Marshal.AllocHGlobal(4 * sizeof(float));
                yBase = (float*)Marshal.AllocHGlobal(64 * sizeof(float));
                iBase = (float*)Marshal.AllocHGlobal(64 * sizeof(float));
                qBase = (float*)Marshal.AllocHGlobal(64 * sizeof(float));
                waveTable = (float*)Marshal.AllocHGlobal(64 * 6 * 4 * sizeof(float));
                cTable = (float*)Marshal.AllocHGlobal(64 * 6 * 4 * sizeof(float));
                emphAtten = (float*)Marshal.AllocHGlobal(8 * 12 * sizeof(float));
                yBaseE = (float*)Marshal.AllocHGlobal(64 * 8 * sizeof(float));
                iBaseE = (float*)Marshal.AllocHGlobal(64 * 8 * sizeof(float));
                qBaseE = (float*)Marshal.AllocHGlobal(64 * 8 * sizeof(float));

                for (int c = 0; c < 16; c++) { double a = c * Math.PI / 6.0; iPhase[c] = -(float)Math.Cos(a); qPhase[c] = (float)Math.Sin(a); }
                for (int k = 0; k < 6; k++) { double a = k * 2.0 * Math.PI / 6.0; cosTab6[k] = (float)Math.Cos(a); sinTab6[k] = (float)Math.Sin(a); }

                ComputeHann(hannY, kWinY); ComputeHann(hannI, kWinI); ComputeHann(hannQ, kWinQ);

                for (int ph = 0; ph < 6; ph++)
                {
                    for (int n = 0; n < kWinI; n++) combinedI[ph * kWinI + n] = hannI[n] * cosTab6[(ph + n) % 6];
                    for (int n = 0; n < kWinQ; n++) combinedQ[ph * kWinQ + n] = hannQ[n] * sinTab6[(ph + n) % 6];
                }
                attenTab[0] = 1.0f; for (int n = 1; n <= 3; n++) attenTab[n] = (float)Math.Pow(0.746, n);
                for (int p = 0; p < 64; p++)
                {
                    int luma = (p >> 4) & 3; int color = p & 0x0F;
                    float lo = loLevels[luma], hi = hiLevels[luma];
                    if (color == 0) lo = hi; else if (color == 0x0D) hi = lo; else if (color > 0x0D) lo = hi = 0f;
                    float sat = (hi - lo) * 0.5f; yBase[p] = (hi + lo) * 0.5f;
                    if (color >= 1 && color <= 12) { iBase[p] = iPhase[color] * sat; qBase[p] = qPhase[color] * sat; }
                    else { iBase[p] = 0f; qBase[p] = 0f; }
                }
                for (int p = 0; p < 64; p++)
                {
                    for (int ph = 0; ph < 6; ph++)
                    {
                        float* wdst = waveTable + (p * 6 + ph) * 4; float* cdst = cTable + (p * 6 + ph) * 4;
                        for (int s = 0; s < 4; s++)
                        {
                            int tm = (ph + s) % 6; cdst[s] = cosTab6[tm] * iBase[p] - sinTab6[tm] * qBase[p]; wdst[s] = yBase[p] + cdst[s];
                        }
                    }
                }
                for (int e = 0; e < 8; e++)
                {
                    for (int p = 0; p < 6; p++)
                    {
                        int cnt = 0; if ((e & 1) != 0 && p >= 1 && p <= 3) cnt++; if ((e & 2) != 0 && p >= 3 && p <= 5) cnt++; if ((e & 4) != 0 && (p >= 5 || p <= 1)) cnt++;
                        emphAtten[e * 12 + p] = (float)Math.Pow(0.746, cnt);
                    }
                    for (int p = 0; p < 6; p++) emphAtten[e * 12 + 6 + p] = emphAtten[e * 12 + p];
                }
                for (int p = 0; p < 64; p++)
                {
                    for (int e = 0; e < 8; e++)
                    {
                        float sumY = 0f, sumI = 0f, sumQ = 0f;
                        for (int ph = 0; ph < 6; ph++)
                        {
                            float V = yBase[p] + iBase[p] * cosTab6[ph] - qBase[p] * sinTab6[ph];
                            V *= emphAtten[e * 12 + ph]; sumY += V; sumI += V * cosTab6[ph]; sumQ -= V * sinTab6[ph];
                        }
                        yBaseE[p * 8 + e] = sumY / 6f; iBaseE[p * 8 + e] = sumI / 3f; qBaseE[p * 8 + e] = sumQ / 3f;
                    }
                }
            }

            UpdateColorTemp();
            UpdateGammaLUT();
            scanPhase6 = 0;
            scanPhaseBase = 0;
            RfAudioLevel = 0f;
            RfBuzzPhase = 0f;
        }

        public static void UpdateColorTemp()
        {
            yiq_rY = 1.0f * ColorTempR; yiq_rI = 1.0841f * ColorTempR; yiq_rQ = 0.3523f * ColorTempR;
            yiq_gY = 1.0f * ColorTempG; yiq_gI = -0.4302f * ColorTempG; yiq_gQ = -0.5547f * ColorTempG;
            yiq_bY = 1.0f * ColorTempB; yiq_bI = -0.6268f * ColorTempB; yiq_bQ = 1.9299f * ColorTempB;
            vRY = new Vector<float>(yiq_rY); vRI = new Vector<float>(yiq_rI); vRQ = new Vector<float>(yiq_rQ);
            vGY = new Vector<float>(yiq_gY); vGI = new Vector<float>(yiq_gI); vGQ = new Vector<float>(yiq_gQ);
            vBY = new Vector<float>(yiq_bY); vBI = new Vector<float>(yiq_bI); vBQ = new Vector<float>(yiq_bQ);
            // YiqToRgb 專用：預乘 255.5
            yiq_rY_255 = yiq_rY * 255.5f; yiq_rI_255 = yiq_rI * 255.5f; yiq_rQ_255 = yiq_rQ * 255.5f;
            yiq_gY_255 = yiq_gY * 255.5f; yiq_gI_255 = yiq_gI * 255.5f; yiq_gQ_255 = yiq_gQ * 255.5f;
            yiq_bY_255 = yiq_bY * 255.5f; yiq_bI_255 = yiq_bI * 255.5f; yiq_bQ_255 = yiq_bQ * 255.5f;
        }

        public static void UpdateGammaLUT()
        {
            if (gammaLUT == null) return;
            float gc = GammaCoeff; float inv255 = 1.0f / 255.0f;
            for (int i = 0; i < 4096; i++)
            {
                int v = (i >= 2048) ? i - 4096 : i;
                if (v < 0) gammaLUT[i] = 0;
                else if (v > 255) gammaLUT[i] = 255;
                else
                {
                    float fv = v * inv255;
                    fv += gc * fv * (fv - 1f);
                    int vi = (int)(fv * 255.5f);
                    gammaLUT[i] = (byte)(vi < 0 ? 0 : (vi > 255 ? 255 : vi));
                }
            }
            GammaCoeffInv = 1f - gc;
            vGC = new Vector<float>(gc);
            v1_minus_GC = new Vector<float>(1f - gc);
        }

        static void ComputeHann(float* w, int N)
        {
            if (N <= 1) { if (N == 1) w[0] = 1f; return; }
            double phaseStep = 2.0 * Math.PI / (N - 1);
            int half = (N + 1) / 2;
            for (int n = 0; n < half; n++)
            {
                float val = (float)(0.5 * (1.0 - Math.Cos(phaseStep * n)));
                w[n] = val; w[N - 1 - n] = val;
            }
            double sum = 0.0;
            for (int n = 0; n < N; n++) sum += w[n];
            float inv = (float)(1.0 / sum);
            for (int n = 0; n < N; n++) w[n] *= inv;
        }

        // ★ 無分支 Bilinear (Loop Peeling)
        static void ResampleH_Bilinear(uint* src, int srcW, uint* dst, int dstW)
        {
            int fpScale = (srcW << 16) / dstW;
            int limit = dstW - 1;
            for (int x = 0; x < limit; x++)
            {
                int fp = x * fpScale; int sx = fp >> 16; uint frac = (uint)((fp >> 8) & 0xFF);
                uint nf = 256 - frac; uint c0 = src[sx], c1 = src[sx + 1];
                uint c0_RB = c0 & 0x00FF00FFu, c1_RB = c1 & 0x00FF00FFu;
                uint res_RB = ((c0_RB * nf + c1_RB * frac) >> 8) & 0x00FF00FFu;
                uint c0_G = c0 & 0x0000FF00u, c1_G = c1 & 0x0000FF00u;
                uint res_G = ((c0_G * nf + c1_G * frac) >> 8) & 0x0000FF00u;
                dst[x] = 0xFF000000u | res_RB | res_G;
            }
            dst[limit] = src[(limit * fpScale) >> 16];
        }

        // ★ 無分支垂直填充 (Fixed-point increment)
        static void VerticalFillRows(int sl, int dstW, uint* row0, int rowStart, int rowEnd)
        {
            if (UpscaleMode == 1 && sl > 0)
            {
                int prevRowStart = (sl - 1) * Crt_DstH / Crt_SrcH;
                int span = rowStart - prevRowStart;
                if (span > 1)
                {
                    uint* prevRow = ntsc_analogScreenBuf + (long)prevRowStart * dstW;
                    uint* dstRowBase = ntsc_analogScreenBuf + (long)(prevRowStart + 1) * dstW;
                    uint tStepFixed = 16777216u / (uint)span; uint tFixed = tStepFixed;
                    for (int r = prevRowStart + 1; r < rowStart; r++)
                    {
                        uint t256 = tFixed >> 16; uint nt = 256 - t256; tFixed += tStepFixed;
                        for (int x = 0; x < dstW; x++)
                        {
                            uint c0 = prevRow[x], c1 = row0[x];
                            uint c0_RB = c0 & 0x00FF00FFu, c1_RB = c1 & 0x00FF00FFu;
                            uint res_RB = ((c0_RB * nt + c1_RB * t256) >> 8) & 0x00FF00FFu;
                            uint c0_G = c0 & 0x0000FF00u, c1_G = c1 & 0x0000FF00u;
                            uint res_G = ((c0_G * nt + c1_G * t256) >> 8) & 0x0000FF00u;
                            dstRowBase[x] = 0xFF000000u | res_RB | res_G;
                        }
                        dstRowBase += dstW;
                    }
                }
            }
            int rowCount = rowEnd - (rowStart + 1);
            if (rowCount > 0)
            {
                long bpr = (long)dstW * sizeof(uint);
                uint* target = ntsc_analogScreenBuf + (long)(rowStart + 1) * dstW;
                for (int i = 0; i < rowCount; i++) { Buffer.MemoryCopy(row0, target, bpr, bpr); target += dstW; }
            }
        }

        public static void DecodeScanline(int sl, byte[] palBuf, byte emphasisBits)
        {
            if (sl < 0 || sl >= kSrcH) return;
            if (ntsc_ultraAnalog)
            {
                float* waveBuf = stackalloc float[kBufLen]; float* cBuf = stackalloc float[kBufLen];
                DecodeScanline_Physical(sl, palBuf, emphasisBits, waveBuf, cBuf);
            }
            else
            {
                float* dotY = stackalloc float[256]; float* dotI = stackalloc float[256]; float* dotQ = stackalloc float[256];
                DecodeScanline_Fast(sl, palBuf, emphasisBits, dotY, dotI, dotQ);
            }
        }

        static void DecodeScanline_Fast(int sl, byte[] palBuf, byte emphasisBits, float* dotY, float* dotI, float* dotQ)
        {
            int phase0 = scanPhase6;

            // ★ 符號位元擴展黑魔法
            scanPhase6 += 2;
            scanPhase6 += ((5 - scanPhase6) >> 31) & -6;

            GenerateSignal(palBuf, emphasisBits, dotY, dotI, dotQ);
            if ((AnalogOutputMode)ntsc_analogOutput == AnalogOutputMode.SVideo) DecodeAV_SVideo(sl, dotY, dotI, dotQ);
            else DecodeAV_Composite(sl, phase0, dotY, dotI, dotQ);
        }

        static void GenerateSignal(byte[] palBuf, byte emphasisBits, float* dotY, float* dotI, float* dotQ)
        {
            int emph = emphasisBits & 7;
            for (int d = 0; d < 256; d++)
            {
                int k = (palBuf[d] & 63) * 8 + emph;
                dotY[d] = yBaseE[k]; dotI[d] = iBaseE[k]; dotQ[d] = qBaseE[k];
            }
        }

        static void DecodeAV_Composite(int sl, int phase0, float* dotY, float* dotI, float* dotQ)
        {
            bool isRF = (AnalogOutputMode)ntsc_analogOutput == AnalogOutputMode.RF;
            bool addNoise = NoiseIntensity > 0f; float ringDamp = RingStrength * 0.5f;
            float nScale = NoiseIntensity * (2f / 255.0f); float nOff = NoiseIntensity;
            int dstW = Crt_DstW; int N = ntsc_analogSize;
            int rowStart = sl * Crt_DstH / Crt_SrcH;
            int rowEnd = Math.Min((sl + 1) * Crt_DstH / Crt_SrcH, Crt_DstH);
            uint* row0 = ntsc_analogScreenBuf + (long)rowStart * dstW;

            float c0 = cosTab6[phase0], s0 = sinTab6[phase0]; float chr0 = dotI[0] * c0 - dotQ[0] * s0;
            float iFilt = HbiSimulation ? 0f : chr0 * c0; float qFilt = HbiSimulation ? 0f : -chr0 * s0;
            float yFilt = HbiSimulation ? 0f : dotY[0]; float yVel = 0f;

            bool herring = false; float hR = 0f, hI = 0f, hC = 1f, hS = 0f;
            if (isRF)
            {
                float buzz = RfAudioLevel * 0.06f;
                float env = buzz * (float)Math.Sin((sl / 240.0 + RfBuzzPhase) * 2.0 * Math.PI);
                if (env > 0.0001f || env < -0.0001f)
                {
                    herring = true; float rads = 1.31683f * 1024f / dstW; hC = (float)Math.Cos(rads); hS = (float)Math.Sin(rads);
                    float lPh = sl * 1364f * 1.31683f; hR = env * (float)Math.Cos(lPh); hI = env * (float)Math.Sin(lPh);
                }
            }
            uint ns = (addNoise || herring) ? (uint)(ntsc_frameCount * 1664525u + (uint)sl * 1013904223u + 1442695041u) : 0u;

            // ★ Code Splitting: 分支外提
            if (!addNoise && !herring) RunDecodeLoop(dstW, N, row0, dotY, dotI, dotQ, phase0, ref iFilt, ref qFilt, ref yFilt, ref yVel, ringDamp, false, false, 0, 0, 0, 0, 0, 0, 0);
            else if (addNoise && !herring) RunDecodeLoop(dstW, N, row0, dotY, dotI, dotQ, phase0, ref iFilt, ref qFilt, ref yFilt, ref yVel, ringDamp, true, false, ns, nScale, nOff, 0, 0, 0, 0);
            else if (!addNoise && herring) RunDecodeLoop(dstW, N, row0, dotY, dotI, dotQ, phase0, ref iFilt, ref qFilt, ref yFilt, ref yVel, ringDamp, false, true, 0, 0, 0, hR, hI, hC, hS);
            else RunDecodeLoop(dstW, N, row0, dotY, dotI, dotQ, phase0, ref iFilt, ref qFilt, ref yFilt, ref yVel, ringDamp, true, true, ns, nScale, nOff, hR, hI, hC, hS);

            VerticalFillRows(sl, dstW, row0, rowStart, rowEnd);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RunDecodeLoop(int dstW, int N, uint* row0, float* dotY, float* dotI, float* dotQ, int phStart,
            ref float iFilt, ref float qFilt, ref float yFilt, ref float yVel, float ringDamp,
            bool addNoise, bool herring, uint ns, float nScale, float nOff, float hR, float hI, float hC, float hS)
        {
            int ph = phStart; float iF = iFilt, qF = qFilt, yF = yFilt, yV = yVel; float hRl = hR, hIl = hI;
            for (int x = 0; x < dstW; x++)
            {
                int d = x / N; float c = cosTab6[ph], s = sinTab6[ph];
                float chroma = dotI[d] * c - dotQ[d] * s;
                iF += ChromaBlur * (chroma * c - iF); qF += ChromaBlur * (-chroma * s - qF);
                yV = yV * ringDamp + (dotY[d] - yF) * SlewRate; yF += yV; float y = yF;

                if (herring) { y += hIl; float t = hRl * hC - hIl * hS; hIl = hRl * hS + hIl * hC; hRl = t; }
                if (addNoise) { ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5; y += (ns & 0xFF) * nScale - nOff; }

                row0[x] = YiqToRgb(y, iF, qF);

                // ★ 符號位元擴展黑魔法
                ph++;
                ph += ((5 - ph) >> 31) & -6;
            }
            iFilt = iF; qFilt = qF; yFilt = yF; yVel = yV;
        }

        static void DecodeAV_SVideo(int sl, float* dotY, float* dotI, float* dotQ)
        {
            float iFilt = HbiSimulation ? 0f : dotI[0]; float qFilt = HbiSimulation ? 0f : dotQ[0]; float yFilt = HbiSimulation ? 0f : dotY[0];
            int dstW = Crt_DstW; int N = ntsc_analogSize;
            int rowStart = sl * Crt_DstH / Crt_SrcH; int rowEnd = Math.Min((sl + 1) * Crt_DstH / Crt_SrcH, Crt_DstH);
            uint* row0 = ntsc_analogScreenBuf + (long)rowStart * dstW;

            for (int outX = 0; outX < dstW; outX++)
            {
                int d = outX / N;
                iFilt += ChromaBlur * (dotI[d] - iFilt); qFilt += ChromaBlur * (dotQ[d] - qFilt); yFilt += SlewRate * (dotY[d] - yFilt);
                row0[outX] = YiqToRgb(yFilt, iFilt, qFilt);
            }
            VerticalFillRows(sl, dstW, row0, rowStart, rowEnd);
        }

        static void DecodeScanline_Physical(int sl, byte[] palBuf, byte emphasisBits, float* waveBuf, float* cBuf)
        {
            int phase0 = scanPhaseBase;

            // ★ 符號位元擴展黑魔法
            scanPhaseBase += 2;
            scanPhaseBase += ((5 - scanPhaseBase) >> 31) & -6;

            if (ColorBurstJitter && (AnalogOutputMode)ntsc_analogOutput == AnalogOutputMode.RF)
            {
                uint jns = (uint)(ntsc_frameCount * 2654435761u + (uint)sl * 340573321u);
                jns ^= jns << 13; jns ^= jns >> 17; jns ^= jns << 5;
                if ((jns & 31) == 0) phase0 = (phase0 + ((jns & 64) != 0 ? 1 : 5)) % 6;
            }
            if ((AnalogOutputMode)ntsc_analogOutput == AnalogOutputMode.SVideo)
            {
                GenerateWaveform_SVideo(palBuf, emphasisBits, sl, phase0, waveBuf, cBuf);
                DemodulateRow_SVideo(sl, phase0, waveBuf, cBuf);
            }
            else
            {
                bool isRF = (AnalogOutputMode)ntsc_analogOutput == AnalogOutputMode.RF;
                GenerateWaveform(palBuf, emphasisBits, isRF, sl, phase0, waveBuf);
                DemodulateRow(sl, phase0, waveBuf);
            }
        }

        static void GenerateWaveform(byte[] palBuf, byte emphasisBits, bool isRF, int sl, int phase0, float* waveBuf)
        {
            int emph = emphasisBits & 7; float* ea = emphAtten + emph * 12;
            bool addNoise = NoiseIntensity > 0f;
            float firstY = yBaseE[(palBuf[0] & 63) * 8 + emph]; float lastY = yBaseE[(palBuf[255] & 63) * 8 + emph];

            float hR_buzz = 0f, hI_buzz = 0f, hC_buzz = 1f, hS_buzz = 0f; bool herring = false;
            if (isRF)
            {
                float buzz = RfAudioLevel * 0.06f; float env = buzz * (float)Math.Sin((sl / 240.0 + RfBuzzPhase) * 2.0 * Math.PI);
                if (env > 0.0001f || env < -0.0001f)
                {
                    herring = true; hC_buzz = (float)Math.Cos(1.31683f); hS_buzz = (float)Math.Sin(1.31683f);
                    float lPh = sl * 1364f * 1.31683f; hR_buzz = env * (float)Math.Cos(lPh); hI_buzz = env * (float)Math.Sin(lPh);
                }
            }
            uint ns = addNoise ? (uint)(ntsc_frameCount * 1664525u + (uint)sl * 1013904223u + 1442695041u) : 0u;
            float nScale = 2f * NoiseIntensity / 255.0f, nOff = NoiseIntensity;

            float leftPad = HbiSimulation ? 0.0f : firstY;
            for (int i = 0; i < kLeadPad; i++) waveBuf[i] = leftPad;

            // ★ Code Splitting: 分支外提波形生成
            if (!addNoise && !herring) RunWaveformLoop(palBuf, ea, waveBuf, phase0, leftPad, lastY, false, false, 0, 0, 0, 0, 0, 0, 0);
            else if (addNoise && !herring) RunWaveformLoop(palBuf, ea, waveBuf, phase0, leftPad, lastY, true, false, ns, nScale, nOff, 0, 0, 0, 0);
            else if (!addNoise && herring) RunWaveformLoop(palBuf, ea, waveBuf, phase0, leftPad, lastY, false, true, 0, 0, 0, hR_buzz, hI_buzz, hC_buzz, hS_buzz);
            else RunWaveformLoop(palBuf, ea, waveBuf, phase0, leftPad, lastY, true, true, ns, nScale, nOff, hR_buzz, hI_buzz, hC_buzz, hS_buzz);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RunWaveformLoop(byte[] palBuf, float* ea, float* waveBuf, int phase0,
            float leftPad, float lastY, bool addNoise, bool herring, uint ns, float nScale, float nOff, float hR, float hI, float hC, float hS)
        {
            float vPrev = leftPad; float ringDamp = RingStrength * 0.5f; float vVel = 0f; int tMod = phase0;
            float hRl = hR, hIl = hI;

            for (int d = 0; d < kDots; d++)
            {
                float* src = waveTable + ((palBuf[d] & 63) * 6 + tMod) * 4;
                int baseIdx = kLeadPad + d * 4;

                // ★ 暴力攤平: 完全展開 s=0~3

                // --- s = 0 ---
                float x0 = src[0] * ea[tMod];
                if (herring) { x0 += hIl; float t = hRl * hC - hIl * hS; hIl = hRl * hS + hIl * hC; hRl = t; }
                if (addNoise) { ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5; x0 += (ns & 0xFF) * nScale - nOff; }
                vVel = vVel * ringDamp + (x0 - vPrev) * SlewRate; vPrev += vVel;
                waveBuf[baseIdx] = vPrev;

                // --- s = 1 ---
                float x1 = src[1] * ea[tMod + 1];
                if (herring) { x1 += hIl; float t = hRl * hC - hIl * hS; hIl = hRl * hS + hIl * hC; hRl = t; }
                if (addNoise) { ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5; x1 += (ns & 0xFF) * nScale - nOff; }
                vVel = vVel * ringDamp + (x1 - vPrev) * SlewRate; vPrev += vVel;
                waveBuf[baseIdx + 1] = vPrev;

                // --- s = 2 ---
                float x2 = src[2] * ea[tMod + 2];
                if (herring) { x2 += hIl; float t = hRl * hC - hIl * hS; hIl = hRl * hS + hIl * hC; hRl = t; }
                if (addNoise) { ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5; x2 += (ns & 0xFF) * nScale - nOff; }
                vVel = vVel * ringDamp + (x2 - vPrev) * SlewRate; vPrev += vVel;
                waveBuf[baseIdx + 2] = vPrev;

                // --- s = 3 ---
                float x3 = src[3] * ea[tMod + 3];
                if (herring) { x3 += hIl; float t = hRl * hC - hIl * hS; hIl = hRl * hS + hIl * hC; hRl = t; }
                if (addNoise) { ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5; x3 += (ns & 0xFF) * nScale - nOff; }
                vVel = vVel * ringDamp + (x3 - vPrev) * SlewRate; vPrev += vVel;
                waveBuf[baseIdx + 3] = vPrev;

                // ★ 符號位元擴展黑魔法
                tMod += 4;
                tMod += ((5 - tMod) >> 31) & -6;
            }

            for (int i = kLeadPad + kWaveLen; i < kBufLen; i++)
            {
                vVel = vVel * ringDamp + (lastY - vPrev) * SlewRate;
                vPrev += vVel;
                waveBuf[i] = vPrev;
            }
        }

        // 移除不該加的 AggressiveInlining 標籤，讓外層維持清爽！
        static void GenerateWaveform_SVideo(byte[] palBuf, byte emphasisBits, int sl, int phase0, float* waveBuf, float* cBuf)
        {
            int emph = emphasisBits & 7; float* ea = emphAtten + emph * 12;
            bool addNoise = NoiseIntensity > 0f; float firstY = yBaseE[(palBuf[0] & 63) * 8 + emph]; float lastY = yBaseE[(palBuf[255] & 63) * 8 + emph];
            uint ns = addNoise ? (uint)(ntsc_frameCount * 1664525u + (uint)sl * 1013904223u + 1442695041u) : 0u;
            float nScale = 2f * NoiseIntensity / 255.0f, nOff = NoiseIntensity;

            float leftPad = HbiSimulation ? 0.0f : firstY;
            for (int i = 0; i < kLeadPad; i++) { waveBuf[i] = leftPad; cBuf[i] = 0f; }

            if (!addNoise) RunWaveformLoop_SVideo(palBuf, ea, waveBuf, cBuf, phase0, emph, leftPad, lastY, false, 0, 0, 0);
            else RunWaveformLoop_SVideo(palBuf, ea, waveBuf, cBuf, phase0, emph, leftPad, lastY, true, ns, nScale, nOff);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RunWaveformLoop_SVideo(byte[] palBuf, float* ea, float* waveBuf, float* cBuf, int phase0,
            int emph, float leftPad, float lastY, bool addNoise, uint ns, float nScale, float nOff)
        {
            float vPrev = leftPad, rd = RingStrength * 0.5f, vv = 0f; int tMod = phase0;
            for (int d = 0; d < kDots; d++)
            {
                float Ytgt = yBaseE[(palBuf[d] & 63) * 8 + emph]; float* csrc = cTable + ((palBuf[d] & 63) * 6 + tMod) * 4;
                int baseIdx = kLeadPad + d * 4;

                // ★ SVideo 暴力攤平: 完全展開 s=0~3

                // --- s = 0 ---
                float y0 = Ytgt;
                if (addNoise) { ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5; y0 += (ns & 0xFF) * nScale - nOff; }
                vv = vv * rd + (y0 - vPrev) * SlewRate; vPrev += vv;
                waveBuf[baseIdx] = vPrev; cBuf[baseIdx] = csrc[0] * ea[tMod];

                // --- s = 1 ---
                float y1 = Ytgt;
                if (addNoise) { ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5; y1 += (ns & 0xFF) * nScale - nOff; }
                vv = vv * rd + (y1 - vPrev) * SlewRate; vPrev += vv;
                waveBuf[baseIdx + 1] = vPrev; cBuf[baseIdx + 1] = csrc[1] * ea[tMod + 1];

                // --- s = 2 ---
                float y2 = Ytgt;
                if (addNoise) { ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5; y2 += (ns & 0xFF) * nScale - nOff; }
                vv = vv * rd + (y2 - vPrev) * SlewRate; vPrev += vv;
                waveBuf[baseIdx + 2] = vPrev; cBuf[baseIdx + 2] = csrc[2] * ea[tMod + 2];

                // --- s = 3 ---
                float y3 = Ytgt;
                if (addNoise) { ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5; y3 += (ns & 0xFF) * nScale - nOff; }
                vv = vv * rd + (y3 - vPrev) * SlewRate; vPrev += vv;
                waveBuf[baseIdx + 3] = vPrev; cBuf[baseIdx + 3] = csrc[3] * ea[tMod + 3];

                // ★ 符號位元擴展黑魔法
                tMod += 4;
                tMod += ((5 - tMod) >> 31) & -6;
            }
            for (int i = kLeadPad + kWaveLen; i < kBufLen; i++) { vv = vv * rd + (lastY - vPrev) * SlewRate; vPrev += vv; waveBuf[i] = vPrev; cBuf[i] = 0f; }
        }

        static void DemodulateRow(int sl, int phase0, float* waveBuf)
        {
            bool toCrt = ntsc_crtEnabled; int dstW = Crt_DstW;
            int rowStart = sl * Crt_DstH / Crt_SrcH; int rowEnd = Math.Min((sl + 1) * Crt_DstH / Crt_SrcH, Crt_DstH);
            uint* row0 = ntsc_analogScreenBuf + (long)rowStart * dstW; int VS = Vector<float>.Count;

            float* qDotBuf = stackalloc float[256];
            {
                float* wvQ = waveBuf + kLeadPad - kWinQ_half + 2;
                int tModQ = ((phase0 - kWinQ_half + 2) % 6 + 6) % 6;
                for (int d = 0; d < 256; d++)
                {
                    float* cwQ = combinedQ + tModQ * kWinQ; int n = 0; var acc = new Vector<float>(0f);
                    for (; n <= kWinQ - VS; n += VS) acc += *(Vector<float>*)(cwQ + n) * *(Vector<float>*)(wvQ + n);
                    float sumQ = Vector.Dot(acc, new Vector<float>(1f)); for (; n < kWinQ; n++) sumQ += cwQ[n] * wvQ[n];
                    qDotBuf[d] = -2f * sumQ; wvQ += kSampDot;

                    // ★ 符號位元擴展黑魔法
                    tModQ += 4;
                    tModQ += ((5 - tModQ) >> 31) & -6;
                }
            }

            float* wvY = waveBuf + kLeadPad - kWinY_half; float* wvI = waveBuf + kLeadPad - kWinI_half;
            int tModI = ((phase0 - kWinI_half) % 6 + 6) % 6;
            float* yChunk = stackalloc float[VS]; float* iChunk = stackalloc float[VS]; float* qChunk = stackalloc float[VS];

            uint* tmpOutBuf = null;
            uint* stackPtr = stackalloc uint[kOutW];
            if (!toCrt) tmpOutBuf = stackPtr;

            float* lbR = toCrt ? linearBuffer + (long)sl * kOutW : null;
            float* lbG = toCrt ? linearBuffer + (long)kPlane + (long)sl * kOutW : null;
            float* lbB = toCrt ? linearBuffer + 2L * kPlane + (long)sl * kOutW : null;

            // ★ 將 toCrt 分支拉出 SIMD 迴圈
            if (toCrt)
            {
                for (int p = 0; p < kOutW; p += VS)
                {
                    for (int k = 0; k < VS; k++)
                    {
                        yChunk[k] = hannY[0] * wvY[0] + hannY[1] * wvY[1] + hannY[2] * wvY[2] + hannY[3] * wvY[3] + hannY[4] * wvY[4] + hannY[5] * wvY[5];
                        float* cwI = combinedI + tModI * kWinI; int n = 0; var acc = new Vector<float>(0f);
                        for (; n <= kWinI - VS; n += VS) acc += *(Vector<float>*)(cwI + n) * *(Vector<float>*)(wvI + n);
                        float sumI = Vector.Dot(acc, new Vector<float>(1f)); for (; n < kWinI; n++) sumI += cwI[n] * wvI[n];
                        iChunk[k] = 2f * sumI; qChunk[k] = qDotBuf[(p + k) >> 2]; wvY++; wvI++;

                        // ★ 符號位元擴展黑魔法
                        tModI++;
                        tModI += ((5 - tModI) >> 31) & -6;
                    }
                    var Y = *(Vector<float>*)yChunk; var I = *(Vector<float>*)iChunk; var Q = *(Vector<float>*)qChunk;
                    *(Vector<float>*)(lbR + p) = Vector.Min(Vector.Max(vRY * Y + vRI * I + vRQ * Q, vZeroN), vOneN);
                    *(Vector<float>*)(lbG + p) = Vector.Min(Vector.Max(vGY * Y + vGI * I + vGQ * Q, vZeroN), vOneN);
                    *(Vector<float>*)(lbB + p) = Vector.Min(Vector.Max(vBY * Y + vBI * I + vBQ * Q, vZeroN), vOneN);
                }
            }
            else
            {
                for (int p = 0; p < kOutW; p += VS)
                {
                    for (int k = 0; k < VS; k++)
                    {
                        yChunk[k] = hannY[0] * wvY[0] + hannY[1] * wvY[1] + hannY[2] * wvY[2] + hannY[3] * wvY[3] + hannY[4] * wvY[4] + hannY[5] * wvY[5];
                        float* cwI = combinedI + tModI * kWinI; int n = 0; var acc = new Vector<float>(0f);
                        for (; n <= kWinI - VS; n += VS) acc += *(Vector<float>*)(cwI + n) * *(Vector<float>*)(wvI + n);
                        float sumI = Vector.Dot(acc, new Vector<float>(1f)); for (; n < kWinI; n++) sumI += cwI[n] * wvI[n];
                        iChunk[k] = 2f * sumI; qChunk[k] = qDotBuf[(p + k) >> 2]; wvY++; wvI++;

                        // ★ 符號位元擴展黑魔法
                        tModI++;
                        tModI += ((5 - tModI) >> 31) & -6;
                    }
                    var Y = *(Vector<float>*)yChunk; var I = *(Vector<float>*)iChunk; var Q = *(Vector<float>*)qChunk;
                    var R = Vector.Min(Vector.Max(vRY * Y + vRI * I + vRQ * Q, vZeroN), vOneN);
                    var G = Vector.Min(Vector.Max(vGY * Y + vGI * I + vGQ * Q, vZeroN), vOneN);
                    var B = Vector.Min(Vector.Max(vBY * Y + vBI * I + vBQ * Q, vZeroN), vOneN);
                    R *= (v1_minus_GC + vGC * R); G *= (v1_minus_GC + vGC * G); B *= (v1_minus_GC + vGC * B);
                    var ri = Vector.ConvertToInt32(R * v255_0N);
                    var gi = Vector.ConvertToInt32(G * v255_0N);
                    var bi = Vector.ConvertToInt32(B * v255_0N);
                    *(Vector<int>*)(tmpOutBuf + p) = Vector.BitwiseOr(Vector.BitwiseOr(bi, gi * v256iN), Vector.BitwiseOr(ri * v65536iN, vAlphaiN));
                }

                if (dstW != kOutW) { if (UpscaleMode == 1) ResampleH_Bilinear(tmpOutBuf, kOutW, row0, dstW); else { int fs = (kOutW << 16) / dstW; for (int x = 0; x < dstW; x++) row0[x] = tmpOutBuf[(x * fs) >> 16]; } }
                else Buffer.MemoryCopy(tmpOutBuf, row0, dstW * sizeof(uint), dstW * sizeof(uint));
                VerticalFillRows(sl, dstW, row0, rowStart, rowEnd);
            }
        }

        static void DemodulateRow_SVideo(int sl, int phase0, float* waveBuf, float* cBuf)
        {
            bool toCrt = ntsc_crtEnabled; int dstW = Crt_DstW;
            int rowStart = sl * Crt_DstH / Crt_SrcH; int rowEnd = Math.Min((sl + 1) * Crt_DstH / Crt_SrcH, Crt_DstH);
            uint* row0 = ntsc_analogScreenBuf + (long)rowStart * dstW; int VS = Vector<float>.Count;
            float* qDotBuf = stackalloc float[256];
            {
                float* wvQ = cBuf + kLeadPad - kWinQ_half + 2;
                int tModQ = ((phase0 - kWinQ_half + 2) % 6 + 6) % 6;
                for (int d = 0; d < 256; d++)
                {
                    float* cwQ = combinedQ + tModQ * kWinQ; int n = 0; var acc = new Vector<float>(0f);
                    for (; n <= kWinQ - VS; n += VS) acc += *(Vector<float>*)(cwQ + n) * *(Vector<float>*)(wvQ + n);
                    float sumQ = Vector.Dot(acc, new Vector<float>(1f)); for (; n < kWinQ; n++) sumQ += cwQ[n] * wvQ[n];
                    qDotBuf[d] = -2f * sumQ; wvQ += kSampDot;

                    // ★ 符號位元擴展黑魔法
                    tModQ += 4;
                    tModQ += ((5 - tModQ) >> 31) & -6;
                }
            }
            float* wvY = waveBuf + kLeadPad - kWinY_half; float* wvI = cBuf + kLeadPad - kWinI_half;
            int tModI = ((phase0 - kWinI_half) % 6 + 6) % 6;
            float* yChunk = stackalloc float[VS]; float* iChunk = stackalloc float[VS]; float* qChunk = stackalloc float[VS];

            uint* tmpOutBuf = null;
            uint* stackPtr = stackalloc uint[kOutW];
            if (!toCrt) tmpOutBuf = stackPtr;

            float* lbR = toCrt ? linearBuffer + (long)sl * kOutW : null; float* lbG = toCrt ? linearBuffer + (long)kPlane + (long)sl * kOutW : null; float* lbB = toCrt ? linearBuffer + 2L * kPlane + (long)sl * kOutW : null;

            // ★ 將 toCrt 分支拉出 SIMD 迴圈
            if (toCrt)
            {
                for (int p = 0; p < kOutW; p += VS)
                {
                    for (int k = 0; k < VS; k++)
                    {
                        yChunk[k] = hannY[0] * wvY[0] + hannY[1] * wvY[1] + hannY[2] * wvY[2] + hannY[3] * wvY[3] + hannY[4] * wvY[4] + hannY[5] * wvY[5];
                        float* cwI = combinedI + tModI * kWinI; int n = 0; var acc = new Vector<float>(0f);
                        for (; n <= kWinI - VS; n += VS) acc += *(Vector<float>*)(cwI + n) * *(Vector<float>*)(wvI + n);
                        float sumI = Vector.Dot(acc, new Vector<float>(1f)); for (; n < kWinI; n++) sumI += cwI[n] * wvI[n];
                        iChunk[k] = 2f * sumI; qChunk[k] = qDotBuf[(p + k) >> 2]; wvY++; wvI++;

                        // ★ 符號位元擴展黑魔法
                        tModI++;
                        tModI += ((5 - tModI) >> 31) & -6;
                    }
                    var Y = *(Vector<float>*)yChunk; var I = *(Vector<float>*)iChunk; var Q = *(Vector<float>*)qChunk;
                    *(Vector<float>*)(lbR + p) = Vector.Min(Vector.Max(vRY * Y + vRI * I + vRQ * Q, vZeroN), vOneN);
                    *(Vector<float>*)(lbG + p) = Vector.Min(Vector.Max(vGY * Y + vGI * I + vGQ * Q, vZeroN), vOneN);
                    *(Vector<float>*)(lbB + p) = Vector.Min(Vector.Max(vBY * Y + vBI * I + vBQ * Q, vZeroN), vOneN);
                }
            }
            else
            {
                for (int p = 0; p < kOutW; p += VS)
                {
                    for (int k = 0; k < VS; k++)
                    {
                        yChunk[k] = hannY[0] * wvY[0] + hannY[1] * wvY[1] + hannY[2] * wvY[2] + hannY[3] * wvY[3] + hannY[4] * wvY[4] + hannY[5] * wvY[5];
                        float* cwI = combinedI + tModI * kWinI; int n = 0; var acc = new Vector<float>(0f);
                        for (; n <= kWinI - VS; n += VS) acc += *(Vector<float>*)(cwI + n) * *(Vector<float>*)(wvI + n);
                        float sumI = Vector.Dot(acc, new Vector<float>(1f)); for (; n < kWinI; n++) sumI += cwI[n] * wvI[n];
                        iChunk[k] = 2f * sumI; qChunk[k] = qDotBuf[(p + k) >> 2]; wvY++; wvI++;

                        // ★ 符號位元擴展黑魔法
                        tModI++;
                        tModI += ((5 - tModI) >> 31) & -6;
                    }
                    var Y = *(Vector<float>*)yChunk; var I = *(Vector<float>*)iChunk; var Q = *(Vector<float>*)qChunk;
                    var R = Vector.Min(Vector.Max(vRY * Y + vRI * I + vRQ * Q, vZeroN), vOneN);
                    var G = Vector.Min(Vector.Max(vGY * Y + vGI * I + vGQ * Q, vZeroN), vOneN);
                    var B = Vector.Min(Vector.Max(vBY * Y + vBI * I + vBQ * Q, vZeroN), vOneN);
                    R *= (v1_minus_GC + vGC * R); G *= (v1_minus_GC + vGC * G); B *= (v1_minus_GC + vGC * B);
                    var ri = Vector.ConvertToInt32(R * v255_0N);
                    var gi = Vector.ConvertToInt32(G * v255_0N);
                    var bi = Vector.ConvertToInt32(B * v255_0N);
                    *(Vector<int>*)(tmpOutBuf + p) = Vector.BitwiseOr(Vector.BitwiseOr(bi, gi * v256iN), Vector.BitwiseOr(ri * v65536iN, vAlphaiN));
                }

                if (dstW != kOutW) { if (UpscaleMode == 1) ResampleH_Bilinear(tmpOutBuf, kOutW, row0, dstW); else { int fs = (kOutW << 16) / dstW; for (int x = 0; x < dstW; x++) row0[x] = tmpOutBuf[(x * fs) >> 16]; } }
                else Buffer.MemoryCopy(tmpOutBuf, row0, dstW * sizeof(uint), dstW * sizeof(uint));
                VerticalFillRows(sl, dstW, row0, rowStart, rowEnd);
            }
        }

        // ★ 終極無分支 YiqToRgb 結合 Gamma LUT
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint YiqToRgb(float y, float i, float q)
        {
            int ri = (int)(yiq_rY_255 * y + yiq_rI_255 * i + yiq_rQ_255 * q) & 4095;
            int gi = (int)(yiq_gY_255 * y + yiq_gI_255 * i + yiq_gQ_255 * q) & 4095;
            int bi = (int)(yiq_bY_255 * y + yiq_bI_255 * i + yiq_bQ_255 * q) & 4095;
            return (uint)(gammaLUT[bi] | ((uint)gammaLUT[gi] << 8) | ((uint)gammaLUT[ri] << 16) | 0xFF000000u);
        }
    }
}