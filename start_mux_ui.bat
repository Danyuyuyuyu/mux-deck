@echo off
setlocal
title Mux Deck - Subtitle Muxer
cd /d "%~dp0"

rem ---- locate python: py -3 -> python on PATH ----
set "PY="
py -3 -c exit() >nul 2>&1 && set "PY=py -3"
if not defined PY where python >nul 2>&1 && set "PY=python"
if not defined PY (
  echo Python not found. Install Python 3.8+ with "Add to PATH" checked.
  pause
  exit /b 1
)

rem already running? -> open the browser only when /api/version really returns ok
%PY% -c "import urllib.request,sys,json;sys.exit(0 if json.load(urllib.request.urlopen('http://127.0.0.1:8765/api/version',timeout=2)).get('ok') else 1)" >nul 2>&1
if not errorlevel 1 (
  start "" "http://127.0.0.1:8765"
  echo Server already running - browser opened.
  timeout /t 3 >nul
  exit /b
)

echo Starting server, browser opens in 2s...
start "" /min %PY% "%~dp0app\server.py"
timeout /t 2 /nobreak >nul
start "" "http://127.0.0.1:8765"
echo Server started: http://127.0.0.1:8765  (close the minimized window to stop)
timeout /t 4 >nul
