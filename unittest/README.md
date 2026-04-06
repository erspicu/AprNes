# NES Emulator Test Framework

An open-source, emulator-agnostic test framework for NES emulators. Runs 184 blargg test ROMs (174 NTSC + 10 PAL) against any emulator that implements the `IEmulatorCore` interface.

## Quick Start (CLI Mode)

If your emulator supports these CLI flags:
```
your_emu.exe --rom <path> --wait-result --max-wait <sec> --region <NTSC|PAL>
```
Exit code 0 = PASS, non-zero = FAIL. Then run:
```bash
python run_tests.py --exe <your-emulator> --rom-dir <path-to-roms>
```

## Quick Start (Library Mode)

1. Reference `NesTestFramework.dll` (netstandard2.0)
2. Implement `IEmulatorCore` (9 methods) — see `examples/minimal_adapter.cs`
3. Use `BlarggTestRunner` to drive tests:
```csharp
using NesTestFramework;

var emu = new MyEmulatorAdapter();
var runner = new BlarggTestRunner(emu);
var tests = TestCatalog.GetAllTests();

foreach (var test in tests)
{
    var result = runner.RunTest(test, "path/to/roms");
    Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL")} {test.Suite}/{test.Rom}");
}
```

## IEmulatorCore Interface

| Method | Purpose |
|--------|---------|
| `SetRegion(NesRegion)` | Set NTSC/PAL/Dendy before loading a ROM |
| `LoadRom(byte[])` | Load iNES ROM data, return success |
| `RunOneFrame()` | Advance exactly one frame (VBlank-to-VBlank) |
| `SoftReset()` | Trigger soft reset |
| `ReadCpuMemory(ushort)` | Read CPU address space ($0000-$FFFF) |
| `GetNametable0(byte[])` | Copy 960 bytes of PPU nametable 0 |
| `GetScreenPixels(uint[])` | Copy 256x240 ARGB pixels |
| `SetP1Buttons(...)` | Set 8 button states for Player 1 |

All methods use managed types (no unsafe pointers) for cross-language compatibility.

## Blargg Test Protocol

The framework implements the standard blargg test detection protocol:

1. **$6000 Protocol** (primary): Checks signature `$6001=DE, $6002=B0, $6003=61`. Status byte at `$6000`: `>=$80` = running, `$81` = soft-reset requested, `<$80` = done (`$00` = pass). Result text at `$6004+`.

2. **Screen Stability** (fallback): Hashes screen pixels each frame. After 90 stable frames, searches nametable for "Passed"/"Failed" text.

3. **CRC Matching**: For visual-output tests (e.g. DMC DMA timing), compares nametable CRC against expected values.

4. **Pass-on-Stable**: For error counter tests (`count_errors.nes`), passes if screen stabilizes showing " 0/" errors.

## Test Catalog

184 tests organized by suite:

| Category | Suites | Count |
|----------|--------|-------|
| APU | apu_mixer, apu_reset, apu_test, blargg_apu_2005 | 25 |
| CPU | blargg_nes_cpu_test5, instr_test-v3/v5, cpu_* | 62 |
| PPU | ppu_vbl_nmi, sprite_hit, sprite_overflow, vbl_nmi_timing | 39 |
| DMA | dmc_dma_during_read4, sprdma_and_dmc_dma | 7 |
| Mapper | mmc3_irq_tests, mmc3_test, mmc3_test_2 | 18 |
| Other | oam_read, ppu_open_bus, read_joy3, branch_timing | 23 |
| PAL | pal_apu_tests | 10 |

## Directory Structure

```
unittest/
    README.md                    # This file
    NesTestFramework/            # .NET class library (netstandard2.0)
        IEmulatorCore.cs         # Interface + enums
        BlarggTestRunner.cs      # Protocol engine
        TestCatalog.cs           # 184 test definitions
        TestResult.cs            # Result types
    adapters/
        AprNesAdapter/           # Reference: wraps AprNes static NesCore
    examples/
        minimal_adapter.cs       # Skeleton for new adapters
    roms/                        # Test ROM directory (symlink or copy)
```

## Writing an Adapter

1. Copy `examples/minimal_adapter.cs`
2. Replace each TODO with your emulator's API
3. Key requirements:
   - `RunOneFrame()` must be **synchronous** — return only after frame completes
   - `ReadCpuMemory()` must reflect current state (including mapper bank switching)
   - `GetNametable0()` reads raw PPU VRAM tile indices, not rendered pixels
   - `SetP1Buttons()` is called before each `RunOneFrame()`

See `adapters/AprNesAdapter/` for a complete reference implementation.

## License

Test ROMs are public domain (blargg). Framework code is MIT licensed.
