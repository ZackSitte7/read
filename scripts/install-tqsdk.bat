@echo off
chcp 65001 >nul
setlocal
title TqSdk 行情依赖安装

echo ============================================
echo   Replica Desktop - TqSdk 行情依赖安装
echo ============================================
echo.

where python >nul 2>&1
if errorlevel 1 (
  echo [错误] 未检测到 Python。
  echo 请先安装 Python 3.10 或更高版本，安装时勾选 "Add python.exe to PATH"。
  echo 下载地址: https://www.python.org/downloads/windows/
  echo.
  pause
  exit /b 1
)

for /f "delims=" %%v in ('python --version 2^>^&1') do echo 当前 %%v

echo.
echo 正在安装 / 更新 tqsdk（首次安装约 100MB，请耐心等待）...
python -m pip install --upgrade pip
python -m pip install --upgrade tqsdk
if errorlevel 1 (
  echo.
  echo [错误] 安装失败。可尝试国内镜像：
  echo python -m pip install -U tqsdk -i https://pypi.tuna.tsinghua.edu.cn/simple
  echo.
  pause
  exit /b 1
)

echo.
echo 验证 tqsdk ...
python -c "import tqsdk; print('tqsdk', tqsdk.__version__)"
if errorlevel 1 (
  echo [错误] tqsdk 导入失败，请检查安装日志。
  pause
  exit /b 1
)

echo.
echo 安装完成。现在可以启动 Replica.Desktop.exe，
echo 登录窗选择 "TqSdk 天勤行情" 与 "TqSdk 快期模拟"。
echo.
pause
