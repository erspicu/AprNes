# run_tests.ps1 — Batch test runner for AprNes
# Usage: powershell -ExecutionPolicy Bypass -File run_tests.ps1

$ErrorActionPreference = "Continue"

$exePath = "$PSScriptRoot\AprNes\bin\Debug\AprNes.exe"
$romDir  = "$PSScriptRoot\nes-test-roms-master\checked"
$outDir  = "$PSScriptRoot\test_output"
$logFile = "$outDir\results.log"

if (-not (Test-Path $exePath)) {
    Write-Host "ERROR: AprNes.exe not found at $exePath" -ForegroundColor Red
    Write-Host "Build first with MSBuild."
    exit 1
}

if (-not (Test-Path $romDir)) {
    Write-Host "ERROR: ROM directory not found: $romDir" -ForegroundColor Red
    exit 1
}

# Create output directory
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
if (-not (Test-Path "$outDir\screenshots")) { New-Item -ItemType Directory -Path "$outDir\screenshots" | Out-Null }

# Clear previous log
if (Test-Path $logFile) { Remove-Item $logFile }

$roms = Get-ChildItem -Path $romDir -Filter "*.nes" -Recurse
$total = $roms.Count
$pass = 0
$fail = 0
$error_count = 0

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " AprNes Test Runner" -ForegroundColor Cyan
Write-Host " ROMs: $total" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

foreach ($rom in $roms) {
    $romRelative = $rom.FullName.Substring($romDir.Length + 1)
    $safeName = $romRelative -replace '[\\\/]', '_' -replace '\.nes$', ''
    $screenshotPath = "$outDir\screenshots\$safeName.png"

    $proc = Start-Process -FilePath $exePath `
        -ArgumentList "--rom", "`"$($rom.FullName)`"", "--wait-result", "--max-wait", "30", "--screenshot", "`"$screenshotPath`"", "--log", "`"$logFile`"" `
        -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$outDir\stdout_temp.txt" -RedirectStandardError "$outDir\stderr_temp.txt"

    $stdout = ""
    $stderr = ""
    if (Test-Path "$outDir\stdout_temp.txt") { $stdout = Get-Content "$outDir\stdout_temp.txt" -Raw }
    if (Test-Path "$outDir\stderr_temp.txt") { $stderr = Get-Content "$outDir\stderr_temp.txt" -Raw }

    $exitCode = $proc.ExitCode

    if ($exitCode -eq 0) {
        $pass++
        Write-Host "  PASS  " -NoNewline -ForegroundColor Green
    } elseif ($exitCode -eq 1) {
        $fail++
        Write-Host "  FAIL  " -NoNewline -ForegroundColor Red
    } else {
        $error_count++
        Write-Host "  ERR   " -NoNewline -ForegroundColor Yellow
    }

    $displayText = if ($stdout) { $stdout.Trim() } else { $romRelative }
    Write-Host $displayText

    if ($stderr) {
        Write-Host "         $($stderr.Trim())" -ForegroundColor DarkGray
    }
}

# Cleanup temp files
Remove-Item "$outDir\stdout_temp.txt" -ErrorAction SilentlyContinue
Remove-Item "$outDir\stderr_temp.txt" -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Results: $pass PASS / $fail FAIL / $error_count ERROR (of $total)" -ForegroundColor Cyan
Write-Host " Log: $logFile" -ForegroundColor Cyan
Write-Host " Screenshots: $outDir\screenshots\" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

exit $fail + $error_count
