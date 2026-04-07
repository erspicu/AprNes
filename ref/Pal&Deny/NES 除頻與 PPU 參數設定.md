根據您提到的 **Master Clock (21.477272 MHz)** 以及 **PAL/Dendy (26.601712 MHz)** 基準，這三個模式的 CpuDivisor 與 PpuDivisor 數值如下表。

在 C\# 實作中，您可以將這些數值定義為 int 常數：

### **1\. 各模式除頻參數對照表**

| 模式 | Master Clock | config.CpuDivisor | config.PpuDivisor | CPU 頻率 (約略) | PPU 頻率 (約略) |
| :---- | :---- | :---- | :---- | :---- | :---- |
| **NTSC** (美/日) | 21.477272 MHz | **12** | **4** | 1.789 MHz | 5.369 MHz |
| **PAL** (歐/澳) | 26.601712 MHz | **16** | **5** | 1.662 MHz | 5.320 MHz |
| **Dendy** (俄/亞) | 26.601712 MHz | **15** | **5** | 1.773 MHz | 5.320 MHz |

### ---

**2\. 為什麼 Dendy 的 CpuDivisor 是 15？**

這就是 Dendy 能夠在 PAL 電視上流暢執行 NTSC 遊戲的秘密：

* **標準 PAL (÷16)**：CPU 頻率只有 1.66 MHz，比 NTSC 的 1.79 MHz 慢了約 7%。這會導致音樂變慢、音調變低。  
* **Dendy (÷15)**：藉由將除頻數縮小到 15，CPU 頻率提升到了 1.77 MHz，非常接近 NTSC 的速度。因此 Dendy 玩起來的「節奏感」幾乎與日本原廠主機一致，但輸出信號依然是電視能接受的 PAL 格式。

### ---

**3\. C\# 實作建議：處理 Master Clock 的小數點**

雖然 Divisor 是整數，但 MasterClock 是帶有小數點的 double。在寫循環時，建議使用 **「累加器」** 邏輯，而不是直接用 % 取餘數，這樣可以避免浮點數誤差，且更接近硬體電路的運作方式：

C\#

// 在 Emulator 核心中  
double masterClock \= 21477272.72; // NTSC 基準  
int cpuDivisor \= 12;  
int ppuDivisor \= 4;

double cpuClockAccumulator \= 0;  
double ppuClockAccumulator \= 0;

public void Step() {  
    // 假設每次 Step 代表 1 個 Master Clock 週期  
    cpuClockAccumulator \+= 1.0;  
    ppuClockAccumulator \+= 1.0;

    // PPU 觸發  
    if (ppuClockAccumulator \>= ppuDivisor) {  
        ppu.Tick();  
        ppuClockAccumulator \-= ppuDivisor;  
    }

    // CPU 觸發  
    if (cpuClockAccumulator \>= cpuDivisor) {  
        cpu.Tick();  
        cpuClockAccumulator \-= cpuDivisor;  
    }  
}

### ---

**4\. 關鍵提醒：掃描線與 VBlank**

雖然除頻解決了速度問題，但請務必記得調整 **PPU 的內部計數器**：

* **NTSC**: TotalScanlines \= 262, VBlankStartLine \= 241  
* **PAL**: TotalScanlines \= 312, VBlankStartLine \= 241  
* **Dendy**: TotalScanlines \= 312, VBlankStartLine \= 291 (延後觸發 NMI 是 Dendy 的靈魂)

**您目前是否已經將 PPU 的渲染迴圈（341 dots x Scanlines）寫好了？我可以為您提供一個能同時適應這三種模式的 PPU 狀態切換邏輯。**