# SIGABA / ECM Mark II — 研究筆記與加入 EnigmaBenchmark 的可行性

整理自 2026-04-19 對話。作為日後加入 EnigmaBenchmark 家族時的參考備忘。

---

## TL;DR

- **SIGABA 是二戰唯一未被任何人破解的主要密碼系統**，這點不是誇飾
- **演算法結構已於 2001 年 NSA 解密，學術界完整重建**（Savard-Pekelney 1999 的推測在解密後被證實正確）
- **可以實作模擬器**（多個開源版本存在），加密/解密 round-trip 可驗證
- **但「破解」benchmark 在 SIGABA 上會失敗**——不是因為 compute 不夠，是因為 **IC 評分找不到信號**；這正是它進 benchmark 的教育意義：顯示真正安全的 rotor 機器長什麼樣
- **具體戰時 rotor 接線值多數仍未公開**，但用重建版 / 測試 wirings / PRNG 產生的 wirings 都能讓 benchmark 結構合法

---

## 1. 歷史地位

美國陸海軍二戰至 1950 年代初戰略級密碼機，也叫 **ECM Mark II**（Electric Cipher Machine），美國陸軍內部代號 **SIGABA**，海軍代號 **CSP-889 / CSP-2900**。

- 1935 年 William Friedman 與 Frank Rowlett 設計
- 1940 年左右正式服役
- 戰爭期間，德國、日本、義大利情報單位全部嘗試分析過，**全數失敗**
- 戰後冷戰初期繼續使用到 1950 年代中
- NSA 視為「至今仍體現深刻安全設計原則」的機器
- 2001 年 10 月正式解密文件

## 2. 為什麼 SIGABA 這麼硬

跟 Enigma 乍看同屬轉輪機家族，但從**步進機制**這個根本面上徹底顛覆 Enigma 的弱點。

### 機制對照表

| 機制 | Enigma (M3/M4) | SIGABA (ECM Mark II) |
|------|---------------|---------------------|
| **轉輪總數** | 3 或 4 | **15**（分三組 5+5+5）|
| **組別** | 單一組，全部加密用 | **Cipher (5) + Control (5) + Index (5)**，各司其職 |
| **步進規律** | 固定 notch 規則，幾乎每鍵一步 | **完全不規則**——每次按鍵 **1 到 4 個** cipher rotor 前進，誰前進由 Control + Index 雙層混沌決定 |
| **Depth 攻擊可行？** | 是（Bletchley 主武器）| **否**——同金鑰重發兩次訊息，cipher rotor 的步進序列都不一樣 |
| **Known-plaintext attack** | Turing 的 crib 攻擊主力 | 被 Control+Index 反饋結構打散，crib 無法延展 |
| **IC-based scoring** | 有效 | **信號極弱**——irregular stepping 讓密文統計接近真隨機 |

### 核心設計：雙層混沌步進

1. **Control rotors（5 個）** 每次按鍵都規律步進（類似 Enigma 的 fast rotor），但它們不做加密，只產生偽隨機訊號
2. **Control 輸出經過 Index rotors（5 個）** 混合/置換。Index 在加密期間**不步進**，只做靜態 lookup
3. **Index 的 10 個輸出經過邏輯電路，決定這一輪哪 0-4 個 Cipher rotor 要步進**
4. **Cipher rotors（5 個）** 做實際的 Enigma-style 加密

關鍵反饋：**哪個 Cipher rotor 會步進，由 Control 目前的狀態 + Index 的靜態接線共同決定**。這讓 Cipher rotor 的步進序列不是一個可預測的週期函數，而是一個 ~10²⁰ 長的偽隨機序列。

這正是 Enigma 被破的根本原因：notch 位置一公開，Bletchley 就能在已知時間點附近枚舉 stepping state。SIGABA 把 stepping 變成 keystream 的一部份，**連步進模式本身都是機密**。

## 3. 破解歷史：零

