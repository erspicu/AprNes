##############################################################
# capture_ui.ps1 — 啟動 WinForms app，截圖視窗後關閉
# 用法: .\tools\capture_ui.ps1 -ExePath "path\to\app.exe" -OutPath "screenshot.png"
##############################################################
param(
    [string]$ExePath,
    [string]$OutPath = "tools\screenshot.png",
    [int]$WaitMs = 3000
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Drawing;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("gdi32.dll")]  public static extern bool BitBlt(IntPtr hdc, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public const uint SWP_NOSIZE = 0x0001;
}
"@

Write-Host "Launching: $ExePath"
$proc = Start-Process -FilePath $ExePath -PassThru -ErrorAction Stop

Write-Host "Waiting ${WaitMs}ms for window to appear..."
Start-Sleep -Milliseconds $WaitMs

# Poll for main window handle (up to 5s)
$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 20; $i++) {
    $proc.Refresh()
    if ($proc.MainWindowHandle -ne [IntPtr]::Zero) {
        $hwnd = $proc.MainWindowHandle
        break
    }
    Start-Sleep -Milliseconds 250
}

if ($hwnd -eq [IntPtr]::Zero) {
    Write-Warning "Could not find window handle"
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

# Move window to top-left (10,10) and bring to front
[Win32]::SetWindowPos($hwnd, [Win32]::HWND_TOPMOST, 10, 10, 0, 0, [Win32]::SWP_NOSIZE) | Out-Null
[Win32]::ShowWindow($hwnd, 9) | Out-Null   # SW_RESTORE
[Win32]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 800

# Get window dimensions
$rect = New-Object Win32+RECT
[Win32]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$w = $rect.Right  - $rect.Left
$h = $rect.Bottom - $rect.Top
Write-Host "Window rect: $($rect.Left),$($rect.Top)  ${w}x${h}"

# Use PrintWindow to capture window content (works even if partially obscured)
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g   = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[Win32]::PrintWindow($hwnd, $hdc, 2) | Out-Null   # PW_RENDERFULLCONTENT=2
$g.ReleaseHdc($hdc)

$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "Saved: $OutPath"

# Close the app
try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch {}
Write-Host "Done."
