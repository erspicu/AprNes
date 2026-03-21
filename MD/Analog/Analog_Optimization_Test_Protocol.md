# 類比管線優化測試規則

**建立日期**: 2026-03-21
**適用範圍**: 依 `MD/Analog/Analog_Performance_Optimization.md` 逐項執行優化修改時的測試與驗收流程

---

## 一、優化來源

所有優化項目依照 `MD/Analog/Analog_Performance_Optimization.md` 的優先順序表執行，從 B5（最高優先）開始，逐項往下。

---

## 二、基準數據

以 `MD/Analog/Analog_Resolution_Benchmark_2026-03-21.md` 中 **4x (1024×840)** 的數據為比較基準：

| 指標 | 值 |
|------|----|
| 基準 FPS | **80.15** |
| AnalogSize | 4x (1024×840) |
| 測試日期 | 2026-03-21 |

---

## 三、測試指令

```bash
AprNes/bin/Debug/AprNes.exe --rom AprNes/etc/"Mega Man 5 (USA).nes" --benchmark 20 --ultra-analog --analog-output RF --analog-size 4 --crt --accuracy A
```

### 參數說明

| 參數 | 值 | 說明 |
|------|----|------|
| `--rom` | `AprNes/etc/Mega Man 5 (USA).nes` | 測試 ROM（Mapper 004, MMC3） |
| `--benchmark` | `20` | 無限速跑 20 秒，計算 FPS |
| `--ultra-analog` | — | 啟用 Level 3 完整物理路徑 |
| `--analog-output` | `RF` | RF 端子（最重管線） |
| `--analog-size` | `4` | 4x 解析度 (1024×840) |
| `--crt` | — | 啟用 Stage 2 CRT 電子束光學 |
| `--accuracy` | `A` | AccuracyOptA=ON |

benchmark 模式自動：無聲音、無 GUI 畫面、無 FPS 限制。

輸出格式：
```
BENCHMARK: 1603 frames in 20.00s = 80.15 FPS
```

---

## 四、測試協議（3 次法）

每個優化項目修改完成後，執行以下流程：

```
Run 1（JIT 暖機）→ 不採計 → sleep 30s
→ Run 2（有效）→ sleep 30s
→ Run 3（有效）→ 取 Run 2 + Run 3 平均值
```

### 原因

.NET TieredPGO 第 1 次以 Tier-0 跑並收集 PGO profile，第 2 次起才用 Tier-1 最佳化程式碼。第 1 次的數據不代表穩態效能。

### 自動化範例

```bash
# Run 1 (JIT warmup, discard)
EXE="AprNes/bin/Debug/AprNes.exe"
ROM='AprNes/etc/Mega Man 5 (USA).nes'
ARGS="--benchmark 20 --ultra-analog --analog-output RF --analog-size 4 --crt --accuracy A"

echo "=== Run 1 (JIT warmup) ==="
"$EXE" --rom "$ROM" $ARGS

sleep 30

echo "=== Run 2 ==="
OUT2=$("$EXE" --rom "$ROM" $ARGS 2>&1)
FPS2=$(echo "$OUT2" | grep -oP '[\d.]+(?= FPS)')
echo "$OUT2"

sleep 30

echo "=== Run 3 ==="
OUT3=$("$EXE" --rom "$ROM" $ARGS 2>&1)
FPS3=$(echo "$OUT3" | grep -oP '[\d.]+(?= FPS)')
echo "$OUT3"

AVG=$(echo "scale=2; ($FPS2 + $FPS3) / 2" | bc)
echo "=== Average: $AVG FPS ==="
```

---

## 五、驗收標準

### 採用門檻：超過基準 0.25%

```
改善幅度 = (新平均 FPS - 基準 FPS) / 基準 FPS × 100%
```

| 結果 | 動作 |
|------|------|
| 改善 ≥ 0.25% | **採用**：保留修改，更新基準 |
| 改善 < 0.25% | **退回**：`git checkout` 還原修改 |
| 效能下降 | **退回**：`git checkout` 還原修改 |

> 原門檻 1%，後調降為 0.25% 以捕捉小幅改善。

### 基準滾動更新

每次採用一項優化後，新的平均 FPS 成為下一項的比較基準。

---

## 六、結果記錄

每項優化測試完成後，將結果追加到 `MD/Analog/Analog_Optimization_Results.md`，格式如下：

```markdown
### [項目 ID] [項目名稱]

| 指標 | 值 |
|------|----|
| 前基準 FPS | xx.xx |
| Run 1 (JIT) | xx.xx |
| Run 2 | xx.xx |
| Run 3 | xx.xx |
| 平均 FPS | **xx.xx** |
| 改善幅度 | +x.xx% |
| 結果 | 採用 / 退回 |
| 新基準 FPS | xx.xx |
```

---

## 七、流程摘要

```
1. 選取下一個優化項目（依優先順序）
2. 實作修改
3. 編譯 (Release x64)
4. 執行 3 次法測試
5. 計算平均 FPS，與當前基準比較
6. 改善 ≥ 1% → 採用，更新基準；否則 → 退回
7. 記錄結果到 MD 檔
8. 重複直到所有項目完成
```
