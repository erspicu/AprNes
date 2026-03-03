#!/bin/bash
# run_tests_AccuracyCoin_report.sh
# Run AccuracyCoin.nes comprehensive accuracy tests and generate HTML report.
#
# Usage:
#   bash run_tests_AccuracyCoin_report.sh             # Full run (build + screenshots + report)
#   bash run_tests_AccuracyCoin_report.sh --no-build  # Skip build step
#   bash run_tests_AccuracyCoin_report.sh --no-screenshots  # Skip page screenshots (faster)
set -u

cd /c/ai_project/AprNes

EXE="AprNes/bin/Debug/AprNes.exe"
ROM="nes-test-roms-master/AccuracyCoin-main/AccuracyCoin.nes"
REPORT_DIR="report"
SS_DIR="$REPORT_DIR/screenshots/ac"
OUTPUT_HTML="$REPORT_DIR/AccuracyCoin_report.html"

OPT_BUILD=1
OPT_SCREENSHOTS=1

for arg in "$@"; do
    case "$arg" in
        --no-build)       OPT_BUILD=0 ;;
        --no-screenshots) OPT_SCREENSHOTS=0 ;;
        *) echo "Unknown option: $arg"; exit 1 ;;
    esac
done

# ─────────────────────────────────────────────
# Build
# ─────────────────────────────────────────────
if [ $OPT_BUILD -eq 1 ]; then
    echo "=== Building project ==="
    powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
    if [ $? -ne 0 ]; then echo "BUILD FAILED"; exit 1; fi
fi

if [ ! -f "$EXE" ]; then
    echo "ERROR: $EXE not found. Run without --no-build first."
    exit 1
fi

if [ ! -f "$ROM" ]; then
    echo "ERROR: ROM not found: $ROM"
    exit 1
fi

mkdir -p "$SS_DIR"

# ─────────────────────────────────────────────
# Timing plan (all values in NES seconds)
# ─────────────────────────────────────────────
# t=3.0 : press Start → begin AutomaticallyRunEveryTestInROM
# t=90.5: screenshot of results summary table
# t=91.0: press Start → enter per-suite page view (lands on page 1)
#
# Per-page navigation (2s per page):
#   page N: Right press at (91 + 1 + (N-1)*2), screenshot at (91 + 1 + (N-1)*2 + 1)
#   i.e.: page 1 = screenshot at 93.0 (no Right needed, already on page 1)
#         page 2 = Right at 94.0, screenshot at 95.0
#         page 3 = Right at 96.0, screenshot at 97.0  ...
#         page 14 = Right at 94+(13-1)*2=118.0, screenshot at 119.0
#
# Page 15 (Power On State, DRAW tests — 5 sub-items):
#   Right at 120.0 → screenshot overview at 121.0
#   A at 122.0 → sub-item 0, screenshot at 123.0
#   Down+A at 124.0,125.0 → sub-item 1, screenshot at 126.0
#   Down+A at 127.0,128.0 → sub-item 2, screenshot at 129.0
#   Down+A at 130.0,131.0 → sub-item 3, screenshot at 132.0
#   Down+A at 133.0,134.0 → sub-item 4, screenshot at 135.0
#
# Pages 16-20:
#   page 16 = Right at 136.0, screenshot at 137.0
#   page 17 = Right at 138.0, screenshot at 139.0
#   page 18 = Right at 140.0, screenshot at 141.0
#   page 19 = Right at 142.0, screenshot at 143.0
#   page 20 = Right at 144.0, screenshot at 145.0
#
# Total run time: 147 NES seconds = ~8832 frames
# (In headless mode this runs in a few wall-clock seconds)

T_TOTAL=147.0

