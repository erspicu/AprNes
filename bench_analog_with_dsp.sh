#!/bin/bash
# Analog + Audio DSP Multi-Resolution Benchmark
# Tests AnalogSize 2x/4x/6x/8x with audio DSP enabled (no playback)
# 3-run protocol: discard JIT run, average run 2+3
#
# Usage:
#   bash bench_analog_with_dsp.sh --audio-mode <0|1|2> [--no-build]
#
# Examples:
#   bash bench_analog_with_dsp.sh --audio-mode 0              # Pure Digital
#   bash bench_analog_with_dsp.sh --audio-mode 1              # Authentic (RF+Buzz max cost)
#   bash bench_analog_with_dsp.sh --audio-mode 2              # Modern (all effects max)
#   bash bench_analog_with_dsp.sh --audio-mode 2 --no-build   # skip build

set -e

# ── Parse arguments ──
AUDIO_MODE=""
NO_BUILD=0

for arg in "$@"; do
    case "$arg" in
        --audio-mode)  shift_next=1 ;;
        --no-build)    NO_BUILD=1 ;;
        *)
            if [[ "$shift_next" == "1" ]]; then
                AUDIO_MODE="$arg"
                shift_next=0
            fi
            ;;
    esac
done

if [[ -z "$AUDIO_MODE" ]] || ! [[ "$AUDIO_MODE" =~ ^[012]$ ]]; then
    echo "Error: --audio-mode <0|1|2> is required"
    echo ""
    echo "Usage: bash bench_analog_with_dsp.sh --audio-mode <0|1|2> [--no-build]"
    echo "  0 = Pure Digital (LUT mixing + DC killer)"
    echo "  1 = Authentic (3D DAC + 256-tap FIR + Console LPF + RF + Buzz)"
    echo "  2 = Modern (5×FIR + Bass Boost + Stereo Pan + Haas + Reverb)"
    exit 1
fi

# Mode name lookup
case "$AUDIO_MODE" in
    0) MODE_NAME="Pure Digital" ; MODE_TAG="mode0" ;;
    1) MODE_NAME="Authentic"    ; MODE_TAG="mode1" ;;
    2) MODE_NAME="Modern"       ; MODE_TAG="mode2" ;;
esac

# ── Config ──
EXE="C:/ai_project/AprNes/AprNes/bin/Release/AprNes.exe"
ROM="C:/ai_project/AprNes/etc/Mega Man 5 (USA).nes"
DURATION=20
JIT_DURATION=10
COOLDOWN=30
TOTAL_RUNS=$((4 * 3))  # 4 resolutions × 3 runs each
RUN_NUM=0
DATE=$(date +%Y-%m-%d)
OUTFILE="C:/ai_project/AprNes/MD/PerformanceWithAV/Analog_DSP_${MODE_TAG}_Benchmark_${DATE}.md"

echo "============================================================"
echo "  Analog + Audio DSP Benchmark"
echo "  Audio Mode: ${AUDIO_MODE} (${MODE_NAME})"
echo "  Output: ${OUTFILE}"
echo "============================================================"
echo ""

# ── Build ──
if [[ "$NO_BUILD" -eq 0 ]]; then
    echo "=== Building Release... ==="
    powershell -NoProfile -Command "& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' 'C:\ai_project\AprNes\AprNes\AprNes.csproj' /p:Configuration=Release /p:Platform=x64 /nologo /v:minimal" 2>&1
    echo ""
fi

# ── Helper ──
extract_fps() {
    echo "$1" | sed -n 's/.*= \([0-9.]*\) FPS.*/\1/p' | head -1
}

# ── Benchmark loop ──
R1=()
R2=()
R3=()
AVG=()

