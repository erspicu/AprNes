#!/usr/bin/env python3
"""Run all 174 NES test ROMs with parallel execution (8 threads).
Usage:
    python run_tests.py          # 8 threads (default)
    python run_tests.py -j 4     # 4 threads
    python run_tests.py -j 1     # sequential
"""
import subprocess, os, sys, time, argparse
from concurrent.futures import ThreadPoolExecutor, as_completed

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
EXE = os.path.join(SCRIPT_DIR, "AprNes", "bin", "Debug", "AprNes.exe")
ROMBASE = os.path.join(SCRIPT_DIR, "nes-test-roms-master", "checked")

def build_test_list():
    """Return list of (suite, rom, extra_args_str) tuples."""
    tests = []
    def a(suite, rom, extra="--max-wait 8"):
        tests.append((suite, rom, extra))

    W = "--max-wait 20"       # singles: complete in <3s, 20s is generous
    WM = "--max-wait 40"      # merged ROMs: up to 6s solo, need headroom under parallel load

    for r in ["dmc.nes","noise.nes","square.nes","triangle.nes"]:
        a("apu_mixer", r, W)
    for r in ["4015_cleared.nes","4017_timing.nes","4017_written.nes","irq_flag_cleared.nes","len_ctrs_enabled.nes","works_immediately.nes"]:
        a("apu_reset", r, W)
    a("apu_test", "apu_test.nes", W)
    for r in ["1-len_ctr.nes","2-len_table.nes","3-irq_flag.nes","4-jitter.nes","5-len_timing.nes","6-irq_flag_timing.nes","7-dmc_basics.nes","8-dmc_rates.nes"]:
        a("apu_test/rom_singles", r, W)
    for r in ["01.len_ctr.nes","02.len_table.nes","03.irq_flag.nes","04.clock_jitter.nes","05.len_timing_mode0.nes","06.len_timing_mode1.nes","07.irq_flag_timing.nes","08.irq_timing.nes","09.reset_timing.nes","10.len_halt_timing.nes","11.len_reload_timing.nes"]:
        a("blargg_apu_2005.07.30", r, W)
    for r in ["cpu.nes","official.nes"]:
        a("blargg_nes_cpu_test5", r, WM)
    for r in ["palette_ram.nes","power_up_palette.nes","sprite_ram.nes","vbl_clear_time.nes","vram_access.nes"]:
        a("blargg_ppu_tests_2005.09.15b", r, W)
    for r in ["1.Branch_Basics.nes","2.Backward_Branch.nes","3.Forward_Branch.nes"]:
        a("branch_timing_tests", r, W)
    a("cpu_dummy_reads", "cpu_dummy_reads.nes", W)
    for r in ["cpu_dummy_writes_oam.nes","cpu_dummy_writes_ppumem.nes"]:
        a("cpu_dummy_writes", r, W)
    for r in ["test_cpu_exec_space_ppuio.nes","test_cpu_exec_space_apu.nes"]:
        a("cpu_exec_space", r, W)
    a("cpu_interrupts_v2", "cpu_interrupts.nes", WM)
    for r in ["1-cli_latency.nes","2-nmi_and_brk.nes","3-nmi_and_irq.nes","4-irq_and_dma.nes","5-branch_delays_irq.nes"]:
        a("cpu_interrupts_v2/rom_singles", r, W)
    for r in ["registers.nes","ram_after_reset.nes"]:
        a("cpu_reset", r, W)
    a("cpu_timing_test6", "cpu_timing_test.nes", WM)
    a("dmc_dma_during_read4", "dma_2007_read.nes", W + " --expected-crc 159A7A8F,5E3DF9C4")
    a("dmc_dma_during_read4", "dma_2007_write.nes", W)
    a("dmc_dma_during_read4", "dma_4016_read.nes", W)
    a("dmc_dma_during_read4", "double_2007_read.nes", W + " --expected-crc 85CFD627,F018C287,440EF923,E52F41A5")
    a("dmc_dma_during_read4", "read_write_2007.nes", W)
    a("instr_misc", "instr_misc.nes", W)
    for r in ["01-abs_x_wrap.nes","02-branch_wrap.nes","03-dummy_reads.nes","04-dummy_reads_apu.nes"]:
        a("instr_misc/rom_singles", r, W)
    a("instr_test-v3", "all_instrs.nes", WM)
    a("instr_test-v3", "official_only.nes", WM)
    for r in ["01-implied.nes","02-immediate.nes","03-zero_page.nes","04-zp_xy.nes","05-absolute.nes","06-abs_xy.nes","07-ind_x.nes","08-ind_y.nes","09-branches.nes","10-stack.nes","11-jmp_jsr.nes","12-rts.nes","13-rti.nes","14-brk.nes","15-special.nes"]:
        a("instr_test-v3/rom_singles", r, W)
    a("instr_test-v5", "all_instrs.nes", WM)
    a("instr_test-v5", "official_only.nes", WM)
    for r in ["01-basics.nes","02-implied.nes","03-immediate.nes","04-zero_page.nes","05-zp_xy.nes","06-absolute.nes","07-abs_xy.nes","08-ind_x.nes","09-ind_y.nes","10-branches.nes","11-stack.nes","12-jmp_jsr.nes","13-rts.nes","14-rti.nes","15-brk.nes","16-special.nes"]:
        a("instr_test-v5/rom_singles", r, W)
    a("instr_timing", "instr_timing.nes", WM)
    for r in ["1-instr_timing.nes","2-branch_timing.nes"]:
        a("instr_timing/rom_singles", r, W)
    for r in ["1.Clocking.nes","2.Details.nes","3.A12_clocking.nes","4.Scanline_timing.nes","5.MMC3_rev_A.nes","6.MMC3_rev_B.nes"]:
        a("mmc3_irq_tests", r, W)
    for r in ["1-clocking.nes","2-details.nes","3-A12_clocking.nes","4-scanline_timing.nes","5-MMC3.nes","6-MMC6.nes"]:
        a("mmc3_test", r, W)
    for r in ["1-clocking.nes","2-details.nes","3-A12_clocking.nes","4-scanline_timing.nes","5-MMC3.nes","6-MMC3_alt.nes"]:
        a("mmc3_test_2/rom_singles", r, W)
    for r in ["01-implied.nes","02-immediate.nes","03-zero_page.nes","04-zp_xy.nes","05-absolute.nes","06-abs_xy.nes","07-ind_x.nes","08-ind_y.nes","09-branches.nes","10-stack.nes","11-special.nes"]:
        a("nes_instr_test/rom_singles", r, W)
    a("oam_read", "oam_read.nes", W)
    a("ppu_open_bus", "ppu_open_bus.nes", W)
    a("ppu_read_buffer", "test_ppu_read_buffer.nes", WM)
    a("ppu_vbl_nmi", "ppu_vbl_nmi.nes", WM)
    for r in ["01-vbl_basics.nes","02-vbl_set_time.nes","03-vbl_clear_time.nes","04-nmi_control.nes","05-nmi_timing.nes","06-suppression.nes","07-nmi_on_timing.nes","08-nmi_off_timing.nes","09-even_odd_frames.nes","10-even_odd_timing.nes"]:
        a("ppu_vbl_nmi/rom_singles", r, W)
    a("read_joy3", "test_buttons.nes", "--max-wait 25 --input A:2.0,B:4.0,Select:6.0,Start:8.0,Up:10.0,Down:12.0,Left:14.0,Right:16.0")
    for r in ["count_errors.nes","count_errors_fast.nes"]:
        a("read_joy3", r, W + " --pass-on-stable")
    a("read_joy3", "thorough_test.nes", W)
    for r in ["sprdma_and_dmc_dma.nes","sprdma_and_dmc_dma_512.nes"]:
        a("sprdma_and_dmc_dma", r, W)
    for r in ["01.basics.nes","02.alignment.nes","03.corners.nes","04.flip.nes","05.left_clip.nes","06.right_edge.nes","07.screen_bottom.nes","08.double_height.nes","09.timing_basics.nes","10.timing_order.nes","11.edge_timing.nes"]:
        a("sprite_hit_tests_2005.10.05", r, W)
    for r in ["1.Basics.nes","2.Details.nes","3.Timing.nes","4.Obscure.nes","5.Emulator.nes"]:
        a("sprite_overflow_tests", r, W)
    for r in ["1.frame_basics.nes","2.vbl_timing.nes","3.even_odd_frames.nes","4.vbl_clear_timing.nes","5.nmi_suppression.nes","6.nmi_disable.nes","7.nmi_timing.nes"]:
        a("vbl_nmi_timing", r, W)

    return tests


