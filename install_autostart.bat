@echo off
setlocal
title Mux Deck - Autostart Installer
cd /d "%~dp0"
py -3 app\tools\autostart.py install
echo.
pause
