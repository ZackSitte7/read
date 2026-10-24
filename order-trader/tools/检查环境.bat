@echo off
rem ===========================================================================
rem  Order Trader v1.0 - environment self check (run from package root)
rem  Writes logs\selftest.txt with per-module import timings and a live market
rem  data connection test, so a slow/hanging startup can be pinpointed.
rem ===========================================================================
chcp 65001 >nul
cd /d "%~dp0"
set PYTHONIOENCODING=utf-8
set PYTHONUTF8=1
set PYTHONDONTWRITEBYTECODE=1
set OPENBLAS_NUM_THREADS=1
set OMP_NUM_THREADS=1

if not exist "logs" mkdir "logs"
if not exist "python\python.exe" goto nopython

echo Running self check, please wait (first run may take 1-2 minutes)...
"python\python.exe" -u "bridge\tqsdk_bridge.py" --selftest > "logs\selftest.txt" 2>&1
type "logs\selftest.txt"
echo.
echo Saved to logs\selftest.txt
echo.
pause
exit /b 0

:nopython
echo [ERROR] python\python.exe not found.
echo Put this file in the Order Trader package root (next to OrderTrader.exe).
echo.
pause
exit /b 1