IDX=0
for SZ in 2 4 6 8; do
    W=$((256 * SZ))
    H=$((210 * SZ))
    echo "============================================================"
    echo "  AnalogSize=${SZ}x  (${W}x${H})  +  AudioDSP Mode ${AUDIO_MODE}"
    echo "============================================================"

    BENCH_FLAGS="--rom $ROM --benchmark %DURATION% --ultra-analog --analog-output RF --analog-size $SZ --crt --accuracy A --audio-dsp --audio-mode $AUDIO_MODE"

    # Run 1: JIT warmup (discard)
    RUN_NUM=$((RUN_NUM + 1))
    echo "--- [${RUN_NUM}/${TOTAL_RUNS}] Run 1 (JIT warmup ${JIT_DURATION}s, discard) ---"
    OUT1=$("$EXE" --rom "$ROM" --benchmark $JIT_DURATION --ultra-analog --analog-output RF --analog-size $SZ --crt --accuracy A --audio-dsp --audio-mode $AUDIO_MODE 2>&1)
    FPS1=$(extract_fps "$OUT1")
    echo "  Run 1: ${FPS1} FPS (discarded)"
    R1[$IDX]="$FPS1"

    # Run 2: effective
    RUN_NUM=$((RUN_NUM + 1))
    echo "--- [${RUN_NUM}/${TOTAL_RUNS}] Run 2 (effective ${DURATION}s) ---"
    OUT2=$("$EXE" --rom "$ROM" --benchmark $DURATION --ultra-analog --analog-output RF --analog-size $SZ --crt --accuracy A --audio-dsp --audio-mode $AUDIO_MODE 2>&1)
    FPS2=$(extract_fps "$OUT2")
    echo "  Run 2: ${FPS2} FPS"
    R2[$IDX]="$FPS2"

    echo "--- Cooling ${COOLDOWN}s ---"
    sleep $COOLDOWN

    # Run 3: effective
    RUN_NUM=$((RUN_NUM + 1))
    echo "--- [${RUN_NUM}/${TOTAL_RUNS}] Run 3 (effective ${DURATION}s) ---"
    OUT3=$("$EXE" --rom "$ROM" --benchmark $DURATION --ultra-analog --analog-output RF --analog-size $SZ --crt --accuracy A --audio-dsp --audio-mode $AUDIO_MODE 2>&1)
    FPS3=$(extract_fps "$OUT3")
    echo "  Run 3: ${FPS3} FPS"
    R3[$IDX]="$FPS3"

    # Average run 2 + run 3
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

# ── Mode-specific DSP description ──
case "$AUDIO_MODE" in
    0) DSP_DESC="LUT Mixing → DC Killer → 44.1 kHz (1-in-~41 decimation)" ;;
    1) DSP_DESC="3D DAC LUT → 90Hz HPF → 256-tap FIR (128 polyphase) → Console Model IIR LPF → 60Hz Buzz → RF Crosstalk" ;;
    2) DSP_DESC="5×256-tap FIR (per-channel) → Triangle Bass Boost (12dB) → Stereo Pan (100%) → Haas Effect (20ms) → Comb Reverb ×4 (wet=15%)" ;;
esac

# ── Generate markdown report ──
echo "=== Generating report: $OUTFILE ==="

mkdir -p "$(dirname "$OUTFILE")"

cat > "$OUTFILE" << MDEOF
# Analog + Audio DSP Benchmark — Mode ${AUDIO_MODE} (${MODE_NAME})

**日期**: ${DATE}
**測試目的**: 量測 Analog + Audio DSP Mode ${AUDIO_MODE} (${MODE_NAME}) 在不同解析度下的完整管線效能

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
| **Audio DSP** | **Mode ${AUDIO_MODE} (${MODE_NAME})** |
| 音效播放 | OFF (DSP 處理完後丟棄，不經 WaveOut) |
| 畫面顯示 | OFF (headless, 無 GPU rendering) |
| 測試時長 | ${DURATION} 秒 / 回合 |
| 測試 ROM | Mega Man 5 (USA).nes (Mapper 004, MMC3) |
| 冷卻時間 | 每回合前 ${COOLDOWN} 秒 |

**影像管線**:
\`\`\`
PPU per-scanline → Ntsc.DecodeScanline (21.477 MHz waveform + coherent demodulation + RF AM modulation)
→ linearBuffer → CrtScreen.Render (Gaussian scanline bloom) → AnalogScreenBuf
\`\`\`

**音訊 DSP 管線 (Mode ${AUDIO_MODE})**:
\`\`\`
${DSP_DESC}
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
echo "=== Summary (Mode ${AUDIO_MODE}: ${MODE_NAME}) ==="
IDX=0
for SZ in 2 4 6 8; do
    W=$((256 * SZ))
    H=$((210 * SZ))
    echo "  ${SZ}x (${W}x${H}): ${AVG[$IDX]} FPS"
    IDX=$((IDX + 1))
done
