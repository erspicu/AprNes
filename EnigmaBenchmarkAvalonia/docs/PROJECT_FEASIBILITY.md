# EnigmaBenchmark — 專案可行性與規劃（v2）

**狀態**：**可行性評估階段**，尚未實作
**日期**：2026-04-18（v2：修正 backend 選擇以對齊 AprNes）
**父專案**：AprNes（位於 `C:\ai_project\AprNes\`）

---

## 0. 核心目標澄清（重要）

**本 benchmark 不是「找最快破譯密碼方式」**，而是**把 AprNes Ava 的三個 CRT backend（Scalar / SIMD / SkSL-GPU）放到另一個 workload 上對比**，讓使用者：

1. **直接看到這三個 backend 在自己機器上的真實差距**（不只是 CRT 單一數據點）
2. **依據跨 workload 一致的結果決定 AprNes Ava 用哪個 CRT backend**
3. **順便學 Enigma**

因此 GPU backend **一定要用 SkSL（同 AprNes CRT）**，不用 ILGPU / CUDA / OpenCL — 否則結果跟 AprNes 無關，失去參考價值。

> SkSL 在純 integer compute 上不是最佳選擇（真 GPU compute 用 ILGPU 能快 10×），但這就是**重點** — 讓使用者看到 SkSL 在 compute workload 的真實能力，對照 CRT（shading workload）的表現，可以判斷自己的機器適不適合走 GPU。

---

## 1. 專案核心概念

一個結合 **密碼學 benchmark** + **互動視覺化** 的子專案，目的：

1. **量測 Scalar / SIMD / SkSL-GPU 在破解 Enigma 密文上的實際性能差距**（同 AprNes CRT 三 backend）
2. **讓 AprNes Ava 使用者依據本機跑分結果決定 CRT backend**（出廠校準工具）
3. **教育 + 娛樂**：使用者扮演德軍輸入密鑰 + 明文加密，另一邊「盟軍」視覺化破解過程（齒輪轉動）

### 概念圖

```
┌─────────────── 德軍端（用戶） ────────────┐
│  設定轉子 / plugboard / reflector          │
│  輸入明文 "HELLOFUEHRER..."              │
│  加密 → 得到密文 "XYZQWERTY..."            │
│  → 按送出                                  │
└────────────────┬─────────────────────────┘
                 │
                 ▼
┌─────────────── 盟軍端（程式）────────────┐
│  Scalar backend 開始跑   [圖示齒輪轉動]   │
│  SIMD backend 同步跑     [多齒輪並排]    │
│  GPU backend 同步跑      [大量平行齒輪] │
│                                           │
│  誰先找到 key → 顯示時間 / 嘗試數          │
│  最後產出 benchmark 表格 + 建議 backend    │
└───────────────────────────────────────────┘
```

---

## 2. 可行性評估（分項）

### 2.1 Enigma 模擬本體 — ★★★★★ 容易

- 3-rotor / 4-rotor Enigma 實作在任何語言都是**經典入門密碼學題**
- 純邏輯替換 + 轉子 stepping 機制 → ~300-500 行 C#
- 網路上有 **M3 / M4 / UKW-D 完整接線圖**（公開資料）
- 每秒純 scalar 可跑 **數百萬次加密**

### 2.2 破解演算法選擇 — ★★★★ 容易（需 scope 控制）

**完整 Enigma key space ~10¹⁷**（加 plugboard 後）。不可能暴力破。但可簡化：

| 層次 | key space | Scalar 時間 | 適合 benchmark？ |
|:----:|:---------:|:----------:|:--------------:|
| 只 rotor positions（固定 plugboard + 齒輪順序）| 26³ = 17,576 | < 1 ms | 太快 |
| rotor positions + wheel order | 60 × 17,576 ≈ 10⁶ | 1-2s | **✅ 最佳** |
| 加 ring setting | 10⁶ × 17,576 ≈ 10¹⁰ | 幾小時 | 不適合互動 |
| 完整 + plugboard | ~10¹⁷ | 數年 | 學術級 |

**建議 scope**：**wheel order + rotor positions**（~10⁶ combinations），每個 combination 跑一次完整解密 + 評分：

- **IC 分數**（Index of Coincidence）：德文明文 IC ≈ 0.0762，亂碼 ≈ 0.0385
- **字頻比對**：檢查 E/N/I/S/R 出現頻率接近德文分布
- **最佳解 = 分數最接近德文分布的設定**

**預估每 backend 時間**：
- Scalar: 1-3 秒
- SIMD: 200-500 ms（8× speedup via `Vector<T>`）
- GPU: 10-50 ms（~50× speedup via compute）

差距明顯 → **benchmark 價值清晰**。

### 2.3 GPU 實作路徑 — **SkSL Fragment-as-Compute**（確定）

依 §0 核心目標：**必須用 SkSL**，與 AprNes CRT 完全一致。

**做法**：GPGPU 的經典 fragment-shader trick：
```
SKSurface.Create(grContext, info(N×M)) ← 每個 pixel = 一組 key 嘗試
Shader main(fragCoord):
    1. 從 fragCoord 編碼出 wheel_order + rotor_positions
    2. 內跑 Enigma 解密（固定迭代數，例如 200 字元）
    3. 計算 IC 分數
    4. 編碼成 RGBA 輸出
