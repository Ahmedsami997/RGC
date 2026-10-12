@echo off
:: Double-click to install the RGC Agent on this PC (asks for administrator rights).
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Deploy-RGC-Agent.ps1"
echo.
echo If you see "Done." above, RGC is installed. Look for the RGC crown icon near the clock.
pause
