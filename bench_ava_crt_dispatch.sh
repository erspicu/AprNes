#!/bin/bash
# AprNesAvalonia CRT dispatch baseline benchmark
# Compares CrtImpl=Scalar vs CrtImpl=Simd on the same machine under locked-frequency.
#
# Fixed condition:
#   --ultra-analog --analog-size 8 --analog-output RF --audio-dsp --audio-mode 2 --crt --accuracy A
#
# 3-run protocol: discard JIT-warmup (Run 1) → avg(Run 2, Run 3)

set -e

# ── Config ──
EXE="AprNesAvalonia/bin/Release/net10.0/AprNesAvalonia.exe"
ROM="etc/Mega Man 5 (USA).nes"
DURATION=20
JIT_DURATION=10
COOLDOWN=30
DATE=$(date +%Y-%m-%d)
OUTFILE="MD/PerformanceWithAV/CRT_Dispatch_Baseline_${DATE}.md"

mkdir -p "$(dirname "$OUTFILE")"

# ── Helper ──
extract_fps() {
    echo "$1" | sed -n 's/.*= \([0-9.]*\) FPS.*/\1/p' | head -1
}

run_bench() {
    local LABEL="$1"
    local DUR="$2"
    local OUT
    OUT=$("$EXE" \
        --rom "$ROM" --benchmark "$DUR" \
        --ultra-analog --analog-output RF --analog-size 8 --crt \
        --accuracy A \
        --audio-dsp --audio-mode 2 2>&1)
    local FPS
    FPS=$(extract_fps "$OUT")
    # Label goes to stderr so caller can capture only the FPS on stdout
    echo "  ${LABEL}: ${FPS} FPS" >&2
    echo "$FPS"
}

# Results array: [Scalar_R1, Scalar_R2, Scalar_R3, Simd_R1, Simd_R2, Simd_R3]
declare -A RESULTS

for IMPL in Scalar Simd; do
    echo "============================================================"
    echo "  Building AprNesAvalonia Release with CrtImpl=${IMPL}"
    echo "============================================================"
    dotnet build AprNesAvalonia/AprNesAvalonia.csproj -c Release --nologo -v q -p:CrtImpl="$IMPL" 2>&1 | tail -3
    echo ""

    echo "============================================================"
    echo "  Benchmark: CrtImpl=${IMPL}  (ultra 8x RF, DSP mode 2)"
    echo "============================================================"

    # Run 1: JIT warmup
    echo "--- Run 1 (JIT warmup ${JIT_DURATION}s, discard) ---"
    FPS1=$(run_bench "Run 1" "$JIT_DURATION")
    RESULTS[${IMPL}_R1]="$FPS1"

    echo "--- Cooling ${COOLDOWN}s ---"
    sleep $COOLDOWN

    # Run 2: effective
    echo "--- Run 2 (effective ${DURATION}s) ---"
    FPS2=$(run_bench "Run 2" "$DURATION")
    RESULTS[${IMPL}_R2]="$FPS2"

    echo "--- Cooling ${COOLDOWN}s ---"
    sleep $COOLDOWN

    # Run 3: effective
    echo "--- Run 3 (effective ${DURATION}s) ---"
    FPS3=$(run_bench "Run 3" "$DURATION")
    RESULTS[${IMPL}_R3]="$FPS3"

    AVG=$(awk "BEGIN{printf \"%.2f\", ($FPS2 + $FPS3) / 2}")
    RESULTS[${IMPL}_AVG]="$AVG"
    echo "  Average (Run 2 + Run 3): ${AVG} FPS"
    echo ""

    # Cooldown before next strategy (skip after last)
    if [[ "$IMPL" != "Simd" ]]; then
        echo "--- Cooling ${COOLDOWN}s before next strategy ---"
        sleep $COOLDOWN
    fi
done

# ── Generate markdown report ──
echo "=== Generating report: $OUTFILE ==="

CPU_INFO=$(wmic cpu get name 2>/dev/null | tail -n +2 | head -1 | tr -d '\r' | sed 's/^ *//;s/ *$//' || echo "Unknown CPU")

cat > "$OUTFILE" << MDEOF
# AprNesAvalonia CRT Dispatch Baseline — ${DATE}

