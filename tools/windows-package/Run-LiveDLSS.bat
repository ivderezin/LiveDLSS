@echo off
cd /d "%~dp0LiveDLSSServer"
LiveDLSSServer.exe %*
echo.
echo Server exited with code %ERRORLEVEL%.
pause
