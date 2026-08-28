@echo off
setlocal
chcp 65001 >nul
REM ============================================
REM  自动子集字幕并封装进 MKV（ass_subset_mux.ps1 的批处理入口）
REM  用法：ass_subset_mux.bat "目标目录"
REM     目标目录内应包含 MKV + .sc.ass/.tc.ass + Fonts（或 Font）文件夹
REM  默认模式（替换）：输出成功后，原 MKV 移入视频旁的 __mux_tmp_manual 备份目录，
REM     新文件占用原名；备份目录同名冲突自动加序号，永不覆盖已有备份。
REM  如需输出到其他目录，请直接使用 PowerShell：
REM     .\ass_subset_mux.ps1 -Root "目录" -OutDir "输出目录"
REM ============================================
if "%~1"=="" (
  echo 用法：参数是包含 MKV + .sc.ass/.tc.ass + Fonts 文件夹的目标目录：
  echo   ass_subset_mux.bat "目标目录"
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0ass_subset_mux.ps1" -Root "%~1"
if %ERRORLEVEL% NEQ 0 (
  echo.
  echo 处理失败（代码 %ERRORLEVEL%），请查看上方错误信息。
  pause
  exit /b %ERRORLEVEL%
)
echo.
echo 处理完成。按任意键退出...
pause >nul
