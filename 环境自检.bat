@echo off
setlocal
title Mux Deck - Self Check
cd /d "%~dp0"
py -3 scripts\selfcheck.py
echo.
pause
