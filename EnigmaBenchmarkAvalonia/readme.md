# EnigmaBenchmark

**WWII German Ciphers × Modern Compute**
**二戰德國頂級密碼系統 × 你書桌上這顆 GPU**

Six of WWII's most iconic cipher systems — each run against four parallel
compute backends (single-thread C#, multi-core parallel, AVX2 SIMD, SkSL GPU
shader) in one interactive Avalonia window.

六台二戰傳奇密碼機，每一台同時用四種運算路徑暴力破解（單執行緒 / 多核平行 / AVX2 SIMD
/ SkSL GPU shader），單一視窗裡一次看完。

---

## Full write-up / 完整文件

The complete story — cipher specifications, codebreaker biographies, benchmark
numbers, architectural notes — lives in the in-app documentation:

完整內容（機器規格 / 破解人物傳 / benchmark 數字 / 實作架構）在 app 內建文件：

- **[English Full README](docs/readme_en.html)** — 30+ biographies from
  Painvin (1918) through Beurling, Rejewski, Turing, Tutte, Flowers to Crum
- **[中文完整文件](docs/readme.html)** — 含 30+ 位破解者傳記，從 Painvin（1918）到
  Beurling、Rejewski、Turing、Tutte、Flowers、Crum

Click the **About ⓘ** button in the app's top-right corner to open them.
In-app 裡按右上角 **About ⓘ** 按鈕即可閱讀。

---

## English

### What it does

`EnigmaBenchmark` takes one brute-force cryptographic problem and runs it
through four very different hardware paths:

| Backend | What it is |
|---------|-----------|
| **Scalar** | one C# thread, vanilla loops |
| **Parallel** | `Parallel.ForEach` over all cores (16× typical) |
| **SIMD** | AVX2 `Vector256<int>` + `GatherVector256` |
| **SkSL GPU** | Skia runtime-effect shader on D3D11 / OpenGL |

