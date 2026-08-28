@echo off
setlocal
title Mux Deck - 环境自检
cd /d "%~dp0"
set "FAIL=0"

echo ============================================
echo   Mux Deck 环境自检
echo ============================================

rem ---- Python ----
set "PY="
if exist "%CD%\bin\python\python.exe" set "PY=%CD%\bin\python\python.exe"
if not defined PY py -3 -c exit() >nul 2>&1 && set "PY=py -3"
if not defined PY where python >nul 2>&1 && set "PY=python"
if defined PY (
  echo [PASS] Python: %PY%
) else (
  echo [FAIL] Python: 未找到（缺 bin\python 或系统 Python）
  set /a FAIL+=1
)

rem ---- mkvmerge ----
set "MKV="
if exist "%CD%\bin\mkvtoolnix\mkvmerge.exe" set "MKV=%CD%\bin\mkvtoolnix\mkvmerge.exe"
if not defined MKV if exist "C:\Program Files\MKVToolNix\mkvmerge.exe" set "MKV=C:\Program Files\MKVToolNix\mkvmerge.exe"
if not defined MKV where mkvmerge >nul 2>&1 && set "MKV=mkvmerge"
if defined MKV (
  echo [PASS] mkvmerge: %MKV%
) else (
  echo [FAIL] mkvmerge: 未找到（缺 bin\mkvtoolnix 或系统安装）
  set /a FAIL+=1
)

rem ---- mkvextract ----
set "MKE="
if exist "%CD%\bin\mkvtoolnix\mkvextract.exe" set "MKE=%CD%\bin\mkvtoolnix\mkvextract.exe"
if not defined MKE if exist "C:\Program Files\MKVToolNix\mkvextract.exe" set "MKE=C:\Program Files\MKVToolNix\mkvextract.exe"
if not defined MKE where mkvextract >nul 2>&1 && set "MKE=mkvextract"
if defined MKE (
  echo [PASS] mkvextract: %MKE%
) else (
  echo [FAIL] mkvextract: 未找到
  set /a FAIL+=1
)

rem ---- ffmpeg ----
set "FF="
if exist "%CD%\bin\ffmpeg\bin\ffmpeg.exe" set "FF=%CD%\bin\ffmpeg\bin\ffmpeg.exe"
if not defined FF where ffmpeg >nul 2>&1 && set "FF=ffmpeg"
if defined FF (
  echo [PASS] ffmpeg: %FF%
) else (
  echo [FAIL] ffmpeg: 未找到（缺 bin\ffmpeg 或 PATH）
  set /a FAIL+=1
)

rem ---- assfonts ----
set "AF="
if exist "%CD%\bin\assfonts\assfonts.exe" set "AF=%CD%\bin\assfonts\assfonts.exe"
if not defined AF where assfonts >nul 2>&1 && set "AF=assfonts"
if defined AF (
  echo [PASS] assfonts: %AF%
) else (
  echo [FAIL] assfonts: 未找到（缺 bin\assfonts\assfonts.exe）
  set /a FAIL+=1
)

rem ---- PowerShell（Windows 内置）----
if exist "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" (
  echo [PASS] PowerShell 5.1: 系统内置
) else (
  echo [FAIL] PowerShell: 未找到
  set /a FAIL+=1
)

rem ---- 端口 8765 ----
netstat -ano | findstr ":8765" | findstr "LISTENING" >nul
if errorlevel 1 (
  echo [PASS] 端口 8765: 空闲
) else (
  echo [WARN] 端口 8765: 已被占用（服务会自动试 8766-8774，但启动脚本探测只认 8765）
)

echo.
if %FAIL%==0 (
  echo 全部关键依赖就绪，可以运行 start_mux_ui.bat 启动。
) else (
  echo 有 %FAIL% 项缺失，请按上方 [FAIL] 提示补齐后再启动。
)
echo.
pause
