#!/bin/bash
# run_tests_report.sh — Run all NES tests with screenshots and generate HTML report
set -u

cd /c/ai_project/AprNes

EXE="AprNes/bin/Debug/AprNes.exe"
ROMBASE="nes-test-roms-master/checked"
REPORT_DIR="report"
SCREENSHOT_DIR="$REPORT_DIR/screenshots"
RESULTS_JSON="$REPORT_DIR/results.json"

PASS=0
FAIL=0
TOTAL=0
FIRST_JSON=1

# ─────────────────────────────────────────────
# Step 1: Build
# ─────────────────────────────────────────────
echo "=== Building project ==="
powershell -NoProfile -Command "& 'C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe' 'C:\ai_project\AprNes\AprNes.sln' /p:Configuration=Debug /t:Rebuild /nologo /v:minimal"
if [ $? -ne 0 ]; then
    echo "BUILD FAILED"
    exit 1
fi

# ─────────────────────────────────────────────
# Prepare directories
# ─────────────────────────────────────────────
rm -rf "$SCREENSHOT_DIR"
mkdir -p "$SCREENSHOT_DIR"
echo "[" > "$RESULTS_JSON"

# ─────────────────────────────────────────────
# Helper functions
# ─────────────────────────────────────────────
json_escape() {
    printf '%s' "$1" \
        | sed $'s/\x1b\[[0-9;]*[a-zA-Z]//g' \
        | tr -d '\000-\010\013\014\016-\037' \
        | tr -d '\r' \
        | awk '
    BEGIN { ORS="" }
    {
        gsub(/\\/, "\\\\")
        gsub(/"/, "\\\"")
        gsub(/\t/, "\\t")
        if (NR > 1) print "\\n"
        print
    }'
}

run_test() {
    local suite="$1"
    local rom="$2"
    local extra_args="$3"
    local rompath="$ROMBASE/$suite/$rom"

    if [ ! -f "$rompath" ]; then
        echo "SKIP: $rompath not found"
        return
    fi

    local rom_base="${rom%.nes}"
    local ss_rel="screenshots/${suite}/${rom_base}.webp"
    local ss_png_path="$REPORT_DIR/screenshots/${suite}/${rom_base}.png"
    local ss_webp_path="$REPORT_DIR/$ss_rel"
    mkdir -p "$(dirname "$ss_png_path")"

    TOTAL=$((TOTAL+1))
    output=$("$EXE" --rom "$rompath" --wait-result --screenshot "$ss_png_path" $extra_args 2>&1) && rc=0 || rc=$?

    # Convert PNG to lossless WebP and remove original
    if [ -f "$ss_png_path" ]; then
        python -c "
from PIL import Image; Image.open(r'$ss_png_path').save(r'$ss_webp_path','WEBP',lossless=True,method=6)
" 2>/dev/null && rm -f "$ss_png_path"
    fi

    if [ $rc -eq 0 ]; then
        PASS=$((PASS+1))
        status="pass"
        echo "PASS: $suite/$rom"
    else
        FAIL=$((FAIL+1))
        status="fail"
        echo "FAIL($rc): $suite/$rom"
    fi

    local esc_suite=$(json_escape "$suite")
    local esc_rom=$(json_escape "$rom")
    local esc_text=$(json_escape "$output")

    if [ $FIRST_JSON -eq 1 ]; then
        FIRST_JSON=0
    else
        printf ',\n' >> "$RESULTS_JSON"
    fi

    printf '  {"suite":"%s","rom":"%s","status":"%s","exit_code":%d,"result_text":"%s","screenshot":"%s"}' \
        "$esc_suite" "$esc_rom" "$status" "$rc" "$esc_text" "$ss_rel" >> "$RESULTS_JSON"
}

# ─────────────────────────────────────────────
# Step 2: Run all tests
# ─────────────────────────────────────────────
echo ""
echo "=== Running tests with screenshots ==="

# apu_mixer (4)
for rom in dmc.nes noise.nes square.nes triangle.nes; do
    run_test "apu_mixer" "$rom" "--max-wait 30"
