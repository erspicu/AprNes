# 模擬器技術問答集

> 從 JIT、DBT、KVM、靜態重編譯，一路到電晶體級模擬與形式化驗證。問答式整理常見的技術名詞、邊界與選用建議，供有興趣自己動手寫模擬器、或想搞清楚現代模擬器內部運作的讀者參考。

本文分八大主題：

1. [前置觀念：先釐清「模擬器的 JIT」vs「語言的 JIT」](#一前置觀念先釐清模擬器的-jit-vs-語言的-jit)
2. [JIT 在模擬器中的角色](#二jit-在模擬器中的角色)
3. [JIT vs DBT vs KVM](#三jit-vs-dbt-vs-kvm)
4. [LLVM 與其他編譯後端](#四llvm-與其他編譯後端)
5. [靜態重編譯（Static Recompilation）](#五靜態重編譯static-recompilation)
6. [四種技術的練習目標選擇](#六四種技術的練習目標選擇)
7. [現代模擬器的高階技術](#七現代模擬器的高階技術)
8. [研究方向與形式化驗證](#八研究方向與形式化驗證)

---

## 一、前置觀念：先釐清「模擬器的 JIT」vs「語言的 JIT」

### Q1. 我聽過 .NET / Java 的 JIT，也聽過模擬器在用 JIT —— 兩個是同一件事嗎？

**不是。** 兩者都叫 JIT（Just-In-Time），都符合「在執行期才把某種代碼翻譯成 Host 機器碼」這個共同定義，但**輸入來源、語義資訊、翻譯單位、實作層級、失敗模式**全部不同。把兩者混在一起談是入門者最常見的誤解。

下表並列比較最容易混淆的點：

| 面向 | .NET / Java JIT | 模擬器 JIT（Dynarec / DBT） |
|---|---|---|
| **翻譯來源** | 中間碼（CIL / Bytecode），由語言編譯器產生 | Guest 主機的**原生機器碼**（已經是 6502 / ARM / x86 等實體 ISA） |
| **語義資訊** | 豐富：class、method、type、變數名、控制流圖都還在 | 貧乏：只有 bit pattern、暫存器編號、記憶體位址 |
| **翻譯單位** | 方法（Method / Function） | Basic Block（基本塊，從一條指令到下一個跳轉/分支為止） |
| **觸發時機** | 偵測「熱方法」（被頻繁呼叫的 method）後升級重編 | 第一次遇到某個 Guest PC 位址就翻譯，存進 Code Cache |
| **實作位置** | Runtime 內建（CLR / JVM 已經幫你做好） | 模擬器自己寫 —— 自己 emit 機器碼，或借用 LLVM 等後端 |
| **常見問題** | codegen bug、GC 互動、tiered 重編延遲 | Self-modifying code、cycle 計時、indirect jump、cache invalidation |
| **目的** | 延後編譯時機，換取平台無關 + 啟用 runtime profile-guided 優化 | 讓 A 架構的程式能在 B 架構上跑（速度可接受） |

### Q2. 為什麼會混淆？

主要是名字一樣。但更深層的原因是：兩者**架構上長得很像**：

- 都有「翻譯」階段
- 都有「Code Cache」
- 都有「執行翻譯後的代碼」階段
- 都涉及 hot path / cold path 的取捨

但「**輸入是什麼、輸出是什麼、翻譯單位是什麼**」這三個問題的答案完全不同。混淆兩者會讓你在讀模擬器原始碼時，把 .NET 的 Tiered Compilation、PGO、On-Stack Replacement 之類的概念誤以為適用 —— 實際上那都是 .NET runtime 內部的策略，跟你模擬器在做的 Guest→Host 翻譯完全不是同一層。

### Q3. 在 .NET / C# / Java 寫模擬器時，會不會「.NET 的 JIT」就把模擬器的 JIT 工作做掉？

**不會。** .NET 跟 JVM 的 JIT 只負責你寫的 C# / Java 程式碼（CIL / Bytecode → 原生）。Guest 主機（例如你模擬的 NES 6502）所執行的指令，對 .NET 來說只是一個 byte 陣列裡面的數字，runtime 完全不知道那是「另一套 ISA 的機器碼」。

所以你如果想在 C# 上面實作模擬器 JIT，必須自己在 .NET 之上**再蓋一層** —— 常見做法有：

1. **用 `System.Reflection.Emit` 動態生成 CIL** —— 把 Guest 指令翻譯成 CIL，再讓 .NET runtime 幫你編進原生碼。優點：跨平台、安全、不用自己寫 x64 / ARM64 機器碼。缺點：經過 CIL 中介層，效能比直接 emit 機器碼略差，且 CIL 表達不出某些低階操作（如旗標暫存器計算）。
2. **直接寫 mmap + 原生機器碼** —— 申請可執行記憶體頁，自己 emit x64 / ARM64 byte sequence，再用函式指標跳轉過去。優點：效能上限最高、可手動控制每個 register。缺點：跨平台得各自寫一份、debug 極困難、跟 .NET GC 互動容易踩雷。
3. **用 LLVM 後端** —— 把 Guest 指令轉成 LLVM IR，讓 LLVM 幫你做優化跟 codegen。詳見 [Q4.4 LLVM 章節](#四llvm-與其他編譯後端)。

### Q4. 那 .NET 10 的 TieredPGO、AOT、ReadyToRun 跟模擬器 JIT 有沒有關係？

**幾乎沒有關係。** 那些是 .NET runtime 自己的編譯策略，影響的是「你寫的 C# 模擬器主體跑得多快」，不影響「你的模擬器內部那層 Guest→Host 翻譯怎麼做」。

不過間接上有兩個影響值得注意：

- 如果你選的是 **Reflection.Emit 路線**（讓 .NET runtime 幫你二次編譯生成的 CIL），那 .NET 自己的 JIT 品質就會直接決定你模擬器 JIT 的最終 codegen 品質。.NET 10 的 TieredPGO 確實會幫到忙。
- 如果你選的是 **直接寫機器碼路線**，那 .NET runtime 完全不參與你的 JIT — 你只是借用 .NET 來 host 一個自己手寫的編譯器。

### Q5. JVM 的 HotSpot C1/C2 跟模擬器 JIT 是不是同一件事？

不是，但概念有相似處。HotSpot 的 C1（client）跟 C2（server）是針對 Java method 的兩種編譯器（一個快編、一個慢編但代碼品質好），透過 profile 累積決定何時把 method 從直譯升級到 C1，再升級到 C2。

這個「分層編譯」概念在模擬器 JIT 也有對應 —— 例如 Dolphin 的 JIT 也分多級編譯器，但**單位是 basic block 不是 method**，**輸入是 PowerPC 機器碼不是 JVM bytecode**。**形式像、本質不同**。

### Q6. .NET、.NET Framework、Java 三者的 JIT 各有什麼差別？跟模擬器 JIT 有什麼關係？

這個對比最容易讓人混淆，三邊都各自演化了二三十年，內部策略一直在變。重點是：**這三邊都是「語言中間碼 → 原生機器碼」的 JIT，跟「Guest 機器碼 → Host 機器碼」的模擬器 JIT 是兩種完全不同類別的工作**。

先看三邊內部差異：

| 面向 | .NET（5+/6+/8/10）| .NET Framework（4.x）| Java HotSpot |
|---|---|---|---|
| **JIT 編譯器** | RyuJIT | RyuJIT（4.6+）/ JIT64（更早） | C1（client）+ C2（server） |
| **中間碼** | CIL | CIL | JVM Bytecode |
| **Tiered 編譯** | ✅（Tier 0 → Tier 1）| ❌ 預設沒有 | ✅（Interpreter → C1 → C2） |
| **Profile-Guided Opt（PGO）** | ✅ TieredPGO（.NET 6+ 預設開）| ❌ | ✅ HotSpot 一直都有 profile |
| **AOT** | ✅ ReadyToRun（R2R）/ Native AOT | ❌（NGen 算半個）| GraalVM Native Image（第三方） |
| **On-Stack Replacement** | ✅（.NET 7+）| ❌ | ✅ |
| **跨平台** | ✅ Windows / Linux / macOS / Android / iOS | ❌ Windows only | ✅ |
| **與 GC 互動** | 暫存器配置會考慮 GC pause point | 同上 | 同上 |

簡單講：

- **.NET Framework 4.x**：Microsoft 的舊版 Windows-only runtime，JIT 是 RyuJIT 或更早的 JIT64，**沒有 Tiered Compilation、沒有 PGO**。Method 第一次被呼叫時編一次，之後就那樣跑下去。維護期、不再加新 feature。
- **.NET（從 .NET 5 開始）**：跨平台後繼者（前身是 .NET Core）。RyuJIT 的同樣血脈，但**多了 Tiered Compilation**（先快編 Tier 0，跑一段時間再用 profile 重編 Tier 1）跟 **TieredPGO**（用收集到的 profile 進一步優化）。.NET 7 加上 OSR（On-Stack Replacement，可以在執行中的方法跑一半時換成優化版本）。
- **Java HotSpot**：JVM 的主流實作。C1 是「client compiler」（編快、優化少）、C2 是「server compiler」（編慢、優化多）。Method 被呼叫到一定次數才升級 —— 概念跟 .NET 的 Tiered Compilation 幾乎一樣，只是 HotSpot 早做了快二十年。

**關鍵釐清：這三者的差異對「模擬器 JIT」這個討論完全不重要。**

為什麼？因為這三邊的 JIT 處理的都是「語言中間碼」（CIL / Bytecode），編出來的也都是「跑你寫的 C# / Java 邏輯」的原生碼。**Guest 主機（你模擬的 NES、GBA、PS3）的指令永遠繞過這三邊的 JIT** —— 對 .NET / JVM 來說，那些 Guest 指令只是 byte 陣列裡面的數字，runtime 不會去翻譯它們。

所以：

- 你選 .NET 10 寫模擬器，不會因為 TieredPGO 就讓你的 JIT「自動加速」。TieredPGO 只會幫你 C# 主迴圈跑快一點。
- 你選 .NET Framework 4.8 寫模擬器，沒有 Tiered Compilation 也不影響你模擬器 JIT 的設計 —— 設計工作量是一樣的，只是 host 端的 C# 跑得稍微慢。
- 你選 Java 寫模擬器，HotSpot 的 C2 不會魔法地把你的 Guest 指令翻譯成 host 機器碼。

唯一真正重要的差別是：**如果你選擇用 `Reflection.Emit` 路線**（讓 runtime 二次編譯你生成的 CIL），那這三邊 JIT 的 codegen 品質會直接決定你模擬器 JIT 最終生成代碼的品質：

| Runtime | Reflection.Emit 出來的代碼品質 |
|---|---|
| .NET 10 | 最好（TieredPGO + 256-bit Vector + FMA） |
| .NET Framework 4.x | 中等（RyuJIT 但無 tiered） |
| JVM | 好（C2 編譯品質一向強） |

但**如果你選擇直接 mmap 寫機器碼**，這三邊的差異就完全不影響你 —— 因為你只是借用 runtime 來 host 你的編譯器，runtime 自己的 JIT 不參與。

**TL;DR**：「.NET / .NET Framework / Java 的 JIT」是 Q 怎麼跑你寫的高階語言；「模擬器的 JIT」是 Q 你寫的高階語言*之內*怎麼把另一套 ISA 翻譯成 host ISA。兩個 Q 完全獨立，混在一起比較沒有意義。

---

## 二、JIT 在模擬器中的角色

### Q6. 模擬器一定要用 JIT 嗎？

不一定。要不要用 JIT，主要看「目標架構複雜度」與「對效能的要求」。

**不需要 JIT 的情境**：8-bit 機種（NES、Game Boy、Atari 2600）。CPU 時脈只有幾 MHz，現代 PC 純直譯器跑起來有極大餘裕。這類機種的開發重心通常在「週期精確度（Cycle Accuracy）」與「PPU/APU 等周邊跟 CPU 的時序同步」，引入 JIT 反而會讓精確時序控制變得困難（因為 JIT 把多條指令打包成 basic block 一次執行）—— 殺雞用牛刀。

**JIT 是命脈的情境**：x86 PC、N64、PS2、PS3、Switch 這類指令集龐大複雜或時脈動輒幾百 MHz~ 幾 GHz 的系統。直譯器每次都要重做 fetch + decode，光這兩步就吃光 CPU。JIT 把翻譯結果存進 Code Cache，下次執行同一段直接跳過 fetch/decode，效能差距可達一個數量級。

代表性的有 JIT 的模擬器：Dolphin（GameCube/Wii）、RPCS3（PS3）、Ryujinx（Switch）、Citra（3DS）、PCSX2（PS2）。

### Q7. JIT 解決了什麼具體的瓶頸？

純直譯器的迴圈長這樣：

```
while (running) {
    opcode = fetch(PC);    // 讀取一條 Guest 指令
    decoded = decode(opcode); // 拆解 opcode
    execute(decoded);      // 用一連串 if/switch 執行對應動作
}
```

每一條指令都要重做 `fetch + decode`，但 decode 在大多數遊戲裡是高度重複的 —— 同一段程式可能在迴圈裡跑成千上萬次。JIT 觀察到這點：

1. **第一次遇到某段 Guest 指令時**：把整段（一個 basic block，從某條指令到下一個跳轉/分支為止）翻譯成 Host 原生指令，存到 Code Cache。
2. **之後遇到同樣的 Guest PC 位址時**：直接從 Code Cache 跳過去執行，省略 fetch + decode。

對於跑滿 CPU 的遊戲，這個改動可以省掉 80~95% 的解碼成本。

### Q8. 實作模擬器 JIT 最頭痛的是什麼？

依照困難度排序大概是這幾個：

1. **快取失效（Cache Invalidation）**：如果 Guest 程式在執行期改自己的指令（**Self-Modifying Code**，SMC），原本翻譯好存在 Code Cache 的版本就過期了。模擬器必須極速偵測 Guest 寫到「已經翻譯過的記憶體頁」並失效對應的 cache 條目。SMC 在老遊戲非常常見（FDS 動態載入、ROM hack、protect 機制都會用）。
2. **Indirect Jump（間接跳轉）**：`JMP (reg)` 或 `RET` 這種跳轉目標只有執行時才知道，靜態分析無法預先翻譯。實務上得靠 hash table 從 Guest PC 對到 Code Cache 內的 Host 函式指標。
3. **狀態映射**：Guest 暫存器要怎麼存？放 .NET / C struct？還是試圖映射到 Host 暫存器？前者好寫但每次存取都進記憶體；後者快但移植性差且容易跟 GC / call convention 打架。
4. **時序精確度**：JIT 的 basic block 一次執行掉好幾條 Guest 指令，但 PPU / DMA 等周邊可能在中間任何一條指令的時序點需要被推進。要嘛在每條指令後面插入 cycle 累積跟同步檢查（成本高），要嘛接受精度退化（換來性能）。

### Q9. JIT 跟「Cycle-Accurate」可以共存嗎？

可以但很痛苦。一般做法是：在 JIT 的 basic block 結尾插入「現在已經跑了 N 個 cycle」的同步點，再讓 PPU/APU 等周邊「補跑到這個 cycle 為止」（這就是 catch-up 模型）。但這個方式對 cycle-accurate 邊界案例（例如「PPU 在 cycle 256 時讀某個暫存器」）的精度會打折。追求極致精度的模擬器通常不走 JIT 路線。

---

## 三、JIT vs DBT vs KVM

### Q10. JIT 跟 DBT（Dynamic Binary Translation）的差別？

兩者在模擬器界經常被混用，因為底層邏輯非常相似。但定義側重點不同：

| 特性 | JIT（Just-In-Time） | DBT（Dynamic Binary Translation） |
|---|---|---|
| **起源領域** | 程式語言 VM（Java VM、.NET CLR） | 系統模擬與二進制相容性（QEMU、Rosetta 2） |
| **輸入來源** | 中間碼（Bytecode / IL） | 原生機器碼 |
| **目標** | 延遲編譯換取平台無關 + 執行期優化 | 讓 A 架構的程式在 B 架構上能跑 |
| **語義資訊** | 豐富（保有類別、方法、型別） | 貧乏（只有暫存器、記憶體位址） |

模擬器界常說的「JIT」嚴格定義其實是 DBT —— 因為輸入是 Guest 主機的原生機器碼，不是中間碼。但講「JIT」聽眾比較直覺，所以兩個術語在模擬器圈幾乎可以互換。學術上更精確的名字叫 **Dynarec（Dynamic Recompiler）**。

### Q11. 模擬器 DBT 的標準流程？

1. 讀取 Guest 記憶體中的機器碼。
2. 將指令解碼，轉換為內部 IR（中間表示）。
3. 對 IR 做優化（消除冗餘 flag 計算、暫存器分配等）。
4. 將 IR 發射（emit）為 Host 指令（x64 / ARM64 byte sequence）。
5. 結果寫進 Code Cache，下次遇到同個位址直接跳過去執行。

### Q12. 那 KVM 是什麼？跟 JIT/DBT 差在哪？

**KVM（Kernel-based Virtual Machine）** 跟 JIT/DBT 有本質差異：它**不是用軟體去翻譯指令**，而是直接利用 CPU 硬體的虛擬化擴展（Intel VT-x / AMD-V）讓 Guest 指令**直接在實體 CPU 上跑**。

- **JIT/DBT**：軟體層面把 A 架構翻譯成 B 架構再執行
- **KVM**：告訴 CPU「請在硬體隔離環境中直接執行這段 A 架構代碼」

由於是硬體直通，KVM 接近 100% 原生效能。但**有一個關鍵限制**：

### Q13. KVM 的關鍵限制是什麼？

**KVM 必須是「同架構」（Same-ISA）**。

- 你在 x64 PC 上模擬 ARM 系統？KVM 用不上 —— 因為 x64 CPU 不會執行 ARM 指令。
- 你在 ARM64 上模擬 x86 系統？也不行。
- 你在 x64 PC 上模擬另一個 x86 系統？OK，KVM 完美派上用場。
- 你在 ARM64 設備上模擬另一個 ARM 系統？也 OK。

跟 JIT/DBT 的對比：

| 特性 | JIT / DBT（軟體翻譯） | KVM（硬體加速） |
|---|---|---|
| **執行效率** | 原生速度的 20%~50% | 接近 100% 原生速度 |
| **架構需求** | 可跨架構（Cross-ISA） | 必須同架構（Same-ISA） |
| **實作難度** | 極高（需要手寫編譯器後端） | 中等（主要是呼叫 Kernel API） |
| **系統權限** | User mode | Kernel mode |
| **典型案例** | mGBA、RPCS3、Dolphin | Android x86 模擬器、QEMU 加速模式 |

### Q14. 為什麼常在 Android 模擬器或 QEMU 看到 KVM？

兩個原因：

1. **開發效率**：Android Studio 模擬器跑的是 x86 版 Android，搭配 KVM 在開發者的 x64 PC 上會接近原生速度。
2. **QEMU 的彈性切換**：QEMU 跨架構時用 TCG（一種 DBT），同架構時自動切到 KVM 拿到接近原生的效能。

### Q15. ARM64 環境下能不能用 KVM 跑 GBA / NDS 遊戲在真實 ARM CPU 上？

理論可行但有「技術斷層」。

GBA/NDS 用的是 **ARMv4T（ARM7TDMI）/ ARMv5TE（ARM946E-S）**，都是 32-bit ARM。現代 ARM64（AArch64）跟早期 32-bit ARM 指令集差異很大。要直接跑得通，你的 ARM64 CPU 必須支援 **AArch32 執行模式**（向下相容 32-bit）。

但即使指令集相容，還會碰到這幾個障礙：

- **特權指令限制**：GBA 程式直接跟硬體對話。在 KVM 模式下，當 GBA 程式嘗試切換處理器模式或存取系統暫存器，會觸發 trap，KVM 把控制權交回給你的模擬器，你必須手動模擬這個行為（**Trap and Emulate**）。
- **記憶體映射**：GBA 的記憶體佈局（例如 `0x08000000` 是 ROM）跟 Linux process 完全不同，得用 KVM 的 stage-2 translation 重建。
- **硬體周邊**：KVM 只能加速 CPU 指令。GBA 的 PPU、APU、DMA 在真實 ARM CPU 上根本不存在，你還是得用軟體模擬。

實務上接近的方案是 ARM64 Linux 上跑 **QEMU + `--enable-kvm`** 並指定 32-bit ARM 目標，讓硬體支援的話 QEMU 自動切到 KVM 模式。但對於追求 cycle-accurate 的 GBA 模擬，這個方案會失去對 CPU 細節時序的控制權，反而不適合。

---

## 四、LLVM 與其他編譯後端

### Q16. 為什麼有些模擬器選擇用 LLVM 做 JIT 後端？

傳統 Dynarec 是開發者自己手寫機器碼 emitter（ARM 指令翻譯成 x64 指令）。這做法的痛點：

- **難以維護**：你得精通 x64、ARM64、RISC-V 等多種組語。
- **優化有限**：自己寫的 emitter 很難達到專業編譯器級別的指令重排、暫存器分配、死碼刪除。

用 LLVM 的好處：

1. **世界級優化器**：LLVM 的 PassManager 直接幫你做 dead code elimination、constant folding、loop invariant hoisting 等。
2. **多平台 codegen**：你只負責把 Guest 指令翻譯成 **LLVM IR**，LLVM 自動產出對應 Windows (x64)、macOS (Apple Silicon)、Linux (ARM64) 的 host 機器碼。

### Q17. LLVM 後端的標準流程？

1. **Frontend**：模擬器讀 Guest 二進制指令。
2. **IR Generation**：翻譯成 LLVM IR（一種帶豐富型別資訊、長得像組語的中間語言）。
3. **Optimization**：呼叫 LLVM PassManager 做優化。
4. **Execution（JIT）**：用 LLVM 的 ORC（On-Request Compilation）或 MCJIT 引擎，把 IR 即時編譯成 host 機器碼並 mmap 進可執行記憶體。

### Q18. LLVM 的代價是什麼？

不是銀彈，對某些模擬器來說太重：

- **編譯延遲**：LLVM 優化非常耗時。執行時遇到沒跑過的代碼，呼叫 LLVM 編譯可能造成明顯卡頓（stuttering）。這就是為什麼 RPCS3 / Ryujinx 等都會做「Shader Cache 預編譯」。
- **體積龐大**：LLVM 函式庫很大，模擬器 binary 會從幾 MB 暴增到幾百 MB。
- **C# 整合難度**：要從 .NET 呼叫 LLVM 的 C++ API 需要透過 P/Invoke 或封裝層（如 LLVMSharp），整合不便。

### Q19. 哪些模擬器用 LLVM？

- **RPCS3（PS3）**：把 PPU/SPU 指令編譯成 x86-64，是它能流暢跑大作的關鍵。
- **Cemu（Wii U）**：也用 LLVM 做翻譯後端。
- **Dolphin** 曾實驗過 LLVM 後端。

### Q20. 不用 LLVM 的話，還有哪些選擇？

- **Dynarmic**：專為 ARM 指令集設計的 dynarec library，被 Citra、yuzu 等採用。比 LLVM 輕量很多，編譯也快。
- **手寫 emitter**：對單一 host 架構手刻 byte sequence。Dolphin 早期 JIT 就是這條路。
- **Cranelift**：Rust 生態系的低延遲 codegen 後端，wasmtime 在用。模擬器界開始有人嘗試。

---

## 五、靜態重編譯（Static Recompilation）

### Q21. 什麼是靜態重編譯？跟 JIT 哪裡不一樣？

**JIT/DBT** 是執行時才翻譯 —— 一邊跑一邊翻。**靜態重編譯（Static Recompilation）**是在遊戲執行**之前**，把整個 ROM 反組譯，**全部翻譯成現代 PC 的 C++ / 機器碼**，編出一個獨立的 `.exe` 執行檔。

跑起來不像「模擬」，更像「原生移植（Native Port）」。

### Q22. 為什麼要這樣做？

- **極致效能**：沒有執行時翻譯開銷。
- **無限優化潛力**：編譯器（GCC / Clang）有充裕時間做深層優化。
- **現代功能整合**：可以加超高解析度、寬螢幕、Ray Tracing 等。
- **不需要 JIT 權限**：對 iOS 等禁止第三方 JIT 的封閉平台極友善 —— 因為它本身就是個編譯好的原生 App。

### Q23. 靜態重編譯的挑戰？

非常難實現，所以這類專案稀少：

- **程式與資料不分**：ROM 裡指令和資料常常混在一起。重編譯器把資料當指令翻譯就會崩潰。
- **間接跳轉（Indirect Jumps）**：`JMP (reg)` 跳轉目標執行時才知道，靜態無法預測所有可能。
- **Self-Modifying Code**：靜態 `.exe` 沒辦法應對執行時改自己指令的行為。

### Q24. 著名的成功案例？

- **《薩爾達傳說：時之笛》Ship of Harkinian**：N64 原始碼提取重構成 C++，PC 上 4K/60fps + 模組支援。
- **《超級瑪利歐 64》PC Port**：MIPS 指令集靜態映射到現代架構，幾乎所有現代硬體都跑得動。
- **N64 Recomp**：通用工具鏈，自動把 N64 ROM 靜態重編成 C 語言。最近的《薩爾達傳說：穆修拉的面具》PC 版就是靠這個誕生的。

這類技術更偏向「軟體工程 + 逆向工程」的結合，追求的不是 100% 硬體精度，而是「讓這款遊戲在現代平台上獲得最佳體驗」。

---

## 六、四種技術的練習目標選擇

### Q25. 想練 JIT、DBT、KVM、Static Recompilation 這四種，分別該選什麼當練習目標？

**練習 JIT — 建議目標：Game Boy Advance（GBA）**
- ARM7TDMI 架構規則清晰、文件齊全。
- GBA 處於「直譯器跑得動，但 JIT 會大幅噴發」的甜蜜點。
- 練習 basic block 辨識、Code Cache 管理、JIT 過程中處理中斷。
- 難度：★★★☆☆

**練習 DBT — 建議目標：Intel 8086 / 80286**
- x86 旗標暫存器計算頻繁，可練「延後旗標計算（Lazy Flag Evaluation）」。
- 暫存器數量少，練 Guest→Host 暫存器映射的精髓。
- 變長指令（1~15 byte）比 ARM 定長指令更鍛鍊解碼器設計。
- 難度：★★★★☆

**練習 KVM — 建議目標：i386 以前的 PC（支援實模式 / 保護模式）**
- x64 主機跑 x86 Guest 是發揮 KVM 唯一場景。
- 練 Linux Kernel API（`ioctl`）、虛擬 CPU 暫存器設定、VM Exit 處理。
- Guest 執行 `OUT` 指令存取硬體時 KVM 會 trap 出來，這時你必須模擬對應硬體行為。
- 難度：★★★★☆

**練習 Static Recompilation — 建議目標：Chip-8 或單一個 NES 簡單遊戲**
- Chip-8 結構單純，最適合做概念驗證（PoC）。
- 進階挑戰可以選一個小型 NES 遊戲（如《Donkey Kong》《Super Mario Bros.》），會強迫你直接面對 indirect jump、code/data 分離等核心難題。
- 難度：★★★★★（最難在於逆向分析的自動化）

### 技術路徑總結表

| 技術 | 推薦平台 | 核心練習價值 |
|---|---|---|
| **JIT** | GBA | IR 生成、動態編譯流量管理 |
| **DBT** | x86 16-bit | 指令優化、Flag 狀態同步 |
| **KVM** | x86 32-bit | Hypervisor API、硬體異常攔截 |
| **Static Recompilation** | NES / Chip-8 | AOT 靜態分析、原生代碼移植 |

---

## 七、現代模擬器的高階技術

### Q26. NDS / 3DS 模擬器為什麼幾乎都在用 JIT？

這兩代主機 CPU 指令集複雜度跟時脈已經超出純直譯器在中階電腦上的負荷。

**NDS（ARM7 + ARM9 雙核）**：主頻不高（67 MHz / 33 MHz），但雙核架構 + 大量硬體中斷同步。melonDS / DeSmuME 早期都是直譯器，後來都加入 JIT recompiler，PC 上能帶來 1.5x~2x 以上的效能提升。Android 端的 DraStic 之所以能在十多年前的手機跑滿速，核心黑科技就是高度優化的 ARM-on-ARM JIT。

**3DS（ARM11 MPCORE）**：雙核（New 3DS 是四核），268 MHz~ 804 MHz。Citra 用純直譯器跑大作（《精靈寶可夢》《薩爾達》）即便在高階 i9 上也可能不到 10fps。Citra 的 JIT 把 ARM11 編譯成 x86-64 才讓 4K 渲染變得可能。

iOS 是反例：Apple 禁止第三方 App 用 JIT，所以 3DS 模擬器在 iOS 上即便用最新硬體也很慘，除非透過特殊側載開啟系統 JIT 權限。

### Q27. GPU API 橋接（HLE 圖形模擬）是什麼？

老一代模擬器用軟體模擬 PPU 的每個暫存器和掃描線（**LLE，Low-Level Emulation**）。現代主機（PS3 / Switch）的遊戲透過 Vulkan / OpenGL / NVN 畫圖 —— 模擬器不再用 CPU 慢慢計算每個像素，而是扮演翻譯官，**直接把繪圖指令轉發給 PC 的顯卡**。

流程：
1. 攔截遊戲的繪圖呼叫（例如 `glDrawElements` 或 `vkCmdDrawIndexed`）。
2. 把參數（頂點、紋理、shader）轉成 PC 顯卡能理解的格式。
3. 呼叫 PC 端的 Vulkan 或 D3D12 API，讓顯卡直接運算。

這稱為 **HLE（High-Level Emulation）圖形模擬**。

### Q28. Shader Recompilation 是什麼？為什麼新場景會卡？

遊戲主機顯卡（例如 Switch 的 Maxwell GPU）有自己的 shader 機器碼，無法直接在 PC 顯卡跑。模擬器在執行時要把遊戲的 shader **即時重編譯**成 PC 顯卡支援的格式（SPIR-V / HLSL）。

副作用就是進入新場景時的 "Compiling Shaders..." 卡頓。雲端 Shader Cache 是這個問題的解法 —— 既然每個玩家會生成一樣的 shader，從雲端下載別人編譯好的版本就行。Dolphin、Citra 都支援。

### Q29. GPU 橋接跟 CPU JIT 的關係？

**JIT/KVM 處理 CPU 運算，GPU 橋接處理 GPU 運算。** 兩者互補。WINE / Proton 的 DXVK 是這項技術的巔峰 —— 把 Windows 的 DirectX 即時橋接成 Linux 的 Vulkan，Steam Deck 就靠它。

### Q30. 近幾年模擬器界還有哪些新趨勢？

從「跑得動」進化到「跑得比原機更強」：

1. **AI 紋理超解析（AI Upscaling）**：把遊戲低解析度紋理在送 GPU 前用 ESRGAN 等模型即時補細節，輸出 4K 等級畫質。PS2、GameCube 模擬器社群很流行。
2. **即時 OCR 翻譯**：RetroArch 等利用 OCR 抓取畫面文字，透過 Google/DeepL API 即時翻譯覆蓋回畫面。
3. **Rollback Netcode**：原本是格鬥遊戲的網路同步技術，現在引入老遊戲模擬器。預測對方輸入，預測錯就利用即時存檔回溯幾影格重算。Fightcade 讓全球玩家流暢對戰《街頭霸王》。
4. **HLE 音訊重建**：取代 LLE 的 DSP 模擬，直接把 Guest 音訊流轉成 PC 的 XAudio2 / SDL Audio 指令。解決多核 CPU 同步壓力。
5. **雲端 Shader Cache**：見 Q28。
6. **FPGA 模擬**：如 Mister FPGA，用 HDL 在電路層級重新設計老主機晶片。零延遲、極致週期精確，是「原機手感」的終極方案 —— 但嚴格說已經不算軟體模擬器了。

### Q31. 老遊戲增強技術（Enhancement Hacks）有哪些？

不只「還原」，是利用現代硬體去補完當年的遺憾：

- **2D 遊戲 3D 化**：3dSen 把 NES sprite 透過手動或自動 profiling 賦予深度，可以從側面觀察瑪利歐在管子裡跳。原理是攔截 PPU 繪圖指令，判斷哪些是背景、哪些是角色，再投影到 3D 空間。
- **寬螢幕補丁**：對 PS1/N64/PS2 直接修改遊戲記憶體中的「投影矩陣」或 FOV 值，渲染出原本被裁切的畫面，從 4:3 變 16:9 原生。
- **HD 紋理替換**：對遊戲每張紋理生成 hash，模擬器偵測到要載入低解析度紋理就「掉包」成玩家放在資料夾的 4K 圖。《風之律動》《怪獸獵人》模擬器社群有極精美的高畫質紋理包。
- **MSU-1 音源置換（SNES）**：虛構一個當年不存在的擴充晶片。模擬器攔截 8-bit 電子音播放指令，改成讀取外部 PCM 檔（CD 品質）。可以在玩《超時空之鑰》《眾神的三角神力》時聽到真實管弦樂版本的 BGM。

### Q32. 怎麼把這些技術整理成一個全景？

| 層次 | 目標 | 代表技術 |
|---|---|---|
| **基礎層（Core）** | 跑得動、跑得準 | Interpreter、Cycle Accuracy |
| **加速層（Speed）** | 跑得順、跨平台 | JIT、DBT、KVM、LLVM 後端 |
| **增強層（Enhance）** | 更好看、更好聽 | HD Textures、MSU-1、Widescreen Hack、AI Upscale、Rollback |
| **原生層（Native）** | 徹底脫離模擬 | Static Recompilation、Source Port |

---

## 八、研究方向與形式化驗證

### Q33. Visual6502 那種電晶體級模擬有人成功跑 NES 遊戲嗎？

**Visual6502** 的核心技術是**電晶體級模擬（Transistor-level Simulation）**—— 不是模擬指令、不是模擬邏輯閘，是模擬**每一個電晶體的開關狀態與電路導通的物理行為**。

技術上已經達成，實際上「**慢到無法玩**」。

NES 的 CPU 是 Ricoh 2A03（基於 6502）。Visual6502 團隊開發了 **Visual2A03**，可以在瀏覽器中模擬電路圖每一條走線。但要跑完整 NES 遊戲（《超級瑪利歐》之類），運算量會讓現代最強 PC 也只能跑出每秒幾影格。

最大的挑戰是 **PPU（2C02）**，電路複雜度遠高於 CPU，且涉及大量類比訊號輸出（NTSC 訊號生成）。雖然有 PerfectPPU 等專案在掃描，但 CPU + PPU 連動的電晶體模擬，運算壓力會呈幾何倍數增長。

### Q34. 既然慢到不能玩，這種技術為什麼重要？

它是模擬器開發者的「**終極參考書**」：

- **解開硬體謎團**：以前模擬器作者只能靠「猜測」處理邊界案例。電晶體級模擬可以從電路層級看清楚某個指令在特定時間點為什麼會產生 bug。
- **修正週期精確度**：許多現代 cycle-accurate 模擬器的精確時序文件，正是受益於前人觀察電晶體行為整理出來的。

### Q35. Visual2C02 怎麼產生的？

研究者用強酸溶解晶片表層、用電子顯微鏡拍照、把數萬個電晶體手動向量化。徹底揭開了 NES 顏色生成與 sprite overflow 的底層邏輯。

### Q36. 模擬技術的層級可以怎麼分？

| 技術層級 | 模擬單位 | 速度 | 用途 |
|---|---|---|---|
| **指令級**（JIT / Interpreter） | OpCode | 極快 | 玩遊戲 |
| **邏輯閘級**（FPGA / HDL） | Gate / Flip-flop | 原生速度 | 精確硬體還原 |
| **電晶體級**（Visual6502） | Transistor / Wire | 極慢（Hz 等級） | 科學研究、骨灰級逆向 |

### Q37. 模擬器是博碩士論文題目嗎？

是。常見研究方向：

1. **DBT 與效能優化**：探討 A 架構指令在 B 架構跑得更快的方法。中研院 / 台大 / 清大體系有許多論文討論 LLVM 作 QEMU 後端（HQEMU 框架）、多執行緒 DBT、軟體 TLB、indirect branch caching、Code Cache 管理等。
2. **全系統模擬與虛擬化**：與 KVM / 硬體輔助虛擬化相關，目標模擬整台電腦含周邊。Stanford McKeown 實驗室的 High-Fidelity Emulation、嵌入式系統跨平台模擬器都是案例。
3. **指令集模擬與形式化驗證**：用 formal methods 驗證模擬器的指令解碼器。RISC-V 教學模擬器設計也是常見題目。
4. **圖形 API 轉譯**：跨平台圖形指令轉換（OpenGL → Vulkan / DirectX），與 3DS / Switch 模擬器核心技術重疊。

中文搜尋關鍵字：`動態二進制碼轉譯`、`虛擬化技術`、`指令集模擬器`、`QEMU 優化`。

### Q38. Lean 這種定理證明工具能用在模擬器嗎？

可以，但目前還在學術階段，沒有主流遊戲模擬器是純 Lean 寫的。三個應用方向：

1. **形式化驗證的處理器模擬（Verified ISA Simulator）**：用 Lean 定義 CPU 指令集的數學模型（Specification），然後證明模擬器實作完全符合規格。常見於 RISC-V / ARM 形式化模型，作為晶片開發前的「黃金參考模型」。
2. **驗證 JIT 編譯器正確性**：LambdaClass 等團隊用 Lean 4 開發**經證明的優化引擎**，確保 Guest→Host 翻譯過程語義不變。對零知識證明虛擬機之類的高安全環境很重要。
3. **Lean 4 作為高效能模擬器的基礎**：Lean 4 用引用計數 + Functional but In-place 技術，純函數式代碼底層執行時可以像 C 一樣直接改記憶體，模擬暫存器狀態很合適。社群有人嘗試實作 Chip-8 / 6502。

### Q39. Lean 適合 / 不適合的情境？

| 優點 | 挑戰 |
|---|---|
| 透過證明確保 `cpu.Execute()` 100% 符合硬體規範 | 學習曲線極陡（需懂 Dependent Type Theory） |
| Lean 4 編譯後的 C 代碼精簡且快 | 生態系尚小，缺現成 UI / SDL / 音訊 bindings |
| 強大的 metaprogramming，可寫巨集自動產生指令翻譯代碼 | 為了通過證明檢查，開發時間遠長於 C# / Rust |

對於追求極致「指令行為精確度」的研究型開發者，Lean 提供「**只要編譯得過、行為就絕對正確**」的終極保證。但對主流遊戲模擬器開發來說，目前 Lean 4 更像是「驗證最核心指令翻譯邏輯」的實驗工具，而非整體實作語言。

---

## 結語

模擬器技術從早期單純的指令直譯，演化到今天的多層架構：CPU 用 JIT 或 KVM 加速、GPU 用 HLE 橋接到 host API、shader 編譯走雲端快取、紋理走 AI 超解析、整個 ROM 甚至可以靜態重編譯成原生 PC 應用。

每一層都不是必需的，選用哪些取決於你的目標 —— 追求 cycle-accurate 學術級精度的小機種模擬器，可能根本不需要 JIT；追求現代硬體上跑 PS3/Switch 大作的流暢度，則離不開 JIT、HLE GPU 橋接、shader cache 三件套。

寫模擬器不只是「讓老遊戲跑起來」，每一個子題目背後都連著編譯器、虛擬化、形式驗證、逆向工程、計算機體系結構這些更大的領域。從直譯器開始，逐步往 JIT / DBT / KVM / 靜態重編譯延伸，是一條既硬核又有結構性回報的學習路徑。
