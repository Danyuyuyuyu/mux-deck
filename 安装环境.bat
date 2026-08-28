@echo off
setlocal
title Mux Deck - 安装便携环境
cd /d "%~dp0"

echo ============================================
echo   Mux Deck 运行时安装
echo ============================================
echo 前提：请先自行安装 Python 3.8+（https://www.python.org/downloads/，
echo        勾选 Add to PATH；装好后在 cmd 里 py -3 可用即可）。
echo.
echo 本脚本将自动下载并解压：
echo   [1/3] MKVToolNix 101.0 (mkvmerge/mkvextract)
echo   [2/3] ffmpeg essentials (含 libass)
echo   [3/3] assfonts v0.7.3
echo.
echo 需要代理时：编辑本文件，将下方 PROXY 变量改为代理地址（如 http://127.0.0.1:7890）
echo 或用环境变量 BOOTSTRAP_PROXY 指定。
echo.
set "PROXY="
if defined PROXY (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\bootstrap.ps1" -Proxy %PROXY%
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\bootstrap.ps1"
)
echo.
if errorlevel 1 (
  echo [失败] 运行时安装未完成，请查看上方错误信息。
) else (
  echo [完成] 运行时就绪，可运行 start_mux_ui.bat 启动，
  echo        或先运行 环境自检.bat 验证。
)
echo.
pause
