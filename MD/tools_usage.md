# AprNes 測試與效能工具使用說明

## 概覽

AprNes 提供三類工具：

| 工具 | 用途 |
|------|------|
| `AprNes.exe` CLI 參數 | 執行單一 ROM 測試、截圖、取得測試結果 |
| `--benchmark` 系列參數 | 測量各版本執行效能（FPS） |
| `benchmark.ps1` | 一鍵執行四版本效能比較 |
| `run_tests.sh` | 執行 174 個 ROM 測試套件（需 bash/WSL） |

---

## 一、AprNes.exe — ROM 測試模式

用於驗證 NES 測試 ROM 的 Pass/Fail 結果（**僅 .NET Framework 版本支援**）。

### 基本語法

```
AprNes.exe --rom <file.nes> [選項...]
```

### 參數說明

| 參數 | 格式 | 說明 |
|------|------|------|
| `--rom` | `--rom <path>` | **必填**。NES ROM 路徑 |
| `--time` | `--time <秒>` | 執行幾秒後截圖並離開（不等待測試結果） |
| `--wait-result` | （旗標） | 等待 Blargg `$6000` 協定的測試結果（0=PASS，1+=FAIL） |
| `--max-wait` | `--max-wait <秒>` | 等待結果的最長秒數（預設 30） |
| `--soft-reset` | `--soft-reset <秒>` | 在指定秒數時發出軟重置（用於需要重置才能繼續的測試） |
| `--input` | `--input "A:1.0,B:2.0"` | 模擬手把輸入，格式見下 |
| `--screenshot` | `--screenshot <out.png>` | 儲存指定時間點的截圖 |
| `--log` | `--log <results.log>` | 將結果附加寫入 log 檔 |
| `--pass-on-stable` | （旗標） | 畫面穩定且無 "Failed" 文字 → 視為 PASS |
| `--expected-crc` | `--expected-crc "ABCD1234,EFGH5678"` | 畫面顯示的 CRC 符合任一值時視為 PASS |
| `--debug-log` | `--debug-log <path>` | 寫出 CPU debug trace 至檔案 |
| `--debug-max` | `--debug-max <n>` | debug trace 最大行數（預設 15000） |

### --input 格式

```
"按鍵:按下秒數[:持續秒數], ..."
```

- 按鍵名稱：`A` `B` `Select` `Start` `Up` `Down` `Left` `Right`（不分大小寫）
- 持續秒數省略時，預設約 166ms（10 frames）

```bash
# 範例：第 1 秒按 Start，第 3 秒按 A 持續 0.5 秒
AprNes.exe --rom test.nes --wait-result --input "Start:1.0,A:3.0:0.5"
```

### 結果判定流程

1. 偵測 Blargg `$6000` 協定（`$6001-$6003` = `DE B0 61`）  
2. 若有 → 等 `$6000 < $80`（0 = PASS，非 0 = FAIL 代碼）  
3. 若無 → 掃描 PPU nametable，找 `Passed` / `Failed` / `$01` / `0/` 等文字  
4. 若仍無 → 讀 `$F0`（舊 blargg 協定）  
5. 超時 → 結果 `0xFF`（unknown）

### 結果輸出格式

```
PASS | cpu_timing_test.nes | Passed
FAIL(2) | nes_instr_test.nes | Error 02
```

### 常用範例

```bash
# 基本測試（等待 $6000 協定）
AprNes.exe --rom nes-test-roms-master/checked/cpu_timing_test6/cpu_timing_test6.nes --wait-result

# 有時間限制的測試
AprNes.exe --rom blargg_test.nes --wait-result --max-wait 60

# 截圖（執行 5 秒後截圖，不等待結果）
AprNes.exe --rom mygame.nes --time 5 --screenshot out.png

# 需要重置的測試
AprNes.exe --rom reset_test.nes --wait-result --soft-reset 2.0

# CRC 比對測試
AprNes.exe --rom ppu_vbl_nmi.nes --wait-result --expected-crc "A1B2C3D4"
```

---

## 二、效能基準模式（--benchmark 系列）

三個版本的 exe 都支援 `--benchmark` 參數（headless 模式，不開視窗）。

