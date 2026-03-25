using System;
using System.Runtime.CompilerServices;

namespace AprNes
{
    // =========================================================================
    // AudioDispatcher — AudioPlus 總調度器
    // =========================================================================
    // 依據 NesCore.AudioMode 將 APU 音訊分發至對應處理管線：
    //   Mode 0 (Pure Digital): 不經過此類，由 APU.cs generateSample() 直接處理
    //   Mode 1 (Authentic):    3D DAC 混音 → 超採樣 FIR 降頻 → 主機型號濾波 → dual mono 輸出
    //   Mode 2 (Modern):       5 軌獨立超採樣 → 立體聲配置 → 空間效果 → true stereo 輸出
    //
    // 整合方式:
    //   apu_step() 每 CPU cycle 呼叫 PushApuCycle()，傳入 5 聲道原始值 + expansion audio。
    //   內部依模式累積樣本，在正確時機（每 ~40.58 cycle）透過
    //   NesCore.AudioSampleReady(L, R) 發出一對 stereo 樣本給 WaveOutPlayer。
    // =========================================================================
    static class AudioDispatcher
    {
        // ── Authentic 管線實例 ─────────────────────────────────
        static AuthenticAudioMixer authenticMixer;       // 3D DAC 非線性混音 + 90Hz HPF
        static OversamplingEngine  authenticOversampler; // 1.79MHz → 44.1kHz FIR 降頻
        static ConsoleModelFilter  authenticModelFilter; // 主機型號 LPF + RF buzz

        // ── Modern 管線實例 ────────────────────────────────────
        static ModernAudioMixer modernMixer; // 5 軌獨立超採樣 + 立體聲 + Bass Boost
        static ModernAudioFX    modernFX;    // Haas Effect + Micro-Room Reverb

        static bool initialized = false;

        // ─────────────────────────────────────────────────────────
        // Init — 建立所有管線實例，在 NesCore.init() 載入 ROM 時呼叫
        // ─────────────────────────────────────────────────────────
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

        // ─────────────────────────────────────────────────────────
        // ApplySettings — 從 NesCore 靜態欄位同步設定到各模組
        // 在 Init() 及 UI 變更設定後呼叫
        // ─────────────────────────────────────────────────────────
        public static void ApplySettings()
        {
            if (!initialized) return;

            // Authentic: 主機型號 + buzz + RF
            authenticModelFilter.SetModel(NesCore.ConsoleModel, NesCore.CustomLpfCutoff, NesCore.CustomBuzz);
            authenticModelFilter.SetBuzzParams(NesCore.BuzzFreq, NesCore.BuzzAmplitude);
            authenticModelFilter.SetRfVolume(NesCore.RfVolume);

            // Modern: 立體聲 + bass boost + 空間效果
            modernMixer.SetStereoWidth(NesCore.StereoWidth);
            modernMixer.SetBassBoost(NesCore.BassBoostDb, NesCore.BassBoostFreq);
            modernFX.SetHaasDelay(NesCore.HaasDelay);
            modernFX.SetHaasCrossfeed(NesCore.HaasCrossfeed);
            modernFX.SetReverbWet(NesCore.ReverbWet);
            modernFX.SetCombFeedback(NesCore.CombFeedback);
            modernFX.SetCombDamp(NesCore.CombDamp);
        }

        // ─────────────────────────────────────────────────────────
        // Reset — 重置所有管線內部狀態（濾波器、ring buffer 等）
        // 在 ROM 載入、Hard/Soft Reset 時呼叫
        // ─────────────────────────────────────────────────────────
        public static void Reset()
        {
            if (!initialized) return;
            authenticMixer.Reset();
            authenticOversampler.Reset();
            authenticModelFilter.Reset();
            modernMixer.Reset();
            modernFX.Reset();
        }