- **戰時（1940-45）**：Axis 情報機關全部嘗試，零成功紀錄
- **冷戰初期（1945-55）**：蘇聯 KGB 據說分析過（OPERATION VENONA 的平行案），沒有公開成功紀錄
- **解密後（2001-）**：
  - **Stamp & Chan 2007** — *"A Ciphertext-Only Attack on SIGABA"*（Cryptologia）——對**簡化變體**（index 減到 3 或更少）的統計攻擊可行，完整 SIGABA 仍不破
  - **Lee 2003** — 已知明文攻擊的可行性討論，結論不可行
  - 至今**沒有任何公開文獻成功破譯真實 SIGABA**

對比 Enigma：1974 年 Winterbotham 公開 Ultra Secret，所有人都知道它被破了。
對比 SIGABA：2001 年解密至今 24 年，仍然沒有「被破」的公開宣告。

## 4. 公開狀況細節

解密程度分層：

| 項目 | 公開狀況 | 可信度 |
|------|---------|--------|
| **整體演算法結構** | ✅ 2001 年 NSA 正式解密 | 完整 |
| 三組轉輪佈局（5+5+5）| ✅ 公開 | 確定 |
| Control rotor 步進規則 | ✅ 公開 | 確定 |
| Index rotor 置換邏輯 | ✅ 公開 | 確定 |
| Control → Index → Cipher 反饋電路 | ✅ 公開 | 確定 |
| Cipher rotor 接線「結構」（26×26 雙邊接線）| ✅ 公開 | 確定 |
| **戰時具體 rotor 接線值（每輪 26 條線各接哪）** | ⚠️ **多數仍未公開或未集中發表** | 不完整 |
| 日常金鑰產生程序 | ⚠️ 部分公開 | 不完整 |
| 特定作戰單位的 key schedule | ❌ 多數未解密 | 很少 |

## 5. 學術重建來源（按時間）

### 1999 · Savard & Pekelney (Cryptologia)
*"The ECM Mark II: Design, History, and Cryptology"*

在 NSA 正式解密**之前兩年**，從以下來源推導出完整運作模型：
- 美國專利（Friedman、Rowlett 等人的公開專利）
- 國會聽證作證紀錄
- 少量部分解密的 NSA 文件
- 少量保存下來的實機照片

2001 解密後比對，Savard-Pekelney 推論**大部份正確**，只在細節（某些鍵線路徑）有偏差。這是密碼機研究史上最漂亮的逆向工程作品之一。

### 2003 · Mark Stamp（模擬器）

Java 實作的 SIGABA simulator，使用公開重建的 rotor wirings 與完整三組步進邏輯。至今仍可在 Stamp 的學術首頁下載。

### 2007 · Stamp & Chan (Cryptologia)
*"A Ciphertext-Only Attack on SIGABA"*

針對**簡化變體**的統計攻擊：
- Index rotor 減到 3 個或更少：統計攻擊在幾週 CPU time 內可行
- 完整 5-Index SIGABA：攻擊者需要的密文量 + 計算量仍超出實務
- 攻擊原理是利用 Index rotor 混合後的偏差，但完整版把偏差稀釋到噪音以下

### 2008 · Stamp, *Applied Cryptanalysis*（書）

第 6 章專門討論 SIGABA 的解析嘗試，整合了上述論文 + 對歷史文獻的綜合。

### 其他

- **Lee 2003** — 已知明文攻擊的上界分析
- **Sullivan & Weierud** 散篇筆記 — 散見於密碼史愛好者網站
- **GitHub 多個非商業重建**（多以 Savard-Pekelney + Stamp 為基礎）

## 6. 實作可行性（加進 EnigmaBenchmark）

### 6.1 機器實作——可行

需要實作的 logic 全部公開：

```
input char
  → Cipher rotors forward (5 個 Enigma-style rotor)
  → reflector (或直接反向)
  → Cipher rotors reverse
  → output char

每次按鍵後，step 邏輯：
  1. Control rotors：fast rotor 每鍵步進，medium/slow 依 notch
  2. Control 輸出經 Index rotors 靜態置換
  3. Index 輸出進入磁鐵組，4 個 Cipher rotor 各自獨立 ON/OFF
  4. 被選中的 Cipher rotor 前進一步（不一定一步，某版本是「向後一步」）
  5. 回到 input
```

實作工時：估計 400-600 行 C#，比 T52e 稍短（沒有 KTF 這種 plaintext feedback，Index 層是靜態的）。

