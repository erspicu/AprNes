#!/bin/bash
# Analog Mode Multi-Resolution Benchmark
# Tests AnalogSize 2x/4x/6x/8x with 3-run protocol (discard JIT run, average run 2+3)
# Usage: bash bench_analog_resolutions.sh [--no-build]

set -e

EXE="C:/ai_project/AprNes/AprNes/bin/Release/AprNes.exe"
ROM="C:/ai_project/AprNes/etc/Mega Man 5 (USA).nes"
DURATION=20
JIT_DURATION=10
COOLDOWN=30
TOTAL_RUNS=$((4 * 3))  # 4 resolutions × 3 runs each
RUN_NUM=0
DATE=$(date +%Y-%m-%d)
OUTFILE="C:/ai_project/AprNes/MD/Analog/Analog_Resolution_Benchmark_${DATE}.md"

# Build unless --no-build
if [[ "$1" != "--no-build" ]]; then
    echo "=== Building Release... ==="
    powershell -NoProfile -Command "& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' 'C:\ai_project\AprNes\AprNes\AprNes.csproj' /p:Configuration=Release /p:Platform=x64 /nologo /v:minimal" 2>&1
    echo ""
fi

# Extract FPS from benchmark output line: "BENCHMARK: NNNN frames in XX.XXs = YYY.YY FPS"
extract_fps() {
    echo "$1" | sed -n 's/.*= \([0-9.]*\) FPS.*/\1/p' | head -1
}

# Arrays for results (indexed 0-3 for sizes 2,4,6,8)
R1=()
R2=()
R3=()
AVG=()

IDX=0
for SZ in 2 4 6 8; do
    W=$((256 * SZ))
    H=$((210 * SZ))
    echo "============================================================"
    echo "  AnalogSize=${SZ}x  (${W}x${H})"
    echo "============================================================"

    # Run 1: JIT warmup (discard, no cooldown after)
    RUN_NUM=$((RUN_NUM + 1))
    echo "--- [${RUN_NUM}/${TOTAL_RUNS}] Run 1 (JIT warmup ${JIT_DURATION}s, discard) ---"
    OUT1=$("$EXE" --rom "$ROM" --benchmark $JIT_DURATION --ultra-analog --analog-output RF --analog-size $SZ --crt --accuracy A 2>&1)
    FPS1=$(extract_fps "$OUT1")
    echo "  Run 1: ${FPS1} FPS (discarded)"
    R1[$IDX]="$FPS1"

    # Run 2: effective
    RUN_NUM=$((RUN_NUM + 1))
    echo "--- [${RUN_NUM}/${TOTAL_RUNS}] Run 2 (effective ${DURATION}s) ---"
    OUT2=$("$EXE" --rom "$ROM" --benchmark $DURATION --ultra-analog --analog-output RF --analog-size $SZ --crt --accuracy A 2>&1)
    FPS2=$(extract_fps "$OUT2")
    echo "  Run 2: ${FPS2} FPS"
    R2[$IDX]="$FPS2"

    echo "--- Cooling ${COOLDOWN}s ---"
    sleep $COOLDOWN

    # Run 3: effective
    RUN_NUM=$((RUN_NUM + 1))
    echo "--- [${RUN_NUM}/${TOTAL_RUNS}] Run 3 (effective ${DURATION}s) ---"
    OUT3=$("$EXE" --rom "$ROM" --benchmark $DURATION --ultra-analog --analog-output RF --analog-size $SZ --crt --accuracy A 2>&1)
    FPS3=$(extract_fps "$OUT3")
    echo "  Run 3: ${FPS3} FPS"
    R3[$IDX]="$FPS3"

    # Compute average of run 2 + run 3
    AVGVAL=$(awk "BEGIN{printf \"%.2f\", ($FPS2 + $FPS3) / 2}")
    AVG[$IDX]="$AVGVAL"
    echo "  Average: ${AVGVAL} FPS"
    echo ""

    # Cooldown before next size (skip after last)
    if [[ "$SZ" != "8" ]]; then
        echo "--- Cooling ${COOLDOWN}s before next resolution ---"
        sleep $COOLDOWN
    fi

    IDX=$((IDX + 1))
done

# Generate markdown report
echo "=== Generating report: $OUTFILE ==="

# Header
cat > "$OUTFILE" << MDEOF
# Analog Mode Multi-Resolution Benchmark

