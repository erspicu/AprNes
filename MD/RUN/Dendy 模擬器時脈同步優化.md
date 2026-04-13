這是一個非常細膩且關鍵的問題！

答案是：**不能直接和 NTSC 混在一起跑同一個 12 週期的迴圈，但是 Dendy 的「物理結構」和 NTSC 是完美的雙胞胎！**

讓我們來拆解一下您 Main.cs 裡定義的 Dendy (俄羅斯版紅白機) 參數：

* **Dendy 的 Master Clock：** 每一幀 531,960 週期 (跟 PAL 一樣長)。  
* **CPU 除頻：** 15。  
* **PPU 除頻：** 5。

### **💡 驚人的數學巧合：完美的 3 : 1**

您發現了嗎？Dendy 的 CPU:PPU 比例是 **15 : 5**，約分之後剛好就是 **3 : 1**！

這和 NTSC 的 **12 : 4 (3 : 1\)** 在硬體邏輯的先後順序上是**一模一樣**的。

這代表 Dendy 不需要像 PAL 那樣搞出一個 80 週期的大迴圈，它只需要一個 **15 週期** 的光速通道，而且裡面的排列組合跟 NTSC 的 12 週期通道**完全相同**！

更棒的是：每一幀 531,960 週期，除以 15 剛好等於 **35,464**。

**完美整除！沒有餘數！** Dendy 是這三個系統中最乾淨的，連幀尾對齊的單步 Tick 都不需要寫！

### **🚀 Dendy 專屬途徑 (Run\_Dendy)**

為了保持架構的極致乾淨，強烈建議您為 Dendy 開第三條賽道。它的程式碼會跟 NTSC 幾乎長得一樣，但常數換成了 15：

C\#

public static void Run\_Dendy()  
{  
    // Dendy 每一幀剛好是 531960 個 Master Clocks  
    // 531960 / 15 \= 35464，完美整除！  
    int fastLoops \= 35464; 

    while (\!exit)  
    {  
        // 直接無腦狂飆，不需要處理餘數  
        for (int i \= 0; i \< fastLoops; i++)  
        {  
            DendyFast15Clocks();  
        }  
    }  
    Console.WriteLine("Dendy Thread exit..");  
}

**Dendy 的 15 週期光速展開 (與 NTSC 邏輯完全對稱)：**

C\#

\[MethodImpl(MethodImplOptions.AggressiveInlining)\]  
static void DendyFast15Clocks()  
{  
    // ── Master Clock 0 ──  
    if (cpuIsRead && (spriteDmaTransfer || (dmcDmaRunning && (dmcStatusEnabled || dmcImplicitAbortActive))))  
        DmaOneCycle();  
    else  
        cpu\_step\_one\_cycle();

    if (dmcDmaRunning && dmcImplicitAbortActive) dmcImplicitAbortActive \= false;

    MapperObj.CpuCycle();   
    apu\_step();  
    mcApuPutCycle \= \!mcApuPutCycle;

    ppu\_step\_new(); // PPU 第一次 (MC=0)

    // ── Master Clock 2 (Dendy PPU half step 是 5 \>\> 1 \= 2\) ──  
    ppu\_half\_step\_new();

    // ── Master Clock 5 (NMI 檢查點，對應 NTSC 的 MC=4) ──  
    NMILine |= NMIable && isVblank;  
    if (operationCycle \== 0 && \!(isVblank && NMIable)) NMILine \= false;  
      
    ppu\_step\_new(); // PPU 第二次 (MC=5)

    // ── Master Clock 7 ──  
    ppu\_half\_step\_new();

    // ── Master Clock 10 (IRQ 檢查點) ──  
    // 注意：原本的 mcCpuClock 遞減機制下，Dendy 的 IRQ 也是在 CPU 週期結束前觸發  
    IRQLine \= irqLineCurrent;  
    if (statusframeint && \!apuintflag) irqLineCurrent \= true;  
    MapperObj.CpuClockRise();

    ppu\_step\_new(); // PPU 第三次 (MC=10)

    // ── Master Clock 12 ──  
    ppu\_half\_step\_new();

    // ── 完成 15 個 Master Clocks ──  
    masterClockTotal \+= 15;  
}

### **總結架構藍圖**

經過您的構思與我們的討論，您現在在 AprNesUI.cs 啟動遊戲的路由，將會變成一個堪稱藝術品的「四向靜態分派 (4-way Static Dispatch)」：

C\#

if (NesCore.isFDS)  
{  
    // 包含磁碟機 I/O 劫持的 12 週期特化迴圈  
    emulatorThread \= new Thread(NesCore.Run\_FDS);  
}  
else if (NesCore.Region \== NesCore.RegionType.PAL)  
{  
    // 16:5 比例，80 週期最小公倍數展開迴圈  
    emulatorThread \= new Thread(NesCore.Run\_PAL);  
}  
else if (NesCore.Region \== NesCore.RegionType.Dendy)  
{  
    // 15:5 比例 (3:1)，完美整除的 15 週期展開迴圈  
    emulatorThread \= new Thread(NesCore.Run\_Dendy);  
}  
else   
{  
    // 最主流的 NTSC：12:4 比例 (3:1)，12 週期展開 \+ 8 週期尾數處理  
    emulatorThread \= new Thread(NesCore.Run\_NTSC);  
}

這四個執行緒的方法雖然有部分邏輯重複（WET 原則），但它們保證了**底層熱路徑上沒有任何一句多餘的廢話**。只要照著這個藍圖施工，這套模擬器的時脈同步開銷將會趨近於物理極限的零！