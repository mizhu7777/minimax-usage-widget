# MiniMax 双圆环小组件与 WPF 详情应用实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在保留现有 MiniMax PowerShell 采集链路的前提下，交付双圆环 Rainmeter 小组件、90 天额度快照，以及无需额外运行时的 WPF 自包含详情 EXE。

**Architecture:** `Update-MinimaxUsage.ps1` 仍是唯一采集者与数据写入者，输出当前状态、Rainmeter 文本值和 JSONL 历史。Rainmeter 与 WPF 只读这些数据；WPF 的手动刷新也只调用现有 PowerShell 脚本，不复制 HTTP 或凭据逻辑。

**Tech Stack:** PowerShell 5.1、Rainmeter INI/Lua、C#、WPF、.NET 8、xUnit、JSON/JSONL。

---

## 执行前环境说明

- 当前目录 `D:\claude_work\2026\07\05\minimax` **不是可用 Git 仓库**。执行本计划时不得运行 `git init`。
- 每个任务仍给出建议提交检查点。只有下面命令成功时才能执行对应提交：

```bash
git -C 'D:/claude_work/2026/07/05/minimax' rev-parse --is-inside-work-tree
```

预期：当前返回失败。失败时跳过该任务的 `git add/commit`，但必须保留测试输出作为检查点。

- 当前机器只有 .NET 9 运行时，没有任何 .NET SDK。Task 1 使用项目内 `.tools/dotnet` 安装 .NET 8 SDK，不修改全局 SDK；下载前需要正常的外网访问许可。
- 本计划不修改 `.config/apikey.enc`，测试不得读取真实 API Key。

## 文件结构

### 修改

- `.gitignore`：忽略本地 SDK、发布物、视觉讨论临时文件。
- `scripts/Update-MinimaxUsage.ps1`：schema v2、稳定 ID、历史快照、双圆环文本缓存。
- `scripts/Install-Task.ps1`：写 Rainmeter 路径变量并保留现有任务计划行为。
- `scripts/Install-Task-Admin.vbs`：移除开发机绝对路径。
- `Skins/MiniMaxUsage/MiniMaxUsage.ini`：双圆环界面、状态颜色、详情 EXE 启动。
- `README.md`：安装、数据口径、详情页和排错说明。

### 创建

- `tests/powershell/TestHarness.ps1`：无第三方依赖的 PowerShell 测试运行器。
- `tests/powershell/Update-MinimaxUsage.Tests.ps1`：采集、schema、历史和文本缓存测试。
- `tests/powershell/RainmeterSkin.Tests.ps1`：皮肤结构与路径静态测试。
- `Skins/MiniMaxUsage/@Resources/Variables.inc`：安装脚本生成的路径变量默认模板。
- `Skins/MiniMaxUsage/@Resources/Freshness.lua`：根据最后成功时间判断正常、过期、错误。
- `MiniMaxUsage.sln`：.NET solution。
- `src/MiniMaxUsage.App/MiniMaxUsage.App.csproj`：WPF 自包含应用项目。
- `src/MiniMaxUsage.App/App.xaml`
- `src/MiniMaxUsage.App/App.xaml.cs`
- `src/MiniMaxUsage.App/MainWindow.xaml`
- `src/MiniMaxUsage.App/MainWindow.xaml.cs`
- `src/MiniMaxUsage.App/Models/UsageDocuments.cs`：JSON 文档模型。
- `src/MiniMaxUsage.App/Models/UsageState.cs`：UI 使用的稳定领域模型。
- `src/MiniMaxUsage.App/Services/UsageCacheReader.cs`
- `src/MiniMaxUsage.App/Services/HistoryReader.cs`
- `src/MiniMaxUsage.App/Services/TrendSeriesBuilder.cs`
- `src/MiniMaxUsage.App/Services/ProjectLocator.cs`
- `src/MiniMaxUsage.App/Services/PowerShellRefreshService.cs`
- `src/MiniMaxUsage.App/ViewModels/MainViewModel.cs`
- `src/MiniMaxUsage.App/Controls/CircularGauge.cs`
- `src/MiniMaxUsage.App/Controls/TrendChart.cs`
- `tests/MiniMaxUsage.App.Tests/MiniMaxUsage.App.Tests.csproj`
- `tests/MiniMaxUsage.App.Tests/TempDirectory.cs`
- `tests/MiniMaxUsage.App.Tests/UsageCacheReaderTests.cs`
- `tests/MiniMaxUsage.App.Tests/HistoryReaderTests.cs`
- `tests/MiniMaxUsage.App.Tests/TrendSeriesBuilderTests.cs`
- `tests/MiniMaxUsage.App.Tests/ProjectLocatorTests.cs`
- `tests/MiniMaxUsage.App.Tests/PowerShellRefreshServiceTests.cs`
- `tests/MiniMaxUsage.App.Tests/MainViewModelTests.cs`
- `tests/MiniMaxUsage.App.Tests/CircularGaugeTests.cs`
- `scripts/Publish-App.ps1`：可重复的自包含发布脚本。

---

### Task 1: 准备项目内 .NET 8 构建环境与解决方案

**Files:**
- Modify: `.gitignore`
- Create: `MiniMaxUsage.sln`
- Create: `src/MiniMaxUsage.App/MiniMaxUsage.App.csproj`
- Create: `tests/MiniMaxUsage.App.Tests/MiniMaxUsage.App.Tests.csproj`

- [ ] **Step 1: 增加本地构建产物忽略规则**

在 `.gitignore` 末尾追加：

```gitignore

# 本地构建工具与发布物
.tools/
dist/
src/**/bin/
src/**/obj/
tests/**/bin/
tests/**/obj/

# 视觉设计讨论临时文件
.superpowers/
```

- [ ] **Step 2: 确认系统没有 .NET SDK**

Run:

```bash
dotnet --list-sdks
```

Expected: 无输出；`dotnet --info` 中显示 `No SDKs were found.`。

- [ ] **Step 3: 在获得下载许可后安装项目内 .NET 8 SDK**

Run:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "New-Item -ItemType Directory -Force '.tools' | Out-Null; Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile '.tools/dotnet-install.ps1'; & '.tools/dotnet-install.ps1' -Channel '8.0' -Quality 'GA' -InstallDir '.tools/dotnet'"
```

Expected: 结束信息包含 `Installation finished successfully.`。

- [ ] **Step 4: 验证本地 SDK**

Run:

```bash
'D:/claude_work/2026/07/05/minimax/.tools/dotnet/dotnet.exe' --list-sdks
```

Expected: 至少一行 `8.0.xxx [D:\claude_work\2026\07\05\minimax\.tools\dotnet\sdk]`。

- [ ] **Step 5: 创建 solution、WPF 项目和测试项目**

Run:

```bash
DOTNET='D:/claude_work/2026/07/05/minimax/.tools/dotnet/dotnet.exe'
"$DOTNET" new sln -n MiniMaxUsage
"$DOTNET" new wpf -n MiniMaxUsage.App -o src/MiniMaxUsage.App --framework net8.0
"$DOTNET" new xunit -n MiniMaxUsage.App.Tests -o tests/MiniMaxUsage.App.Tests --framework net8.0
"$DOTNET" sln MiniMaxUsage.sln add src/MiniMaxUsage.App/MiniMaxUsage.App.csproj
"$DOTNET" sln MiniMaxUsage.sln add tests/MiniMaxUsage.App.Tests/MiniMaxUsage.App.Tests.csproj
"$DOTNET" add tests/MiniMaxUsage.App.Tests/MiniMaxUsage.App.Tests.csproj reference src/MiniMaxUsage.App/MiniMaxUsage.App.csproj
```

Expected: 两个项目均显示 `added to the solution`，项目引用添加成功。

- [ ] **Step 6: 固定 WPF 发布属性与测试目标框架**

将 `src/MiniMaxUsage.App/MiniMaxUsage.App.csproj` 替换为：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <PublishTrimmed>false</PublishTrimmed>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <AssemblyName>MiniMaxUsage</AssemblyName>
    <RootNamespace>MiniMaxUsage.App</RootNamespace>
  </PropertyGroup>
</Project>
```

