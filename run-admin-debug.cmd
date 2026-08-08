@echo off
setlocal
cd /d "%~dp0"
if not exist ".verification" mkdir ".verification"
del /q ".verification\deck-drop-runtime.log" 2>nul
set "EXE=%~dp0RELYR\bin\Debug\net10.0-windows\win-x64\RELYR.exe"
if not exist "%EXE%" (
  echo Debug executable was not found. Build RELYR in Debug first.
  pause
  exit /b 1
)
set "RELYR_DROP_DIAGNOSTICS=1"
echo Starting RELYR elevated for the admin drag-and-drop test.
echo Accept the UAC prompt, then do the same Explorer-to-Deck drop.
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$p=Start-Process -FilePath '%EXE%' -ArgumentList '--normal-debug','--skip-setup','--drop-diagnostics' -Verb RunAs -PassThru -Wait; exit $p.ExitCode"
echo.
echo ---- Deck drop diagnostic log ----
if exist ".verification\deck-drop-runtime.log" (
  type ".verification\deck-drop-runtime.log"
) else (
  echo No WM_DROPFILES message was recorded.
)
echo ----------------------------------
pause
