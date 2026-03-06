#!/bin/bash
# Run all 174 NES test ROMs. Supports parallel execution.
# Usage:
#   bash run_tests.sh          # parallel (default: 4 jobs)
#   bash run_tests.sh -j 1     # sequential
cd /c/ai_project/AprNes
EXE="AprNes/bin/Debug/AprNes.exe"
ROMBASE="nes-test-roms-master/checked"

JOBS=4
while getopts "j:" opt; do
    case $opt in j) JOBS=$OPTARG ;; esac
done

TMPDIR=$(mktemp -d)
trap "rm -rf $TMPDIR" EXIT

# Write test list
TL="$TMPDIR/tests.txt"
a() { echo "$1|$2|$3" >> "$TL"; }

for r in dmc.nes noise.nes square.nes triangle.nes; do a "apu_mixer" "$r" "--max-wait 15"; done
for r in 4015_cleared.nes 4017_timing.nes 4017_written.nes irq_flag_cleared.nes len_ctrs_enabled.nes works_immediately.nes; do a "apu_reset" "$r" "--max-wait 15"; done
a "apu_test" "apu_test.nes" "--max-wait 15"
for r in 1-len_ctr.nes 2-len_table.nes 3-irq_flag.nes 4-jitter.nes 5-len_timing.nes 6-irq_flag_timing.nes 7-dmc_basics.nes 8-dmc_rates.nes; do a "apu_test/rom_singles" "$r" "--max-wait 15"; done
for r in 01.len_ctr.nes 02.len_table.nes 03.irq_flag.nes 04.clock_jitter.nes 05.len_timing_mode0.nes 06.len_timing_mode1.nes 07.irq_flag_timing.nes 08.irq_timing.nes 09.reset_timing.nes 10.len_halt_timing.nes 11.len_reload_timing.nes; do a "blargg_apu_2005.07.30" "$r" "--max-wait 15"; done
for r in cpu.nes official.nes; do a "blargg_nes_cpu_test5" "$r" "--max-wait 20"; done
for r in palette_ram.nes power_up_palette.nes sprite_ram.nes vbl_clear_time.nes vram_access.nes; do a "blargg_ppu_tests_2005.09.15b" "$r" "--max-wait 15"; done
for r in "1.Branch_Basics.nes" "2.Backward_Branch.nes" "3.Forward_Branch.nes"; do a "branch_timing_tests" "$r" "--max-wait 15"; done
a "cpu_dummy_reads" "cpu_dummy_reads.nes" "--max-wait 15"
for r in cpu_dummy_writes_oam.nes cpu_dummy_writes_ppumem.nes; do a "cpu_dummy_writes" "$r" "--max-wait 15"; done
for r in test_cpu_exec_space_ppuio.nes test_cpu_exec_space_apu.nes; do a "cpu_exec_space" "$r" "--max-wait 15"; done
a "cpu_interrupts_v2" "cpu_interrupts.nes" "--max-wait 20"
for r in 1-cli_latency.nes 2-nmi_and_brk.nes 3-nmi_and_irq.nes 4-irq_and_dma.nes 5-branch_delays_irq.nes; do a "cpu_interrupts_v2/rom_singles" "$r" "--max-wait 15"; done
for r in registers.nes ram_after_reset.nes; do a "cpu_reset" "$r" "--max-wait 15"; done
a "cpu_timing_test6" "cpu_timing_test.nes" "--max-wait 20"
a "dmc_dma_during_read4" "dma_2007_read.nes" "--max-wait 15 --expected-crc 159A7A8F,5E3DF9C4"
a "dmc_dma_during_read4" "dma_2007_write.nes" "--max-wait 15"
a "dmc_dma_during_read4" "dma_4016_read.nes" "--max-wait 15"
a "dmc_dma_during_read4" "double_2007_read.nes" "--max-wait 15 --expected-crc 85CFD627,F018C287,440EF923,E52F41A5"
a "dmc_dma_during_read4" "read_write_2007.nes" "--max-wait 15"
a "instr_misc" "instr_misc.nes" "--max-wait 15"
for r in 01-abs_x_wrap.nes 02-branch_wrap.nes 03-dummy_reads.nes 04-dummy_reads_apu.nes; do a "instr_misc/rom_singles" "$r" "--max-wait 15"; done
a "instr_test-v3" "all_instrs.nes" "--max-wait 20"
a "instr_test-v3" "official_only.nes" "--max-wait 20"
for r in 01-implied.nes 02-immediate.nes 03-zero_page.nes 04-zp_xy.nes 05-absolute.nes 06-abs_xy.nes 07-ind_x.nes 08-ind_y.nes 09-branches.nes 10-stack.nes 11-jmp_jsr.nes 12-rts.nes 13-rti.nes 14-brk.nes 15-special.nes; do a "instr_test-v3/rom_singles" "$r" "--max-wait 15"; done
a "instr_test-v5" "all_instrs.nes" "--max-wait 20"
a "instr_test-v5" "official_only.nes" "--max-wait 20"
for r in 01-basics.nes 02-implied.nes 03-immediate.nes 04-zero_page.nes 05-zp_xy.nes 06-absolute.nes 07-abs_xy.nes 08-ind_x.nes 09-ind_y.nes 10-branches.nes 11-stack.nes 12-jmp_jsr.nes 13-rts.nes 14-rti.nes 15-brk.nes 16-special.nes; do a "instr_test-v5/rom_singles" "$r" "--max-wait 15"; done
a "instr_timing" "instr_timing.nes" "--max-wait 20"
for r in 1-instr_timing.nes 2-branch_timing.nes; do a "instr_timing/rom_singles" "$r" "--max-wait 15"; done
for r in 1.Clocking.nes 2.Details.nes 3.A12_clocking.nes 4.Scanline_timing.nes 5.MMC3_rev_A.nes 6.MMC3_rev_B.nes; do a "mmc3_irq_tests" "$r" "--max-wait 15"; done
for r in 1-clocking.nes 2-details.nes 3-A12_clocking.nes 4-scanline_timing.nes 5-MMC3.nes 6-MMC6.nes; do a "mmc3_test" "$r" "--max-wait 15"; done
for r in 1-clocking.nes 2-details.nes 3-A12_clocking.nes 4-scanline_timing.nes 5-MMC3.nes 6-MMC3_alt.nes; do a "mmc3_test_2/rom_singles" "$r" "--max-wait 15"; done
for r in 01-implied.nes 02-immediate.nes 03-zero_page.nes 04-zp_xy.nes 05-absolute.nes 06-abs_xy.nes 07-ind_x.nes 08-ind_y.nes 09-branches.nes 10-stack.nes 11-special.nes; do a "nes_instr_test/rom_singles" "$r" "--max-wait 15"; done
a "oam_read" "oam_read.nes" "--max-wait 15"
a "ppu_open_bus" "ppu_open_bus.nes" "--max-wait 15"
a "ppu_read_buffer" "test_ppu_read_buffer.nes" "--max-wait 15"
a "ppu_vbl_nmi" "ppu_vbl_nmi.nes" "--max-wait 20"
for r in 01-vbl_basics.nes 02-vbl_set_time.nes 03-vbl_clear_time.nes 04-nmi_control.nes 05-nmi_timing.nes 06-suppression.nes 07-nmi_on_timing.nes 08-nmi_off_timing.nes 09-even_odd_frames.nes 10-even_odd_timing.nes; do a "ppu_vbl_nmi/rom_singles" "$r" "--max-wait 15"; done
a "read_joy3" "test_buttons.nes" "--max-wait 25 --input A:2.0,B:4.0,Select:6.0,Start:8.0,Up:10.0,Down:12.0,Left:14.0,Right:16.0"
for r in count_errors.nes count_errors_fast.nes; do a "read_joy3" "$r" "--max-wait 15 --pass-on-stable"; done
a "read_joy3" "thorough_test.nes" "--max-wait 15"
for r in sprdma_and_dmc_dma.nes sprdma_and_dmc_dma_512.nes; do a "sprdma_and_dmc_dma" "$r" "--max-wait 15"; done
for r in 01.basics.nes 02.alignment.nes 03.corners.nes 04.flip.nes 05.left_clip.nes 06.right_edge.nes 07.screen_bottom.nes 08.double_height.nes 09.timing_basics.nes 10.timing_order.nes 11.edge_timing.nes; do a "sprite_hit_tests_2005.10.05" "$r" "--max-wait 15"; done
for r in 1.Basics.nes 2.Details.nes 3.Timing.nes 4.Obscure.nes 5.Emulator.nes; do a "sprite_overflow_tests" "$r" "--max-wait 15"; done
for r in 1.frame_basics.nes 2.vbl_timing.nes 3.even_odd_frames.nes 4.vbl_clear_timing.nes 5.nmi_suppression.nes 6.nmi_disable.nes 7.nmi_timing.nes; do a "vbl_nmi_timing" "$r" "--max-wait 15"; done

