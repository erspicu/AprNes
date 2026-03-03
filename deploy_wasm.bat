@echo off
setlocal
echo [Deploy] AprNesWasm - GitHub Pages
echo.

REM ── 前置檢查 ────────────────────────────────────────────────
if not exist "publish_wasm\wwwroot\index.html" (
    echo [ERROR] publish_wasm\wwwroot not found.
    echo         Please run build_wasm.bat first.
    exit /b 1
)

echo === GitHub Pages ===
echo.

REM 建立暫存部署目錄
if exist "deploy_tmp" rmdir /s /q deploy_tmp
mkdir deploy_tmp
xcopy /E /I /Q publish_wasm\wwwroot deploy_tmp >nul

REM GitHub Pages 部署在子路徑 /AprNes/，需修正 base href
powershell -NoProfile -Command "(Get-Content 'deploy_tmp\index.html') -replace '<base href=\"/\" />', '<base href=\"/AprNes/\" />' | Set-Content 'deploy_tmp\index.html'"

REM SPA fallback：404 導回 index.html
copy /Y deploy_tmp\index.html deploy_tmp\404.html >nul

REM 防止 Jekyll 忽略 _framework 資料夾
echo. > deploy_tmp\.nojekyll

REM 用獨立 git repo 強推 gh-pages 分支
cd deploy_tmp
git init -b gh-pages -q
git config core.autocrlf false
git add -A
git commit -q -m "Deploy AprNesWasm to GitHub Pages"
git remote add origin https://github.com/erspicu/AprNes.git
git push -f origin gh-pages
cd ..

rmdir /s /q deploy_tmp

echo.
echo [Done] Deployment complete.
echo        GitHub Pages: https://erspicu.github.io/AprNes/

