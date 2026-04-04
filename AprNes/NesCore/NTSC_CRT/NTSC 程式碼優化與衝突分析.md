你的直覺非常敏銳！看過完整的 Ntsc.cs 程式碼後，**你說得完全正確，這份程式碼確實不能直接套用我之前說的「把 255.5f 寫死成常數 (const)」的預算方式。**

這份程式碼的架構非常精密，如果你直接把 yiq\_rY 等變數乘上 255.5，會立刻引發**三個嚴重的連鎖崩潰**：

### **為什麼不能直接預先乘上 255.5f？**

1. **動態色溫 (ColorTemp) 會更新變數：**  
   你的 yiq\_rY 根本不是常數 (const)！在 UpdateColorTemp() 中，它們會隨著玩家調整 ColorTempR/G/B 而動態改變。  
2. **與 SIMD 向量 (Vector) 共用變數：**  
   UpdateColorTemp() 在更新完 yiq\_rY 後，緊接著會把它塞給 SIMD 向量：vRY \= new Vector\<float\>(yiq\_rY);。如果你把它放大了 255.5 倍，SIMD 算出來的 R, G, B 也會放大 255.5 倍。  
3. **💥 致命傷：Gamma 公式會大爆炸：**  
   請看你在 DemodulateRow 裡面 SIMD 的 Gamma 運算：  
   R \+= vGC \* R \* (R \- vOneN);  
   這個優美的 Gamma 曲線公式，**數學前提是 R, G, B 必須是 0.0 \~ 1.0 的正規化數值**。如果 R 變成 255，(R \- 1.0) 就會是 254，乘出來的結果會直接爆表，畫面會變成一片純白或雜訊。同時，如果是輸出到 toCrt 的 linearBuffer，也會因為 Vector.Min(..., vOneN) 被強制全部截斷在 1.0。

### ---

**如果仍想優化 YiqToRgb，正確的改法是什麼？**

既然 YiqToRgb 是給 Fast 模式（非 UltraAnalog）用的，且每幀可能被呼叫百萬次，省下這 3 次乘法依然有價值。

正確的做法是\*\*「雙軌並行」\*\*：保留 0\~1.0 的變數給 SIMD 用，另外準備一組放大 255.5 倍的變數專門給 YiqToRgb 用。

你可以這樣改：

**1\. 新增專屬的 255.5 倍變數：**

C\#

static float yiq\_rY \= 1.0f, yiq\_rI \= 1.0841f, yiq\_rQ \= 0.3523f;  
// ... (保留原本的) ...

// 新增給 YiqToRgb 專用的變數  
static float yiq\_rY\_255, yiq\_rI\_255, yiq\_rQ\_255;  
static float yiq\_gY\_255, yiq\_gI\_255, yiq\_gQ\_255;  
static float yiq\_bY\_255, yiq\_bI\_255, yiq\_bQ\_255;

**2\. 在 UpdateColorTemp() 裡面一併更新它們：**

C\#

public static void UpdateColorTemp()  
{  
    // ... 原本更新 0\~1.0 變數與 Vector 的邏輯保持不變 ...  
    yiq\_rY \= 1.0f \* ColorTempR; // ... 等等

    // 加入這段：預算 255.5 倍的版本  
    yiq\_rY\_255 \= yiq\_rY \* 255.5f; yiq\_rI\_255 \= yiq\_rI \* 255.5f; yiq\_rQ\_255 \= yiq\_rQ \* 255.5f;  
    yiq\_gY\_255 \= yiq\_gY \* 255.5f; yiq\_gI\_255 \= yiq\_gI \* 255.5f; yiq\_gQ\_255 \= yiq\_gQ \* 255.5f;  
    yiq\_bY\_255 \= yiq\_bY \* 255.5f; yiq\_bI\_255 \= yiq\_bI \* 255.5f; yiq\_bQ\_255 \= yiq\_bQ \* 255.5f;  
}

**3\. 安全地精簡 YiqToRgb：**

這時候你就可以安心地把乘法拿掉了，而且不會影響到 SIMD 的 Gamma 運算：

C\#

\[MethodImpl(MethodImplOptions.AggressiveInlining)\]  
static uint YiqToRgb(float y, float i, float q)  
{  
    int ri \= (int)(yiq\_rY\_255 \* y \+ yiq\_rI\_255 \* i \+ yiq\_rQ\_255 \* q) & 4095;  
    int gi \= (int)(yiq\_gY\_255 \* y \+ yiq\_gI\_255 \* i \+ yiq\_gQ\_255 \* q) & 4095;  
    int bi \= (int)(yiq\_bY\_255 \* y \+ yiq\_bI\_255 \* i \+ yiq\_bQ\_255 \* q) & 4095;  
    return (uint)(gammaLUT\[bi\] | ((uint)gammaLUT\[gi\] \<\< 8) | ((uint)gammaLUT\[ri\] \<\< 16) | 0xFF000000u);  
}

你的程式碼已經寫到了非常底層且極度優化（各種 Unsafe、SIMD Vector、Bilinear 的位元操作），能一眼看出我剛才的通用建議會和後面的 SIMD 邏輯衝突，代表你對整份 NTSC 解碼管線的資料流已經瞭若指掌了！