@echo off
rem ---------------------------------------------------------------------------
rem  Couchtop emergency recovery script
rem  Restores Windows Explorer as your shell WITHOUT needing .NET or Couchtop.
rem  Run it from Task Manager (Ctrl+Alt+Del > Task Manager > Run new task > cmd),
rem  from Safe Mode, or by double-clicking it.
rem ---------------------------------------------------------------------------
title Couchtop Recovery
echo Restoring Windows Explorer for user %USERNAME% ...

taskkill /f /im Couchtop.Guardian.exe >nul 2>&1
taskkill /f /im Couchtop.exe >nul 2>&1

reg query "HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Shell >nul 2>&1
if %errorlevel%==0 (
  reg delete "HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Shell /f
  echo Removed the per-user shell override.
) else (
  echo No per-user shell override was set.
)

reg add "HKCU\Software\Couchtop\Shell" /v Enabled /t REG_SZ /d False /f >nul 2>&1
reg delete "HKCU\Software\Couchtop\Shell" /v PendingBoots /f >nul 2>&1

tasklist /fi "imagename eq explorer.exe" 2>nul | find /i "explorer.exe" >nul
if errorlevel 1 (
  start "" "%windir%\explorer.exe"
  echo Started Windows Explorer.
)

echo.
echo Done. Windows Explorer will also be used at your next sign-in.
if /i not "%~1"=="/quiet" pause
