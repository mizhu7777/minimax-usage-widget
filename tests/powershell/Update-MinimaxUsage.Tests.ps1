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

It 'appends when values change and waits 15 minutes when unchanged' {
    $now = [DateTimeOffset]::Parse('2026-07-22T12:00:00+08:00').ToUniversalTime()
    $base = $now.ToString('o')
    $win = @(
        [pscustomobject]@{ model_id='general'; window_id='5h'; remaining_pct=54; reset_at='2026-07-22T16:00:00Z' },
        [pscustomobject]@{ model_id='general'; window_id='weekly'; remaining_pct=87; reset_at='2026-07-26T16:00:00Z' }
    )
    $current = [pscustomobject]@{ recorded_at = $base; windows = $win }
    $sameRecent = [pscustomobject]@{ recorded_at = $now.AddMinutes(-10).ToString('o'); windows = $win }
    $sameOld    = [pscustomobject]@{ recorded_at = $now.AddMinutes(-16).ToString('o'); windows = $win }
    $changed    = [pscustomobject]@{ recorded_at = $now.AddMinutes(-5).ToString('o'); windows = @(
        [pscustomobject]@{ model_id='general'; window_id='5h'; remaining_pct=53; reset_at='2026-07-22T16:00:00Z' },
        [pscustomobject]@{ model_id='general'; window_id='weekly'; remaining_pct=87; reset_at='2026-07-26T16:00:00Z' }
    )}

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

Complete-Tests