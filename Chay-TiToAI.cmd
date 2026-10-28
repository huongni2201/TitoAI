@echo off
setlocal
cd /d "%~dp0"

powershell.exe -NoProfile -Command "if (Get-Process -Name 'TiToAI.Desktop' -ErrorAction SilentlyContinue) { exit 1 }"
if errorlevel 1 (
    echo Hay dong TiToAI dang chay truoc khi mo ban moi.
    pause
    exit /b 1
)

echo Dang build TiToAI va giao dien moi nhat...
dotnet build "src\TiToAI.Desktop\TiToAI.Desktop.csproj" -m:1
if errorlevel 1 (
    echo Build that bai. TiToAI chua duoc khoi chay.
    pause
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "tests\TiToAI.Desktop.SmokeChecks\VerifyWebUiPackaging.ps1" -OutputDirectory "src\TiToAI.Desktop\bin\Debug\net10.0-windows"
if errorlevel 1 (
    echo Kiem tra giao dien that bai. TiToAI chua duoc khoi chay.
    pause
    exit /b 1
)

start "" "src\TiToAI.Desktop\bin\Debug\net10.0-windows\TiToAI.Desktop.exe"
exit /b 0