def run_one(idx, suite, rom, extra, exe, rombase):
    """Run a single test ROM. Returns (idx, status, name, detail)."""
    name = f"{suite}/{rom}"
    rompath = os.path.join(rombase, suite, rom)
    if not os.path.isfile(rompath):
        return (idx, "SKIP", name, "")

    cmd = [exe, "--rom", rompath, "--wait-result"] + extra.split()
    try:
        proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        stdout_b, stderr_b = proc.communicate(timeout=30)
        stdout_s = stdout_b.decode("utf-8", errors="replace")
        if proc.returncode == 0:
            return (idx, "PASS", name, "")
        else:
            detail = " ".join(stdout_s.split("\n")[:3])
            return (idx, "FAIL", name, f"rc={proc.returncode} {detail}")
    except subprocess.TimeoutExpired:
        proc.kill()
        proc.communicate()
        return (idx, "FAIL", name, "TIMEOUT")
    except Exception as e:
        return (idx, "FAIL", name, str(e))


def main():
    parser = argparse.ArgumentParser(description="Run NES test ROMs")
    parser.add_argument("-j", "--jobs", type=int, default=10, help="Parallel threads (default: 10)")
    args = parser.parse_args()

    os.chdir(SCRIPT_DIR)

    tests = build_test_list()
    total = len(tests)
    jobs = args.jobs

    print(f"=== Starting test run ({total} tests, {jobs} threads) ===")
    start_time = time.time()

    results = [None] * total
    pass_count = 0
    fail_count = 0
    skip_count = 0
    failures = []

    with ThreadPoolExecutor(max_workers=jobs) as executor:
        futures = {}
        for i, (suite, rom, extra) in enumerate(tests):
            fut = executor.submit(run_one, i, suite, rom, extra, EXE, ROMBASE)
            futures[fut] = i

        for fut in as_completed(futures):
            idx, status, name, detail = fut.result()
            results[idx] = (status, name, detail)

    # Print results in order
    for status, name, detail in results:
        if status == "PASS":
            pass_count += 1
            print(f"PASS: {name}")
        elif status == "SKIP":
            skip_count += 1
            print(f"SKIP: {name}")
        else:
            fail_count += 1
            line = f"FAIL: {name} -- {detail}"
            print(line)
            failures.append(line)

    elapsed = time.time() - start_time
    print()
    print("=== FINAL RESULTS ===")
    print(f"PASS: {pass_count} / TOTAL: {total} / FAIL: {fail_count} / SKIP: {skip_count}")
    print(f"Time: {elapsed:.1f}s")
    print()
    if failures:
        print("=== ALL FAILURES ===")
        for f in failures:
            print(f)


if __name__ == "__main__":
    main()