Readback → CPU 掃描最高分 pixel → 對應的 key setting
```

**技術限制與對策**（我們在 CRT 都踩過）：

| 限制 | 對策 |
|------|------|
| SkSL 不允許 `int` uniform | 用 `float` 轉 int |
| 不允許 integer `%` | 用 `mod()` + floor |
| Array 要 fixed size | 密文 + rotor wiring 全用 `const arr[N]` |
| 無 loop 動態邊界 | 密文長度固定（ex. 200 char），unroll 友善 |
| 大 shader 可能超 instruction limit | 密文分塊（每 shader 處理 50 chars × 4 pass）|
| Readback 成本 | Benchmark 本就要 readback，可接受 |
| SkSL 整數 ALU 吞吐不如 float | 用 float 表示字母 0-25，矩陣化 rotor 查表 |

**預期效能（相對 Scalar）**：

| Backend | 相對速度 |
|---------|:-------:|
| Scalar | 1× |
| SIMD `Vector<T>` | 5-10× |
| **SkSL GPU** | **3-15×**（依 GPU + shader 複雜度）|

SkSL GPU 對 integer crypto 比真 compute shader 慢**很多**（ILGPU 可能 80×），但這是**預期且有意義的結果** — 告訴使用者 SkSL 在 compute-heavy workload 的真實上限。

### 2.4 視覺化（3D 齒輪動畫）— ★★ 最大風險點

**Avalonia 11.3 沒有內建 3D**。選項：

| 方案 | 擬真度 | 實作難度 | 整合成本 |
|:----:|:------:|:-------:|:-------:|
| **2D 齒輪**（SkiaSharp 畫圓 + 字母旋轉）| 基本 | 1-2 週 | 低（沿用現有 skia）|
| **2.5D 透視**（SkiaSharp perspective matrix）| 中等 | 2-3 週 | 中 |
| **完整 3D**（OpenTK.Avalonia + OpenGL/Silk.NET）| 高 | 3-5 週 | 高 |
| **WebView + Three.js**（HTML 頁嵌入 Avalonia）| 高 | 2 週 | 中（跨技術棧）|

#### 關於「真實齒輪運動」

真實 Enigma 齒輪運動規則：
- Rotor 1 每按鍵轉 1 step
- Rotor 1 到 notch 位置 → 下一次按鍵 Rotor 2 也跟著轉（類 odometer）
- **double-step anomaly**：Rotor 2 到 notch 時會再自轉一次

要呈現：**27 個齒輪狀態機**（其實是 3-4 個 rotor × 26 positions）+ **notch 連動**。

**2D 版本完全夠清楚**：三個字母輪、每按鍵轉一格、用顏色標記 notch 觸發 → 教學價值 100%，3D 只是 bonus。

### 2.5 互動與 UX — ★★★★ 易

- 德軍端：4 個下拉（轉子順序 + starting 位置）+ plugboard 連連看 + 明文 textbox + 加密按鈕
- 盟軍端：3 欄（Scalar / SIMD / GPU）各自跑自己的破解，齒輪即時旋轉，下方 progress bar
- 最終：表格 + 「建議 backend：GPU（快 50×）」

---

## 3. 與 AprNes Ava 的整合潛力

**有直接實用價值**：使用者跑完 EnigmaBenchmark → 得到「你這台電腦上 GPU 比 SIMD 快 N 倍」結論 → **寫入 AprNes.ini**，AprNes Ava 啟動時讀這個自動選 backend。

```ini
[Benchmark]
LastRunDate=2026-04-18
ScalarScore=1000         ; 相對分
SimdScore=3000
GpuScore=8000
RecommendedCrtBackend=Gpu ; 自動建議
```

這讓 EnigmaBenchmark 變成「出廠校準工具」而非純玩具。

---

## 4. 分階段規劃（建議）

### Phase A — Core Benchmark（純 CLI + 計時）
**時間**：1-2 週

- 3-rotor Enigma M3 完整模擬（加密 / 解密）
- 三個 backend 實作（**對齊 AprNes CRT**）：
  - Scalar C#
  - SIMD via `Vector<T>` / `Vector256<T>`
  - **SkSL Fragment-as-Compute via SkiaSharp**（同 AprNes）
- Brute force wheel order + rotor positions，IC 評分
- CLI 介面：`EnigmaBenchmark.exe --plaintext "..." --key ABC --show-progress`
- 輸出 console：三個 backend 各自 FPS / elapsed / keys/sec
- **交付物**：一個 exe，跑完印表格

**驗收**：三個 backend 都能得出相同解密結果；speedup 順序穩定；**結果與 AprNes CRT benchmark 有一致趨勢**（SkSL 相對 SIMD 的倍率應接近）。

### Phase B — Simple GUI（Avalonia 2D）
**時間**：1-2 週

- Avalonia 視窗
- 左半：德軍輸入面板（下拉 + textbox）
- 右半：三欄並排破解進度（2D 齒輪動畫、進度條、嘗試計數）
- SkiaSharp 畫齒輪（圓 + 字母 + 旋轉 matrix）
- 進度更新 throttled 60Hz
- 結束：顯示 benchmark 結果 + 建議

**交付物**：可玩的 GUI app，基本完整。

### Phase C — 3D 升級（stretch）
**時間**：3-5 週
**條件**：Phase B 完成且使用者願意投入

- 選 3D tech：OpenTK.Avalonia 或 Silk.NET
- 3D 齒輪模型（可從 Blender 做或程序生成）
- 光照 / 材質（金屬質感）
- 相機運動（可旋轉觀察）
- **風險**：整合複雜、Avalonia 控件嵌入 OpenGL 有坑

### Phase D — 與 AprNes Ava 整合
**時間**：0.5-1 天
**條件**：Phase A 完成即可

- EnigmaBenchmark 跑完寫入 `AprNes.ini`
- AprNes Ava 啟動時讀，自動挑 backend
- UI 提供「重新校準」按鈕呼叫 EnigmaBenchmark

---

## 5. 技術棧建議（最終）

| 層 | 技術 | 理由 |
|----|------|------|
| 專案類型 | .NET 10 console (Phase A) → Avalonia GUI (Phase B) | 與 AprNes Ava 一致 |
| Enigma 邏輯 | 純 C# + `Vector<T>` | 跨架構（x64 AVX2 / ARM NEON 自動）|
| **GPU compute** | **SkSL Fragment-as-Compute（同 AprNes CRT）** | **核心設計原則：同一 GPU 技術跨 workload 驗證** |
| GUI | Avalonia 11.3 + SkiaSharp | 沿用 AprNes 生態 |
| 2D 動畫 | SkiaSharp `SKCanvas` 2D + matrix | 現有技能 |
| 3D（若做）| OpenTK.Avalonia 或 Silk.NET | 主流選擇，文件多 |
| Benchmark 格式 | 直接存 `AprNes.ini` 或 JSON | 與 AprNes 整合 |

**零新依賴** — 完全用 AprNes 已有的套件（SkiaSharp 3.119.3-preview、Avalonia 11.3）。

---

## 6. 風險清單

| 風險 | 影響 | 緩解 |
|------|:---:|------|
| SkSL shader 超過 instruction limit（Enigma 解密 200 字元）| 高 | 分塊：一個 shader 跑 50 chars，四個 pass 串起來；或把密文當 texture 由 shader 讀 |
| SkSL 整數 compute 慢，SIMD 可能比它快 | 中 | **這就是 benchmark 要呈現的真實** — 讓使用者知道 SkSL 在非 shading workload 的極限 |
| 3D 齒輪整合 Avalonia 很麻煩 | 高 | **先做 2D，3D 視人力再說** |
| Brute force scope 太小分不出差距 | 低 | 增加 wheel order 或加 ring setting 級別 |
| 互動動畫 60Hz 拖累 cracker 執行 | 中 | 分離 render thread 與 cracker worker thread |
| SkSL readback 每個 batch 成本 | 中 | Batch 越大 overhead 分攤越好；調整 batch size 找 sweet spot |

---

## 7. 時間與工作量估計

| Phase | 描述 | 工作量 | 價值 |
|:-----:|------|:------:|:----:|
| A | Core benchmark + CLI | 1-2 週 | ★★★★★（核心功能）|
| B | Avalonia 2D GUI + 齒輪動畫 | 1-2 週 | ★★★★（完整體驗）|
| C | 3D 齒輪升級 | 3-5 週 | ★★（視覺 bonus）|
| D | AprNes Ava 整合 | 0.5 天 | ★★★（產品化） |

**MVP 建議 = Phase A + B + D**：3-4 週完成，有完整互動 + 實用整合；3D（Phase C）列為未來選配。

---

## 8. 可行性結論

### ✅ 值得做的部分（高可行性）

- **Phase A + B + D**：技術棧完全在 AprNes 現有生態內，**零新依賴**
- **SkSL 限制變成 benchmark 價值**：結果不是「GPU 多快」，而是「**SkSL 這條路徑多快**」— 這才是對 AprNes CRT backend 決策最有用的資訊
- 對 AprNes Ava 有實際產品價值（backend 自動校準）
- Scalar / SIMD / SkSL 三個實作直接對應 AprNes CRT 的三個 backend — **跨 workload 一致性驗證**

### ⚠️ 需再評估的部分（中風險）

- **Phase C（3D）**：技術可行但整合成本高，與本案核心目標（benchmark + 互動）邊際收益遞減
- **建議：Phase B 完成後若使用者仍有熱情再做 Phase C**，不要上來就挑戰

### ❌ 不建議做的部分

- 完整 Enigma + plugboard 破解（10¹⁷ keyspace）→ benchmark 時間太長，失去互動性
- Lorenz / Tunny 模擬 → 技術深度完全不同等級，更適合單獨專案

---

## 9. 下一步

1. 使用者決策：**三階段 A+B+D 是否接受？**
2. 若接受：
   - 開 `EnigmaBenchmark/EnigmaBenchmark.csproj`（console 專案）
   - Phase A 先做：Enigma M3 模擬 + Scalar cracker + IC scoring
   - 加 SIMD 版本
   - 加 GPU (ILGPU) 版本
   - 量測、調整
3. Phase B：建 `EnigmaBenchmark.Gui.csproj`（Avalonia）
4. Phase D：AprNes Ava 讀 benchmark ini

### 檔案結構草案

```
EnigmaBenchmark/
  PROJECT_FEASIBILITY.md          ← 本檔
  EnigmaBenchmark.sln
  EnigmaBenchmark/                ← Phase A: 核心 library + CLI
    Core/
      EnigmaMachine.cs
      Rotor.cs
      Reflector.cs
      IcScorer.cs
    Crackers/
      ICracker.cs
      ScalarCracker.cs
      SimdCracker.cs
      GpuCracker.cs
    Program.cs
    EnigmaBenchmark.csproj
  EnigmaBenchmark.Gui/             ← Phase B: Avalonia GUI
    MainWindow.axaml
    Views/
      GermanPanel.axaml
      AlliedPanel.axaml
      RotorVisualizer2D.cs
    Assets/
    EnigmaBenchmark.Gui.csproj
  docs/
    enigma_algorithm.md
    benchmark_protocol.md
