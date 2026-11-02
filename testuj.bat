@echo off
setlocal
where pwsh >nul 2>nul
if %ERRORLEVEL% neq 0 (
  echo [ERROR] Nie znaleziono pwsh.
  exit /b 1
)
pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\BuildTools.ps1" -Mode test %*
if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%