TOTAL=$(wc -l < "$TL")
echo "=== Starting test run ($TOTAL tests, $JOBS parallel jobs) ==="

# Write a worker script that the parallel executor calls
cat > "$TMPDIR/worker.sh" << 'WORKER'
#!/bin/bash
idx="$1"; line="$2"; EXE="$3"; ROMBASE="$4"; TMPDIR="$5"
IFS='|' read -r suite rom extra <<< "$line"
rompath="$ROMBASE/$suite/$rom"
if [ ! -f "$rompath" ]; then
    echo "SKIP|$suite/$rom||" > "$TMPDIR/r_$idx"
    exit 0
fi
output=$("$EXE" --rom "$rompath" --wait-result $extra 2>&1)
rc=$?
if [ $rc -eq 0 ]; then
    echo "PASS|$suite/$rom||" > "$TMPDIR/r_$idx"
else
    detail=$(echo "$output" | head -3 | tr '\n' ' ')
    echo "FAIL|$suite/$rom|$rc|$detail" > "$TMPDIR/r_$idx"
fi
WORKER
chmod +x "$TMPDIR/worker.sh"

# Launch in batches
idx=0
while IFS= read -r line; do
    bash "$TMPDIR/worker.sh" "$idx" "$line" "$EXE" "$ROMBASE" "$TMPDIR" &
    idx=$((idx+1))
    if [ $((idx % JOBS)) -eq 0 ]; then wait; fi
done < "$TL"
wait

# Collect
PASS=0; FAIL=0; FAILURES=""
for ((i=0; i<TOTAL; i++)); do
    IFS='|' read -r st name rc detail < "$TMPDIR/r_$i"
    case "$st" in
        PASS) PASS=$((PASS+1)); echo "PASS: $name" ;;
        SKIP) echo "SKIP: $name" ;;
        FAIL) FAIL=$((FAIL+1)); echo "FAIL($rc): $name -- $detail"
              FAILURES="${FAILURES}FAIL($rc): $name -- $detail
" ;;
        *) FAIL=$((FAIL+1)); echo "ERROR: test $i no result" ;;
    esac
done

echo ""
echo "=== FINAL RESULTS ==="
echo "PASS: $PASS / TOTAL: $TOTAL / FAIL: $FAIL"
echo ""
echo "=== ALL FAILURES ==="
echo "$FAILURES"
