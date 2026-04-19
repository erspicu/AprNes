# .NET JIT 分析流程（AprNes / Avalonia 專案）

針對 C# hot path（CRT shader、SIMD cracker、NES 核心）JIT 產出分析與效能調校 SOP。

---

## 0. 核心前提

**.NET TieredPGO 的 warmup 特性**（這是全專案最容易踩雷的地方）：

1. **Tier-0 JIT**：方法首次呼叫用的低品質 JIT，同時收集 PGO profile
2. **Tier-1 JIT**：熱點方法收到 PGO 資料後重編，啟用 inline / devirtualize / 各種優化
3. Tier-0 → Tier-1 的切換通常要**數百到數千次執行**，不是即時的

**實測意義**：Benchmark 第 1 次跑是 Tier-0（慢），第 2 次才 Tier-1（真實速度）。

所以 `feedback_gemini_sequential.md` 記錄的 **3 次 benchmark 協議** 是硬規則：

```
第 1 次：JIT warmup（不採計）
sleep 60
第 2 次：有效數據
sleep 60
第 3 次：有效數據
最終數字 = 第 2、3 次平均
```

sleep 60 是為了讓 thermal throttling 不影響；不是為 JIT。

---

## 1. 啟用 TieredPGO + ReadyToRun

`.csproj` 已設定：

```xml
<TieredCompilation>true</TieredCompilation>
<TieredPGO>true</TieredPGO>
```

`EnigmaBenchmarkAvalonia.csproj`、`AprNesAvalonia.csproj` 都開了。

**對 Release benchmark 才有效**，Debug build 不走 Tiered JIT。

---

## 2. 看 JIT 產出的 assembly

### 方法 A：環境變數 dump disasm

```bash
# PowerShell
$env:DOTNET_JitDisasm = "EnigmaBenchmark.Crackers.SimdCrackerT52e:DecryptSimd4"
$env:DOTNET_JitDisasmAssemblies = "EnigmaBenchmarkAvalonia"
.\bin\Release\net10.0\EnigmaBenchmarkAvalonia.exe

# bash
DOTNET_JitDisasm="EnigmaBenchmark.Crackers.SimdCrackerT52e:DecryptSimd4" \
  ./bin/Release/net10.0/EnigmaBenchmarkAvalonia.exe
```

可用萬用字元：
- `SimdCracker*` — 所有 SimdCracker 開頭的 class
- `*:Crack` — 所有叫 Crack 的方法
- `EnigmaBenchmark.Crackers.*:*` — 整個 namespace

輸出到 stdout，太多的話導向檔案：
```bash
DOTNET_JitDisasm="..." .\bin\...\.exe > disasm.txt 2>&1
```

### 方法 B：看 Tier 狀態

```bash
$env:DOTNET_JitDisasmSummary = "1"
# 執行程式，會列出所有方法哪時 JIT、哪個 tier
```

### 方法 C：強制 Tier-1（跳過 warmup）

對 benchmark 測量友善：

```bash
$env:DOTNET_TieredCompilation = "1"
$env:DOTNET_TC_QuickJit = "0"   # 關閉 Tier-0
# 會慢啟動（全部直接 Tier-1）但數據一致
```

---

## 3. 確認 SIMD 實際用到了什麼指令

SIMD code 寫了 `Vector128.Xor` 或 `Avx2.GatherVector256`，想確認 JIT 真的產對應硬體指令：

```bash
$env:DOTNET_JitDisasm = "SimdCrackerT52e:DecryptSimd4"
$env:DOTNET_JitDisasmAssemblies = "EnigmaBenchmarkAvalonia"
# 然後在 disasm 裡找：
#   x86-64: vpxor, vpaddd, vpcmpeqd, vpgatherdd
#   ARM64:  eor.16b, add.4s, cmeq.4s
```

**若 disasm 看到 scalar 指令（xor reg, mov reg, ...）混在 vector 路徑裡，表示 JIT 沒吃到 SIMD**，檢查：

1. `SimdCaps.HasAvx2` / `SimdCaps.HasNeon` 回傳值
2. Vector128/256 變數是否被強制 spill 到記憶體（register pressure）
3. `stackalloc` 在 loop 裡會 accumulate 爆 stack，要 hoist 出來

---

## 4. 效能 profiling（不看 assembly）

### 方法 A：Stopwatch 手動計時

最簡單，適合熱路徑局部測量。現有 crackers 都這樣做：

```csharp
var sw = Stopwatch.StartNew();
// work
sw.Stop();
Console.WriteLine($"{sw.Elapsed.TotalMilliseconds:F2} ms");
```

### 方法 B：dotnet-trace（全域 flamegraph）

```bash
dotnet tool install -g dotnet-trace

# 啟動程式
.\bin\Release\net10.0\EnigmaBenchmarkAvalonia.exe &

# 抓 30 秒的 sample profile
dotnet-trace collect -p <PID> --providers Microsoft-DotNETCore-SampleProfiler --duration 00:00:30
```