**測試目的**：量測 Phase 0 的 MSBuild \`CrtImpl\` 切換下，Scalar 與 SIMD 兩條 CRT 管線 baseline FPS。電腦目前為**鎖頻狀態**，過往 MEMORY.md 中的數字已不適用，本檔為新基準。

---

## 測試條件

| 項目 | 設定 |
|------|------|
| 專案 | AprNesAvalonia (Avalonia 11.3.13 / .NET 10) |
| 組態 | Release (TieredPGO ON) |
| CPU | ${CPU_INFO} |
| AccuracyOptA | ON |
| AnalogMode | ON + UltraAnalog (Level 3 物理路徑) |
| CRT | ON (Stage 2 電子束光學) |
| AnalogOutput | RF |
| AnalogSize | 8x (2048×1680) |
| **Audio DSP** | Mode 2 (Modern: 5×FIR + Bass Boost + Stereo + Haas + Reverb) |
| 音效播放 | OFF (DSP 處理完後丟棄，不經 WaveOut) |
| 畫面顯示 | OFF (headless) |
| 測試時長 | ${DURATION} 秒 / 回合 |
| 測試 ROM | Mega Man 5 (USA).nes (Mapper 004, MMC3) |
| 冷卻時間 | 每回合前 ${COOLDOWN} 秒 |

**測試協議**：3 次法 — Run 1（JIT/TieredPGO 暖機，${JIT_DURATION}s）不採計 → cooldown → Run 2（${DURATION}s，採計）→ cooldown → Run 3（${DURATION}s，採計）→ 取 Run 2、Run 3 平均。

**切換機制**：\`dotnet build -p:CrtImpl=Scalar|Simd\`（Phase 0 build-time 切換；Phase 1 將改為 runtime \`--crt-strategy\` CLI）。

**影像 + 音訊管線**：
\`\`\`
PPU per-scanline → Ntsc.DecodeScanline (21.477 MHz waveform + coherent demod + RF AM)
→ linearBuffer → CrtScreen.Render (scanline bloom + mask + phosphor + convergence + curvature)
→ AnalogScreenBuf

Audio: 5×256-tap FIR (per-channel) → Triangle Bass Boost (12dB) →
       Stereo Pan (100%) → Haas (20ms) → Comb Reverb ×4 (wet=15%)
\`\`\`

---

## 測試結果

| CrtImpl | Run 1 (JIT) | Run 2 | Run 3 | **平均 FPS** | 即時倍率 |
|:-------:|:-----------:|:-----:|:-----:|:------------:|:--------:|
MDEOF

for IMPL in Scalar Simd; do
    AVG="${RESULTS[${IMPL}_AVG]}"
    REALTIME=$(awk "BEGIN{printf \"%.2f\", $AVG / 60.0988}")
    echo "| ${IMPL} | ${RESULTS[${IMPL}_R1]} | ${RESULTS[${IMPL}_R2]} | ${RESULTS[${IMPL}_R3]} | **${AVG}** | ${REALTIME}x |" >> "$OUTFILE"
done

# Speedup analysis
SCALAR_AVG="${RESULTS[Scalar_AVG]}"
SIMD_AVG="${RESULTS[Simd_AVG]}"
SPEEDUP=$(awk "BEGIN{printf \"%.2f\", $SIMD_AVG / $SCALAR_AVG}")

cat >> "$OUTFILE" << MDEOF

### Speedup 分析

| 比較 | Scalar FPS | SIMD FPS | Speedup |
|------|:----------:|:--------:|:-------:|
| Scalar → SIMD | ${SCALAR_AVG} | ${SIMD_AVG} | **${SPEEDUP}x** |

> **NES 即時 FPS**：60.0988（NTSC）。平均 FPS ÷ 60.0988 = 即時倍率；≥ 1.0x 即可流暢運行。

---

## 後續里程碑

本檔為 **Phase 0 baseline**。當 Phase 1（runtime dispatch 重構）與 Phase 2（GPU backend）完成後，將在同檔追加：
- runtime \`--crt-strategy\` 切換的 FPS 結果（驗證 0-overhead）
- GPU backend 的 FPS（目標 ≥ 2x SIMD）
- ARM NEON backend（如 Phase 3 實作）

參考：[MD/gpu/CRT_GPU_Design.md](../gpu/CRT_GPU_Design.md)
MDEOF

echo ""
echo "=== Done ==="
echo "Scalar avg: ${SCALAR_AVG} FPS"
echo "SIMD   avg: ${SIMD_AVG} FPS"
echo "Speedup:   ${SPEEDUP}x"
echo "Report:    ${OUTFILE}"