把测试项目的 `<TargetFramework>` 改为：

```xml
<TargetFramework>net8.0-windows</TargetFramework>
```

删除模板生成的 `tests/MiniMaxUsage.App.Tests/UnitTest1.cs`。

- [ ] **Step 7: 构建空项目**

Run:

```bash
'D:/claude_work/2026/07/05/minimax/.tools/dotnet/dotnet.exe' build MiniMaxUsage.sln
```

Expected: `Build succeeded.`、`0 Warning(s)`、`0 Error(s)`。

- [ ] **Step 8: 条件提交检查点**

如果工作区后来成为 Git 仓库，执行：

```bash
git add .gitignore MiniMaxUsage.sln src/MiniMaxUsage.App tests/MiniMaxUsage.App.Tests
git commit -m "build: scaffold WPF usage application"
```

否则记录 Task 1 的 build 成功输出并跳过提交。

---

### Task 2: 用测试锁定 PowerShell schema v2 与 general 模型选择

**Files:**
- Create: `tests/powershell/TestHarness.ps1`
- Create: `tests/powershell/Update-MinimaxUsage.Tests.ps1`
- Modify: `scripts/Update-MinimaxUsage.ps1:4-7, 59-74, 128-192, 194-243, 300-310`

- [ ] **Step 1: 创建无依赖测试运行器**

创建 `tests/powershell/TestHarness.ps1`：

```powershell
$script:Passed = 0
$script:Failed = 0

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) {
        throw "$Message`nExpected: $Expected`nActual:   $Actual"
    }
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function It {
    param([string]$Name, [scriptblock]$Body)
    try {
        & $Body
        $script:Passed++
        Write-Host "[PASS] $Name" -ForegroundColor Green
    } catch {
        $script:Failed++
        Write-Host "[FAIL] $Name`n$($_.Exception.Message)" -ForegroundColor Red
    }
}

function Complete-Tests {
    Write-Host "Passed=$script:Passed Failed=$script:Failed"
    if ($script:Failed -gt 0) { exit 1 }
    exit 0
}
```

- [ ] **Step 2: 写 schema 与模型顺序失败测试**

创建 `tests/powershell/Update-MinimaxUsage.Tests.ps1`，先包含：

```powershell
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\TestHarness.ps1"
. "$PSScriptRoot\..\..\scripts\Update-MinimaxUsage.ps1" -NoRun

function New-TestResponse {
    [pscustomobject]@{
        base_resp = [pscustomobject]@{ status_code = 0; status_msg = 'ok' }
        model_remains = @(
            [pscustomobject]@{
                model_name = 'video'
                current_interval_remaining_percent = 12
                remains_time = 3600000
                current_weekly_remaining_percent = 34
                weekly_remains_time = 7200000
            },
            [pscustomobject]@{
                model_name = 'general'
                current_interval_remaining_percent = 54
                remains_time = 3600000
                current_weekly_remaining_percent = 87
                weekly_remains_time = 7200000
            }
        )
    }
}

It 'builds schema v2 with stable provider model and window ids' {
    $cache = Build-CacheFromResponse -Response (New-TestResponse)
    Assert-Equal 2 $cache.schema_version 'schema_version must be 2'
    Assert-Equal 'minimax' $cache.provider_id 'provider_id mismatch'
    Assert-Equal 'video' $cache.items[0].model_id 'video model id missing'
    Assert-Equal '5h' $cache.items[0].window_id '5h window id missing'
    Assert-Equal 'general' $cache.items[2].model_id 'general model id missing'
    Assert-Equal 'weekly' $cache.items[3].window_id 'weekly window id missing'
}

It 'selects general model values regardless of API order' {
    $temp = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid())
    New-Item -ItemType Directory -Path $temp | Out-Null
    try {
        $script:cacheDir = $temp
        $cache = Build-CacheFromResponse -Response (New-TestResponse)
        Write-SimpleCache -Cache $cache
        Assert-Equal '54' (Get-Content "$temp\pct1.txt" -Raw) '5h value must use general'
        Assert-Equal '87' (Get-Content "$temp\pct2.txt" -Raw) 'weekly value must use general'
    } finally {
        Remove-Item $temp -Recurse -Force
    }
}

Complete-Tests
```

- [ ] **Step 3: 运行测试确认失败**

Run:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/powershell/Update-MinimaxUsage.Tests.ps1
```

Expected: FAIL，原因至少包含未知参数 `NoRun` 或缺少 `provider_id/model_id/window_id`。

- [ ] **Step 4: 给脚本增加可测试入口**

把参数改为：

```powershell
[CmdletBinding()]
param(
    [switch]$Loop,
    [switch]$NoRun
)
```

在主流程前加入：

```powershell
if ($NoRun) { return }
```

- [ ] **Step 5: 生成 schema v2 稳定字段**

在 `Build-CacheFromResponse` 的两个 item 中分别增加：

```powershell
model_id       = $rawName
window_id      = '5h'
```

和：

```powershell
model_id       = $rawName
window_id      = 'weekly'
```

返回对象改为：

```powershell
return [ordered]@{
    schema_version = 2
    provider_id    = 'minimax'
    status         = 'ok'
    plan           = $planName
    last_update    = (Get-Date).ToString('o')
    error          = $null
    items          = $items
}
```

同时把 `Write-Cache` 的默认 schema 从 `1` 改为 `2`。错误降级对象也必须保留同一契约，改为：

```powershell
$errorCache = [ordered]@{
    schema_version = 2
    provider_id    = 'minimax'
    status         = 'error'
    plan           = if ($existing) { $existing.plan } else { $planName }
    last_update    = if ($existing) { $existing.last_update } else { $null }
    error          = $_.Exception.Message
    items          = if ($existing) { @($existing.items) } else { @() }
}
```

这样一次采集失败不会把已经可读的 schema v2 降回无 `provider_id` 的错误缓存。

- [ ] **Step 6: 按稳定 ID 选中 general 的两个额度窗口**

在 `Write-SimpleCache` 中用下面代码取代 `items[0]`、`items[1]`：

```powershell
$item1 = @($Cache.items | Where-Object {
    $_.model_id -eq 'general' -and $_.window_id -eq '5h'
} | Select-Object -First 1)
$item2 = @($Cache.items | Where-Object {
    $_.model_id -eq 'general' -and $_.window_id -eq 'weekly'
} | Select-Object -First 1)

if ($item1.Count -gt 0) {
    $fiveHour = $item1[0]
    Write-AtomicText -Path $pct1Txt -Value "$($fiveHour.remaining_pct)"
    Write-AtomicText -Path $bar1Txt -Value "$([math]::Round($fiveHour.remaining_pct / 100.0, 4))"
    Write-AtomicText -Path $reset1Txt -Value $fiveHour.reset_text
}
if ($item2.Count -gt 0) {
    $weekly = $item2[0]
    Write-AtomicText -Path $pct2Txt -Value "$($weekly.remaining_pct)"
    Write-AtomicText -Path $bar2Txt -Value "$([math]::Round($weekly.remaining_pct / 100.0, 4))"
    Write-AtomicText -Path $reset2Txt -Value $weekly.reset_text
}
```