build_sequences() {
    python3 - <<'PYEOF'
import sys

# NES fps constant (same as TestRunner.cs)
NES_FPS = 60.0988

def f(t):
    """Convert NES seconds to frame number (int)"""
    return int(t * NES_FPS)

input_events = []  # "Button:sec"
timed_shots  = []  # "path:sec"

SS_DIR = "report/screenshots/ac"

# Boot + trigger all-tests run
input_events.append("Start:3.0")

# Results summary screenshot (before pressing Start to navigate pages)
timed_shots.append(f"{SS_DIR}/ac_summary.png:90.5")

# Press Start → enter per-suite page view (lands on page 1)
input_events.append("Start:91.0")

# Page 1: screenshot at 93.0 (no Right needed)
timed_shots.append(f"{SS_DIR}/ac_page_01.png:93.0")

# Pages 2-14
for page in range(2, 15):
    t_right  = 92.0 + (page - 1) * 2.0
    t_shot   = t_right + 1.0
    input_events.append(f"Right:{t_right:.1f}")
    timed_shots.append(f"{SS_DIR}/ac_page_{page:02d}.png:{t_shot:.1f}")

# Page 15 (Power On State — DRAW tests, 5 sub-items)
# Right to navigate to page 15
input_events.append("Right:120.0")
timed_shots.append(f"{SS_DIR}/ac_page_15.png:121.0")
# Sub-item 0 (cursor already at item 0): press A
input_events.append("A:122.0")
timed_shots.append(f"{SS_DIR}/ac_page_15_item00.png:123.0")
# Sub-items 1-4: Down then A
for item in range(1, 5):
    t_down = 122.0 + item * 5.0
    t_a    = t_down + 1.0
    t_shot = t_a + 1.0
    input_events.append(f"Down:{t_down:.1f}")
    input_events.append(f"A:{t_a:.1f}")
    timed_shots.append(f"{SS_DIR}/ac_page_15_item{item:02d}.png:{t_shot:.1f}")

# Pages 16-20
for page in range(16, 21):
    t_right = 134.0 + (page - 16) * 2.0
    t_shot  = t_right + 1.0
    input_events.append(f"Right:{t_right:.1f}")
    timed_shots.append(f"{SS_DIR}/ac_page_{page:02d}.png:{t_shot:.1f}")

# Output as shell variables
print(f"INPUT_SPEC=\"{','.join(input_events)}\"")
print(f"TIMED_SHOTS=\"{','.join(timed_shots)}\"")
PYEOF
}

# ─────────────────────────────────────────────
# Run emulator
# ─────────────────────────────────────────────
if [ $OPT_SCREENSHOTS -eq 1 ]; then
    echo "=== Generating timing sequences ==="
    eval "$(build_sequences)"

    echo "=== Running AccuracyCoin (with screenshots, ~5-10 seconds wall time) ==="
    RUN_OUTPUT=$("$EXE" \
        --rom "$ROM" \
        --time "$T_TOTAL" \
        --input "$INPUT_SPEC" \
        --timed-screenshots "$TIMED_SHOTS" \
        --dump-ac-results \
        2>/dev/null)
else
    echo "=== Running AccuracyCoin (results only, no screenshots) ==="
    RUN_OUTPUT=$("$EXE" \
        --rom "$ROM" \
        --time "$T_TOTAL" \
        --input "Start:3.0" \
        --dump-ac-results \
        2>/dev/null)
fi

echo "$RUN_OUTPUT" | grep -v "^AC_RESULTS_HEX:"

AC_HEX=$(echo "$RUN_OUTPUT" | grep "^AC_RESULTS_HEX:" | head -1 | cut -d: -f2-)

if [ -z "$AC_HEX" ]; then
    echo "WARNING: No AC_RESULTS_HEX in output — test results will show as 'not run'"
fi

# ─────────────────────────────────────────────
# Generate HTML report
# ─────────────────────────────────────────────
echo "=== Generating HTML report ==="

python3 - "$AC_HEX" "$OUTPUT_HTML" "$SS_DIR" <<'PYEOF'
import sys, os, json, datetime

ac_hex    = sys.argv[1]  # hex string of $0300-$04FF (512 hex chars = 256 bytes)
out_html  = sys.argv[2]
ss_dir    = sys.argv[3]

# Decode results bytes ($0300-$04FF)
# Our dump is $0300-$04FF → index 0 = $0300
mem = bytes.fromhex(ac_hex) if len(ac_hex) == 512 else bytes(256)
def mem_byte(addr):
    idx = addr - 0x0300
    return mem[idx] if 0 <= idx < len(mem) else 0

def decode_result(byte):
    """Returns ('PASS','FAIL','SKIP','NOT_RUN'), error_code"""
    if byte == 0xFF: return 'SKIP', None
    if byte == 0x00: return 'NOT_RUN', None
    if byte & 0x01:  return 'PASS', None
    ec = byte >> 2
    return 'FAIL', ec

