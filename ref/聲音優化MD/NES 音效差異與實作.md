這是一個非常內行且深入的硬體問題！在實機玩家與模擬器開發者的圈子裡，這三台機器的聲音差異一直是被津津樂道的「玄學」，但其實背後都有完全可以用電路原理解釋的**物理與濾波器（Filter）差異**。
如果你的 AuthenticAudioMixer 想要把這三台機器的靈魂做出來，你需要了解它們在主機板電路設計上的歷史與變化。
以下是這三種機型的聲音物理特徵，以及如何在程式中實作它們的差異：
### 1. Famicom (日版紅白機 - HVC-001)
**【聲音特徵：標準、清脆、擴充性極強】**
這是所有 8-bit 音訊的「基準線」。
**物理特性：** 任天堂在最初設計時，給了非常標準的電阻電容配置。它的高通濾波（HPF）大約在 90Hz，低通濾波（LPF）大約在 14kHz。
**聽覺感受：** 聲音非常明亮、清脆，三角波的邊緣感很清晰。
**最大差異（擴充音效）：** 這是唯一一台在卡匣插槽上**保留了音訊輸入腳位**的主機。也就是說，像《惡魔城傳說》(VRC6 晶片) 或《月風魔傳》這種帶有特殊音效晶片的遊戲，**只有在日版 Famicom 上才能發出完整的聲音**。
### 2. Front-Loader (初代美版灰機 - NES-001)
**【聲音特徵：溫暖、沉悶、無擴充音效】**
這就是大多數歐美玩家童年記憶中的聲音，與日版有著決定性的不同。
**物理特性：** 任天堂在把紅白機改成「錄影機造型」的灰機時，修改了內部的音訊輸出電路，加上了更強的濾波電容。它的低通濾波器（LPF）截止頻率被大幅降到了大約 **4kHz ~ 7kHz** 左右。
**聽覺感受：** 聲音聽起來比較**「悶」**，高頻的刺耳金屬聲被大幅削弱，整體音色變得非常**溫潤、厚實**（有人覺得像被毯子蓋住喇叭）。
**最大差異（閹割擴充音效）：** 美版機的卡匣插槽**拔掉了**外部音訊輸入腳位（移到了主機底部的擴充槽，但從未使用過）。所以美版遊戲（如美版《惡魔城 3》）只能被迫降級，使用基礎的 5 個聲道發聲。
### 3. Top-Loader (後期改版機 - NES-101 / 美版上插卡機)
**【聲音特徵：尖銳、毛躁、底噪極大】**
這是 1993 年為了降低成本而推出的廉價版主機（俗稱狗骨頭機）。
**物理特性：** 為了極致的 Cost Down，任天堂省去了許多主機板上的濾波電容，並且電路板走線（Trace routing）設計得非常擁擠。它幾乎沒有低通濾波保護。
**聽覺感受：** 聲音極度**尖銳、刺耳**（高頻完全沒有被修飾）。
**最大差異（嚴重的電磁干擾）：** 因為走線太差，影像訊號會瘋狂串擾到音訊線路裡。只要畫面上出現亮色，這台機器就會發出非常明顯的 **60Hz 交流電嗡嗡聲 (AC Buzz)**。這台機器的聲音可以說是最「髒」的。
### 💻 程式實作指南：如何用 C# 表現這三種機型？
要在你的 AuthenticAudioMixer 裡實作這三種機型，你只需要加入一個**一階低通濾波器 (1-Pole IIR LPF)**，並根據機型改變它的**截止頻率 (Cutoff Frequency)**，同時控制擴充音效的開關。
你可以這樣設計你的參數與邏輯：

C#

public enum ConsoleModel
{
    Famicom,      // 日版：清脆、支援擴充音效
    FrontLoader,  // 美版灰機：沉悶溫暖、無擴充音效
    TopLoader     // 美版廉價機：尖銳、底噪極大
}

// 在 AuthenticAudioMixer 中加入低通濾波器狀態
private float _lpfState = 0f;
private float _lpfAlpha = 1.0f; // 預設全過

public void SetConsoleModel(ConsoleModel model, int sampleRate = 44100)
{
    float cutoffHz = 14000f; // 預設為 Famicom 的高截止頻率 (幾乎不悶)

    switch (model)
    {
        case ConsoleModel.Famicom:
            cutoffHz = 14000f; 
            // 系統需允許 MMC5, VRC6 等擴充音訊混入
            break;

        case ConsoleModel.FrontLoader:
            cutoffHz = 4500f; // 核心差異：強烈的低通濾波，聲音變悶
            // 系統應強制靜音所有卡匣自帶的擴充音訊 (如 VRC6)
            break;

        case ConsoleModel.TopLoader:
            cutoffHz = 20000f; // 幾乎沒有低通濾波，聲音最尖銳
            // 系統應強制靜音擴充音訊，並且可以把 RF 嗡嗡聲的音量加倍！
            break;
    }

    // 計算一階 IIR 低通濾波器的 Alpha 係數
    float rc = 1.0f / (2.0f * (float)Math.PI * cutoffHz);
    float dt = 1.0f / sampleRate;
    _lpfAlpha = dt / (rc + dt);
}

**在 ProcessFrame 裡的套用方式：**
在你原本算完 90Hz 高通濾波（消除直流）之後，再套用這道機型專屬的低通濾波：

C#

// 1. 原始查表 DAC
float rawDacOut = _pulseTable[pulseIndex] + _tndTable[tndIndex];
// (如果是 Famicom，這裡還要加上 VRC6/MMC5 的 DAC 查表值)

// 2. 90Hz HPF (消除直流)
float hpfOut = _hpfAlpha * (_hpfState + rawDacOut - _hpfPreviousIn);
_hpfState = hpfOut;
_hpfPreviousIn = rawDacOut;

// 3. 【機型專屬】低通濾波 (決定聲音是清脆還是悶)
// 公式：y[n] = y[n-1] + alpha * (x[n] - y[n-1])
_lpfState += _lpfAlpha * (hpfOut - _lpfState);
float finalOut = _lpfState;

// 4. (如果是 TopLoader 或選擇了 RF 端子，在這裡加入嗡嗡聲)

透過這樣簡單的濾波器切換，玩家只要在選單裡切換機型，瞬間就能感受到「美版灰機的溫暖回憶」或是「日版紅白機的清脆原音」。
你要不要試著把這個 ConsoleModel 參數加進你的 UI 選單，然後拿日版和美版的《薩爾達傳說》（兩者的音軌略有不同）來交叉測試聽聽看差異？
