**完全沒問題！** 而且這次的簡化會讓純量 (Scalar) 版本的指令流水線跑得更順暢。

我們一樣可以套用\*\*「Gamma 代數提取」\*\*的數學技巧。不過，純量版本在最後的「浮點數轉整數」上，有一個和 SIMD 完全不同的底層硬體特性，所以我順便為你拆解這兩者的差異。

### **1\. Gamma 代數簡化 (與 SIMD 相同)**

如同我們上一篇推導的，Gamma 曲線公式：

$V\_{new} \= V \+ GC \\cdot V \\cdot (V \- 1)$

可以直接因式分解提取為：

$V\_{new} \= V \\cdot ((1 \- GC) \+ GC \\cdot V)$

我們只需要在常數區（或動態更新 Gamma 的地方）準備好 1f \- gc，就可以為每個通道省下一次減法，且讓 CPU 直接套用硬體的 FMA (融合乘加) 指令。

### **2\. 為什麼純量版原本就沒有寫 Math.Min/Max(0, 255)？**

你可能會發現，你在 SIMD 版本裡為了保證不越界寫了 Vector.Min(..., v255i)，但在純量版的 (int)(r \* 255.5f) 卻直接轉型，完全沒有保護。**這是因為你無意間寫出了最完美的寫法！**

* **SIMD 的轉型 (Vector.ConvertToInt32)**：底層呼叫的是 x86 的 CVTPS2DQ 指令，預設行為是\*\*「四捨五入到偶數」\*\*。所以 1.0f \* 255.5f \= 255.5f 會被進位成 256，導致越界爆掉。  
* **純量 C\# 的轉型 ((int)float)**：底層呼叫的是 CVTTSS2SI，那個 TT 代表的是 **Truncate (無條件捨去小數)**！  
  * 當 $R \= 1.0$，$1.0 \\times 255.5 \= 255.5$，無條件捨去後剛好是 **$255$**。  
  * 當 $R \= 0.0$，$0.0 \\times 255.5 \= 0.0$，無條件捨去後是 **$0$**。

所以純量版本**本來就絕對不會越界**，你原本保留 255.5f 作為些微的容差補償是非常聰明且安全的做法。

### ---

**💻 簡化後的純量版本**

請先在外部（例如 Ntsc.cs 的全域變數區）準備好反向的 Gamma 常數：

C\#

// 與 Ntsc.GammaCoeff 同步更新  
public static float GammaCoeffInv \= 1f \- 0.229f; 

接著套用你的 ProcessPixelScalar：

C\#

\[MethodImpl(MethodImplOptions.AggressiveInlining)\]  
static uint ProcessPixelScalar(float r, float g, float b, float constA, float constB)  
{  
    float bright \= r \* 0.3f \+ g \* 0.59f \+ b \* 0.11f;  
    float fw \= constA \+ bright \* constB;  
      
    // 將常數拉到區域變數，幫助 JIT 編譯器優化暫存器配置  
    float gc \= Ntsc.GammaCoeff;  
    float gcInv \= Ntsc.GammaCoeffInv;

    // 無分支邊界限制 (保持不變)  
    r \= Math.Max(0f, Math.Min(r \* fw, 1f));  
    g \= Math.Max(0f, Math.Min(g \* fw, 1f));  
    b \= Math.Max(0f, Math.Min(b \* fw, 1f));

    // Gamma 數學簡化: V \= V \* ((1 \- GC) \+ GC \* V)  
    // 成功省下 3 次浮點數減法！  
    r \*= (gcInv \+ gc \* r);  
    g \*= (gcInv \+ gc \* g);  
    b \*= (gcInv \+ gc \* b);

    // 組裝 32-bit ARGB (利用純量 Truncate 特性，保證落在 0\~255)  
    return (uint)((int)(b \* 255.5f) | ((int)(g \* 255.5f) \<\< 8) | ((int)(r \* 255.5f) \<\< 16) | 0xFF000000u);  
}

這個改動雖然看起來只是把減法變成了加法，但對於 CPU 的微架構 (Microarchitecture) 來說，加法與乘法能夠更緊密地打包在同一個指令週期內執行。積少成多，對於這台 NES PPU 渲染器來說，又是一次完美的體質瘦身！