### 6.2 Rotor wirings——三個選項

| 選項 | 來源 | 歷史真實度 | 可行性 |
|------|------|----------|--------|
| A. 戰時實際 wirings | 多未公開 | 100% | ❌ 拿不到 |
| B. Stamp 模擬器的測試 wirings | 公開、有署名 | 0%（純虛構）| ✅ 可用，需引用 |
| C. 改寫 Enigma-V wirings | 結構類似 | 0%（純虛構）| ✅ 可用 |
| D. PRNG seed 生成 | 新鮮生成 | 0%（純虛構）| ✅ 與 T52e 做法一致 |

**建議走 D**，跟本 benchmark 其他機器（T52e、ADFGVX 等）做法一致，用 `Random(seed)` 產生一組合法接線，這樣不會牽涉「這份接線從哪抄來」的著作權/來源追溯問題。

### 6.3 Benchmark 搜尋空間——這裡是痛點

| 縮減策略 | Keyspace | 評論 |
|---------|---------|------|
| 只搜 Cipher rotor 起始位置 | 26⁵ = **11,881,376** | 可跑 |
| 再搜 Control rotor 起始位置 | 26⁵ × 26⁵ = 10¹⁴ | 不可跑 |
| 加 Index rotor 起始 | 10¹⁴ × 10⁵ = 10¹⁹ | 不可跑 |
| 完整 | ~10²¹+ | 不可跑 |

所以 benchmark 只能做 26⁵ = 12M 這個 scope。這個大小跟 Lorenz χ-only 的 22M、T52e 的 24M 同量級，**compute-wise 完全跑得動**。

### 6.4 但「評分找不到信號」——這才是真正的挑戰

12M 裡只有 1 個正解。要從 12M 候選 decrypt 結果中挑出那 1 個，需要**有效的 scorer**：

- **Enigma**：26-letter IC 有 10σ 以上信號，穩定分辨
- **Lorenz χ-only**：Baudot IC + Δ-statistic 有明確信號
- **T52e**：Baudot IC 搭 KTF-off 模式有信號
- **SIGABA**：**irregular stepping 把所有這類信號稀釋到接近均勻分布**。12M 個 candidate 的 IC 分佈會幾乎全部落在 0.033-0.037 的隨機區間，正解可能只高出 0.001，**在噪音裡淹沒**

這就是為什麼 Stamp-Chan 要簡化 Index 層才能攻擊——Index 全開的話統計偏差低於噪音。

### 6.5 實作後的預期輸出

跑下來的 benchmark log 會像：

```
──── RUN  SIGABA (ECM Mark II) — Cipher rotor start recovery  (11,881,376 keys) ────

Historical context: SIGABA was the US Army/Navy strategic cipher used
  1940-1955. Never operationally broken by any adversary during or
  after WWII. The irregular three-bank stepping (Cipher/Control/Index)
  was specifically designed to defeat depth and known-plaintext attacks.

True Cipher starts: [A, B, C, D, E] (5 positions)

  [Scalar SIGABA]           11,881,376 keys / 85.2s (139 K/s)  bestIC=0.0342  found=False
  [Parallel 16 cores]       11,881,376 keys /  6.3s (1.9 M/s)  bestIC=0.0341  found=False
  [SIMD]                    11,881,376 keys /  2.8s (4.2 M/s)  bestIC=0.0337  found=False
  [SkSL GPU]                11,881,376 keys /  0.3s (39 M/s)   bestIC=0.0346  found=False

  Best-scoring recovered: [K, Q, M, A, T]   (WRONG — not true key)
  All backends terminate the full search with IC below 0.045 threshold.

HISTORICAL VERIFICATION: no backend broke SIGABA. This is the expected
  result. Irregular stepping dilutes the IC signal below statistical
  separability, and no known analytic attack exists against the full
  machine. The GPU's 200× speedup over scalar cannot substitute for
  a structural weakness in the cipher.
```

**這個輸出是整個 EnigmaBenchmark 的「定海神針」**：告訴觀眾「同樣是轉輪機，Enigma 被 GPU 秒殺，SIGABA 秒不破」，展示**結構**比**算力**更決定安全。

## 7. 加進 benchmark 的建議

### 選項評估

