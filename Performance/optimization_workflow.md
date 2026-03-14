# AprNes 效能最佳化工作流程

## 原則

每次只處理一個 TODO 項目，完整走完流程後才進行下一個。
不允許跳過任何步驟，特別是效能測試和正確性驗證。

---

## 流程圖

```
選取一個 TODO 項目（Status: 🔲 TODO）
        ↓
實作程式碼修改
        ↓
執行效能測試（--perf, 20s）
        ↓
    有改善？
   ↙        ↘
  YES         NO
   ↓           ↓
正確性驗證   標記 FAILED
blargg +AC   分析原因
   ↓           ↓
全數通過？   revert 修改
  ↙  ↘         ↓
YES   NO    commit 文件
 ↓     ↓
保留  revert
結果  修改
 ↓     ↓
更新  標記
TODO  FAILED
 ↓
commit & push
```

---

## 詳細步驟

### Step 1 — 選取項目

從 `performance_optimization_todo.md` 選一個 `Status: 🔲 TODO` 的項目。
記錄目前基線 FPS（最新 `_perf_vN.md` 的 Average FPS）。

---

### Step 2 — 實作修改

按照 TODO 項目的 Method 說明進行程式碼修改。
修改範圍盡量最小化，不順手改其他東西。

---

### Step 3 — 效能測試

**編譯：**
```powershell
MSBuild AprNes\AprNes.csproj /p:Configuration=Release /p:Platform=x64
```
編譯必須 **0 errors**，才繼續。

**執行 benchmark（必須先等 CPU 降溫 60 秒）：**
```bash
sleep 60 && AprNes/bin/Release/AprNes.exe --perf "Performance/Mega Man 5 (USA).nes" 20 "描述"
```
結果自動存入 `Performance/{date}_perf_vN.md`。

> ⚠️ **CPU 熱降頻注意**：連續多次 20s benchmark 後 CPU 溫度升高，FPS 會虛假偏低超過 10%。每次測試前必須 `sleep 60` 確保 CPU 回到正常頻率。

**判斷標準：**

| 結果 | 行動 |
|------|------|
| 改善 > 0.25% | ✅ 繼續 Step 4 |
| 改善 ≤ 0.25% 或持平 | ❌ 跳至 Step 5（負優化） |
| 效能下降 | ❌ 跳至 Step 5（負優化） |

---

### Step 4 — 正確性驗證

**blargg 174 測試：**
```
python run_tests.py -j 10
```
期望：`174 PASS / 0 FAIL`

**AccuracyCoin 136 測試：**
```
bash run_tests_AccuracyCoin_report.sh --no-build --no-screenshots
```
期望：`136/136 PASS, 0 FAIL`

**兩者都通過 → Step 6（保留）**
**任一有 FAIL → Step 5（revert）**

---

### Step 5 — 失敗處理（負優化 或 正確性失敗）

1. **revert 程式碼修改**（恢復到修改前的狀態）
2. **更新 TODO** 狀態：
   - 負優化：`❌ FAILED — 實測負效益`
   - 正確性失敗：`❌ FAILED — 造成邏輯錯誤`
3. 在 TODO 加上**失敗原因分析**（FPS 數字、失敗的測試名稱、推測原因）
4. 加入 **Failed / Ineffective Attempts** 區段
5. `git add` 文件（不含程式碼，程式碼已 revert）
6. `git commit`（僅含 TODO 更新，訊息說明失敗）
7. `git push`

---

### Step 6 — 成功處理（效能改善 + 測試全過）

1. **更新 TODO** 狀態：
   ```
   ✅ DONE — +X.X% (before FPS → after FPS)；blargg 174/174 + AC 136/136 驗證通過
   ```
2. **在 Results Log 加一筆紀錄**（編號、描述、Before/After FPS、Delta、Report 連結）
3. `git add` 所有修改（程式碼 + perf MD + TODO）
4. `git commit`（訊息格式：`Priority N: 描述 (+X.X%)`）
5. `git push`

---

## Commit 訊息格式

**成功：**
```
Priority N: 描述 (+X.X%)

簡短說明修改內容與機制。
累計基線: XXX → YYY FPS (+ZZ.Z% from baseline)。
blargg 174/174 + AccuracyCoin 136/136 verified.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
```

**失敗（僅文件）：**
```
docs: record Priority N as FAILED (負優化/邏輯錯誤)

原因簡述。

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
```

---

## 快速參考

| 指令 | 說明 |
|------|------|
| `AprNes.exe --perf "Performance\Mega Man 5 (USA).nes" 20 "說明"` | 效能測試（自動存檔） |
| `python run_tests.py -j 10` | blargg 174 測試 |
| `bash run_tests_AccuracyCoin_report.sh --no-build --no-screenshots` | AC 136 測試 |

## 目前累計效能

| 基線（Debug） | 最終 Debug | Release 基線 | 目前（Release） | .NET 10 |
|---|---|---|---|---|
| 181.70 FPS | 247.95 FPS (+36.5%) | 241.45 FPS | ~259 FPS | ~348 FPS |

> **注意**：2026-03-15 起改用 Release 組態測試。Release 基線 241.45 FPS 對應 Debug 247.95 FPS（同一份程式碼）。catchUpPPU/APU loop unroll (+4.3%) + Sprite 0 hit range check (+2.8%) 為 Release 組態下新增的兩項改善。
