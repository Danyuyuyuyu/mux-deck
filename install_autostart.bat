@echo off
setlocal
title Mux Deck - Autostart Installer

set "VBS=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\muxui.vbs"
set "SRV=%~dp0app\server.py"

rem ---- locate pythonw: bundled portable -> pyw -3 -> where pythonw -> original hardcoded path ----
set "PYW="
if exist "%~dp0..\bin\python\pythonw.exe" set "PYW=%~dp0..\bin\python\pythonw.exe"
if not defined PYW pyw -3 -c exit() >nul 2>&1 && set "PYW=pyw -3"
if not defined PYW where pythonw >nul 2>&1 && set "PYW=pythonw"
if not defined PYW if exist "C:\Users\ZhenXun\AppData\Local\Programs\Python\Python312\pythonw.exe" set "PYW=C:\Users\ZhenXun\AppData\Local\Programs\Python\Python312\pythonw.exe"
if not defined PYW (
  echo pythonw not found. Tried: ^"pyw -3^", ^"pythonw^" on PATH and the hardcoded path.
  echo Install Python 3 with ^"Add to PATH^" checked, or edit the hardcoded path at the top of this script.
  pause
  exit /b 1
)

rem ---- write the startup VBS in one shot (PowerShell Set-Content, no line-by-line echo; paths with special chars are safe) ----
powershell -NoProfile -ExecutionPolicy Bypass -Command "$q=[char]34; $cmd='%PYW% %SRV%'; Set-Content -LiteralPath '%VBS%' -Value @('Set ws = CreateObject('+$q+'WScript.Shell'+$q+')','ws.Run '+$q+$cmd+$q+', 0, False') -Encoding Ascii; if($?){exit 0}else{exit 1}"
if errorlevel 1 (
  echo Failed to write autostart script: "%VBS%"
  pause
  exit /b 1
)

echo Autostart installed (runs at next logon).
echo Starting server now...
start "" %PYW% "%SRV%"

rem ---- wait up to ~10s for the server, then show the URL ----
for /l %%i in (1,1,10) do (
  powershell -NoProfile -Command "try{$r=Invoke-WebRequest 'http://127.0.0.1:8765/api/version' -UseBasicParsing -TimeoutSec 1;$j=$r.Content|ConvertFrom-Json;if($j.ok){exit 0}}catch{};exit 1" >nul 2>&1
  if not errorlevel 1 goto ready
  timeout /t 1 /nobreak >nul
)
echo Server did not respond within ~10s (http://127.0.0.1:8765/api/version).
echo Check: pythonw works, port 8765 is free, and server.py starts without errors.
pause
exit /b 1

:ready
echo Server is up: open http://127.0.0.1:8765
echo To remove autostart, delete: "%VBS%"
pause
