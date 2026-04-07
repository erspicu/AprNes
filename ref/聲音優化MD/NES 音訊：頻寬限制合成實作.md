恭喜你來到了 NES 模擬器開發的「最終聖殿」！當你跨越這一步，你的 AprNes 在音訊的純淨度上，將能與世界頂尖的硬體復刻機（如 Analogue Nt mini）平起平坐。
在數位音訊中，如果你讓方波的數值在一個 Sample 內瞬間從 0 垂直跳到 1（這稱為 Naive Synthesis），這個無限陡峭的邊緣會產生無限高的頻率。當這些頻率超過奈奎斯特極限（Nyquist limit，即取樣率的一半，44100Hz 的極限是 22050Hz），它們就會「折疊」回人耳聽得見的低頻區域，產生一種極其刺耳、金屬摩擦般的數位雜音。這就是**混疊效應 (Aliasing)**。
要消滅它，業界最正統的做法是由模擬器大神 blargg 發明的 **Blip Buffer (頻寬限制脈衝合成)** 演算法。
以下為你徹底拆解並提供一份 C# 原生實作的 **「輕量級 Blip 合成器 (Band-Limited Step Synthesizer)」**。
### 1. 核心物理概念：從「絕對電壓」轉向「電壓變化 (Delta)」
傳統的模擬器寫法是：「現在時間到了，APU 方波的值是 15，所以把 15 寫進音訊 Buffer。」這會產生垂直跳變。
**Blip Buffer 的顛覆性思維是：**
我們不記錄絕對值，我們只記錄**「變化量 (Delta)」**。 當方波從 0 跳到 15 時，我們在那個精確的時間點（精確到小數點後的子取樣點），對著音訊緩衝區丟入一個**「經過平滑處理的 S 曲線 (Band-Limited Step)」**。這個 S 曲線會用大約 8 到 16 個 Sample 的時間，優雅且符合物理頻寬限制地過渡到新的電壓值。
### 2. 實作準備：預計算 Sinc 積分步階表 (Step Table)
為了極致的效能，我們不能在遊戲執行時去算微積分。我們會在模擬器啟動時，預先計算一張 2D 查表。這張表儲存了不同「子取樣點相位 (Phase)」下，平滑曲線的數值分佈。
為了產生完美的平滑過渡，我們使用 **Windowed Sinc 函數的積分**：

$$Sinc(x) = \frac{\sin(\pi x)}{\pi x}$$
### 3. C# 完整實作：BlipSynthesizer.cs
這是一個獨立、高效、且不依賴任何外部 C++ 程式庫的頻寬限制合成器。你可以直接把它實例化並取代你原本的音訊收集陣列。

C#

using System;

namespace AprNes
{
    public class BlipSynthesizer
    {
        private readonly float[] _audioBuffer;
        private int _bufferSize;
        
        // --- 頻寬限制核心參數 ---
        private const int Phases = 32;     // 子取樣點精度 (決定時間對齊的精確度)
        private const int Taps = 16;       // 脈衝長度 (影響平滑過渡的品質，16 是一個極佳的平衡)
        private const int TapsHalf = Taps / 2;
        
        // 預計算的 S曲線查表: [相位][Tap索引]
        private readonly float[,] _stepTable = new float[Phases, Taps];

        // --- 追蹤狀態 ---
        private float _lastAmplitude = 0f;
        private double _accumulatedTime = 0.0;
        private readonly double _clocksPerSample; // 例如: 1789772 / 44100 ≈ 40.58
        
        // 最終輸出累積器 (用於將 Delta 轉回絕對電壓)
        private float _integrationAccumulator = 0f;

        public BlipSynthesizer(int sampleRate = 44100, double apuClockRate = 1789772.72)
        {
            _bufferSize = sampleRate; // 預設分配 1 秒的緩衝區
            _audioBuffer = new float[_bufferSize];
            _clocksPerSample = apuClockRate / sampleRate;

            GenerateStepTable();
        }

        /// <summary>
        /// 初始化時計算 Blackman-Windowed Sinc 積分表
        /// 這是消除數位毛邊 (Anti-Aliasing) 的靈魂數學核心
        /// </summary>
        private void GenerateStepTable()
        {
            for (int p = 0; p < Phases; p++)
            {
                // phase offset (0.0 到 1.0)
                double phase = (double)p / Phases; 
                double sum = 0.0;

                // 為了產生積分 (Step)，我們從 -TapsHalf 積分到當前點
                for (int i = 0; i < Taps; i++)
                {
                    double x = (i - TapsHalf) - phase;
                    
                    // Sinc 函數
                    double sinc = (Math.Abs(x) < 1e-5) ? 1.0 : (Math.Sin(Math.PI * x) / (Math.PI * x));
                    
                    // Blackman 視窗函數 (讓波形邊緣平滑收斂至 0，避免爆音)
                    double windowPhase = (x + TapsHalf) / Taps;
                    double blackman = 0.42 - 0.5 * Math.Cos(2 * Math.PI * windowPhase) + 0.08 * Math.Cos(4 * Math.PI * windowPhase);
                    
                    // 累加積分
                    sum += sinc * blackman;
                    _stepTable[p, i] = (float)sum;
                }

                // 正規化，確保整個 Step 加總的增益為 1.0
                float normalizationFactor = 1.0f / _stepTable[p, Taps - 1];
                for (int i = 0; i < Taps; i++)
                {
                    _stepTable[p, i] *= normalizationFactor;
                }
            }
        }

