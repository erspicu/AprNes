using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AprNes
{
    // ============================================================
    // NES NTSC 訊號解碼器 - .NET 4.8.1 究極暴力攤平無分支版
    // ============================================================
    public enum AnalogOutputMode { AV = 0, SVideo = 1, RF = 2 }

    unsafe public partial class NesCore
    {
        public static int UpscaleMode = 1;

        // ── Analog-size compile-time specialization ──
        // Generic struct constraint: each TScale instantiation produces a distinct JIT-
        // specialized method body where N is a compile-time constant. Power-of-2 N
        // becomes a shift; non-power-of-2 (e.g. 6) becomes constant-divide via magic
        // multiplication. Either is far cheaper than runtime `x / N` in the hot loop.
        private interface IAnalogScale { int N { get; } }
        private struct Scale2 : IAnalogScale { public int N { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 2; } }
        private struct Scale4 : IAnalogScale { public int N { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 4; } }
        private struct Scale6 : IAnalogScale { public int N { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 6; } }
        private struct Scale8 : IAnalogScale { public int N { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 8; } }

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

        /// <summary>只更新 buffer 指標，不改 analogSize 等參數（用於 swap）</summary>
        public static void Ntsc_UpdateScreenBuf(uint* buf) => ntsc_analogScreenBuf = buf;

        public static void Ntsc_SetFrameCount(int fc) => ntsc_frameCount = fc;

        // ── 共用唯讀查表與參數 (Thread-Safe) ─────────────────
        static float* loLevels; static float* hiLevels;
        static float* iPhase; static float* qPhase;
        static float* cosTabPhase; static float* sinTabPhase;
        static float* hannY; static float* hannI; static float* hannQ;
        static float* combinedI; static float* combinedQ;
        public static byte* gammaLUT;
        static float* yBase; static float* iBase; static float* qBase;
        static float* waveTable; static float* cTable;
        static float* attenTab; static float* emphAtten;
        static float* yBaseE; static float* iBaseE; static float* qBaseE;

        public static float RfAudioLevel = 0.0f;
        public static float RfBuzzPhase = 0.0f;

        // ── Sampling-rate-dependent constants (HD_NTSC switch) ────────────
        // HD_NTSC (defined only in Avalonia csproj) doubles the NTSC sampling
        // rate to 2048 samples/scanline (12× Fsc oversampling) for higher chroma
        // demod precision. NetFx keeps 1024 (6× Fsc) for compatibility / perf.
        // See MD/Avalonia/ntsc_2048_sampling_plan.md for the full design.
#if HD_NTSC
        public const int kOutW          = 2048;
        public const int kSampDot       = 8;
        public const int kSampDotLog2   = 3;      // log2(kSampDot) — used to convert sample index → dot index ((p+k) >> kSampDotLog2)
        public const int kPhaseEntries  = 12;     // cos/sin table size = master clock phases per Fsc cycle (master=12×Fsc when sampling at 2× master rate)
        public const float kSampleRateScale = 0.5f;  // halves IIR coefficients (ChromaBlur/SlewRate/RingStrength) to keep same physical cutoff at 2× rate
#else
        public const int kOutW          = 1024;
        public const int kSampDot       = 4;
        public const int kSampDotLog2   = 2;
        public const int kPhaseEntries  = 6;
        public const float kSampleRateScale = 1.0f;
#endif
        // emphAtten layout = [8 emph][kEmphStride phases]; second half duplicates
        // first half so inner loops can read phases 0..2N-1 without explicit
        // % kPhaseEntries (branchless wrap for ±1 lookahead).
        public const int kEmphStride    = kPhaseEntries * 2;
        // Master clock phases per Fsc cycle is always 6 (NES hardware: master = 6 × Fsc).
        // At HD_NTSC, kPhaseEntries=12 = 2 sub-samples per master phase. This ratio is
        // used to scale emphasis-attenuation phase ranges (master-phase indexed in NES spec).
        public const int kSubPerMaster  = kPhaseEntries / 6;

        // ── Phase increments per loop iteration unit (HD_NTSC scaled) ─────
        // Each step value gives identical numerical behaviour to the previous
        // hardcoded literal in non-HD builds.
        // Per-line carry = (1364 master cycles × kSubPerMaster) mod kPhaseEntries
        //   non-HD (kPhaseEntries=6, kSubPerMaster=1): 1364 mod 6 = 2
        //   HD     (kPhaseEntries=12, kSubPerMaster=2): 2728 mod 12 = 4
        const int kPhaseStepLine    = 2 * kSubPerMaster;
        // Per-dot = kSampDot phases (kSampDot samples per dot, +1 phase each).
        const int kPhaseStepDot     = kSampDot;
        // Per-sample inside DemodulateRow inner loop = always +1 phase.
        const int kPhaseStepSample  = 1;
        // Per-output-pixel chroma demod stride in RunDecodeLoop. In 1024-rate this
        // is +1 (one master tick of phase per output pixel at default analog 4×).
        // At HD_NTSC, +2 to keep same physical angular velocity per output pixel.
        const int kPhaseStepOutPx   = 1 * kSubPerMaster;
        // Branchless-wrap support: bit-shift threshold = kPhaseEntries - step - 1
        // and AND-mask = -kPhaseEntries (negative wrap delta).
        const int kPhaseWrap        = -kPhaseEntries;
        const int kThreshLine       = kPhaseEntries - kPhaseStepLine    - 1;
        const int kThreshDot        = kPhaseEntries - kPhaseStepDot     - 1;
        const int kThreshSample     = kPhaseEntries - kPhaseStepSample  - 1;
        const int kThreshOutPx      = kPhaseEntries - kPhaseStepOutPx   - 1;
        // Initial-offset constants (used in DemodulateRow_Core to seed tModI/tModQ
        // at specific phase angles for I/Q demod). Original values 3 and 5 represent
        // 180° and 300° offsets in the 6-entry frame; HD doubles them.
        const int kPhaseInitI = 3 * kSubPerMaster;   // 180° offset
        const int kPhaseInitQ = 5 * kSubPerMaster;   // 300° offset (= kPhaseEntries - 1 by coincidence in 6-frame)
        // Wrap threshold for the (phase0 + N) initial offset wrap = kPhaseEntries - 1.
        const int kPhaseInitMax = kPhaseEntries - 1;

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
            // HD_NTSC: halve IIR coefficients (per-sample, frequency-domain) so the
            // 3dB cutoff stays at the same physical Hz when sampling rate doubles.
            // NoiseIntensity is per-sample amplitude (not a filter pole) — leave it.
            SlewRate   *= kSampleRateScale;
            ChromaBlur *= kSampleRateScale;
        }

        static int scanPhase6 = 0;
        static int scanPhaseBase = 0;

        const int kDots = 256;
        // kSampDot is HD_NTSC-conditional (declared above with kOutW/kPhaseEntries)
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

        // Filter window sizes — scaled by kSampDot ratio at HD_NTSC so the
        // physical bandwidth (in Fsc cycles) matches the 1024-rate baseline.
#if HD_NTSC
        const int kWinY = 12; const int kWinY_half = kWinY / 2;
        const int kWinI = 36; const int kWinI_half = kWinI / 2;
        const int kWinQ = 108; const int kWinQ_half = kWinQ / 2;
        static int winQ = 108, winQ_half = 54; // runtime: 108 asymmetric / 36 symmetric
#else
        const int kWinY = 6; const int kWinY_half = kWinY / 2;
        const int kWinI = 18; const int kWinI_half = kWinI / 2;
        const int kWinQ = 54; const int kWinQ_half = kWinQ / 2;
        static int winQ = 54, winQ_half = 27; // runtime: 54 (asymmetric 1953) or 18 (symmetric 1960s)
#endif

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
        public static bool SymmetricIQ = true; // true=1960s symmetric quadrature, false=1953 asymmetric I/Q

        public static void Ntsc_Init()
        {
            if (loLevels == null)
            {
                loLevels = (float*)NesCore.AllocUnmanaged(4 * sizeof(float));
                loLevels[0] = -0.12f; loLevels[1] = 0.00f; loLevels[2] = 0.31f; loLevels[3] = 0.72f;
                // Row-capture buffers for deferred parallel demodulation.
                // Scratch buffers (wave/cBuf/dotY/I/Q) are now per-thread via [ThreadStatic].
                // Phase A2: ntsc_rowPalettes is now allocated unconditionally in Main.init/initFDS
                // (used by both analog and digital paths). Only NTSC-specific row metadata stays here.
                ntsc_rowEmphasis = (byte*)NesCore.AllocUnmanaged(kSrcH);
                ntsc_rowPhase0   = (int*)NesCore.AllocUnmanaged(kSrcH * sizeof(int));
                hiLevels = (float*)NesCore.AllocUnmanaged(4 * sizeof(float));
                hiLevels[0] = 0.40f; hiLevels[1] = 0.68f; hiLevels[2] = 1.00f; hiLevels[3] = 1.00f;
                iPhase = (float*)NesCore.AllocUnmanaged(16 * sizeof(float));
                qPhase = (float*)NesCore.AllocUnmanaged(16 * sizeof(float));
                linearBuffer = (float*)NesCore.AllocUnmanaged(kOutW * kSrcH * 3 * sizeof(float));
                cosTabPhase = (float*)NesCore.AllocUnmanaged(kPhaseEntries * sizeof(float));
                sinTabPhase = (float*)NesCore.AllocUnmanaged(kPhaseEntries * sizeof(float));
                hannY = (float*)NesCore.AllocUnmanaged(kWinY * sizeof(float));
                hannI = (float*)NesCore.AllocUnmanaged(kWinI * sizeof(float));
                hannQ = (float*)NesCore.AllocUnmanaged(kWinQ * sizeof(float));
                combinedI = (float*)NesCore.AllocUnmanaged(kPhaseEntries * kWinI * sizeof(float));
                combinedQ = (float*)NesCore.AllocUnmanaged(kPhaseEntries * kWinQ * sizeof(float));
                gammaLUT = (byte*)NesCore.AllocUnmanaged(4096);
                attenTab = (float*)NesCore.AllocUnmanaged(4 * sizeof(float));
                yBase = (float*)NesCore.AllocUnmanaged(64 * sizeof(float));
                iBase = (float*)NesCore.AllocUnmanaged(64 * sizeof(float));
                qBase = (float*)NesCore.AllocUnmanaged(64 * sizeof(float));
                waveTable = (float*)NesCore.AllocUnmanaged(64 * kPhaseEntries * kSampDot * sizeof(float));
                cTable = (float*)NesCore.AllocUnmanaged(64 * kPhaseEntries * kSampDot * sizeof(float));
                // emphAtten layout: [8 emph][kPhaseEntries × 2 phases] — 2× width
                // for branchless mod-free indexing (inner loops read phases 0..2N-1
                // without explicit % kPhaseEntries).
                emphAtten = (float*)NesCore.AllocUnmanaged(8 * (kPhaseEntries * 2) * sizeof(float));
                yBaseE = (float*)NesCore.AllocUnmanaged(64 * 8 * sizeof(float));
                iBaseE = (float*)NesCore.AllocUnmanaged(64 * 8 * sizeof(float));
                qBaseE = (float*)NesCore.AllocUnmanaged(64 * 8 * sizeof(float));

                // iPhase/qPhase: 16 NES color codes mapped to NTSC chroma phase.
                // The /6.0 here is "12 colors per cycle ÷ 2" = 30° per code, which
                // is set by NES hardware behavior, NOT by sampling rate. Keep 6.0.
                for (int c = 0; c < 16; c++) { double a = c * Math.PI / 6.0; iPhase[c] = -(float)Math.Cos(a); qPhase[c] = (float)Math.Sin(a); }
                // cos/sin phase table: kPhaseEntries entries spanning one full Fsc cycle.
                for (int k = 0; k < kPhaseEntries; k++) { double a = k * 2.0 * Math.PI / kPhaseEntries; cosTabPhase[k] = (float)Math.Cos(a); sinTabPhase[k] = (float)Math.Sin(a); }

                ComputeHann(hannY, kWinY); ComputeHann(hannI, kWinI); ComputeHann(hannQ, kWinQ);

                for (int ph = 0; ph < kPhaseEntries; ph++)
                {
                    for (int n = 0; n < kWinI; n++) combinedI[ph * kWinI + n] = hannI[n] * cosTabPhase[(ph + n) % kPhaseEntries];
                    for (int n = 0; n < kWinQ; n++) combinedQ[ph * kWinQ + n] = hannQ[n] * sinTabPhase[(ph + n) % kPhaseEntries];
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
                // waveTable / cTable: per-palette × per-phase × per-subsample
                // pre-computed waveform. Layout: (p * kPhaseEntries + ph) * kSampDot
                for (int p = 0; p < 64; p++)
                {
                    for (int ph = 0; ph < kPhaseEntries; ph++)
                    {
                        float* wdst = waveTable + (p * kPhaseEntries + ph) * kSampDot;
                        float* cdst = cTable    + (p * kPhaseEntries + ph) * kSampDot;
                        for (int s = 0; s < kSampDot; s++)
                        {
                            int tm = (ph + s) % kPhaseEntries;
                            cdst[s] = cosTabPhase[tm] * iBase[p] - sinTabPhase[tm] * qBase[p];
                            wdst[s] = yBase[p] + cdst[s];
                        }
                    }
                }
                // emphAtten: NES emphasis (R/G/B bits) attenuates 3 master clock
                // phases of the Fsc cycle each. At HD_NTSC, master phase boundaries
                // scale by kSubPerMaster so 1 master tick = kSubPerMaster sub-samples.
                for (int e = 0; e < 8; e++)
                {
                    for (int p = 0; p < kPhaseEntries; p++)
                    {
                        int cnt = 0;
                        // R bit: master phases 1-3 (inclusive) → sub-samples [1..4)*kSubPerMaster
                        if ((e & 1) != 0 && p >= 1 * kSubPerMaster && p < 4 * kSubPerMaster) cnt++;
                        // G bit: master phases 3-5 (inclusive) → sub-samples [3..6)*kSubPerMaster
                        if ((e & 2) != 0 && p >= 3 * kSubPerMaster && p < 6 * kSubPerMaster) cnt++;
                        // B bit: master phases 5,0,1 (wrap) → sub-samples [5..6)*kSubPerMaster ∪ [0..2)*kSubPerMaster
                        if ((e & 4) != 0 && (p >= 5 * kSubPerMaster || p < 2 * kSubPerMaster)) cnt++;
                        emphAtten[e * kEmphStride + p] = (float)Math.Pow(0.746, cnt);
                    }
                    // Duplicate first half into second half for branchless wrap access
                    for (int p = 0; p < kPhaseEntries; p++)
                        emphAtten[e * kEmphStride + kPhaseEntries + p] = emphAtten[e * kEmphStride + p];
                }
                for (int p = 0; p < 64; p++)
                {
                    for (int e = 0; e < 8; e++)
                    {
                        float sumY = 0f, sumI = 0f, sumQ = 0f;
                        for (int ph = 0; ph < kPhaseEntries; ph++)
                        {
                            float V = yBase[p] + iBase[p] * cosTabPhase[ph] - qBase[p] * sinTabPhase[ph];
                            V *= emphAtten[e * kEmphStride + ph]; sumY += V; sumI += V * cosTabPhase[ph]; sumQ -= V * sinTabPhase[ph];
                        }
                        // Average over kPhaseEntries (luma) and kPhaseEntries/2 (chroma — half-cycle integration).
                        yBaseE[p * 8 + e] = sumY / (float)kPhaseEntries;
                        iBaseE[p * 8 + e] = sumI / (float)(kPhaseEntries / 2);
                        qBaseE[p * 8 + e] = sumQ / (float)(kPhaseEntries / 2);
                    }
                }
            }

            UpdateColorTemp();
            UpdateGammaLUT();
            UpdateIQMode();
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

        public static void UpdateIQMode()
        {
            int newQ = SymmetricIQ ? kWinI : kWinQ;
            winQ = newQ;
            winQ_half = newQ / 2;
            if (hannQ == null) return; // not yet initialized
            ComputeHann(hannQ, winQ);
            for (int ph = 0; ph < kPhaseEntries; ph++)
                for (int n = 0; n < winQ; n++)
                    combinedQ[ph * kWinQ + n] = hannQ[n] * sinTabPhase[(ph + n) % kPhaseEntries];
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

        // Per-scanline scratch — [ThreadStatic] so each Parallel.For worker owns its own copy.
        // Lazy-allocated on first use per thread (thread pool threads persist for app lifetime).
        [ThreadStatic] static float* tls_waveBuf;
        [ThreadStatic] static float* tls_cBuf;
        [ThreadStatic] static float* tls_dotY;
        [ThreadStatic] static float* tls_dotI;
        [ThreadStatic] static float* tls_dotQ;

        // Per-scanline capture buffers — PPU thread snapshots palette + emphasis + phase0 here,
        // then at frame end all 240 rows are demodulated in parallel.
        // CRITICAL: phase0 must be captured per-scanline on the PPU thread (scanPhase6 /
        // scanPhaseBase are serial state — reading them from parallel workers would race
        // and produce non-deterministic subcarrier phase, corrupting colour output).
        public static byte* ntsc_rowPalettes;    // kSrcH × 256 bytes (Phase A5: public — emu's per-frame palette buffer)
        static byte* ntsc_rowEmphasis;    // kSrcH bytes
        static int* ntsc_rowPhase0;       // kSrcH ints — captured subcarrier phase per scanline

        static void EnsureThreadScratch()
        {
            if (tls_waveBuf != null) return;
            tls_waveBuf = (float*)NesCore.AllocUnmanaged(kBufLen * sizeof(float));
            tls_cBuf    = (float*)NesCore.AllocUnmanaged(kBufLen * sizeof(float));
            tls_dotY    = (float*)NesCore.AllocUnmanaged(256 * sizeof(float));
            tls_dotI    = (float*)NesCore.AllocUnmanaged(256 * sizeof(float));
            tls_dotQ    = (float*)NesCore.AllocUnmanaged(256 * sizeof(float));
        }

        // Fast path: PPU thread snapshots palette + emphasis + current subcarrier phase at
        // cx==260 of each scanline. Advancing scanPhase6 / scanPhaseBase here (single-threaded)
        // keeps the per-scanline phase sequence deterministic — subsequent parallel decode
        // reads the captured phase0 per row, never touching the serial counters.
        // Phase A1: PixelZone now writes palette indices directly into ntsc_rowPalettes.
        // This function only captures emphasis + per-scanline phase0 for parallel decode.
        public static void Ntsc_CaptureScanline(int sl, byte emphasisBits)
        {
            if (sl < 0 || sl >= kSrcH) return;
            ntsc_rowEmphasis[sl] = emphasisBits;
            // Capture + advance per the path that'll decode this row.
            // Ultra-analog → _Physical uses scanPhaseBase; else _Fast uses scanPhase6.
            if (ntsc_ultraAnalog)
            {
                ntsc_rowPhase0[sl] = scanPhaseBase;
                scanPhaseBase += kPhaseStepLine + (((kThreshLine - scanPhaseBase) >> 31) & kPhaseWrap);
            }
            else
            {
                ntsc_rowPhase0[sl] = scanPhase6;
                scanPhase6 += kPhaseStepLine + (((kThreshLine - scanPhase6) >> 31) & kPhaseWrap);
            }
        }

        // Called at frame end (sl=240 cx=1) before Crt_Render — runs all 240 row demodulations in parallel.
        public static void Ntsc_FlushPendingRows()
        {
            Parallel.For(0, kSrcH, sl =>
            {
                EnsureThreadScratch();
                byte* palBuf = ntsc_rowPalettes + sl * 256;
                byte emph = ntsc_rowEmphasis[sl];
                int phase0 = ntsc_rowPhase0[sl];
                if (ntsc_ultraAnalog)
                    DecodeScanline_Physical_Worker(sl, palBuf, emph, phase0, tls_waveBuf, tls_cBuf);
                else
                    DecodeScanline_Fast_Worker(sl, palBuf, emph, phase0, tls_dotY, tls_dotI, tls_dotQ);
            });
        }

        // Legacy serial entry (kept for correctness fallback / non-benchmark callers).
        public static void DecodeScanline(int sl, byte* palBuf, byte emphasisBits)
        {
            if (sl < 0 || sl >= kSrcH) return;
            EnsureThreadScratch();
            if (ntsc_ultraAnalog)
                DecodeScanline_Physical(sl, palBuf, emphasisBits, tls_waveBuf, tls_cBuf);
            else
                DecodeScanline_Fast(sl, palBuf, emphasisBits, tls_dotY, tls_dotI, tls_dotQ);
        }

        static void DecodeScanline_Fast(int sl, byte* palBuf, byte emphasisBits, float* dotY, float* dotI, float* dotQ)
        {
            int phase0 = scanPhase6;

            // ★ 符號位元擴展黑魔法
            scanPhase6 += kPhaseStepLine + (((kThreshLine - scanPhase6) >> 31) & kPhaseWrap);

            DecodeScanline_Fast_Worker(sl, palBuf, emphasisBits, phase0, dotY, dotI, dotQ);
        }

        // Phase0 passed in — no shared-state mutation, safe to call from Parallel.For workers.
        static void DecodeScanline_Fast_Worker(int sl, byte* palBuf, byte emphasisBits, int phase0, float* dotY, float* dotI, float* dotQ)
        {
            GenerateSignal(palBuf, emphasisBits, dotY, dotI, dotQ);
            if ((AnalogOutputMode)ntsc_analogOutput == AnalogOutputMode.SVideo) DecodeAV_SVideo(sl, dotY, dotI, dotQ);
            else DecodeAV_Composite(sl, phase0, dotY, dotI, dotQ);
        }

        static void GenerateSignal(byte* palBuf, byte emphasisBits, float* dotY, float* dotI, float* dotQ)
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
            bool addNoise = NoiseIntensity > 0f; float ringDamp = RingStrength * 0.5f * kSampleRateScale;
            float nScale = NoiseIntensity * (2f / 255.0f); float nOff = NoiseIntensity;
            int dstW = Crt_DstW; int N = ntsc_analogSize;
            int rowStart = sl * Crt_DstH / Crt_SrcH;
            int rowEnd = Math.Min((sl + 1) * Crt_DstH / Crt_SrcH, Crt_DstH);
            uint* row0 = ntsc_analogScreenBuf + (long)rowStart * dstW;

            float c0 = cosTabPhase[phase0], s0 = sinTabPhase[phase0]; float chr0 = dotI[0] * c0 - dotQ[0] * s0;
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

            // ★ Code Splitting: 分支外提 + scale 特化（N 變 compile-time const）
            switch (N)
            {
                case 2: DispatchDecodeLoop<Scale2>(dstW, row0, dotY, dotI, dotQ, phase0, ref iFilt, ref qFilt, ref yFilt, ref yVel, ringDamp, addNoise, herring, ns, nScale, nOff, hR, hI, hC, hS); break;
                case 4: DispatchDecodeLoop<Scale4>(dstW, row0, dotY, dotI, dotQ, phase0, ref iFilt, ref qFilt, ref yFilt, ref yVel, ringDamp, addNoise, herring, ns, nScale, nOff, hR, hI, hC, hS); break;
                case 6: DispatchDecodeLoop<Scale6>(dstW, row0, dotY, dotI, dotQ, phase0, ref iFilt, ref qFilt, ref yFilt, ref yVel, ringDamp, addNoise, herring, ns, nScale, nOff, hR, hI, hC, hS); break;
                case 8: DispatchDecodeLoop<Scale8>(dstW, row0, dotY, dotI, dotQ, phase0, ref iFilt, ref qFilt, ref yFilt, ref yVel, ringDamp, addNoise, herring, ns, nScale, nOff, hR, hI, hC, hS); break;
                default: RunDecodeLoopGeneric(dstW, N, row0, dotY, dotI, dotQ, phase0, ref iFilt, ref qFilt, ref yFilt, ref yVel, ringDamp, addNoise, herring, ns, nScale, nOff, hR, hI, hC, hS); break;
            }

            VerticalFillRows(sl, dstW, row0, rowStart, rowEnd);
        }

        // Inner 4-way noise/herring dispatch (each branch hands JIT compile-time bool flags).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DispatchDecodeLoop<TScale>(int dstW, uint* row0, float* dotY, float* dotI, float* dotQ, int phStart,
            ref float iFilt, ref float qFilt, ref float yFilt, ref float yVel, float ringDamp,
            bool addNoise, bool herring, uint ns, float nScale, float nOff, float hR, float hI, float hC, float hS)
            where TScale : struct, IAnalogScale
        {
            if (!addNoise && !herring) RunDecodeLoop<TScale>(dstW, row0, dotY, dotI, dotQ, phStart, ref iFilt, ref qFilt, ref yFilt, ref yVel, ringDamp, false, false, 0, 0, 0, 0, 0, 0, 0);
            else if (addNoise && !herring) RunDecodeLoop<TScale>(dstW, row0, dotY, dotI, dotQ, phStart, ref iFilt, ref qFilt, ref yFilt, ref yVel, ringDamp, true, false, ns, nScale, nOff, 0, 0, 0, 0);
            else if (!addNoise && herring) RunDecodeLoop<TScale>(dstW, row0, dotY, dotI, dotQ, phStart, ref iFilt, ref qFilt, ref yFilt, ref yVel, ringDamp, false, true, 0, 0, 0, hR, hI, hC, hS);
            else RunDecodeLoop<TScale>(dstW, row0, dotY, dotI, dotQ, phStart, ref iFilt, ref qFilt, ref yFilt, ref yVel, ringDamp, true, true, ns, nScale, nOff, hR, hI, hC, hS);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RunDecodeLoop<TScale>(int dstW, uint* row0, float* dotY, float* dotI, float* dotQ, int phStart,
            ref float iFilt, ref float qFilt, ref float yFilt, ref float yVel, float ringDamp,
            bool addNoise, bool herring, uint ns, float nScale, float nOff, float hR, float hI, float hC, float hS)
            where TScale : struct, IAnalogScale
        {
            int N = default(TScale).N; // compile-time const after JIT specialization
            int ph = phStart; float iF = iFilt, qF = qFilt, yF = yFilt, yV = yVel; float hRl = hR, hIl = hI;
            for (int x = 0; x < dstW; x++)
            {
                int d = x / N; float c = cosTabPhase[ph], s = sinTabPhase[ph];
                float chroma = dotI[d] * c - dotQ[d] * s;
                iF += ChromaBlur * (chroma * c - iF); qF += ChromaBlur * (-chroma * s - qF);
                yV = yV * ringDamp + (dotY[d] - yF) * SlewRate; yF += yV; float y = yF;

                if (herring) { y += hIl; float t = hRl * hC - hIl * hS; hIl = hRl * hS + hIl * hC; hRl = t; }
                if (addNoise) { ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5; y += (ns & 0xFF) * nScale - nOff; }

                row0[x] = YiqToRgb(y, iF, qF);

                // ★ 符號位元擴展黑魔法
                ph += kPhaseStepOutPx + (((kThreshOutPx - ph) >> 31) & kPhaseWrap);
            }
            iFilt = iF; qFilt = qF; yFilt = yF; yVel = yV;
        }

        // Runtime-N fallback for unusual analog sizes (kept for forward compatibility).
        private static void RunDecodeLoopGeneric(int dstW, int N, uint* row0, float* dotY, float* dotI, float* dotQ, int phStart,
            ref float iFilt, ref float qFilt, ref float yFilt, ref float yVel, float ringDamp,
            bool addNoise, bool herring, uint ns, float nScale, float nOff, float hR, float hI, float hC, float hS)
        {
            int ph = phStart; float iF = iFilt, qF = qFilt, yF = yFilt, yV = yVel; float hRl = hR, hIl = hI;
            for (int x = 0; x < dstW; x++)
            {
                int d = x / N; float c = cosTabPhase[ph], s = sinTabPhase[ph];
                float chroma = dotI[d] * c - dotQ[d] * s;
                iF += ChromaBlur * (chroma * c - iF); qF += ChromaBlur * (-chroma * s - qF);
                yV = yV * ringDamp + (dotY[d] - yF) * SlewRate; yF += yV; float y = yF;

                if (herring) { y += hIl; float t = hRl * hC - hIl * hS; hIl = hRl * hS + hIl * hC; hRl = t; }
                if (addNoise) { ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5; y += (ns & 0xFF) * nScale - nOff; }

                row0[x] = YiqToRgb(y, iF, qF);

                ph += kPhaseStepOutPx + (((kThreshOutPx - ph) >> 31) & kPhaseWrap);
            }
            iFilt = iF; qFilt = qF; yFilt = yF; yVel = yV;
        }

        static void DecodeAV_SVideo(int sl, float* dotY, float* dotI, float* dotQ)
        {
            float iFilt = HbiSimulation ? 0f : dotI[0]; float qFilt = HbiSimulation ? 0f : dotQ[0]; float yFilt = HbiSimulation ? 0f : dotY[0];
            int dstW = Crt_DstW; int N = ntsc_analogSize;
            int rowStart = sl * Crt_DstH / Crt_SrcH; int rowEnd = Math.Min((sl + 1) * Crt_DstH / Crt_SrcH, Crt_DstH);
            uint* row0 = ntsc_analogScreenBuf + (long)rowStart * dstW;

            // Scale specialization (compile-time const N → shift or magic-multiply div)
            switch (N)
            {
                case 2: RunSVideoLoop<Scale2>(dstW, row0, dotY, dotI, dotQ, ref iFilt, ref qFilt, ref yFilt); break;
                case 4: RunSVideoLoop<Scale4>(dstW, row0, dotY, dotI, dotQ, ref iFilt, ref qFilt, ref yFilt); break;
                case 6: RunSVideoLoop<Scale6>(dstW, row0, dotY, dotI, dotQ, ref iFilt, ref qFilt, ref yFilt); break;
                case 8: RunSVideoLoop<Scale8>(dstW, row0, dotY, dotI, dotQ, ref iFilt, ref qFilt, ref yFilt); break;
                default: RunSVideoLoopGeneric(dstW, N, row0, dotY, dotI, dotQ, ref iFilt, ref qFilt, ref yFilt); break;
            }
            VerticalFillRows(sl, dstW, row0, rowStart, rowEnd);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RunSVideoLoop<TScale>(int dstW, uint* row0, float* dotY, float* dotI, float* dotQ,
            ref float iFilt, ref float qFilt, ref float yFilt)
            where TScale : struct, IAnalogScale
        {
            int N = default(TScale).N; // compile-time const after JIT specialization
            float iF = iFilt, qF = qFilt, yF = yFilt;
            for (int outX = 0; outX < dstW; outX++)
            {
                int d = outX / N;
                iF += ChromaBlur * (dotI[d] - iF); qF += ChromaBlur * (dotQ[d] - qF); yF += SlewRate * (dotY[d] - yF);
                row0[outX] = YiqToRgb(yF, iF, qF);
            }
            iFilt = iF; qFilt = qF; yFilt = yF;
        }

        private static void RunSVideoLoopGeneric(int dstW, int N, uint* row0, float* dotY, float* dotI, float* dotQ,
            ref float iFilt, ref float qFilt, ref float yFilt)
        {
            float iF = iFilt, qF = qFilt, yF = yFilt;
            for (int outX = 0; outX < dstW; outX++)
            {
                int d = outX / N;
                iF += ChromaBlur * (dotI[d] - iF); qF += ChromaBlur * (dotQ[d] - qF); yF += SlewRate * (dotY[d] - yF);
                row0[outX] = YiqToRgb(yF, iF, qF);
            }
            iFilt = iF; qFilt = qF; yFilt = yF;
        }

        static void DecodeScanline_Physical(int sl, byte* palBuf, byte emphasisBits, float* waveBuf, float* cBuf)
        {
            int phase0 = scanPhaseBase;

            // ★ 符號位元擴展黑魔法
            scanPhaseBase += kPhaseStepLine + (((kThreshLine - scanPhaseBase) >> 31) & kPhaseWrap);

            DecodeScanline_Physical_Worker(sl, palBuf, emphasisBits, phase0, waveBuf, cBuf);
        }

        // Phase0 passed in — no shared-state mutation, safe to call from Parallel.For workers.
        static void DecodeScanline_Physical_Worker(int sl, byte* palBuf, byte emphasisBits, int phase0, float* waveBuf, float* cBuf)
        {
            if (ColorBurstJitter && (AnalogOutputMode)ntsc_analogOutput == AnalogOutputMode.RF)
            {
                uint jns = (uint)(ntsc_frameCount * 2654435761u + (uint)sl * 340573321u);
                jns ^= jns << 13; jns ^= jns >> 17; jns ^= jns << 5;
                // ColorBurstJitter: rare (1/32) ±1 master-tick phase nudge mod 6 master ticks.
                // In HD_NTSC each master tick = kSubPerMaster sub-samples, and the wrap
                // domain becomes kPhaseEntries (= 12) instead of 6.
                if ((jns & 31) == 0) { phase0 += ((jns & 64) != 0 ? kSubPerMaster : 5 * kSubPerMaster); phase0 += ((kPhaseInitMax - phase0) >> 31) & kPhaseWrap; }
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

        // Precomputed herringbone rotation constants (cos/sin of 1.31683 rad).
        private const float HerringRadPerDot = 1.31683f;
        private static readonly float CosHerring = (float)Math.Cos(HerringRadPerDot);
        private static readonly float SinHerring = (float)Math.Sin(HerringRadPerDot);

        static void GenerateWaveform(byte* palBuf, byte emphasisBits, bool isRF, int sl, int phase0, float* waveBuf)
        {
            int emph = emphasisBits & 7; float* ea = emphAtten + emph * kEmphStride;
            bool addNoise = NoiseIntensity > 0f;
            float firstY = yBaseE[(palBuf[0] & 63) * 8 + emph]; float lastY = yBaseE[(palBuf[255] & 63) * 8 + emph];

            float hR_buzz = 0f, hI_buzz = 0f, hC_buzz = 1f, hS_buzz = 0f; bool herring = false;
            if (isRF)
            {
                float buzz = RfAudioLevel * 0.06f; float env = buzz * (float)Math.Sin((sl / 240.0 + RfBuzzPhase) * 2.0 * Math.PI);
                if (env > 0.0001f || env < -0.0001f)
                {
                    herring = true; hC_buzz = CosHerring; hS_buzz = SinHerring;
                    float lPh = sl * 1364f * HerringRadPerDot; hR_buzz = env * (float)Math.Cos(lPh); hI_buzz = env * (float)Math.Sin(lPh);
                }
            }
            uint ns = 0u; float nScale = 0f, nOff = 0f;
            if (addNoise)
            {
                ns = (uint)(ntsc_frameCount * 1664525u + (uint)sl * 1013904223u + 1442695041u);
                nScale = 2f * NoiseIntensity / 255.0f; nOff = NoiseIntensity;
            }

            float leftPad = HbiSimulation ? 0.0f : firstY;
            for (int i = 0; i < kLeadPad; i++) waveBuf[i] = leftPad;

            // ★ Code Splitting: 分支外提波形生成
            if (!addNoise && !herring) RunWaveformLoop(palBuf, ea, waveBuf, phase0, leftPad, lastY, false, false, 0, 0, 0, 0, 0, 0, 0);
            else if (addNoise && !herring) RunWaveformLoop(palBuf, ea, waveBuf, phase0, leftPad, lastY, true, false, ns, nScale, nOff, 0, 0, 0, 0);
            else if (!addNoise && herring) RunWaveformLoop(palBuf, ea, waveBuf, phase0, leftPad, lastY, false, true, 0, 0, 0, hR_buzz, hI_buzz, hC_buzz, hS_buzz);
            else RunWaveformLoop(palBuf, ea, waveBuf, phase0, leftPad, lastY, true, true, ns, nScale, nOff, hR_buzz, hI_buzz, hC_buzz, hS_buzz);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RunWaveformLoop(byte* palBuf, float* ea, float* waveBuf, int phase0,
            float leftPad, float lastY, bool addNoise, bool herring, uint ns, float nScale, float nOff, float hR, float hI, float hC, float hS)
        {
            float vPrev = leftPad; float ringDamp = RingStrength * 0.5f * kSampleRateScale; float vVel = 0f; int tMod = phase0;
            float hRl = hR, hIl = hI;

            // kSampDot-step lookahead: precompute rotation matrices for steps 1..kSampDot
            float c1 = hC, s1 = hS;
            float c2 = c1 * hC - s1 * hS, s2 = s1 * hC + c1 * hS;
            float c3 = c2 * hC - s2 * hS, s3 = s2 * hC + c2 * hS;
            float c4 = c3 * hC - s3 * hS, s4 = s3 * hC + c3 * hS;
#if HD_NTSC
            float c5 = c4 * hC - s4 * hS, s5 = s4 * hC + c4 * hS;
            float c6 = c5 * hC - s5 * hS, s6 = s5 * hC + c5 * hS;
            float c7 = c6 * hC - s6 * hS, s7 = s6 * hC + c6 * hS;
            float c8 = c7 * hC - s7 * hS, s8 = s7 * hC + c7 * hS;
#endif

            for (int d = 0; d < kDots; d++)
            {
                float* src = waveTable + ((palBuf[d] & 63) * kPhaseEntries + tMod) * kSampDot;
                float* ePtr = ea + tMod;
                int baseIdx = kLeadPad + d * kSampDot;

                // Herringbone: parallel kSampDot-sample computation (breaks data dependency)
                float h0 = 0, h1 = 0, h2 = 0, h3 = 0;
#if HD_NTSC
                float h4 = 0, h5 = 0, h6 = 0, h7 = 0;
#endif
                if (herring)
                {
                    h0 = hIl;
                    h1 = hRl * s1 + hIl * c1;
                    h2 = hRl * s2 + hIl * c2;
                    h3 = hRl * s3 + hIl * c3;
#if HD_NTSC
                    h4 = hRl * s4 + hIl * c4;
                    h5 = hRl * s5 + hIl * c5;
                    h6 = hRl * s6 + hIl * c6;
                    h7 = hRl * s7 + hIl * c7;
                    float tR = hRl * c8 - hIl * s8;
                    hIl = hRl * s8 + hIl * c8;
                    hRl = tR;
#else
                    float tR = hRl * c4 - hIl * s4;
                    hIl = hRl * s4 + hIl * c4;
                    hRl = tR;
#endif
                }

                // Noise: xorshift produces 4 noise bytes; HD needs 2 xorshifts for 8.
                float n0 = 0, n1 = 0, n2 = 0, n3 = 0;
#if HD_NTSC
                float n4 = 0, n5 = 0, n6 = 0, n7 = 0;
#endif
                if (addNoise)
                {
                    ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5;
                    n0 = (ns & 0xFF) * nScale - nOff;
                    n1 = ((ns >> 8) & 0xFF) * nScale - nOff;
                    n2 = ((ns >> 16) & 0xFF) * nScale - nOff;
                    n3 = ((ns >> 24) & 0xFF) * nScale - nOff;
#if HD_NTSC
                    ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5;
                    n4 = (ns & 0xFF) * nScale - nOff;
                    n5 = ((ns >> 8) & 0xFF) * nScale - nOff;
                    n6 = ((ns >> 16) & 0xFF) * nScale - nOff;
                    n7 = ((ns >> 24) & 0xFF) * nScale - nOff;
#endif
                }

                // Pre-compute kSampDot sample inputs up front — independent of the
                // vVel/vPrev filter chain (sequential below), so RyuJIT + CPU OoO
                // engine can schedule them in parallel with the LTI filter.
                float x0 = src[0] * ePtr[0] + h0 + n0;
                float x1 = src[1] * ePtr[1] + h1 + n1;
                float x2 = src[2] * ePtr[2] + h2 + n2;
                float x3 = src[3] * ePtr[3] + h3 + n3;
#if HD_NTSC
                float x4 = src[4] * ePtr[4] + h4 + n4;
                float x5 = src[5] * ePtr[5] + h5 + n5;
                float x6 = src[6] * ePtr[6] + h6 + n6;
                float x7 = src[7] * ePtr[7] + h7 + n7;
#endif

                // LTI filter: vVel/vPrev chain is sequential — can't be vectorised.
                waveBuf[baseIdx]     = (vPrev += (vVel = vVel * ringDamp + (x0 - vPrev) * SlewRate));
                waveBuf[baseIdx + 1] = (vPrev += (vVel = vVel * ringDamp + (x1 - vPrev) * SlewRate));
                waveBuf[baseIdx + 2] = (vPrev += (vVel = vVel * ringDamp + (x2 - vPrev) * SlewRate));
                waveBuf[baseIdx + 3] = (vPrev += (vVel = vVel * ringDamp + (x3 - vPrev) * SlewRate));
#if HD_NTSC
                waveBuf[baseIdx + 4] = (vPrev += (vVel = vVel * ringDamp + (x4 - vPrev) * SlewRate));
                waveBuf[baseIdx + 5] = (vPrev += (vVel = vVel * ringDamp + (x5 - vPrev) * SlewRate));
                waveBuf[baseIdx + 6] = (vPrev += (vVel = vVel * ringDamp + (x6 - vPrev) * SlewRate));
                waveBuf[baseIdx + 7] = (vPrev += (vVel = vVel * ringDamp + (x7 - vPrev) * SlewRate));
#endif

                tMod += kPhaseStepDot + (((kThreshDot - tMod) >> 31) & kPhaseWrap);
            }

            for (int i = kLeadPad + kWaveLen; i < kBufLen; i++)
            {
                waveBuf[i] = (vPrev += (vVel = vVel * ringDamp + (lastY - vPrev) * SlewRate));
            }
        }

        // 移除不該加的 AggressiveInlining 標籤，讓外層維持清爽！
        static void GenerateWaveform_SVideo(byte* palBuf, byte emphasisBits, int sl, int phase0, float* waveBuf, float* cBuf)
        {
            int emph = emphasisBits & 7; float* ea = emphAtten + emph * kEmphStride;
            bool addNoise = NoiseIntensity > 0f; float firstY = yBaseE[(palBuf[0] & 63) * 8 + emph]; float lastY = yBaseE[(palBuf[255] & 63) * 8 + emph];
            uint ns = addNoise ? (uint)(ntsc_frameCount * 1664525u + (uint)sl * 1013904223u + 1442695041u) : 0u;
            float nScale = 2f * NoiseIntensity / 255.0f, nOff = NoiseIntensity;

            float leftPad = HbiSimulation ? 0.0f : firstY;
            for (int i = 0; i < kLeadPad; i++) { waveBuf[i] = leftPad; cBuf[i] = 0f; }

            if (!addNoise) RunWaveformLoop_SVideo(palBuf, ea, waveBuf, cBuf, phase0, emph, leftPad, lastY, false, 0, 0, 0);
            else RunWaveformLoop_SVideo(palBuf, ea, waveBuf, cBuf, phase0, emph, leftPad, lastY, true, ns, nScale, nOff);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RunWaveformLoop_SVideo(byte* palBuf, float* ea, float* waveBuf, float* cBuf, int phase0,
            int emph, float leftPad, float lastY, bool addNoise, uint ns, float nScale, float nOff)
        {
            float vPrev = leftPad, rd = RingStrength * 0.5f * kSampleRateScale, vv = 0f; int tMod = phase0;
            for (int d = 0; d < kDots; d++)
            {
                float Ytgt = yBaseE[(palBuf[d] & 63) * 8 + emph]; float* csrc = cTable + ((palBuf[d] & 63) * kPhaseEntries + tMod) * kSampDot;
                int baseIdx = kLeadPad + d * kSampDot;

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
#if HD_NTSC
                // --- s = 4 ---
                float y4 = Ytgt;
                if (addNoise) { ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5; y4 += (ns & 0xFF) * nScale - nOff; }
                vv = vv * rd + (y4 - vPrev) * SlewRate; vPrev += vv;
                waveBuf[baseIdx + 4] = vPrev; cBuf[baseIdx + 4] = csrc[4] * ea[tMod + 4];

                // --- s = 5 ---
                float y5 = Ytgt;
                if (addNoise) { ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5; y5 += (ns & 0xFF) * nScale - nOff; }
                vv = vv * rd + (y5 - vPrev) * SlewRate; vPrev += vv;
                waveBuf[baseIdx + 5] = vPrev; cBuf[baseIdx + 5] = csrc[5] * ea[tMod + 5];

                // --- s = 6 ---
                float y6 = Ytgt;
                if (addNoise) { ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5; y6 += (ns & 0xFF) * nScale - nOff; }
                vv = vv * rd + (y6 - vPrev) * SlewRate; vPrev += vv;
                waveBuf[baseIdx + 6] = vPrev; cBuf[baseIdx + 6] = csrc[6] * ea[tMod + 6];

                // --- s = 7 ---
                float y7 = Ytgt;
                if (addNoise) { ns ^= ns << 13; ns ^= ns >> 17; ns ^= ns << 5; y7 += (ns & 0xFF) * nScale - nOff; }
                vv = vv * rd + (y7 - vPrev) * SlewRate; vPrev += vv;
                waveBuf[baseIdx + 7] = vPrev; cBuf[baseIdx + 7] = csrc[7] * ea[tMod + 7];
#endif

                // ★ 符號位元擴展黑魔法
                tMod += kPhaseStepDot + (((kThreshDot - tMod) >> 31) & kPhaseWrap);
            }
            for (int i = kLeadPad + kWaveLen; i < kBufLen; i++) { vv = vv * rd + (lastY - vPrev) * SlewRate; vPrev += vv; waveBuf[i] = vPrev; cBuf[i] = 0f; }
        }

        // Composite: chroma (I/Q) comes from the same waveBuf as luma
        static void DemodulateRow(int sl, int phase0, float* waveBuf)
            => DemodulateRow_Core(sl, phase0, waveBuf, waveBuf);

        // S-Video: chroma (I/Q) comes from a separate clean channel (cBuf)
        static void DemodulateRow_SVideo(int sl, int phase0, float* waveBuf, float* cBuf)
            => DemodulateRow_Core(sl, phase0, waveBuf, cBuf);

        // Unified NTSC demodulation core — only the chroma source pointer differs
        static void DemodulateRow_Core(int sl, int phase0, float* waveBuf, float* chromaBuf)
        {
            bool toCrt = ntsc_crtEnabled; int dstW = Crt_DstW;
            int rowStart = sl * Crt_DstH / Crt_SrcH; int rowEnd = Math.Min((sl + 1) * Crt_DstH / Crt_SrcH, Crt_DstH);
            uint* row0 = ntsc_analogScreenBuf + (long)rowStart * dstW; int VS = Vector<float>.Count;

            float* qDotBuf = stackalloc float[256];
            {
                int wQ = winQ, wQ_half = winQ_half;
                // +kSampDot/2 = half-dot offset for chroma window centering on the dot midpoint.
                float* wvQ = chromaBuf + kLeadPad - wQ_half + (kSampDot / 2);
                int tModQ = phase0 + kPhaseInitQ;
                tModQ += ((kPhaseInitMax - tModQ) >> 31) & kPhaseWrap;
                for (int d = 0; d < 256; d++)
                {
                    float* cwQ = combinedQ + tModQ * kWinQ; int n = 0;
                    var accQ0 = new Vector<float>(0f); var accQ1 = new Vector<float>(0f); int stride2Q = VS * 2;
#if NET10_0_OR_GREATER
                    for (; n <= wQ - stride2Q; n += stride2Q) { accQ0 = Vector.MultiplyAddEstimate(*(Vector<float>*)(cwQ + n), *(Vector<float>*)(wvQ + n), accQ0); accQ1 = Vector.MultiplyAddEstimate(*(Vector<float>*)(cwQ + n + VS), *(Vector<float>*)(wvQ + n + VS), accQ1); }
                    for (; n <= wQ - VS; n += VS) accQ0 = Vector.MultiplyAddEstimate(*(Vector<float>*)(cwQ + n), *(Vector<float>*)(wvQ + n), accQ0);
#else
                    for (; n <= wQ - stride2Q; n += stride2Q) { accQ0 += *(Vector<float>*)(cwQ + n) * *(Vector<float>*)(wvQ + n); accQ1 += *(Vector<float>*)(cwQ + n + VS) * *(Vector<float>*)(wvQ + n + VS); }
                    for (; n <= wQ - VS; n += VS) accQ0 += *(Vector<float>*)(cwQ + n) * *(Vector<float>*)(wvQ + n);
#endif
                    float sumQ = Vector.Dot(accQ0 + accQ1, new Vector<float>(1f)); for (; n < wQ; n++) sumQ += cwQ[n] * wvQ[n];
                    qDotBuf[d] = -2f * sumQ; wvQ += kSampDot;

                    // ★ 符號位元擴展黑魔法
                    tModQ += kPhaseStepDot + (((kThreshDot - tModQ) >> 31) & kPhaseWrap);
                }
            }

            float* wvY = waveBuf + kLeadPad - kWinY_half; float* wvI = chromaBuf + kLeadPad - kWinI_half;
            int tModI = phase0 + kPhaseInitI;
            tModI += ((kPhaseInitMax - tModI) >> 31) & kPhaseWrap;
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
                        float yAcc = hannY[0] * wvY[0] + hannY[1] * wvY[1] + hannY[2] * wvY[2] + hannY[3] * wvY[3] + hannY[4] * wvY[4] + hannY[5] * wvY[5];
#if HD_NTSC
                        yAcc += hannY[6] * wvY[6] + hannY[7] * wvY[7] + hannY[8] * wvY[8] + hannY[9] * wvY[9] + hannY[10] * wvY[10] + hannY[11] * wvY[11];
#endif
                        yChunk[k] = yAcc;
                        float* cwI = combinedI + tModI * kWinI; int n = 0; var acc = new Vector<float>(0f);
#if NET10_0_OR_GREATER
                        for (; n <= kWinI - VS; n += VS) acc = Vector.MultiplyAddEstimate(*(Vector<float>*)(cwI + n), *(Vector<float>*)(wvI + n), acc);
#else
                        for (; n <= kWinI - VS; n += VS) acc += *(Vector<float>*)(cwI + n) * *(Vector<float>*)(wvI + n);
#endif
                        float sumI = Vector.Dot(acc, new Vector<float>(1f)); for (; n < kWinI; n++) sumI += cwI[n] * wvI[n];
                        // (p+k) >> kSampDotLog2 = sample index → dot index. Critical:
                        // bare `>> 2` here would index past qDotBuf[256] when kSampDot=8.
                        iChunk[k] = 2f * sumI; qChunk[k] = qDotBuf[(p + k) >> kSampDotLog2]; wvY++; wvI++;

                        // ★ 符號位元擴展黑魔法
                        tModI += kPhaseStepSample + (((kThreshSample - tModI) >> 31) & kPhaseWrap);
                    }
                    var Y = *(Vector<float>*)yChunk; var I = *(Vector<float>*)iChunk; var Q = *(Vector<float>*)qChunk;
#if NET10_0_OR_GREATER
                    // .NET 10: FMA chain — vfmadd231 on AVX2+FMA hardware, scalar fallback otherwise
                    *(Vector<float>*)(lbR + p) = Vector.Min(Vector.Max(Vector.MultiplyAddEstimate(vRQ, Q, Vector.MultiplyAddEstimate(vRI, I, vRY * Y)), vZeroN), vOneN);
                    *(Vector<float>*)(lbG + p) = Vector.Min(Vector.Max(Vector.MultiplyAddEstimate(vGQ, Q, Vector.MultiplyAddEstimate(vGI, I, vGY * Y)), vZeroN), vOneN);
                    *(Vector<float>*)(lbB + p) = Vector.Min(Vector.Max(Vector.MultiplyAddEstimate(vBQ, Q, Vector.MultiplyAddEstimate(vBI, I, vBY * Y)), vZeroN), vOneN);
#else
                    *(Vector<float>*)(lbR + p) = Vector.Min(Vector.Max(vRY * Y + vRI * I + vRQ * Q, vZeroN), vOneN);
                    *(Vector<float>*)(lbG + p) = Vector.Min(Vector.Max(vGY * Y + vGI * I + vGQ * Q, vZeroN), vOneN);
                    *(Vector<float>*)(lbB + p) = Vector.Min(Vector.Max(vBY * Y + vBI * I + vBQ * Q, vZeroN), vOneN);
#endif
                }
            }
            else
            {
                for (int p = 0; p < kOutW; p += VS)
                {
                    for (int k = 0; k < VS; k++)
                    {
                        float yAcc = hannY[0] * wvY[0] + hannY[1] * wvY[1] + hannY[2] * wvY[2] + hannY[3] * wvY[3] + hannY[4] * wvY[4] + hannY[5] * wvY[5];
#if HD_NTSC
                        yAcc += hannY[6] * wvY[6] + hannY[7] * wvY[7] + hannY[8] * wvY[8] + hannY[9] * wvY[9] + hannY[10] * wvY[10] + hannY[11] * wvY[11];
#endif
                        yChunk[k] = yAcc;
                        float* cwI = combinedI + tModI * kWinI; int n = 0; var acc = new Vector<float>(0f);
#if NET10_0_OR_GREATER
                        for (; n <= kWinI - VS; n += VS) acc = Vector.MultiplyAddEstimate(*(Vector<float>*)(cwI + n), *(Vector<float>*)(wvI + n), acc);
#else
                        for (; n <= kWinI - VS; n += VS) acc += *(Vector<float>*)(cwI + n) * *(Vector<float>*)(wvI + n);
#endif
                        float sumI = Vector.Dot(acc, new Vector<float>(1f)); for (; n < kWinI; n++) sumI += cwI[n] * wvI[n];
                        // (p+k) >> kSampDotLog2 = sample index → dot index. Critical:
                        // bare `>> 2` here would index past qDotBuf[256] when kSampDot=8.
                        iChunk[k] = 2f * sumI; qChunk[k] = qDotBuf[(p + k) >> kSampDotLog2]; wvY++; wvI++;

                        // ★ 符號位元擴展黑魔法
                        tModI += kPhaseStepSample + (((kThreshSample - tModI) >> 31) & kPhaseWrap);
                    }
                    var Y = *(Vector<float>*)yChunk; var I = *(Vector<float>*)iChunk; var Q = *(Vector<float>*)qChunk;
#if NET10_0_OR_GREATER
                    // .NET 10: FMA chain for YIQ→RGB matrix and gamma curve
                    var R = Vector.Min(Vector.Max(Vector.MultiplyAddEstimate(vRQ, Q, Vector.MultiplyAddEstimate(vRI, I, vRY * Y)), vZeroN), vOneN);
                    var G = Vector.Min(Vector.Max(Vector.MultiplyAddEstimate(vGQ, Q, Vector.MultiplyAddEstimate(vGI, I, vGY * Y)), vZeroN), vOneN);
                    var B = Vector.Min(Vector.Max(Vector.MultiplyAddEstimate(vBQ, Q, Vector.MultiplyAddEstimate(vBI, I, vBY * Y)), vZeroN), vOneN);
                    R *= Vector.MultiplyAddEstimate(vGC, R, v1_minus_GC);
                    G *= Vector.MultiplyAddEstimate(vGC, G, v1_minus_GC);
                    B *= Vector.MultiplyAddEstimate(vGC, B, v1_minus_GC);
#else
                    var R = Vector.Min(Vector.Max(vRY * Y + vRI * I + vRQ * Q, vZeroN), vOneN);
                    var G = Vector.Min(Vector.Max(vGY * Y + vGI * I + vGQ * Q, vZeroN), vOneN);
                    var B = Vector.Min(Vector.Max(vBY * Y + vBI * I + vBQ * Q, vZeroN), vOneN);
                    R *= (v1_minus_GC + vGC * R); G *= (v1_minus_GC + vGC * G); B *= (v1_minus_GC + vGC * B);
#endif
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