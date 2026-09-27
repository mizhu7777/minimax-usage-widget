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
    $originalProjectDir = $projectDir
    try {
        # C8 修复:路径重定向走 Initialize-Paths 单点注入;S15 兜底:必须断言生效,
        # 覆盖未送达时立即失败,绝不静默写生产 .cache
        Initialize-Paths -ProjectDir $temp
        Assert-CacheDirIsolated -ExpectedCacheDir (Join-Path $temp '.cache')
        $cache = Build-CacheFromResponse -Response (New-TestResponse)
        Write-SimpleCache -Cache $cache
        Assert-Equal '54' (Get-Content "$temp\.cache\pct1.txt" -Raw) '5h value must use general'
        Assert-Equal '87' (Get-Content "$temp\.cache\pct2.txt" -Raw) 'weekly value must use general'
    } finally {
        Remove-Item $temp -Recurse -Force
        # S7 修复:恢复路径变量,消除测试间顺序耦合
        Initialize-Paths -ProjectDir $originalProjectDir
    }
}

It 'writes N/A markers when selected quota windows are missing' {
    # C5 修复回归:窗口缺失时显式写 N/A/0,不再残留旧值,冷启动文件也齐全
    $temp = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid())
    New-Item -ItemType Directory -Path $temp | Out-Null
    $originalProjectDir = $projectDir
    try {
        Initialize-Paths -ProjectDir $temp
        Assert-CacheDirIsolated -ExpectedCacheDir (Join-Path $temp '.cache')
        $cache = [ordered]@{
            schema_version = 2; provider_id = 'minimax'; status = 'ok'
            plan = 'MiniMax'; last_update = '2026-09-27T00:00:00.0000000+08:00'
            error = $null; items = @()
        }
        Write-SimpleCache -Cache $cache
        Assert-Equal 'N/A' (Get-Content "$temp\.cache\pct1.txt" -Raw) 'missing 5h must write N/A'
        Assert-Equal '0' (Get-Content "$temp\.cache\bar1.txt" -Raw) 'missing 5h ring must be empty'
        Assert-Equal 'N/A' (Get-Content "$temp\.cache\pct2.txt" -Raw) 'missing weekly must write N/A'
        Assert-Equal 'N/A' (Get-Content "$temp\.cache\reset2.txt" -Raw) 'missing weekly reset must be N/A'
    } finally {
        Remove-Item $temp -Recurse -Force
        Initialize-Paths -ProjectDir $originalProjectDir
    }
}

It 'sanitizes null elements out of items before writing cache' {
    # C7 修复回归:降级路径 @($existing.items) 在 existing.items 为 null 时
    # 产出含一个 null 元素的数组,Write-Cache 必须过滤,否则序列化为 "items":[null]
    $temp = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid())
    New-Item -ItemType Directory -Path $temp | Out-Null
    $originalProjectDir = $projectDir
    try {
        Initialize-Paths -ProjectDir $temp
        Assert-CacheDirIsolated -ExpectedCacheDir (Join-Path $temp '.cache')
        $existingItems = $null
        Write-Cache -Data @{
            schema_version = 2; provider_id = 'minimax'; status = 'error'
            plan = 'P'; last_update = $null; error = 'e'
            items = @($existingItems)
        }
        $parsed = Get-Content "$temp\.cache\cache.json" -Raw -Encoding UTF8 | ConvertFrom-Json
        Assert-Equal 0 @($parsed.items).Count 'items must serialize as [] not [null]'
    } finally {
        Remove-Item $temp -Recurse -Force
        Initialize-Paths -ProjectDir $originalProjectDir
    }
}

