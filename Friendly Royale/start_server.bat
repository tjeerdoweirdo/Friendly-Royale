@echo off
echo Starting Friendly Royale Dedicated Server...
echo.
echo Server Configuration:
echo - Port: 7777
echo - Max Players: 2
echo - Connection: 0.0.0.0:7777
echo.
echo Starting server...

REM Change directory to the built game folder
pushd "%~dp0Bulds"

REM Launch the dedicated server headless and write logs to server.log
"Friendly Royale.exe" -batchmode -nographics -server -logFile server.log

REM Return to original directory
popd

pause