The GPU path is the same shader pipeline used by
[AprNes](https://github.com/erspicu/AprNes)'s CRT filter — this benchmark exists
partly as a cross-domain stress test of that pipeline on a non-graphics
workload.

Typical results: what takes the Bletchley-era Bombe 15–20 minutes finishes in
**0.25 seconds** on a consumer GPU, and the single-thread scalar path takes
about 11 s — making the 44× GPU-vs-CPU delta physically legible.

### Ciphers implemented

| # | Cipher | Year | Who used it | Backends |
|---|--------|------|-------------|----------|
| 1 | **Zimmermann / Code 0075** | 1917 | Auswärtiges Amt → Mexico | Scalar (Room 40-style crib recovery) |
| 2 | **ADFGVX** | 1918 | Kaiserliches Heer | Scalar (brute-force K! column orders) |
| 3 | **Enigma M3** | 1930s–1945 | Wehrmacht / Heer / Luftwaffe | Scalar · Parallel · SIMD · GPU |
| 4 | **Enigma M4 "Shark"** | 1942 Feb | Kriegsmarine U-Boat | Scalar · Parallel · SIMD · GPU |
| 5 | **Lorenz SZ42 "Tunny"** | 1941 | OKW strategic comms | Scalar · Parallel · SIMD · GPU |
| 6 | **Siemens T52e "Sturgeon"** | 1943 | Luftwaffe strategic comms | Scalar · Parallel · SIMD · GPU |

### Quick start

```bash
dotnet build -c Release
bin/Release/net10.0/EnigmaBenchmarkAvalonia.exe
```

In the window:
1. Pick a cipher (six options — WWI at top, WWII underneath)
2. Pick a scope (Quick / Normal / Hard / Extreme — applies to Enigma variants)
3. Check which backends to run
4. Press **Start Benchmark**

GPU finishes first (~0.3 s on M3); CPU backends follow in SIMD → Parallel →
Scalar order.

---

## 中文

### 這是什麼

`EnigmaBenchmark` 用四條完全不同的硬體路線跑同一個暴力破解問題：

| Backend | 什麼東西 |
|---------|---------|
| **Scalar** | 單一 C# 執行緒、普通迴圈 |
| **Parallel** | `Parallel.ForEach` 全 CPU 多核（16× 左右） |
| **SIMD** | AVX2 `Vector256<int>` + `GatherVector256` |
| **SkSL GPU** | Skia runtime-effect shader 跑在 D3D11 / OpenGL |

GPU 那條路跟 [AprNes](https://github.com/erspicu/AprNes) NES 模擬器的 CRT 濾鏡用
同一套 shader pipeline——這專案一部分就是那條 pipeline 在非圖形工作負載下的跨域壓測。

典型結果：1940 年代 Bletchley 的 Bombe 要 15–20 分鐘才能解開的一把 Enigma，
現在一張消費級 GPU **0.25 秒**搞定；同問題單核 C# 要跑 11 秒——44× 的差異讓
「為什麼要用 GPU」變成物理層面可見的事實。

### 已實作的密碼

| # | 密碼 | 年代 | 用的人 | Backend |
|---|------|------|--------|---------|
| 1 | **Zimmermann / Code 0075** | 1917 | 德外交部 → 墨西哥 | Scalar（Room 40 式 crib attack） |
| 2 | **ADFGVX** | 1918 | 德陸軍（Kaiserliches Heer） | Scalar（K! 欄序暴力搜） |
| 3 | **Enigma M3** | 1930s–1945 | 國防軍、陸軍、空軍 | Scalar · Parallel · SIMD · GPU |
| 4 | **Enigma M4「Shark」** | 1942 Feb | 海軍 U-Boat | Scalar · Parallel · SIMD · GPU |
| 5 | **Lorenz SZ42「Tunny」** | 1941 | 國防軍最高統帥部（OKW） | Scalar · Parallel · SIMD · GPU |
| 6 | **Siemens T52e「Sturgeon」** | 1943 | 空軍戰略通訊 | Scalar · Parallel · SIMD · GPU |

### 快速上手

```bash
dotnet build -c Release
bin/Release/net10.0/EnigmaBenchmarkAvalonia.exe
```

視窗開啟後：
1. 從下拉選單選一個密碼（六選一——一戰兩個在上、二戰四個在下）
2. 選 Scope（Quick / Normal / Hard / Extreme，僅適用 Enigma 系列）
3. 勾選要跑的 backend
4. 按 **Start Benchmark**

GPU 最先跑完（M3 約 0.3 秒），CPU 按 SIMD → Parallel → Scalar 順序跟上。

---

## Repo layout / 專案結構

```
EnigmaBenchmarkAvalonia/
├── Core/                  # Cipher machines (pure logic, no UI)
│   ├── EnigmaM3.cs        · EnigmaM4.cs · LorenzSZ40.cs · T52eMachine.cs
│   ├── AdfgvxMachine.cs   · ZimmermannCodebook.cs
│   ├── RotorData.cs       · Baudot.cs · IcScorer.cs · T52eSelfTest.cs
├── Crackers/              # Per-cipher × per-backend cracker implementations
│   ├── ScalarCracker*     · ParallelScalarCracker*
│   ├── SimdCracker*       · GpuCracker*
│   └── ICracker*          # Interfaces
├── Shaders/               # SkSL runtime-effect shaders (one per GPU cipher)
│   └── *.sksl
├── Presets/
│   └── DefaultScenario.cs # Plaintext + ground-truth keys per cipher
├── docs/                  # Rich in-app docs
│   ├── readme.html        · readme_en.html  (bilingual full write-up)
│   └── research-t52e/     (Davies 1982 T52e reverse-engineering research)
├── MainWindow.axaml(.cs)  # Avalonia UI
├── BenchmarkControl.cs    # GPU-render-thread plumbing
├── CipherRevealPanel.cs   # Cipher preview widget
└── Program.cs             # Entry point (also handles --t52e-test self-test)
```

## Acknowledgments / 致謝

Built on top of the cryptanalytic work of Marian Rejewski, Alan Turing,
Gordon Welchman, Bill Tutte, Tommy Flowers, Arne Beurling, Georges Painvin,
Room 40 (de Grey & Montgomery), Michael Crum, and the ~7,500 women of
Bletchley Park — most of whom took the secret to their graves.

建立在 Rejewski、Turing、Welchman、Tutte、Flowers、Beurling、Painvin、
Room 40（de Grey 與 Montgomery）、Crum 以及 Bletchley Park 約 7,500 位女性
員工的破譯工作之上——她們之中多數人把秘密帶進了墳墓。

See `docs/readme_en.html` (or `readme.html`) for the full biographies.
完整傳記請看 `docs/readme_en.html`（或 `readme.html`）。

## Parent project / 母專案

Companion benchmark to the [AprNes](https://github.com/erspicu/AprNes)
NES emulator — demonstrates that its CRT shader pipeline generalises well
to non-graphics compute workloads.

[AprNes](https://github.com/erspicu/AprNes) NES 模擬器的姊妹 benchmark——
展示其 CRT shader pipeline 在非圖形工作負載下的通用性。

## License

Source code: same license as the parent AprNes repository.
Historical scenarios and fictional plaintexts are original to this project
and released under CC-BY 4.0.
