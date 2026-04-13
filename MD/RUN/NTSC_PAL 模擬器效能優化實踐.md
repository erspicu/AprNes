這個架構重構的精髓在於：**把動態的 if 判斷，轉換成編譯期的「常數展開（Loop Unrolling）」**。

我們已經知道各個系統的 Master Clock 總數與除頻比例：

* **NTSC:** 每一幀 357,368 個 Master Clocks。CPU 12，PPU 4。  
* **PAL:** 每一幀 531,960 個 Master Clocks。CPU 16，PPU 5。

我們可以為主執行緒打造專屬的 Run\_NTSC() 與 Run\_PAL()。以下是具體的寫法與架構設計：

### **1\. NTSC 專屬賽道 (Run\_NTSC)**

NTSC 的週期比例非常完美 (12 和 4，最小公倍數就是 12)。每一幀 357,368 個週期，除以 12 等於 **29,780**，餘數是 **8**。

我們可以直接跑 29,780 次的光速通道，最後再補上 8 個週期的單步執行來對齊幀尾。

C\#

public static void Run\_NTSC()  
{  
    // NTSC 參數預先算好  
    int ticksPerFrame \= 357368;  
    int fastLoops \= ticksPerFrame / 12; // 29780  
    int remainder \= ticksPerFrame % 12; // 8

    while (\!exit)  
    {  
        // 1\. 光速通道：執行 99.99% 的幀內時間  
        for (int i \= 0; i \< fastLoops; i++)  
        {  
            NTSCFast12Clocks();  
        }

        // 2\. 處理幀尾餘數 (8 個 Master Clocks)   
        // 為了不破壞相位，這裡呼叫一個極簡版的單步 Tick  
        for (int i \= 0; i \< remainder; i++)  
        {  
            NTSCSingleClockTick();   
        }  
    }  
    Console.WriteLine("NTSC Thread exit..");  
}

**NTSC 12 週期光速展開 (無分支)：**

將原本 MasterClockTick 裡倒數計時的行為，完全攤平成循序執行的邏輯。

C\#

\[MethodImpl(MethodImplOptions.AggressiveInlining)\]  
static void NTSCFast12Clocks()  
{  
    // ── Master Clock 0 (同時觸發 CPU, APU, PPU) ──  
    if (cpuIsRead && (spriteDmaTransfer || (dmcDmaRunning && (dmcStatusEnabled || dmcImplicitAbortActive))))  
        DmaOneCycle();  
    else  
        cpu\_step\_one\_cycle();

    if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive \= false;

    MapperObj.CpuCycle(); // NTSC 專屬賽道，直接拔掉 isFDS 判斷！  
      
    apu\_step();  
    mcApuPutCycle \= \!mcApuPutCycle;

    ppu\_step\_new(); // PPU 第一次 (MC=0)

    // ── Master Clock 2 ──  
    ppu\_half\_step\_new();

    // ── Master Clock 4 (NMI 檢查點: 原本的 mcCpuClock \== 8\) ──  
    NMILine |= NMIable && isVblank;  
    if (operationCycle \== 0 && \!(isVblank && NMIable)) NMILine \= false;  
      
    ppu\_step\_new(); // PPU 第二次 (MC=4)

    // ── Master Clock 6 ──  
    ppu\_half\_step\_new();

    // ── Master Clock 7 (IRQ 檢查點: 原本的 mcCpuClock \== 5\) ──  
    IRQLine \= irqLineCurrent;  
    if (statusframeint && \!apuintflag) irqLineCurrent \= true;  
    MapperObj.CpuClockRise();

    // ── Master Clock 8 ──  
    ppu\_step\_new(); // PPU 第三次 (MC=8)

    // ── Master Clock 10 ──  
    ppu\_half\_step\_new();

    masterClockTotal \+= 12;  
}

### ---

**2\. PAL 專屬賽道 (Run\_PAL)**

PAL 的除頻是 16 (CPU) 和 5 (PPU)。這兩個數字的**最小公倍數 (LCM) 是 80**。

在 80 個 Master Clocks 裡面，包含了完美的 **5 次 CPU Step** 與 **16 次 PPU Step**。

PAL 每一幀 531,960 週期，除以 80 等於 **6,649**，餘數是 **40**。

C\#

