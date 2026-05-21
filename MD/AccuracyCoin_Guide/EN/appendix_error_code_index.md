# Appendix A: Per-page / error-code quick index

> This is not the full text of the error codes (that's in the ROM's own `README.md`, with an official description per code). This is a **navigational index** — what each of the 20 pages checks, which chapter of this guide covers it, and the pitfalls to watch for.
> Full error-code text: `nes-test-roms-master/AccuracyCoin-main-20260521/README.md`.
> Can't tell what a code checks: search the `.asm` for `TEST_<test name>:` and count the sub-tests between `INC <ErrorCode` (`ErrorCode` counts from 1, fail N = the Nth sub-test). See [`00_methodology.md`](00_methodology.md) §3.

AccuracyCoin `20260521` has **20 pages, 139 PASS/FAIL tests + 5 DRAW**.

---

## Page overview

| Page | Topic | Guide chapter | Notes / signature pitfalls |
|------|-------|---------------|----------------------------|
| **P1** | CPU Behavior | [`01_cpu`](01_cpu.md) | ROM not writable, RAM mirroring, PC wraparound, **decimal flag**, **B flag**, dummy read/write, **open bus**, all NOP |
| **P2–P9** | Unofficial Opcodes | [`01_cpu`](01_cpu.md) §4 | SLO/RLA/SRE/RRA, SAX/LAX, DCP, ISC… — mostly combinations of official instructions; match cycles + dummy reads |
| **P10** | Unofficial: SH\* | [`01_cpu`](01_cpu.md) §4 | **SHA/SHX/SHY/SHS**'s `&(H+1)`; DMA inserted before the write needs **ignoreH** |
| **P11** | Unofficial: Misc | [`01_cpu`](01_cpu.md) §4 | ANC/ASR/ARR/ANE/LXA/AXS/SBC immediate |
| **P12** | CPU Interrupts | [`01_cpu`](01_cpu.md) §5 | **Interrupt flag latency**, NMI Overlap BRK/IRQ — penultimate-cycle sampling, no NMI polling during the interrupt sequence |
| **P13** | DMA Tests | [`02_dma`](02_dma.md) | DMA + Open Bus/$2002/$2007R/$2007W/$4015R/$4016R, Bus Conflicts, **Explicit / Implicit DMA Abort** |
| **P14** | APU Tests | [`03_apu`](03_apu.md) | Length Counter/Table, **Frame Counter IRQ** (deferred clear), DMC, **APU Register Activation**, Controller Strobing/Clocking |
| **P15** | Power On State | — | **DRAW only** (power-on values of PPU Reset Flag / CPU RAM / CPU Registers / PPU RAM / Palette RAM); no auto-judging, read the screenshot |
| **P16** | PPU Rendering / Registers | [`04_ppu`](04_ppu.md) §3,4,6 | CHR ROM not writable, Register Mirroring/Open Bus, **Read Buffer**, **Palette RAM Quirks**, **Rendering Flag**, $2007 read w/ rendering |
| **P17** | PPU VBlank Timing | [`04_ppu`](04_ppu.md) §1 | VBlank begin/end, **NMI Control/Timing/Suppression**, NMI at/disabled-at VBlank |
| **P18** | Sprite Evaluation | [`04_ppu`](04_ppu.md) §2,5 | Sprite overflow, **Sprite 0 Hit**, **$2002 flag timing** (M2 stagger), Suddenly Resize, Arbitrary Sprite Zero, Misaligned OAM, $2004, **OAM Corruption**, INC $4014 |
| **P19** | PPU Misc (advanced) | [`04_ppu`](04_ppu.md) §5,6 | Attributes As Tiles, t Register Quirks, **Stale BG / Sprite Shift Registers**, BG Serial In, **Sprites On Scanline 0**, $2004 / $2007 Stress |
| **P20** | CPU Behavior 2 | [`01_cpu`](01_cpu.md) §1,2,6 | Instruction Timing, Implied / Branch Dummy Reads, JSR Edge Cases, **Internal Data Bus** |

---

## The error codes most likely to stall you (by difficulty, with our fixes)

> The "signature tests" — pass these and the whole of AC is basically within reach. Each links to its full fix record.

| Test (page) | code | what it checks | fix |
|-------------|------|----------------|-----|
| Internal Data Bus (P20) | 2 | `$4015` bit5 internal vs external bus | [dual data-bus](../../bugfix/2026-05-22_AC_InternalDataBus_DualDataBus.md) (CPU reads internal, DMA reads external) |
| APU Register Activation (P14) | 6/7 | OAM DMA reading APU registers + $20 mirror + bus conflict | [BUGFIX46](../../bugfix/2026-03-08_BUGFIX46.md) + dual-bus |
| Implicit DMA Abort (P13) | 2 | enable when a 1-byte sample is about to end → phantom 1-cycle DMA | [BUGFIX56](../../bugfix/2026-03-14_BUGFIX56_Implicit_DMA_Abort.md) (the last test that clinched v1 136/136) |
| Explicit DMA Abort (P13) | 2 | the deferred-delay parity of a disable during DMA | [BUGFIX55](../../bugfix/2026-03-13_BUGFIX55_Explicit_DMA_Abort.md) |
| Frame Counter IRQ (P14) | 7 | reading `$4015` clears the IRQ flag with a delay + inhibit means "happens then retracted" | [BUGFIX37](../../bugfix/2026-03-07_BUGFIX37.md) |
| $2002 flag timing (P18) | 1 | sprite flags clear ~2 dots before VBL (M2 duty 15/24) | [BUGFIX45](../../bugfix/2026-03-07_BUGFIX45.md) |
| Sprites On Scanline 0 (P19) | 2 | pre-render line `(261&255)=5` + secondary OAM carryover | [BUGFIX47](../../bugfix/2026-03-08_BUGFIX47.md) |
| $2004 Stress Test (P19) | — | per-dot OAM buffer reads during rendering | [BUGFIX48](../../bugfix/2026-03-08_BUGFIX48.md) |
| SH\* opcodes (P10) | — | DMA inserted before the write removes H masking | [BUGFIX51](../../bugfix/2026-03-10_BUGFIX51_SH_opcodes.md) |
| Open Bus (P1) | 1,4,9 | open bus = the data bus's residual value; ZP must update it too; $4015 bit5 | [BUGFIX29](../../bugfix/2026-03-04_BUGFIX29.md) |
| Branch Dummy Reads (P20) | 4,5 | a taken branch's dummy read must actually read memory | [BUGFIX29](../../bugfix/2026-03-04_BUGFIX29.md) |

> For the complete front-to-back fix order, see [`00_fix_history.md`](00_fix_history.md).
