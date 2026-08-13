# Update-MinimaxUsage.ps1
# 后台查询脚本：每 1 分钟调用 MiniMax API，解密凭据，写 JSON 缓存
# 由 Windows 任务计划程序定时触发（每 1 分钟执行一次，脚本单次运行即退出）

[CmdletBinding()]
param(
    [switch]$Loop,
    [switch]$NoRun
)

$ErrorActionPreference = 'Stop'

# === 配置 ===
$apiUrl = 'https://www.minimaxi.com/v1/token_plan/remains'
$planName = 'MiniMax'
$timeoutSec = 10
$maxRetries = 3
$retryDelaySec = 5
$loopIntervalSec = 30   # 循环模式下每次调用的间隔

# === 路径解析（全部留在项目目录下） ===
$scriptDir  = $PSScriptRoot
$projectDir = Split-Path -Parent $scriptDir
$configDir  = Join-Path $projectDir '.config'
$cacheDir   = Join-Path $projectDir '.cache'
$apiKeyEnc  = Join-Path $configDir 'apikey.enc'
$cacheFile  = Join-Path $cacheDir 'cache.json'
$logFile    = Join-Path $cacheDir 'widget.log'
$historyFile = Join-Path $cacheDir 'history.jsonl'
$historyPrunedDateFile = Join-Path $cacheDir 'history-pruned-date.txt'
$lastSuccessEpochFile = Join-Path $cacheDir 'last-success-epoch.txt'
$updatedTxt = Join-Path $cacheDir 'updated.txt'

# 确保缓存目录存在
if (-not (Test-Path $cacheDir)) {
    New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
}

# === 日志函数 ===
function Write-Log {
    param([string]$Level, [string]$Message)
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $line = "[$timestamp] [$Level] $Message"
    Add-Content -Path $logFile -Value $line -Encoding UTF8

    # 日志轮转：超过 1MB 时截断保留最后 500 行
    if ((Test-Path $logFile) -and (Get-Item $logFile).Length -gt 1MB) {
        $tail = Get-Content $logFile -Tail 500 -Encoding UTF8
        Set-Content -Path $logFile -Value $tail -Encoding UTF8
    }
}

# === 读取已有缓存（用于错误降级） ===
function Get-LastCache {
    if (Test-Path $cacheFile) {
        try {
            return Get-Content $cacheFile -Raw -Encoding UTF8 | ConvertFrom-Json
        } catch {
            return $null
        }
    }
    return $null
}

# === 写入缓存（原子写：先写 .tmp 再 rename，避免读取时读到半截） ===
function Write-Cache {
    param([hashtable]$Data)
    $tmp = "$cacheFile.tmp"
    # 修复 PowerShell 空数组序列化为 {} 的问题
    if ($Data.ContainsKey('items') -and $null -eq $Data['items']) {
        $Data['items'] = @()
    }
    # 默认注入 schema_version，便于皮肤/工具检测字段兼容性
    if (-not $Data.ContainsKey('schema_version')) {
        $Data['schema_version'] = 2
    }
    $json = $Data | ConvertTo-Json -Depth 5 -Compress
    [System.IO.File]::WriteAllText($tmp, $json, [System.Text.UTF8Encoding]::new($false))
    Move-Item -Path $tmp -Destination $cacheFile -Force
}

# === 计算人类可读的重置时间（精确到分钟） ===
function Format-ResetText {
    param([string]$resetAt)
    if (-not $resetAt) { return 'N/A' }
    try {
        $diff = [DateTimeOffset]::Parse($resetAt).ToLocalTime() - (Get-Date)
        if ($diff.TotalSeconds -le 0) { return '即将重置' }
        # 先取整（向下取整）再除，避免 [int] cast 的四舍五入问题
        $totalMinutes = [int][math]::Floor($diff.TotalMinutes)
        if ($totalMinutes -lt 60) { return "${totalMinutes}分钟后" }
        $hours = [math]::Floor($totalMinutes / 60)
        $mins = $totalMinutes % 60
        if ($hours -lt 24) {
            if ($mins -eq 0) { return "${hours}小时后" }
            return "${hours}小时${mins}分后"
        }
        $days = [math]::Floor($hours / 24)
        $remHours = $hours % 24
        if ($remHours -eq 0) { return "${days}天后" }
        return "${days}天${remHours}小时后"
    } catch {
        return 'N/A'
    }
}

# === DPAPI 解密 API Key ===
function Get-DecryptedKey {
    if (-not (Test-Path $apiKeyEnc)) {
        throw "未找到加密的 API Key: $apiKeyEnc。请先运行 Set-MinimaxApiKey.ps1"
    }
    Add-Type -AssemblyName System.Security
    $encBytes = [System.IO.File]::ReadAllBytes($apiKeyEnc)
    $plainBytes = [System.Security.Cryptography.ProtectedData]::Unprotect(
        $encBytes, $null,
        [System.Security.Cryptography.DataProtectionScope]::CurrentUser
    )
    return [System.Text.Encoding]::UTF8.GetString($plainBytes)
}