done

# apu_reset (6)
for rom in 4015_cleared.nes 4017_timing.nes 4017_written.nes irq_flag_cleared.nes len_ctrs_enabled.nes works_immediately.nes; do
    run_test "apu_reset" "$rom" "--max-wait 30"
done

# apu_test merged + singles (9)
run_test "apu_test" "apu_test.nes" "--max-wait 60"
for rom in 1-len_ctr.nes 2-len_table.nes 3-irq_flag.nes 4-jitter.nes 5-len_timing.nes 6-irq_flag_timing.nes 7-dmc_basics.nes 8-dmc_rates.nes; do
    run_test "apu_test/rom_singles" "$rom" "--max-wait 30"
done

# blargg_apu_2005.07.30 (11)
for rom in 01.len_ctr.nes 02.len_table.nes 03.irq_flag.nes 04.clock_jitter.nes 05.len_timing_mode0.nes 06.len_timing_mode1.nes 07.irq_flag_timing.nes 08.irq_timing.nes 09.reset_timing.nes 10.len_halt_timing.nes 11.len_reload_timing.nes; do
    run_test "blargg_apu_2005.07.30" "$rom" "--max-wait 30"
done

# blargg_nes_cpu_test5 (2)
for rom in cpu.nes official.nes; do
    run_test "blargg_nes_cpu_test5" "$rom" "--max-wait 60"
done

# blargg_ppu_tests_2005.09.15b (5)
for rom in palette_ram.nes power_up_palette.nes sprite_ram.nes vbl_clear_time.nes vram_access.nes; do
    run_test "blargg_ppu_tests_2005.09.15b" "$rom" "--max-wait 30"
done

# branch_timing_tests (3)
for rom in "1.Branch_Basics.nes" "2.Backward_Branch.nes" "3.Forward_Branch.nes"; do
    run_test "branch_timing_tests" "$rom" "--max-wait 30"
done

# cpu_dummy_reads (1)
run_test "cpu_dummy_reads" "cpu_dummy_reads.nes" "--max-wait 30"

# cpu_dummy_writes (2)
for rom in cpu_dummy_writes_oam.nes cpu_dummy_writes_ppumem.nes; do
    run_test "cpu_dummy_writes" "$rom" "--max-wait 30"
done

# cpu_exec_space (2)
for rom in test_cpu_exec_space_ppuio.nes test_cpu_exec_space_apu.nes; do
    run_test "cpu_exec_space" "$rom" "--max-wait 30"
done

# cpu_interrupts_v2 merged + singles (6)
run_test "cpu_interrupts_v2" "cpu_interrupts.nes" "--max-wait 60"
for rom in 1-cli_latency.nes 2-nmi_and_brk.nes 3-nmi_and_irq.nes 4-irq_and_dma.nes 5-branch_delays_irq.nes; do
    run_test "cpu_interrupts_v2/rom_singles" "$rom" "--max-wait 30"
done

# cpu_reset (2)
for rom in registers.nes ram_after_reset.nes; do
    run_test "cpu_reset" "$rom" "--max-wait 30"
done

# cpu_timing_test6 (1)
run_test "cpu_timing_test6" "cpu_timing_test.nes" "--max-wait 60"

# dmc_dma_during_read4 (5)
for rom in dma_2007_read.nes dma_2007_write.nes dma_4016_read.nes double_2007_read.nes read_write_2007.nes; do
    run_test "dmc_dma_during_read4" "$rom" "--max-wait 30"
done

# instr_misc merged + singles (5)
run_test "instr_misc" "instr_misc.nes" "--max-wait 60"
for rom in 01-abs_x_wrap.nes 02-branch_wrap.nes 03-dummy_reads.nes 04-dummy_reads_apu.nes; do
    run_test "instr_misc/rom_singles" "$rom" "--max-wait 30"
done

