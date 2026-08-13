@echo off
REM 一键注册 MiniMaxUsageRefresh 任务计划
REM 以管理员身份运行 Install-Task.ps1

echo ========================================
echo  MiniMax Usage 任务计划注册工具
echo ========================================
echo.

cd /d "%~dp0"

powershell -ExecutionPolicy Bypass -File "Install-Task.ps1"

pause