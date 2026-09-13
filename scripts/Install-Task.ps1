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

# P6 修复:UAC 提权上下文的用户可能不是桌面登录用户(例如用另一个管理员账户确认 UAC),
# 而 DPAPI 的 API Key 绑定桌面用户 —— 任务必须注册到交互登录用户名下,
# 否则定时刷新全部因解密失败报"API Key 无效"。通过 explorer.exe 属主探测真实桌面用户
function Get-InteractiveUser {
    try {
        $explorer = Get-Process -Name explorer -ErrorAction Stop | Select-Object -First 1
        $owner = Invoke-CimMethod -InputObject (Get-CimInstance -ClassName Win32_Process -Filter "ProcessId=$($explorer.Id)") -MethodName GetOwner
        if ($owner -and $owner.User) {
            if ($owner.Domain) { return "$($owner.Domain)\$($owner.User)" }
            return $owner.User
        }
    } catch { }
    return [Security.Principal.WindowsIdentity]::GetCurrent().Name
}

# P1-9 修复:Rainmeter 实际加载的是用户 Documents(或注册表 SkinPath)下的皮肤副本,
# 只更新仓库目录不会让用户皮肤生效。提权账户可能不是桌面用户,需解析交互用户的资料目录
function Get-InteractiveUserProfileDir {
    try {
        $sid = ([Security.Principal.NTAccount](Get-InteractiveUser)).Translate([Security.Principal.SecurityIdentifier]).Value
        $profilePath = (Get-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid" -Name ProfileImagePath -ErrorAction Stop).ProfileImagePath
        if ($profilePath) { return $profilePath }
    } catch { }
    return $env:USERPROFILE
}

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
# P6 修复:用交互登录用户而非提权上下文用户注册任务
$elevatedUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$currentUser = Get-InteractiveUser
if ($currentUser -ne $elevatedUser) {
    Write-Host "[WARN] 提权账户($elevatedUser)与桌面登录用户($currentUser)不同" -ForegroundColor Yellow
    Write-Host "       任务将注册到桌面用户 $currentUser 名下（API Key 由该用户 DPAPI 加密）" -ForegroundColor Yellow
}

$xmlBody = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Author>MiniMax Usage Widget</Author>
    <Description>每 1 分钟刷新 MiniMax API 用量缓存</Description>
    <!-- M-7 修复:带版本号,便于排查"用户跑的是老版任务定义还是新版" -->
    <Version>2</Version>
  </RegistrationInfo>
  <Triggers>
    <TimeTrigger>
      <StartBoundary>$startBoundary</StartBoundary>
      <Enabled>true</Enabled>
      <Repetition>
        <Interval>PT1M</Interval>
      </Repetition>
    </TimeTrigger>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <Delay>PT15S</Delay>
    </LogonTrigger>
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
    <ExecutionTimeLimit>PT2M</ExecutionTimeLimit>
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
      <LogonType>Interactive</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
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

# P2-4 修复:schtasks 重定向文件用 $PSScriptRoot,避免污染 C:\Windows\System32
# P2-5 修复:Start-Process 包 try/catch,失败给友好提示
$stderrLog = Join-Path $scriptDir 'schtasks_stderr.txt'
$stdoutLog = Join-Path $scriptDir 'schtasks_stdout.txt'
try {
    $proc = Start-Process -FilePath 'schtasks.exe' `
        -ArgumentList @('/Create', '/TN', "`"$taskName`"", '/XML', "`"$xmlFile`"", '/F') `
        -Wait -PassThru -NoNewWindow -RedirectStandardError $stderrLog -RedirectStandardOutput $stdoutLog
} catch {
    Write-Host "[ERR] 启动 schtasks 失败: $_" -ForegroundColor Red
    if (Test-Path $xmlFile) { Remove-Item $xmlFile -Force }
    exit 1
}

if ($proc.ExitCode -ne 0) {
    $errMsg = ''
    if (Test-Path $stderrLog) { $errMsg = Get-Content $stderrLog -Raw }
    Write-Host "[ERR] schtasks 失败 (ExitCode=$($proc.ExitCode))" -ForegroundColor Red
    if ($errMsg) { Write-Host $errMsg.Trim() -ForegroundColor Red }
    if (Test-Path $xmlFile) { Remove-Item $xmlFile -Force }
    if (Test-Path $stderrLog) { Remove-Item $stderrLog }
    if (Test-Path $stdoutLog) { Remove-Item $stdoutLog }
    exit 1
}

# 清理临时文件
if (Test-Path $xmlFile) { Remove-Item $xmlFile -Force }
if (Test-Path $stderrLog) { Remove-Item $stderrLog }
if (Test-Path $stdoutLog) { Remove-Item $stdoutLog }

# 验证任务确实创建
$verifyTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if (-not $verifyTask) {
    Write-Host "[ERR] 任务未创建（schtasks 报告成功但查不到）" -ForegroundColor Red
    exit 1
}

# P1-6 + N4 修复:注册成功后,自动生成 Rainmeter 皮肤所需的 Variables.inc
# $scriptDir 已是 scripts/ 的父目录,只需 1 次 Split-Path 即可回到项目根
$repoRoot = Split-Path -Parent $scriptDir
$skinDir = Join-Path $repoRoot 'Skins\MiniMaxUsage'
if (Test-Path $skinDir) {
    try {
        Write-RainmeterVariables -ProjectDir $repoRoot -SkinDir $skinDir
        Write-Host "[OK]  Rainmeter Variables.inc 已生成于 SkinDir" -ForegroundColor DarkGray
    } catch {
        Write-Host "[WARN] 生成 Rainmeter 变量失败: $_" -ForegroundColor Yellow
        Write-Host "  请手动重新安装或单独运行 Write-RainmeterVariables" -ForegroundColor Yellow
    }
} else {
    Write-Host "[WARN] 未找到皮肤目录 $skinDir,跳过 Variables.inc 生成" -ForegroundColor Yellow
}

# P1-9 修复:同步 Variables.inc 到 Rainmeter 实际加载的皮肤目录(注册表 SkinPath 优先,
# 其次交互用户与提权用户的 Documents\Rainmeter\Skins),已安装皮肤才会显示新数据
$rainmeterRoots = @()
$regSkinPath = (Get-ItemProperty -Path 'HKCU:\Software\Rainmeter' -Name SkinPath -ErrorAction SilentlyContinue).SkinPath
if ($regSkinPath) { $rainmeterRoots += $regSkinPath }
$rainmeterRoots += Join-Path (Get-InteractiveUserProfileDir) 'Documents\Rainmeter\Skins'
$rainmeterRoots += Join-Path $env:USERPROFILE 'Documents\Rainmeter\Skins'
foreach ($skinsRoot in ($rainmeterRoots | Select-Object -Unique)) {
    $installedSkin = Join-Path $skinsRoot 'MiniMaxUsage'
    if (Test-Path (Join-Path $installedSkin 'MiniMaxUsage.ini')) {
        try {
            Write-RainmeterVariables -ProjectDir $repoRoot -SkinDir $installedSkin
            Write-Host "[OK]  已同步 Rainmeter 皮肤变量: $installedSkin\@Resources\Variables.inc" -ForegroundColor Green
        } catch {
            Write-Host "[WARN] 同步 Rainmeter 变量到 $installedSkin 失败: $_" -ForegroundColor Yellow
        }
    }
}

Write-Host ''
Write-Host '=== 安装成功 ===' -ForegroundColor Green
Write-Host "任务名称: $taskName"
Write-Host "触发频率: 每 1 分钟（15 秒后启动第一次）"
Write-Host "登录自启: 启用 (LogonTrigger,延迟 15s)"
Write-Host "睡眠唤醒: 启用 (WakeToRun)"
Write-Host "电池模式: 启用 (AllowStartIfOnBatteries)"
Write-Host "账户: $currentUser (最小权限)"
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
