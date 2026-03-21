#!/bin/bash
# run_ac_test.sh — Run a specific AccuracyCoin page (and optionally a single item)
#
# Usage:
#   bash run_ac_test.sh <page>                  # Run all tests on a page
#   bash run_ac_test.sh <page> <item>           # Run a single test item (1-based)
#   bash run_ac_test.sh <page> --skip <item>    # Skip an item, run the rest
#   bash run_ac_test.sh <page> --no-build       # Skip build
#
# Examples:
#   bash run_ac_test.sh 12                     # Run P12 all tests (including IFlagLatency)
#   bash run_ac_test.sh 12 1                   # Run only P12 item 1 (IFlagLatency)
#   bash run_ac_test.sh 12 --skip 1            # Run P12, skip item 1
#   bash run_ac_test.sh 14 7                   # Run only P14 item 7 (APU Register Activation)
set -u

cd /c/ai_project/AprNes

EXE="AprNes/bin/Debug/AprNes.exe"
ROM="nes-test-roms-master/AccuracyCoin-main/AccuracyCoin.nes"

PAGE=""
ITEM=""
SKIP_ITEM=""
OPT_BUILD=1
TIMEOUT=120
EXTRA_TIME=0

# Parse arguments
while [ $# -gt 0 ]; do
    case "$1" in
        --no-build) OPT_BUILD=0; shift ;;
        --skip)     SKIP_ITEM="$2"; shift 2 ;;
        --timeout)  TIMEOUT="$2"; shift 2 ;;
        --extra-time) EXTRA_TIME="$2"; shift 2 ;;
        *)
            if [ -z "$PAGE" ]; then PAGE="$1"
            elif [ -z "$ITEM" ]; then ITEM="$1"
            else echo "Unknown arg: $1"; exit 1
            fi
            shift ;;
    esac
done

if [ -z "$PAGE" ]; then
    echo "Usage: bash run_ac_test.sh <page> [item] [--skip N] [--no-build] [--timeout N]"
    exit 1
fi

# Build
if [ $OPT_BUILD -eq 1 ]; then
    echo "=== Building ==="
    powershell -NoProfile -Command "& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' \
        'C:\ai_project\AprNes\AprNes\AprNes.csproj' /p:Configuration=Debug /p:Platform=x64 /t:Rebuild /nologo /v:minimal" 2>&1 | tail -3
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

# Navigate to target page (always use Right — Left doesn't wrap in AccuracyCoin)
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

# Handle skip item (press Down to item, B to skip, Up to go back)
if [ -n "$SKIP_ITEM" ]; then
    # Navigate down to the item
    for ((i=0; i<SKIP_ITEM; i++)); do
        T=$(python3 -c "print(f'{$T + 0.4:.1f}')")
        if [ -n "$INPUT_EVENTS" ]; then INPUT_EVENTS="$INPUT_EVENTS,"; fi
        INPUT_EVENTS="${INPUT_EVENTS}Down:${T}"
    done
    # B to toggle skip
    T=$(python3 -c "print(f'{$T + 0.4:.1f}')")
    INPUT_EVENTS="${INPUT_EVENTS},B:${T}"
    # Navigate back up
    for ((i=0; i<SKIP_ITEM; i++)); do
        T=$(python3 -c "print(f'{$T + 0.3:.1f}')")
        INPUT_EVENTS="${INPUT_EVENTS},Up:${T}"
    done
    T=$(python3 -c "print(f'{$T + 0.5:.1f}')")
    T_A=$T
fi

# Run specific item or whole page
if [ -n "$ITEM" ]; then
    # Navigate down to specific item
    for ((i=0; i<ITEM; i++)); do
        T=$(python3 -c "print(f'{$T + 0.4:.1f}')")
        if [ -n "$INPUT_EVENTS" ]; then INPUT_EVENTS="$INPUT_EVENTS,"; fi
        INPUT_EVENTS="${INPUT_EVENTS}Down:${T}"
    done
    # A to run single item
    T=$(python3 -c "print(f'{$T + 0.5:.1f}')")
    INPUT_EVENTS="${INPUT_EVENTS},A:${T}"
    T_A=$T
    TEST_WAIT=15  # single item: up to 15s
else
    # A to run all tests on the page
    if [ -n "$INPUT_EVENTS" ]; then INPUT_EVENTS="$INPUT_EVENTS,"; fi
    INPUT_EVENTS="${INPUT_EVENTS}A:${T_A}"
    TEST_WAIT=$(get_test_wait $PAGE)
fi

TEST_WAIT=$(( TEST_WAIT + EXTRA_TIME ))
TOTAL_TIME=$(python3 -c "print(f'{$T_A + $TEST_WAIT:.1f}')")
SS_TIME=$(python3 -c "print(f'{$T_A + $TEST_WAIT - 0.5:.1f}')")
SS_FILE="result/ac_p${PAGE}_test.png"

echo "=== AccuracyCoin Page $PAGE${ITEM:+ Item $ITEM}${SKIP_ITEM:+ (skip item $SKIP_ITEM)} ==="
echo "  Input: $INPUT_EVENTS"
echo "  Total time: ${TOTAL_TIME}s"

$EXE --rom "$ROM" \
    --time "$TOTAL_TIME" \
    --input "$INPUT_EVENTS" \
    --screenshot "$SS_FILE" \
    --timed-screenshots "$SS_FILE:$SS_TIME" \
    --dump-ac-results 2>/dev/null

echo ""
echo "Screenshot: $SS_FILE"

# Parse and show results
HEX=$(cat /dev/stdin 2>/dev/null || true)
