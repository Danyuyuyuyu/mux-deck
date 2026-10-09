@echo off
setlocal
cd /d "%~dp0desktop"
where dotnet >nul 2>&1
if errorlevel 1 (
  echo .NET 10 SDK not found. Install it from https://dotnet.microsoft.com/download
  pause
  exit /b 1
)
dotnet run --project src\MuxDeck.Desktop -- --root "%~dp0."
if errorlevel 1 pause
