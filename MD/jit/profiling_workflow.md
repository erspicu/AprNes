# AprNes 效能分析流程

## 概述

使用 PerfView (ETW) 收集 CPU sampling + JIT events，再用自製 EtlAnalyzer (.NET 10) 解析 ETL 產生文字報告。

## 工具位置

| 工具 | 路徑 |
|------|------|
| PerfView.exe | `temp/PerfView.exe` |
| bench_profile.bat | `temp/bench_profile.bat` — 跑 benchmark 的目標程式 |
| run_perfview.bat | `temp/run_perfview.bat` — PerfView 收集 ETW trace |
| EtlAnalyzer | `temp/EtlAnalyzer/` — .NET 10 console app，解析 ETL |
| 報告輸出 | `MD/jit/` — 帶時間戳的 md 報告 |

## 完整流程

### Step 1: 編譯目標版本

```bash
# Debug build
powershell -NoProfile -Command "& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' 'C:\ai_project\AprNes\AprNes\AprNes.csproj' /p:Configuration=Debug /p:Platform=x64 /nologo /v:minimal"
```

### Step 2: 修改 bench_profile.bat（如需變更目標）

`temp/bench_profile.bat` 內容：
```bat
@echo off
"C:\ai_project\AprNes\AprNes\bin\Debug\AprNes.exe" --rom "C:\ai_project\AprNes\AprNes\bin\Debug\tools\benchmark\ny2011.nes" --benchmark 30 --region NTSC --audio-mode 0
```

可調整：
- 改 `Debug` → `Release` 切換版本
- 改 `--benchmark 30` 調整收集時間
- 改 ROM 路徑測試不同遊戲

### Step 3: 收集 ETW Trace

```bash
cmd.exe //C "C:\\ai_project\\AprNes\\temp\\run_perfview.bat"
```

產生：`temp/aprnes_jit.etl`（~18MB，含 CPU sampling + CLR JIT/Inlining events）

PerfView 收集的 kernel + CLR events：
- `Profile` — CPU sampling（每 ~1ms 取樣一次）
- `Jit` — 方法 JIT 編譯事件
- `JitTracing` — Inlining 成功/失敗事件

### Step 4: 執行分析

```bash
dotnet run --project "C:/ai_project/AprNes/temp/EtlAnalyzer/EtlAnalyzer.csproj" -c Release
```

或指定參數：
```bash
dotnet run --project ... -c Release -- <etl路徑> <process名> <output路徑>
```

預設：
- ETL: `temp/aprnes_jit.etl`
- Process: `AprNes`
- Output: `temp/profile_report.txt`

### Step 5: 報告產出

分析器輸出包含：

1. **CPU Sampling — Exclusive**：各方法自身 CPU 時間佔比（self time）
2. **CPU Sampling — Inclusive**：各方法含 callees 的 CPU 時間佔比
3. **NesCore-only Exclusive**：僅模擬器核心方法，含 NesCore 總佔比
4. **JIT Compilation**：所有被 JIT 編譯的方法及 IL size
5. **Inlining**：成功/失敗 inline 的方法及原因
6. **Hot Path Inline Status**：交叉分析 — 熱點方法是否被 inline

### Step 6: 整理到 MD/jit/

```bash
# 取得時間戳
date +%Y%m%d_%H%M%S
# 複製並整理到 MD/jit/ 目錄，加上分析說明
```

## PerfView GUI 進階分析

如需更詳細的 call tree / flame graph：

```bash
temp/PerfView.exe temp/aprnes_jit.etl
```

操作：
1. 左側展開 `aprnes_jit.etl`
2. 雙擊 `CPU Stacks`
3. Process Filter 選 `AprNes`
4. **By Name** tab — 看各方法 inclusive/exclusive
5. **CallTree** tab — 看呼叫樹
6. **Flame Graph** tab — 視覺化熱點

## EtlAnalyzer 技術細節

- 框架：.NET 10 console app
- 相依：`Microsoft.Diagnostics.Tracing.TraceEvent` 3.2.2 NuGet
- Phase 1：用 `ETWTraceEventSource` 解析 raw JIT/Inlining events（快速）
- Phase 2：用 `TraceLog.CreateFromEventTraceLogFile()` 轉換為 ETLX，解析 CPU sampling stacks（需要 symbol resolution，較慢）
- Stack 解析：遍歷每個 `PerfInfoSample` event 的 `CallStack()`，累計 inclusive/exclusive samples

## 注意事項

- Debug build 的效能數據僅供相對比較，絕對 FPS 會比 Release 低很多
- CPU sampling 的精度取決於收集時間（30s 約得到 ~30K samples）
- Inlining 數據只反映 JIT 編譯期的決策，不代表執行期行為
- .NET Framework 4.8.1 不支援 TieredPGO，所有方法只 JIT 一次
