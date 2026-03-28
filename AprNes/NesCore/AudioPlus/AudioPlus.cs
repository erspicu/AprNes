using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AprNes
{
    unsafe public partial class NesCore
    {
        // ==================================================================
        // AudioPlus — 非託管記憶體配置輔助 (apm_)
        // ==================================================================
        static float* apm_AllocFloat(int count)
        {
            float* p = (float*)Marshal.AllocHGlobal(count * sizeof(float));
            for (int i = 0; i < count; i++) p[i] = 0f;
            return p;
        }
        static double* apm_AllocDouble(int count)
        {
            double* p = (double*)Marshal.AllocHGlobal(count * sizeof(double));
            for (int i = 0; i < count; i++) p[i] = 0.0;
            return p;
        }
        static int* apm_AllocInt(int count)
        {
            int* p = (int*)Marshal.AllocHGlobal(count * sizeof(int));
            for (int i = 0; i < count; i++) p[i] = 0;
            return p;
        }
        static uint* apm_AllocUint(int count)
        {
            uint* p = (uint*)Marshal.AllocHGlobal(count * sizeof(uint));
            for (int i = 0; i < count; i++) p[i] = 0;
            return p;
        }
        static void apm_ZeroFloat(float* p, int count)
        {
            for (int i = 0; i < count; i++) p[i] = 0f;
        }
        static void apm_ZeroInt(int* p, int count)
        {
            for (int i = 0; i < count; i++) p[i] = 0;
        }
        static void apm_ZeroUint(uint* p, int count)
        {
            for (int i = 0; i < count; i++) p[i] = 0;
        }

        // ==================================================================
        // AudioPlus — 共用常數
        // ==================================================================
        const double AP_CPU_FREQ = 1789772.72;
        const int AP_SAMPLE_RATE = 44100;
        const double AP_CLOCKS_PER_SAMPLE = AP_CPU_FREQ / AP_SAMPLE_RATE;

        // ==================================================================
        // AudioPlus — 總調度器 (AudioPlus_)
        // ==================================================================
        const float AP_GAIN_MODE1 = 40000f;  // Mode 1: DAC 電壓 ~0-0.8 → 適合 40000
        const float AP_GAIN_MODE2 = 20000f;  // Mode 2: 正規化 0-1 × 5ch pan sum → 適合 20000

        static float ap_modeGain = AP_GAIN_MODE1;
        static bool ap_initialized = false;
        static bool ap_tablesInitialized = false;

        public static void AudioPlus_Init()
        {
            ap_InitTables();
            ose_InitInstances();
            blip_Init();
            cmf_Init();
            mfx_Init();
            mmix_Init();
            ap_initialized = true;
            AudioPlus_Reset();
            AudioPlus_ApplySettings();
        }

        static void ap_InitTables()
        {
            if (ap_tablesInitialized) return;
            authMix_InitTables();
            blip_InitTables();
            cmf_InitTables();
            mfx_InitTables();
            mmix_InitTables();
            ose_InitTables();
            ap_tablesInitialized = true;
        }

        public static void AudioPlus_ApplySettings()
        {
            if (!ap_initialized) return;
            ap_modeGain = (AudioMode == 2) ? AP_GAIN_MODE2 : AP_GAIN_MODE1;
            cmf_SetModel(ConsoleModel, CustomLpfCutoff, CustomBuzz);
            cmf_SetBuzzParams(BuzzFreq, BuzzAmplitude);
            cmf_SetRfVolume(RfVolume);
            mmix_SetStereoWidth(StereoWidth);
            mmix_SetBassBoost(BassBoostDb, BassBoostFreq);
            mfx_SetHaasDelay(HaasDelay);
            mfx_SetHaasCrossfeed(HaasCrossfeed);
            mfx_SetReverbWet(ReverbWet);
            mfx_SetCombFeedback(CombFeedback);
            mfx_SetCombDamp(CombDamp);
            mmix_UpdateChannelGains();
        }

        public static void AudioPlus_Reset()
        {
            if (!ap_initialized) return;
            blip_Reset();
            for (int i = 0; i < OSE_COUNT; i++) ose_Reset(i);
            cmf_Reset();
            mfx_Reset();
            mmix_ResetBiquad();
            mmix_UpdateChannelGains();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AudioPlus_PushApuCycle(
            int sq1, int sq2, int tri, int noise, int dmc,
            int expansionAudio)
        {
            if (!AudioEnabled || !ap_initialized) return;

            int mode = AudioMode;

            if (mode == 1)
            {
                // 1. 取得帶有真實 DC Offset 的物理電壓 (已移除 1.79MHz HPF)
                float voltage = authMix_GetVoltage(sq1, sq2, tri, noise, dmc, expansionAudio);

                // 2. 完整保留 DC 特性，送入超採樣引擎 (FIR 降頻為線性操作，無損通過)
                ose_PushSample(0, voltage);

                float sample;
                if (ose_TryGetSample(0, out sample))
                {
                    // 3. 降頻至 44.1kHz 後，在這裡消除 DC Offset 並加上主機濾波
                    sample = cmf_Process(sample, RfCrosstalk);
                    ap_OutputStereo(sample, sample);
                }
            }
            else if (mode == 2)
            {
                mmix_PushChannels(sq1, sq2, tri, noise, dmc);

                float sampleL, sampleR;
                if (mmix_TryGetStereoSample(out sampleL, out sampleR))
                    ap_OutputStereo(sampleL, sampleR);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ap_OutputStereo(float left, float right)
        {
            float g = ap_modeGain * (Volume * 0.01f);

            int scaledL = ap_SoftClipToInt16(left * g);
            int scaledR = ap_SoftClipToInt16(right * g);

            AudioSampleReady?.Invoke((short)scaledL, (short)scaledR);

            if (AnalogEnabled && AnalogOutput == AnalogOutputMode.RF)
            {
                int mono = (scaledL + scaledR) / 2;
                float absS = mono < 0 ? -mono / 32767f : mono / 32767f;
                RfAudioLevel = RfAudioLevel * 0.95f + absS * 0.05f;
                RfBuzzPhase = (RfBuzzPhase + absS * 0.0001f) % 1.0f;
            }
        }

        /// <summary>
        /// Soft limiter: 線性通過 ±80% (±26214)，超過部分漸進壓縮至 ±32767。
        /// 消除硬 clip 爆音，同時保留正常範圍的線性度。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int ap_SoftClipToInt16(float x)
        {
            const float KNEE = 26214f;       // 32767 × 0.8
            const float RANGE = 6553f;       // 32767 - KNEE
            const float INV_RANGE = 1f / 6553f;

            if (x > KNEE)
            {
                float over = (x - KNEE) * INV_RANGE;
                return (int)(KNEE + RANGE * over / (1f + over));
            }
            if (x < -KNEE)
            {
                float over = (-x - KNEE) * INV_RANGE;
                return -(int)(KNEE + RANGE * over / (1f + over));
            }
            return (int)x;
        }

        // ==================================================================
        // AuthenticAudioMixer (authMix_) — 物理級 DAC 非線性混音器
        // ==================================================================
        static float* authMix_pulseTable;
        static float* authMix_tndTable;

        static void authMix_InitTables()
        {
            authMix_pulseTable = apm_AllocFloat(31);
            authMix_pulseTable[0] = 0f;
            for (int i = 1; i < 31; i++)
                authMix_pulseTable[i] = (float)(95.88 / (8128.0 / i + 100.0));

            int tndSize = 16 * 16 * 128;
            authMix_tndTable = apm_AllocFloat(tndSize);
            for (int t = 0; t < 16; t++)
                for (int n = 0; n < 16; n++)
                    for (int d = 0; d < 128; d++)
                    {
                        double sum = t / 8227.0 + n / 12241.0 + d / 22638.0;
                        float val = (sum < 1e-12) ? 0f : (float)(159.79 / (1.0 / sum + 100.0));
                        authMix_tndTable[(t << 11) | (n << 7) | d] = val;
                    }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float authMix_GetVoltage(int sq1, int sq2, int tri, int noise, int dmc, int expansionAudio)
        {
            int pulseIdx = sq1 + sq2;
            if (pulseIdx > 30) pulseIdx = 30;
            float pulse = authMix_pulseTable[pulseIdx];
            float tnd = authMix_tndTable[(tri << 11) | (noise << 7) | dmc];
            double raw = pulse + tnd + (expansionAudio / 98302.0);
            return (float)raw;
        }

        // ==================================================================
        // BlipSynthesizer (blip_) — 帶限步階合成器 (目前未使用，保留)
        // ==================================================================
        const int BLIP_PHASES = 32;
        const int BLIP_TAPS = 16;
        const int BLIP_HALF_TAPS = BLIP_TAPS / 2;
        const int BLIP_BUF_SIZE = 2048;
        const int BLIP_BUF_MASK = BLIP_BUF_SIZE - 1;
        const double BLIP_INTEGRATOR_LEAK = 0.99997;

        static float* blip_stepTable;
        static float* blip_buffer;
        static int blip_readPos;
        static int blip_writeBase;
        static double blip_clockAccum;
        static float blip_lastAmplitude;
        static double blip_integrator;

        static void blip_InitTables()
        {
            blip_stepTable = apm_AllocFloat(BLIP_PHASES * BLIP_TAPS);
            for (int p = 0; p < BLIP_PHASES; p++)
            {
                int pOffset = p * BLIP_TAPS;
                double phase = (double)p / BLIP_PHASES;
                double sum = 0.0;
                for (int i = 0; i < BLIP_TAPS; i++)
                {
                    double x = (i - BLIP_HALF_TAPS) - phase;
                    double sinc;
                    if (Math.Abs(x) < 1e-9) sinc = 1.0;
                    else sinc = Math.Sin(Math.PI * x) / (Math.PI * x);
                    double wPhase = (x + BLIP_HALF_TAPS) / BLIP_TAPS;
                    double window = 0.42
                                  - 0.50 * Math.Cos(2.0 * Math.PI * wPhase)
                                  + 0.08 * Math.Cos(4.0 * Math.PI * wPhase);
                    sum += sinc * window;
                    blip_stepTable[pOffset + i] = (float)sum;
                }
                if (Math.Abs(sum) > 1e-12)
                {
                    float invSum = (float)(1.0 / sum);
                    for (int i = 0; i < BLIP_TAPS; i++)
                        blip_stepTable[pOffset + i] *= invSum;
                }
            }
        }

        static void blip_Init()
        {
            if (blip_buffer == null)
                blip_buffer = apm_AllocFloat(BLIP_BUF_SIZE);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void blip_AddDelta(float newAmplitude)
        {
            float delta = newAmplitude - blip_lastAmplitude;
            if (Math.Abs(delta) < 1e-8f) return;
            blip_lastAmplitude = newAmplitude;

            double exactPos = blip_clockAccum / AP_CLOCKS_PER_SAMPLE;
            int sampleIdx = (int)exactPos;
            double frac = exactPos - sampleIdx;

            int phaseIdx = (int)(frac * BLIP_PHASES);
            if (phaseIdx >= BLIP_PHASES) phaseIdx = BLIP_PHASES - 1;

            int baseIdx = (blip_writeBase + sampleIdx) & BLIP_BUF_MASK;
            for (int i = 0; i < BLIP_TAPS; i++)
                blip_buffer[(baseIdx + i) & BLIP_BUF_MASK] += delta * blip_stepTable[phaseIdx * BLIP_TAPS + i];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void blip_ClockAdvance()
        {
            blip_clockAccum += 1.0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool blip_TryGetSample(out float result)
        {
            result = 0f;
            if (blip_clockAccum < AP_CLOCKS_PER_SAMPLE) return false;
            blip_clockAccum -= AP_CLOCKS_PER_SAMPLE;

            float delta = blip_buffer[blip_readPos];
            blip_buffer[blip_readPos] = 0f;

            blip_integrator = blip_integrator * BLIP_INTEGRATOR_LEAK + delta;
            result = (float)blip_integrator;

            blip_readPos = (blip_readPos + 1) & BLIP_BUF_MASK;
            blip_writeBase = blip_readPos;
            return true;
        }

        static void blip_Reset()
        {
            apm_ZeroFloat(blip_buffer, BLIP_BUF_SIZE);
            blip_readPos = 0;
            blip_writeBase = 0;
            blip_clockAccum = 0.0;
            blip_lastAmplitude = 0f;
            blip_integrator = 0.0;
        }

        // ==================================================================
        // ConsoleModelFilter (cmf_) — 主機型號差異濾波器
        // ==================================================================
        const double CMF_DT = 1.0 / 44100.0;
        const float CMF_HPF_ALPHA_44K = 0.9872f;
        const double CMF_RF_PHASE_INC = 59.94 / 44100.0;

        static double* cmf_presetCutoffs;
        static byte* cmf_presetBuzz;
        static float cmf_hpfState;
        static float cmf_hpfPrev;
        static float cmf_lpfState;
        static float cmf_currentBeta;
        static int cmf_currentModel;
        static bool cmf_buzzEnabled;
        static double cmf_buzzPhase;
        static double cmf_buzzPhaseInc;
        static float cmf_buzzAmplitude;
        static double cmf_rfPhase;
        static float cmf_rfBaseVolume;
        static float cmf_videoLuminance;

        static void cmf_InitTables()
        {
            cmf_presetCutoffs = apm_AllocDouble(6);
            cmf_presetCutoffs[0] = 14000; cmf_presetCutoffs[1] = 4700; cmf_presetCutoffs[2] = 20000;
            cmf_presetCutoffs[3] = 19000; cmf_presetCutoffs[4] = 12000; cmf_presetCutoffs[5] = 16000;

            cmf_presetBuzz = (byte*)Marshal.AllocHGlobal(6);
            cmf_presetBuzz[0] = 0; cmf_presetBuzz[1] = 0; cmf_presetBuzz[2] = 1;
            cmf_presetBuzz[3] = 0; cmf_presetBuzz[4] = 0; cmf_presetBuzz[5] = 0;
        }

        static void cmf_Init()
        {
            cmf_hpfState = 0f; cmf_hpfPrev = 0f;
            cmf_lpfState = 0f; cmf_currentModel = 0;
            cmf_buzzEnabled = false; cmf_buzzPhase = 0.0;
            cmf_rfPhase = 0.0; cmf_videoLuminance = 0f;
            cmf_SetBuzzParams(60, 30);
            cmf_SetRfVolume(50);
            cmf_SetModel(0, 14000, false);
        }

        static float cmf_CalcBeta(double cutoffHz)
        {
            double rc = 1.0 / (2.0 * Math.PI * cutoffHz);
            return (float)(CMF_DT / (rc + CMF_DT));
        }

        static void cmf_SetModel(int model, int customCutoff, bool customBuzz)
        {
            cmf_currentModel = Math.Max(0, Math.Min(6, model));
            if (cmf_currentModel < 6)
            {
                cmf_currentBeta = cmf_CalcBeta(cmf_presetCutoffs[cmf_currentModel]);
                cmf_buzzEnabled = cmf_presetBuzz[cmf_currentModel] != 0;
            }
            else
            {
                int cutoff = Math.Max(1000, Math.Min(22000, customCutoff));
                cmf_currentBeta = cmf_CalcBeta(cutoff);
                cmf_buzzEnabled = customBuzz;
            }
        }

        static void cmf_SetBuzzParams(int freq, int amplitude)
        {
            int f = (freq == 50) ? 50 : 60;
            cmf_buzzPhaseInc = f / 44100.0;
            cmf_buzzAmplitude = Math.Max(0, Math.Min(100, amplitude)) * 0.0001f;
        }

        static void cmf_SetRfVolume(int volume)
        {
            cmf_rfBaseVolume = Math.Max(0, Math.Min(200, volume)) * 0.001f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void cmf_SetVideoLuminance(float luma)
        {
            cmf_videoLuminance = luma;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float cmf_Process(float input, bool rfCrosstalk)
        {
            // 1. 在 44.1kHz 進行完美無損的 DC Offset 消除 (90Hz HPF)
            float diff = input - cmf_hpfPrev;
            cmf_hpfPrev = input;
            cmf_hpfState = diff + CMF_HPF_ALPHA_44K * cmf_hpfState;
            float cleanInput = cmf_hpfState;

            // 2. 進行主機型號 LPF 濾波
            cmf_lpfState += cmf_currentBeta * (cleanInput - cmf_lpfState);
            float output = cmf_lpfState;

            // 3. 疊加物理缺陷雜音
            if (cmf_buzzEnabled && cmf_buzzAmplitude > 0f)
            {
                cmf_buzzPhase += cmf_buzzPhaseInc;
                if (cmf_buzzPhase >= 1.0) cmf_buzzPhase -= 1.0;
                output += (float)(Math.Sin(2.0 * Math.PI * cmf_buzzPhase)) * cmf_buzzAmplitude;
            }

            if (rfCrosstalk && cmf_rfBaseVolume > 0f)
            {
                cmf_rfPhase += CMF_RF_PHASE_INC;
                if (cmf_rfPhase >= 1.0) cmf_rfPhase -= 1.0;
                float sawtooth = (float)(cmf_rfPhase - 0.5) * 2f;
                output += sawtooth * cmf_videoLuminance * cmf_rfBaseVolume;
            }

            return output;
        }

        static void cmf_Reset()
        {
            cmf_lpfState = 0f; cmf_buzzPhase = 0.0;
            cmf_rfPhase = 0.0; cmf_hpfState = 0f; cmf_hpfPrev = 0f;
        }

        // ==================================================================
        // ModernAudioFX (mfx_) — 空間效果處理器
        // ==================================================================
        const int MFX_MAX_DELAY = (int)(AP_SAMPLE_RATE * 0.035);
        const int MFX_COMB_COUNT = 4;

        static float* mfx_haasDelayBuf;
        static int mfx_haasWritePos;
        static int mfx_haasDelaySamples;
        static float mfx_haasCrossfeed;
        static int* mfx_combLengths;
        static float** mfx_combBuf;
        static int* mfx_combPos;
        static float* mfx_combLpfState;
        static float mfx_combFeedback;
        static float mfx_combDamp;
        static float mfx_reverbWet;

        static void mfx_InitTables()
        {
            mfx_combLengths = apm_AllocInt(4);
            mfx_combLengths[0] = 1116; mfx_combLengths[1] = 1188;
            mfx_combLengths[2] = 1277; mfx_combLengths[3] = 1356;
        }

        static void mfx_Init()
        {
            if (mfx_haasDelayBuf != null) return; // 已配置，共用記憶體
            mfx_haasDelayBuf = apm_AllocFloat(MFX_MAX_DELAY);
            mfx_combBuf = (float**)Marshal.AllocHGlobal(MFX_COMB_COUNT * sizeof(float*));
            mfx_combPos = apm_AllocInt(MFX_COMB_COUNT);
            mfx_combLpfState = apm_AllocFloat(MFX_COMB_COUNT);
            for (int i = 0; i < MFX_COMB_COUNT; i++)
                mfx_combBuf[i] = apm_AllocFloat(mfx_combLengths[i]);
        }

        static void mfx_SetHaasDelay(int ms)
        {
            ms = Math.Max(10, Math.Min(30, ms));
            mfx_haasDelaySamples = AP_SAMPLE_RATE * ms / 1000;
        }

        static void mfx_SetHaasCrossfeed(int percent)
        {
            mfx_haasCrossfeed = Math.Max(0, Math.Min(80, percent)) / 100f;
        }

        static void mfx_SetReverbWet(int percent)
        {
            mfx_reverbWet = Math.Max(0, Math.Min(30, percent)) / 100f;
        }

        static void mfx_SetCombFeedback(int percent)
        {
            mfx_combFeedback = Math.Max(30, Math.Min(90, percent)) / 100f;
        }

        static void mfx_SetCombDamp(int percent)
        {
            mfx_combDamp = Math.Max(10, Math.Min(70, percent)) / 100f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void mfx_ProcessSample(ref float L, ref float R)
        {
            if (mfx_reverbWet > 0f)
            {
                float mono = (L + R) * 0.5f;
                float fb = mfx_combFeedback;
                float damp = mfx_combDamp;
                float reverbOut;

                // Comb 0
                {
                    float* cb = mfx_combBuf[0];
                    int p = mfx_combPos[0];
                    float d = cb[p];
                    float lp = d + damp * (mfx_combLpfState[0] - d);
                    mfx_combLpfState[0] = lp;
                    cb[p] = mono + lp * fb;
                    if (++p >= mfx_combLengths[0]) p = 0;
                    mfx_combPos[0] = p;
                    reverbOut = d;
                }
                // Comb 1
                {
                    float* cb = mfx_combBuf[1];
                    int p = mfx_combPos[1];
                    float d = cb[p];
                    float lp = d + damp * (mfx_combLpfState[1] - d);
                    mfx_combLpfState[1] = lp;
                    cb[p] = mono + lp * fb;
                    if (++p >= mfx_combLengths[1]) p = 0;
                    mfx_combPos[1] = p;
                    reverbOut += d;
                }
                // Comb 2
                {
                    float* cb = mfx_combBuf[2];
                    int p = mfx_combPos[2];
                    float d = cb[p];
                    float lp = d + damp * (mfx_combLpfState[2] - d);
                    mfx_combLpfState[2] = lp;
                    cb[p] = mono + lp * fb;
                    if (++p >= mfx_combLengths[2]) p = 0;
                    mfx_combPos[2] = p;
                    reverbOut += d;
                }
                // Comb 3
                {
                    float* cb = mfx_combBuf[3];
                    int p = mfx_combPos[3];
                    float d = cb[p];
                    float lp = d + damp * (mfx_combLpfState[3] - d);
                    mfx_combLpfState[3] = lp;
                    cb[p] = mono + lp * fb;
                    if (++p >= mfx_combLengths[3]) p = 0;
                    mfx_combPos[3] = p;
                    reverbOut += d;
                }

                reverbOut *= (0.25f * mfx_reverbWet);
                L += reverbOut;
                R += reverbOut;
            }

            mfx_haasDelayBuf[mfx_haasWritePos] = R;

            int readPos = mfx_haasWritePos - mfx_haasDelaySamples;
            if (readPos < 0) readPos += MFX_MAX_DELAY;
            float delayedR = mfx_haasDelayBuf[readPos];

            if (++mfx_haasWritePos >= MFX_MAX_DELAY) mfx_haasWritePos = 0;

            L += delayedR * mfx_haasCrossfeed;
            R = delayedR;
        }

        static void mfx_Process(float[] stereoBuffer, int sampleCount)
        {
            fixed (float* pBuf = stereoBuffer)
            {
                int idx = 0;
                for (int i = 0; i < sampleCount; i++, idx += 2)
                {
                    float L = pBuf[idx];
                    float R = pBuf[idx + 1];
                    mfx_ProcessSample(ref L, ref R);
                    pBuf[idx] = L;
                    pBuf[idx + 1] = R;
                }
            }
        }

        static void mfx_Reset()
        {
            apm_ZeroFloat(mfx_haasDelayBuf, MFX_MAX_DELAY);
            mfx_haasWritePos = 0;
            for (int i = 0; i < MFX_COMB_COUNT; i++)
            {
                apm_ZeroFloat(mfx_combBuf[i], mfx_combLengths[i]);
                mfx_combPos[i] = 0;
                mfx_combLpfState[i] = 0f;
            }
        }

        // ==================================================================
        // ModernAudioMixer (mmix_) — 5 軌獨立超採樣立體聲混音器
        // ==================================================================
        const int MMIX_MAX_SAMPLES = 800;

        const int MMIX_NES_CH = 5;   // NES built-in: sq1, sq2, tri, noise, dmc
        const int MMIX_EXP_CH = 8;   // max expansion channels (Namco163 = 8)
        const int MMIX_TOTAL_CH = MMIX_NES_CH + MMIX_EXP_CH; // 13

        static float** mmix_chBuf;
        static float* mmix_basePanL;
        static float* mmix_basePanR;
        static float* mmix_panL;
        static float* mmix_panR;
        static float* mmix_normScale;  // [5] NES channels base normalization (1/15, 1/127)
        static float* mmix_chGain;     // [13] precomputed per-channel gain = normScale × (chVol/100)
        static double mmix_bq_b0, mmix_bq_b1, mmix_bq_b2, mmix_bq_a1, mmix_bq_a2;
        static double mmix_bq_s1, mmix_bq_s2; // Transposed Direct Form II state
        static int mmix_cachedBoostDb;
        static int mmix_cachedBoostFreq;

        static void mmix_InitTables()
        {
            mmix_basePanL = apm_AllocFloat(MMIX_TOTAL_CH);
            mmix_basePanL[0] = 0.7f; mmix_basePanL[1] = 0.3f; mmix_basePanL[2] = 0.5f;
            mmix_basePanL[3] = 0.5f; mmix_basePanL[4] = 0.5f;
            // Expansion channels: default center pan
            for (int i = MMIX_NES_CH; i < MMIX_TOTAL_CH; i++) mmix_basePanL[i] = 0.5f;

            mmix_basePanR = apm_AllocFloat(MMIX_TOTAL_CH);
            mmix_basePanR[0] = 0.3f; mmix_basePanR[1] = 0.7f; mmix_basePanR[2] = 0.5f;
            mmix_basePanR[3] = 0.5f; mmix_basePanR[4] = 0.5f;
            for (int i = MMIX_NES_CH; i < MMIX_TOTAL_CH; i++) mmix_basePanR[i] = 0.5f;

            mmix_normScale = apm_AllocFloat(MMIX_NES_CH);
            mmix_normScale[0] = 1f / 15f;
            mmix_normScale[1] = 1f / 15f;
            mmix_normScale[2] = 1f / 15f;
            mmix_normScale[3] = 1f / 15f;
            mmix_normScale[4] = 1f / 127f;
        }

        static void mmix_Init()
        {
            if (mmix_chBuf != null) return; // 已配置，共用記憶體
            mmix_chBuf = (float**)Marshal.AllocHGlobal(MMIX_TOTAL_CH * sizeof(float*));
            for (int i = 0; i < MMIX_TOTAL_CH; i++)
                mmix_chBuf[i] = apm_AllocFloat(MMIX_MAX_SAMPLES);
            mmix_panL = apm_AllocFloat(MMIX_TOTAL_CH);
            mmix_panR = apm_AllocFloat(MMIX_TOTAL_CH);
            mmix_chGain = apm_AllocFloat(MMIX_TOTAL_CH);
        }

        /// <summary>
        /// 從 ChannelVolume[13] 預算所有聲道增益。
        /// Mode 2: per-channel gain = normScale × (chVol/100) (NES) 或 chipGain/98302 × (chVol/100) (expansion)
        /// Mode 0/1: 從擴展聲道音量平均值計算 per-chip 增益 (ap_mode01ExpGain)
        /// </summary>
        public static void mmix_UpdateChannelGains()
        {
            // Mode 2: NES 聲道 per-channel gain
            for (int i = 0; i < MMIX_NES_CH; i++)
                mmix_chGain[i] = mmix_normScale[i] * (ChannelVolume[i] * 0.01f);

            // Mode 2: 擴展聲道 per-channel gain
            int ct = (int)expansionChipType;
            float baseGain = (ct > 0 && ct < DefaultChipGain.Length)
                ? DefaultChipGain[ct] * (1f / 98302f) : 0f;
            for (int i = 0; i < MMIX_EXP_CH; i++)
                mmix_chGain[MMIX_NES_CH + i] = baseGain * (ChannelVolume[MMIX_NES_CH + i] * 0.01f);

            // Mode 0/1: 擴展聲道 per-chip 平均增益
            int expCount = expansionChannelCount;
            if (expCount > 0 && ct > 0 && ct < DefaultChipGain.Length)
            {
                float avgVol = 0f;
                for (int i = 0; i < expCount; i++)
                    avgVol += ChannelVolume[MMIX_NES_CH + i];
                avgVol = avgVol / expCount * 0.01f;
                ap_mode01ExpGain = DefaultChipGain[ct] * avgVol;
            }
            else
            {
                ap_mode01ExpGain = 0f;
            }
        }

        static void mmix_SetStereoWidth(int width)
        {
            float w = Math.Max(0, Math.Min(100, width)) / 100f;
            for (int ch = 0; ch < MMIX_TOTAL_CH; ch++)
            {
                mmix_panL[ch] = 0.5f + (mmix_basePanL[ch] - 0.5f) * w;
                mmix_panR[ch] = 0.5f + (mmix_basePanR[ch] - 0.5f) * w;
            }
        }

        static void mmix_SetBassBoost(int dB, int freq)
        {
            dB = Math.Max(0, Math.Min(12, dB));
            freq = Math.Max(80, Math.Min(300, freq));
            if (dB == mmix_cachedBoostDb && freq == mmix_cachedBoostFreq) return;
            mmix_cachedBoostDb = dB;
            mmix_cachedBoostFreq = freq;

            if (dB == 0)
            {
                mmix_bq_b0 = 1.0; mmix_bq_b1 = 0; mmix_bq_b2 = 0;
                mmix_bq_a1 = 0; mmix_bq_a2 = 0;
                return;
            }

            double gainDb = dB;
            double Q = 0.707;
            double A = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * freq / 44100.0;
            double sinW0 = Math.Sin(w0);
            double cosW0 = Math.Cos(w0);
            double alpha = sinW0 / (2.0 * Q);
            double sqrtA2alpha = 2.0 * Math.Sqrt(A) * alpha;

            double a0 = (A + 1) + (A - 1) * cosW0 + sqrtA2alpha;
            mmix_bq_b0 = (A * ((A + 1) - (A - 1) * cosW0 + sqrtA2alpha)) / a0;
            mmix_bq_b1 = (2.0 * A * ((A - 1) - (A + 1) * cosW0)) / a0;
            mmix_bq_b2 = (A * ((A + 1) - (A - 1) * cosW0 - sqrtA2alpha)) / a0;
            mmix_bq_a1 = (-2.0 * ((A - 1) + (A + 1) * cosW0)) / a0;
            mmix_bq_a2 = ((A + 1) + (A - 1) * cosW0 - sqrtA2alpha) / a0;

            mmix_bq_s1 = mmix_bq_s2 = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void mmix_PushChannels(int sq1, int sq2, int tri, int noise, int dmc)
        {
            ose_PushSample(1, sq1 * mmix_chGain[0]);
            ose_PushSample(2, sq2 * mmix_chGain[1]);
            ose_PushSample(3, tri * mmix_chGain[2]);
            ose_PushSample(4, noise * mmix_chGain[3]);
            ose_PushSample(5, dmc * mmix_chGain[4]);

            // Expansion channels (oversampler idx 6~13)
            int expCount = expansionChannelCount;
            if (expCount > 0)
            {
                for (int i = 0; i < expCount; i++)
                    ose_PushSample(6 + i, expansionChannels[i] * mmix_chGain[MMIX_NES_CH + i]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool mmix_TryGetStereoSample(out float L, out float R)
        {
            L = R = 0f;

            float s0;
            if (!ose_TryGetSample(1, out s0)) return false;

            float s1, s2, s3, s4;
            ose_TryGetSample(2, out s1);
            ose_TryGetSample(3, out s2);
            ose_TryGetSample(4, out s3);
            ose_TryGetSample(5, out s4);

            // Transposed Direct Form II: 5 mul + 2 state update (fewer memory ops)
            double triIn = s2;
            double triOut = mmix_bq_b0 * triIn + mmix_bq_s1;
            mmix_bq_s1 = mmix_bq_b1 * triIn - mmix_bq_a1 * triOut + mmix_bq_s2;
            mmix_bq_s2 = mmix_bq_b2 * triIn - mmix_bq_a2 * triOut;
            float triMixed = (float)triOut;

            L += s0 * mmix_panL[0]; R += s0 * mmix_panR[0];
            L += s1 * mmix_panL[1]; R += s1 * mmix_panR[1];
            L += triMixed * mmix_panL[2]; R += triMixed * mmix_panR[2];
            L += s3 * mmix_panL[3]; R += s3 * mmix_panR[3];
            L += s4 * mmix_panL[4]; R += s4 * mmix_panR[4];

            // Expansion channels (oversampler idx 6~13)
            int expCount = expansionChannelCount;
            for (int i = 0; i < expCount; i++)
            {
                float es;
                ose_TryGetSample(6 + i, out es);
                int pi = MMIX_NES_CH + i;
                L += es * mmix_panL[pi]; R += es * mmix_panR[pi];
            }

            mfx_ProcessSample(ref L, ref R);

            return true;
        }

        static int mmix_ProcessFrame(float[] stereoOut, int maxStereoSamples)
        {
            int expCount = expansionChannelCount;
            int totalCh = MMIX_NES_CH + expCount;

            int count = ose_Decimate(1, mmix_chBuf[0], MMIX_MAX_SAMPLES);
            for (int ch = 1; ch < MMIX_NES_CH; ch++)
                ose_Decimate(ch + 1, mmix_chBuf[ch], MMIX_MAX_SAMPLES);
            for (int ch = 0; ch < expCount; ch++)
                ose_Decimate(6 + ch, mmix_chBuf[MMIX_NES_CH + ch], MMIX_MAX_SAMPLES);

            int outCount = Math.Min(count, maxStereoSamples);
            float* p0 = mmix_chBuf[0];
            float* p1 = mmix_chBuf[1];
            float* p2 = mmix_chBuf[2];
            float* p3 = mmix_chBuf[3];
            float* p4 = mmix_chBuf[4];

            // ==============================================================
            // Pass 1: 純量處理 IIR 濾波器 (解決狀態相依問題)
            // 直接原地覆寫 ch2 陣列，準備給下一個階段無縫讀取
            // ==============================================================
            for (int i = 0; i < outCount; i++)
            {
                double triSample = p2[i];
                double triOut = mmix_bq_b0 * triSample + mmix_bq_s1;
                mmix_bq_s1 = mmix_bq_b1 * triSample - mmix_bq_a1 * triOut + mmix_bq_s2;
                mmix_bq_s2 = mmix_bq_b2 * triSample - mmix_bq_a2 * triOut;
                p2[i] = (float)triOut;
            }

            // ==============================================================
            // Pass 2: NES 5 channel SIMD 立體聲矩陣混音 + expansion scalar mix
            // ==============================================================
            int vecLen = Vector<float>.Count;
            int iIdx = 0;

            fixed (float* pOut = stereoOut)
            {
                var vPanL0 = new Vector<float>(mmix_panL[0]); var vPanR0 = new Vector<float>(mmix_panR[0]);
                var vPanL1 = new Vector<float>(mmix_panL[1]); var vPanR1 = new Vector<float>(mmix_panR[1]);
                var vPanL2 = new Vector<float>(mmix_panL[2]); var vPanR2 = new Vector<float>(mmix_panR[2]);
                var vPanL3 = new Vector<float>(mmix_panL[3]); var vPanR3 = new Vector<float>(mmix_panR[3]);
                var vPanL4 = new Vector<float>(mmix_panL[4]); var vPanR4 = new Vector<float>(mmix_panR[4]);

                for (; iIdx <= outCount - vecLen; iIdx += vecLen)
                {
                    var v0 = *(Vector<float>*)(p0 + iIdx);
                    var v1 = *(Vector<float>*)(p1 + iIdx);
                    var v2 = *(Vector<float>*)(p2 + iIdx);
                    var v3 = *(Vector<float>*)(p3 + iIdx);
                    var v4 = *(Vector<float>*)(p4 + iIdx);

                    var vL = v0 * vPanL0 + v1 * vPanL1 + v2 * vPanL2 + v3 * vPanL3 + v4 * vPanL4;
                    var vR = v0 * vPanR0 + v1 * vPanR1 + v2 * vPanR2 + v3 * vPanR3 + v4 * vPanR4;

                    float* outPtr = pOut + (iIdx * 2);
                    for (int k = 0; k < vecLen; k++)
                    {
                        outPtr[k * 2] = vL[k];
                        outPtr[k * 2 + 1] = vR[k];
                    }
                }

                for (; iIdx < outCount; iIdx++)
                {
                    float L = p0[iIdx] * mmix_panL[0] + p1[iIdx] * mmix_panL[1] + p2[iIdx] * mmix_panL[2] + p3[iIdx] * mmix_panL[3] + p4[iIdx] * mmix_panL[4];
                    float R = p0[iIdx] * mmix_panR[0] + p1[iIdx] * mmix_panR[1] + p2[iIdx] * mmix_panR[2] + p3[iIdx] * mmix_panR[3] + p4[iIdx] * mmix_panR[4];
                    pOut[iIdx * 2] = L;
                    pOut[iIdx * 2 + 1] = R;
                }

                // Pass 3: add expansion channels (scalar, since count is dynamic)
                for (int ec = 0; ec < expCount; ec++)
                {
                    float* pe = mmix_chBuf[MMIX_NES_CH + ec];
                    int pi = MMIX_NES_CH + ec;
                    float pL = mmix_panL[pi], pR = mmix_panR[pi];
                    for (int i = 0; i < outCount; i++)
                    {
                        pOut[i * 2]     += pe[i] * pL;
                        pOut[i * 2 + 1] += pe[i] * pR;
                    }
                }
            }

            return outCount;
        }

        static void mmix_ResetBiquad()
        {
            mmix_bq_s1 = mmix_bq_s2 = 0;
        }

        // ==================================================================
        // OversamplingEngine (ose_) — 6 instances indexed
        // idx 0 = authentic, idx 1~5 = modern NES ch0~ch4, idx 6~13 = expansion ch0~ch7
        // ==================================================================
        const int OSE_COUNT = 14;
        const int OSE_TAPS = 256;
        const int OSE_PHASES = 128;
        const int OSE_HALF_TAPS = OSE_TAPS / 2;
        const int OSE_BUF_SIZE = 65536;
        const int OSE_BUF_MASK = OSE_BUF_SIZE - 1;
        const double OSE_CUTOFF_NORM = 20000.0 / AP_CPU_FREQ;
        const uint OSE_ONE_CLOCK_FP = 1 << 16;
        const uint OSE_CLOCKS_PER_SAMPLE_FP = (uint)(AP_CLOCKS_PER_SAMPLE * OSE_ONE_CLOCK_FP);

        static float* ose_kernelFlat;
        static float** ose_ringBuf;    // [OSE_COUNT] pointers
        static int* ose_writePos;      // [OSE_COUNT]
        static uint* ose_inputPhaseFp; // [OSE_COUNT]

        static void ose_InitTables()
        {
            ose_kernelFlat = apm_AllocFloat(OSE_PHASES * OSE_TAPS);
            for (int p = 0; p < OSE_PHASES; p++)
            {
                int pOffset = p * OSE_TAPS;
                double fraction = (double)p / OSE_PHASES;
                double sum = 0.0;

                for (int i = 0; i < OSE_TAPS; i++)
                {
                    double x = (i - OSE_HALF_TAPS) - fraction;

                    // 修正：x≈0 極限值為 2.0*CUTOFF_NORM（normalized sinc 的正確定義）
                    double sinc = (Math.Abs(x) < 1e-9)
                        ? (2.0 * OSE_CUTOFF_NORM)
                        : Math.Sin(2.0 * Math.PI * OSE_CUTOFF_NORM * x) / (Math.PI * x);

                    double wPhase = (x + OSE_HALF_TAPS) / (double)OSE_TAPS;
                    double window = 0.42
                        - 0.50 * Math.Cos(2.0 * Math.PI * wPhase)
                        + 0.08 * Math.Cos(4.0 * Math.PI * wPhase);

                    double val = sinc * window;
                    ose_kernelFlat[pOffset + i] = (float)val;
                    sum += val;
                }

                if (Math.Abs(sum) > 1e-12)
                {
                    float invSum = (float)(1.0 / sum);
                    for (int i = 0; i < OSE_TAPS; i++)
                        ose_kernelFlat[pOffset + i] *= invSum;
                }
            }
        }

        static void ose_InitInstances()
        {
            if (ose_ringBuf != null) return; // 已配置，共用記憶體
            int ringSize = OSE_BUF_SIZE + OSE_TAPS;
            ose_ringBuf = (float**)Marshal.AllocHGlobal(OSE_COUNT * sizeof(float*));
            ose_writePos = apm_AllocInt(OSE_COUNT);
            ose_inputPhaseFp = apm_AllocUint(OSE_COUNT);
            for (int i = 0; i < OSE_COUNT; i++)
                ose_ringBuf[i] = apm_AllocFloat(ringSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ose_PushSample(int idx, float voltage)
        {
            float* ring = ose_ringBuf[idx];
            int wp = ose_writePos[idx];

            ring[wp] = voltage;
            if (wp < OSE_TAPS)
                ring[OSE_BUF_SIZE + wp] = voltage;

            ose_writePos[idx] = (wp + 1) & OSE_BUF_MASK;
            ose_inputPhaseFp[idx] += OSE_ONE_CLOCK_FP;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool ose_TryGetSample(int idx, out float result)
        {
            result = 0f;
            if (ose_inputPhaseFp[idx] < OSE_CLOCKS_PER_SAMPLE_FP) return false;

            ose_inputPhaseFp[idx] -= OSE_CLOCKS_PER_SAMPLE_FP;

            int intPhase = (int)(ose_inputPhaseFp[idx] >> 16);
            uint fracFp = ose_inputPhaseFp[idx] & 0xFFFF;
            int phaseIdx = (int)(fracFp >> 9);
            int startIdx = (ose_writePos[idx] - 1 - intPhase - OSE_HALF_TAPS) & OSE_BUF_MASK;

            result = ose_Convolve(idx, phaseIdx, startIdx);
            return true;
        }

        static int ose_Decimate(int idx, float* output, int maxCount)
        {
            int produced = 0;
            float sample;
            while (produced < maxCount && ose_TryGetSample(idx, out sample))
                output[produced++] = sample;
            return produced;
        }

        static void ose_Reset(int idx)
        {
            apm_ZeroFloat(ose_ringBuf[idx], OSE_BUF_SIZE + OSE_TAPS);
            ose_writePos[idx] = 0;
            ose_inputPhaseFp[idx] = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float ose_Convolve(int idx, int phaseIdx, int startIdx)
        {
            int vecLen = Vector<float>.Count;
            int kOffset = phaseIdx * OSE_TAPS;

            float* pRingBuf = ose_ringBuf[idx] + startIdx;
            float* pKernel = ose_kernelFlat + kOffset;

            if (Vector.IsHardwareAccelerated)
            {
                Vector<float> vSum0 = Vector<float>.Zero;
                Vector<float> vSum1 = Vector<float>.Zero;
                int stride2 = vecLen * 2;
                int i = 0;

                // Unroll 2×: 雙累加器隱藏 load latency，提升 ILP
                for (; i <= OSE_TAPS - stride2; i += stride2)
                {
                    vSum0 += *(Vector<float>*)(pRingBuf + i) * *(Vector<float>*)(pKernel + i);
                    vSum1 += *(Vector<float>*)(pRingBuf + i + vecLen) * *(Vector<float>*)(pKernel + i + vecLen);
                }

                // 處理剩餘的完整 vector
                for (; i <= OSE_TAPS - vecLen; i += vecLen)
                    vSum0 += *(Vector<float>*)(pRingBuf + i) * *(Vector<float>*)(pKernel + i);

                float acc = Vector.Dot(vSum0 + vSum1, Vector<float>.One);

                for (; i < OSE_TAPS; i++)
                    acc += pRingBuf[i] * pKernel[i];

                return acc;
            }
            else
            {
                float acc = 0f;
                for (int i = 0; i < OSE_TAPS; i++)
                    acc += pRingBuf[i] * pKernel[i];
                return acc;
            }
        }
    }
}