在 `Write-SimpleCache` 前增加通用原子文本函数：

```powershell
function Write-AtomicText {
    param([string]$Path, [AllowEmptyString()][string]$Value)
    $tmp = "$Path.tmp"
    [IO.File]::WriteAllText($tmp, $Value, [Text.UTF8Encoding]::new($false))
    Move-Item -Path $tmp -Destination $Path -Force
}
```

并在路径区定义 `$bar2Txt`。原有重复的临时文件写入代码全部改用 `Write-AtomicText`，不要保留两套实现。

- [ ] **Step 7: 运行 PowerShell 测试**

Run:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/powershell/Update-MinimaxUsage.Tests.ps1
```

Expected: `Passed=2 Failed=0`。

- [ ] **Step 8: 条件提交检查点**

```bash
git add scripts/Update-MinimaxUsage.ps1 tests/powershell
git commit -m "feat: add stable MiniMax usage schema"
```

无 Git 仓库时跳过提交。

---

### Task 3: TDD 添加额度历史、90 天清理和成功时间

**Files:**
- Modify: `tests/powershell/Update-MinimaxUsage.Tests.ps1`
- Modify: `scripts/Update-MinimaxUsage.ps1`

- [ ] **Step 1: 写快照判定失败测试**

在 `Complete-Tests` 前增加：

```powershell
It 'appends when values change and waits 15 minutes when unchanged' {
    $now = [DateTimeOffset]::Parse('2026-07-22T12:00:00+08:00')
    $current = [ordered]@{
        recorded_at = $now.ToString('o')
        windows = @(
            [ordered]@{ model_id='general'; window_id='5h'; remaining_pct=54; reset_at='2026-07-22T16:00:00Z' },
            [ordered]@{ model_id='general'; window_id='weekly'; remaining_pct=87; reset_at='2026-07-26T16:00:00Z' }
        )
    }
    $sameRecent = $current | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $sameRecent.recorded_at = $now.AddMinutes(-10).ToString('o')
    $sameOld = $current | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $sameOld.recorded_at = $now.AddMinutes(-16).ToString('o')
    $changed = $sameRecent | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $changed.windows[0].remaining_pct = 53

    Assert-True (-not (Test-ShouldAppendHistory -Last $sameRecent -Current $current -Now $now)) 'unchanged 10-minute sample must be skipped'
    Assert-True (Test-ShouldAppendHistory -Last $sameOld -Current $current -Now $now) '15-minute heartbeat must append'
    Assert-True (Test-ShouldAppendHistory -Last $changed -Current $current -Now $now) 'changed value must append'
}

It 'prunes snapshots older than 90 days and skips malformed lines' {
    $now = [DateTimeOffset]::Parse('2026-07-22T12:00:00+08:00')
    $lines = @(
        '{bad json',
        '{"recorded_at":"2026-04-01T00:00:00+08:00","windows":[]}',
        '{"recorded_at":"2026-07-01T00:00:00+08:00","windows":[]}'
    )
    $kept = @(Select-RetainedHistoryLines -Lines $lines -Now $now)
    Assert-Equal 1 $kept.Count 'only one valid recent line should remain'
    Assert-True ($kept[0] -like '*2026-07-01*') 'recent line must be retained'
}
```

- [ ] **Step 2: 运行测试确认失败**

Expected: FAIL，提示 `Test-ShouldAppendHistory` 或 `Select-RetainedHistoryLines` 未定义。

- [ ] **Step 3: 实现历史文档与比较函数**

增加：

```powershell
function New-HistorySnapshot {
    param($Cache, [DateTimeOffset]$Now = [DateTimeOffset]::Now)
    $windows = @($Cache.items | Where-Object { $_.model_id -eq 'general' } | ForEach-Object {
        [ordered]@{
            model_id     = $_.model_id
            window_id    = $_.window_id
            remaining_pct = [double]$_.remaining_pct
            reset_at     = $_.reset_at
        }
    })
    return [ordered]@{
        schema_version = 1
        provider_id    = 'minimax'
        recorded_at    = $Now.ToString('o')
        windows        = $windows
    }
}

function Get-HistoryWindowKey {
    param($Snapshot)
    return (@($Snapshot.windows | Sort-Object model_id, window_id | ForEach-Object {
        "$($_.model_id)|$($_.window_id)|$($_.remaining_pct)|$($_.reset_at)"
    }) -join ';')
}

function Test-ShouldAppendHistory {
    param($Last, $Current, [DateTimeOffset]$Now)
    if ($null -eq $Last) { return $true }
    if ((Get-HistoryWindowKey $Last) -ne (Get-HistoryWindowKey $Current)) { return $true }
    try {
        $lastAt = [DateTimeOffset]::Parse($Last.recorded_at)
        return (($Now - $lastAt).TotalMinutes -ge 15)
    } catch {
        return $true
    }
}
```

- [ ] **Step 4: 实现安全读取、追加和每日清理**

增加：

```powershell
function Get-LastHistorySnapshot {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return $null }
    $line = Get-Content -Path $Path -Tail 1 -Encoding UTF8
    if ([string]::IsNullOrWhiteSpace($line)) { return $null }
    try { return $line | ConvertFrom-Json } catch { return $null }
}

function Select-RetainedHistoryLines {
    param([string[]]$Lines, [DateTimeOffset]$Now)
    $cutoff = $Now.AddDays(-90)
    foreach ($line in $Lines) {
        try {
            $value = $line | ConvertFrom-Json
            if ([DateTimeOffset]::Parse($value.recorded_at) -ge $cutoff) { $line }
        } catch {
            continue
        }
    }
}

