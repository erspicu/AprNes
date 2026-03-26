using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace AprNes
{
    // =========================================================================
    // AudioPlusMem — 非託管記憶體配置輔助
    // =========================================================================
    static unsafe class AudioPlusMem
    {
        public static float* AllocFloat(int count)
        {
            float* p = (float*)Marshal.AllocHGlobal(count * sizeof(float));
            for (int i = 0; i < count; i++) p[i] = 0f;
            return p;
        }
        public static double* AllocDouble(int count)
        {
            double* p = (double*)Marshal.AllocHGlobal(count * sizeof(double));
            for (int i = 0; i < count; i++) p[i] = 0.0;
            return p;
        }
        public static int* AllocInt(int count)
        {
            int* p = (int*)Marshal.AllocHGlobal(count * sizeof(int));
            for (int i = 0; i < count; i++) p[i] = 0;
            return p;
        }
        public static void ZeroFloat(float* p, int count)
        {
            for (int i = 0; i < count; i++) p[i] = 0f;
        }
        public static void ZeroInt(int* p, int count)
        {
            for (int i = 0; i < count; i++) p[i] = 0;
        }
    }

    // =========================================================================
    // AudioDispatcher — AudioPlus 總調度器
    // =========================================================================
    static class AudioDispatcher
    {
        static AuthenticAudioMixer authenticMixer;
        static OversamplingEngine authenticOversampler;
        static ConsoleModelFilter authenticModelFilter;

        static ModernAudioMixer modernMixer;
        static ModernAudioFX modernFX;

        static bool initialized = false;

        public static void Init()
        {
            authenticMixer = new AuthenticAudioMixer();
            authenticOversampler = new OversamplingEngine();
            authenticModelFilter = new ConsoleModelFilter();

            modernMixer = new ModernAudioMixer();
            modernFX = new ModernAudioFX();

            initialized = true;
            ApplySettings();
        }

        public static void ApplySettings()
        {
            if (!initialized) return;

            authenticModelFilter.SetModel(NesCore.ConsoleModel, NesCore.CustomLpfCutoff, NesCore.CustomBuzz);
            authenticModelFilter.SetBuzzParams(NesCore.BuzzFreq, NesCore.BuzzAmplitude);
            authenticModelFilter.SetRfVolume(NesCore.RfVolume);

            modernMixer.SetStereoWidth(NesCore.StereoWidth);
            modernMixer.SetBassBoost(NesCore.BassBoostDb, NesCore.BassBoostFreq);
            modernFX.SetHaasDelay(NesCore.HaasDelay);
            modernFX.SetHaasCrossfeed(NesCore.HaasCrossfeed);
            modernFX.SetReverbWet(NesCore.ReverbWet);
            modernFX.SetCombFeedback(NesCore.CombFeedback);
            modernFX.SetCombDamp(NesCore.CombDamp);
        }

        public static void Reset()
        {
            if (!initialized) return;
            authenticMixer.Reset();
            authenticOversampler.Reset();
            authenticModelFilter.Reset();
            modernMixer.Reset();
            modernFX.Reset();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PushApuCycle(
            int sq1, int sq2, int tri, int noise, int dmc,
            int expansionAudio)
        {
            if (!NesCore.AudioEnabled || !initialized) return;

            int mode = NesCore.AudioMode;

            if (mode == 1)
            {
                // 1. 取得帶有真實 DC Offset 的物理電壓 (已移除 1.79MHz HPF)
                float voltage = authenticMixer.GetVoltage(sq1, sq2, tri, noise, dmc, expansionAudio);

                // 2. 完整保留 DC 特性，送入超採樣引擎 (FIR 降頻為線性操作，無損通過)
                authenticOversampler.PushSample(voltage);

                float sample;
                if (authenticOversampler.TryGetSample(out sample))
                {
                    // 3. 降頻至 44.1kHz 後，在這裡消除 DC Offset 並加上主機濾波
                    sample = authenticModelFilter.Process(sample, NesCore.RfCrosstalk);
                    OutputStereo(sample, sample);
                }
            }
            else if (mode == 2)
            {
                modernMixer.PushChannels(sq1, sq2, tri, noise, dmc);

                float sampleL, sampleR;
                if (modernMixer.TryGetStereoSample(out sampleL, out sampleR, modernFX))
                    OutputStereo(sampleL, sampleR);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void OutputStereo(float left, float right)
        {
            const float GAIN = 40000f;
            float volumeScale = NesCore.Volume * 0.01f;

            int scaledL = Clamp16((int)(left * GAIN * volumeScale));
            int scaledR = Clamp16((int)(right * GAIN * volumeScale));

            NesCore.AudioSampleReady?.Invoke((short)scaledL, (short)scaledR);

            if (NesCore.AnalogEnabled && NesCore.AnalogOutput == AnalogOutputMode.RF)
            {
                int mono = (scaledL + scaledR) / 2;
                float absS = mono < 0 ? -mono / 32767f : mono / 32767f;
                NesCore.RfAudioLevel = NesCore.RfAudioLevel * 0.95f + absS * 0.05f;
                NesCore.RfBuzzPhase = (NesCore.RfBuzzPhase + absS * 0.0001f) % 1.0f;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Clamp16(int sample)
        {
            if ((uint)(sample + 32768) <= 65535u)
                return sample;

            return sample < 0 ? -32768 : 32767;
        }
    }

    // =========================================================================
    // AuthenticAudioMixer — 物理級 DAC 非線性混音器 (純物理電壓版)
    // =========================================================================
    unsafe class AuthenticAudioMixer
    {
        static readonly float* pulseTable;
        static readonly float* tndTable;

        static AuthenticAudioMixer()
        {
            pulseTable = AudioPlusMem.AllocFloat(31);
            pulseTable[0] = 0f;
            for (int i = 1; i < 31; i++)
                pulseTable[i] = (float)(95.88 / (8128.0 / i + 100.0));

            int tndSize = 16 * 16 * 128;
            tndTable = AudioPlusMem.AllocFloat(tndSize);
            for (int t = 0; t < 16; t++)
            {
                for (int n = 0; n < 16; n++)
                {
                    for (int d = 0; d < 128; d++)
                    {
                        double sum = t / 8227.0 + n / 12241.0 + d / 22638.0;
                        float val = (sum < 1e-12) ? 0f : (float)(159.79 / (1.0 / sum + 100.0));
                        tndTable[(t << 11) | (n << 7) | d] = val;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetVoltage(int sq1, int sq2, int tri, int noise, int dmc, int expansionAudio)
        {
            int pulseIdx = sq1 + sq2;
            if (pulseIdx > 30) pulseIdx = 30;

            float pulse = pulseTable[pulseIdx];
            float tnd = tndTable[(tri << 11) | (noise << 7) | dmc];
            double raw = pulse + tnd + (expansionAudio / 98302.0);

            // 成為最純粹的物理電壓計算機，不再這裡做 HPF 破壞浮點數精度
            return (float)raw;
        }

        public void Reset()
        {
            // 已無狀態
        }
    }

    // =========================================================================
    // BlipSynthesizer — 帶限步階合成器 (Blip Buffer)
    // =========================================================================
    unsafe class BlipSynthesizer
    {
        const int PHASES = 32;
        const int TAPS = 16;
        const int HALF_TAPS = TAPS / 2;
        const int BUF_SIZE = 2048;
        const int BUF_MASK = BUF_SIZE - 1;

        const double CPU_FREQ = 1789772.72;
        const int SAMPLE_RATE = 44100;
        const double CLOCKS_PER_SAMPLE = CPU_FREQ / SAMPLE_RATE;

        static readonly float* stepTable;

        static BlipSynthesizer()
        {
            stepTable = AudioPlusMem.AllocFloat(PHASES * TAPS);

            for (int p = 0; p < PHASES; p++)
            {
                int pOffset = p * TAPS;
                double phase = (double)p / PHASES;
                double sum = 0.0;

                for (int i = 0; i < TAPS; i++)
                {
                    double x = (i - HALF_TAPS) - phase;

                    double sinc;
                    if (Math.Abs(x) < 1e-9)
                        sinc = 1.0;
                    else
                        sinc = Math.Sin(Math.PI * x) / (Math.PI * x);

                    double wPhase = (x + HALF_TAPS) / TAPS;
                    double window = 0.42
                                  - 0.50 * Math.Cos(2.0 * Math.PI * wPhase)
                                  + 0.08 * Math.Cos(4.0 * Math.PI * wPhase);

                    sum += sinc * window;
                    stepTable[pOffset + i] = (float)sum;
                }

                if (Math.Abs(sum) > 1e-12)
                {
                    float invSum = (float)(1.0 / sum);
                    for (int i = 0; i < TAPS; i++)
                        stepTable[pOffset + i] *= invSum;
                }
            }
        }

        float* buffer;
        int readPos = 0;
        int writeBase = 0;
        double clockAccum = 0.0;
        float lastAmplitude = 0f;
        double integrator = 0.0;
        const double INTEGRATOR_LEAK = 0.99997;

        public BlipSynthesizer()
        {
            buffer = AudioPlusMem.AllocFloat(BUF_SIZE);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddDelta(float newAmplitude)
        {
            float delta = newAmplitude - lastAmplitude;
            if (Math.Abs(delta) < 1e-8f)
                return;
            lastAmplitude = newAmplitude;

            double exactPos = clockAccum / CLOCKS_PER_SAMPLE;
            int sampleIdx = (int)exactPos;
            double frac = exactPos - sampleIdx;

            int phaseIdx = (int)(frac * PHASES);
            if (phaseIdx >= PHASES) phaseIdx = PHASES - 1;

            int baseIdx = (writeBase + sampleIdx) & BUF_MASK;
            for (int i = 0; i < TAPS; i++)
                buffer[(baseIdx + i) & BUF_MASK] += delta * stepTable[phaseIdx * TAPS + i];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClockAdvance()
        {
            clockAccum += 1.0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetSample(out float result)
        {
            result = 0f;
            if (clockAccum < CLOCKS_PER_SAMPLE) return false;

            clockAccum -= CLOCKS_PER_SAMPLE;

            float delta = buffer[readPos];
            buffer[readPos] = 0f;

            integrator = integrator * INTEGRATOR_LEAK + delta;
            result = (float)integrator;

            readPos = (readPos + 1) & BUF_MASK;
            writeBase = readPos;

            return true;
        }

        public void Reset()
        {
            AudioPlusMem.ZeroFloat(buffer, BUF_SIZE);
            readPos = 0;
            writeBase = 0;
            clockAccum = 0.0;
            lastAmplitude = 0f;
            integrator = 0.0;
        }
    }

    // =========================================================================
    // ConsoleModelFilter — 主機型號差異濾波器 (包含 44.1kHz 完美 90Hz HPF)
    // =========================================================================
    unsafe class ConsoleModelFilter
    {
        const double DT = 1.0 / 44100.0;

        static readonly double* presetCutoffs;
        static readonly byte* presetBuzz;

        static ConsoleModelFilter()
        {
            presetCutoffs = AudioPlusMem.AllocDouble(6);
            presetCutoffs[0] = 14000; presetCutoffs[1] = 4700; presetCutoffs[2] = 20000;
            presetCutoffs[3] = 19000; presetCutoffs[4] = 12000; presetCutoffs[5] = 16000;

            presetBuzz = (byte*)Marshal.AllocHGlobal(6);
            presetBuzz[0] = 0; presetBuzz[1] = 0; presetBuzz[2] = 1;
            presetBuzz[3] = 0; presetBuzz[4] = 0; presetBuzz[5] = 0;
        }

        // --- 44.1kHz DC Blocker (90Hz HPF) 參數 ---
        float hpfState = 0f;
        float hpfPrev = 0f;
        // ~90Hz @ 44.1kHz 的衰減係數，浮點數極度健康
        const float HPF_ALPHA_44K = 0.9872f;

        float lpfState = 0f;
        float currentBeta;
        int currentModel = 0;
        bool buzzEnabled = false;

        double buzzPhase = 0.0;
        double buzzPhaseInc;
        float buzzAmplitude;

        double rfPhase = 0.0;
        const double RF_PHASE_INC = 59.94 / 44100.0;
        float rfBaseVolume;
        float videoLuminance = 0f;

        public ConsoleModelFilter()
        {
            SetBuzzParams(60, 30);
            SetRfVolume(50);
            SetModel(0, 14000, false);
        }

        static float CalcBeta(double cutoffHz)
        {
            double rc = 1.0 / (2.0 * Math.PI * cutoffHz);
            return (float)(DT / (rc + DT));
        }

        public void SetModel(int model, int customCutoff, bool customBuzz)
        {
            currentModel = Math.Max(0, Math.Min(6, model));

            if (currentModel < 6)
            {
                currentBeta = CalcBeta(presetCutoffs[currentModel]);
                buzzEnabled = presetBuzz[currentModel] != 0;
            }
            else
            {
                int cutoff = Math.Max(1000, Math.Min(22000, customCutoff));
                currentBeta = CalcBeta(cutoff);
                buzzEnabled = customBuzz;
            }
        }

        public void SetBuzzParams(int freq, int amplitude)
        {
            int f = (freq == 50) ? 50 : 60;
            buzzPhaseInc = f / 44100.0;
            buzzAmplitude = Math.Max(0, Math.Min(100, amplitude)) * 0.0001f;
        }

        public void SetRfVolume(int volume)
        {
            rfBaseVolume = Math.Max(0, Math.Min(200, volume)) * 0.001f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetVideoLuminance(float luma)
        {
            videoLuminance = luma;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Process(float input, bool rfCrosstalk)
        {
            // 1. 在 44.1kHz 進行完美無損的 DC Offset 消除 (90Hz HPF)
            float diff = input - hpfPrev;
            hpfPrev = input;
            hpfState = HPF_ALPHA_44K * (hpfState + diff);

            float cleanInput = hpfState;

            // 2. 進行主機型號 LPF 濾波
            lpfState += currentBeta * (cleanInput - lpfState);
            float output = lpfState;

            // 3. 疊加物理缺陷雜音
            if (buzzEnabled && buzzAmplitude > 0f)
            {
                buzzPhase += buzzPhaseInc;
                if (buzzPhase >= 1.0) buzzPhase -= 1.0;
                output += (float)(Math.Sin(2.0 * Math.PI * buzzPhase)) * buzzAmplitude;
            }

            if (rfCrosstalk && rfBaseVolume > 0f)
            {
                rfPhase += RF_PHASE_INC;
                if (rfPhase >= 1.0) rfPhase -= 1.0;
                float sawtooth = (float)(rfPhase - 0.5) * 2f;
                output += sawtooth * videoLuminance * rfBaseVolume;
            }

            return output;
        }

        public void Reset()
        {
            lpfState = 0f;
            buzzPhase = 0.0;
            rfPhase = 0.0;
            hpfState = 0f;
            hpfPrev = 0f;
        }
    }

    // =========================================================================
    // ModernAudioFX — 空間效果處理器
    // =========================================================================
    unsafe class ModernAudioFX
    {
        const int SAMPLE_RATE = 44100;
        const int MAX_DELAY = (int)(SAMPLE_RATE * 0.035);

        float* haasDelayBuf;
        int haasWritePos = 0;
        int haasDelaySamples;
        float haasCrossfeed = 0.4f;

        static readonly int* combLengths;
        const int COMB_COUNT = 4;
        float** combBuf;
        int* combPos;
        float* combLpfState;
        float combFeedback = 0.7f;
        float combDamp = 0.3f;

        float reverbWet = 0f;

        static ModernAudioFX()
        {
            combLengths = AudioPlusMem.AllocInt(4);
            combLengths[0] = 1116; combLengths[1] = 1188;
            combLengths[2] = 1277; combLengths[3] = 1356;
        }

        public ModernAudioFX()
        {
            haasDelayBuf = AudioPlusMem.AllocFloat(MAX_DELAY);
            combBuf = (float**)Marshal.AllocHGlobal(COMB_COUNT * sizeof(float*));
            combPos = AudioPlusMem.AllocInt(COMB_COUNT);
            combLpfState = AudioPlusMem.AllocFloat(COMB_COUNT);
            for (int i = 0; i < COMB_COUNT; i++)
            {
                combBuf[i] = AudioPlusMem.AllocFloat(combLengths[i]);
                combPos[i] = 0;
                combLpfState[i] = 0f;
            }
            SetHaasDelay(20);
            SetHaasCrossfeed(40);
            SetReverbWet(0);
            SetCombFeedback(70);
            SetCombDamp(30);
        }

        public void SetHaasDelay(int ms)
        {
            ms = Math.Max(10, Math.Min(30, ms));
            haasDelaySamples = SAMPLE_RATE * ms / 1000;
        }

        public void SetHaasCrossfeed(int percent)
        {
            haasCrossfeed = Math.Max(0, Math.Min(80, percent)) / 100f;
        }

        public void SetReverbWet(int percent)
        {
            reverbWet = Math.Max(0, Math.Min(30, percent)) / 100f;
        }

        public void SetCombFeedback(int percent)
        {
            combFeedback = Math.Max(30, Math.Min(90, percent)) / 100f;
        }

        public void SetCombDamp(int percent)
        {
            combDamp = Math.Max(10, Math.Min(70, percent)) / 100f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ProcessSample(ref float L, ref float R)
        {
            if (reverbWet > 0f)
            {
                float mono = (L + R) * 0.5f;
                float reverbOut = 0f;

                for (int c = 0; c < COMB_COUNT; c++)
                {
                    float* comb = combBuf[c];
                    int pos = combPos[c];

                    float delayed = comb[pos];
                    float lpf = delayed + combDamp * (combLpfState[c] - delayed);
                    combLpfState[c] = lpf;
                    comb[pos] = mono + lpf * combFeedback;

                    pos++;
                    if (pos >= combLengths[c]) pos = 0;
                    combPos[c] = pos;

                    reverbOut += delayed;
                }

                reverbOut *= (1f / COMB_COUNT);
                float wet = reverbWet;
                L += reverbOut * wet;
                R += reverbOut * wet;
            }

            haasDelayBuf[haasWritePos] = R;

            int readPos = haasWritePos - haasDelaySamples;
            if (readPos < 0) readPos += MAX_DELAY;
            float delayedR = haasDelayBuf[readPos];

            haasWritePos++;
            if (haasWritePos >= MAX_DELAY) haasWritePos = 0;

            L += delayedR * haasCrossfeed;
            R = delayedR;
        }

        public void Process(float[] stereoBuffer, int sampleCount)
        {
            fixed (float* pBuf = stereoBuffer)
            {
                int idx = 0;
                for (int i = 0; i < sampleCount; i++, idx += 2)
                {
                    float L = pBuf[idx];
                    float R = pBuf[idx + 1];
                    ProcessSample(ref L, ref R);
                    pBuf[idx] = L;
                    pBuf[idx + 1] = R;
                }
            }
        }

        public void Reset()
        {
            AudioPlusMem.ZeroFloat(haasDelayBuf, MAX_DELAY);
            haasWritePos = 0;
            for (int i = 0; i < COMB_COUNT; i++)
            {
                AudioPlusMem.ZeroFloat(combBuf[i], combLengths[i]);
                combPos[i] = 0;
                combLpfState[i] = 0f;
            }
        }
    }

    // =========================================================================
    // ModernAudioMixer — 5 軌獨立超採樣立體聲混音器
    // =========================================================================
    unsafe class ModernAudioMixer
    {
        readonly OversamplingEngine[] engines = new OversamplingEngine[5];
        float** chBuf;
        const int MAX_SAMPLES = 800;

        static readonly float* basePanL;
        static readonly float* basePanR;

        float* panL;
        float* panR;

        double bq_b0, bq_b1, bq_b2, bq_a1, bq_a2;
        double bq_x1, bq_x2, bq_y1, bq_y2;
        int cachedBoostDb = -1;
        int cachedBoostFreq = -1;

        static readonly float* normScale;

        static ModernAudioMixer()
        {
            basePanL = AudioPlusMem.AllocFloat(5);
            basePanL[0] = 0.7f; basePanL[1] = 0.3f; basePanL[2] = 0.5f;
            basePanL[3] = 0.5f; basePanL[4] = 0.5f;

            basePanR = AudioPlusMem.AllocFloat(5);
            basePanR[0] = 0.3f; basePanR[1] = 0.7f; basePanR[2] = 0.5f;
            basePanR[3] = 0.5f; basePanR[4] = 0.5f;

            normScale = AudioPlusMem.AllocFloat(5);
            normScale[0] = 1f / 15f;
            normScale[1] = 1f / 15f;
            normScale[2] = 1f / 15f;
            normScale[3] = 1f / 15f;
            normScale[4] = 1f / 127f;
        }

        public ModernAudioMixer()
        {
            chBuf = (float**)Marshal.AllocHGlobal(5 * sizeof(float*));
            for (int i = 0; i < 5; i++)
            {
                engines[i] = new OversamplingEngine();
                chBuf[i] = AudioPlusMem.AllocFloat(MAX_SAMPLES);
            }
            panL = AudioPlusMem.AllocFloat(5);
            panR = AudioPlusMem.AllocFloat(5);
            SetStereoWidth(50);
            SetBassBoost(0, 150);
        }

        public void SetStereoWidth(int width)
        {
            float w = Math.Max(0, Math.Min(100, width)) / 100f;
            for (int ch = 0; ch < 5; ch++)
            {
                panL[ch] = 0.5f + (basePanL[ch] - 0.5f) * w;
                panR[ch] = 0.5f + (basePanR[ch] - 0.5f) * w;
            }
        }

        public void SetBassBoost(int dB, int freq)
        {
            dB = Math.Max(0, Math.Min(12, dB));
            freq = Math.Max(80, Math.Min(300, freq));
            if (dB == cachedBoostDb && freq == cachedBoostFreq) return;
            cachedBoostDb = dB;
            cachedBoostFreq = freq;

            if (dB == 0)
            {
                bq_b0 = 1.0; bq_b1 = 0; bq_b2 = 0;
                bq_a1 = 0; bq_a2 = 0;
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
            bq_b0 = (A * ((A + 1) - (A - 1) * cosW0 + sqrtA2alpha)) / a0;
            bq_b1 = (2.0 * A * ((A - 1) - (A + 1) * cosW0)) / a0;
            bq_b2 = (A * ((A + 1) - (A - 1) * cosW0 - sqrtA2alpha)) / a0;
            bq_a1 = (-2.0 * ((A - 1) + (A + 1) * cosW0)) / a0;
            bq_a2 = ((A + 1) + (A - 1) * cosW0 - sqrtA2alpha) / a0;

            bq_x1 = bq_x2 = bq_y1 = bq_y2 = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushChannels(int sq1, int sq2, int tri, int noise, int dmc)
        {
            engines[0].PushSample(sq1 * normScale[0]);
            engines[1].PushSample(sq2 * normScale[1]);
            engines[2].PushSample(tri * normScale[2]);
            engines[3].PushSample(noise * normScale[3]);
            engines[4].PushSample(dmc * normScale[4]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetStereoSample(out float L, out float R, ModernAudioFX fx)
        {
            L = R = 0f;

            float s0;
            if (!engines[0].TryGetSample(out s0)) return false;

            float s1, s2, s3, s4;
            engines[1].TryGetSample(out s1);
            engines[2].TryGetSample(out s2);
            engines[3].TryGetSample(out s3);
            engines[4].TryGetSample(out s4);

            double triIn = s2;
            double triOut = bq_b0 * triIn + bq_b1 * bq_x1 + bq_b2 * bq_x2
                          - bq_a1 * bq_y1 - bq_a2 * bq_y2;
            bq_x2 = bq_x1; bq_x1 = triIn;
            bq_y2 = bq_y1; bq_y1 = triOut;
            float triMixed = (float)triOut;

            L += s0 * panL[0]; R += s0 * panR[0];
            L += s1 * panL[1]; R += s1 * panR[1];
            L += triMixed * panL[2]; R += triMixed * panR[2];
            L += s3 * panL[3]; R += s3 * panR[3];
            L += s4 * panL[4]; R += s4 * panR[4];

            if (fx != null)
                fx.ProcessSample(ref L, ref R);

            return true;
        }

        public int ProcessFrame(float[] stereoOut, int maxStereoSamples)
        {
            int count = engines[0].Decimate(chBuf[0], MAX_SAMPLES);
            for (int ch = 1; ch < 5; ch++)
                engines[ch].Decimate(chBuf[ch], MAX_SAMPLES);

            int outCount = Math.Min(count, maxStereoSamples);
            float* p0 = chBuf[0];
            float* p1 = chBuf[1];
            float* p2 = chBuf[2];
            float* p3 = chBuf[3];
            float* p4 = chBuf[4];

            // ==============================================================
            // Pass 1: 純量處理 IIR 濾波器 (解決狀態相依問題)
            // 直接原地覆寫 ch2 陣列，準備給下一個階段無縫讀取
            // ==============================================================
            for (int i = 0; i < outCount; i++)
            {
                double triSample = p2[i];
                double triOut = bq_b0 * triSample + bq_b1 * bq_x1 + bq_b2 * bq_x2
                              - bq_a1 * bq_y1 - bq_a2 * bq_y2;
                bq_x2 = bq_x1; bq_x1 = triSample;
                bq_y2 = bq_y1; bq_y1 = triOut;
                p2[i] = (float)triOut; // 原地更新
            }

            // ==============================================================
            // Pass 2: SIMD 5軌立體聲矩陣混音 (算力碾壓)
            // ==============================================================
            int vecLen = Vector<float>.Count;
            int iIdx = 0;

            fixed (float* pOut = stereoOut)
            {
                // 將平移係數廣播 (Broadcast) 到 SIMD 向量中
                var vPanL0 = new Vector<float>(panL[0]); var vPanR0 = new Vector<float>(panR[0]);
                var vPanL1 = new Vector<float>(panL[1]); var vPanR1 = new Vector<float>(panR[1]);
                var vPanL2 = new Vector<float>(panL[2]); var vPanR2 = new Vector<float>(panR[2]);
                var vPanL3 = new Vector<float>(panL[3]); var vPanR3 = new Vector<float>(panR[3]);
                var vPanL4 = new Vector<float>(panL[4]); var vPanR4 = new Vector<float>(panR[4]);

                for (; iIdx <= outCount - vecLen; iIdx += vecLen)
                {
                    // 單指令極速載入 5 個頻道的樣本
                    var v0 = *(Vector<float>*)(p0 + iIdx);
                    var v1 = *(Vector<float>*)(p1 + iIdx);
                    var v2 = *(Vector<float>*)(p2 + iIdx); // 此時讀到的已經是過濾好的低音
                    var v3 = *(Vector<float>*)(p3 + iIdx);
                    var v4 = *(Vector<float>*)(p4 + iIdx);

                    // SIMD 矩陣相乘與加總 (完美契合 CPU 的 FMA 指令)
                    var vL = v0 * vPanL0 + v1 * vPanL1 + v2 * vPanL2 + v3 * vPanL3 + v4 * vPanL4;
                    var vR = v0 * vPanR0 + v1 * vPanR1 + v2 * vPanR2 + v3 * vPanR3 + v4 * vPanR4;

                    // 交錯寫入 (Interleave) 到立體聲輸出緩衝區
                    float* outPtr = pOut + (iIdx * 2);
                    for (int k = 0; k < vecLen; k++)
                    {
                        outPtr[k * 2] = vL[k];
                        outPtr[k * 2 + 1] = vR[k];
                    }
                }

                // ==============================================================
                // Epilogue: 處理剩餘的尾數樣本
                // ==============================================================
                for (; iIdx < outCount; iIdx++)
                {
                    float L = p0[iIdx] * panL[0] + p1[iIdx] * panL[1] + p2[iIdx] * panL[2] + p3[iIdx] * panL[3] + p4[iIdx] * panL[4];
                    float R = p0[iIdx] * panR[0] + p1[iIdx] * panR[1] + p2[iIdx] * panR[2] + p3[iIdx] * panR[3] + p4[iIdx] * panR[4];
                    pOut[iIdx * 2] = L;
                    pOut[iIdx * 2 + 1] = R;
                }
            }

            return outCount;
        }

        public void Reset()
        {
            for (int i = 0; i < 5; i++)
                engines[i].Reset();
            bq_x1 = bq_x2 = bq_y1 = bq_y2 = 0;
        }
    }

    // =========================================================================
    // OversamplingEngine — 原生取樣率超採樣引擎 (.NET 4.8.1 終極極限版)
    // =========================================================================
    unsafe class OversamplingEngine
    {
        const int TAPS = 256;
        const int PHASES = 128;
        const int HALF_TAPS = TAPS / 2;
        const int BUF_SIZE = 65536;
        const int BUF_MASK = BUF_SIZE - 1;

        const double CPU_FREQ = 1789772.72;
        const int SAMPLE_RATE = 44100;
        const double CLOCKS_PER_SAMPLE = CPU_FREQ / SAMPLE_RATE;
        const double CUTOFF_NORM = 20000.0 / CPU_FREQ;

        const uint ONE_CLOCK_FP = 1 << 16;
        const uint CLOCKS_PER_SAMPLE_FP = (uint)(CLOCKS_PER_SAMPLE * ONE_CLOCK_FP);

        static readonly float* kernelFlat;

        static OversamplingEngine()
        {
            kernelFlat = AudioPlusMem.AllocFloat(PHASES * TAPS);
            for (int p = 0; p < PHASES; p++)
            {
                int pOffset = p * TAPS;
                double fraction = (double)p / PHASES;
                double sum = 0.0;

                for (int i = 0; i < TAPS; i++)
                {
                    double x = (i - HALF_TAPS) - fraction;

                    // 修正：x≈0 極限值為 2.0*CUTOFF_NORM（normalized sinc 的正確定義）
                    double sinc = (Math.Abs(x) < 1e-9) ? (2.0 * CUTOFF_NORM) : Math.Sin(2.0 * Math.PI * CUTOFF_NORM * x) / (Math.PI * x);

                    double wPhase = (x + HALF_TAPS) / (double)TAPS;
                    double window = 0.42 - 0.50 * Math.Cos(2.0 * Math.PI * wPhase) + 0.08 * Math.Cos(4.0 * Math.PI * wPhase);

                    double val = sinc * window;
                    kernelFlat[pOffset + i] = (float)val;
                    sum += val;
                }

                if (Math.Abs(sum) > 1e-12)
                {
                    float invSum = (float)(1.0 / sum);
                    for (int i = 0; i < TAPS; i++)
                        kernelFlat[pOffset + i] *= invSum;
                }
            }
        }

        float* ringBuf;
        int writePos = 0;
        uint inputPhaseFp = 0;

        public OversamplingEngine()
        {
            ringBuf = AudioPlusMem.AllocFloat(BUF_SIZE + TAPS);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushSample(float voltage)
        {
            ringBuf[writePos] = voltage;

            if (writePos < TAPS)
                ringBuf[BUF_SIZE + writePos] = voltage;

            writePos = (writePos + 1) & BUF_MASK;
            inputPhaseFp += ONE_CLOCK_FP;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetSample(out float result)
        {
            result = 0f;
            if (inputPhaseFp < CLOCKS_PER_SAMPLE_FP) return false;

            inputPhaseFp -= CLOCKS_PER_SAMPLE_FP;

            int intPhase = (int)(inputPhaseFp >> 16);
            uint fracFp = inputPhaseFp & 0xFFFF;
            int phaseIdx = (int)(fracFp >> 9);
            int startIdx = (writePos - 1 - intPhase - HALF_TAPS) & BUF_MASK;

            result = Convolve(phaseIdx, startIdx);
            return true;
        }

        public int Decimate(float* output, int maxCount)
        {
            int produced = 0;

            float sample;
            while (produced < maxCount && TryGetSample(out sample))
                output[produced++] = sample;

            return produced;
        }

        public void Reset()
        {
            AudioPlusMem.ZeroFloat(ringBuf, BUF_SIZE + TAPS);
            writePos = 0;
            inputPhaseFp = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        float Convolve(int phaseIdx, int startIdx)
        {
            int vecLen = Vector<float>.Count;
            int kOffset = phaseIdx * TAPS;

            float* pRingBuf = ringBuf + startIdx;
            float* pKernel = kernelFlat + kOffset;

            if (Vector.IsHardwareAccelerated)
            {
                Vector<float> vSum = Vector<float>.Zero;
                int i = 0;

                for (; i <= TAPS - vecLen; i += vecLen)
                {
                    var vSample = *(Vector<float>*)(pRingBuf + i);
                    var vKernelVec = *(Vector<float>*)(pKernel + i);
                    vSum += vSample * vKernelVec;
                }

                float acc = Vector.Dot(vSum, Vector<float>.One);

                for (; i < TAPS; i++)
                    acc += pRingBuf[i] * pKernel[i];

                return acc;
            }
            else
            {
                float acc = 0f;
                for (int i = 0; i < TAPS; i++)
                    acc += pRingBuf[i] * pKernel[i];
                return acc;
            }
        }
    }
}
