# Install-Task.ps1
# 注册/卸载 Windows 任务计划，让 Update-MinimaxUsage.ps1 每 1 分钟执行一次
# 用法:
#   注册: powershell -ExecutionPolicy Bypass -File scripts\Install-Task.ps1
#   卸载: powershell -ExecutionPolicy Bypass -File scripts\Install-Task.ps1 -Uninstall

[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$NoRun
)

$ErrorActionPreference = 'Stop'

$taskName = 'MiniMaxUsageRefresh'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$updateScript = Join-Path $scriptDir 'Update-MinimaxUsage.vbs'
$xmlFile = Join-Path $scriptDir 'MiniMaxUsageRefresh.xml'

# === 复用函数：生成 Rainmeter 路径变量文件 ===
function Write-RainmeterVariables {
    param([string]$ProjectDir, [string]$SkinDir)
    $resources = Join-Path $SkinDir '@Resources'
    New-Item -ItemType Directory -Path $resources -Force | Out-Null
    $projectUri = $ProjectDir.Replace('\', '/')
    $detailExe = Join-Path $ProjectDir 'dist\win-x64\MiniMaxUsage.exe'
    $content = @"
[Variables]
ProjectDir=$ProjectDir
ProjectDirUri=$projectUri
DetailExe=$detailExe
NormalColor=37,131,247,255
WeeklyColor=139,92,246,255
WarningColor=245,158,11,255
CriticalColor=239,68,68,255
TrackColor=90,113,136,36
"@
    [IO.File]::WriteAllText((Join-Path $resources 'Variables.inc'), $content, [Text.UTF8Encoding]::new($false))
    Write-Host "[OK] Rainmeter 变量已写入 $resources\Variables.inc" -ForegroundColor Green
}

function Invoke-InstallTask {
    param([switch]$Uninstall)
# === 卸载 ===
if ($Uninstall) {
    Write-Host "卸载任务计划: $taskName" -ForegroundColor Cyan
    $existing = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($existing) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        Write-Host '[OK] 任务计划已卸载' -ForegroundColor Green
    } else {
        Write-Host '任务计划不存在，无需卸载' -ForegroundColor Yellow
    }
    if (Test-Path $xmlFile) { Remove-Item $xmlFile -Force }
    exit 0
}

# === 注册 ===
Write-Host "注册任务计划: $taskName" -ForegroundColor Cyan
Write-Host "脚本路径: $updateScript"
Write-Host ''

# 如果已存在，先删除
$existing = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host '检测到已存在的任务，先卸载...' -ForegroundColor Yellow
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}

# 关键修复：手动拼装字节，UTF-16 LE BOM + 内容。先用 ASCII 编码器构造内容，
# 再转换为 UTF-16 LE 字节流，最后前置 BOM (FF FE)。这种方式彻底绕开
# PowerShell 5.1 的 Out-File / Get-Content 字符串处理破坏 XML 的问题。
$startBoundary = (Get-Date).AddSeconds(15).ToString('yyyy-MM-ddTHH:mm:ss')

# 在 XML 之前定义 currentUser，以便 here-string 插值
$currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name

$xmlBody = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Author>MiniMax Usage Widget</Author>
    <Description>每 1 分钟刷新 MiniMax API 用量缓存</Description>
  </RegistrationInfo>
  <Triggers>
    <TimeTrigger>
      <StartBoundary>$startBoundary</StartBoundary>
      <Enabled>true</Enabled>
      <Repetition>
        <Interval>PT1M</Interval>
        <Duration>P1D</Duration>
        <StopAtDurationEnd>false</StopAtDurationEnd>
      </Repetition>
    </TimeTrigger>
  </Triggers>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <WakeToRun>true</WakeToRun>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine>
    <ExecutionTimeLimit>PT1M</ExecutionTimeLimit>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>wscript.exe</Command>
      <Arguments>"$updateScript"</Arguments>
    </Exec>
  </Actions>
  <Principals>
    <Principal id="Author">
      <UserId>$currentUser</UserId>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
</Task>
"@

# 关键：用字节流写入 UTF-16 LE BOM + 内容，绕过 PowerShell 5.1 字符串处理 bug
$utf16 = [System.Text.Encoding]::Unicode  # UTF-16 LE
$contentBytes = $utf16.GetBytes($xmlBody)
$bom = [byte[]](0xFF, 0xFE)
$fullBytes = New-Object System.Collections.Generic.List[byte]
$fullBytes.AddRange($bom)
$fullBytes.AddRange($contentBytes)
[System.IO.File]::WriteAllBytes($xmlFile, $fullBytes.ToArray())

# 调用 schtasks /Create /XML（XML 内部已经包含正确的 Principal UserId = 当前用户）
$proc = Start-Process -FilePath 'schtasks.exe' `
    -ArgumentList @('/Create', '/TN', "`"$taskName`"", '/XML', "`"$xmlFile`"", '/F') `
    -Wait -PassThru -NoNewWindow -RedirectStandardError 'schtasks_stderr.txt' -RedirectStandardOutput 'schtasks_stdout.txt'

if ($proc.ExitCode -ne 0) {
    $errMsg = ''
    if (Test-Path 'schtasks_stderr.txt') { $errMsg = Get-Content 'schtasks_stderr.txt' -Raw }
    Write-Host "[ERR] schtasks 失败 (ExitCode=$($proc.ExitCode))" -ForegroundColor Red
    if ($errMsg) { Write-Host $errMsg.Trim() -ForegroundColor Red }
    if (Test-Path $xmlFile) { Remove-Item $xmlFile -Force }
    if (Test-Path 'schtasks_stderr.txt') { Remove-Item 'schtasks_stderr.txt' }
    if (Test-Path 'schtasks_stdout.txt') { Remove-Item 'schtasks_stdout.txt' }
    exit 1
}

# 清理临时文件
if (Test-Path $xmlFile) { Remove-Item $xmlFile -Force }
if (Test-Path 'schtasks_stderr.txt') { Remove-Item 'schtasks_stderr.txt' }
if (Test-Path 'schtasks_stdout.txt') { Remove-Item 'schtasks_stdout.txt' }

# 验证任务确实创建
$verifyTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if (-not $verifyTask) {
    Write-Host "[ERR] 任务未创建（schtasks 报告成功但查不到）" -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host '=== 安装成功 ===' -ForegroundColor Green
Write-Host "任务名称: $taskName"
Write-Host "触发频率: 每 1 分钟（15 秒后启动第一次）"
Write-Host "睡眠唤醒: 启用 (WakeToRun)"
Write-Host "电池模式: 启用 (AllowStartIfOnBatteries)"
Write-Host "账户: $currentUser (Highest 权限)"
Write-Host ''
Write-Host '查看: 任务计划程序 → 任务计划库 → 找到 "MiniMaxUsageRefresh"' -ForegroundColor Cyan
Write-Host '卸载: powershell -ExecutionPolicy Bypass -File scripts\Install-Task.ps1 -Uninstall' -ForegroundColor Cyan
exit 0
}

if ($NoRun) { return }

# 需要管理员权限（任务计划程序要求）
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)
if (-not $isAdmin) {
    Write-Host '错误：需要以管理员身份运行此脚本' -ForegroundColor Red
    Write-Host '右键 PowerShell → "以管理员身份运行" 后重试' -ForegroundColor Yellow
    exit 1
}
Invoke-InstallTask -Uninstall:$Uninstall
