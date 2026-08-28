@echo off
setlocal
title Mux Deck - Subtitle Muxer
cd /d "%~dp0"

rem already running? -> open the browser only when /api/version really returns ok
powershell -NoProfile -Command "try{$r=Invoke-WebRequest 'http://127.0.0.1:8765/api/version' -UseBasicParsing -TimeoutSec 2;$j=$r.Content|ConvertFrom-Json;if($j.ok){exit 0}}catch{};exit 1" >nul 2>&1
if not errorlevel 1 (
  start "" "http://127.0.0.1:8765"
  echo Server already running - browser opened.
  timeout /t 3 >nul
  exit /b
)

rem ---- locate python: bundled portable -> py -3 -> where python -> original hardcoded path ----
set "PY="
if exist "%~dp0..\bin\python\python.exe" set "PY=%~dp0..\bin\python\python.exe"
if not defined PY py -3 -c exit() >nul 2>&1 && set "PY=py -3"
if not defined PY where python >nul 2>&1 && set "PY=python"
if not defined PY if exist "C:\Users\ZhenXun\AppData\Local\Programs\Python\Python312\python.exe" set "PY=C:\Users\ZhenXun\AppData\Local\Programs\Python\Python312\python.exe"
if not defined PY (
  echo Python not found. Tried: ^"py -3^", ^"python^" on PATH and the hardcoded path.
  echo Install Python 3 with ^"Add to PATH^" checked, or edit the hardcoded path at the top of this script.
  pause
  exit /b 1
)

echo Starting server, browser opens in 2s...
start "" /min %PY% "%~dp0app\server.py"
timeout /t 2 /nobreak >nul
start "" "http://127.0.0.1:8765"
echo Server started: http://127.0.0.1:8765  ^(close the minimized window to stop^)
timeout /t 4 >nul
