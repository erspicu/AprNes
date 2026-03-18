#!/bin/bash
# run_tests_AccuracyCoin_report.sh
# Run AccuracyCoin page-by-page and generate HTML report.
# All 136 tests are executed (no skips).
# Page 15 (Power On State) is DRAW-only, screenshots only.
#
# Usage:
#   bash run_tests_AccuracyCoin_report.sh                    # Full run (build + test + summary + report)
#   bash run_tests_AccuracyCoin_report.sh --no-build         # Skip build
#   bash run_tests_AccuracyCoin_report.sh --no-screenshots   # Skip screenshots
#   bash run_tests_AccuracyCoin_report.sh --skip 12:1        # Skip P12 item 1 (disables summary run)
#   bash run_tests_AccuracyCoin_report.sh --skip 12:1 --skip 13:7  # Multiple skips
set -u

cd /c/ai_project/AprNes

EXE="AprNes/bin/Debug/AprNes.exe"
ROM="nes-test-roms-master/AccuracyCoin-main/AccuracyCoin.nes"
REPORT_DIR="reports/report"
SS_DIR="$REPORT_DIR/screenshots-ac"
OUTPUT_HTML="$REPORT_DIR/AccuracyCoin_report.html"
RESULTS_DIR="temp/ac_results"

OPT_BUILD=1
OPT_SCREENSHOTS=1
SKIP_SPECS=()  # Array of "page:item" skip specs

while [ $# -gt 0 ]; do
    case "$1" in
        --no-build)       OPT_BUILD=0; shift ;;
        --no-screenshots) OPT_SCREENSHOTS=0; shift ;;
        --skip)           SKIP_SPECS+=("$2"); shift 2 ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

HAS_SKIPS=0
if [ ${#SKIP_SPECS[@]} -gt 0 ]; then HAS_SKIPS=1; fi

# Build
if [ $OPT_BUILD -eq 1 ]; then
    echo "=== Building project ==="
    powershell -NoProfile -Command "& 'C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe' 'C:\ai_project\AprNes\AprNes.sln' /p:Configuration=Debug /t:Rebuild /nologo" 2>&1 | tail -3
    if [ $? -ne 0 ]; then echo "BUILD FAILED"; exit 1; fi
fi

if [ ! -f "$EXE" ]; then echo "ERROR: $EXE not found."; exit 1; fi
if [ ! -f "$ROM" ]; then echo "ERROR: ROM not found: $ROM"; exit 1; fi

mkdir -p "$SS_DIR" "$RESULTS_DIR"

# Clean old results
rm -f "$RESULTS_DIR"/page_*.hex

# ───────────────────────────────────────────────
# Run each page separately
# ───────────────────────────────────────────────
get_test_wait() {
    # Per-page test wait time (seconds after A press for all tests to complete)
    # Measured empirically with 2s safety buffer
    case $1 in
        12)     echo 30 ;;  # CPU Interrupts (IFlagLatency takes ~20s)
        14)     echo 9  ;;  # APU Tests: ~7s
        17)     echo 16 ;;  # PPU VBlank Timing: ~14s
        13)     echo 8  ;;  # APU Registers/DMA: ~6s
        18)     echo 12 ;;  # PPU OAM tests: ~10s
        16|19|20) echo 8 ;;  # PPU tests: ~6s
        *)      echo 6  ;;  # CPU/unofficial opcodes: ~3-4s
    esac
}

# Get skip items for a given page from SKIP_SPECS array
get_page_skips() {
    local PAGE=$1
    local ITEMS=""
    for spec in "${SKIP_SPECS[@]}"; do
        local SP=$(echo "$spec" | cut -d: -f1)
        local SI=$(echo "$spec" | cut -d: -f2)
        if [ "$SP" -eq "$PAGE" ] 2>/dev/null; then
            if [ -n "$ITEMS" ]; then ITEMS="$ITEMS "; fi
            ITEMS="${ITEMS}${SI}"
        fi
    done
    echo "$ITEMS"
}

