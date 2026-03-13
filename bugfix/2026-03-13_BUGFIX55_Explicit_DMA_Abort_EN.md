# BUGFIX55: P13 Explicit DMA Abort (2026-03-13)

## Problem
AccuracyCoin P13 "Explicit DMA Abort" test FAIL (err=2).
The test verifies that when DMC DMA is in progress, the deferred status delay for writing
$4015=#$00 (disable) must be extended near the timer fire boundary.

## Root Cause Analysis

### 1. Explicit abort only detected the "just fired" case
The original condition `dmctimer == dmcrate` only caught the cycle when the timer just
fired (reloaded to rate). However, TriCNES covers **two** cycles of the fire window:
- `timer == Rate && PutCycle` (just reloaded)
- `timer == 2 && !PutCycle` (about to fire)

In AprNes (where clockdmc decrements by 1 each cycle and runs before the CPU write):
- `dmctimer == dmcrate`: timer fired and reloaded this cycle
- `dmctimer == 1`: timer will fire next cycle

### 2. Normal delay does not account for timer/deferred same-cycle conflict
When `dmctimer == delay`, timer fire and deferred status trigger in the same cycle.
In clockdmc, timer fire (DMA trigger) executes before deferred status, but dmcStopTransfer()
in the same cycle immediately cancels the just-started DMA.

A parity-dependent delay is needed to avoid the conflict: delay=4 when getCycle=true,
delay=3 when getCycle=false.

## Fix (APU.cs)

### Change 1: Normal delay changed to parity-dependent
```csharp
// Before
dmcStatusDelay = 3;
// After
dmcStatusDelay = getCycle ? 4 : 3;
```

### Change 2: Explicit abort covers 2-cycle fire window
```csharp
// Before: only detects timer==dmcrate
bool timerJustFired = (dmctimer == dmcrate);
if (timerJustFired) dmcStatusDelay = 5;

// After: covers both "just fired" and "about to fire"
if (dmctimer == dmcrate)
    dmcStatusDelay = 4;  // just fired (TriCNES Rate&&PUT)
else if (dmctimer == 1)
    dmcStatusDelay = 5;  // about to fire (TriCNES 2&&GET)
```

## Test Results
- Explicit DMA Abort answer key fully matched:
  `$04,$04,$04,$04,$04,$04,$03,$04,$01,$01,$00,$00,$00,$00,$00,$00`
- blargg: 174/174 PASS (no regression)
- AccuracyCoin: 134→135/136 (+1)

## Baseline
- **174 PASS / 0 FAIL / 174 TOTAL**
- **AccuracyCoin: 135/136 PASS** (1 remaining FAIL: P13 Implicit DMA Abort)
