#!/bin/bash
# run_ac_test_tricnes.sh — Run AccuracyCoin on TriCNES headless mode
#
# Usage:
#   bash run_ac_test_tricnes.sh <page>              # Run all tests on a page
#   bash run_ac_test_tricnes.sh <page> <item>       # Run a single test item (1-based)
#   bash run_ac_test_tricnes.sh <page> --no-build   # Skip build
#
# Examples:
#   bash run_ac_test_tricnes.sh 19                  # Run P19 all tests
#   bash run_ac_test_tricnes.sh 19 6                # Run only P19 item 6
set -u

cd /c/ai_project/AprNes

EXE="ref/TriCNES-main/bin/Debug/TriCNES.exe"
ROM="nes-test-roms-master/AccuracyCoin-main/AccuracyCoin.nes"

PAGE=""
ITEM=""
OPT_BUILD=1

# Parse arguments
while [ $# -gt 0 ]; do
    case "$1" in
        --no-build) OPT_BUILD=0; shift ;;
        *)
            if [ -z "$PAGE" ]; then PAGE="$1"
            elif [ -z "$ITEM" ]; then ITEM="$1"
            else echo "Unknown arg: $1"; exit 1
            fi
            shift ;;
    esac
done

if [ -z "$PAGE" ]; then
    echo "Usage: bash run_ac_test_tricnes.sh <page> [item] [--no-build]"
    exit 1
fi

# Build
if [ $OPT_BUILD -eq 1 ]; then
    echo "=== Building TriCNES ==="
    powershell -NoProfile -Command "& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' \
        'C:\ai_project\AprNes\ref\TriCNES-main\TriCNES.csproj' /p:Configuration=Debug /t:Rebuild /nologo /v:minimal" 2>&1 | tail -3
fi

if [ ! -f "$EXE" ]; then echo "ERROR: $EXE not found."; exit 1; fi

# Per-page test wait time
get_test_wait() {
    case $1 in
        12)       echo 30 ;;
        14)       echo 12 ;;
        17)       echo 18 ;;
        13)       echo 10 ;;
        18)       echo 14 ;;
        16|19|20) echo 10 ;;
        *)        echo 8  ;;
    esac
}

# Build navigation input events
NAV_TIME=3.0
INPUT_EVENTS=""
T=$NAV_TIME

# Navigate to target page
PRESSES=$(( PAGE - 1 ))
DIR="Right"
INTERVAL=0.5

for ((i=0; i<PRESSES; i++)); do
    T=$(python3 -c "print(f'{$T + $INTERVAL:.1f}')")
    if [ -n "$INPUT_EVENTS" ]; then INPUT_EVENTS="$INPUT_EVENTS,"; fi
    INPUT_EVENTS="${INPUT_EVENTS}${DIR}:${T}"
done

# Wait after navigation
T=$(python3 -c "print(f'{$T + 1.0:.1f}')")
T_A=$T

# Run specific item or whole page
if [ -n "$ITEM" ]; then
    for ((i=0; i<ITEM; i++)); do
        T=$(python3 -c "print(f'{$T + 0.4:.1f}')")
        if [ -n "$INPUT_EVENTS" ]; then INPUT_EVENTS="$INPUT_EVENTS,"; fi
        INPUT_EVENTS="${INPUT_EVENTS}Down:${T}"
    done
    T=$(python3 -c "print(f'{$T + 0.5:.1f}')")
    INPUT_EVENTS="${INPUT_EVENTS},A:${T}"
    T_A=$T
    TEST_WAIT=15
else
    if [ -n "$INPUT_EVENTS" ]; then INPUT_EVENTS="$INPUT_EVENTS,"; fi
    INPUT_EVENTS="${INPUT_EVENTS}A:${T_A}"
    TEST_WAIT=$(get_test_wait $PAGE)
fi

# TriCNES --time takes integer seconds
TOTAL_TIME=$(python3 -c "import math; print(int(math.ceil($T_A + $TEST_WAIT)))")
SS_FILE="result/tricnes_p${PAGE}_test.png"

echo "=== TriCNES AccuracyCoin Page $PAGE${ITEM:+ Item $ITEM} ==="
echo "  Input: $INPUT_EVENTS"
echo "  Total time: ${TOTAL_TIME}s"

$EXE --rom "$ROM" \
    --time "$TOTAL_TIME" \
    --input "$INPUT_EVENTS" \
    --screenshot "$SS_FILE"

echo ""
echo "Screenshot: $SS_FILE"