public static void Run\_PAL()  
{  
    int ticksPerFrame \= 531960;  
    int fastLoops \= ticksPerFrame / 80; // 6649  
    int remainder \= ticksPerFrame % 80; // 40

    while (\!exit)  
    {  
        for (int i \= 0; i \< fastLoops; i++)  
        {  
            PALFast80Clocks();  
        }  
        for (int i \= 0; i \< remainder; i++)  
        {  
            PALSingleClockTick();  
        }  
    }  
}

**PAL 80 週期終極展開 (摘錄核心概念)：**

在這個區塊內，我們要手動把 CPU 和 PPU 發生的時間點「硬編碼」排上去。

C\#

\[MethodImpl(MethodImplOptions.AggressiveInlining)\]  
static void PALFast80Clocks()  
{  
    // 在這 80 個週期中，PPU 每 5 步觸發一次，CPU 每 16 步觸發一次。  
    // 我們可以將它展開成精確的實體時間線！

    // MC \= 0 (CPU 與 PPU 對齊)  
    PAL\_CPU\_Macro();   
    ppu\_step\_new();

    // MC \= 2  
    ppu\_half\_step\_new();

    // MC \= 5  
    ppu\_step\_new();

    // MC \= 7  
    // PAL 的 NMI 觸發點 (16-4 \= MC 12?) \-\> 需要依據您的硬體時序微調  
    ppu\_half\_step\_new();

    // MC \= 10  
    ppu\_step\_new();

    // MC \= 12  
    ppu\_half\_step\_new();

    // MC \= 15  
    ppu\_step\_new();

    // MC \= 16 (CPU 第二次觸發)  
    PAL\_CPU\_Macro();  
      
    // MC \= 17  
    ppu\_half\_step\_new();

    // ... 依此類推，精確排滿 80 個 Master Clock ...  
      
    masterClockTotal \+= 80;  
}

\[MethodImpl(MethodImplOptions.AggressiveInlining)\]  
static void PAL\_CPU\_Macro()  
{  
    // 把原本的 CPU 執行與 DMA 判斷包成一個 Macro  
    if (cpuIsRead && (spriteDmaTransfer || (dmcDmaRunning && (dmcStatusEnabled || dmcImplicitAbortActive))))  
        DmaOneCycle();  
    else  
        cpu\_step\_one\_cycle();

    if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive \= false;

    MapperObj.CpuCycle();  
    apu\_step();  
    mcApuPutCycle \= \!mcApuPutCycle;  
}

### ---

**3\. FDS 專屬賽道 (Run\_FDS)**

既然切開了，FDS 就可以獨立享有一個最乾淨、不怕干擾的迴圈。FDS 通常是 NTSC 時序，但它的 CPU 週期需要額外處理磁碟機的中斷。

C\#

public static void Run\_FDS()  
{  
    int fastLoops \= 357368 / 12;  
    int remainder \= 357368 % 12;

    while (\!exit)  
    {  
        for (int i \= 0; i \< fastLoops; i++)  
        {  
            FDSFast12Clocks();  
        }  
        for (int i \= 0; i \< remainder; i++)  
        {  
            FDSSingleClockTick();   
        }  
    }  
}

\[MethodImpl(MethodImplOptions.AggressiveInlining)\]  
static void FDSFast12Clocks()  
{  
    // ... 與 NTSCFast12Clocks 幾乎相同，但把 MapperObj.CpuCycle() 換成：  
    fds\_CpuCycle();   
      
    // 磁碟機專屬的 IRQ 或狀態機也可以直接寫死在這裡  
    // ...  
}

### **🎯 總結：這樣做值得嗎？**

**絕對值得。**

在軟體工程中，我們通常被告誡要「避免重複程式碼 (DRY)」。但在**硬核模擬器開發的底層熱路徑 (Hot Path)** 上，為了追求極致的緩取命中率 (Cache Coherency) 與消除分支預測失敗 (Branch Misprediction)，我們必須採用\*\*「WET (Write Everything Twice)」\*\*策略，進行手動的靜態分派。

這樣切開之後：

1. **NTSC 遊戲 (佔 90% 以上)** 執行時，CPU 完全不用問自己是不是 FDS、是不是 PAL，也不用維護 mcCpuClock 倒數器。管線乾淨到底。  
2. **您的除錯會變得非常直覺**。如果 PAL 遊戲破圖，您只要去改 PALFast80Clocks，絕對不怕不小心改壞 NTSC 的時序。

在 AprNesUI.cs 讀取 ROM Header 後，根據 NesCore.Region 與 NesCore.isFDS，啟動對應的 Thread，這就是現代頂級模擬器壓榨效能的「最終奧義」了！