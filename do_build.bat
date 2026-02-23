@echo off
cd /d C:\ai_project\AprNes\AprNes
"C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" AprNes.csproj /p:Configuration=Debug /t:Rebuild /nologo > ..\build_out.txt 2>&1
if %ERRORLEVEL%==0 (echo BUILD_OK >> ..\build_out.txt) else (echo BUILD_FAIL >> ..\build_out.txt)