# ── Full test map ─────────────────────────────────────────────────────────────
# Format: (suite_name, [(test_name, result_addr_hex), ...])
# DRAW tests (Power On State) store at $03FC-$03FF; omitted from run-all summary.
SUITES = [
    ("CPU Behavior", [
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
    ("Addressing mode wraparound", [
        ("Absolute Indexed",  0x046E),
        ("Zero Page Indexed", 0x046F),
        ("Indirect",          0x0470),
        ("Indirect, X",       0x0471),
        ("Indirect, Y",       0x0472),
        ("Relative",          0x0473),
    ]),
    ("Unofficial Instructions: SLO", [
        ("$03  SLO (indirect,X)", 0x0409),
        ("$07  SLO zeropage",     0x040A),
        ("$0F  SLO absolute",     0x040B),
        ("$13  SLO (indirect),Y", 0x040C),
        ("$17  SLO zeropage,X",   0x040D),
        ("$1B  SLO absolute,Y",   0x040E),
        ("$1F  SLO absolute,X",   0x040F),
    ]),
    ("Unofficial Instructions: RLA", [
        ("$23  RLA (indirect,X)", 0x0419),
        ("$27  RLA zeropage",     0x041A),
        ("$2F  RLA absolute",     0x041B),
        ("$33  RLA (indirect),Y", 0x041C),
        ("$37  RLA zeropage,X",   0x041D),
        ("$3B  RLA absolute,Y",   0x041E),
        ("$3F  RLA absolute,X",   0x041F),
    ]),
    ("Unofficial Instructions: SRE", [
        ("$43  SRE (indirect,X)", 0x0420),
        ("$47  SRE zeropage",     0x047F),
        ("$4F  SRE absolute",     0x0422),
        ("$53  SRE (indirect),Y", 0x0423),
        ("$57  SRE zeropage,X",   0x0424),
        ("$5B  SRE absolute,Y",   0x0425),
        ("$5F  SRE absolute,X",   0x0426),
    ]),
    ("Unofficial Instructions: RRA", [
        ("$63  RRA (indirect,X)", 0x0427),
        ("$67  RRA zeropage",     0x0428),
        ("$6F  RRA absolute",     0x0429),
        ("$73  RRA (indirect),Y", 0x042A),
        ("$77  RRA zeropage,X",   0x042B),
        ("$7B  RRA absolute,Y",   0x042C),
        ("$7F  RRA absolute,X",   0x042D),
    ]),
    ("Unofficial Instructions: *AX", [
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
    ("Unofficial Instructions: DCP", [
        ("$C3  DCP (indirect,X)", 0x0438),
        ("$C7  DCP zeropage",     0x0439),
        ("$CF  DCP absolute",     0x043A),
        ("$D3  DCP (indirect),Y", 0x043B),
        ("$D7  DCP zeropage,X",   0x043C),
        ("$DB  DCP absolute,Y",   0x043D),
        ("$DF  DCP absolute,X",   0x043E),
    ]),
    ("Unofficial Instructions: ISC", [
        ("$E3  ISC (indirect,X)", 0x043F),
        ("$E7  ISC zeropage",     0x0440),
        ("$EF  ISC absolute",     0x0441),
        ("$F3  ISC (indirect),Y", 0x0442),
        ("$F7  ISC zeropage,X",   0x0443),
        ("$FB  ISC absolute,Y",   0x0444),
        ("$FF  ISC absolute,X",   0x0445),
    ]),
    ("Unofficial Instructions: SH*", [
        ("$93  SHA (indirect),Y", 0x0446),
        ("$9F  SHA absolute,Y",   0x0447),
        ("$9B  SHS absolute,Y",   0x0448),
        ("$9C  SHY absolute,X",   0x0449),
        ("$9E  SHX absolute,Y",   0x044A),
        ("$BB  LAE absolute,Y",   0x044B),
    ]),
    ("Unofficial Immediates", [
        ("$0B  ANC Immediate", 0x0410),
        ("$2B  ANC Immediate", 0x0411),
        ("$4B  ASR Immediate", 0x0412),
        ("$6B  ARR Immediate", 0x0413),
        ("$8B  ANE Immediate", 0x0414),
        ("$AB  LXA Immediate", 0x0415),
        ("$CB  AXS Immediate", 0x0416),
        ("$EB  SBC Immediate", 0x0417),
    ]),
    ("CPU Interrupts", [
        ("Interrupt flag latency", 0x0461),
        ("NMI Overlap BRK",        0x0462),
        ("NMI Overlap IRQ",        0x0463),
    ]),
    ("APU Registers and DMA tests", [
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
    ("APU Tests", [
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
    ("Power On State (DRAW)", [
        # DRAW tests — results stored at $03FC-$03FF, not in run-all summary
        ("PPU Reset Flag", 0x03FD),  # result_PowOn_PPUReset
        ("CPU RAM",        0x03FC),  # result_PowOn_CPURAM
        ("CPU Registers",  0x03FD),  # result_PowOn_CPUReg (shares addr with PPUReset intentionally)
        ("PPU RAM",        0x03FE),  # result_PowOn_PPURAM
        ("Palette RAM",    0x03FF),  # result_PowOn_PPUPal
    ]),
    ("PPU Behavior", [
        ("CHR ROM is not writable", 0x0485),
        ("PPU Register Mirroring",  0x0404),
        ("PPU Register Open Bus",   0x044E),
        ("PPU Read Buffer",         0x0476),
        ("Palette RAM Quirks",      0x047E),
        ("Rendering Flag Behavior", 0x0486),
        ("$2007 read w/ rendering", 0x048A),
    ]),
    ("PPU VBlank Timing", [
        ("VBlank beginning",       0x0450),
        ("VBlank end",             0x0451),
        ("NMI Control",            0x0452),
        ("NMI Timing",             0x0453),
        ("NMI Suppression",        0x0454),
        ("NMI at VBlank end",      0x0455),
        ("NMI disabled at VBlank", 0x0456),
    ]),
    ("Sprite Evaluation", [
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
    ("PPU Misc.", [
        ("Attributes As Tiles",      0x0481),
        ("t Register Quirks",        0x0482),
        ("Stale BG Shift Registers", 0x0483),
        ("BG Serial In",             0x0487),
        ("Sprites On Scanline 0",    0x0484),
        ("$2004 Stress Test",        0x048C),
    ]),
    ("CPU Behavior 2", [
        ("Instruction Timing",  0x0460),
        ("Implied Dummy Reads", 0x046D),
        ("Branch Dummy Reads",  0x048B),
        ("JSR Edge Cases",      0x047C),
    ]),
]

# ── Count results ─────────────────────────────────────────────────────────────
total_pass = total_fail = total_skip = total_not_run = 0
for suite_name, tests in SUITES:
    is_draw = "(DRAW)" in suite_name
    for test_name, addr in tests:
        if is_draw:
            continue  # DRAW tests are not in run-all; skip from summary
        b = mem_byte(addr)
        status, _ = decode_result(b)
        if   status == 'PASS':    total_pass    += 1
        elif status == 'FAIL':    total_fail    += 1
        elif status == 'SKIP':    total_skip    += 1
        else:                     total_not_run += 1

total_tests = total_pass + total_fail + total_skip + total_not_run

def ss_rel(path):
    """Return relative path from report dir for use in HTML"""
    return os.path.relpath(path, os.path.dirname(out_html)).replace('\\', '/')

def ss_img(name, alt=""):
    """Return <img> tag if screenshot exists, else placeholder"""
    path = f"{ss_dir}/{name}"
    if os.path.exists(path):
        rel = ss_rel(path)
        return f'<img src="{rel}" alt="{alt}" class="ss" loading="lazy">'
    return f'<div class="ss-missing">[screenshot not available]</div>'

def status_badge(status, err_code=None):
    if status == 'PASS':
        return '<span class="badge pass">PASS</span>'
    elif status == 'FAIL':
        ec = f" (err {err_code})" if err_code else ""
        return f'<span class="badge fail">FAIL{ec}</span>'
    elif status == 'SKIP':
        return '<span class="badge skip">SKIP</span>'
    elif status == 'NOT_RUN':
        return '<span class="badge notrun">N/A</span>'
    return '<span class="badge unknown">?</span>'

now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")

# ── Generate HTML ─────────────────────────────────────────────────────────────
html = f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>AccuracyCoin Report — AprNes</title>
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
.ss-missing {{ background:#111; color:#555; text-align:center; padding:16px; font-style:italic; }}
.page-section {{ background:#16213e; border:1px solid #2d3561; border-radius:8px; margin-bottom:24px; padding:16px; }}
.page-section h3 {{ margin:0 0 10px; color:#a3c4f3; }}
.page-grid {{ display:grid; grid-template-columns:repeat(auto-fill,minmax(270px,1fr)); gap:12px; }}
.page-card {{ background:#1a1a2e; border:1px solid #2d3561; border-radius:6px; padding:10px; text-align:center; }}
.page-card h4 {{ margin:0 0 8px; color:#ccc; font-size:.9em; }}
.draw-section {{ background:#1a0a2e; border:1px solid #4a148c; border-radius:8px; padding:16px; margin-bottom:24px; }}
.draw-section h3 {{ color:#ce93d8; margin:0 0 10px; }}
.draw-grid {{ display:grid; grid-template-columns:repeat(auto-fill,minmax(270px,1fr)); gap:12px; }}
.draw-card {{ background:#12001e; border:1px solid #4a148c; border-radius:6px; padding:10px; text-align:center; }}
.draw-card h4 {{ margin:0 0 8px; color:#ce93d8; font-size:.9em; }}
</style>
</head>
<body>
<h1>🪙 AccuracyCoin Report — AprNes</h1>
<div class="meta">Generated: {now} | ROM: AccuracyCoin.nes (Mapper 0/NROM)</div>

<div class="summary">
  <div class="stat"><div class="num pass-c">{total_pass}</div><div class="lbl">PASS</div></div>
  <div class="stat"><div class="num fail-c">{total_fail}</div><div class="lbl">FAIL</div></div>
  <div class="stat"><div class="num skip-c">{total_skip}</div><div class="lbl">SKIP</div></div>
  <div class="stat"><div class="num notrun-c">{total_not_run}</div><div class="lbl">N/A</div></div>
  <div class="stat"><div class="num">{total_tests}</div><div class="lbl">TOTAL</div></div>
</div>

<div class="page-section">
  <h3>📊 Results Summary Screen</h3>
  {ss_img('ac_summary.png', 'Summary screen')}
</div>
"""

# Per-suite sections with screenshots
for suite_idx, (suite_name, tests) in enumerate(SUITES):
    page_num = suite_idx + 1
    is_draw = "(DRAW)" in suite_name
    disp_name = suite_name.replace(" (DRAW)", "")
    ss_file = f"ac_page_{page_num:02d}.png"

    # Count pass/fail/skip for header
    s_pass = s_fail = s_skip = s_nr = 0
    for test_name, addr in tests:
        b = mem_byte(addr)
        status, _ = decode_result(b)
        if   status == 'PASS':    s_pass += 1
        elif status == 'FAIL':    s_fail += 1
        elif status == 'SKIP':    s_skip += 1
        else:                     s_nr += 1

    stats_parts = []
    if s_pass:  stats_parts.append(f'<span class="pass-c">{s_pass}✓</span>')
    if s_fail:  stats_parts.append(f'<span class="fail-c">{s_fail}✗</span>')
    if s_skip:  stats_parts.append(f'<span class="skip-c">{s_skip}↷</span>')
    if s_nr and not is_draw: stats_parts.append(f'<span class="notrun-c">{s_nr}?</span>')

    if is_draw:
        # Draw test section (purple)
        html += f"""
<div class="draw-section">
  <h3>🎨 Page {page_num}: {disp_name} (DRAW Tests)</h3>
  <p style="color:#aaa;font-size:.85em">These tests display memory values rather than simple pass/fail.
  Run individually with sub-item navigation (A button) to see results.</p>
  <div class="draw-grid">
    <div class="draw-card"><h4>Page overview</h4>{ss_img(ss_file, disp_name)}</div>
"""
        for item_idx in range(len(tests)):
            item_ss = f"ac_page_15_item{item_idx:02d}.png"
            test_name = tests[item_idx][0]
            html += f'    <div class="draw-card"><h4>{test_name}</h4>{ss_img(item_ss, test_name)}</div>\n'
        html += "  </div>\n</div>\n"
    else:
        html += f"""
<div class="suite">
  <div class="suite-header">
    <span>Page {page_num}: {disp_name}</span>
    <span class="suite-stats">{' '.join(stats_parts)}</span>
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
  AccuracyCoin by &lt;author&gt; | Result encoding: 0x01=PASS, (N&lt;&lt;2)|0x02=FAIL(N), 0xFF=SKIP, 0x00=not run
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

echo "=== Done ==="
echo "HTML report: $OUTPUT_HTML"
