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
$staleAfterSeconds = 180   # 超过 3 分钟无成功刷新视为"已过期"
$mutexName = 'Global\MiniMaxUsage.Refresh'  # 防止 WPF 手动刷新与任务计划并发跑同一脚本

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
    try {
        Add-Content -Path $logFile -Value $line -Encoding UTF8

        # P2-18 修复:日志轮转的 Get-Content/Set-Content 在文件被外部锁时抛
        # 用 try/catch 吃掉轮转失败,主写入仍保留
        if ((Test-Path $logFile) -and (Get-Item $logFile).Length -gt 1MB) {
            try {
                $tail = Get-Content $logFile -Tail 500 -Encoding UTF8
                Set-Content -Path $logFile -Value $tail -Encoding UTF8
            } catch {
                # 轮转失败不致命,下次重试
            }
        }
    } catch {
        # 写日志本身失败(磁盘满/权限)也不致命
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
# 注:ConvertTo-Json 把 PowerShell 的 $null 数组序列化为 "{}",但下游 ConvertFrom-Json
# 在 PowerShell 5.1 上会把 {} 反序列化为 $null,造成下游访问 .items 失败。
# 这里把 null 显式替换为 @(),序列化为 "[]",可正确反序列化为空数组。
function Write-Cache {
    param([hashtable]$Data)
    $tmp = "$cacheFile.tmp"
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
                # 注:remains_time 字段为「毫秒」,API 直接给的是窗口剩余的毫秒数
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
                # 注:weekly_remains_time 字段为「毫秒」
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

# === 原子写文本文件（不删旧文件,直接 rename 替换,避免 Rainmeter 读到缺失窗口） ===
function Write-AtomicText {
    param([string]$Path, [AllowEmptyString()][string]$Value)
    $tmp = "$Path.tmp"
    if (Test-Path $tmp) { Remove-Item $tmp -Force }
    [IO.File]::WriteAllText($tmp, $Value, [System.Text.UTF8Encoding]::new($false))
    # 不预先 Remove-Item,直接 Move-Item -Force 在同卷上是原子替换
    Move-Item -Path $tmp -Destination $Path -Force
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

    # 注:数字字段(pct/bar)必须用 InvariantCulture 格式化,避免 de-DE 等区域
    # 写出 "0,5" 把 Rainmeter Calc/数字解析撑坏
    $ci = [System.Globalization.CultureInfo]::InvariantCulture

    # 按稳定 ID 选中 general 模型的 5h 和 weekly 窗口
    $item1 = @($Cache.items | Where-Object {
        $_.model_id -eq 'general' -and $_.window_id -eq '5h'
    } | Select-Object -First 1)
    $item2 = @($Cache.items | Where-Object {
        $_.model_id -eq 'general' -and $_.window_id -eq 'weekly'
    } | Select-Object -First 1)

    if ($item1.Count -gt 0) {
        $fiveHour = $item1[0]
        Write-AtomicText -Path $pct1Txt -Value ($fiveHour.remaining_pct.ToString($ci))
        $bar1 = [math]::Round($fiveHour.remaining_pct / 100.0, 4)
        Write-AtomicText -Path $bar1Txt -Value ($bar1.ToString($ci))
        Write-AtomicText -Path $reset1Txt -Value $fiveHour.reset_text
    }
    if ($item2.Count -gt 0) {
        $weekly = $item2[0]
        Write-AtomicText -Path $pct2Txt -Value ($weekly.remaining_pct.ToString($ci))
        $bar2 = [math]::Round($weekly.remaining_pct / 100.0, 4)
        Write-AtomicText -Path $bar2Txt -Value ($bar2.ToString($ci))
        Write-AtomicText -Path $reset2Txt -Value $weekly.reset_text
    }
}

# === 写 status.txt 时考虑「新鲜/警告/过期/错误」四态（P2-19 修复）===
function Write-Status {
    param([string]$Status, [DateTimeOffset]$Now, [string]$LastSuccessEpoch)
    $display = switch ($Status) {
        'ok' {
            if ($LastSuccessEpoch) {
                $age = $Now.ToUnixTimeSeconds() - [long]$LastSuccessEpoch
                if ($age -gt $staleAfterSeconds) { '● 已过期' }
                else { '● 稳定' }
            } else {
                '● 稳定'
            }
        }
        'error' { '● 错误' }
        default { '● 未知' }
    }
    Write-AtomicText -Path (Join-Path $cacheDir 'status.txt') -Value $display
    Write-AtomicText -Path (Join-Path $cacheDir 'updated.txt') -Value "$($Now.ToString('HH:mm')) 更新"
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
    # P1-4 + N1 修复:用命名互斥体防止 WPF 手动刷新与任务计划并发跑同一脚本
    # 第 1 个参数 initiallyOwned=$true → 如果我们创建了互斥体,自动获得所有权 → ReleaseMutex() 不会抛
    $mutex = $null
    $createdNew = $false
    try {
        $mutex = New-Object System.Threading.Mutex($true, $mutexName, [ref]$createdNew)
    } catch {
        Write-Log 'WARN' "创建互斥体失败: $_ — 继续执行(无并发保护)"
        # 不 return,继续走流程 — 没并发保护总比脚本每次都挂强
    }
    if ($mutex -and -not $createdNew) {
        # 互斥体已存在(另一实例在跑)→ 我们没所有权,不能释放它
        $mutex.Dispose()
        Write-Log 'WARN' "另一个刷新实例正在运行,本次跳过（互斥体 $mutexName）"
        return $false
    }
    try {
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
            # status 包含四态:稳定/已过期/错误/未知（P2-19 修复）
            $lastEpoch = if (Test-Path $lastSuccessEpochFile) { Get-Content $lastSuccessEpochFile -Raw } else { '' }
            Write-Status -Status 'ok' -Now $now -LastSuccessEpoch $lastEpoch
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
                $now = [DateTimeOffset]::Now
                $lastEpoch = if (Test-Path $lastSuccessEpochFile) { Get-Content $lastSuccessEpochFile -Raw } else { '' }
                Write-Status -Status 'error' -Now $now -LastSuccessEpoch $lastEpoch
            } catch {
                # P2-18 修复:catch 内的 Write-Log 也可能抛(磁盘满/句柄耗尽),
                # 不能让它击穿到主 catch,否则会丢整个错误降级路径
                try { Write-Log 'ERROR' "写入错误缓存失败: $_" } catch { }
            }
            return $false
        }
    } finally {
        # N1 修复:仅当我们真的获得了所有权(创建了新互斥体)时才释放它
        # 如果互斥体已存在,我们不持有,ReleaseMutex() 会抛 ApplicationException
        if ($mutex -and $createdNew) {
            try { $mutex.ReleaseMutex() | Out-Null } catch { }
        }
        if ($mutex) {
            try { $mutex.Dispose() } catch { }
        }
    }
}

# === 主流程 ===
if ($NoRun) { return }

if ($Loop) {
    Write-Log 'INFO' "进入循环模式，每 $loopIntervalSec 秒刷新一次"
    $consecutiveFailures = 0
    $maxConsecutiveFailures = 10
    while ($true) {
        try {
            $null = Invoke-RefreshOnce
            $consecutiveFailures = 0
        } catch {
            $consecutiveFailures++
            # P2-7 修复:循环内的意外异常不能击穿 $ErrorActionPreference=Stop
            # 累积失败到阈值后退出(避免"看似在跑实则卡死"的状态)
            try { Write-Log 'ERROR' "循环内未捕获异常 #${consecutiveFailures}: $_" } catch { }
            if ($consecutiveFailures -ge $maxConsecutiveFailures) {
                try { Write-Log 'FATAL' "连续失败 ${maxConsecutiveFailures} 次,循环模式退出" } catch { }
                exit 1
            }
        }
        Start-Sleep -Seconds $loopIntervalSec
    }
} else {
    $ok = Invoke-RefreshOnce
    if ($ok) { exit 0 } else { exit 1 }
}
