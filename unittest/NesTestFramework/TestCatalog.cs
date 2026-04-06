using System.Collections.Generic;

namespace NesTestFramework
{
    public class TestDefinition
    {
        public string Suite;           // e.g. "apu_mixer"
        public string Rom;             // e.g. "dmc.nes"
        public int MaxWaitSeconds;     // timeout in seconds
        public NesRegion Region;       // NTSC or PAL
        public string InputSpec;       // null or "A:2.0,B:4.0,..." (timed joypad input)
        public string[] ExpectedCrcs;  // null or list of valid CRCs (for visual-output tests)
        public bool PassOnStable;      // true = pass if screen becomes stable (error counter tests)

        public TestDefinition(string suite, string rom, int maxWait = 15,
            NesRegion region = NesRegion.NTSC, string input = null,
            string[] crcs = null, bool passOnStable = false)
        {
            Suite = suite; Rom = rom; MaxWaitSeconds = maxWait;
            Region = region; InputSpec = input; ExpectedCrcs = crcs;
            PassOnStable = passOnStable;
        }
    }

    public static class TestCatalog
    {
        public static List<TestDefinition> GetAllTests()
        {
            var t = new List<TestDefinition>();
            int W = 30, WM = 50;

            // APU Mixer
            foreach (var r in new[] { "dmc.nes", "noise.nes", "square.nes", "triangle.nes" })
                t.Add(new TestDefinition("apu_mixer", r, W));

            // APU Reset
            foreach (var r in new[] { "4015_cleared.nes", "4017_timing.nes", "4017_written.nes", "irq_flag_cleared.nes", "len_ctrs_enabled.nes", "works_immediately.nes" })
                t.Add(new TestDefinition("apu_reset", r, W));

            // APU Test
            t.Add(new TestDefinition("apu_test", "apu_test.nes", W));
            foreach (var r in new[] { "1-len_ctr.nes", "2-len_table.nes", "3-irq_flag.nes", "4-jitter.nes", "5-len_timing.nes", "6-irq_flag_timing.nes", "7-dmc_basics.nes", "8-dmc_rates.nes" })
                t.Add(new TestDefinition("apu_test/rom_singles", r, W));

            // Blargg APU 2005
            foreach (var r in new[] { "01.len_ctr.nes", "02.len_table.nes", "03.irq_flag.nes", "04.clock_jitter.nes", "05.len_timing_mode0.nes", "06.len_timing_mode1.nes", "07.irq_flag_timing.nes", "08.irq_timing.nes", "09.reset_timing.nes", "10.len_halt_timing.nes", "11.len_reload_timing.nes" })
                t.Add(new TestDefinition("blargg_apu_2005.07.30", r, W));

            // CPU Tests
            foreach (var r in new[] { "cpu.nes", "official.nes" })
                t.Add(new TestDefinition("blargg_nes_cpu_test5", r, WM));
            foreach (var r in new[] { "palette_ram.nes", "power_up_palette.nes", "sprite_ram.nes", "vbl_clear_time.nes", "vram_access.nes" })
                t.Add(new TestDefinition("blargg_ppu_tests_2005.09.15b", r, W));
            foreach (var r in new[] { "1.Branch_Basics.nes", "2.Backward_Branch.nes", "3.Forward_Branch.nes" })
                t.Add(new TestDefinition("branch_timing_tests", r, W));

            t.Add(new TestDefinition("cpu_dummy_reads", "cpu_dummy_reads.nes", W));
            foreach (var r in new[] { "cpu_dummy_writes_oam.nes", "cpu_dummy_writes_ppumem.nes" })
                t.Add(new TestDefinition("cpu_dummy_writes", r, W));
            foreach (var r in new[] { "test_cpu_exec_space_ppuio.nes", "test_cpu_exec_space_apu.nes" })
                t.Add(new TestDefinition("cpu_exec_space", r, W));

            t.Add(new TestDefinition("cpu_interrupts_v2", "cpu_interrupts.nes", WM));
            foreach (var r in new[] { "1-cli_latency.nes", "2-nmi_and_brk.nes", "3-nmi_and_irq.nes", "4-irq_and_dma.nes", "5-branch_delays_irq.nes" })
                t.Add(new TestDefinition("cpu_interrupts_v2/rom_singles", r, W));
            foreach (var r in new[] { "registers.nes", "ram_after_reset.nes" })
                t.Add(new TestDefinition("cpu_reset", r, W));

            t.Add(new TestDefinition("cpu_timing_test6", "cpu_timing_test.nes", WM));

            // DMC DMA
            t.Add(new TestDefinition("dmc_dma_during_read4", "dma_2007_read.nes", W, crcs: new[] { "159A7A8F", "5E3DF9C4" }));
            t.Add(new TestDefinition("dmc_dma_during_read4", "dma_2007_write.nes", W));
            t.Add(new TestDefinition("dmc_dma_during_read4", "dma_4016_read.nes", W));
            t.Add(new TestDefinition("dmc_dma_during_read4", "double_2007_read.nes", W, crcs: new[] { "85CFD627", "F018C287", "440EF923", "E52F41A5" }));
            t.Add(new TestDefinition("dmc_dma_during_read4", "read_write_2007.nes", W));

            // Instruction Tests
            t.Add(new TestDefinition("instr_misc", "instr_misc.nes", W));
            foreach (var r in new[] { "01-abs_x_wrap.nes", "02-branch_wrap.nes", "03-dummy_reads.nes", "04-dummy_reads_apu.nes" })
                t.Add(new TestDefinition("instr_misc/rom_singles", r, W));

            t.Add(new TestDefinition("instr_test-v3", "all_instrs.nes", WM));
            t.Add(new TestDefinition("instr_test-v3", "official_only.nes", WM));
            foreach (var r in new[] { "01-implied.nes", "02-immediate.nes", "03-zero_page.nes", "04-zp_xy.nes", "05-absolute.nes", "06-abs_xy.nes", "07-ind_x.nes", "08-ind_y.nes", "09-branches.nes", "10-stack.nes", "11-jmp_jsr.nes", "12-rts.nes", "13-rti.nes", "14-brk.nes", "15-special.nes" })
                t.Add(new TestDefinition("instr_test-v3/rom_singles", r, W));

            t.Add(new TestDefinition("instr_test-v5", "all_instrs.nes", WM));
            t.Add(new TestDefinition("instr_test-v5", "official_only.nes", WM));
            foreach (var r in new[] { "01-basics.nes", "02-implied.nes", "03-immediate.nes", "04-zero_page.nes", "05-zp_xy.nes", "06-absolute.nes", "07-abs_xy.nes", "08-ind_x.nes", "09-ind_y.nes", "10-branches.nes", "11-stack.nes", "12-jmp_jsr.nes", "13-rts.nes", "14-rti.nes", "15-brk.nes", "16-special.nes" })
                t.Add(new TestDefinition("instr_test-v5/rom_singles", r, W));

            t.Add(new TestDefinition("instr_timing", "instr_timing.nes", WM));
            foreach (var r in new[] { "1-instr_timing.nes", "2-branch_timing.nes" })
                t.Add(new TestDefinition("instr_timing/rom_singles", r, W));

            // MMC3
            foreach (var r in new[] { "1.Clocking.nes", "2.Details.nes", "3.A12_clocking.nes", "4.Scanline_timing.nes", "5.MMC3_rev_A.nes", "6.MMC3_rev_B.nes" })
                t.Add(new TestDefinition("mmc3_irq_tests", r, W));
            foreach (var r in new[] { "1-clocking.nes", "2-details.nes", "3-A12_clocking.nes", "4-scanline_timing.nes", "5-MMC3.nes", "6-MMC6.nes" })
                t.Add(new TestDefinition("mmc3_test", r, W));
            foreach (var r in new[] { "1-clocking.nes", "2-details.nes", "3-A12_clocking.nes", "4-scanline_timing.nes", "5-MMC3.nes", "6-MMC3_alt.nes" })
                t.Add(new TestDefinition("mmc3_test_2/rom_singles", r, W));

            // NES Instruction Test
            foreach (var r in new[] { "01-implied.nes", "02-immediate.nes", "03-zero_page.nes", "04-zp_xy.nes", "05-absolute.nes", "06-abs_xy.nes", "07-ind_x.nes", "08-ind_y.nes", "09-branches.nes", "10-stack.nes", "11-special.nes" })
                t.Add(new TestDefinition("nes_instr_test/rom_singles", r, W));

            // PPU Tests
            t.Add(new TestDefinition("oam_read", "oam_read.nes", W));
            t.Add(new TestDefinition("ppu_open_bus", "ppu_open_bus.nes", W));
            t.Add(new TestDefinition("ppu_read_buffer", "test_ppu_read_buffer.nes", WM));
            t.Add(new TestDefinition("ppu_vbl_nmi", "ppu_vbl_nmi.nes", WM));
            foreach (var r in new[] { "01-vbl_basics.nes", "02-vbl_set_time.nes", "03-vbl_clear_time.nes", "04-nmi_control.nes", "05-nmi_timing.nes", "06-suppression.nes", "07-nmi_on_timing.nes", "08-nmi_off_timing.nes", "09-even_odd_frames.nes", "10-even_odd_timing.nes" })
                t.Add(new TestDefinition("ppu_vbl_nmi/rom_singles", r, W));

            // Controller
            t.Add(new TestDefinition("read_joy3", "test_buttons.nes", 25, input: "A:2.0,B:4.0,Select:6.0,Start:8.0,Up:10.0,Down:12.0,Left:14.0,Right:16.0"));
            foreach (var r in new[] { "count_errors.nes", "count_errors_fast.nes" })
                t.Add(new TestDefinition("read_joy3", r, W, passOnStable: true));
            t.Add(new TestDefinition("read_joy3", "thorough_test.nes", W));

            // Sprite DMA
            foreach (var r in new[] { "sprdma_and_dmc_dma.nes", "sprdma_and_dmc_dma_512.nes" })
                t.Add(new TestDefinition("sprdma_and_dmc_dma", r, W));

            // Sprite Tests
            foreach (var r in new[] { "01.basics.nes", "02.alignment.nes", "03.corners.nes", "04.flip.nes", "05.left_clip.nes", "06.right_edge.nes", "07.screen_bottom.nes", "08.double_height.nes", "09.timing_basics.nes", "10.timing_order.nes", "11.edge_timing.nes" })
                t.Add(new TestDefinition("sprite_hit_tests_2005.10.05", r, W));
            foreach (var r in new[] { "1.Basics.nes", "2.Details.nes", "3.Timing.nes", "4.Obscure.nes", "5.Emulator.nes" })
                t.Add(new TestDefinition("sprite_overflow_tests", r, W));

            // VBL NMI Timing
            foreach (var r in new[] { "1.frame_basics.nes", "2.vbl_timing.nes", "3.even_odd_frames.nes", "4.vbl_clear_timing.nes", "5.nmi_suppression.nes", "6.nmi_disable.nes", "7.nmi_timing.nes" })
                t.Add(new TestDefinition("vbl_nmi_timing", r, W));

            // ── PAL region tests ──
            foreach (var r in new[] { "01.len_ctr.nes", "02.len_table.nes", "03.irq_flag.nes", "04.clock_jitter.nes", "05.len_timing_mode0.nes", "06.len_timing_mode1.nes", "07.irq_flag_timing.nes", "08.irq_timing.nes", "10.len_halt_timing.nes", "11.len_reload_timing.nes" })
                t.Add(new TestDefinition("pal_apu_tests", r, W, NesRegion.PAL));

            return t;
        }
    }
}