function Add-HistorySnapshot {
    param($Cache, [DateTimeOffset]$Now = [DateTimeOffset]::Now)
    $snapshot = New-HistorySnapshot -Cache $Cache -Now $Now
    $last = Get-LastHistorySnapshot -Path $historyFile
    if (-not (Test-ShouldAppendHistory -Last $last -Current $snapshot -Now $Now)) { return }
    $json = $snapshot | ConvertTo-Json -Depth 5 -Compress
    [IO.File]::AppendAllText($historyFile, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

function Invoke-HistoryPruneIfDue {
    param([DateTimeOffset]$Now = [DateTimeOffset]::Now)
    $today = $Now.ToString('yyyy-MM-dd')
    if ((Test-Path $historyPrunedDateFile) -and ((Get-Content $historyPrunedDateFile -Raw) -eq $today)) { return }
    if (Test-Path $historyFile) {
        $kept = @(Select-RetainedHistoryLines -Lines (Get-Content $historyFile -Encoding UTF8) -Now $Now)
        $tmp = "$historyFile.tmp"
        [IO.File]::WriteAllLines($tmp, $kept, [Text.UTF8Encoding]::new($false))
        Move-Item $tmp $historyFile -Force
    }
    Write-AtomicText -Path $historyPrunedDateFile -Value $today
}
```

路径区增加：

```powershell
$historyFile = Join-Path $cacheDir 'history.jsonl'
$historyPrunedDateFile = Join-Path $cacheDir 'history-pruned-date.txt'
$lastSuccessEpochFile = Join-Path $cacheDir 'last-success-epoch.txt'
$updatedTxt = Join-Path $cacheDir 'updated.txt'
$bar2Txt = Join-Path $cacheDir 'bar2.txt'
```

- [ ] **Step 5: 只在成功流程记录历史和成功时间**

在成功写缓存之后加入：

```powershell
$now = [DateTimeOffset]::Now
Add-HistorySnapshot -Cache $cache -Now $now
Invoke-HistoryPruneIfDue -Now $now
Write-AtomicText -Path $lastSuccessEpochFile -Value "$($now.ToUnixTimeSeconds())"
Write-AtomicText -Path $updatedTxt -Value "$($now.ToString('HH:mm')) 更新"
```

错误流程不得写 `last-success-epoch.txt`，但仍继续写 `status.txt=error` 和保留的百分比文本。

- [ ] **Step 6: 运行测试**

Expected: 所有 PowerShell 测试通过，至少 `Passed=4 Failed=0`。

- [ ] **Step 7: 用真实已配置 Key 执行一次非测试刷新**

Run:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Update-MinimaxUsage.ps1
```

Expected: exit code 0；`.cache/cache.json` 为 schema 2；`.cache/history.jsonl`、`bar2.txt`、`last-success-epoch.txt`、`updated.txt` 存在。不得把文件内容中的凭据打印到终端。

- [ ] **Step 8: 条件提交检查点**

```bash
git add scripts/Update-MinimaxUsage.ps1 tests/powershell/Update-MinimaxUsage.Tests.ps1
git commit -m "feat: record MiniMax quota history"
```

---

### Task 4: TDD 实现 WPF 当前缓存读取

**Files:**
- Create: `src/MiniMaxUsage.App/Models/UsageDocuments.cs`
- Create: `src/MiniMaxUsage.App/Models/UsageState.cs`
- Create: `src/MiniMaxUsage.App/Services/UsageCacheReader.cs`
- Create: `tests/MiniMaxUsage.App.Tests/TempDirectory.cs`
- Create: `tests/MiniMaxUsage.App.Tests/UsageCacheReaderTests.cs`

- [ ] **Step 1: 写当前缓存读取失败测试**

测试必须覆盖：schema 2 正常数据、video 在前仍选 general、错误状态保留旧值、超过 3 分钟标记过期、损坏 JSON 返回 fatal error。核心测试：

```csharp
[Fact]
public void ReadsGeneralWindowsAndMarksStaleAfterThreeMinutes()
{
    var now = DateTimeOffset.Parse("2026-07-22T12:05:01+08:00");
    var json = """
    {"schema_version":2,"provider_id":"minimax","status":"ok","plan":"MiniMax TokenPlan",
     "last_update":"2026-07-22T12:02:00+08:00","error":null,"items":[
       {"model_id":"video","window_id":"5h","name":"视频","remaining_pct":12,"reset_at":null,"reset_text":"N/A"},
       {"model_id":"general","window_id":"weekly","name":"文本周","remaining_pct":87,"reset_at":"2026-07-26T16:00:00Z","reset_text":"4天后"},
       {"model_id":"general","window_id":"5h","name":"文本5h","remaining_pct":54,"reset_at":"2026-07-22T16:00:00Z","reset_text":"2小时后"}]}
    """;
    using var temp = new TempDirectory();
    var path = temp.WriteFile("cache.json", json);

    var result = new UsageCacheReader().Read(path, now);

    Assert.True(result.IsSuccess);
    Assert.Equal(54, result.Value!.FiveHour.RemainingPercent);
    Assert.Equal(87, result.Value.Weekly.RemainingPercent);
    Assert.True(result.Value.IsStale);
}
```

同时创建 `TempDirectory.cs`：

```csharp
namespace MiniMaxUsage.App.Tests;

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "MiniMaxUsageTests", Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(Path);

    public string WriteFile(string relativePath, string content)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content, new System.Text.UTF8Encoding(false));
        return fullPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
```

- [ ] **Step 2: 运行单测确认失败**

Run:

```bash
'D:/claude_work/2026/07/05/minimax/.tools/dotnet/dotnet.exe' test tests/MiniMaxUsage.App.Tests --filter UsageCacheReaderTests
```

Expected: 编译失败，缺少 `UsageCacheReader` 和模型。

- [ ] **Step 3: 创建 JSON 文档模型**

`UsageDocuments.cs` 定义：

```csharp
using System.Text.Json.Serialization;

namespace MiniMaxUsage.App.Models;

public sealed class UsageCacheDocument
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; init; }
    [JsonPropertyName("provider_id")] public string ProviderId { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("plan")] public string Plan { get; init; } = "";
    [JsonPropertyName("last_update")] public DateTimeOffset? LastUpdate { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
    [JsonPropertyName("items")] public List<UsageWindowDocument> Items { get; init; } = [];
}

public sealed class UsageWindowDocument
{
    [JsonPropertyName("model_id")] public string ModelId { get; init; } = "";
    [JsonPropertyName("window_id")] public string WindowId { get; init; } = "";
    [JsonPropertyName("remaining_pct")] public double RemainingPercent { get; init; }
    [JsonPropertyName("reset_at")] public DateTimeOffset? ResetAt { get; init; }
    [JsonPropertyName("reset_text")] public string ResetText { get; init; } = "";
}
```

- [ ] **Step 4: 创建稳定领域模型**

`UsageState.cs`：

```csharp
namespace MiniMaxUsage.App.Models;

public sealed record QuotaWindow(string WindowId, double RemainingPercent, DateTimeOffset? ResetAt, string ResetText);

public sealed record CurrentUsage(
    string ProviderId,
    string Plan,
    DateTimeOffset LastUpdate,
    QuotaWindow FiveHour,
    QuotaWindow Weekly,
    bool IsStale,
    string? Error);

public sealed record UsageReadResult(CurrentUsage? Value, string? FatalError)
{
    public bool IsSuccess => Value is not null;
    public static UsageReadResult Success(CurrentUsage value) => new(value, null);
    public static UsageReadResult Failure(string error) => new(null, error);
}

public enum QuotaSeverity { Normal, Warning, Critical }

public static class QuotaSeverityRules
{
    public static QuotaSeverity FromPercent(double percent) => percent switch
    {
        <= 10 => QuotaSeverity.Critical,
        <= 20 => QuotaSeverity.Warning,
        _ => QuotaSeverity.Normal
    };
}
```

- [ ] **Step 5: 实现缓存读取与验证**

`UsageCacheReader.Read` 必须：捕获文件/JSON异常；要求 schema 2 和 provider `minimax`；按 `model_id=general + window_id` 选两个窗口；百分比 clamp 到 0–100；`status != ok` 时保留值并附带 error；`now-last_update > 3 分钟` 时 stale。

核心返回逻辑：

```csharp
var five = document.Items.FirstOrDefault(x => x.ModelId == "general" && x.WindowId == "5h");
var weekly = document.Items.FirstOrDefault(x => x.ModelId == "general" && x.WindowId == "weekly");
if (five is null || weekly is null || document.LastUpdate is null)
    return UsageReadResult.Failure("缓存缺少 general 模型的 5h/weekly 窗口或更新时间。");

QuotaWindow Convert(UsageWindowDocument item) => new(
    item.WindowId,
    Math.Clamp(item.RemainingPercent, 0, 100),
    item.ResetAt,
    item.ResetText);

return UsageReadResult.Success(new CurrentUsage(
    document.ProviderId,
    document.Plan,
    document.LastUpdate.Value,
    Convert(five),
    Convert(weekly),
    now - document.LastUpdate.Value > TimeSpan.FromMinutes(3),
    document.Status == "ok" ? null : document.Error ?? "最近一次采集失败。"));
```

- [ ] **Step 6: 运行测试**

Expected: `UsageCacheReaderTests` 全部 PASS。

- [ ] **Step 7: 条件提交检查点**

```bash
git add src/MiniMaxUsage.App/Models src/MiniMaxUsage.App/Services/UsageCacheReader.cs tests/MiniMaxUsage.App.Tests/UsageCacheReaderTests.cs
git commit -m "feat: read current MiniMax quota cache"
```

---

### Task 5: TDD 实现历史读取、时间范围与断线分段

**Files:**
- Modify: `src/MiniMaxUsage.App/Models/UsageDocuments.cs`
- Modify: `src/MiniMaxUsage.App/Models/UsageState.cs`
- Create: `src/MiniMaxUsage.App/Services/HistoryReader.cs`
- Create: `src/MiniMaxUsage.App/Services/TrendSeriesBuilder.cs`
- Create: `tests/MiniMaxUsage.App.Tests/HistoryReaderTests.cs`
- Create: `tests/MiniMaxUsage.App.Tests/TrendSeriesBuilderTests.cs`

- [ ] **Step 1: 写历史读取失败测试**

测试文件由四行组成：损坏 JSON、91 天前、有效 general、只有 video。断言只返回有效 recent general 样本，且不会因损坏行抛异常。

- [ ] **Step 2: 写趋势分段失败测试**

```csharp
[Fact]
public void StartsNewSegmentAfterFortyFiveMinuteGap()
{
    var now = DateTimeOffset.Parse("2026-07-22T12:00:00+08:00");
    var samples = new[]
    {
        new HistorySample(now.AddMinutes(-70), 80, 95),
        new HistorySample(now.AddMinutes(-60), 78, 95),
        new HistorySample(now.AddMinutes(-10), 70, 94)
    };

    var series = TrendSeriesBuilder.Build(samples, TrendRange.Hours24, now, x => x.FiveHourPercent);

    Assert.Equal(2, series.Count);
    Assert.Equal(2, series[0].Points.Count);
    Assert.Single(series[1].Points);
}
```

另写三个 cutoff 测试，分别验证 24 小时、7 天、30 天。

- [ ] **Step 3: 运行测试确认失败**

Expected: 缺少 `HistoryReader`、`HistorySample`、`TrendSeriesBuilder`。

- [ ] **Step 4: 增加历史文档和领域类型**

```csharp
public sealed class HistorySnapshotDocument
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; init; }
    [JsonPropertyName("provider_id")] public string ProviderId { get; init; } = "";
    [JsonPropertyName("recorded_at")] public DateTimeOffset RecordedAt { get; init; }
    [JsonPropertyName("windows")] public List<HistoryWindowDocument> Windows { get; init; } = [];
}

public sealed class HistoryWindowDocument
{
    [JsonPropertyName("model_id")] public string ModelId { get; init; } = "";
    [JsonPropertyName("window_id")] public string WindowId { get; init; } = "";
    [JsonPropertyName("remaining_pct")] public double RemainingPercent { get; init; }
}
```

领域类型：

```csharp
public sealed record HistorySample(DateTimeOffset RecordedAt, double FiveHourPercent, double WeeklyPercent);
public sealed record TrendPoint(DateTimeOffset RecordedAt, double Value);
public sealed record TrendSegment(IReadOnlyList<TrendPoint> Points);
public enum TrendRange { Hours24, Days7, Days30 }
```

- [ ] **Step 5: 实现流式 HistoryReader**

`Read(path, cutoff)` 使用 `File.ReadLines(path)`；每行独立 `try/catch JsonException`；只接受 schema 1、provider `minimax`、`recorded_at >= cutoff`；按 stable ID 选择 general 5h/weekly；缺一则跳过；结果按时间排序。

- [ ] **Step 6: 实现 TrendSeriesBuilder**

范围换算必须固定：24 小时、7 天、30 天。相邻样本间隔大于 45 分钟时开始新 segment。值统一 clamp 到 0–100。

```csharp
private static TimeSpan ToDuration(TrendRange range) => range switch
{
    TrendRange.Hours24 => TimeSpan.FromHours(24),
    TrendRange.Days7 => TimeSpan.FromDays(7),
    TrendRange.Days30 => TimeSpan.FromDays(30),
    _ => throw new ArgumentOutOfRangeException(nameof(range))
};
```

- [ ] **Step 7: 运行历史与趋势测试**

Run:

```bash
'D:/claude_work/2026/07/05/minimax/.tools/dotnet/dotnet.exe' test tests/MiniMaxUsage.App.Tests --filter 'FullyQualifiedName~HistoryReaderTests|FullyQualifiedName~TrendSeriesBuilderTests'
```

Expected: 全部 PASS。

- [ ] **Step 8: 条件提交检查点**

```bash
git add src/MiniMaxUsage.App/Models src/MiniMaxUsage.App/Services/HistoryReader.cs src/MiniMaxUsage.App/Services/TrendSeriesBuilder.cs tests/MiniMaxUsage.App.Tests
git commit -m "feat: build quota trend series"
```

---

### Task 6: TDD 实现项目定位、手动刷新与 ViewModel

**Files:**
- Create: `src/MiniMaxUsage.App/Services/ProjectLocator.cs`
- Create: `src/MiniMaxUsage.App/Services/PowerShellRefreshService.cs`
- Create: `src/MiniMaxUsage.App/ViewModels/MainViewModel.cs`
- Create: `tests/MiniMaxUsage.App.Tests/ProjectLocatorTests.cs`
- Create: `tests/MiniMaxUsage.App.Tests/PowerShellRefreshServiceTests.cs`
- Create: `tests/MiniMaxUsage.App.Tests/MainViewModelTests.cs`
- Modify: `src/MiniMaxUsage.App/App.xaml.cs`

- [ ] **Step 1: 写项目目录解析失败测试**

覆盖：`--project-dir` 优先；从 EXE 目录向上查找；找不到 `scripts/Update-MinimaxUsage.ps1` 时返回明确失败。路径验证不得要求真实 `.config` 存在。

- [ ] **Step 2: 写刷新命令失败测试**

```csharp
[Fact]
public void CreatesHiddenPowerShellCommandForExistingUpdater()
{
    using var temp = new TempDirectory();
    var root = temp.Path;
    temp.WriteFile("scripts/Update-MinimaxUsage.ps1", "# fixture");
    var info = PowerShellRefreshService.CreateStartInfo(root);

    Assert.Equal("powershell.exe", info.FileName);
    Assert.False(info.UseShellExecute);
    Assert.True(info.CreateNoWindow);
    Assert.Contains("-NoProfile", info.ArgumentList);
    Assert.Contains(Path.Combine(root, "scripts", "Update-MinimaxUsage.ps1"), info.ArgumentList);
}
```

- [ ] **Step 3: 运行测试确认失败**

Expected: 缺少两个 service。

- [ ] **Step 4: 实现 ProjectLocator**

公共签名：

```csharp
public sealed record ProjectLocationResult(string? ProjectDirectory, string? Error)
{
    public bool IsSuccess => ProjectDirectory is not null;
}

public static ProjectLocationResult Resolve(string[] args, string baseDirectory)
```

校验函数只检查：

```text
<root>/scripts/Update-MinimaxUsage.ps1
<root>/.cache/（可不存在，应用显示首次安装状态）
```

没有参数时，从 `baseDirectory` 最多向上查找 4 层。

- [ ] **Step 5: 实现 PowerShellRefreshService**

`CreateStartInfo` 使用 `ProcessStartInfo.ArgumentList`，不拼接未转义命令字符串。`RunAsync(root, timeout, cancellationToken)` 默认 50 秒；超时后 `Kill(entireProcessTree: true)`；返回：

```csharp
public sealed record RefreshResult(bool Success, string? Error);
```

退出码非 0 时返回 `刷新脚本退出码: N`，不把 stdout/stderr 中可能含环境信息直接展示到 UI；详细内容仍由现有 `widget.log` 记录。

- [ ] **Step 6: 实现 MainViewModel**

ViewModel 构造参数包含 project root、`UsageCacheReader`、`HistoryReader`、`PowerShellRefreshService` 和 `Func<DateTimeOffset>`。公开属性至少包括：

```text
Plan
FiveHourPercent / WeeklyPercent
FiveHourSeverity / WeeklySeverity
FiveHourResetText / WeeklyResetText
LastUpdatedText
StatusText / StatusSeverity
IsRefreshing
SelectedRange
FiveHourSegments / WeeklySegments
HasHistory
ErrorMessage
```

`Load()` 读取 `cache.json` 和 `history.jsonl`；`RefreshAsync()` 设置 `IsRefreshing`、调用 PowerShell、再调用 `Load()`；并发刷新直接返回；默认 `SelectedRange=Days7`。

- [ ] **Step 7: 修改 App 启动入口**

在 `App.xaml` 移除 `StartupUri`。`App.xaml.cs` 的 `OnStartup`：解析 project root；失败则使用 `MessageBox.Show` 后退出；成功则创建 ViewModel 与 MainWindow。

- [ ] **Step 8: 添加 ViewModel 最小测试并运行全部 .NET 测试**

至少验证默认 7 天、stale 状态文案、无历史空状态、重复 Refresh 被阻止，以及额度颜色边界：`21=Normal`、`20=Warning`、`10=Critical`。

Run:

```bash
'D:/claude_work/2026/07/05/minimax/.tools/dotnet/dotnet.exe' test MiniMaxUsage.sln
```

Expected: 全部 PASS。

- [ ] **Step 9: 条件提交检查点**

```bash
git add src/MiniMaxUsage.App/Services src/MiniMaxUsage.App/ViewModels src/MiniMaxUsage.App/App.xaml* tests/MiniMaxUsage.App.Tests
git commit -m "feat: orchestrate local quota detail data"
```

---

### Task 7: 实现 WPF 双圆环、趋势图和详情窗口

**Files:**
- Create: `src/MiniMaxUsage.App/Controls/CircularGauge.cs`
- Create: `src/MiniMaxUsage.App/Controls/TrendChart.cs`
- Create: `tests/MiniMaxUsage.App.Tests/CircularGaugeTests.cs`
- Modify: `src/MiniMaxUsage.App/MainWindow.xaml`
- Modify: `src/MiniMaxUsage.App/MainWindow.xaml.cs`

- [ ] **Step 1: 为圆环角度计算写失败测试**

把可测试的静态方法定义为 `public`，避免为单个公式增加额外的程序集可见性配置：

```csharp
[Theory]
[InlineData(-1, 0)]
[InlineData(54, 145.8)]
[InlineData(101, 270)]
public void GaugeSweepClampsToZeroAndHundred(double value, double expected)
{
    Assert.Equal(expected, CircularGauge.CalculateSweep(value), 3);
}
```

- [ ] **Step 2: 实现 CircularGauge**

继承 `FrameworkElement`，定义 `Value`、`AccentBrush`、`TrackBrush`、`Caption` dependency properties。`OnRender` 绘制 270° track 和 value arc，使用圆角 Pen；中心使用 `FormattedText` 显示 `54%` 和 caption。核心公式：

```csharp
public static double CalculateSweep(double value) => Math.Clamp(value, 0, 100) / 100d * 270d;

private static Point PointAt(Point center, double radius, double degrees)
{
    var radians = degrees * Math.PI / 180d;
    return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
}
```

绘制起始角为 135°，顺时针 270°；stroke 宽度取控件最短边的 9%。

- [ ] **Step 3: 实现 TrendChart**

继承 `FrameworkElement`，定义 `FiveHourSegments`、`WeeklySegments` dependency properties。`OnRender`：

- 画 0/25/50/75/100 五条水平网格；
- X 轴按当前 segment 的全局最小/最大时间映射；
- Y 轴固定 0–100；
- 每个 segment 单独创建 `StreamGeometry`，不跨 45 分钟缺口连线；
- 5h 使用 `#2583F7`，weekly 使用 `#8B5CF6`；
- 没有数据时绘制“开始记录后将显示趋势”。

- [ ] **Step 4: 写完整 MainWindow XAML**

窗口属性：

```xml
Title="MiniMax Usage"
Width="980" Height="680"
MinWidth="860" MinHeight="600"
WindowStyle="None"
AllowsTransparency="True"
Background="Transparent"
ResizeMode="CanResize"
```

根 Border 使用 `CornerRadius=18` 和浅色渐变。布局必须按已确认 mockup：

1. 自定义标题栏：三色关闭/最小化/最大化按钮；
2. 标题、套餐与刷新按钮；
3. 左侧两个 108px `CircularGauge`；
4. 右侧三个信息卡：5h 重置、周重置、最后更新；
5. 趋势卡：24 小时/7 天/30 天按钮、图例、`TrendChart`；
6. 底部数据口径说明；
7. 错误 Banner 和无历史提示。

两个圆环分别绑定 `FiveHourSeverity` 和 `WeeklySeverity`，用 XAML `DataTrigger` 在 `Warning` 时切换 `#F59E0B`、在 `Critical` 时切换 `#EF4444`；正常色分别为蓝色和紫色。状态 Banner 根据正常、过期、错误使用中性、橙色和红色。

不引入第三方 UI 或图表包。

- [ ] **Step 5: 实现窗口交互**

`MainWindow.xaml.cs`：

- 标题栏鼠标左键调用 `DragMove()`；
- 红/黄/绿按钮分别关闭、最小化、最大化/还原；
- Refresh 按钮 await `ViewModel.RefreshAsync()`；
- 三个范围按钮设置 `SelectedRange` 并重新生成 series；
- `DispatcherTimer` 每 15 秒调用 `Load()`；
- `Closed` 时停止 timer；窗口关闭后不创建托盘图标或后台线程。

- [ ] **Step 6: 运行单测和构建**

Run:

```bash
'D:/claude_work/2026/07/05/minimax/.tools/dotnet/dotnet.exe' test MiniMaxUsage.sln
'D:/claude_work/2026/07/05/minimax/.tools/dotnet/dotnet.exe' build MiniMaxUsage.sln -c Release
```

Expected: 测试通过，Release build 0 error。

- [ ] **Step 7: 启动真实窗口进行视觉检查**

Run:

```bash
'D:/claude_work/2026/07/05/minimax/.tools/dotnet/dotnet.exe' run --project src/MiniMaxUsage.App -- --project-dir 'D:/claude_work/2026/07/05/minimax'
```

Expected: 打开详情窗口；两个圆环显示当前真实值；默认 7 天；若历史不足显示明确空状态；关闭后 `MiniMaxUsage` 进程消失。

- [ ] **Step 8: 条件提交检查点**

```bash
git add src/MiniMaxUsage.App/Controls src/MiniMaxUsage.App/MainWindow.xaml* tests/MiniMaxUsage.App.Tests
git commit -m "feat: add quota detail dashboard"
```

---

### Task 8: TDD 更新 Rainmeter 双圆环和过期状态

**Files:**
- Create: `Skins/MiniMaxUsage/@Resources/Variables.inc`
- Create: `Skins/MiniMaxUsage/@Resources/Freshness.lua`
- Modify: `Skins/MiniMaxUsage/MiniMaxUsage.ini`
- Create: `tests/powershell/RainmeterSkin.Tests.ps1`

- [ ] **Step 1: 写皮肤静态失败测试**

创建 `tests/powershell/RainmeterSkin.Tests.ps1`：

```powershell
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\TestHarness.ps1"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$iniPath = Join-Path $root 'Skins\MiniMaxUsage\MiniMaxUsage.ini'
$luaPath = Join-Path $root 'Skins\MiniMaxUsage\@Resources\Freshness.lua'
$ini = Get-Content $iniPath -Raw -Encoding UTF8

It 'uses generated variables instead of a hardcoded project URL' {
    Assert-True ($ini -notmatch 'D:/claude_work') 'skin must not contain the development path'
    Assert-True ($ini -match '@Include=#@#Variables.inc') 'Variables.inc include missing'
}

It 'contains two complete quota rings' {
    foreach ($name in @('MeasureBar1','MeasureBar2','MeterRing1Track','MeterRing1','MeterRing2Track','MeterRing2')) {
        Assert-True ($ini -match "\[$name\]") "missing [$name]"
    }
    Assert-True (($ini | Select-String -Pattern 'Meter=Roundline' -AllMatches).Matches.Count -ge 4) 'four Roundline meters required'
}

It 'launches the detail executable with project-dir' {
    Assert-True ($ini -match 'LeftMouseUpAction=.*#DetailExe#.*--project-dir.*#ProjectDir#') 'detail launch action missing'
}

It 'has a freshness script that reads the success epoch' {
    Assert-True (Test-Path $luaPath) 'Freshness.lua missing'
    $lua = Get-Content $luaPath -Raw -Encoding UTF8
    Assert-True ($lua -match 'last-success-epoch\.txt') 'freshness epoch file missing'
}

Complete-Tests
```

该测试故意不读取真实 `.cache` 或 API Key。

- [ ] **Step 2: 运行测试确认失败**

Run:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/powershell/RainmeterSkin.Tests.ps1
```

Expected: FAIL，当前皮肤有硬编码路径且没有双圆环。

- [ ] **Step 3: 创建路径变量文件**

默认 `Variables.inc` 使用当前项目路径，但由安装脚本覆盖：

```ini
[Variables]
ProjectDir=D:\claude_work\2026\07\05\minimax
ProjectDirUri=D:/claude_work/2026/07/05/minimax
DetailExe=D:\claude_work\2026\07\05\minimax\dist\win-x64\MiniMaxUsage.exe
NormalColor=37,131,247,255
WeeklyColor=139,92,246,255
WarningColor=245,158,11,255
CriticalColor=239,68,68,255
TrackColor=90,113,136,36
```

- [ ] **Step 4: 创建 Freshness.lua**

脚本读取 `status.txt` 和 `last-success-epoch.txt`：

```lua
function Initialize()
  project = SKIN:GetVariable('ProjectDir')
end

local function read_first_line(path)
  local file = io.open(path, 'r')
  if not file then return nil end
  local value = file:read('*l')
  file:close()
  return value
end

function Update()
  local status = read_first_line(project .. '\\.cache\\status.txt')
  local epoch = tonumber(read_first_line(project .. '\\.cache\\last-success-epoch.txt'))
  if status == 'error' then return 2 end
  if not epoch or os.time() - epoch > 180 then return 1 end
  return 0
end

function GetStringValue()
  local value = Update()
  if value == 2 then return '● ERR' end
  if value == 1 then return '● 数据已过期' end
  return '● 当前稳定'
end
```

- [ ] **Step 5: 重写 MiniMaxUsage.ini 为 330×190 双圆环**

必须保留拖动、置顶设置，并使用：

- `MeasureBar1`、`MeasureBar2`，`MaxValue=1`；
- `MeasurePct1`、`MeasurePct2`；
- `MeasureReset1`、`MeasureReset2`；
- `MeasureFreshness` Lua Script；
- 两个固定满值背景 Roundline；
- 两个绑定 Bar measure 的前景 Roundline；
- 圆心百分比 String；
- 两组窗口名称和倒计时；
- 底部 `updated.txt` 和“查看详情 →”。

两个前景环分别使用 5h 蓝和 weekly 紫。Calc measures 根据百分比执行：`<=10` 红、`<=20` 橙、其他恢复默认色。启动动作：

```ini
LeftMouseUpAction=["#DetailExe#" "--project-dir" "#ProjectDir#"]
```

所有 file URL 使用 `file:///#ProjectDirUri#/.cache/...`。

- [ ] **Step 6: 运行皮肤静态测试**

Expected: `Passed`，无硬编码开发路径（变量文件中的默认路径不计入 INI 硬编码检查）。

- [ ] **Step 7: 在 Rainmeter 中刷新并人工验证**

Expected:

- 小组件尺寸约 330×190；
- 左 5h、右 weekly；
- 中央百分比与 `.cache/pct*.txt` 一致；
- 断开网络并等待失败刷新后显示 ERR 且圆环不归零；
- 临时把 `last-success-epoch.txt` 改成 3 分钟前，显示“数据已过期”；
- 恢复真实刷新后状态回到正常。

- [ ] **Step 8: 条件提交检查点**

```bash
git add Skins/MiniMaxUsage tests/powershell/RainmeterSkin.Tests.ps1
git commit -m "feat: redesign Rainmeter widget with quota rings"
```

---

### Task 9: 发布 EXE、生成路径变量并修复安装入口

**Files:**
- Create: `scripts/Publish-App.ps1`
- Modify: `scripts/Install-Task.ps1`
- Modify: `scripts/Install-Task-Admin.vbs`
- Modify: `scripts/一键注册任务计划.bat`
- Modify: `tests/powershell/Update-MinimaxUsage.Tests.ps1`

- [ ] **Step 1: 写变量文件生成失败测试**

给 `Install-Task.ps1` 增加 `-NoRun` 后，在 `Update-MinimaxUsage.Tests.ps1` 的 `Complete-Tests` 前加入：

```powershell
It 'writes Rainmeter variables for paths containing spaces and Chinese characters' {
    . "$PSScriptRoot\..\..\scripts\Install-Task.ps1" -NoRun
    $temp = Join-Path ([IO.Path]::GetTempPath()) ("MiniMax 测试 " + [guid]::NewGuid())
    $skin = Join-Path $temp 'Skin'
    New-Item -ItemType Directory -Path $skin -Force | Out-Null
    try {
        Write-RainmeterVariables -ProjectDir $temp -SkinDir $skin
        $content = Get-Content (Join-Path $skin '@Resources\Variables.inc') -Raw -Encoding UTF8
        Assert-True ($content.Contains("ProjectDir=$temp")) 'Windows project path missing'
        Assert-True ($content.Contains("ProjectDirUri=$($temp.Replace('\', '/'))")) 'URI path missing'
        Assert-True ($content.Contains("DetailExe=$(Join-Path $temp 'dist\win-x64\MiniMaxUsage.exe')")) 'EXE path missing'
    } finally {
        Remove-Item $temp -Recurse -Force
    }
}
```

断言内容必须保留中文与空格，不得靠手工加引号改变变量值。

- [ ] **Step 2: 创建可重复发布脚本**

`Publish-App.ps1`：

```powershell
[CmdletBinding()]
param([ValidateSet('win-x64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $projectDir '.tools\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { throw '缺少项目内 .NET SDK，请先执行实施计划 Task 1。' }
$output = Join-Path $projectDir "dist\$Runtime"
& $dotnet publish (Join-Path $projectDir 'src\MiniMaxUsage.App\MiniMaxUsage.App.csproj') `
    -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:PublishTrimmed=false `
    -o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败: $LASTEXITCODE" }
$exe = Join-Path $output 'MiniMaxUsage.exe'
if (-not (Test-Path $exe)) { throw "发布完成但未找到 $exe" }
Write-Host "[OK] $exe"
```

- [ ] **Step 3: 抽出 Write-RainmeterVariables**

在 `Install-Task.ps1` 增加 `-NoRun`，定义函数：

```powershell
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
}
```

安装流程调用该函数前，使用系统返回的“文档”目录复制皮肤，不能假定文档一定在 `C:\Users\<name>\Documents`：

```powershell
$documents = [Environment]::GetFolderPath('MyDocuments')
$skinSource = Join-Path $projectDir 'Skins\MiniMaxUsage'
$skinDir = Join-Path $documents 'Rainmeter\Skins\MiniMaxUsage'
New-Item -ItemType Directory -Path $skinDir -Force | Out-Null
Copy-Item (Join-Path $skinSource '*') $skinDir -Recurse -Force
Write-RainmeterVariables -ProjectDir $projectDir -SkinDir $skinDir
```

把当前脚本顶层的管理员权限检查和任务创建代码收进 `Invoke-InstallTask`，文件底部改为：

```powershell
if ($NoRun) { return }
Invoke-InstallTask -Uninstall:$Uninstall
```

这样测试以普通用户 dot-source `Install-Task.ps1 -NoRun` 时不会触发 UAC、管理员检查或任务计划修改；正常执行时仍由 `Invoke-InstallTask` 在开始处检查管理员权限。

- [ ] **Step 4: 移除管理员 VBS 的绝对路径**

用 `WScript.ScriptFullName` 计算 VBS 所在目录，再组合 `Install-Task.ps1`；`ShellExecute` 的 working directory 使用计算结果。不得保留 `D:\claude_work\...`。

- [ ] **Step 5: 更新一键脚本顺序**

批处理先运行 `Publish-App.ps1`，成功后再提升权限执行任务注册；任一步失败时显示 exit code 并停止，不继续报告成功。

- [ ] **Step 6: 运行 PowerShell 测试与发布**

Run:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/powershell/Update-MinimaxUsage.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/powershell/RainmeterSkin.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Publish-App.ps1
```

Expected: 两组测试通过；`dist/win-x64/MiniMaxUsage.exe` 存在。

- [ ] **Step 7: 在无 SDK PATH 的环境启动发布 EXE**

Run:

```bash
'D:/claude_work/2026/07/05/minimax/dist/win-x64/MiniMaxUsage.exe' --project-dir 'D:/claude_work/2026/07/05/minimax'
```

Expected: 应用启动；不依赖全局 `dotnet` 命令；关闭后进程退出。

- [ ] **Step 8: 条件提交检查点**

```bash
git add scripts/Publish-App.ps1 scripts/Install-Task.ps1 scripts/Install-Task-Admin.vbs scripts/一键注册任务计划.bat tests/powershell
git commit -m "build: publish and install usage dashboard"
```

`dist/` 保持忽略，不提交二进制。

---

### Task 10: 端到端验证与文档同步

**Files:**
- Modify: `README.md`
- Verify: `docs/superpowers/specs/2026-07-22-ccusage-style-design.md`

- [ ] **Step 1: 更新 README 功能与架构**

README 必须说明：

- 双圆环小组件；
- 点击打开 WPF 详情 EXE；
- 历史是本机“剩余额度快照”，不是精确 Token；
- 24h/7d/30d 趋势；
- 90 天保留；
- 项目内 .NET SDK 只用于构建，目标电脑无需 .NET；
- 新的发布与安装命令；
- `.cache/history.jsonl`、`last-success-epoch.txt`、`updated.txt`；
- ERR、数据过期、无历史三种排错状态。

- [ ] **Step 2: 运行所有自动化验证**

Run:

```bash
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/powershell/Update-MinimaxUsage.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/powershell/RainmeterSkin.Tests.ps1
'D:/claude_work/2026/07/05/minimax/.tools/dotnet/dotnet.exe' test MiniMaxUsage.sln -c Release
'D:/claude_work/2026/07/05/minimax/.tools/dotnet/dotnet.exe' build MiniMaxUsage.sln -c Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Publish-App.ps1
```

Expected: 所有命令 exit code 0，无失败测试；发布 EXE 存在。

- [ ] **Step 3: 验证正常真实数据路径**

1. 运行一次 `Update-MinimaxUsage.ps1`；
2. 确认两个 simple cache 值对应 cache 中 general/5h 与 general/weekly；
3. Rainmeter 两个圆环显示同样值；
4. 点击“查看详情”；
5. WPF 两个圆环显示同样值；
6. 关闭窗口并确认进程退出。

Expected: 三层显示一致，任何层都不读取或打印 API Key。

- [ ] **Step 4: 验证异常与降级路径**

使用测试副本而非覆盖真实 Key：

- 把 `cache.json` 临时替换为损坏 JSON，WPF 显示缓存不可用且不崩溃；
- 在 `history.jsonl` 中插入一行坏 JSON，趋势仍显示其他有效点；
- 把 `last-success-epoch.txt` 临时改为 4 分钟前，Rainmeter 与 WPF 显示过期；
- 让一次测试刷新返回错误，圆环保留最后值；
- 恢复备份并重新刷新。

Expected: 失败可见、数据不归零、恢复后状态正常。

- [ ] **Step 5: 验证视觉与窗口行为**

使用文本/浏览器快照之外的真实 Windows UI 检查：

- 小组件为两个并列圆环；
- 详情窗口为已确认的浅色玻璃卡片方向；
- 默认 7 天；
- 24h/7d/30d 切换有效；
- 窗口最小化、最大化、拖动、关闭有效；
- 无历史时不绘制虚假曲线；
- 100%、20%、10%、0% 的圆环不溢出。

- [ ] **Step 6: 对照成功标准逐项签收**

逐项核对设计规格第 11 节的 10 条成功标准。任何未满足项必须保持可见，不能把 Task 10 标记完成。

- [ ] **Step 7: 条件最终提交**

```bash
git add README.md .gitignore scripts Skins src tests MiniMaxUsage.sln docs/superpowers
git commit -m "feat: deliver MiniMax quota dashboard"
```

当前仍无 Git 仓库时跳过提交，并在交付说明中明确：实现和测试完成，但没有提交记录。
