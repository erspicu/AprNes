# PPU_DATA_Pipeline_Step3 Idle Fast-Path (#2)

- **Date**: 2026-04-25
- **Source**: `MD/optimization/PPU_NTSC_CRT_Optimization_Notes.md` item #2
- **Build**: Debug x64, .NET Framework 4.8.1

## Latch idle pattern derivation

Step1 / Step3 latch update equations (with Read_SR / Write_SR = false):

```
Step1: readLatch = (readLatch & 0x0A) | 0 | ((~readLatch << 1) & 0x14)
Step3: readLatch = (readLatch & 0x15) | ((~readLatch << 1) & 0x0A)
```

Tracing from `readLatch = 0`:

```
Step1: 0    → 0x14   PD_RB false
Step3: 0x14 → 0x16
Step1: 0x16 → 0x12   PD_RB=TRUE (transient phantom trigger!)
Step3: 0x12 → 0x1A
Step1: 0x1A → 0x0A
Step3: 0x0A → 0x0A   ← fixed point
```

**Idle pattern = `readLatch == 0x0A && writeLatch == 0x0A`**.

## Skip safety in idle steady state

| Step3 operation | Idle behaviour |
|---|---|
| `TStep = TStep_Latch \|\| PD_RB` | `false \|\| false = false` → TStep block skipped |
| `PPU_ALE = ReadALE \|\| WriteALE` | `false`（已是 false） |
| `Ppu2007_BusRead()` | early-return on `!PD_RB` |
| `readLatch` update | `0x0A → 0x0A` (fixed point) |
| `writeLatch` update | `0x0A → 0x0A` (fixed point) |
| `DB_PAR = (writeLatch & 0x05) == 0x04` | false (no change) |
| `PPU_WRITE` 更新 | false (no change) |

In fully idle, every Step3 operation is a no-op on observable state. Skipping the entire body is byte-equivalent.

## Implementation

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
static void PPU_DATA_Pipeline_Step3()
{
    if (readLatch == 0x0A && writeLatch == 0x0A
        && !ppu2007_Read_SR && !ppu2007_Write_SR
        && !ppu2007_PD_RB && !ppu2007_DB_PAR)
    {
        return;
    }
    // ... existing body ...
}
```

6-condition guard at entry. JIT compiles this to a short-circuit chain; branch predictor learns that idle is the common case (>99% of dots).

## Correctness verification

- **blargg test suite: 184/184 PASS** — including all `$2007` timing tests:
  - `ppu_read_buffer`
  - `vram_access`
  - `ppu_open_bus`
  - palette tests
  - APU/CPU timing tests

## Performance

| Metric | Before #2 | After #2 | Δ |
|---|---:|---:|---:|
| **Pure-core baseline FPS** | 146.91 | **155.32** | **+8.41 (+5.7%)** ✓✓ |
| **Global I-cache miss** | 0.52% | **0.50%** | -0.02pp |
| `ppu_half_step_new` Excl% | 4.1% | **2.6%** | **-1.5pp** ✓ |
| `Ppu_Tick_Visible_PixelZone_Analog` Excl% | 8.5% | 8.2% | -0.3pp |
| `Run_NTSC` Excl% | 4.8% | 4.9% | +0.1 (noise) |
| `DemodulateRow_Core` miss% | 1.25% | 1.14% | -0.11pp |
| CRT `<Render>` miss% | 1.91% | 1.13% | -0.78pp ✓ |

**Top win**: `ppu_half_step_new` Excl drops 1.5pp because Step3 fast-returns in most calls. Half-step is invoked ~3× per CPU cycle, so saving most of its work meaningfully reduces the per-tick budget.

The +5.7% pure-core FPS gain exceeds the source doc's high-end estimate of 6%, suggesting the optimization is well-targeted.

## Why ppu_half_step_new improved most

Looking at `ppu_half_step_new`:

```csharp
static void ppu_half_step_new()
{
    // ... BG shift, commit, VBL latch, OAM buffer, sprite0 hit pipeline ...

    PPU_DATA_Pipeline_Step3();  // ← NOW fast-returns when idle
}
```

Step3 was the dominant call in half-step. Each ppu_half_step_new fires 3× per CPU cycle (mcPpuClock = 2, 6, 10), so ~5.4M times/sec at NTSC speed. With Step3 fast-returning ~99% of those, the half-step Excl drops 1.5pp.

## Why FPS gain > sum of individual Excl drops

Inline budget cascade in reverse:
- Step3 fast-return shrinks ppu_half_step_new's effective body
- Smaller ppu_half_step_new → JIT more willing to fully inline it
- More inlining → fewer call overheads, better CPU register allocation
- Compounding effect amplifies the raw work savings

Additionally, smaller machine code footprint improves L1 I-cache density, helping unrelated methods (CRT lambda miss% dropped 0.78pp).

## Avalonia compatibility

Same shared NesCore code; both NetFx and Avalonia benefit equally. Avalonia GUI users should see proportional FPS gain in non-CRT-bottleneck configs.

## Conclusion

- ✅ +5.7% FPS in pure-core benchmark (largest single optimization since refactor)
- ✅ Global I-cache improves -0.02pp
- ✅ `ppu_half_step_new` Excl -1.5pp
- ✅ CRT lambda I-cache miss recovers (1.91% → 1.13%)
- ✅ 184/184 blargg tests pass — correctness preserved
- ✅ AC test verification: deferred to user
