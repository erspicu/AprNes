#!/bin/bash
cd /c/ai_project/AprNes
EXE="AprNes/bin/Debug/AprNes.exe"
ROMBASE="nes-test-roms-master/checked"
PASS=0
FAIL=0
TOTAL=0
FAILURES=""

run_test() {
    local suite="$1"
    local rom="$2"
    local extra_args="$3"
    local rompath="$ROMBASE/$suite/$rom"

    if [ ! -f "$rompath" ]; then
        echo "SKIP: $rompath not found"
        return
    fi

    TOTAL=$((TOTAL+1))
    output=$("$EXE" --rom "$rompath" --wait-result $extra_args 2>&1)
    rc=$?
    if [ $rc -eq 0 ]; then
        PASS=$((PASS+1))
        echo "PASS: $suite/$rom"
    else
        FAIL=$((FAIL+1))
        short=$(echo "$output" | head -3 | tr '\n' ' ')
        FAILURES="${FAILURES}FAIL($rc): $suite/$rom -- $short
"
        echo "FAIL($rc): $suite/$rom -- $short"
    fi
}

echo "=== Starting test run ==="

# apu_mixer (4)
for rom in dmc.nes noise.nes square.nes triangle.nes; do
    run_test "apu_mixer" "$rom" "--max-wait 30"
done

# apu_reset (6)
for rom in 4015_cleared.nes 4017_timing.nes 4017_written.nes irq_flag_cleared.nes len_ctrs_enabled.nes works_immediately.nes; do
    run_test "apu_reset" "$rom" "--max-wait 30"
done

# apu_test merged + singles (9)
run_test "apu_test" "apu_test.nes" "--max-wait 120"
for rom in 1-len_ctr.nes 2-len_table.nes 3-irq_flag.nes 4-jitter.nes 5-len_timing.nes 6-irq_flag_timing.nes 7-dmc_basics.nes 8-dmc_rates.nes; do
    run_test "apu_test/rom_singles" "$rom" "--max-wait 30"
done

# blargg_apu_2005.07.30 (11)
for rom in 01.len_ctr.nes 02.len_table.nes 03.irq_flag.nes 04.clock_jitter.nes 05.len_timing_mode0.nes 06.len_timing_mode1.nes 07.irq_flag_timing.nes 08.irq_timing.nes 09.reset_timing.nes 10.len_halt_timing.nes 11.len_reload_timing.nes; do
    run_test "blargg_apu_2005.07.30" "$rom" "--max-wait 30"
done

# blargg_nes_cpu_test5 (2)
for rom in cpu.nes official.nes; do
    run_test "blargg_nes_cpu_test5" "$rom" "--max-wait 120"
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
run_test "cpu_interrupts_v2" "cpu_interrupts.nes" "--max-wait 120"
for rom in 1-cli_latency.nes 2-nmi_and_brk.nes 3-nmi_and_irq.nes 4-irq_and_dma.nes 5-branch_delays_irq.nes; do
    run_test "cpu_interrupts_v2/rom_singles" "$rom" "--max-wait 30"
done

# cpu_reset (2)
for rom in registers.nes ram_after_reset.nes; do
    run_test "cpu_reset" "$rom" "--max-wait 30"
done

# cpu_timing_test6 (1)
run_test "cpu_timing_test6" "cpu_timing_test.nes" "--max-wait 120"

# dmc_dma_during_read4 (5)
for rom in dma_2007_read.nes dma_2007_write.nes dma_4016_read.nes double_2007_read.nes read_write_2007.nes; do
    run_test "dmc_dma_during_read4" "$rom" "--max-wait 30"
done

# instr_misc merged + singles (5)
run_test "instr_misc" "instr_misc.nes" "--max-wait 120"
for rom in 01-abs_x_wrap.nes 02-branch_wrap.nes 03-dummy_reads.nes 04-dummy_reads_apu.nes; do
    run_test "instr_misc/rom_singles" "$rom" "--max-wait 30"
done

# instr_test-v3 (17)
run_test "instr_test-v3" "all_instrs.nes" "--max-wait 120"
run_test "instr_test-v3" "official_only.nes" "--max-wait 120"
for rom in 01-implied.nes 02-immediate.nes 03-zero_page.nes 04-zp_xy.nes 05-absolute.nes 06-abs_xy.nes 07-ind_x.nes 08-ind_y.nes 09-branches.nes 10-stack.nes 11-jmp_jsr.nes 12-rts.nes 13-rti.nes 14-brk.nes 15-special.nes; do
    run_test "instr_test-v3/rom_singles" "$rom" "--max-wait 30"
done

# instr_test-v5 (18)
run_test "instr_test-v5" "all_instrs.nes" "--max-wait 120"
run_test "instr_test-v5" "official_only.nes" "--max-wait 120"
for rom in 01-basics.nes 02-implied.nes 03-immediate.nes 04-zero_page.nes 05-zp_xy.nes 06-absolute.nes 07-abs_xy.nes 08-ind_x.nes 09-ind_y.nes 10-branches.nes 11-stack.nes 12-jmp_jsr.nes 13-rts.nes 14-rti.nes 15-brk.nes 16-special.nes; do
    run_test "instr_test-v5/rom_singles" "$rom" "--max-wait 30"
done

# instr_timing (3)
run_test "instr_timing" "instr_timing.nes" "--max-wait 120"
for rom in 1-instr_timing.nes 2-branch_timing.nes; do
    run_test "instr_timing/rom_singles" "$rom" "--max-wait 30"
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

# ppu_vbl_nmi merged + singles (10)  -- note: merged is in root, singles in rom_singles/
run_test "ppu_vbl_nmi" "ppu_vbl_nmi.nes" "--max-wait 120"
for rom in 01-vbl_basics.nes 02-vbl_set_time.nes 03-vbl_clear_time.nes 04-nmi_control.nes 05-nmi_timing.nes 06-suppression.nes 07-nmi_on_timing.nes 08-nmi_off_timing.nes 09-even_odd_frames.nes 10-even_odd_timing.nes; do
    run_test "ppu_vbl_nmi/rom_singles" "$rom" "--max-wait 30"
done

# read_joy3 (4)
run_test "read_joy3" "test_buttons.nes" "--max-wait 60 --input A:2.0,B:4.0,Select:6.0,Start:8.0,Up:10.0,Down:12.0,Left:14.0,Right:16.0"
for rom in count_errors.nes count_errors_fast.nes; do
    run_test "read_joy3" "$rom" "--max-wait 30"
done
run_test "read_joy3" "thorough_test.nes" "--max-wait 45"

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

echo ""
echo "=== FINAL RESULTS ==="
echo "PASS: $PASS / TOTAL: $TOTAL / FAIL: $FAIL"
echo ""
echo "=== ALL FAILURES ==="
echo "$FAILURES"