        // ─────────────────────────────────────────────────────────
        // PushApuCycle — 每 APU cycle（CPU cycle）呼叫一次
        // 接收 5 聲道原始整數值 + expansion audio，依模式分流處理
        //
        // 參數:
        //   sq1, sq2: Pulse 聲道輸出 (0-15)
        //   tri:      Triangle 聲道輸出 (0-15)
        //   noise:    Noise 聲道輸出 (0-15)
        //   dmc:      DMC 聲道輸出 (0-127)
        //   expansionAudio: Mapper 擴展音效（VRC6, 5B, N163 等，整數 scale）
        // ─────────────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PushApuCycle(
            int sq1, int sq2, int tri, int noise, int dmc,
            int expansionAudio)
        {
            if (!NesCore.AudioEnabled || !initialized) return;

            int mode = NesCore.AudioMode;

            if (mode == 1) // ── Authentic 管線 ──
            {
                // Step 1: 3D DAC 非線性混音 + 90Hz HPF（在 1.79MHz 原生取樣率）
                float voltage = authenticMixer.GetVoltage(sq1, sq2, tri, noise, dmc, expansionAudio);

                // Step 2: 推入超採樣 ring buffer
                authenticOversampler.PushSample(voltage);

                // Step 3: 嘗試 FIR 降頻產出一個 44.1kHz 樣本（每 ~40.58 cycle 成功一次）
                float sample;
                if (authenticOversampler.TryGetSample(out sample))
                {
                    // Step 4: 主機型號 LPF + 可選 RF crosstalk
                    sample = authenticModelFilter.Process(sample, NesCore.RfCrosstalk);

                    // Step 5: 輸出 dual mono（L=R）
                    OutputStereo(sample, sample);
                }
            }
            else if (mode == 2) // ── Modern 管線 ──
            {
                // Step 1: 5 軌各自推入獨立超採樣引擎（歸一化在 mixer 內部）
                modernMixer.PushChannels(sq1, sq2, tri, noise, dmc);

                // Step 2: 嘗試產出一對 stereo 樣本（含 bass boost + pan + Haas + reverb）
                float sampleL, sampleR;
                if (modernMixer.TryGetStereoSample(out sampleL, out sampleR, modernFX))
                {
                    OutputStereo(sampleL, sampleR);
                }
            }
            // mode == 0 (Pure Digital): 不走這裡，由 APU.cs generateSample() 處理
        }

        // ─────────────────────────────────────────────────────────
        // OutputStereo — 將 float 樣本轉換為 16-bit signed stereo 並送出
        // 套用使用者音量，clamp 至 [-32768, 32767]，
        // 透過 NesCore.AudioSampleReady(L, R) callback 送給 WaveOutPlayer
        // ─────────────────────────────────────────────────────────
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void OutputStereo(float left, float right)
        {
            // float → 16-bit signed（增益 40000 將 ±0.5 映射至 ±20000 範圍）
            const float GAIN = 40000f;

            int scaledL = (int)(left * GAIN) * NesCore.Volume / 100;
            int scaledR = (int)(right * GAIN) * NesCore.Volume / 100;

            if (scaledL > 32767) scaledL = 32767;
            if (scaledL < -32768) scaledL = -32768;
            if (scaledR > 32767) scaledR = 32767;
            if (scaledR < -32768) scaledR = -32768;

            NesCore.AudioSampleReady?.Invoke((short)scaledL, (short)scaledR);

            // RF 音訊干擾回饋給視訊類比模擬系統（buzz bar 振幅 + 滾動速度）
            if (NesCore.AnalogEnabled && NesCore.AnalogOutput == AnalogOutputMode.RF)
            {
                int mono = (scaledL + scaledR) / 2;
                float absS = mono < 0 ? -mono / 32767f : mono / 32767f;
                Ntsc.RfAudioLevel = Ntsc.RfAudioLevel * 0.95f + absS * 0.05f;
                Ntsc.RfBuzzPhase = (Ntsc.RfBuzzPhase + absS * 0.0001f) % 1.0f;
            }
        }
    }
}
