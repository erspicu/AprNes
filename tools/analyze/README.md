# AprNes 效能分析工具

這個目錄放 AprNes 的 ETW / PMU profiling 工具鏈，含：

- **JIT + CPU sampling 分析**（inline status、top exclusive/inclusive methods、opcode 熱度等）
- **Hardware PMU 分析**（L1 I-cache miss rate、TotalCycles per method）

---

## 目錄結構

```
tools/analyze/
├── README.md                 ← 本檔
├── bench_profile.bat         ← 啟動 benchmark 的目標腳本（兩個 run_*.bat 都會呼叫它）
├── run_perfview.bat          ← 收集 JIT + CPU sampling trace
├── run_perfview_pmu.bat      ← 收集 hardware PMU trace
├── EtlAnalyzer/              ← .NET 10 分析器（JIT / CPU sampling）
│   ├── EtlAnalyzer.csproj
│   └── Program.cs
└── PmuAnalyzer/              ← .NET 10 分析器（PMU counters）
    ├── PmuAnalyzer.csproj
    └── Program.cs
```

執行時輸出路徑：
- ETL trace 檔 → `temp/aprnes_jit.etl` / `temp/aprnes_pmu.etl`（~15-280 MB）
- 分析報告 → `temp/profile_report.txt` / `temp/pmu_report.txt`
- 最終報告 md → `MD/jit/YYYYMMDD_HHMMSS_profile_<label>.md`

---

## 前置準備

### 1. PerfView

從 https://github.com/microsoft/perfview/releases 下載 `PerfView.exe`（單一執行檔，~25 MB），放到：

```
C:\ai_project\AprNes\temp\PerfView.exe
```

（`temp/` 是 gitignored，不佔 repo 空間。若要共用，也可放到這個目錄下；編輯兩個 .bat 改路徑即可。）

首次啟動需 `/AcceptEULA` 接受授權（兩個 .bat 已自動帶）。

### 2. .NET 10 SDK

`EtlAnalyzer` / `PmuAnalyzer` 是 .NET 10 console app。`dotnet --version` 應回傳 10.x。

### 3. 以系統管理員身分執行

PerfView 的 kernel ETW session + PMU counter 需要 admin 權限。終端機要以系統管理員開啟，否則會靜默失敗（ETL 產生但內容空）。

### 4. 編譯 Debug 版 AprNes

```bash
powershell -NoProfile -Command "& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' 'C:\ai_project\AprNes\AprNes\AprNes.csproj' /p:Configuration=Debug /p:Platform=x64 /nologo /v:minimal"
```

benchmark 用的 ROM 預設在 `AprNes/bin/Debug/tools/benchmark/ny2011.nes`。若要換，編輯 `bench_profile.bat`。

---

## 流程 A：JIT + CPU Sampling 分析

### A1. 收集 trace

```bash
cmd //C "C:\\ai_project\\AprNes\\tools\\analyze\\run_perfview.bat"
```

PerfView 會啟動 `bench_profile.bat`（跑 30s benchmark），收集 CPU sampling（~1ms 間隔）+ CLR JIT/Inlining 事件，輸出到 `temp/aprnes_jit.etl`（~15-25 MB）。

### A2. 執行分析

```bash
dotnet run --project C:/ai_project/AprNes/tools/analyze/EtlAnalyzer -c Release
```

輸出：`temp/profile_report.txt`（文字報告）

可選參數：
```bash
dotnet run --project ... -- <etl路徑> <process名> <輸出路徑>
```

預設：
- ETL: `temp/aprnes_jit.etl`
- Process: `AprNes`
- Output: `temp/profile_report.txt`

### A3. 報告內容

分析器產出六個區塊：

1. **CPU Sampling — Exclusive**：各方法自身 CPU 時間（self time）% 與 samples 數
2. **CPU Sampling — Inclusive**：含 callees 的 CPU 時間
3. **NesCore-only Exclusive**：僅模擬器核心方法，含 NesCore 小計
4. **JIT Compilation**：所有 JIT 過的方法 + IL 大小
5. **Inlining**：成功 / 失敗的 inline 決策及原因
6. **Hot Path Inline Status**：top N 熱點方法 × inline 狀態交叉表

### A4. 歸檔報告

```bash
cp temp/profile_report.txt "MD/jit/$(date +%Y%m%d_%H%M%S)_profile_<label>.md"
```

或手動 `cp` + 在 md 頭加說明段落（FPS、branch、commit、config 等）。參考過去報告：`MD/jit/20260413_*.md`。

---

## 流程 B：Hardware PMU 分析（L1 I-cache miss 等）

### B1. 查可用的 counter（首次使用）

```bash
C:\ai_project\AprNes\temp\PerfView.exe ListCpuCounters /AcceptEULA /LogFile=temp/pv_counters.log
cat temp/pv_counters.log
```

每個 CPU 支援的 counter 不同。AMD Ryzen 7 3700X 實測有：
- `Timer` (0), `TotalIssues` (2), `BranchInstructions` (6)
- `DcacheMisses` (8), **`IcacheMisses` (9)**
- `CacheMisses` (10) — L2/L3
- `BranchMispredictions` (11)
- **`IcacheIssues` (20)** — L1 I-cache fetch (miss rate 分母)
- `TotalCycles` (19), `InstructionRetired` (25)
- AMD-specific: `ICFetch` (26), `ICMiss` (27)

Intel CPU 名稱可能不同。

### B2. PMU hardware slot 限制

AMD/Intel PMU 有 **4-6 programmable slot**。一次啟用太多會收到 COM exception `0x800705B6 大小引數不正確`。建議維持 ≤ 4 個 counter：