| 方案 | 描述 | 工時 | 教育價值 | 技術價值 |
|------|------|------|---------|---------|
| **A. 純模擬 + demo** | 實作機器、加密 / 解密 round-trip、不含 cracker | ~2 天 | 中 | 低 |
| **B. 模擬 + cracker（預期失敗）** | 含四個 backend 的 brute force + 統計 scorer | ~3 天 | **高** | 中 |
| **C. 模擬 + Stamp-Chan 簡化變體攻擊** | 對簡化 Index 成功 crack，完整版失敗並解釋差異 | ~5 天 | 非常高 | 高 |
| **D. 不加** | 維持現狀 6 台密碼機 | 0 | 0 | 0 |

### 建議 B 路徑

- 實作完整 SIGABA 機器（符合 2001 NSA 解密文件）
- 用 PRNG 生 rotor wirings（跟 T52e 一致做法）
- 提供四個 backend（Scalar / Parallel / SIMD / GPU）跑 12M Cipher rotor 搜尋
- **預期所有 backend 都失敗**（IC bestIC < 0.045 threshold）
- UI 上 cipher 欄選 SIGABA 時顯示警告提示：「這個 benchmark 展示為什麼 SIGABA 至今未被破解——即使用最快的 GPU 也無法在 IC 評分下找到正解。」
- 加進 `readme.html` 第七張 cipher card，標題就叫「**SIGABA — The Uncrackable**」

這比「再加一台可以破的 cipher」有意義得多——整個 EnigmaBenchmark 的主軸從此明確：**不是慶祝 GPU 算力，是對比 cipher 結構強弱**。

### 選 C 的話……

技術上最有趣，但實作 Stamp-Chan 攻擊需要研讀 paper 細節、調校統計參數、處理簡化變體 vs 完整版的差異展示。工時會膨脹。先走 B，之後有興趣再加 C。

## 8. 開發上的風險點

1. **三組 rotor 步進的正確性驗證**
   - 機器步進錯一位，encrypt/decrypt round-trip 會立刻暴露
   - 需要用 Stamp 模擬器的已知 plaintext/ciphertext 對比
   - 或自己寫 round-trip 測試（加密兩次回到原文）

2. **Index rotor 的靜態置換是否正確**
   - Index 在加密期間不步進，只是一層固定 lookup
   - 容易誤寫成「每鍵都 step」，結果不正確

3. **Control rotor 的 notch 規則**
   - 公開文件描述 fast rotor 每鍵一步，medium/slow 依各自 notch
   - 部分版本 SIGABA 的 Control notch 設計跟 Enigma 不同，要對文件

4. **Cipher rotor 是否「向後一步」**
   - 某些版本 SIGABA 的 cipher rotor 前進方向跟 Control 相反
   - 需要仔細核對實作目標的版本

5. **Scorer 設計**
   - 如果 IC threshold 設太低，noise 會觸發 false positive「found」
   - 建議沿用 Enigma M3 的 0.055 German threshold，讓 SIGABA 正常跑出 `found=False`

## 9. 參考資料連結

實作前應優先取得：

- **Savard & Pekelney 1999** Cryptologia — 完整機器重建
- **Mark Stamp SIGABA simulator** — 參考實作（Java）
- **NSA 2001 declassification set** — `sigaba-ecm-declassified-2001.pdf` 類似關鍵字可找到
- **Frode Weierud's CryptoCellar** — 密碼史愛好者收集的 SIGABA 照片 + 文件連結
- **Wikipedia 英文條目** — 結構描述品質不錯，可作起始點

---

## 結論

SIGABA 的**演算法結構**已夠公開到可以實作，**戰時 rotor 接線值**的欠缺不影響實作合法性（用重建版或 PRNG 生成的 wirings 都行）。加進 EnigmaBenchmark 最有意義的形式是 **B 方案**——讓所有 backend 跑完 12M keyspace 卻都 `found=False`，展示**結構安全**如何擊敗**算力窮舉**，給整個 benchmark 畫一個完美的句點。

未來要做的話優先讀 Savard-Pekelney 1999 + 跑 Stamp 模擬器 round-trip 驗證，再開始寫 C# 實作。

— 對話生成時間：2026-04-19