It 'heartbeats every 15 minutes, throttles changes to 2 minutes, ignores reset_at jitter' {
    $now = [DateTimeOffset]::Parse('2026-07-22T12:00:00+08:00').ToUniversalTime()
    $base = $now.ToString('o')
    # S1 修复回归:生产环境 reset_at 每次由本地时钟+remains_time 现算、带亚秒抖动,
    # last 与 current 的 reset_at 必须故意不同 —— 验证比较键已不再包含它
    $winCurrent = @(
        [pscustomobject]@{ model_id='general'; window_id='5h'; remaining_pct=54; reset_at='2026-07-22T16:00:00.1111111Z' },
        [pscustomobject]@{ model_id='general'; window_id='weekly'; remaining_pct=87; reset_at='2026-07-26T16:00:00.2222222Z' }
    )
    $winLast = @(
        [pscustomobject]@{ model_id='general'; window_id='5h'; remaining_pct=54; reset_at='2026-07-22T16:00:00.9999999Z' },
        [pscustomobject]@{ model_id='general'; window_id='weekly'; remaining_pct=87; reset_at='2026-07-26T16:00:00.8888888Z' }
    )
    $current = [pscustomobject]@{ recorded_at = $base; windows = $winCurrent }
    $sameRecent = [pscustomobject]@{ recorded_at = $now.AddMinutes(-10).ToString('o'); windows = $winLast }
    $sameOld    = [pscustomobject]@{ recorded_at = $now.AddMinutes(-16).ToString('o'); windows = $winLast }
    # P1-4 修复(agy §7.2 仲裁):变化节流阈值 10→2 分钟 —— 10 分钟窗口内"跌下去又重置回原值"
    # 的波动因键相同会被整体抹掉;2 分钟保证短时消耗与重置跳变都能落盘
    $changedNear    = [pscustomobject]@{ recorded_at = $now.AddMinutes(-3).ToString('o'); windows = @(
        [pscustomobject]@{ model_id='general'; window_id='5h'; remaining_pct=53; reset_at='2026-07-22T16:00:00.9999999Z' },
        [pscustomobject]@{ model_id='general'; window_id='weekly'; remaining_pct=87; reset_at='2026-07-26T16:00:00.8888888Z' }
    )}
    $changedInstant = [pscustomobject]@{ recorded_at = $now.AddMinutes(-1).ToString('o'); windows = @(
        [pscustomobject]@{ model_id='general'; window_id='5h'; remaining_pct=53; reset_at='2026-07-22T16:00:00.9999999Z' },
        [pscustomobject]@{ model_id='general'; window_id='weekly'; remaining_pct=87; reset_at='2026-07-26T16:00:00.8888888Z' }
    )}

    Assert-True (-not (Test-ShouldAppendHistory -Last $sameRecent -Current $current -Now $now)) 'unchanged values with reset_at jitter must be skipped within 15 minutes'
    Assert-True (Test-ShouldAppendHistory -Last $sameOld -Current $current -Now $now) '15-minute heartbeat must append'
    Assert-True (Test-ShouldAppendHistory -Last $changedNear -Current $current -Now $now) 'changed value after 2 minutes must append'
    Assert-True (-not (Test-ShouldAppendHistory -Last $changedInstant -Current $current -Now $now)) 'changed value within 2 minutes must be throttled'
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

It 'formats reset countdown cleanly avoiding 0分钟后 in final minute' {
    Assert-Equal 'N/A' (Format-ResetText -resetAt '') 'empty reset_at must return N/A'
    $past = [DateTimeOffset]::Now.AddSeconds(-10).ToString('o')
    Assert-Equal '即将重置' (Format-ResetText -resetAt $past) 'expired reset_at must return 即将重置'

    $soon = [DateTimeOffset]::Now.AddSeconds(30).ToString('o')
    Assert-Equal '即将重置' (Format-ResetText -resetAt $soon) 'under 1 minute must return 即将重置'

    $mins = [DateTimeOffset]::Now.AddMinutes(5).AddSeconds(10).ToString('o')
    Assert-Equal '5分钟后' (Format-ResetText -resetAt $mins) '5 minutes countdown must match'

    $hours = [DateTimeOffset]::Now.AddHours(2).AddMinutes(15).AddSeconds(20).ToString('o')
    Assert-Equal '2小时15分后' (Format-ResetText -resetAt $hours) 'hours and minutes countdown must match'
}

It 'writes Rainmeter variables for paths containing spaces and Chinese characters' {
    . "$PSScriptRoot\..\..\scripts\Install-Task.ps1" -NoRun
    $temp = Join-Path ([IO.Path]::GetTempPath()) ("MiniMax 测试 " + [guid]::NewGuid())
    $skin = Join-Path $temp 'Skin'
    New-Item -ItemType Directory -Path $skin -Force | Out-Null
    try {
        Write-RainmeterVariables -ProjectDir $temp -SkinDir $skin
        $content = Get-Content (Join-Path $skin '@Resources\Variables.inc') -Raw -Encoding UTF8
        Assert-True ($content.Contains("ProjectDir=$temp")) 'Windows project path missing'
        Assert-True ($content.Contains("ProjectDirUri=$($temp.Replace('\', '/').Replace(' ', '%20'))")) 'URI path missing (G16: space must be encoded)'
        Assert-True ($content.Contains("DetailExe=$(Join-Path $temp 'dist\win-x64\MiniMaxUsage.exe')")) 'EXE path missing'
    } finally {
        Remove-Item $temp -Recurse -Force
    }
}

It 'isolation assertion fails loudly when cache dir points elsewhere' {
    # S15 兜底回归:断言不过必须抛异常,把"覆盖失效"从静默污染变成显式错误
    $threw = $false
    try {
        Assert-CacheDirIsolated -ExpectedCacheDir (Join-Path ([IO.Path]::GetTempPath()) ('not-isolated-' + [guid]::NewGuid()))
    } catch {
        $threw = $true
    }
    Assert-True $threw 'Assert-CacheDirIsolated must fail loudly on mismatch'
}

Complete-Tests