### 2.1 標準 benchmark

所有版本通用：

```
<exe> --benchmark <rom路徑> [秒數] [輸出檔]
```

| 位置 | 說明 | 預設值 |
|------|------|--------|
| `<rom路徑>` | **必填**。ROM 檔路徑 | — |
| `[秒數]` | 測試持續時間 | `10` |
| `[輸出檔]` | 結果附加寫入的文字檔 | 不寫檔，僅輸出至 console |

```bash
# .NET Framework 10 秒測試
AprNes\bin\Release\AprNes.exe --benchmark "game.nes" 10

# .NET 8 測試，結果存入 result.txt
AprNesAOT\bin\Release\net8.0-windows\AprNesAOT.exe --benchmark "game.nes" 10 result.txt

# .NET 10 測試
AprNesAOT10\bin\Release\net10.0-windows\AprNesAOT10.exe --benchmark "game.nes" 10 result.txt
```

輸出格式：
```
JIT [.NET 10 RyuJIT] :    7640 frames      764.0 FPS
```

> **注意**：`AprNesAOT.exe` 的 `--benchmark` 還會同時跑一次 AOT DLL（`NesCoreNative.dll`）benchmark，輸出兩行結果：
> ```
> JIT [.NET 8 RyuJIT]     :    7018 frames      701.8 FPS
> AOT [NesCoreNative]     :    5500 frames      550.0 FPS
> ```

---

### 2.2 SIMD 比較模式（僅 AprNesAOT10）

**同一 process** 連續執行 SIMD ON → SIMD OFF（適合快速感受差異，但受 JIT warm-up 影響）：

```
AprNesAOT10.exe --benchmark-simd <rom路徑> [秒數] [輸出檔]
```

```bash
AprNesAOT10\bin\Release\net10.0-windows\AprNesAOT10.exe --benchmark-simd "spritecans.nes" 10
```

輸出格式：
```
[SIMD  ON ] running ...    5582 frames  558.2 FPS
[SIMD  OFF] running ...    6038 frames  603.8 FPS
[SIMD gain] -45.6 FPS  (-7.6%)
```

> ⚠️ **限制**：同一 process 中，第一段測試的 JIT PGO 狀態會影響第二段，數據不完全公平。

---

### 2.3 強制 SIMD OFF 模式（僅 AprNesAOT10）

**獨立 process** 測試，搭配另一次正常 `--benchmark` 組成公平比較：

```
AprNesAOT10.exe --benchmark-nosimd <rom路徑> [秒數] [輸出檔]
```

```powershell
# 公平 SIMD 對比（兩個獨立 process，對調順序取平均）
$exe = "AprNesAOT10\bin\Release\net10.0-windows\AprNesAOT10.exe"
$rom = "spritecans.nes"

# Round 1：SIMD ON 先跑
Start-Process -FilePath $exe -ArgumentList "--benchmark", $rom, 10, "on.txt"  -Wait -NoNewWindow
Start-Sleep -Seconds 5   # CPU 冷卻
Start-Process -FilePath $exe -ArgumentList "--benchmark-nosimd", $rom, 10, "off.txt" -Wait -NoNewWindow

# Round 2：SIMD OFF 先跑（對調順序消除偏差）
Start-Process -FilePath $exe -ArgumentList "--benchmark-nosimd", $rom, 10, "off2.txt" -Wait -NoNewWindow
Start-Sleep -Seconds 5
Start-Process -FilePath $exe -ArgumentList "--benchmark", $rom, 10, "on2.txt"  -Wait -NoNewWindow
```

---

## 三、benchmark.ps1 — 四版本一鍵比較

### 用法

```powershell
# 在 repo 根目錄執行
powershell -NoProfile -ExecutionPolicy Bypass -File benchmark.ps1
```

或直接在 PowerShell 中：
```powershell
.\benchmark.ps1
```

### 測試流程