# instr_test-v3 (17)
run_test "instr_test-v3" "all_instrs.nes" "--max-wait 60"
run_test "instr_test-v3" "official_only.nes" "--max-wait 60"
for rom in 01-implied.nes 02-immediate.nes 03-zero_page.nes 04-zp_xy.nes 05-absolute.nes 06-abs_xy.nes 07-ind_x.nes 08-ind_y.nes 09-branches.nes 10-stack.nes 11-jmp_jsr.nes 12-rts.nes 13-rti.nes 14-brk.nes 15-special.nes; do
    run_test "instr_test-v3/rom_singles" "$rom" "--max-wait 30"
done

# instr_test-v5 (18)
run_test "instr_test-v5" "all_instrs.nes" "--max-wait 60"
run_test "instr_test-v5" "official_only.nes" "--max-wait 60"
for rom in 01-basics.nes 02-implied.nes 03-immediate.nes 04-zero_page.nes 05-zp_xy.nes 06-absolute.nes 07-abs_xy.nes 08-ind_x.nes 09-ind_y.nes 10-branches.nes 11-stack.nes 12-jmp_jsr.nes 13-rts.nes 14-rti.nes 15-brk.nes 16-special.nes; do
    run_test "instr_test-v5/rom_singles" "$rom" "--max-wait 30"
done

# instr_timing (3)
run_test "instr_timing" "instr_timing.nes" "--max-wait 60"
for rom in 1-instr_timing.nes 2-branch_timing.nes; do
    run_test "instr_timing/rom_singles" "$rom" "--max-wait 30"
done

# mmc3_irq_tests (6)
for rom in 1.Clocking.nes 2.Details.nes 3.A12_clocking.nes 4.Scanline_timing.nes 5.MMC3_rev_A.nes 6.MMC3_rev_B.nes; do
    run_test "mmc3_irq_tests" "$rom" "--max-wait 30"
done

# mmc3_test (6)
for rom in 1-clocking.nes 2-details.nes 3-A12_clocking.nes 4-scanline_timing.nes 5-MMC3.nes 6-MMC6.nes; do
    run_test "mmc3_test" "$rom" "--max-wait 30"
done

# mmc3_test_2 (6)
for rom in 1-clocking.nes 2-details.nes 3-A12_clocking.nes 4-scanline_timing.nes 5-MMC3.nes 6-MMC3_alt.nes; do
    run_test "mmc3_test_2/rom_singles" "$rom" "--max-wait 30"
done

# nes_instr_test (11)
for rom in 01-implied.nes 02-immediate.nes 03-zero_page.nes 04-zp_xy.nes 05-absolute.nes 06-abs_xy.nes 07-ind_x.nes 08-ind_y.nes 09-branches.nes 10-stack.nes 11-special.nes; do
    run_test "nes_instr_test/rom_singles" "$rom" "--max-wait 30"
done

# oam_read (1)
run_test "oam_read" "oam_read.nes" "--max-wait 30"

# ppu_open_bus (1)
run_test "ppu_open_bus" "ppu_open_bus.nes" "--max-wait 30"

# ppu_read_buffer (1)
run_test "ppu_read_buffer" "test_ppu_read_buffer.nes" "--max-wait 30"

# ppu_vbl_nmi merged + singles (11)
run_test "ppu_vbl_nmi" "ppu_vbl_nmi.nes" "--max-wait 60"
for rom in 01-vbl_basics.nes 02-vbl_set_time.nes 03-vbl_clear_time.nes 04-nmi_control.nes 05-nmi_timing.nes 06-suppression.nes 07-nmi_on_timing.nes 08-nmi_off_timing.nes 09-even_odd_frames.nes 10-even_odd_timing.nes; do
    run_test "ppu_vbl_nmi/rom_singles" "$rom" "--max-wait 30"
done

# read_joy3 (4)
run_test "read_joy3" "test_buttons.nes" "--max-wait 60 --input A:2.0,B:4.0,Select:6.0,Start:8.0,Up:10.0,Down:12.0,Left:14.0,Right:16.0"
for rom in count_errors.nes count_errors_fast.nes; do
    run_test "read_joy3" "$rom" "--max-wait 30"
