#!/usr/bin/env python3
"""
Universal NES Emulator Test Runner (CLI Mode)

Runs blargg test ROMs against any emulator that supports:
    <emu.exe> --rom <path> --wait-result --max-wait <sec> [--region <NTSC|PAL>]
    Exit code 0 = PASS, non-zero = FAIL

Usage:
    python run_tests.py --exe <emulator>                    # run all 184 tests
    python run_tests.py --exe <emulator> -j 4               # 4 parallel threads
    python run_tests.py --exe <emulator> --ntsc-only        # skip PAL tests
    python run_tests.py --exe <emulator> --suite apu_mixer  # run one suite only
"""
import subprocess, os, sys, time, argparse, json
from concurrent.futures import ThreadPoolExecutor, as_completed

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ROMBASE = os.path.join(SCRIPT_DIR, "roms")
CATALOG = None  # loaded on demand

def load_catalog():
    """Load test catalog from test_catalog.json or build inline."""
    # Inline catalog matching NesTestFramework.TestCatalog
    tests = []
    def a(suite, rom, max_wait=15, region="NTSC", input_spec=None, crcs=None, pass_on_stable=False):
        tests.append({"suite": suite, "rom": rom, "max_wait": max_wait,
                       "region": region, "input": input_spec, "crcs": crcs,
                       "pass_on_stable": pass_on_stable})

    W = 30; WM = 50

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
    a("dmc_dma_during_read4", "dma_2007_read.nes", W, crcs=["159A7A8F","5E3DF9C4"])
    a("dmc_dma_during_read4", "dma_2007_write.nes", W)
    a("dmc_dma_during_read4", "dma_4016_read.nes", W)
    a("dmc_dma_during_read4", "double_2007_read.nes", W, crcs=["85CFD627","F018C287","440EF923","E52F41A5"])
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
    a("read_joy3", "test_buttons.nes", 25, input_spec="A:2.0,B:4.0,Select:6.0,Start:8.0,Up:10.0,Down:12.0,Left:14.0,Right:16.0")
    for r in ["count_errors.nes","count_errors_fast.nes"]:
        a("read_joy3", r, W, pass_on_stable=True)
    a("read_joy3", "thorough_test.nes", W)
    for r in ["sprdma_and_dmc_dma.nes","sprdma_and_dmc_dma_512.nes"]:
        a("sprdma_and_dmc_dma", r, W)
    for r in ["01.basics.nes","02.alignment.nes","03.corners.nes","04.flip.nes","05.left_clip.nes","06.right_edge.nes","07.screen_bottom.nes","08.double_height.nes","09.timing_basics.nes","10.timing_order.nes","11.edge_timing.nes"]:
        a("sprite_hit_tests_2005.10.05", r, W)
    for r in ["1.Basics.nes","2.Details.nes","3.Timing.nes","4.Obscure.nes","5.Emulator.nes"]:
        a("sprite_overflow_tests", r, W)
    for r in ["1.frame_basics.nes","2.vbl_timing.nes","3.even_odd_frames.nes","4.vbl_clear_timing.nes","5.nmi_suppression.nes","6.nmi_disable.nes","7.nmi_timing.nes"]:
        a("vbl_nmi_timing", r, W)
    # PAL
    for r in ["01.len_ctr.nes","02.len_table.nes","03.irq_flag.nes","04.clock_jitter.nes","05.len_timing_mode0.nes","06.len_timing_mode1.nes","07.irq_flag_timing.nes","08.irq_timing.nes","10.len_halt_timing.nes","11.len_reload_timing.nes"]:
        a("pal_apu_tests", r, W, "PAL")

    return tests


def run_one(idx, test, exe, rombase):
    name = f"{test['suite']}/{test['rom']}"
    rompath = os.path.join(rombase, test['suite'], test['rom'])
    if not os.path.isfile(rompath):
        return (idx, "SKIP", name, "")

    cmd = [exe, "--rom", rompath, "--wait-result", "--max-wait", str(test['max_wait'])]
    if test['region'] != "NTSC":
        cmd += ["--region", test['region']]
    if test.get('input'):
        cmd += ["--input", test['input']]
    if test.get('crcs'):
        cmd += ["--expected-crc", ",".join(test['crcs'])]
    if test.get('pass_on_stable'):
        cmd += ["--pass-on-stable"]

    try:
        proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        stdout_b, _ = proc.communicate(timeout=60)
        stdout_s = stdout_b.decode("utf-8", errors="replace")
        if proc.returncode == 0:
            return (idx, "PASS", name, "")
        else:
            detail = " ".join(stdout_s.split("\n")[:3])
            return (idx, "FAIL", name, f"rc={proc.returncode} {detail}")
    except subprocess.TimeoutExpired:
        proc.kill(); proc.communicate()
        return (idx, "FAIL", name, "TIMEOUT")
    except Exception as e:
        return (idx, "FAIL", name, str(e))


def main():
    parser = argparse.ArgumentParser(description="Universal NES Emulator Test Runner")
    parser.add_argument("--exe", required=True, help="Path to emulator executable")
    parser.add_argument("--rom-dir", default=ROMBASE, help=f"ROM directory (default: {ROMBASE})")
    parser.add_argument("-j", "--jobs", type=int, default=6, help="Parallel threads (default: 6)")
    parser.add_argument("--ntsc-only", action="store_true", help="Skip PAL tests")
    parser.add_argument("--suite", type=str, default=None, help="Run only a specific suite")
    parser.add_argument("--json", action="store_true", help="Output JSON results")
    args = parser.parse_args()

    tests = load_catalog()
    if args.ntsc_only:
        tests = [t for t in tests if t['region'] == 'NTSC']
    if args.suite:
        tests = [t for t in tests if t['suite'] == args.suite]

    total = len(tests)
    print(f"=== NES Test Framework: {total} tests, {args.jobs} threads ===")
    start = time.time()

    results = [None] * total
    pass_c = fail_c = skip_c = 0

    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        futures = {pool.submit(run_one, i, tests[i], args.exe, args.rom_dir): i for i in range(total)}
        for fut in as_completed(futures):
            idx, status, name, detail = fut.result()
            results[idx] = (status, name, detail)
            if status == "PASS": pass_c += 1
            elif status == "FAIL": fail_c += 1
            else: skip_c += 1
            if not args.json:
                print(f"{status}: {name}" + (f" -- {detail}" if detail else ""))

    elapsed = time.time() - start

    if args.json:
        json_results = [{"status": r[0], "name": r[1], "detail": r[2]} for r in results if r]
        print(json.dumps({"pass": pass_c, "fail": fail_c, "skip": skip_c,
                           "total": total, "time": round(elapsed, 1), "results": json_results}, indent=2))
    else:
        print(f"\n=== FINAL RESULTS ===")
        print(f"PASS: {pass_c} / TOTAL: {total} / FAIL: {fail_c} / SKIP: {skip_c}")
        print(f"Time: {elapsed:.1f}s")
        if fail_c > 0:
            print(f"\n=== ALL FAILURES ===")
            for r in results:
                if r and r[0] == "FAIL":
                    print(f"FAIL: {r[1]} -- {r[2]}")

    sys.exit(0 if fail_c == 0 else 1)


if __name__ == "__main__":
    main()
