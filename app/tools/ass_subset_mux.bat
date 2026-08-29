@echo off
setlocal
cd /d "%~dp0"
if "%~1"=="" (
  echo Usage: ass_subset_mux.bat "target folder"
  echo The folder should contain MKV + .sc.ass/.tc.ass + Fonts folder.
  echo Default mode replaces original MKV; originals are kept in __mux_tmp_manual next to the video.
  echo For a separate output dir: py -3 app\tools\batch_cli.py --root "folder" --out-dir "out"
  pause
  exit /b 1
)
py -3 "%~dp0batch_cli.py" --root "%~1"
if errorlevel 1 (
  echo.
  echo Failed, see messages above.
  pause
  exit /b 1
)
echo.
echo Done.
pause