**日期**: ${DATE}
**測試目的**: 比較不同 AnalogSize (2x/4x/6x/8x) 下完整類比模擬管線的效能

---

## 測試條件

| 項目 | 設定 |
|------|------|
| 組態 | Release (x64) |
| AccuracyOptA | ON |
| AnalogMode | 1 (Enabled) |
| UltraAnalog | 1 (Level 3 物理路徑) |
| CRT | 1 (Stage 2 電子束光學) |
| AnalogOutput | RF |
| 音效輸出 | OFF (headless) |
| 畫面顯示 | OFF (headless, 無 GPU rendering) |
| 測試時長 | ${DURATION} 秒 / 回合 |
| 測試 ROM | Mega Man 5 (USA).nes (Mapper 004, MMC3) |
| 冷卻時間 | 每回合前 ${COOLDOWN} 秒 |
| SIMD 優化 | S01 (vFw 常數提升) + S02 (SIMD 像素打包) |

**完整管線**:
\`\`\`
PPU per-scanline → Ntsc.DecodeScanline (21.477 MHz waveform + coherent demodulation + RF AM modulation)
→ linearBuffer → CrtScreen.Render (Gaussian scanline bloom) → AnalogScreenBuf
\`\`\`

**測試協議**: 3 次法 — 第 1 次為 JIT/TieredPGO 暖機不採計 → sleep ${COOLDOWN}s → 第 2 次（有效）→ sleep ${COOLDOWN}s → 第 3 次（有效）→ 取 Run 2、Run 3 平均

---

## 測試結果

### 各解析度 FPS 對照

| AnalogSize | 解析度 | 像素數 | Run 1 (JIT) | Run 2 | Run 3 | **平均 FPS** | 即時倍率 |
|:----------:|:------:|:------:|:-----------:|:-----:|:-----:|:------------:|:--------:|
MDEOF

# Write table rows
IDX=0
for SZ in 2 4 6 8; do
    W=$((256 * SZ))
    H=$((210 * SZ))
    PIXELS=$((W * H))
    PIXELS_K=$(awk "BEGIN{printf \"%.1f\", $PIXELS / 1000}")
    REALTIME=$(awk "BEGIN{printf \"%.2f\", ${AVG[$IDX]} / 60.0988}")
    echo "| ${SZ}x | ${W}×${H} | ${PIXELS_K}K | ${R1[$IDX]} | ${R2[$IDX]} | ${R3[$IDX]} | **${AVG[$IDX]}** | ${REALTIME}x |" >> "$OUTFILE"
    IDX=$((IDX + 1))
done

# Scaling analysis
cat >> "$OUTFILE" << 'MDEOF'

### 效能縮放分析

MDEOF

echo "| 比較 | 基準 (2x) FPS | 目標 FPS | 像素比 | FPS 比 | 備註 |" >> "$OUTFILE"
echo "|------|:------------:|:--------:|:------:|:------:|------|" >> "$OUTFILE"

BASE_FPS="${AVG[0]}"
BASE_PIX=$((512 * 420))
IDX=0
for SZ in 2 4 6 8; do
    W=$((256 * SZ))
    H=$((210 * SZ))
    PIX=$((W * H))
    PIX_RATIO=$(awk "BEGIN{printf \"%.1f\", $PIX / $BASE_PIX}")
    FPS_RATIO=$(awk "BEGIN{printf \"%.2f\", ${AVG[$IDX]} / $BASE_FPS}")
    if [[ "$SZ" == "2" ]]; then
        NOTE="基準"
    else
        NOTE=""
    fi
    echo "| 2x → ${SZ}x | ${BASE_FPS} | ${AVG[$IDX]} | ${PIX_RATIO}x | ${FPS_RATIO}x | ${NOTE} |" >> "$OUTFILE"
    IDX=$((IDX + 1))
done

cat >> "$OUTFILE" << 'MDEOF'

> **NES 即時 FPS**: 60.0988 FPS（NTSC）。平均 FPS ÷ 60.0988 = 即時倍率，≥ 1.0x 即可流暢運行。
MDEOF

echo ""
echo "=== Benchmark complete ==="
echo "Results written to: $OUTFILE"
echo ""
echo "=== Summary ==="
IDX=0
for SZ in 2 4 6 8; do
    W=$((256 * SZ))
    H=$((210 * SZ))
    echo "  ${SZ}x (${W}x${H}): ${AVG[$IDX]} FPS"
    IDX=$((IDX + 1))
done
