@echo off
setlocal
title Mux Deck - Self Check
cd /d "%~dp0"
py -3 app\tools\selfcheck.py
echo.
pause
