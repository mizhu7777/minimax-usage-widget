@echo off
REM 一键注册 MiniMaxUsageRefresh 任务计划
REM P2-3 修复:本 bat 本身不提权,直接 powershell 会被系统 UAC 拦截;
REM 改用 Install-Task-Admin.vbs 触发 UAC 弹窗,以管理员身份运行 Install-Task.ps1

echo ========================================
echo  MiniMax Usage 任务计划注册工具
echo ========================================
echo.

cd /d "%~dp0"

REM 调起 VBS 触发 UAC
wscript.exe Install-Task-Admin.vbs

pause