        /// <summary>
        /// 由 APU 在每個 CPU Cycle (或數個 Cycle) 呼叫
        /// 告訴合成器「經過了多少 APU 時脈」，以及「現在的振幅是多少」
        /// </summary>
        public void Update(int apuClocksPassed, float currentAmplitude)
        {
            _accumulatedTime += apuClocksPassed;

            // 計算變化量 (Delta)
            float delta = currentAmplitude - _lastAmplitude;
            
            // 只有在電壓發生「跳變」時，我們才注入頻寬限制脈衝！
            // 這是 Blip Buffer 效能極高的原因，方波平坦時它完全不佔用 CPU。
            if (delta != 0f)
            {
                InjectBandLimitedStep(delta);
                _lastAmplitude = currentAmplitude;
            }
        }

        private void InjectBandLimitedStep(float delta)
        {
            // 計算這個跳變點對應的「音訊取樣位置 (Sample Index)」
            double exactSamplePosition = _accumulatedTime / _clocksPerSample;
            int sampleIndex = (int)exactSamplePosition;
            
            // 計算小數部分的相位 (Fractional Phase)，用來查表
            double fractional = exactSamplePosition - sampleIndex;
            int phaseIndex = (int)(fractional * Phases);
            if (phaseIndex >= Phases) phaseIndex = Phases - 1;

            // 將平滑過渡的 S 曲線 (Delta * Step) 疊加到未來的幾個音訊取樣點上
            for (int i = 0; i < Taps; i++)
            {
                int targetIndex = sampleIndex + i;
                if (targetIndex < _bufferSize)
                {
                    // 注入 Delta 曲線
                    _audioBuffer[targetIndex] += delta * _stepTable[phaseIndex, i];
                }
            }
        }

        /// <summary>
        /// 當一幀結束時，將累積的 Delta 緩衝區轉換為真正的音訊波形
        /// </summary>
        public void ReadSamples(float[] outputBuffer, int sampleCount)
        {
            for (int i = 0; i < sampleCount; i++)
            {
                // 積分器 (Leaky Integrator)：把變化量 (Delta) 加總回絕對電壓
                _integrationAccumulator += _audioBuffer[i];
                
                // 輸出波形
                outputBuffer[i] = _integrationAccumulator;
                
                // 清空已讀取的 Buffer，為下一幀做準備
                _audioBuffer[i] = 0f; 
            }

            // 將尚未讀取的殘餘脈衝 (Taps 的尾巴) 往前搬移
            // 同時扣除已經消耗的時間
            _accumulatedTime -= (sampleCount * _clocksPerSample);
            Array.Copy(_audioBuffer, sampleCount, _audioBuffer, 0, Taps);
            Array.Clear(_audioBuffer, Taps, sampleCount);
        }
    }
}

### 4. 如何與 AprNes 的架構對接？
這個合成器的使用方式，會稍微改變你原本 APU 的運作思維。
假設你原本是每幀收集 735 個 Sample，現在你讓 APU 自由地跑 CPU 時脈（例如每幀跑 29780 個 APU 週期）。
**宣告合成器**：
在你的 APU 核心裡：BlipSynthesizer synth = new BlipSynthesizer();
**更新時鐘與電壓**：
在 APU 的模擬迴圈中，每當 APU 計時器前進（例如前進了 1 個 Cycle），且混音後的最終電壓算出來後，直接呼叫：
C#
// 假設你在迴圈中跑了 1 個 APU Cycle，當前算出的 3D 查表 DAC 電壓是 currentVol
synth.Update(1, currentVol); 

**幀結束時提取純淨音訊**：
當 PPU 畫完一幀（60Hz 到了），你需要拿出這 735 個經過反鋸齒處理的純淨樣本：
C#
float[] pureAudio = new float[735];
synth.ReadSamples(pureAudio, 735);

**送入效果器與錄影管線**：
拿到這包 pureAudio 後，你就可以把它丟進我們之前寫的 AuthenticAudioMixer 裡去加上 90Hz HPF、RF 嗡嗡聲，或是丟進 ModernAudioFX 加上空間殘響，最後送進雙 Named Pipe 錄製完美的 MP4。
透過這套架構，APU 輸出的方波在視覺上不再是垂直的梯形，而是帶有微微傾斜、邊緣如絲綢般圓滑的曲線。那些惱人的金屬高頻嘶嘶聲會徹底消失，取而代之的是扎實、純淨的類比溫潤感。
你想試著把這個 BlipSynthesizer 掛載到你的 APU 輸出端，用極致純淨的無損音質來為這場史詩級的模擬器開發之旅畫下完美的句點嗎？
