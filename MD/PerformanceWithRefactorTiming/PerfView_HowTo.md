# PerfView 分析操作指南 — AprNes

## 工具位置
```
C:\ai_project\AprNes\temp\PerfView.exe
```

---

## 1. 收集 CPU Profile + JIT 資料

### Step 1: 準備 Benchmark bat
建立 `temp\bench_profile.bat`:
```batch
@echo off
"C:\ai_project\AprNes\AprNes\bin\Debug\AprNes.exe" --rom "C:\ai_project\AprNes\AprNes\bin\Debug\tools\benchmark\ny2011.nes" --benchmark 40 --region NTSC --audio-mode 0
```

### Step 2: 收集 (需要管理員權限)
建立 `temp\run_perfview.bat`:
```batch
@echo off
cd /d C:\ai_project\AprNes
C:\ai_project\AprNes\temp\PerfView.exe /nogui /accepteula /LogFile:C:\ai_project\AprNes\temp\pv_jit.log /dataFile:C:\ai_project\AprNes\temp\aprnes_jit.etl /merge:true /zip:false /kernelEvents:Profile /clrEvents:Jit,JitTracing run C:\ai_project\AprNes\temp\bench_profile.bat
```

**重要**: 
- 必須以**管理員權限**執行（ETW kernel events 需要 elevation）
- 從 Claude Code bash 執行時，直接呼叫 bat 檔即可（不要用 `cmd.exe /c`，會有參數解析問題）：
  ```bash
  /c/ai_project/AprNes/temp/run_perfview.bat
  ```
- PerfView 如果偵測到非 elevated，會自動嘗試 UAC relaunch
- 收集完成後會自動 merge，輸出 `temp\aprnes_jit.etl`

### 收集的 Events
| Flag | 收集內容 |
|------|---------|
| `/kernelEvents:Profile` | CPU sampling（每 ms 一次 IP 取樣） |
| `/clrEvents:Jit` | JIT 編譯事件（哪些方法被 JIT、IL size、native size） |
| `/clrEvents:JitTracing` | JIT inline 決策（成功/失敗原因） |

如果只需要 CPU profile（不需要 JIT 資訊），可以改用：
```
/kernelEvents:Profile /clrEvents:JITSymbols
```
（`JITSymbols` 只提供方法名稱解析，不含 inline 決策）

---

## 2. 分析 CPU Profile（GUI）

```bash
temp/PerfView.exe temp/aprnes_jit.etl
```

1. 在左側樹狀展開 → **CPU Stacks** → 雙擊開啟
2. 上方 **Process Filter** 選 `AprNes`
3. **GroupPats** 留空或設為 `[no grouping]` 看原始方法
4. 按 **Exc %** 排序 → 找到最耗 CPU 的方法
5. 雙擊某方法 → 看 caller/callee 關係

### 關鍵欄位
| 欄位 | 說明 |
|------|------|
| **Exc %** | 該方法自身消耗的 CPU 時間百分比（不含子呼叫） |
| **Inc %** | 該方法及其所有子呼叫消耗的 CPU 時間百分比 |
| **Exc** | Exclusive 取樣次數 |
| **Inc** | Inclusive 取樣次數 |
| **Fold** | 被折疊（inline）的次數 |

### 匯出 CPU Profile 為文字
在 CPU Stacks 視窗中：**File → Save View As Text** → 存為 `.txt`

---

## 3. 分析 JIT 編譯狀態（GUI）

1. 開啟 ETL 後，左側找 **JIT Stats** → 雙擊
2. Process Filter 選 `AprNes`
3. 可看到每個被 JIT 的方法：
   - **IL Size**: IL 位元組數
   - **Native Size**: 產生的機器碼大小
   - **JIT Time**: JIT 編譯耗時

---

## 4. 分析 JIT Inlining 決策（CLI — tracerpt 方式）

PerfView 的 JIT Stats GUI 可以看 inline 決策，但如果需要批量提取：

### Step 1: 轉換 ETL 為 CSV
```bash
tracerpt "C:\ai_project\AprNes\temp\aprnes_jit.etl" -o "C:\ai_project\AprNes\temp\jit_events.csv" -of csv -y
```

### Step 2: 搜尋 JIT 編譯事件
```bash
# 列出所有被 JIT 的 NesCore 方法
grep "LoadVerbose.*Jitted.*NesCore" temp/jit_events.csv | sed 's/.*"Jitted ", "AprNes.NesCore", //' | sort -u

# 列出所有 inline 失敗的熱區方法
grep "InliningFailed.*NesCore" temp/jit_events.csv | sed 's/.*"AprNes.NesCore", "//' | sort -u

# 列出 inline 成功的方法
grep "InliningSucceeded.*NesCore" temp/jit_events.csv | sed 's/.*"AprNes.NesCore", "//' | sort -u

# 搜尋特定方法的 JIT/inline 事件
grep -i "ppu_step_new\|PpuPhase4" temp/jit_events.csv | grep "InliningFailed\|InliningSucceeded\|JittingStarted\|LoadVerbose"
```

### Step 3: 常見 Inline 失敗原因

| 原因 | 說明 | 可修復性 |
|------|------|---------|
| `too many il bytes` | 方法 IL 太大（>.NET JIT 門檻約 ~100 bytes） | 可拆分方法 |
| `too many locals` | 本地變數 slot 過多 | 可減少變數或拆分 |
| `noinline per IL/cached result` | 標記了 `[NoInlining]` 或 JIT 快取了先前決策 | 預期行為 |
| `target not direct` | Interface/virtual call，JIT 不知道實際型別 | 需改架構（去除 interface） |
| `delegate invoke` | Delegate 呼叫無法 inline | 需改為直接呼叫 |
| `unprofitable inline` | JIT 判斷 inline 不划算（call site 不熱） | 通常不需修復 |
| `has exception handling` | 含 try/catch 的方法不能被 inline | 移除 exception handling |

---

## 5. 只收集 CPU Profile（快速模式）

如果只需要 CPU cost 分析，不需要 JIT 資訊：

```batch
@echo off
cd /d C:\ai_project\AprNes
temp\PerfView.exe /nogui /accepteula /LogFile:temp\pv_run.log /dataFile:temp\aprnes_perf.etl /merge:true /zip:false /kernelEvents:Profile /clrEvents:JITSymbols run temp\bench_profile.bat
```

這個比完整收集快很多（不需要 JIT tracing events）。

---

## 6. 常見問題

### Q: PerfView 需要管理員權限嗎？
A: 收集 kernel events（CPU sampling）需要。純 CLR user-mode events 理論上不需要，但 PerfView 通常還是會要求。

### Q: 收集過程中 PerfView overhead 對 benchmark 影響多大？
A: 約 2-5% FPS 降低。CPU sampling 的 overhead 很小（每 ms 一次 interrupt），JitTracing 稍多一些。

### Q: ETL 檔案很大怎麼辦？
A: 加 `/zip:true` 會壓縮，或縮短收集時間。40s benchmark 通常產生 20-30MB ETL。

### Q: 為什麼從 Claude Code bash 跑 PerfView 有問題？
A: Git bash 會把 `/nogui` 等 PerfView 參數解析為 Unix 路徑。解法：寫成 `.bat` 檔，用 bash 直接呼叫 bat：
```bash
/c/ai_project/AprNes/temp/run_perfview.bat
```
不要用 `cmd.exe /c "..."` 包裝（會有額外的參數解析問題）。