```
[1/4] .NET Framework 4.6.1 JIT  (AprNes.exe)         ← 寫入 header + 第1行
[2/4] .NET 8 RyuJIT              (AprNesAOT.exe)       ← 附加第2行（JIT）
[3/4] Native AOT                 (AprNesAOT.exe)       ← 附加第3行（AOT DLL）
[4/4] .NET 10 RyuJIT             (AprNesAOT10.exe)     ← 附加第4行
```

### 設定（修改 benchmark.ps1 頂部變數）

| 變數 | 預設值 | 說明 |
|------|--------|------|
| `$rom` | `Controller Test (USA).nes` | 測試 ROM 路徑 |
| `$seconds` | `10` | 每項測試秒數 |
| `$output` | `benchmark.txt` | 結果輸出檔案 |

### 輸出格式（benchmark.txt）

```
=== AprNes Benchmark ===
ROM  : Controller Test (USA).nes
Time : 10 sec each
Date : 2026-03-03 15:00:00
OS   : Microsoft Windows NT 10.0.19045.0
CPU  : 13th Gen Intel(R) Core(TM) i7-1370P

JIT [.NET Framework 4.6.1 JIT] :    4220 frames      422.0 FPS
JIT [.NET 8 RyuJIT]            :    7018 frames      701.8 FPS
AOT [NesCoreNative]            :    5500 frames      550.0 FPS
JIT [.NET 10 RyuJIT]           :    7640 frames      764.0 FPS
```

### 自動 Build

exe 不存在時腳本會自動嘗試 build：
- `AprNes.exe` / `AprNesAOT.exe` → 呼叫 `build.ps1` 和 `buildAot.bat`
- `AprNesAOT10.exe` → 呼叫 `dotnet build AprNesAOT10\AprNesAOT10.csproj -c Release`

---

## 四、run_tests.sh — ROM 測試套件（bash/WSL）

### 用法

```bash
# 需在 repo 根目錄，EXE 需先 build（Debug 版）
bash run_tests.sh

# 產出報告版本
bash run_tests_report.sh
```

### 前置條件

```bash
# 先建置 .NET Framework Debug 版
powershell -NoProfile -File build.ps1
# 確認 EXE 存在
ls AprNes/bin/Debug/AprNes.exe
```

### 測試 ROM 路徑

所有測試 ROM 放在：
```
nes-test-roms-master/checked/<suite>/<rom.nes>
```

### 結果摘要

```
=== Starting test run ===
PASS: cpu_timing_test6/cpu_timing_test6.nes
FAIL(2): nes_instr_test/nes_instr_test.nes -- Error 02
...
=== Results: 170 passed, 4 failed / 174 total ===
```

---

## 五、ROM 選擇建議

| 用途 | 推薦 ROM |
|------|---------|
| 純 CPU 效能測試 | `nes-test-roms-master/Controller Test (USA)/Controller Test (USA).nes` |
| 最大 Sprite 負荷 | `nes-test-roms-master/spritecans-2011/spritecans.nes` |
| PPU 正確性 | `nes-test-roms-master/checked/ppu_vbl_nmi/` 系列 |
| CPU 指令正確性 | `nes-test-roms-master/checked/cpu_timing_test6/` |

---

## 六、常見問題

### Q：benchmark.ps1 執行後程式一閃而過？
`AprNesAOT.exe` 是 WinExe（GUI），PowerShell 的 `&` 運算子不會等待 GUI exe 結束。腳本已使用 `Start-Process -Wait -NoNewWindow` 解決此問題。若自行呼叫，也請使用相同方式。

### Q：兩次 SIMD 測試結果差異超大？
筆電 CPU 的 Turbo Boost 熱節流會造成 ±10% 波動，遠大於 Sprite Pass 3 的 SIMD 效益。建議：
1. 對調測試順序各跑一次取平均
2. 在 CPU 頻率固定的環境（停用 Turbo Boost）下測試

### Q：測試結果是 FAIL(255)？
結果 `0xFF` 代表超時且無法判定結果（找不到 `$6000` 協定、`Passed`/`Failed` 文字、`$F0` 等任何結果指標）。通常表示：
- ROM 需要更長的 `--max-wait`
- ROM 使用不支援的結果協定
- ROM 本身有 Mapper 問題（模擬不支援）

*文件更新：2026-03-03*