```

---

## 10. 補充：為什麼 Enigma 是好的 benchmark 題材

- **大量獨立嘗試** = 完美平行（GPU 天生優勢）
- **計算密集 + 分支少** = SIMD 親和性高
- **明確解**（IC 達標就是破解）= 可比較「誰先找到」
- **歷史 + 戲劇性** = 比跑合成 flop 有趣
- **可調 scope** = 降 keyspace 給弱 GPU / 升 keyspace 給強 GPU
- **與 AprNes 的 CRT GPU 架構一致**（都是 parallel compute over fixed workload）

### 10.1 跟 AprNes CRT workload 的對照價值

| 項目 | CRT shader | Enigma shader |
|------|:---------:|:-------------:|
| 每 fragment 的運算類型 | float 取樣 + 少量算術 | int 查表 + 迭代 |
| Shader 複雜度 | 中（~100 行）| 高（~300 行 unrolled）|
| 是 GPU 的「舒適區」嗎 | **是**（float、紋理、per-pixel）| **否**（int、查表、迭代）|

**正因為兩個 workload 對 GPU 的「友善度」不同**，benchmark 跑出來的 SkSL/SIMD 相對倍率會不同：
- CRT：SkSL 跑得比 SIMD 好很多（GPU 舒適區）
- Enigma：SkSL 跟 SIMD 可能差不多，甚至 SIMD 勝

**這正是使用者想知道的**：自己機器跑 SkSL 在「不太理想的 workload」上能多快 — 若還能接近 SIMD，那 CRT（舒適區）肯定沒問題；若連 SIMD 都輸，那使用者該考慮用 SIMD 就好，省 GPU 啟動成本。

---

## 相關文件

- 父專案：`C:\ai_project\AprNes\README.md`
- AprNes CRT GPU 設計：`C:\ai_project\AprNes\MD\gpu\CRT_GPU_Design.md`
- 此文件：未來實作時的 roadmap，可直接對照進度
