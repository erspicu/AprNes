# benchmark.ps1 - AprNes JIT vs AOT Benchmark Script
# 執行兩種版本的 NesCore benchmark，結果寫入 benchmark.txt

$ErrorActionPreference = "Stop"
$root    = $PSScriptRoot
$exe     = "$root\AprNesAOT\bin\Release\net8.0-windows\AprNesAOT.exe"
$rom     = "$root\nes-test-roms-master\Controller Test (USA)\Controller Test (USA).nes"
$seconds = 10
$output  = "$root\benchmark.txt"

# ── 確認 exe 存在，否則先 build ──────────────────────────────────────────────
if (-not (Test-Path $exe)) {
    Write-Host "[INFO] AprNesAOT.exe not found, running buildAot.bat first..." -ForegroundColor Yellow
    & "$root\buildAot.bat"
    if ($LASTEXITCODE -ne 0) { Write-Error "buildAot.bat failed"; exit 1 }
}

# ── 確認 ROM 存在 ─────────────────────────────────────────────────────────────
if (-not (Test-Path $rom)) {
    Write-Error "ROM not found: $rom"
    exit 1
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " AprNes Benchmark  (JIT .NET8  vs  AOT NesCoreNative.dll)" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " ROM    : $(Split-Path $rom -Leaf)"
Write-Host " Seconds: $seconds sec each"
Write-Host " Output : $output"
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# ── 執行 benchmark ────────────────────────────────────────────────────────────
& $exe --benchmark $rom $seconds $output

if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Benchmark failed (exit code $LASTEXITCODE)" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host " Done. Results saved to: $output" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