run_page() {
    local PAGE=$1
    local NAV_TIME=3.0  # seconds to wait for ROM boot
    local INPUT_EVENTS=""
    local TIMED_SHOTS=""
    local SS_FILE="$SS_DIR/ac_page_$(printf '%02d' $PAGE).png"

    # Check for skips on this page
    local PAGE_SKIPS=$(get_page_skips $PAGE)

    if [ -n "$PAGE_SKIPS" ]; then
        echo -n "  Page $PAGE/20 (skip item $PAGE_SKIPS): "
    else
        echo -n "  Page $PAGE/20: "
    fi

    # Navigate to the target page
    # Pages 1-10: use Right from page 1 (0.5s intervals)
    # Pages 11-20: use Left from page 1 (1.0s intervals, wraps around)
    if [ $PAGE -le 10 ]; then
        local PRESSES=$(( PAGE - 1 ))
        local DIR="Right"
        local NAV_INTERVAL=0.5
    else
        local PRESSES=$(( 21 - PAGE ))
        local DIR="Left"
        local NAV_INTERVAL=1.0
    fi

    local T=$NAV_TIME
    for ((i=0; i<PRESSES; i++)); do
        T=$(python3 -c "print(f'{$T + $NAV_INTERVAL:.1f}')")
        if [ -n "$INPUT_EVENTS" ]; then INPUT_EVENTS="$INPUT_EVENTS,"; fi
        INPUT_EVENTS="${INPUT_EVENTS}${DIR}:${T}"
    done

    # After navigation, wait a moment
    T=$(python3 -c "print(f'{$T + 1.0:.1f}')")
    local T_A=$T

    # Handle per-item skips: navigate to item, press B, return
    if [ -n "$PAGE_SKIPS" ]; then
        for SKIP_ITEM in $PAGE_SKIPS; do
            # Navigate down to the item
            for ((si=0; si<SKIP_ITEM; si++)); do
                T=$(python3 -c "print(f'{$T + 0.4:.1f}')")
                if [ -n "$INPUT_EVENTS" ]; then INPUT_EVENTS="$INPUT_EVENTS,"; fi
                INPUT_EVENTS="${INPUT_EVENTS}Down:${T}"
            done
            # B to toggle skip
            T=$(python3 -c "print(f'{$T + 0.4:.1f}')")
            if [ -n "$INPUT_EVENTS" ]; then INPUT_EVENTS="$INPUT_EVENTS,"; fi
            INPUT_EVENTS="${INPUT_EVENTS}B:${T}"
            # Navigate back up
            for ((si=0; si<SKIP_ITEM; si++)); do
                T=$(python3 -c "print(f'{$T + 0.3:.1f}')")
                INPUT_EVENTS="${INPUT_EVENTS},Up:${T}"
            done
            T=$(python3 -c "print(f'{$T + 0.5:.1f}')")
            T_A=$T
        done
    fi

    # Press A to run all (non-skipped) tests
    if [ -n "$INPUT_EVENTS" ]; then INPUT_EVENTS="$INPUT_EVENTS,"; fi
    INPUT_EVENTS="${INPUT_EVENTS}A:${T_A}"

    # Per-page test wait time (optimized)
    local TEST_WAIT=$(get_test_wait $PAGE)
    local TOTAL_TIME=$(python3 -c "print(f'{$T_A + $TEST_WAIT:.1f}')")

    # Screenshot at the end (after tests complete)
    local T_SS=$(python3 -c "print(f'{$T_A + $TEST_WAIT - 0.5:.1f}')")

    if [ $OPT_SCREENSHOTS -eq 1 ]; then
        TIMED_SHOTS="$SS_FILE:$T_SS"
    fi

    # Run emulator
    local CMD="$EXE --rom $ROM --time $TOTAL_TIME --input $INPUT_EVENTS --dump-ac-results"
    if [ -n "$TIMED_SHOTS" ]; then
        CMD="$CMD --timed-screenshots $TIMED_SHOTS"
    fi

    local OUTPUT
    OUTPUT=$($CMD 2>/dev/null)
    local HEX=$(echo "$OUTPUT" | grep "^AC_RESULTS_HEX:" | head -1 | cut -d: -f2-)

    if [ -n "$HEX" ]; then
        echo "$HEX" > "$RESULTS_DIR/page_$(printf '%02d' $PAGE).hex"
    fi

    # Count pass/fail using known test addresses for this page
    local PASS_COUNT=$(echo "$HEX" | python3 -c "
import sys
PAGE = $PAGE
data = sys.stdin.read().strip()
if len(data) != 1024:
    print('no data'); exit()
mem = bytes.fromhex(data)
def mb(addr):
    i = addr - 0x0300
    return mem[i] if 0 <= i < len(mem) else 0
# Known test addresses per page
ADDRS = {
    1: [0x0405,0x0403,0x044D,0x0474,0x0475,0x0406,0x0407,0x0408,0x047D],
    2: [0x046E,0x046F,0x0470,0x0471,0x0472,0x0473],
    3: [0x0409,0x040A,0x040B,0x040C,0x040D,0x040E,0x040F],
    4: [0x0419,0x041A,0x041B,0x041C,0x041D,0x041E,0x041F],
    5: [0x0420,0x047F,0x0422,0x0423,0x0424,0x0425,0x0426],
    6: [0x0427,0x0428,0x0429,0x042A,0x042B,0x042C,0x042D],
    7: [0x042E,0x042F,0x0430,0x0431,0x0432,0x0433,0x0434,0x0435,0x0436,0x0437],
    8: [0x0438,0x0439,0x043A,0x043B,0x043C,0x043D,0x043E],
    9: [0x043F,0x0440,0x0441,0x0442,0x0443,0x0444,0x0445],
    10: [0x0446,0x0447,0x0448,0x0449,0x044A,0x044B],
    11: [0x0410,0x0411,0x0412,0x0413,0x0414,0x0415,0x0416,0x0417],
    12: [0x0461,0x0462,0x0463],
    13: [0x046C,0x0488,0x044C,0x044F,0x045D,0x045E,0x046B,0x0477,0x0479,0x0478],
    14: [0x0465,0x0466,0x0467,0x0468,0x0469,0x046A,0x045C,0x045F,0x047A],
    15: [],
    16: [0x0485,0x0404,0x044E,0x0476,0x047E,0x0486,0x048A],
    17: [0x0450,0x0451,0x0452,0x0453,0x0454,0x0455,0x0456],
    18: [0x0459,0x0457,0x048D,0x0489,0x0458,0x045A,0x045B,0x047B,0x0480],
    19: [0x0481,0x0482,0x0483,0x0487,0x0484,0x048C],
    20: [0x0460,0x046D,0x048B,0x047C],
}
addrs = ADDRS.get(PAGE, [])
p = f = s = 0
for a in addrs:
    b = mb(a)
    if b == 0xFF: s += 1
    elif b == 0: pass
    elif b & 0x01: p += 1
    else: f += 1
print(f'{p} PASS, {f} FAIL, {s} SKIP')
" 2>/dev/null)

    echo "$PASS_COUNT"
}

run_page_15_draw() {
    echo -n "  Page 15/20 (DRAW): "
    local NAV_TIME=3.0
    local INPUT_EVENTS=""
    local TIMED_SHOTS=""

    # Navigate to page 15 using Left (6 presses: page 1 -> 20 -> 19 -> 18 -> 17 -> 16 -> 15)
    local T=$NAV_TIME
    for ((i=0; i<6; i++)); do
        T=$(python3 -c "print(f'{$T + 1.0:.1f}')")
        if [ -n "$INPUT_EVENTS" ]; then INPUT_EVENTS="$INPUT_EVENTS,"; fi
        INPUT_EVENTS="${INPUT_EVENTS}Left:${T}"
    done

    # Wait for page to load, then screenshot overview
    T=$(python3 -c "print(f'{$T + 1.0:.1f}')")

    if [ $OPT_SCREENSHOTS -eq 1 ]; then
        TIMED_SHOTS="$SS_DIR/ac_page_15.png:$T"

        # Navigate to each sub-item (5 items)
        # First: press Down to go to first item, then A to run it
        local T_DOWN=$(python3 -c "print(f'{$T + 1.0:.1f}')")
        INPUT_EVENTS="${INPUT_EVENTS},Down:${T_DOWN}"
        local T_ITEM_A=$(python3 -c "print(f'{$T_DOWN + 0.5:.1f}')")
        INPUT_EVENTS="${INPUT_EVENTS},A:${T_ITEM_A}"
        local T_ITEM_SS=$(python3 -c "print(f'{$T_ITEM_A + 3.0:.1f}')")
        TIMED_SHOTS="${TIMED_SHOTS},$SS_DIR/ac_page_15_item00.png:$T_ITEM_SS"

        # Items 1-4: Down + A + screenshot
        for ((item=1; item<5; item++)); do
            T_DOWN=$(python3 -c "print(f'{$T_ITEM_SS + 1.0:.1f}')")
            INPUT_EVENTS="${INPUT_EVENTS},Down:${T_DOWN}"
            T_ITEM_A=$(python3 -c "print(f'{$T_DOWN + 0.5:.1f}')")
            INPUT_EVENTS="${INPUT_EVENTS},A:${T_ITEM_A}"
            T_ITEM_SS=$(python3 -c "print(f'{$T_ITEM_A + 3.0:.1f}')")
            TIMED_SHOTS="${TIMED_SHOTS},$SS_DIR/ac_page_15_item$(printf '%02d' $item).png:$T_ITEM_SS"
        done

        local TOTAL_TIME=$(python3 -c "print(f'{$T_ITEM_SS + 2.0:.1f}')")
    else
        local TOTAL_TIME=$(python3 -c "print(f'{$T + 2.0:.1f}')")
    fi

    $EXE --rom "$ROM" --time "$TOTAL_TIME" --input "$INPUT_EVENTS" \
        ${TIMED_SHOTS:+--timed-screenshots "$TIMED_SHOTS"} \
        2>/dev/null >/dev/null

    echo "screenshots captured"
}

echo "=== Running AccuracyCoin page-by-page ==="
echo ""

for PAGE in $(seq 1 20); do
    if [ $PAGE -eq 15 ]; then
        run_page_15_draw
    else
        run_page $PAGE
    fi
done

echo ""

# ───────────────────────────────────────────────
# Run full summary (Start button → wait for all tests → screenshot)
# Only when no --skip specified (Start can't skip individual items)
# ───────────────────────────────────────────────
SUMMARY_SS="$SS_DIR/ac_summary.png"
if [ $HAS_SKIPS -eq 0 ]; then
    echo "=== Running full summary (Start → ~95s) ==="
    SUMMARY_INPUT="Start:3.0"
    SUMMARY_TOTAL=98.0
    SUMMARY_SS_TIME=97.0

    SUMMARY_CMD="$EXE --rom $ROM --time $SUMMARY_TOTAL --input $SUMMARY_INPUT --dump-ac-results"
    # Summary screenshot is always captured (even with --no-screenshots)
    SUMMARY_CMD="$SUMMARY_CMD --timed-screenshots $SUMMARY_SS:$SUMMARY_SS_TIME"

    SUMMARY_OUTPUT=$($SUMMARY_CMD 2>/dev/null)
    SUMMARY_HEX=$(echo "$SUMMARY_OUTPUT" | grep "^AC_RESULTS_HEX:" | head -1 | cut -d: -f2-)
    if [ -n "$SUMMARY_HEX" ]; then
        echo "$SUMMARY_HEX" > "$RESULTS_DIR/summary.hex"
    fi
    echo "  Summary screenshot: $SUMMARY_SS"
else
    echo "=== Skipping summary run (--skip specified) ==="
    rm -f "$SUMMARY_SS"
fi
echo ""

# ───────────────────────────────────────────────
# Convert PNG screenshots to WebP
# ───────────────────────────────────────────────
echo "=== Converting screenshots to WebP ==="
python3 -c "
from PIL import Image
import glob, os
ss_dir = '$SS_DIR'
count = 0
for png in glob.glob(os.path.join(ss_dir, '*.png')):
    webp = png.rsplit('.', 1)[0] + '.webp'
    try:
        img = Image.open(png)
        img.save(webp, 'WEBP', lossless=True)
        os.remove(png)
        count += 1
    except Exception as e:
        print(f'  WARN: {png}: {e}')
print(f'  Converted {count} screenshots to WebP')
"
echo ""

# ───────────────────────────────────────────────
# Merge results from all pages
# ───────────────────────────────────────────────
echo "=== Merging results and generating report ==="

python3 - "$OUTPUT_HTML" "$SS_DIR" "$RESULTS_DIR" "$OPT_SCREENSHOTS" "$SUMMARY_SS" <<'PYEOF'
import sys, os, datetime

out_html  = sys.argv[1]
ss_dir    = sys.argv[2]
results_dir = sys.argv[3]
has_screenshots = sys.argv[4] == "1"
summary_ss = sys.argv[5].rsplit('.', 1)[0] + '.webp'  # use webp version

# Merge all page results: overlay non-zero bytes
merged = bytearray(512)  # $0300-$04FF
for page in range(1, 21):
    hex_file = os.path.join(results_dir, f"page_{page:02d}.hex")
    if not os.path.exists(hex_file):
        continue
    with open(hex_file, 'r') as f:
        hex_data = f.read().strip()
    if len(hex_data) != 1024:
        continue
    page_data = bytes.fromhex(hex_data)
    for i in range(512):
        if page_data[i] != 0:
            merged[i] = page_data[i]

def mem_byte(addr):
    idx = addr - 0x0300
    return merged[idx] if 0 <= idx < len(merged) else 0

def decode_result(byte):
    if byte == 0xFF: return 'SKIP', None
    if byte == 0x00: return 'NOT_RUN', None
    if byte & 0x01:  return 'PASS', None
    ec = byte >> 2
    return 'FAIL', ec

# Full test map (page_number, suite_name, tests)
SUITES = [
    (1, "CPU Behavior", [
        ("ROM is not writable",  0x0405),
        ("RAM Mirroring",        0x0403),
        ("PC Wraparound",        0x044D),
        ("The Decimal Flag",     0x0474),
        ("The B Flag",           0x0475),
        ("Dummy read cycles",    0x0406),
        ("Dummy write cycles",   0x0407),
        ("Open Bus",             0x0408),
        ("All NOP instructions", 0x047D),
    ]),
    (2, "Addressing mode wraparound", [
        ("Absolute Indexed",  0x046E),
        ("Zero Page Indexed", 0x046F),
        ("Indirect",          0x0470),
        ("Indirect, X",       0x0471),
        ("Indirect, Y",       0x0472),
        ("Relative",          0x0473),
    ]),
    (3, "Unofficial Instructions: SLO", [
        ("$03  SLO (indirect,X)", 0x0409),
        ("$07  SLO zeropage",     0x040A),
        ("$0F  SLO absolute",     0x040B),
        ("$13  SLO (indirect),Y", 0x040C),
        ("$17  SLO zeropage,X",   0x040D),
        ("$1B  SLO absolute,Y",   0x040E),
        ("$1F  SLO absolute,X",   0x040F),
    ]),
    (4, "Unofficial Instructions: RLA", [
        ("$23  RLA (indirect,X)", 0x0419),
        ("$27  RLA zeropage",     0x041A),
        ("$2F  RLA absolute",     0x041B),
        ("$33  RLA (indirect),Y", 0x041C),
        ("$37  RLA zeropage,X",   0x041D),
        ("$3B  RLA absolute,Y",   0x041E),
        ("$3F  RLA absolute,X",   0x041F),
    ]),
    (5, "Unofficial Instructions: SRE", [
        ("$43  SRE (indirect,X)", 0x0420),
        ("$47  SRE zeropage",     0x047F),
        ("$4F  SRE absolute",     0x0422),
        ("$53  SRE (indirect),Y", 0x0423),
        ("$57  SRE zeropage,X",   0x0424),
        ("$5B  SRE absolute,Y",   0x0425),
        ("$5F  SRE absolute,X",   0x0426),
    ]),
    (6, "Unofficial Instructions: RRA", [
        ("$63  RRA (indirect,X)", 0x0427),
        ("$67  RRA zeropage",     0x0428),
        ("$6F  RRA absolute",     0x0429),
        ("$73  RRA (indirect),Y", 0x042A),
        ("$77  RRA zeropage,X",   0x042B),
        ("$7B  RRA absolute,Y",   0x042C),
        ("$7F  RRA absolute,X",   0x042D),
    ]),
    (7, "Unofficial Instructions: *AX", [
        ("$83  SAX (indirect,X)", 0x042E),
        ("$87  SAX zeropage",     0x042F),
        ("$8F  SAX absolute",     0x0430),
        ("$97  SAX zeropage,Y",   0x0431),
        ("$A3  LAX (indirect,X)", 0x0432),
        ("$A7  LAX zeropage",     0x0433),
        ("$AF  LAX absolute",     0x0434),
        ("$B3  LAX (indirect),Y", 0x0435),
        ("$B7  LAX zeropage,Y",   0x0436),
        ("$BF  LAX absolute,X",   0x0437),
    ]),
    (8, "Unofficial Instructions: DCP", [
        ("$C3  DCP (indirect,X)", 0x0438),
        ("$C7  DCP zeropage",     0x0439),
        ("$CF  DCP absolute",     0x043A),
        ("$D3  DCP (indirect),Y", 0x043B),
        ("$D7  DCP zeropage,X",   0x043C),
        ("$DB  DCP absolute,Y",   0x043D),
        ("$DF  DCP absolute,X",   0x043E),
    ]),
    (9, "Unofficial Instructions: ISC", [
        ("$E3  ISC (indirect,X)", 0x043F),
        ("$E7  ISC zeropage",     0x0440),
        ("$EF  ISC absolute",     0x0441),
        ("$F3  ISC (indirect),Y", 0x0442),
        ("$F7  ISC zeropage,X",   0x0443),
        ("$FB  ISC absolute,Y",   0x0444),
        ("$FF  ISC absolute,X",   0x0445),
    ]),
    (10, "Unofficial Instructions: SH*", [
        ("$93  SHA (indirect),Y", 0x0446),
        ("$9F  SHA absolute,Y",   0x0447),
        ("$9B  SHS absolute,Y",   0x0448),
        ("$9C  SHY absolute,X",   0x0449),
        ("$9E  SHX absolute,Y",   0x044A),
        ("$BB  LAE absolute,Y",   0x044B),
    ]),
    (11, "Unofficial Immediates", [
        ("$0B  ANC Immediate", 0x0410),
        ("$2B  ANC Immediate", 0x0411),
        ("$4B  ASR Immediate", 0x0412),
        ("$6B  ARR Immediate", 0x0413),
        ("$8B  ANE Immediate", 0x0414),
        ("$AB  LXA Immediate", 0x0415),
        ("$CB  AXS Immediate", 0x0416),
        ("$EB  SBC Immediate", 0x0417),
    ]),
    (12, "CPU Interrupts", [
        ("Interrupt flag latency", 0x0461),
        ("NMI Overlap BRK",        0x0462),
        ("NMI Overlap IRQ",        0x0463),
    ]),
    (13, "APU Registers and DMA tests", [
        ("DMA + Open Bus",        0x046C),
        ("DMA + $2002 Read",      0x0488),
        ("DMA + $2007 Read",      0x044C),
        ("DMA + $2007 Write",     0x044F),
        ("DMA + $4015 Read",      0x045D),
        ("DMA + $4016 Read",      0x045E),
        ("DMC DMA Bus Conflicts", 0x046B),
        ("DMC DMA + OAM DMA",     0x0477),
        ("Explicit DMA Abort",    0x0479),
        ("Implicit DMA Abort",    0x0478),
    ]),
    (14, "APU Tests", [
        ("Length Counter",           0x0465),
        ("Length Table",             0x0466),
        ("Frame Counter IRQ",        0x0467),
        ("Frame Counter 4-step",     0x0468),
        ("Frame Counter 5-step",     0x0469),
        ("Delta Modulation Channel", 0x046A),
        ("APU Register Activation",  0x045C),
        ("Controller Strobing",      0x045F),
        ("Controller Clocking",      0x047A),
    ]),
    (15, "Power On State (DRAW)", [
        ("PPU Reset Flag", 0x03FD),
        ("CPU RAM",        0x03FC),
        ("CPU Registers",  0x03FD),
        ("PPU RAM",        0x03FE),
        ("Palette RAM",    0x03FF),
    ]),
    (16, "PPU Behavior", [
        ("CHR ROM is not writable", 0x0485),
        ("PPU Register Mirroring",  0x0404),
        ("PPU Register Open Bus",   0x044E),
        ("PPU Read Buffer",         0x0476),
        ("Palette RAM Quirks",      0x047E),
        ("Rendering Flag Behavior", 0x0486),
        ("$2007 read w/ rendering", 0x048A),
    ]),
    (17, "PPU VBlank Timing", [
        ("VBlank beginning",       0x0450),
        ("VBlank end",             0x0451),
        ("NMI Control",            0x0452),
        ("NMI Timing",             0x0453),
        ("NMI Suppression",        0x0454),
        ("NMI at VBlank end",      0x0455),
        ("NMI disabled at VBlank", 0x0456),
    ]),
    (18, "Sprite Evaluation", [
        ("Sprite overflow behavior", 0x0459),
        ("Sprite 0 Hit behavior",    0x0457),
        ("$2002 flag clear timing",  0x048D),
        ("Suddenly Resize Sprite",   0x0489),
        ("Arbitrary Sprite zero",    0x0458),
        ("Misaligned OAM behavior",  0x045A),
        ("Address $2004 behavior",   0x045B),
        ("OAM Corruption",           0x047B),
        ("INC $4014",                0x0480),
    ]),
    (19, "PPU Misc.", [
        ("Attributes As Tiles",      0x0481),
        ("t Register Quirks",        0x0482),
        ("Stale BG Shift Registers", 0x0483),
        ("BG Serial In",             0x0487),
        ("Sprites On Scanline 0",    0x0484),
        ("$2004 Stress Test",        0x048C),
    ]),
    (20, "CPU Behavior 2", [
        ("Instruction Timing",  0x0460),
        ("Implied Dummy Reads", 0x046D),
        ("Branch Dummy Reads",  0x048B),
        ("JSR Edge Cases",      0x047C),
    ]),
]

# Count results (exclude DRAW page)
total_pass = total_fail = total_skip = total_not_run = 0
for page_num, suite_name, tests in SUITES:
    is_draw = "(DRAW)" in suite_name
    for test_name, addr in tests:
        if is_draw:
            continue
        b = mem_byte(addr)
        status, _ = decode_result(b)
        if   status == 'PASS':    total_pass    += 1
        elif status == 'FAIL':    total_fail    += 1
        elif status == 'SKIP':    total_skip    += 1
        else:                     total_not_run += 1

total_tests = total_pass + total_fail + total_skip + total_not_run

def ss_rel(path):
    return os.path.relpath(path, os.path.dirname(out_html)).replace('\\', '/')

def ss_img(name, alt=""):
    path = os.path.join(ss_dir, name)
    if os.path.exists(path):
        rel = ss_rel(path)
        return f'<img src="{rel}" alt="{alt}" class="ss" loading="lazy">'
    return f'<div class="ss-missing">[screenshot not available]</div>'

def status_badge(status, err_code=None):
    if status == 'PASS':
        return '<span class="badge pass">PASS</span>'
    elif status == 'FAIL':
        ec = f" (err {err_code})" if err_code is not None else ""
        return f'<span class="badge fail">FAIL{ec}</span>'
    elif status == 'SKIP':
        return '<span class="badge skip">SKIP</span>'
    elif status == 'NOT_RUN':
        return '<span class="badge notrun">N/A</span>'
    return '<span class="badge unknown">?</span>'

now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")

html = f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>AccuracyCoin Report - AprNes</title>
<style>
body {{ font-family: 'Segoe UI', Arial, sans-serif; background:#1a1a2e; color:#e0e0e0; margin:0; padding:20px; }}
h1 {{ color:#a3c4f3; margin-bottom:4px; }}
.meta {{ color:#888; font-size:.9em; margin-bottom:20px; }}
.summary {{ display:flex; gap:16px; flex-wrap:wrap; margin-bottom:24px; }}
.stat {{ background:#16213e; border-radius:8px; padding:12px 20px; text-align:center; min-width:80px; }}
.stat .num {{ font-size:2em; font-weight:bold; }}
.stat .lbl {{ font-size:.8em; color:#aaa; }}
.pass-c  {{ color:#4caf50; }}
.fail-c  {{ color:#f44336; }}
.skip-c  {{ color:#ff9800; }}
.notrun-c{{ color:#9e9e9e; }}
.suite {{ margin-bottom:28px; border:1px solid #2d3561; border-radius:8px; overflow:hidden; }}
.suite-header {{ background:#0f3460; padding:10px 16px; font-weight:bold; font-size:1.05em; display:flex; align-items:center; gap:10px; }}
.suite-header .suite-stats {{ font-size:.85em; font-weight:normal; color:#aaa; margin-left:auto; }}
table {{ width:100%; border-collapse:collapse; }}
th {{ background:#16213e; padding:8px 12px; text-align:left; font-size:.85em; color:#aaa; }}
td {{ padding:8px 12px; border-top:1px solid #2d3561; font-size:.9em; vertical-align:middle; }}
tr:hover td {{ background:#1e2a5e; }}
.badge {{ display:inline-block; padding:2px 8px; border-radius:4px; font-size:.8em; font-weight:bold; }}
.badge.pass   {{ background:#1b5e20; color:#a5d6a7; }}
.badge.fail   {{ background:#b71c1c; color:#ef9a9a; }}
.badge.skip   {{ background:#e65100; color:#ffcc80; }}
.badge.notrun {{ background:#424242; color:#bdbdbd; }}
.badge.draw   {{ background:#4a148c; color:#ce93d8; }}
.addr {{ font-family:monospace; color:#78909c; font-size:.82em; }}
.ss {{ max-width:512px; width:100%; border:2px solid #2d3561; border-radius:4px; display:block; margin:8px auto; image-rendering:pixelated; }}
.ss-summary {{ max-width:512px; width:100%; border:2px solid #4caf50; border-radius:4px; display:block; margin:12px auto; image-rendering:pixelated; }}
.ss-missing {{ background:#111; color:#555; text-align:center; padding:16px; font-style:italic; }}
.draw-section {{ background:#1a0a2e; border:1px solid #4a148c; border-radius:8px; padding:16px; margin-bottom:24px; }}
.draw-section h3 {{ color:#ce93d8; margin:0 0 10px; }}
.draw-grid {{ display:grid; grid-template-columns:repeat(auto-fill,minmax(270px,1fr)); gap:12px; }}
.draw-card {{ background:#12001e; border:1px solid #4a148c; border-radius:6px; padding:10px; text-align:center; }}
.draw-card h4 {{ margin:0 0 8px; color:#ce93d8; font-size:.9em; }}
.note {{ background:#1e3a1e; border:1px solid #4caf50; border-radius:8px; padding:12px 16px; margin-bottom:24px; font-size:.9em; }}
.note.warn {{ background:#3a2a1e; border-color:#ff9800; }}
</style>
</head>
<body>
<h1>AccuracyCoin Report - AprNes</h1>
<div class="meta">Generated: {now} | ROM: AccuracyCoin.nes (Mapper 0/NROM) | Method: page-by-page</div>

<div class="summary">
  <div class="stat"><div class="num pass-c">{total_pass}</div><div class="lbl">PASS</div></div>
  <div class="stat"><div class="num fail-c">{total_fail}</div><div class="lbl">FAIL</div></div>
  <div class="stat"><div class="num skip-c">{total_skip}</div><div class="lbl">SKIP</div></div>
  <div class="stat"><div class="num notrun-c">{total_not_run}</div><div class="lbl">N/A</div></div>
  <div class="stat"><div class="num">{total_tests}</div><div class="lbl">TOTAL</div></div>
</div>
"""

# Summary screenshot (from Start button full run — always captured)
if os.path.exists(summary_ss):
    summary_rel = os.path.relpath(summary_ss, os.path.dirname(out_html)).replace('\\', '/')
    html += f'<img src="{summary_rel}" alt="AccuracyCoin Summary" class="ss-summary" loading="lazy">\n'

# List remaining failures after summary
if total_fail > 0:
    html += f'<div style="background:#2a1e1e;border:1px solid #f44336;border-radius:8px;padding:12px 16px;margin-bottom:24px;font-size:.9em">\n'
    html += f'<h3 style="color:#ef9a9a;margin:0 0 8px">Remaining {total_fail} Failure{"s" if total_fail != 1 else ""}</h3>\n'
    html += '<table style="margin:0"><tr><th>Page</th><th>Test</th><th>Error</th></tr>\n'
    for page_num, suite_name, tests in SUITES:
        if "(DRAW)" in suite_name:
            continue
        disp = suite_name.replace(" (DRAW)", "")
        for test_name, addr in tests:
            b = mem_byte(addr)
            status, ec = decode_result(b)
            if status == 'FAIL':
                ec_str = f'err {ec}' if ec is not None else '?'
                html += f'<tr><td>P{page_num}</td><td>{test_name}</td><td><span class="badge fail">FAIL ({ec_str})</span></td></tr>\n'
    html += '</table>\n</div>\n'

# Per-suite sections
for page_num, suite_name, tests in SUITES:
    is_draw = "(DRAW)" in suite_name
    disp_name = suite_name.replace(" (DRAW)", "")
    ss_file = f"ac_page_{page_num:02d}.webp"

    s_pass = s_fail = s_skip = s_nr = 0
    for test_name, addr in tests:
        b = mem_byte(addr)
        status, _ = decode_result(b)
        if   status == 'PASS':    s_pass += 1
        elif status == 'FAIL':    s_fail += 1
        elif status == 'SKIP':    s_skip += 1
        else:                     s_nr += 1

    stats_parts = []
    if s_pass:  stats_parts.append(f'<span class="pass-c">{s_pass} PASS</span>')
    if s_fail:  stats_parts.append(f'<span class="fail-c">{s_fail} FAIL</span>')
    if s_skip:  stats_parts.append(f'<span class="skip-c">{s_skip} SKIP</span>')
    if s_nr and not is_draw: stats_parts.append(f'<span class="notrun-c">{s_nr} N/A</span>')

    if is_draw:
        html += f"""
<div class="draw-section">
  <h3>Page {page_num}: {disp_name} (DRAW Tests)</h3>
  <p style="color:#aaa;font-size:.85em">These tests display memory values rather than simple pass/fail.</p>
  <div class="draw-grid">
    <div class="draw-card"><h4>Page overview</h4>{ss_img(ss_file, disp_name)}</div>
"""
        for item_idx in range(len(tests)):
            item_ss = f"ac_page_15_item{item_idx:02d}.webp"
            test_name = tests[item_idx][0]
            html += f'    <div class="draw-card"><h4>{test_name}</h4>{ss_img(item_ss, test_name)}</div>\n'
        html += "  </div>\n</div>\n"
    else:
        html += f"""
<div class="suite">
  <div class="suite-header">
    <span>Page {page_num}: {disp_name}</span>
    <span class="suite-stats">{' | '.join(stats_parts)}</span>
  </div>
  {ss_img(ss_file, disp_name)}
  <table>
    <tr><th>#</th><th>Test</th><th>Result</th><th>Addr</th></tr>
"""
        for t_idx, (test_name, addr) in enumerate(tests):
            b = mem_byte(addr)
            status, ec = decode_result(b)
            badge = status_badge(status, ec)
            html += f'    <tr><td>{t_idx+1}</td><td>{test_name}</td><td>{badge}</td><td class="addr">${addr:04X}={b:02X}</td></tr>\n'
        html += "  </table>\n</div>\n"

html += """
<hr style="border-color:#2d3561;margin:30px 0">
<p style="color:#555;font-size:.8em;text-align:center">
  AccuracyCoin | Result encoding: 0x01=PASS, (N&lt;&lt;2)|0x02=FAIL(N), 0xFF=SKIP, 0x00=not run
</p>
</body>
</html>
"""

os.makedirs(os.path.dirname(out_html), exist_ok=True)
with open(out_html, 'w', encoding='utf-8') as f:
    f.write(html)

print(f"[OK] Report: {out_html}")
print(f"[OK] Results: {total_pass}/{total_tests} PASS, {total_fail} FAIL, {total_skip} SKIP, {total_not_run} N/A")
PYEOF

echo ""
echo "=== Done ==="
echo "HTML report: $OUTPUT_HTML"