done
run_test "read_joy3" "thorough_test.nes" "--max-wait 30 --input A:2.0"

# sprdma_and_dmc_dma (2)
for rom in sprdma_and_dmc_dma.nes sprdma_and_dmc_dma_512.nes; do
    run_test "sprdma_and_dmc_dma" "$rom" "--max-wait 30"
done

# sprite_hit_tests_2005.10.05 (11)
for rom in 01.basics.nes 02.alignment.nes 03.corners.nes 04.flip.nes 05.left_clip.nes 06.right_edge.nes 07.screen_bottom.nes 08.double_height.nes 09.timing_basics.nes 10.timing_order.nes 11.edge_timing.nes; do
    run_test "sprite_hit_tests_2005.10.05" "$rom" "--max-wait 30"
done

# sprite_overflow_tests (5)
for rom in 1.Basics.nes 2.Details.nes 3.Timing.nes 4.Obscure.nes 5.Emulator.nes; do
    run_test "sprite_overflow_tests" "$rom" "--max-wait 30"
done

# vbl_nmi_timing (7)
for rom in 1.frame_basics.nes 2.vbl_timing.nes 3.even_odd_frames.nes 4.vbl_clear_timing.nes 5.nmi_suppression.nes 6.nmi_disable.nes 7.nmi_timing.nes; do
    run_test "vbl_nmi_timing" "$rom" "--max-wait 30"
done

# ─────────────────────────────────────────────
# Close JSON array
# ─────────────────────────────────────────────
echo "" >> "$RESULTS_JSON"
echo "]" >> "$RESULTS_JSON"

echo ""
echo "=== RESULTS: $PASS PASS / $FAIL FAIL / $TOTAL TOTAL ==="

# ─────────────────────────────────────────────
# Step 3: Generate HTML report
# ─────────────────────────────────────────────
echo ""
echo "=== Generating HTML report ==="

