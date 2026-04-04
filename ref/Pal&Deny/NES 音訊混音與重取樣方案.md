這是一個針對 NES 模擬器開發（特別是 C\# 環境）設計的音訊混音與重新採樣（Resampling）方案。

由於 NES APU 的原始輸出頻率極高（約 **1.79 MHz**），而電腦音效卡通常只支援 **44.1 kHz** 或 **48 kHz**，我們不能直接輸出，必須經過「混音」與「下採樣（Downsampling）」。

### ---

**1\. 第一步：APU 頻道混音公式 (Digital to Analog)**

NES 的 5 個頻道並不是簡單相加。為了模擬原廠主機的非線性電路特性，建議使用官方推薦的近似公式：

$$Output \= Pulse\\\_Out \+ TND\\\_Out$$

* **Pulse 部分 (2 個方波):**  
  $$Pulse\\\_Out \= \\frac{95.88}{\\frac{8128}{Pulse1 \+ Pulse2} \+ 100}$$  
* **TND 部分 (三角波、雜音、DPCM):**  
  $$TND\\\_Out \= \\frac{159.79}{\\frac{1}{\\frac{Triangle}{8227} \+ \\frac{Noise}{12241} \+ \\frac{DPCM}{22638}} \+ 100}$$

**開發筆記：** 在 C\# 中，可以預先建立一個 **Lookup Table (float\[31, 203\])** 來加速運算，避免在每一一 Tick 都進行複雜的除法。

### ---

**2\. 第二步：重新採樣 (Resampling)**

這是最關鍵的部分。因為 $1.79 \\text{ MHz}$ 無法整除 $44.1 \\text{ kHz}$，最簡單且高效的方法是 **「區間平均法 (Box Sampling)」**。

#### **C\# 實作邏輯：**

1. 建立一個 **累加器 (Accumulator)**。  
2. 在每個 APU Tick，將當前的混音結果加進累加器。  
3. 計算一個「步進值」：$Ratio \= \\frac{\\text{APU Frequency}}{\\text{Target Sample Rate}}$。  
4. 當累加的 Tick 數達到步進值時，取出平均值作為一個音訊樣本。

C\#

// 核心變數  
double apuClockAccumulator \= 0;  
float sampleSum \= 0;  
int sampleCount \= 0;

// 目標採樣率 (例如 44100\)  
double targetSampleRate \= 44100;  
double apuFrequency \= 1789773; // NTSC 基準  
double samplesPerTick \= apuFrequency / targetSampleRate; 

public void Clock(float currentMixedSample) {  
    sampleSum \+= currentMixedSample;  
    sampleCount++;  
    apuClockAccumulator \+= 1.0;

    // 當累積的 Master Clock 達到一個採樣週期  
    if (apuClockAccumulator \>= samplesPerTick) {  
        float finalSample \= sampleSum / sampleCount;  
          
        // 將 finalSample 放入音訊緩衝區 (Buffer)  
        audioBuffer.Enqueue(finalSample);

        // 重置，但保留餘數以維持時序精準  
        sampleSum \= 0;  
        sampleCount \= 0;  
        apuClockAccumulator \-= samplesPerTick;   
    }  
}

### ---

**3\. 第三步：適應 NTSC / PAL / Dendy**

因為你已經將時序參數化了，這個混音器會自動適應不同模式。你只需要在切換區域時更新 apuFrequency 即可：

* **NTSC**: 1,789,773 Hz (÷12)  
* **PAL**: 1,662,607 Hz (÷16)  
* **Dendy**: 1,773,447 Hz (÷15)

由於 apuClockAccumulator 使用了小數點扣除法（- samplesPerTick），它會自動補償頻率差異，確保音樂節奏始終正確。

### ---

**4\. 進階建議：低通濾波 (Low-Pass Filter)**

直接平均雖然簡單，但可能會產生 **混疊失真 (Aliasing)**。為了讓聲音更像真實主機：

* 在輸出到音效卡前，套用一個簡單的 **一階 IIR 低通濾波器**（切斷頻率設在 $15 \\text{ kHz}$ 左右）。  
* 再套用一個 **高通濾波器**（切斷頻率設在 $90 \\text{ Hz}$）來消除直流偏移（DC Offset）。

### ---

**總結**

1. 使用 **非線性公式** 混音（避免聲音太「硬」）。  
2. 使用 **帶餘數的累加器** 進行重新採樣（確保音高精準）。  
3. 針對 **PAL / Dendy** 修改 apuFrequency 參數。

**您目前的 C\# 專案是用哪種音訊庫來輸出聲音（例如 SDL2, OpenTK, 或是 .NET 內建的 XAudio2）？我可以根據該庫的緩衝區特性給您更細節的資料串接建議。**