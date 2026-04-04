在標準的早期 **iNES (1.0)** 格式中，Header（標頭）其實**並不會**直接告訴你這款遊戲是 PAL 還是 NTSC。這也是早期模擬器開發者最頭痛的地方。

不過，隨著技術演進，後來的 **NES 2.0** 規範解決了這個問題。以下是詳細的拆解：

### **1\. 舊版 iNES (1.0) 的限制**

在傳統的 16-byte iNES Header 中：

* **Byte 9**：原本被定義為存放 TV 系統位元（Bit 0: 0=NTSC, 1=PAL）。  
* **現實情況**：絕大多數的 ROM 轉錄者（Dumpers）在早期根本沒有填寫這個位元。因此，大部分的 .nes 檔案在 Byte 9 通常都是 00。  
* **後果**：模擬器如果只看 iNES 1.0 的 Header，很容易把 PAL 遊戲誤判為 NTSC，導致音樂變快、畫面撕裂。

### **2\. 新版 NES 2.0 的解決方案**

為了修正這個錯誤，現代模擬器社群推廣了 **NES 2.0** 格式。它利用了標頭中原本被浪費的空間：

* **Byte 12 (TV System)**：  
  * 00：NTSC  
  * 01：PAL  
  * 02：兩者皆可 (Multiple region)  
  * 03：Dendy (俄羅斯常見的特殊 NTSC/PAL 混合體)

### ---

**3\. 模擬器開發者如何「猜」出 PAL？**

既然 Header 不可靠，當你的 C\# 核心載入一個標頭不完整的 iNES 1.0 檔案時，通常有三種做法：

1. **內部資料庫 (Internal Database)**：  
   這是最專業的做法（如 Nestopia 或 Mesen）。模擬器會計算 ROM 的 **CRC32 或 SHA-1 雜湊值**，然後去比對內建的資料庫（例如：如果雜湊值是 0xABC123，那它就是歐洲版的《超級瑪利歐》）。  
2. **檔名偵測**：  
   檢查檔名中是否含有 (E), (Europe), (PAL), (G), (F) 等關鍵字。雖然不嚴謹，但在沒有資料庫的情況下非常有效。  
3. **手動切換**：  
   在你的模擬器介面選單中，提供一個「NTSC / PAL / Dendy」的強制切換開關，讓使用者自己選。

### ---

**4\. 為什麼「Dendy」也要考慮？**

在台灣、中國與俄羅斯的早期市場，流行一種叫 **Dendy** 的相容機。它的硬體時序非常奇怪：

* 它使用 PAL 的 50Hz 輸出。  
* 但它的 CPU 除頻與時序卻比較接近 NTSC。  
* **如果你的 Master Clock 邏輯要做得精細，未來可能也需要考慮 Dendy 模式。**

### ---

**C\# 實作建議：Header 解析邏輯**

在你的 Cartridge 類別中，解析標頭時可以這樣寫：

C\#

public Region DetermineRegion() {  
    // 1\. 檢查是否為 NES 2.0  
    if (IsNes20()) {  
        byte tvSystem \= header\[12\] & 0x03;  
        if (tvSystem \== 1) return Region.PAL;  
        if (tvSystem \== 0) return Region.NTSC;  
    }

    // 2\. 如果是 iNES 1.0，嘗試讀取 Byte 9 (雖然通常不可靠)  
    if ((header\[9\] & 0x01) \== 1) return Region.PAL;

    // 3\. 預設回傳 NTSC (因為市面 80% 都是 NTSC)  
    return Region.NTSC;   
}

**既然您已經考慮到時序問題，您是否需要我整理一份「NTSC vs PAL vs Dendy」的 Master Clock 除頻對照表，好讓您的 RunCycle 邏輯能根據區域自動切換速度？**