產 `.nettrace` 檔，可用 [PerfView](https://github.com/microsoft/perfview) 或 [SpeedScope](https://www.speedscope.app/) 看 flamegraph。

### 方法 C：EventPipe + PerfView

看 GC、JIT 次數、tier transitions：

```bash
dotnet-trace collect -p <PID> \
  --providers Microsoft-Windows-DotNETRuntime:0x14C14FCCBD:4
```

---

## 5. 常見 JIT 陷阱（本專案遇過的）

| 現象 | 原因 | 解法 |
|------|------|------|
| **Tier-0 數字當有效 benchmark 用** | 第 1 跑沒丟 | 3 次協議：第 1 次不採計 |
| **Vector256 `stackalloc` 在 loop 內** | stack accumulate，爆掉 | hoist stackalloc 到方法 entry（CA2014 警告） |
| **SIMD path 不觸發** | IsSupported 判斷位置錯 | 把 gate 放在 Crack() 進入點，不要放 hot loop 內 |
| **JIT 沒 inline 熱點 helper** | 方法太大或有 try/catch | 拆小方法、標 `[MethodImpl(MethodImplOptions.AggressiveInlining)]` |
| **Array bound check 在 hot loop** | `arr[i]` 檢查索引範圍 | 用 `Span<T>` + fixed，或用 `Unsafe.Add(ref ...)` |
| **GC pressure 從 alloc in loop** | `new int[n]` 每 iter 配置 | hoist allocation 到迴圈外，重複使用 buffer |

---

## 6. SIMD runtime dispatch 規格

本專案用一個 `SimdCaps` helper（`EnigmaBenchmarkAvalonia/Core/SimdCaps.cs`）：

```csharp
public static bool HasAvx2  => Avx2.IsSupported;
public static bool HasNeon  => AdvSimd.IsSupported;
public static bool HasAnyVector => HasAvx2 || HasNeon;

public static string HardwareDesc
    => $"{RuntimeInformation.ProcessArchitecture} / {ActivePath}";
```

每個 SIMD cracker 的 Crack() 第一行：

```csharp
if (!SimdCaps.HasAnyVector)
    return new ParallelScalarXxx().Crack(...);

// 選平台特定路徑
bool useAvx2 = SimdCaps.HasAvx2;
Parallel.ForEach(units, ...,
    (unit, _, local) => {
        if (useAvx2) RunUnit(...);       // Vector256 / AVX2 path
        else         RunUnitNeon(...);   // Vector128 / NEON path
    });
```

JIT 會把 `useAvx2` branch 在 hot loop 裡做 branch prediction — 實測無可觀測 overhead（因為 thread pool worker 起 life 就分好）。

---

## 7. Benchmark 結果對照表（2026-04-19 實測）

T52e 24M keyspace、Release build、16-core Zen CPU、AVX2 on：

| Backend | Time | K keys/s | 備註 |
|---------|------|----------|------|
| Scalar | 560s | 43 | 單執行緒 |
| Parallel (scalar) | 57s | 420 | 16 核 × scalar |
| SIMD (LUT + Parallel) | 43s | 556 | 舊版 |
| SIMD (bit-sliced Vector128 + LUT + Parallel) | **待測** | **預期 ~1000 Kkeys/s** | 新版 — 4-lane × 16 cores |
| GPU (SkSL) | ~1s | ~25,000 | D3D11 |

有跑新 bit-sliced SIMD 的實測結果後更新這段。

---

## 8. 避免 JIT 重編譯影響 benchmark

程式碼改 → publish 後測：

```bash
# 一定要砍舊 bin、重新 publish，避免 obsolete 機器碼 cached
rm -rf EnigmaBenchmarkAvalonia/bin/Release
dotnet publish ... -c Release ...
```

若 benchmark 會跑前 warmup 函式（現有 RunBenchmarkXxx 就是），確保 warmup 路徑**跟正式測量同一個方法**，否則 Tier-1 JIT 不會覆蓋到。

現有 warmup 作法：

```csharp
await cracker.Crack(ct, fixedParts, scope, 3);   // 3 秒 timeout warmup
await cracker.Crack(ct, fixedParts, scope, 90);  // measured
```

兩次 Crack 用同一個 instance、同一個 method — Tier-1 JIT 在 warmup 結束後理論上已觸發。

---

## 9. 想深入 JIT 時的資源

- [.NET JIT design docs](https://github.com/dotnet/runtime/tree/main/docs/design/coreclr/jit)
- [Vector128/256 intrinsic API ref](https://learn.microsoft.com/dotnet/api/system.runtime.intrinsics)
- [BenchmarkDotNet](https://benchmarkdotnet.org/) — 框架會自動處理 warmup + statistical rigour，但本專案目前用手工 Stopwatch
- [PerfView 教學](https://github.com/microsoft/perfview/blob/main/documentation/PerfViewGettingStarted.md)

---

## 修正舊紀錄

- `feedback_dsp_benchmark.md`（AprNes 主線）描述 JIT warmup 觀察
- `feedback_gemini_sequential.md` 描述三次協議起源
- 這份文件是 EnigmaBenchmark 專用的補充，主專案 AprNes CRT 也可參考方法論
