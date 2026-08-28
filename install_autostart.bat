@echo off
setlocal
title Mux Deck - Autostart Installer
cd /d "%~dp0"
py -3 scripts\autostart.py install
echo.
pause
