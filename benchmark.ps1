# benchmark.ps1 - AprNes 三版 JIT/AOT Benchmark Script
# 1. .NET Framework 4.6.1 JIT  (AprNes.exe)
# 2. .NET 8 RyuJIT             (AprNesAOT.exe)
# 3. Native AOT                (NesCoreNative.dll via AprNesAOT.exe)

$ErrorActionPreference = "Stop"
$root       = $PSScriptRoot
$fxExe      = "$root\AprNes\bin\Release\AprNes.exe"
$dotnetExe  = "$root\AprNesAOT\bin\Release\net8.0-windows\AprNesAOT.exe"
$rom        = "$root\nes-test-roms-master\Controller Test (USA)\Controller Test (USA).nes"
$seconds    = 10
$output     = "$root\benchmark.txt"

# ── 確認 exe 存在，否則先 build ──────────────────────────────────────────────
if (-not (Test-Path $fxExe) -or -not (Test-Path $dotnetExe)) {
    Write-Host "[INFO] Executables not found, building..." -ForegroundColor Yellow
    & "$root\build.ps1"
    & "$root\buildAot.bat"
}

# ── 確認 ROM 存在 ─────────────────────────────────────────────────────────────
if (-not (Test-Path $rom)) { Write-Error "ROM not found: $rom"; exit 1 }

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " AprNes Benchmark  (.NET Fx JIT  /  .NET 8 RyuJIT  /  AOT)" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " ROM     : $(Split-Path $rom -Leaf)"
Write-Host " Seconds : $seconds sec each"
Write-Host " Output  : $output"
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# ── 建立 header，寫入 output 檔（清空舊內容）────────────────────────────────
$header = @"
=== AprNes Benchmark ===
ROM  : $(Split-Path $rom -Leaf)
Time : $seconds sec each
Date : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
OS   : $([System.Environment]::OSVersion.VersionString)
CPU  : $((Get-ItemProperty 'HKLM:\HARDWARE\DESCRIPTION\System\CentralProcessor\0').ProcessorNameString)

"@
Set-Content -Path $output -Value $header -Encoding UTF8

# ── 1. .NET Framework 4.6.1 JIT ──────────────────────────────────────────────
Write-Host "[1/3] .NET Framework 4.6.1 JIT ..." -ForegroundColor Yellow
& $fxExe --benchmark $rom $seconds $output
if ($LASTEXITCODE -ne 0) { Write-Host "[WARN] AprNes.exe exited with $LASTEXITCODE" -ForegroundColor Red }

# ── 2+3. .NET 8 RyuJIT + AOT DLL ─────────────────────────────────────────────
Write-Host "[2/3] .NET 8 RyuJIT  +  [3/3] AOT DLL ..." -ForegroundColor Yellow
& $dotnetExe --benchmark $rom $seconds $output
if ($LASTEXITCODE -ne 0) { Write-Host "[WARN] AprNesAOT.exe exited with $LASTEXITCODE" -ForegroundColor Red }

# ── 顯示結果 ──────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host " Results:" -ForegroundColor Green
Get-Content $output | Write-Host
Write-Host "============================================================" -ForegroundColor Green
Write-Host " Saved to: $output" -ForegroundColor Green
