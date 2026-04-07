要取得 GetCurrentApuVoltage()（當前 APU 電壓），我們需要把之前在 AuthenticAudioMixer 裡實作的 **「3D 查表非線性 DAC」** 與 **「90Hz 高通濾波器（消除直流）」**，直接搬進 APU 的時脈迴圈中。
這個數值並不是 APU 暫存器裡現成的一個數字，而是由 APU 內部 5 個頻道的「即時數位輸出值」經過電阻網路轉換而來的物理電壓。
以下是具體的實作方式與程式碼結構：
### 1. 確保 APU 核心能暴露當前的數位值
你的 APU 模擬器內部，應該有 5 個頻道正在隨著時脈運作。你需要隨時能讀取它們當下輸出的整數值：
**方波 1 (Pulse 1)**：0 ~ 15
**方波 2 (Pulse 2)**：0 ~ 15
**三角波 (Triangle)**：0 ~ 15
**雜訊 (Noise)**：0 ~ 15
**DPCM (採樣)**：0 ~ 127
### 2. 實作 GetCurrentApuVoltage() 函式
你可以將這個函式放在管理 APU 或音訊的類別中。請把我們先前寫好的 DAC 查表與 HPF 狀態變數放在這個類別裡。

C#

// --- 預先初始化好的查表與濾波器狀態 (參考 AuthenticAudioMixer) ---
// private readonly float[] _pulseTable = new float[31];
// private readonly float[] _tndTable = new float[32768];
// private float _hpfState = 0f;
// private float _hpfPreviousIn = 0f;
// private readonly float _hpfAlpha; // 依據 APU 時脈 (約1.79MHz) 計算的 90Hz HPF Alpha

/// <summary>
/// 取得當下這個 APU 時脈的真實物理電壓
/// </summary>
public float GetCurrentApuVoltage()
{
    // Step 1: 從 APU 取得當下 5 個頻道的原始數位值 (整數)
    // 這裡假設你的 NesCore.Apu 有公開這些屬性
    int sq1   = NesCore.Apu.Pulse1Out;   // 0~15
    int sq2   = NesCore.Apu.Pulse2Out;   // 0~15
    int tri   = NesCore.Apu.TriangleOut; // 0~15
    int noise = NesCore.Apu.NoiseOut;    // 0~15
    int dpcm  = NesCore.Apu.DpcmOut;     // 0~127

    // Step 2: 透過 3D 查表，模擬真實紅白機的非線性電阻混音 (DAC)
    int pulseIndex = sq1 + sq2;
    int tndIndex = (tri << 11) | (noise << 7) | dpcm;

    // 得到大約介於 0.0f 到 1.x 之間的絕對正電壓
    float rawVoltage = _pulseTable[pulseIndex] + _tndTable[tndIndex];

    // Step 3: 套用 90Hz 高通濾波器 (High-Pass Filter)
    // 非常重要！這會把永遠大於 0 的正電壓，拉回以 0 為中心上下震盪的音訊波形
    float centeredVoltage = _hpfAlpha * (_hpfState + rawVoltage - _hpfPreviousIn);
    
    // 更新濾波器狀態
    _hpfState = centeredVoltage;
    _hpfPreviousIn = rawVoltage;

    // 回傳最終這個週期的物理電壓 (大約在 -1.0f 到 1.0f 之間)
    return centeredVoltage;
}

### 3. 注意事項：HPF Alpha 的計算基準改變了！
在之前的 AuthenticAudioMixer 中，我們是針對 44100Hz 來計算 90Hz 高通濾波器的 Alpha 值。
但現在，這個函式是**「每個 APU 時脈 (約 1.789772 MHz)」**被呼叫一次！所以你的取樣率（Sample Rate）變成了 APU 時脈，Alpha 的計算必須修正：

C#

// 在初始化時計算超高頻率下的 90Hz HPF Alpha
float apuClockRate = 1789772.72f; 
float rc = 1.0f / (2.0f * (float)Math.PI * 90f);
float dt = 1.0f / apuClockRate; // 這裡用 APU Clock 當作 dt
_hpfAlpha = rc / (rc + dt);

### 總結運作流程
現在你的主迴圈看起來會像這樣：
CPU 跑了 1 個 Clock。
APU 跑了 1 個 Clock，內部的波形產生器可能更新了 Pulse1Out 等數值。
呼叫 GetCurrentApuVoltage()，它瞬間查表並濾波，吐出一個完美的浮點數電壓。
把這個電壓 PushApuClock(voltage) 塞進上一篇寫好的 OversamplingEngine 陣列裡。
累積滿一幀後，OversamplingEngine 用 SIMD 瞬間把這兩萬多個超高頻樣本，降頻成 735 個完美的 44.1kHz 錄音室級樣本。