{
# ── HTML Part 1: head + styles + body start ──
cat << 'HTML1'
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>AprNes Test Report</title>
<style>
*{box-sizing:border-box;margin:0;padding:0}
:root{
  --bg:#0f172a;--surface:#1e293b;--surface2:#283548;--border:#334155;
  --text:#e2e8f0;--text-dim:#94a3b8;
  --pass:#22c55e;--pass-bg:rgba(34,197,94,.15);
  --fail:#ef4444;--fail-bg:rgba(239,68,68,.15);
  --accent:#3b82f6;
}
body{background:var(--bg);color:var(--text);font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',system-ui,sans-serif;line-height:1.5;padding:1.5rem;min-height:100vh}
a{color:var(--accent);text-decoration:none}

.header{text-align:center;margin-bottom:2rem}
.header h1{font-size:1.8rem;margin-bottom:.25rem;letter-spacing:-.02em}
.header .date{color:var(--text-dim);font-size:.85rem}
.stats{display:flex;justify-content:center;gap:2.5rem;margin:1.5rem 0;flex-wrap:wrap}
.stat{text-align:center}
.stat .num{font-size:2.2rem;font-weight:700}
.stat .lbl{font-size:.75rem;color:var(--text-dim);text-transform:uppercase;letter-spacing:.05em}
.stat.pass .num{color:var(--pass)}
.stat.fail .num{color:var(--fail)}
.progress-wrap{max-width:600px;margin:1rem auto}
.progress-bar{width:100%;height:12px;background:var(--surface);border-radius:6px;overflow:hidden}
.progress-bar .fill{height:100%;background:var(--pass);border-radius:6px;transition:width .3s}
.progress-text{text-align:center;font-size:.85rem;color:var(--text-dim);margin-top:.4rem}

.controls{display:flex;gap:.75rem;justify-content:center;flex-wrap:wrap;margin:1.5rem 0;align-items:center}
.btn-group{display:flex;border-radius:8px;overflow:hidden;border:1px solid var(--border)}
.btn-group button{padding:.45rem .9rem;border:none;background:var(--surface);color:var(--text);cursor:pointer;font-size:.82rem;transition:background .15s}
.btn-group button:hover{background:var(--surface2)}
.btn-group button.active{background:var(--accent);color:#fff}
select,input[type="text"]{padding:.45rem .9rem;border:1px solid var(--border);border-radius:8px;background:var(--surface);color:var(--text);font-size:.82rem;outline:none}
select:focus,input[type="text"]:focus{border-color:var(--accent)}
input[type="text"]{width:220px}
input[type="text"]::placeholder{color:var(--text-dim)}

.suite-section{margin-bottom:1.25rem}
.suite-header{display:flex;align-items:center;gap:.75rem;padding:.65rem 1rem;background:var(--surface);border:1px solid var(--border);border-radius:8px;cursor:pointer;user-select:none;margin-bottom:.65rem;transition:background .15s}
.suite-header:hover{background:var(--surface2)}
.suite-header .arrow{transition:transform .2s;font-size:.75rem;color:var(--text-dim)}
.suite-header .arrow.collapsed{transform:rotate(-90deg)}
.suite-header .name{font-weight:600;flex:1}
.suite-header .badge{font-size:.72rem;padding:.15rem .45rem;border-radius:4px;font-weight:600}
.suite-header .badge.pass{background:var(--pass-bg);color:var(--pass)}
.suite-header .badge.fail{background:var(--fail-bg);color:var(--fail)}
.suite-header .count{color:var(--text-dim);font-size:.78rem}

.card-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:.85rem}
.card-grid.hidden{display:none}
.card{background:var(--surface);border:1px solid var(--border);border-radius:8px;overflow:hidden;transition:transform .12s,box-shadow .12s}
.card:hover{transform:translateY(-2px);box-shadow:0 4px 16px rgba(0,0,0,.35)}
.card .thumb{width:100%;aspect-ratio:256/240;background:#000;cursor:pointer;display:block;position:relative;overflow:hidden}
.card .thumb img{width:100%;height:100%;object-fit:contain;image-rendering:pixelated;display:block}
.card .thumb .no-img{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;color:var(--text-dim);font-size:.75rem}
.card .info{padding:.65rem .75rem}
.card .status-badge{display:inline-block;font-size:.7rem;font-weight:700;padding:.12rem .45rem;border-radius:4px;text-transform:uppercase;letter-spacing:.03em}
.card .status-badge.pass{background:var(--pass-bg);color:var(--pass)}
.card .status-badge.fail{background:var(--fail-bg);color:var(--fail)}
.card .rom-name{font-weight:600;font-size:.85rem;margin-top:.35rem;word-break:break-word}
.card .suite-name{font-size:.72rem;color:var(--text-dim);margin-top:.1rem}
.card .expand-btn{font-size:.72rem;color:var(--accent);cursor:pointer;border:none;background:none;padding:.2rem 0;margin-top:.35rem}
.card .expand-btn:hover{text-decoration:underline}
.card .result-text{font-size:.68rem;font-family:Consolas,Monaco,'Courier New',monospace;color:var(--text-dim);white-space:pre-wrap;word-break:break-word;max-height:0;overflow:hidden;transition:max-height .25s ease}
.card .result-text.expanded{max-height:300px;overflow-y:auto;margin-top:.35rem;padding:.4rem;background:var(--bg);border-radius:4px}

.modal-overlay{display:none;position:fixed;inset:0;background:rgba(0,0,0,.88);z-index:1000;justify-content:center;align-items:center;flex-direction:column;cursor:pointer}
.modal-overlay.active{display:flex}
.modal-overlay img{max-width:min(90vw,768px);max-height:85vh;image-rendering:pixelated;border:2px solid var(--border);border-radius:4px}
.modal-overlay .modal-title{margin-top:1rem;background:var(--surface);padding:.4rem 1rem;border-radius:8px;font-size:.85rem;border:1px solid var(--border)}

.empty-msg{text-align:center;color:var(--text-dim);padding:3rem;font-size:.95rem}
.footer{text-align:center;color:var(--text-dim);font-size:.75rem;margin-top:2rem;padding-top:1rem;border-top:1px solid var(--border)}
</style>
</head>
<body>
<div class="header">
  <h1>AprNes Test Report</h1>
  <div class="date" id="build-date"></div>
</div>
<div class="stats">
  <div class="stat pass"><div class="num" id="pass-num">0</div><div class="lbl">Passed</div></div>
  <div class="stat fail"><div class="num" id="fail-num">0</div><div class="lbl">Failed</div></div>
  <div class="stat"><div class="num" id="total-num">0</div><div class="lbl">Total</div></div>
</div>
<div class="progress-wrap">
  <div class="progress-bar"><div class="fill" id="progress-fill"></div></div>
  <div class="progress-text" id="progress-text"></div>
</div>
<div class="controls">
  <div class="btn-group">
    <button class="filter-btn active" data-filter="all">All</button>
    <button class="filter-btn" data-filter="pass">Pass</button>
    <button class="filter-btn" data-filter="fail">Fail</button>
  </div>
  <select id="suite-select"><option value="all">All Suites</option></select>
  <input type="text" id="search" placeholder="Search ROM name...">
</div>
<div id="content"></div>
<div class="modal-overlay" id="modal">
  <img id="modal-img" src="" alt="">
  <div class="modal-title" id="modal-title"></div>
</div>
<div class="footer"><a href="methodology.html">Testing Methodology</a> | Generated by AprNes Test Runner</div>
<script>
HTML1

# ── Inject build date and JSON data ──
echo "var BUILD_DATE='$(date '+%Y-%m-%d %H:%M:%S')';"
echo "var RESULTS="
cat "$RESULTS_JSON"

# ── HTML Part 2: JavaScript logic + closing tags ──
cat << 'HTML2'
;
document.getElementById('build-date').textContent = BUILD_DATE;
var totalCount = RESULTS.length;
var passCount = RESULTS.filter(function(r){return r.status==='pass'}).length;
var failCount = totalCount - passCount;
var passRate = totalCount > 0 ? (passCount/totalCount*100).toFixed(1) : '0.0';
document.getElementById('total-num').textContent = totalCount;
document.getElementById('pass-num').textContent = passCount;
document.getElementById('fail-num').textContent = failCount;
document.getElementById('progress-fill').style.width = passRate + '%';
document.getElementById('progress-text').textContent = passCount + ' / ' + totalCount + ' (' + passRate + '%)';

var suiteSelect = document.getElementById('suite-select');
var searchInput = document.getElementById('search');
var content = document.getElementById('content');
var modal = document.getElementById('modal');
var modalImg = document.getElementById('modal-img');
var modalTitle = document.getElementById('modal-title');

var filter = 'all';
var suiteFilter = 'all';
var search = '';
var collapsed = {};

var suiteNames = [];
var seen = {};
RESULTS.forEach(function(r) {
  if (!seen[r.suite]) { seen[r.suite] = true; suiteNames.push(r.suite); }
});
suiteNames.sort();
suiteNames.forEach(function(s) {
  var opt = document.createElement('option');
  opt.value = s; opt.textContent = s;
  suiteSelect.appendChild(opt);
});

document.querySelectorAll('.filter-btn').forEach(function(btn) {
  btn.addEventListener('click', function() {
    document.querySelectorAll('.filter-btn').forEach(function(b){b.classList.remove('active')});
    btn.classList.add('active');
    filter = btn.dataset.filter;
    render();
  });
});
suiteSelect.addEventListener('change', function() { suiteFilter = suiteSelect.value; render(); });
searchInput.addEventListener('input', function() { search = searchInput.value.toLowerCase(); render(); });
modal.addEventListener('click', function() { modal.classList.remove('active'); });

function openModal(src, title) {
  modalImg.src = src;
  modalTitle.textContent = title;
  modal.classList.add('active');
}

function toggleSuite(suite) {
  collapsed[suite] = !collapsed[suite];
  render();
}

function toggleResult(id) {
  var el = document.getElementById(id);
  if (el) el.classList.toggle('expanded');
}

function esc(s) {
  var d = document.createElement('div');
  d.appendChild(document.createTextNode(s));
  return d.innerHTML;
}

function render() {
  var filtered = RESULTS.filter(function(r) {
    if (filter === 'pass' && r.status !== 'pass') return false;
    if (filter === 'fail' && r.status !== 'fail') return false;
    if (suiteFilter !== 'all' && r.suite !== suiteFilter) return false;
    if (search && r.rom.toLowerCase().indexOf(search) === -1 && r.suite.toLowerCase().indexOf(search) === -1) return false;
    return true;
  });

  var groups = {};
  filtered.forEach(function(r) {
    if (!groups[r.suite]) groups[r.suite] = [];
    groups[r.suite].push(r);
  });

  var sortedSuites = Object.keys(groups).sort();
  sortedSuites.forEach(function(s) {
    groups[s].sort(function(a, b) {
      if (a.status !== b.status) return a.status === 'fail' ? -1 : 1;
      return a.rom.localeCompare(b.rom);
    });
  });

  var html = '';
  sortedSuites.forEach(function(suite) {
    var tests = groups[suite];
    var sp = tests.filter(function(t){return t.status==='pass'}).length;
    var sf = tests.length - sp;
    var isCollapsed = collapsed[suite];
    var safeId = suite.replace(/[^a-zA-Z0-9]/g, '_');

    html += '<div class="suite-section">';
    html += '<div class="suite-header" onclick="toggleSuite(\'' + suite.replace(/'/g, "\\'") + '\')">';
    html += '<span class="arrow' + (isCollapsed ? ' collapsed' : '') + '">&#9660;</span>';
    html += '<span class="name">' + esc(suite) + '</span>';
    if (sf > 0) html += '<span class="badge fail">' + sf + ' fail</span>';
    html += '<span class="badge pass">' + sp + ' pass</span>';
    html += '<span class="count">' + tests.length + ' tests</span>';
    html += '</div>';
    html += '<div class="card-grid' + (isCollapsed ? ' hidden' : '') + '">';

    tests.forEach(function(t, i) {
      var rid = 'r_' + safeId + '_' + i;
      html += '<div class="card">';
      html += '<div class="thumb" onclick="openModal(\'' + t.screenshot.replace(/'/g, "\\'") + '\',\'' + esc(t.rom).replace(/'/g, "\\'") + '\')">';
      html += '<img src="' + esc(t.screenshot) + '" alt="' + esc(t.rom) + '" loading="lazy" onerror="this.style.display=\'none\';this.nextElementSibling.style.display=\'flex\'">';
      html += '<div class="no-img" style="display:none">No Screenshot</div>';
      html += '</div>';
      html += '<div class="info">';
      html += '<span class="status-badge ' + t.status + '">' + t.status + '</span>';
      html += '<div class="rom-name">' + esc(t.rom) + '</div>';
      html += '<div class="suite-name">' + esc(t.suite) + '</div>';
      if (t.result_text) {
        html += '<button class="expand-btn" onclick="toggleResult(\'' + rid + '\')">Details &#9656;</button>';
        html += '<div class="result-text" id="' + rid + '">' + esc(t.result_text) + '</div>';
      }
      html += '</div></div>';
    });

    html += '</div></div>';
  });

  if (!html) {
    html = '<div class="empty-msg">No matching tests found.</div>';
  }
  content.innerHTML = html;
}

render();
</script>
</body>
</html>
HTML2
} > "$REPORT_DIR/index.html"

echo ""
echo "=== Report generated ==="
echo "  HTML:        $REPORT_DIR/index.html"
echo "  JSON:        $RESULTS_JSON"
echo "  Screenshots: $SCREENSHOT_DIR/"
echo "  Results:     $PASS PASS / $FAIL FAIL / $TOTAL TOTAL"
