@echo off
echo Starting Friendly Royale Dedicated Server...
echo.
echo Server Configuration:
echo - Port: 7777
echo - Max Players: 2
echo - Connection: 0.0.0.0:7777
echo.
echo Starting server...
echo.
echo Tip: To start from PowerShell directly, run:
echo   powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start_server.ps1" -Tail

REM Prefer using the PowerShell helper so you can auto-tail the log
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start_server.ps1" -Tail

pause