```
/CpuCounters:"Timer:10000,IcacheMisses:65536,IcacheIssues:65536,TotalCycles:65536"
```

格式：`Name:Period`。Period = 觸發抽樣的事件數間隔。
- `Timer:10000` = 每 ~10,000 計時器 tick 取樣（接近預設 1ms 間隔）
- `IcacheMisses:65536` = 每 65,536 次 cache miss 取樣

若某 counter 事件很稀少（如 LLC miss），可降 period（如 10000）增加取樣密度；反之 TotalCycles 可設高（如 1M）。

### B3. 修改 counter 清單

編輯 `run_perfview_pmu.bat` 第 10 行：

```bat
/CpuCounters:"Timer:10000,BranchMispredictions:65536,DcacheMisses:65536,TotalCycles:65536" ^
```

然後在 `PmuAnalyzer/Program.cs` 的 `counterNames` Dictionary 加對應 ID → 名稱（參考 ListCpuCounters 的 ID）。例：

```csharp
var counterNames = new Dictionary<int, string>
{
    { 0, "Timer" },
    { 8, "DcacheMisses" },       // 新增
    { 11, "BranchMispredictions" }, // 新增
    { 19, "TotalCycles" },
};
```

### B4. 收集 trace

```bash
cmd //C "C:\\ai_project\\AprNes\\tools\\analyze\\run_perfview_pmu.bat"
```

輸出：`temp/aprnes_pmu.etl`（較大，~280 MB，含上百萬 PMC 樣本）。

### B5. 執行分析

```bash
dotnet run --project C:/ai_project/AprNes/tools/analyze/PmuAnalyzer -c Release
```

輸出：`temp/pmu_report.txt`。

### B6. 報告內容

- **每個 counter 的 top 30 方法**（sample 數 + 百分比）
- **L1 I-cache miss rate 排行**（IcacheMisses / IcacheIssues per 方法，fetch >= 50 filter）
- **全域 miss rate**（misses / fetches 總合）

健康區間參考：
- **< 1%**：超好，working set 輕鬆塞進 L1
- **1-3%**：健康，L2 吸收小量 eviction
- **3-10%**：需注意
- **> 10%**：I-cache 不夠，優化方向

參考：`MD/jit/20260414_005000_pmu_icache_analysis.md`（首次 PMU 分析報告，Ryzen 7 3700X 實測 AprNes 全域 0.52%）。

---

## 常見故障排除

### 問題：Process X not found

ETL 已錄但 analyzer 說找不到 process。通常因 bench 跑完後 AprNes.exe 已退出，PerfView 來不及寫入 process name。

**解法**：加長 benchmark 時間（`bench_profile.bat` 改 `--benchmark 60`）或調大 PerfView `/MaxCollectSec=90`。

### 問題：symbols 都顯示 `[?!0x...]` / 都沒方法名

ETL 缺少 CLR JIT 事件 → JIT'd 方法位址無法解析。

**解法**：確認 `.bat` 有帶 `/clrEvents:Jit,JitTracing`。**不要**設 `/clrEvents:None`。

### 問題：COM exception 0x800705B6

PMU counter 太多。PMU 硬體只支援 4-6 個同時啟用。

**解法**：精簡 `/CpuCounters` 清單至 ≤ 4 個。

### 問題：PerfView 靜默失敗 / ETL 很小

沒有以 admin 權限執行。

**解法**：以系統管理員開啟 terminal 再執行 `.bat`。

### 問題：Stack trace 深度很淺 / top 方法都看不到

ETW stack capture 的預設深度夠用但某些情境（深遞迴）會被截斷。

**解法**：PerfView CLI 沒有 depth 選項，用 GUI 的 CPU Stacks view 查完整 stack。

---

## PerfView GUI 進階用法

若要看 flame graph / 詳細 call tree：

```bash
temp\PerfView.exe temp\aprnes_jit.etl
```

操作：
1. 左側樹展開 `aprnes_jit.etl`
2. 雙擊 `CPU Stacks`
3. Process Filter 選 `AprNes`
4. **By Name** tab — exclusive/inclusive per method
5. **CallTree** tab — callee 展開樹
6. **Flame Graph** tab — 視覺化

PMU ETL 同理，但 `Timer` / `IcacheMisses` 等 counter 會各自變成獨立的 CPU Stacks node。

---

## 既有報告

`MD/jit/` 目錄下依時間戳歸檔：

| 時間 | 標題 | 重點 |
|------|------|------|
| 20260413_184428 | Bresenham + mod-6 magic merge | 微優化組合 +0.42 FPS |
| 20260413_215013 | Static dispatch main loop | Stage 1A 64% FPS 基線 |
| 20260413_222721 | Direct-inline fix | 移除 FastNClocks wrapper +4% FPS |
| 20260413_225610 | Master vs feature 對比 | 同 session 驗證 +5.5% |
| 20260413_234837 | Feature warm state | 系統恢復後 62.59 FPS |
| 20260414_005000 | **PMU L1 I-cache 分析** | **全域 miss rate 0.52%，無 I-cache 壓力** |

---

## 3-次法 benchmark 協議

`MEMORY.md` 記載：
- 第 1 次：JIT 暖機，**不採計**
- sleep 60
- 第 2 次：有效量測
- sleep 60
- 第 3 次：有效量測
- 取第 2 + 3 次平均

原因：.NET TieredPGO 第 1 次以 Tier-0 跑並收集 PGO，第 2 次起才用 Tier-1 最佳化程式碼。

**實務上**：`bench_profile.bat` 跑 3 次背對背（或手動呼叫），報告標明三次數值與平均。若數字差距 > 5%，代表系統狀態不穩，等 1-2 分鐘再重跑。
