@echo off
setlocal
title Mux Deck - Install Runtime
cd /d "%~dp0"

rem Prerequisite: install Python 3.8+ yourself (https://www.python.org/downloads/, check "Add to PATH").
rem Downloads MKVToolNix / ffmpeg / assfonts into bin\ (skips what already exists).
rem Proxy: pass --proxy http://127.0.0.1:7890 or set BOOTSTRAP_PROXY.

py -3 scripts\bootstrap.py %*
if errorlevel 1 (
  echo.
  echo [FAILED] Runtime install incomplete, see messages above.
) else (
  echo.
  echo [DONE] Runtime ready. Run start_mux_ui.bat to start.
)
echo.
pause
