# ============================================================
#  AprNesAvalonia GUI Benchmark (Phase 3A — render-thread D3D11)
# ============================================================
# Runs 3 CRT strategies (scalar / simd / gpu) × 3 runs each (JIT warmup +
# 2 counted) in GUI mode. Reads gui_benchmark.log after each run,
# aggregates, prints a summary table.

param(
    [int]$Duration = 20,
    [int]$Cooldown = 10,
    [string]$RomPath = '',
    [int]$Size     = 8        # analog-size multiplier (2/4/6/8/10/...); see CRT_DstW/H = 256/210 * Size
)

$ErrorActionPreference = 'Continue'

$exePath = Join-Path $PSScriptRoot 'AprNesAvalonia.exe'
$logPath = Join-Path $PSScriptRoot 'gui_benchmark.log'

if (-not (Test-Path $exePath)) {
    Write-Error "EXE not found: $exePath"
    exit 1
}

# ROM resolution: --RomPath arg > tools/benchmark/ny2011.nes > repo etc/Mega Man 5
if ([string]::IsNullOrEmpty($RomPath)) {
    $cand = Join-Path $PSScriptRoot 'tools\benchmark\ny2011.nes'
    if (Test-Path $cand) {
        $RomPath = $cand
    } else {
        $RomPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..\etc\Mega Man 5 (USA).nes'))
    }
}
if (-not (Test-Path $RomPath)) {
    Write-Error "ROM not found: $RomPath"
    exit 1
}

$strategies = @('scalar', 'simd', 'gpu')
$results    = @{}

Write-Host ""
Write-Host "=========================================================="
Write-Host "  AprNesAvalonia GUI Benchmark (Phase 3A)"
$dstW = 256 * $Size
$dstH = 210 * $Size
Write-Host "  ROM:        $RomPath"
Write-Host "  Duration:   ${Duration}s per run"
Write-Host "  Cooldown:   ${Cooldown}s between runs"
Write-Host "  Flags:      ultra-analog + RF + ${Size}x (${dstW}x${dstH}) + CRT + DSP mode 2"
Write-Host "  Strategies: $($strategies -join ' / ')"
Write-Host "=========================================================="

function Parse-Fps {
    param([string]$logFile, [string]$pattern)
    if (-not (Test-Path $logFile)) { return $null }
    foreach ($ln in (Get-Content $logFile)) {
        if ($ln -match "\(([0-9.]+) FPS $pattern") { return [double]$Matches[1] }
    }
    return $null
}

foreach ($strat in $strategies) {
    Write-Host ""
    Write-Host "=========================================================="
    Write-Host "  Strategy: $strat"
    Write-Host "=========================================================="

    $renderSamples = @()
    $emuSamples    = @()

    for ($r = 1; $r -le 3; $r++) {
        $label = if ($r -eq 1) { 'Run 1 (JIT warmup, discarded)' } else { "Run $r" }
        Write-Host ""
        Write-Host "--- $label  [strategy=$strat] ---"

        Remove-Item $logPath -ErrorAction SilentlyContinue

        $exeArgs = @(
            '--rom', $RomPath,
            '--gui-benchmark', $Duration,
            '--analog', '--ultra-analog',
            '--analog-output', 'RF',
            '--analog-size', $Size,
            '--crt',
            '--audio-dsp', '--audio-mode', '2',
            '--crt-strategy', $strat
        )

        & $exePath @exeArgs | Out-Null

        $rFps = Parse-Fps -logFile $logPath -pattern 'presented'
        $eFps = Parse-Fps -logFile $logPath -pattern 'produced'

        if ($rFps -ne $null -and $eFps -ne $null) {
            Write-Host ("  Presented: {0,7:F2} FPS    Emu: {1,7:F2} FPS" -f $rFps, $eFps)
            if ($r -ge 2) {
                $renderSamples += $rFps
                $emuSamples    += $eFps
            }
        } else {
            Write-Warning "  gui_benchmark.log missing or unparseable — run $r counted as 0"
        }

        if ($r -lt 3) {
            Write-Host "--- Cooling ${Cooldown}s ---"
            Start-Sleep -Seconds $Cooldown
        }
    }

    $results[$strat] = [PSCustomObject]@{
        Render = if ($renderSamples.Count -gt 0) { ($renderSamples | Measure-Object -Average).Average } else { 0 }
        Emu    = if ($emuSamples.Count    -gt 0) { ($emuSamples    | Measure-Object -Average).Average } else { 0 }
    }

    Write-Host ""
    Write-Host "--- Cooling ${Cooldown}s before next strategy ---"
    Start-Sleep -Seconds $Cooldown
}

Write-Host ""
Write-Host "=========================================================="
Write-Host "  GUI BENCHMARK SUMMARY (avg of Run 2 + Run 3)"
Write-Host "=========================================================="
Write-Host ""
Write-Host ("  {0,-10} | {1,16} | {2,10}" -f 'strategy', 'presented FPS', 'emu FPS')
Write-Host "  -----------+------------------+-----------"
foreach ($strat in $strategies) {
    $r = $results[$strat]
    Write-Host ("  {0,-10} | {1,16:F2} | {2,10:F2}" -f $strat, $r.Render, $r.Emu)
}

# Derived speedups if both simd and gpu ran
if ($results['simd'] -and $results['gpu']) {
    $simdR = $results['simd'].Render
    $gpuR  = $results['gpu'].Render
    if ($simdR -gt 0) {
        $speedup = $gpuR / $simdR
        Write-Host ""
        Write-Host ("  GPU / SIMD speedup (presented FPS): {0:F2}x" -f $speedup)
    }
}

Write-Host ""
Write-Host "Last run's full log: $logPath"
Write-Host ""
