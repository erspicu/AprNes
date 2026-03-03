# benchmark.ps1 - AprNes 四版 JIT/AOT Benchmark Script
# 1. .NET Framework 4.6.1 JIT  (AprNes.exe)
# 2. .NET 8 RyuJIT             (AprNesAOT.exe)
# 3. Native AOT                (NesCoreNative.dll via AprNesAOT.exe)
# 4. .NET 10 RyuJIT            (AprNesAOT10.exe)

$ErrorActionPreference = "Stop"
$root        = $PSScriptRoot
$fxExe       = "$root\AprNes\bin\Release\AprNes.exe"
$dotnet8Exe  = "$root\AprNesAOT\bin\Release\net8.0-windows\AprNesAOT.exe"
$dotnet10Exe = "$root\AprNesAOT10\bin\Release\net10.0-windows\AprNesAOT10.exe"
$rom         = "$root\nes-test-roms-master\Controller Test (USA)\Controller Test (USA).nes"
$seconds     = 10
$output      = "$root\benchmark.txt"

# ── 確認 exe 存在，否則先 build ──────────────────────────────────────────────
if (-not (Test-Path $fxExe) -or -not (Test-Path $dotnet8Exe)) {
    Write-Host "[INFO] Executables not found, building..." -ForegroundColor Yellow
    & "$root\build.ps1"
    & "$root\buildAot.bat"
}
if (-not (Test-Path $dotnet10Exe)) {
    Write-Host "[INFO] AprNesAOT10 not found, building..." -ForegroundColor Yellow
    dotnet build "$root\AprNesAOT10\AprNesAOT10.csproj" -c Release
}

# ── 確認 ROM 存在 ─────────────────────────────────────────────────────────────
if (-not (Test-Path $rom)) { Write-Error "ROM not found: $rom"; exit 1 }

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " AprNes Benchmark  (.NET Fx / .NET 8 / AOT / .NET 10)" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host " ROM     : $(Split-Path $rom -Leaf)"
Write-Host " Seconds : $seconds sec each"
Write-Host " Output  : $output"
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# ── 建立 header，寫入 output 檔（清空舊內容）────────────────────────────────
$cpuName = (Get-ItemProperty 'HKLM:\HARDWARE\DESCRIPTION\System\CentralProcessor\0').ProcessorNameString
$header = "=== AprNes Benchmark ===" + [Environment]::NewLine +
          "ROM  : $(Split-Path $rom -Leaf)" + [Environment]::NewLine +
          "Time : $seconds sec each" + [Environment]::NewLine +
          "Date : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" + [Environment]::NewLine +
          "OS   : $([System.Environment]::OSVersion.VersionString)" + [Environment]::NewLine +
          "CPU  : $cpuName" + [Environment]::NewLine +
          [Environment]::NewLine
Set-Content -Path $output -Value $header -Encoding UTF8 -NoNewline

# ── 1. .NET Framework 4.6.1 JIT ──────────────────────────────────────────────
Write-Host "[1/4] .NET Framework 4.6.1 JIT ..." -ForegroundColor Yellow
$p1 = Start-Process -FilePath $fxExe -ArgumentList @("--benchmark", "`"$rom`"", $seconds, "`"$output`"") -Wait -PassThru -NoNewWindow
if ($p1.ExitCode -ne 0) { Write-Host "[WARN] AprNes.exe exited with $($p1.ExitCode)" -ForegroundColor Red }

# ── 2+3. .NET 8 RyuJIT + AOT DLL ─────────────────────────────────────────────
Write-Host "[2/4] .NET 8 RyuJIT  +  [3/4] AOT DLL ..." -ForegroundColor Yellow
$p2 = Start-Process -FilePath $dotnet8Exe -ArgumentList @("--benchmark", "`"$rom`"", $seconds, "`"$output`"") -Wait -PassThru -NoNewWindow
if ($p2.ExitCode -ne 0) { Write-Host "[WARN] AprNesAOT.exe exited with $($p2.ExitCode)" -ForegroundColor Red }

# ── 4. .NET 10 RyuJIT ────────────────────────────────────────────────────────
Write-Host "[4/4] .NET 10 RyuJIT ..." -ForegroundColor Yellow
$p3 = Start-Process -FilePath $dotnet10Exe -ArgumentList @("--benchmark", "`"$rom`"", $seconds, "`"$output`"") -Wait -PassThru -NoNewWindow
if ($p3.ExitCode -ne 0) { Write-Host "[WARN] AprNesAOT10.exe exited with $($p3.ExitCode)" -ForegroundColor Red }

# ── 顯示結果 ──────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host " Results:" -ForegroundColor Green
Get-Content $output | Write-Host
Write-Host "============================================================" -ForegroundColor Green
Write-Host " Saved to: $output" -ForegroundColor Green

Write-Host ""
Write-Host "請按任意鍵繼續 . . ." -NoNewline
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
