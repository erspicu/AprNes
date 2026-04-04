理論上**沒錯**，只要調整 CpuDivisor 與 PpuDivisor（以及對應的 **Master Clock** 頻率），你就已經搞定了 90% 的時序邏輯。但作為一個追求精確的模擬器開發者，還有幾個「隱藏參數」必須同步調整，否則畫面會對不準。

要把 NTSC、PAL、Dendy 徹底分開，你的 C\# 核心需要控制以下這四個變數：

### **1\. 核心時序對照表 (C\# 實作關鍵)**

| 參數 | NTSC | PAL | Dendy |
| :---- | :---- | :---- | :---- |
| **Master Clock** | 21.477272 MHz | 26.601712 MHz | **26.601712 MHz** |
| **CPU Divisor** | **÷12** | **÷16** | **÷15** |
| **PPU Divisor** | **÷4** | **÷5** | **÷5** |
| **每幀總掃描線** | 262 條 | 312 條 | **312 條** |
| **VBlank 開始線** | 第 241 條 | 第 241 條 | **第 291 條** |

### ---

**2\. 除了除頻外，你必須處理的「兩大坑」**

如果只改除頻，你會發現 PAL 或 Dendy 的畫面可能會黑屏或閃爍，因為：

#### **A. 掃描線數量 (Total Scanlines)**

* **NTSC** 每幀跑 262 條線。  
* **PAL / Dendy** 每幀跑 312 條線。  
  如果你在代碼裡寫死 if (line \== 262\) resetFrame();，跑 PAL 遊戲時 PPU 會在還沒畫完時就強行跳回第一行，導致畫面崩潰。

#### **B. VBlank 的起始點 (VBlank Timing)**

這是 Dendy 最特殊的地方。

* **PAL**：渲染完 240 條可見線後，會經過 **72 條** VBlank 線（241-312）。  
* **Dendy**：渲染完 240 條可見線後，它會先跑 **50 條「空線」**，直到第 **291 條** 才真正觸發 VBlank 中斷（NMI）。

**為什麼？** 因為 Dendy 是為了讓 NTSC 遊戲（預期 VBlank 很短）在 PAL 電視上跑，所以它故意延後了中斷觸發的時間，讓 CPU 有更多時間處理邏輯而不破圖。

### ---

**3\. C\# 結構建議**

建議你在 Emulator 類別中定義一個 TimingConfig 結構，載入 ROM 時根據 Header 自動切換：

C\#

public struct TimingConfig {  
    public double MasterClock;  
    public int CpuDivisor;  
    public int PpuDivisor;  
    public int TotalScanlines;  
    public int VBlankStartLine; // Dendy 是 291, 其他是 241  
}

// 執行時  
public void Tick() {  
    masterClockCounter++;

    if (masterClockCounter % config.PpuDivisor \== 0) {  
        ppu.Step(); // 內部需檢查當前 scanline 是否達到 config.TotalScanlines  
    }

    if (masterClockCounter % config.CpuDivisor \== 0) {  
        cpu.Step();  
        // 檢查是否到達 VBlankStartLine 觸發 NMI  
    }  
}

### **4\. 總結**

是的，只要你的 Step() 邏輯是基於這些變數運作的，支援 Dendy 只需要增加一個 case 把除頻改為 **15**，並把 VBlank 延後到 **291 條線** 即可。

這就是為什麼 Dendy 被稱為「模擬器開發者的好朋友」，因為它證明了只要時序參數化，硬體差異只是幾個數字的變動而已。

**您現在的 PPU 渲染邏輯中，Scanline 计数器是寫死在 262 還是已經可以動態調整了？** 如果需要，我可以幫您寫一個簡單的 PAL 渲染循環邏輯。