# === 调用 MiniMax API ===
function Invoke-MiniMaxApi {
    param([string]$ApiKey)

    # 重要：此接口使用 GET 而非 POST
    $response = Invoke-RestMethod -Uri $apiUrl -Method Get `
        -Headers @{Authorization = "Bearer $ApiKey"} `
        -TimeoutSec $timeoutSec `
        -ErrorAction Stop

    return $response
}

# === 解析响应并构造缓存数据 ===
function Build-CacheFromResponse {
    param($Response)

    # 检查业务状态码
    $statusCode = $Response.base_resp.status_code
    if ($statusCode -ne 0) {
        $statusMsg = $Response.base_resp.status_msg
        if ($statusCode -eq 2049) {
            throw "API Key 无效（status_code=2049）"
        }
        throw "API 错误: $statusMsg ($statusCode)"
    }

    $models = $Response.model_remains
    if (-not $models -or $models.Count -eq 0) {
        throw "响应中没有 model_remains 数据"
    }

    $items = @()
    foreach ($m in $models) {
        $rawName = $m.model_name
        $displayName = switch ($rawName) {
            'general' { '文本' }
            'video'   { '视频' }
            default   { $rawName }
        }

        # 区间剩余（5小时窗口）
        if ($null -ne $m.current_interval_remaining_percent) {
            $resetAt = $null
            if ($m.remains_time -gt 0) {
                $resetAt = (Get-Date).ToUniversalTime().AddMilliseconds([double]$m.remains_time).ToString('o')
            }
            $items += [ordered]@{
                name            = "$displayName (5小时)"
                model_id        = $rawName
                window_id       = '5h'
                remaining_pct   = [double]$m.current_interval_remaining_percent
                reset_at        = $resetAt
                reset_text      = Format-ResetText $resetAt
            }
        }

        # 周剩余
        if ($null -ne $m.current_weekly_remaining_percent) {
            $resetAt = $null
            if ($m.weekly_remains_time -gt 0) {
                $resetAt = (Get-Date).ToUniversalTime().AddMilliseconds([double]$m.weekly_remains_time).ToString('o')
            }
            $items += [ordered]@{
                name            = "$displayName (周)"
                model_id        = $rawName
                window_id       = 'weekly'
                remaining_pct   = [double]$m.current_weekly_remaining_percent
                reset_at        = $resetAt
                reset_text      = Format-ResetText $resetAt
            }
        }
    }

    return [ordered]@{
        schema_version = 2
        provider_id    = 'minimax'
        status         = 'ok'
        plan           = $planName
        last_update    = (Get-Date).ToString('o')
        error          = $null
        items          = $items
    }
}

# === 原子写文本文件 ===
function Write-AtomicText {
    param([string]$Path, [AllowEmptyString()][string]$Value)
    $tmp = "$Path.tmp"
    if (Test-Path $tmp) { Remove-Item $tmp -Force }
    [IO.File]::WriteAllText($tmp, $Value, [System.Text.UTF8Encoding]::new($false))
    if (Test-Path $Path) { Remove-Item $Path -Force }
    Move-Item -Path $tmp -Destination $Path
}

# === 同时写多个简单文本文件（供 Rainmeter 读取）===
# 因为 Rainmeter WebParser RegExp 对空格和特殊字符处理有 bug，
# 改为每个字段写一个 .txt 文件，皮肤用 RegExp=.* 直接读取
# 按稳定 ID 选中 general 模型的两个额度窗口
function Write-SimpleCache {
    param($Cache)
    $planTxt   = Join-Path $cacheDir 'plan.txt'
    $statusTxt = Join-Path $cacheDir 'status.txt'
    $pct1Txt   = Join-Path $cacheDir 'pct1.txt'
    $reset1Txt = Join-Path $cacheDir 'reset1.txt'
    $pct2Txt   = Join-Path $cacheDir 'pct2.txt'
    $reset2Txt = Join-Path $cacheDir 'reset2.txt'
    $bar1Txt   = Join-Path $cacheDir 'bar1.txt'
    $bar2Txt   = Join-Path $cacheDir 'bar2.txt'
    $statusDisplayTxt = Join-Path $cacheDir 'status.txt'
    $detailTxt = Join-Path $cacheDir 'detail.txt'

    Write-AtomicText -Path $planTxt -Value $Cache.plan
    if ($Cache.status -eq 'ok') {
        Write-AtomicText -Path $statusDisplayTxt -Value "● 稳定"
    } else {
        Write-AtomicText -Path $statusDisplayTxt -Value "● ERR"
    }
    Write-AtomicText -Path $detailTxt -Value "查看详情 →"

    # 按稳定 ID 选中 general 模型的 5h 和 weekly 窗口
    $item1 = @($Cache.items | Where-Object {
        $_.model_id -eq 'general' -and $_.window_id -eq '5h'
    } | Select-Object -First 1)
    $item2 = @($Cache.items | Where-Object {
        $_.model_id -eq 'general' -and $_.window_id -eq 'weekly'
    } | Select-Object -First 1)

    if ($item1.Count -gt 0) {
        $fiveHour = $item1[0]
        Write-AtomicText -Path $pct1Txt -Value "$($fiveHour.remaining_pct)"
        $bar1 = [math]::Round($fiveHour.remaining_pct / 100.0, 4)
        Write-AtomicText -Path $bar1Txt -Value "$bar1"
        Write-AtomicText -Path $reset1Txt -Value $fiveHour.reset_text
    }
    if ($item2.Count -gt 0) {
        $weekly = $item2[0]
        Write-AtomicText -Path $pct2Txt -Value "$($weekly.remaining_pct)"
        $bar2 = [math]::Round($weekly.remaining_pct / 100.0, 4)
        Write-AtomicText -Path $bar2Txt -Value "$bar2"
        Write-AtomicText -Path $reset2Txt -Value $weekly.reset_text
    }
}

# === 历史快照函数 ===
function New-HistorySnapshot {
    param($Cache, [DateTimeOffset]$Now = [DateTimeOffset]::Now)
    $windows = @($Cache.items | Where-Object { $_.model_id -eq 'general' } | ForEach-Object {
        [ordered]@{
            model_id      = $_.model_id
            window_id     = $_.window_id
            remaining_pct = [double]$_.remaining_pct
            reset_at      = $_.reset_at
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
    $result = @()
    foreach ($line in $Lines) {
        try {
            $value = $line | ConvertFrom-Json
            if ([DateTimeOffset]::Parse($value.recorded_at) -ge $cutoff) { $result += $line }
        } catch {
            continue
        }
    }
    return $result
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

# === 单次刷新流程 ===
function Invoke-RefreshOnce {
    try {
        $apiKey = Get-DecryptedKey

        $response = $null
        $lastError = $null
        for ($i = 1; $i -le $maxRetries; $i++) {
            try {
                $response = Invoke-MiniMaxApi -ApiKey $apiKey
                break
            } catch {
                $lastError = $_
                Write-Log 'WARN' "第 $i/$maxRetries 次调用失败: $_"
                if ($i -lt $maxRetries) {
                    Start-Sleep -Seconds $retryDelaySec
                }
            }
        }

        if (-not $response) {
            throw "API 调用失败（已重试 $maxRetries 次）: $lastError"
        }

        $cache = Build-CacheFromResponse -Response $response
        Write-Cache -Data $cache
        Write-SimpleCache -Cache $cache
        $now = [DateTimeOffset]::Now
        Add-HistorySnapshot -Cache $cache -Now $now
        Invoke-HistoryPruneIfDue -Now $now
        Write-AtomicText -Path $lastSuccessEpochFile -Value "$($now.ToUnixTimeSeconds())"
        Write-AtomicText -Path $updatedTxt -Value "$($now.ToString('HH:mm')) 更新"
        Write-Log 'INFO' "刷新成功：套餐=$planName, 条目数=$($cache.items.Count)"

        # 清理解密后的 key
        $apiKey = $null
        return $true

    } catch {
        # 失败：写入错误状态（保留上次数据供皮肤降级显示）
        Write-Log 'ERROR' $_.Exception.Message

        $existing = Get-LastCache
        $errorCache = [ordered]@{
            schema_version = 2
            provider_id    = 'minimax'
            status         = 'error'
            plan           = if ($existing) { $existing.plan } else { $planName }
            last_update    = if ($existing) { $existing.last_update } else { $null }
            error          = $_.Exception.Message
            items          = if ($existing) { @($existing.items) } else { @() }
        }
        try {
            Write-Cache -Data $errorCache
            Write-SimpleCache -Cache $errorCache
        } catch {
            Write-Log 'ERROR' "写入错误缓存失败: $_"
        }
        return $false
    }
}

# === 主流程 ===
if ($NoRun) { return }

if ($Loop) {
    Write-Log 'INFO' "进入循环模式，每 $loopIntervalSec 秒刷新一次"
    while ($true) {
        $null = Invoke-RefreshOnce
        Start-Sleep -Seconds $loopIntervalSec
    }
} else {
    $ok = Invoke-RefreshOnce
    if ($ok) { exit 0 } else { exit 1 }
}
