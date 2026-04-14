@echo off
REM 1x native, no image filter, no audio (audio-mode 0)
"C:\ai_project\AprNes\AprNes\bin\Debug\AprNes.exe" --rom "C:\ai_project\AprNes\AprNes\bin\Debug\tools\benchmark\ny2011.nes" --benchmark 30 --region NTSC --audio-mode 0
