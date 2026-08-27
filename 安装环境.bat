@echo off
setlocal
chcp 65001 >nul
title Mux Deck - 安装便携环境
cd /d "%~dp0"

echo ============================================
echo   Mux Deck 便携环境安装
echo ============================================
echo 将自动下载并解压：
echo   [1/4] Python 3.12.8 embeddable
echo   [2/4] MKVToolNix 101.0 (mkvmerge/mkvextract)
echo   [3/4] ffmpeg essentials (含 libass)
echo   [4/4] assfonts v0.7.3
echo.
echo 需要代理时：编辑本文件，将下方 PROXY 变量改为你的代理地址（如 http://127.0.0.1:7890）
echo 或用环境变量 BOOTSTRAP_PROXY 指定。
echo.
set "PROXY="
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\bootstrap.ps1" -Proxy %PROXY%
echo.
if errorlevel 1 (
  echo [失败] 环境安装未完成，请查看上方错误信息。
) else (
  echo [完成] 便携环境就绪，可运行 start_mux_ui.bat 启动，
  echo        或先运行 环境自检.bat 验证。
)
echo.
pause
