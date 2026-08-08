@echo off
setlocal
cd /d "%~dp0"
if not exist ".verification" mkdir ".verification"
del /q ".verification\deck-drop-runtime.log" 2>nul
set "EXE=%~dp0RELYR\bin\Debug\net10.0-windows\win-x64\RELYR.exe"
set "RELYR_DROP_DIAGNOSTICS=1"
if not exist "%EXE%" (
  echo Debug executable was not found. Build RELYR in Debug first.
  pause
  exit /b 1
)
echo Starting RELYR in normal user mode. Do not use Run as administrator.
"%EXE%" --normal-debug --skip-setup --drop-diagnostics
echo.
echo ---- Deck drop diagnostic log ----
if exist ".verification\deck-drop-runtime.log" (
  type ".verification\deck-drop-runtime.log"
) else (
  echo No WM_DROPFILES message was recorded.
)
echo ----------------------------------
pause
