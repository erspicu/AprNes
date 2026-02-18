# Find MSBuild via vswhere
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1

if (-not $msbuild) {
    Write-Host "[ERROR] MSBuild not found. Please install Visual Studio." -ForegroundColor Red
    exit 1
}

Write-Host "MSBuild: $msbuild" -ForegroundColor Cyan
Write-Host ""

$proj = "$PSScriptRoot\AprNes\AprNes.csproj"
$sep  = "=" * 60

# Build Debug|x64
Write-Host $sep -ForegroundColor Yellow
Write-Host " Building Debug|x64 ..." -ForegroundColor Yellow
Write-Host $sep -ForegroundColor Yellow
& $msbuild $proj /p:Configuration=Debug /p:Platform=x64 /m /nologo /v:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAILED] Debug|x64" -ForegroundColor Red
    exit 1
}
Write-Host "[OK] Debug|x64" -ForegroundColor Green
Write-Host ""

# Build Release|x64
Write-Host $sep -ForegroundColor Yellow
Write-Host " Building Release|x64 ..." -ForegroundColor Yellow
Write-Host $sep -ForegroundColor Yellow
& $msbuild $proj /p:Configuration=Release /p:Platform=x64 /m /nologo /v:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAILED] Release|x64" -ForegroundColor Red
    exit 1
}
Write-Host "[OK] Release|x64" -ForegroundColor Green
Write-Host ""

Write-Host $sep -ForegroundColor Green
Write-Host " All builds succeeded." -ForegroundColor Green
Write-Host " Debug  : AprNes\bin\Debug\AprNes.exe" -ForegroundColor Green
Write-Host " Release: AprNes\bin\Release\AprNes.exe" -ForegroundColor Green
Write-Host $sep -ForegroundColor Green
